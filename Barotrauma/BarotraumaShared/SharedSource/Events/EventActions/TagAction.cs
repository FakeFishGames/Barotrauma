using Barotrauma.Extensions;
using System;
using System.Xml.Linq;

namespace Barotrauma
{
    class TagAction : EventAction
    {
        public enum SubType { Any = 0, Player = 1, Outpost = 2, Wreck = 4, BeaconStation = 8 }

        [Serialize("", true)]
        public string Criteria { get; set; }

        [Serialize("", true)]
        public string Tag { get; set; }

        [Serialize(SubType.Any, true)]
        public SubType SubmarineType { get; set; }

        [Serialize(true, true)]
        public bool IgnoreIncapacitatedCharacters { get; set; }

        private bool isFinished = false;

        public TagAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) { }

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

        private void TagHumansByIdentifier(string identifier)
        {
            foreach (Character c in Character.CharacterList)
            {
                if (c.Prefab?.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase) ?? false)
                {
                    ParentEvent.AddTarget(Tag, c);
                }
            }
        }
        private void TagStructuresByIdentifier(string identifier)
        {
            ParentEvent.AddTargetPredicate(Tag, e => e is Structure s && SubmarineTypeMatches(s.Submarine) && s.Prefab.Identifier.Equals(identifier, StringComparison.InvariantCultureIgnoreCase));
        }

        private void TagItemsByIdentifier(string identifier)
        {
            ParentEvent.AddTargetPredicate(Tag, e => e is Item it && SubmarineTypeMatches(it.Submarine) && it.Prefab.Identifier.Equals(identifier, StringComparison.InvariantCultureIgnoreCase));
        }

        private void TagItemsByTag(string tag)
        {
            ParentEvent.AddTargetPredicate(Tag, e => e is Item it && SubmarineTypeMatches(it.Submarine) && it.HasTag(tag));
        }

        private void TagHullsByName(string name)
        {
            ParentEvent.AddTargetPredicate(Tag, e => e is Hull h && SubmarineTypeMatches(h.Submarine) && h.RoomName.Contains(name, StringComparison.OrdinalIgnoreCase));
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

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            string[] criteriaSplit = Criteria.Split(';');

            foreach (string entry in criteriaSplit)
            {
                string[] kvp = entry.Split(':');
                switch (kvp[0].Trim().ToLowerInvariant())
                {
                    case "player":
                        TagPlayers();
                        break;
                    case "bot":
                        TagBots(playerCrewOnly: false);
                        break;
                    case "crew":
                        TagCrew();
                        break;
                    case "humanprefabidentifier":
                        if (kvp.Length > 1) { TagHumansByIdentifier(kvp[1].Trim()); }
                        break;
                    case "structureidentifier":
                        if (kvp.Length > 1) { TagStructuresByIdentifier(kvp[1].Trim()); }
                        break;
                    case "itemidentifier":
                        if (kvp.Length > 1) { TagItemsByIdentifier(kvp[1].Trim()); }
                        break;
                    case "itemtag":
                        if (kvp.Length > 1) { TagItemsByTag(kvp[1].Trim()); }
                        break;
                    case "hullname":
                        if (kvp.Length > 1) { TagHullsByName(kvp[1].Trim()); }
                        break;
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