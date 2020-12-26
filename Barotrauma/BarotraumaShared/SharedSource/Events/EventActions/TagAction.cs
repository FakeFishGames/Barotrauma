using Barotrauma.Extensions;
using System;
using System.Xml.Linq;

namespace Barotrauma
{
    class TagAction : EventAction
    {
        [Serialize("", true)]
        public string Criteria { get; set; }

        [Serialize("", true)]
        public string Tag { get; set; }

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

        private void TagBots()
        {
            if (IgnoreIncapacitatedCharacters)
            {
                ParentEvent.AddTargetPredicate(Tag, e => e is Character c && c.IsBot && !c.IsIncapacitated);
            }
            else
            {
                ParentEvent.AddTargetPredicate(Tag, e => e is Character c && c.IsBot);
            }
        }

        private void TagCrew()
        {
#if CLIENT
            GameMain.GameSession.CrewManager.GetCharacters().ForEach(c => ParentEvent.AddTarget(Tag, c));
#else
            TagPlayers(); TagBots(); //TODO: this seems like it would tag more than it should, fix
#endif
        }

        private void TagStructuresByIdentifier(string identifier)
        {
            ParentEvent.AddTargetPredicate(Tag, e => e is Structure s && s.Prefab.Identifier.Equals(identifier, StringComparison.InvariantCultureIgnoreCase));
        }

        private void TagItemsByIdentifier(string identifier)
        {
            ParentEvent.AddTargetPredicate(Tag, e => e is Item it && it.Prefab.Identifier.Equals(identifier, StringComparison.InvariantCultureIgnoreCase));
        }

        private void TagItemsByTag(string tag)
        {
            ParentEvent.AddTargetPredicate(Tag, e => e is Item it && it.HasTag(tag));
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
                        TagBots();
                        break;
                    case "crew":
                        TagCrew();
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
                }
            }

            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(TagAction)} -> (Criteria: {Criteria.ColorizeObject()}, Tag: {Tag.ColorizeObject()})";
        }
    }
}