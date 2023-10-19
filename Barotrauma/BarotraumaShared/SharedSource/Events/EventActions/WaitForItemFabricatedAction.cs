#nullable enable

using Barotrauma.Items.Components;
using System.Linq;

namespace Barotrauma
{
    class WaitForItemFabricatedAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier CharacterTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier ItemIdentifier { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier ItemTag { get; set; }

        [Serialize(1, IsPropertySaveable.Yes)]
        public int Amount { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag to apply to the fabricated item(s).")]
        public Identifier ApplyTagToItem { get; set; }

        private int counter;

        public WaitForItemFabricatedAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
        { 
            if (ItemTag.IsEmpty && ItemIdentifier.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in event \"{ParentEvent.Prefab.Identifier}\". {nameof(WaitForItemFabricatedAction)} does't define either a tag or an identifier of the item to check.");
            }
            foreach (var item in Item.ItemList)
            {
                var fabricator = item.GetComponent<Fabricator>();
                if (fabricator != null)
                {
                    fabricator.OnItemFabricated += OnItemFabricated;
                }
            }
        }

        public void OnItemFabricated(Item item, Character character)
        {
            if (item == null) { return; }
            if (!CharacterTag.IsEmpty)
            {
                if (!ParentEvent.GetTargets(CharacterTag).Contains(character)) { return; }
            }
            if (item.ContainerIdentifier == ItemTag || item.HasTag(ItemTag))
            {
                if (!ApplyTagToItem.IsEmpty)
                {
                    ParentEvent.AddTarget(ApplyTagToItem, item);
                }
                counter++;
            }
        }

        public override bool IsFinished(ref string goTo)
        {
            return counter >= Amount;
        }

        public override void Reset()
        {
            counter = 0;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(counter >= Amount)} {nameof(WaitForItemFabricatedAction)} -> ({ItemTag}, {counter}/{Amount})";
        }
    }
}