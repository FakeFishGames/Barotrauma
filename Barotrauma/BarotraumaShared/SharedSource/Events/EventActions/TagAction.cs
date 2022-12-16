using Barotrauma.Extensions;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    class TagAction : EventAction
    {
        public enum SubType { Any = 0, Player = 1, Outpost = 2, Wreck = 4, BeaconStation = 8 }

        [Serialize("", IsPropertySaveable.Yes)]
        public string Criteria { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier Tag { get; set; }

        [Serialize(SubType.Any, IsPropertySaveable.Yes)]
        public SubType SubmarineType { get; set; }

        [Serialize(true, IsPropertySaveable.Yes)]
        public bool IgnoreIncapacitatedCharacters { get; set; }

        private bool isFinished = false;

        public TagAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            Taggers = new (string k, Action<Identifier> v)[]
            {
                ("players", v => TagPlayers()),
                ("player", v => TagPlayers()),
                ("bot", v => TagBots(playerCrewOnly: false)),
                ("crew", v => TagCrew()),
                ("humanprefabidentifier", TagHumansByIdentifier),
                ("jobidentifier", TagHumansByJobIdentifier),
                ("structureidentifier", TagStructuresByIdentifier),
                ("structurespecialtag", TagStructuresBySpecialTag),
                ("itemidentifier", TagItemsByIdentifier),
                ("itemtag", TagItemsByTag),
                ("hullname", TagHullsByName),
                ("submarine", TagSubmarinesByType),
            }.Select(t => (t.k.ToIdentifier(), t.v)).ToImmutableDictionary();
        }

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }
        public override void Reset()
        {
            isFinished = false;
        }

        private void TagPlayers()
        {
            if (IgnoreIncapacitatedCharacters)
            {
                ParentEvent.AddTargetPredicate(Tag, e => e is Character c && c.IsPlayer && !c.IsIncapacitated);
            }
            else
            {
                ParentEvent.AddTargetPredicate(Tag, e => e is Character c && c.IsPlayer);
            }
        }

        private void TagBots(bool playerCrewOnly)
        {
            if (IgnoreIncapacitatedCharacters)
            {
                ParentEvent.AddTargetPredicate(Tag, e => e is Character c && c.IsBot && !c.IsIncapacitated && (!playerCrewOnly || c.TeamID == CharacterTeamType.Team1));
            }
            else
            {
                ParentEvent.AddTargetPredicate(Tag, e => e is Character c && c.IsBot && (!playerCrewOnly || c.TeamID == CharacterTeamType.Team1));
            }
        }

        private void TagCrew()
        {
#if CLIENT
            GameMain.GameSession.CrewManager.GetCharacters().ForEach(c => ParentEvent.AddTarget(Tag, c));
#else
            TagPlayers(); 
            TagBots(playerCrewOnly: true);
#endif
        }

        private void TagHumansByIdentifier(Identifier identifier)
        {
            foreach (Character c in Character.CharacterList)
            {
                if (c.HumanPrefab?.Identifier == identifier)
                {
                    ParentEvent.AddTarget(Tag, c);
                }
            }
        }

        private void TagHumansByJobIdentifier(Identifier jobIdentifier)
        {
            foreach (Character c in Character.CharacterList)
            {
                if (c.HasJob(jobIdentifier))
                {
                    ParentEvent.AddTarget(Tag, c);
                }
            }
        }

        private void TagStructuresByIdentifier(Identifier identifier)
        {
            ParentEvent.AddTargetPredicate(Tag, e => e is Structure s && SubmarineTypeMatches(s.Submarine) && s.Prefab.Identifier == identifier);
        }

        private void TagStructuresBySpecialTag(Identifier tag)
        {
            ParentEvent.AddTargetPredicate(Tag, e => e is Structure s && SubmarineTypeMatches(s.Submarine) && s.SpecialTag.ToIdentifier() == tag);
        }

        private void TagItemsByIdentifier(Identifier identifier)
        {
            ParentEvent.AddTargetPredicate(Tag, e => e is Item it && SubmarineTypeMatches(it.Submarine) && it.Prefab.Identifier == identifier);
        }

        private void TagItemsByTag(Identifier tag)
        {
            ParentEvent.AddTargetPredicate(Tag, e => e is Item it && SubmarineTypeMatches(it.Submarine) && it.HasTag(tag));
        }

        private void TagHullsByName(Identifier name)
        {
            ParentEvent.AddTargetPredicate(Tag, e => e is Hull h && SubmarineTypeMatches(h.Submarine) && h.RoomName.Contains(name.Value, StringComparison.OrdinalIgnoreCase));
        }

        private void TagSubmarinesByType(Identifier type)
        {
            ParentEvent.AddTargetPredicate(Tag, e => e is Submarine s && SubmarineTypeMatches(s) && (type.IsEmpty || type == s.Info?.Type.ToIdentifier()));
        }

        private bool SubmarineTypeMatches(Submarine sub)
        {
            if (SubmarineType == SubType.Any) { return true; }
            if (sub == null) { return false; }
            switch (sub.Info.Type)
            {
                case Barotrauma.SubmarineType.Player:
                    return SubmarineType.HasFlag(SubType.Player);
                case Barotrauma.SubmarineType.Outpost:
                case Barotrauma.SubmarineType.OutpostModule:
                    return SubmarineType.HasFlag(SubType.Outpost);
                case Barotrauma.SubmarineType.Wreck:
                    return SubmarineType.HasFlag(SubType.Wreck);
                case Barotrauma.SubmarineType.BeaconStation:
                    return SubmarineType.HasFlag(SubType.BeaconStation);
                default:
                    return false;
            }
        }

        private readonly ImmutableDictionary<Identifier, Action<Identifier>> Taggers;
        
        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            string[] criteriaSplit = Criteria.Split(';');

            foreach (string entry in criteriaSplit)
            {
                string[] kvp = entry.Split(':');
                Identifier key = kvp[0].Trim().ToIdentifier();
                Identifier value = kvp.Length > 1 ? kvp[1].Trim().ToIdentifier() : Identifier.Empty;
                if (Taggers.TryGetValue(key, out Action<Identifier> tagger))
                {
                    tagger(value);
                }
                else
                {
                    string errorMessage = $"Error in TagAction (event \"{ParentEvent.Prefab.Identifier}\") - unrecognized target criteria \"{key}\".";
                    DebugConsole.ThrowError(errorMessage);
                    GameAnalyticsManager.AddErrorEventOnce($"TagAction.Update:InvalidCriteria_{ParentEvent.Prefab.Identifier}_{key}", GameAnalyticsManager.ErrorSeverity.Error, errorMessage);
                }
            }

            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(TagAction)} -> (Criteria: {Criteria.ColorizeObject()}, Tag: {Tag.ColorizeObject()}, Sub: {SubmarineType.ColorizeObject()})";
        }
    }
}