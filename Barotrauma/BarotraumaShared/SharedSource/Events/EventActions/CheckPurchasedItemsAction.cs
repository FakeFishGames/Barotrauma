using System;
using System.Linq;

namespace Barotrauma;

class CheckPurchasedItemsAction : BinaryOptionAction
{
    public enum TransactionType
    {
        Purchased,
        Sold
    }

    [Serialize(TransactionType.Purchased, IsPropertySaveable.Yes)]
    public TransactionType Type { get; set; }

    [Serialize("", IsPropertySaveable.Yes)]
    public Identifier ItemIdentifier { get; set; }

    [Serialize("", IsPropertySaveable.Yes)]
    public Identifier ItemTag { get; set; }

    [Serialize(1, IsPropertySaveable.Yes)]
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