using System;
using System.Linq;

namespace Barotrauma;

/// <summary>
/// Check whether specific kinds of items have been purchased or sold during the round.
/// </summary>
class CheckPurchasedItemsAction : BinaryOptionAction
{
    public enum TransactionType
    {
        Purchased,
        Sold
    }

    [Serialize(TransactionType.Purchased, IsPropertySaveable.Yes, description: "Do the items need to have been purchased or sold?")]
    public TransactionType Type { get; set; }

    [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the item that must have been purchased or sold.")]
    public Identifier ItemIdentifier { get; set; }

    [Serialize("", IsPropertySaveable.Yes, description: "Tag of the item that must have been purchased or sold.")]
    public Identifier ItemTag { get; set; }

    [Serialize(1, IsPropertySaveable.Yes, description: "Minimum number of matching items that must have been purchased or sold.")]
    public int MinCount { get; set; }

    public CheckPurchasedItemsAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
    {
        MinCount = Math.Max(MinCount, 1);
    }

    protected override bool? DetermineSuccess()
    {
        if (ItemIdentifier.IsEmpty && ItemTag.IsEmpty)
        {
            return false;
        }
        if (GameMain.GameSession?.Campaign?.CargoManager is not CargoManager cargoManager)
        {
            return false;
        }
        if (Type == TransactionType.Purchased)
        {
            int totalPurchased = 0;
            foreach ((Identifier id, var items) in cargoManager.PurchasedItems)
            {
                if (!ItemIdentifier.IsEmpty)
                {
                    totalPurchased += items.Find(i => i.ItemPrefabIdentifier == ItemIdentifier)?.Quantity ?? 0;
                }
                else if (!ItemTag.IsEmpty)
                {
                    foreach (var item in items)
                    {
                        if (item.ItemPrefab.Tags.Contains(ItemTag))
                        {
                            totalPurchased += item.Quantity;
                        }
                    }
                }
                if (totalPurchased >= MinCount)
                {
                    return true;
                }
            }
        }
        else
        {
            int totalSold = 0;
            foreach ((Identifier id, var items) in cargoManager.SoldItems)
            {
                if (!ItemIdentifier.IsEmpty)
                {
                    totalSold += items.Count(i => i.ItemPrefab.Identifier == ItemIdentifier);
                }
                else if (!ItemTag.IsEmpty)
                {
                    totalSold += items.Count(i => i.ItemPrefab.Tags.Contains(ItemTag));
                }
                if (totalSold >= MinCount)
                {
                    return true;
                }
            }
        }
        return false;
    }
}