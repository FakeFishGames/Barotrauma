#nullable enable

using System;
using System.Linq;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal readonly record struct ItemSlotIndexPair(int Slot, int StackIndex)
    {
        public static Option<ItemSlotIndexPair> TryDeserializeFromXML(ContentXElement element, string elementName)
        {
            string? elementStr = element.GetAttributeString(elementName, string.Empty);
            if (string.IsNullOrEmpty(elementStr)) { return Option.None; }

            var point = XMLExtensions.ParsePoint(elementStr);
            return Option.Some(new ItemSlotIndexPair(point.X, point.Y));
        }

        public static string Serialize(Item item)
        {
            Inventory parent = item.ParentInventory;
            if (item.ParentInventory is null)
            {
                throw new Exception($"Item \"{item.Name}\" is not in an inventory.");
            }

            int slotIndex = parent.FindIndex(item);

            int stackIndex = parent.GetItemStackSlotIndex(item, slotIndex);

            if (slotIndex < 0 || stackIndex < 0)
            {
                throw new Exception($"Unable to find item \"{item.Name}\" in its parent inventory.");
            }

            return XMLExtensions.PointToString(new Point(slotIndex, stackIndex));
        }

        public Item? FindItemInContainer(ItemContainer? container)
        {
            var items = container?.Inventory.GetItemsAt(Slot);
            if (items != null && StackIndex >= 0 && StackIndex < items.Count())
            {
                return items.ElementAt(StackIndex);
            }
            else
            {
                string errorMsg =
                    $"Circuit box error: failed to find an item in the container {container?.Item.Name ?? "null"}.";
                DebugConsole.ThrowError(
                    errorMsg +
                    $" Items: {items?.Count().ToString() ?? "null"}, " +
                    $" Slot: {Slot}, StackIndex: {StackIndex}");
                GameAnalyticsManager.AddErrorEventOnce("ItemSlotIndexPair.FindItemInContainer", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                return null;
            }
        }
    }
}