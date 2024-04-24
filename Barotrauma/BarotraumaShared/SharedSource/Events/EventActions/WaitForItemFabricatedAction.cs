#nullable enable

using Barotrauma.Items.Components;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Waits for some item(s) to be fabricated before continuing the execution of the event.
    /// </summary>
    class WaitForItemFabricatedAction : EventAction
    {
        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character who must fabricate the item. If empty, it doesn't matter who fabricates it.")]
        public Identifier CharacterTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the item that must be fabricated. Optional if ItemTag is set.")]
        public Identifier ItemIdentifier { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the item that must be fabricated. Optional if ItemIdentifier is set.")]
        public Identifier ItemTag { get; set; }

        [Serialize(1, IsPropertySaveable.Yes, description: "Number of items that need to be fabricated.")]
        public int Amount { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag to apply to the fabricated item(s).")]
        public Identifier ApplyTagToItem { get; set; }

        private int counter;

        public WaitForItemFabricatedAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
        { 
            if (ItemTag.IsEmpty && ItemIdentifier.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in event \"{ParentEvent.Prefab.Identifier}\". {nameof(WaitForItemFabricatedAction)} does't define either a tag or an identifier of the item to check.", 
                    contentPackage: element.ContentPackage);
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
            if ((!ItemIdentifier.IsEmpty && item.Prefab.Identifier == ItemIdentifier) ||
                (!ItemTag.IsEmpty && item.HasTag(ItemTag)))
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