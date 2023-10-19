using Barotrauma.Extensions;
using System;
using System.Collections.Generic;
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

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool AllowHiddenItems { get; set; }

        [Serialize(false, IsPropertySaveable.Yes)]
        public bool ChooseRandom { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should the event continue if the TagAction can't find any valid targets?")]
        public bool ContinueIfNoTargetsFound { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "If larger than 0, the specified percentage of the matching targets are tagged. Between 0-100.")]
        public float ChoosePercentage { get; set; }

        private bool isFinished = false;

        private bool targetNotFound = false;

        public TagAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            Taggers = new (string k, Action<Identifier> v)[]
            {
                ("players", v => TagPlayers()),
                ("player", v => TagPlayers()),
                ("traitor", v => TagTraitors()),
                ("nontraitor", v => TagNonTraitors()),
                ("nontraitorplayer", v => TagNonTraitorPlayers()),
                ("bot", v => TagBots(playerCrewOnly: false)),
                ("crew", v => TagCrew()),
                ("humanprefabidentifier", TagHumansByIdentifier),
                ("jobidentifier", TagHumansByJobIdentifier),
                ("structureidentifier", TagStructuresByIdentifier),
                ("structurespecialtag", TagStructuresBySpecialTag),
                ("itemidentifier", TagItemsByIdentifier),
                ("itemtag", TagItemsByTag),
                ("hull", v => TagHulls()),
                ("hullname", TagHullsByName),
                ("submarine", TagSubmarinesByType),
                ("eventtag", TagByEventTag),
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

        private void TagByEventTag(Identifier eventTag)
        {
            AddTarget(Tag, ParentEvent.GetTargets(eventTag).Where(t => SubmarineTypeMatches(t.Submarine)));
        }

        private void TagPlayers()
        {
            AddTargetPredicate(Tag, e => e is Character c && c.IsPlayer && (!c.IsIncapacitated || !IgnoreIncapacitatedCharacters));
        }

        private void TagTraitors()
        {
            AddTargetPredicate(Tags.Traitor, e => e is Character c && (c.IsPlayer || c.IsBot) && c.IsTraitor && !c.IsIncapacitated);
        }

        private void TagNonTraitors()
        {
            AddTargetPredicate(Tags.NonTraitor, e => e is Character c && (c.IsPlayer || c.IsBot) && !c.IsTraitor && c.IsOnPlayerTeam && !c.IsIncapacitated);
        }

        private void TagNonTraitorPlayers()
        {
            AddTargetPredicate(Tags.NonTraitorPlayer, e => e is Character c && c.IsPlayer && !c.IsTraitor && c.IsOnPlayerTeam && !c.IsIncapacitated);
        }

        private void TagBots(bool playerCrewOnly)
        {
            AddTargetPredicate(Tag, e => 
                e is Character c &&
                c.IsBot && 
                (!c.IsIncapacitated || !IgnoreIncapacitatedCharacters) && 
                (!playerCrewOnly || c.TeamID == CharacterTeamType.Team1));
        }

        private void TagCrew()
        {
#if CLIENT
            AddTarget(Tag, GameMain.GameSession.CrewManager.GetCharacters());
#else
            TagPlayers(); 
            TagBots(playerCrewOnly: true);
#endif
        }

        private void TagHumansByIdentifier(Identifier identifier)
        {
            AddTarget(Tag, Character.CharacterList.Where(c => c.HumanPrefab?.Identifier == identifier));
        }

        private void TagHumansByJobIdentifier(Identifier jobIdentifier)
        {
            AddTarget(Tag, Character.CharacterList.Where(c => c.HasJob(jobIdentifier)));
        }

        private void TagStructuresByIdentifier(Identifier identifier)
        {
            AddTargetPredicate(Tag, e => e is Structure s && SubmarineTypeMatches(s.Submarine) && s.Prefab.Identifier == identifier);
        }

        private void TagStructuresBySpecialTag(Identifier tag)
        {
            AddTargetPredicate(Tag, e => e is Structure s && SubmarineTypeMatches(s.Submarine) && s.SpecialTag.ToIdentifier() == tag);
        }

        private void TagItemsByIdentifier(Identifier identifier)
        {
            AddTargetPredicate(Tag, e => e is Item it && IsValidItem(it) && it.Prefab.Identifier == identifier);
        }

        private void TagItemsByTag(Identifier tag)
        {
            AddTargetPredicate(Tag, e => e is Item it && IsValidItem(it) && it.HasTag(tag));
        }

        private void TagHulls()
        {
            AddTargetPredicate(Tag, e => e is Hull h && SubmarineTypeMatches(h.Submarine));
        }

        private void TagHullsByName(Identifier name)
        {
            AddTargetPredicate(Tag, e => e is Hull h && SubmarineTypeMatches(h.Submarine) && h.RoomName.Contains(name.Value, StringComparison.OrdinalIgnoreCase));
        }

        private void TagSubmarinesByType(Identifier type)
        {
            AddTargetPredicate(Tag, e => e is Submarine s && SubmarineTypeMatches(s) && (type.IsEmpty || type == s.Info?.Type.ToIdentifier()));
        }

        private bool IsValidItem(Item it)
        {
            return (!it.HiddenInGame || AllowHiddenItems) && SubmarineTypeMatches(it.Submarine);
        }

        private bool SubmarineTypeMatches(Submarine sub)
        {
            if (SubmarineType == SubType.Any) { return true; }
            if (sub == null) { return false; }
            switch (sub.Info.Type)
            {
                case Barotrauma.SubmarineType.Player:
                    return SubmarineType.HasFlag(SubType.Player) && sub != GameMain.NetworkMember?.RespawnManager?.RespawnShuttle;
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

        private void AddTargetPredicate(Identifier tag, Predicate<Entity> predicate)
        {
            if (ChoosePercentage > 0.0f)
            {
                TagPercentage(tag, Entity.GetEntities().Where(e => predicate(e)));
            }
            else if (ChooseRandom)
            {
                TagRandom(tag, Entity.GetEntities().Where(e => predicate(e)));
            }
            else
            {
                ParentEvent.AddTargetPredicate(tag, predicate);
            }
        }

        private void AddTarget(Identifier tag, IEnumerable<Entity> entities)
        {
            if (entities.None())
            {
                targetNotFound = true;
                return;
            }
            if (ChoosePercentage > 0.0f)
            {
                TagPercentage(tag, entities);
            }
            else if (ChooseRandom)
            {
                TagRandom(tag, entities);
            }
            else
            {
                foreach (var entity in entities)
                {
                    ParentEvent.AddTarget(tag, entity);
                }
            }
        }

        private List<Entity> tempEntities;
        private void TagPercentage(Identifier tag, IEnumerable<Entity> entities)
        {
            if (entities.None())
            {
                targetNotFound = true;
                return;
            }

            int amountToChoose = (int)Math.Ceiling(entities.Count() * (ChoosePercentage / 100.0f));

            tempEntities ??= new List<Entity>();
            tempEntities.Clear();
            for (int i = 0; i < amountToChoose; i++)
            {
                var entity = entities.GetRandomUnsynced();
                tempEntities.Remove(entity);
                ParentEvent.AddTarget(tag, entity);
            }
        }

        private void TagRandom(Identifier tag, IEnumerable<Entity> entities)
        {
            if (entities.None()) 
            {
                targetNotFound = true;
                return; 
            }
            ParentEvent.AddTarget(tag, entities.GetRandomUnsynced());
        }

        private readonly ImmutableDictionary<Identifier, Action<Identifier>> Taggers;
        
        public override void Update(float deltaTime)
        {
            if (isFinished || targetNotFound) { return; }

            string[] criteriaSplit = Criteria.Split(';');

            targetNotFound = false;
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

            if (ContinueIfNoTargetsFound)
            {
                isFinished = true;
            }
            else
            {
                isFinished = !targetNotFound;
            }
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(TagAction)} -> (Criteria: {Criteria.ColorizeObject()}, Tag: {Tag.ColorizeObject()}, Sub: {SubmarineType.ColorizeObject()})";
        }
    }
}