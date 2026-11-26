using System.Collections.Immutable;
using System.Xml.Linq;
using Barotrauma;
using Barotrauma.Items.Components;

namespace HomeAndHarbor.Resources;

/// <summary>
/// A resource that calculates the number of hulls in the outpost that have certain kinds of items in them, as defined in the resource.
/// </summary>
internal class FurnishedHullResource : ReadonlyResource
{
    private readonly ImmutableArray<Identifier> requiredItems;

    /// <summary>
    /// Identifiers/tags counted together as one group (an item matching any of them counts once).
    /// The hull must contain between <see cref="minCount"/> and <see cref="maxCount"/> (inclusive) items of the group.
    /// </summary>
    private readonly ImmutableArray<Identifier> countItems;
    private readonly int minCount;
    private readonly int maxCount;

    /// <summary>
    /// Hulls containing any item matching any of these identifiers/tags are not counted (e.g. "reactor").
    /// </summary>
    private readonly ImmutableArray<Identifier> excludedItems;

    public FurnishedHullResource(ResourcePrefab prefab, ResourceManager manager, XElement? element = null) : base(prefab, manager, element)
    {
        excludedItems = prefab.OriginalElement.GetAttributeIdentifierImmutableHashSet(nameof(excludedItems), def: []).ToImmutableArray();
        requiredItems = prefab.OriginalElement.GetAttributeIdentifierArray(nameof(requiredItems), def: []).ToImmutableArray();
        countItems = prefab.OriginalElement.GetAttributeIdentifierArray(nameof(countItems), def: []).ToImmutableArray();
        minCount = prefab.OriginalElement.GetAttributeInt(nameof(minCount), 0);
        maxCount = prefab.OriginalElement.GetAttributeInt(nameof(maxCount), int.MaxValue);
    }

    protected override void OnOutpostEnteredResourceSpecific(Submarine outpost, PersistentOutpostData outpostData)
    {
        RefreshAmount(outpost, outpostData);
    }

    protected override void OnOutpostExitedResourceSpecific(Submarine outpost, PersistentOutpostData outpostData, bool shouldCreateLogs)
    {
        RefreshAmount(outpost, outpostData);
    }

    protected override float GetAmountResourceSpecific()
    {
        return CalculatedAmount;
    }

    /// <summary>
    /// The amount calculated the last time the outpost was entered or exited. Saved so the value persists when the outpost isn't loaded.
    /// </summary>
    [Serialize(0f, IsPropertySaveable.Yes, description: "The number of furnished hulls calculated the last time the outpost was entered or exited.")]
    public float CalculatedAmount { get; private set; }

    private void RefreshAmount(Submarine outpost, PersistentOutpostData outpostData)
    {
        // Group items by hull once, instead of scanning all items for every hull and check
        Dictionary<Hull, List<Item>> itemsByHull = new Dictionary<Hull, List<Item>>();
        foreach (Item item in outpost.GetItems(alsoFromConnectedSubs: false))
        {
            if (item.CurrentHull == null) { continue; }
            if (!itemsByHull.TryGetValue(item.CurrentHull, out List<Item>? hullItems))
            {
                hullItems = new List<Item>();
                itemsByHull[item.CurrentHull] = hullItems;
            }
            hullItems.Add(item);
        }

        int furnishedHullCount = 0;
        foreach (Hull hull in outpost.GetHulls(alsoFromConnectedSubs: false))
        {
            if (hull.IsWetRoom || hull.IsAirlock || hull.AvoidStaying) { continue; }
            itemsByHull.TryGetValue(hull, out List<Item>? hullItems);
            hullItems ??= new List<Item>();
            if (excludedItems.Length > 0 &&
                hullItems.Any(it => excludedItems.Any(id => it.HasIdentifierOrTag(id))))
            {
                continue;
            }

            bool allItemsFound = true;
            foreach (Identifier requiredItem in requiredItems.Distinct())
            {
                int requiredCount = requiredItems.Count(it => it == requiredItem);
                if (hullItems.Count(it => it.HasIdentifierOrTag(requiredItem)) < requiredCount)
                {
                    allItemsFound = false; 
                    break;
                }
            }
            if (allItemsFound && countItems.Length > 0)
            {
                int groupCount = hullItems.Count(it => countItems.Any(id => it.HasIdentifierOrTag(id)));
                if (groupCount < minCount || groupCount > maxCount) { allItemsFound = false; }
            }
            if (allItemsFound) { furnishedHullCount++; }
        }
        CalculatedAmount = furnishedHullCount;
    }
}