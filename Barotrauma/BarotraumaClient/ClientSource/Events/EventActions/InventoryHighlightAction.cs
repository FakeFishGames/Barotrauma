using Microsoft.Xna.Framework;
using System.Linq;

namespace Barotrauma;

partial class InventoryHighlightAction : EventAction
{
    private static readonly Color highlightColor = Color.Orange;

    partial void UpdateProjSpecific()
    {
        foreach (var target in ParentEvent.GetTargets(TargetTag))
        {
            SetHighlight(target);
        }
    }

    private void SetHighlight(Entity entity)
    {
        if (entity is Item item)
        {
            int i = 0;
            foreach (var itemContainer in item.GetComponents<Items.Components.ItemContainer>())
            {
                if (ItemContainerIndex == -1 || i == ItemContainerIndex)
                {
                    SetHighlight(itemContainer.Inventory);
                }
                i++;
            }
        }
        else if (entity is Character c)
        {
            SetHighlight(c.Inventory);
        }
    }

    private void SetHighlight(Inventory inventory)
    {
        if (inventory?.visualSlots == null) { return; }
        for (int i = 0; i < inventory.visualSlots.Length; i++)
        {
            if (inventory.visualSlots[i].HighlightTimer > 0) { continue; }
            Item item = inventory.GetItemAt(i);
            if (IsSuitableItem(item) ||
                (Recursive && item?.OwnInventory != null && item.OwnInventory.FindAllItems(it => IsSuitableItem(it), recursive: true).Any()))
            {
                inventory.visualSlots[i].ShowBorderHighlight(highlightColor, 0.5f, 0.5f, 0.1f);
            }
        }
    }

    private bool IsSuitableItem(Item item)
    {
        return (ItemIdentifier.IsEmpty && item == null) ||
                (item != null && (item.Prefab.Identifier == ItemIdentifier || item.HasTag(ItemIdentifier)));
    }
}