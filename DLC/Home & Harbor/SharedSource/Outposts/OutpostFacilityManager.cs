using System.Collections.Immutable;
using System.Xml.Linq;
using Barotrauma;
using Barotrauma.Items.Components;
using HomeAndHarbor.Resources;
using Microsoft.Xna.Framework;

namespace HomeAndHarbor
{
    public readonly record struct ItemOnOutpost(ItemPrefab ItemPrefab, XElement Element)
    {
        public static ItemOnOutpost FromXML(ItemPrefab prefab, XElement element)
            => new ItemOnOutpost(prefab, element);
    }

    public sealed class OutpostFacilityManager(ImmutableDictionary<ItemPrefab, int> itemCountsOnOutpost)
    {
        private readonly Dictionary<Identifier, int> cachedCounts = new();

        public static OutpostFacilityManager ConstructFromOutpost(Submarine outpost)
        {
            var builder = ImmutableDictionary.CreateBuilder<ItemPrefab, int>();
            foreach (Item item in outpost.GetItems(alsoFromConnectedSubs: true))
            {
                //the item may have been removed by some other resource, but the removal hasn't been processed yet
                if (Entity.Spawner?.IsInRemoveQueue(item) ?? false) { continue; }
                //non-interactable items are most likely some decorative things that shouldn't contribute to resources
                if (item.NonInteractable || item.HiddenInGame) { continue; }
                //disregard items in character inventories
                if (item.GetRootInventoryOwner() is Character ownerCharacter) { continue; }
                //plants only count when planted and alive (not e.g. seeds in a cabinet, or dead plants)
                if (item.GetComponent<Growable>() is { } growable && !IsLivingPlant(item, growable)) { continue; }
                builder.TryGetValue(item.Prefab, out int itemCount);
                builder[item.Prefab] = ++itemCount;
            }

            return new OutpostFacilityManager(builder.ToImmutable());
        }

        private static bool IsLivingPlant(Item item, Growable growable)
            => item.Container?.GetComponent<Planter>() != null && !growable.Decayed;

        public static OutpostFacilityManager ConstructFromOutpostElement(XElement element, string debugLogName)
        {
            var builder = ImmutableDictionary.CreateBuilder<ItemPrefab, int>();
            foreach (XElement itemElement in element.Elements())
            {
                if (itemElement.NameAsIdentifier() == nameof(Item))
                {
                    Identifier itemIdentifier = itemElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                    if (!ItemPrefab.Prefabs.TryGet(itemIdentifier, out ItemPrefab? result)) 
                    {
                        DebugConsole.ThrowError($"Error while loading an {nameof(OutpostFacilityManager)} for {debugLogName}: could not find the item {itemIdentifier}.");
                        continue;
                    }

                    if (itemElement.GetAttributeBool(nameof(Item.NonInteractable), false) || 
                        itemElement.GetAttributeBool(nameof(Item.HiddenInGame), false))
                    {
                        continue;
                    }

                    if (!builder.TryAdd(result, 1))
                    {
                        builder[result]++;
                    }
                }
            }

            return new OutpostFacilityManager(builder.ToImmutable());
        }

        public static OutpostFacilityManager Load(XElement element)
        {
            var builder = ImmutableDictionary.CreateBuilder<ItemPrefab, int>();
            foreach (XElement itemElement in element.Elements())
            {
                if (itemElement.NameAsIdentifier() == nameof(ItemPrefab))
                {
                    Identifier itemIdentifier = itemElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                    if (!ItemPrefab.Prefabs.TryGet(itemIdentifier, out ItemPrefab? result)) { continue; }

                    if (itemElement.GetAttributeBool(nameof(Item.NonInteractable), false) ||
                        itemElement.GetAttributeBool(nameof(Item.HiddenInGame), false))
                    {
                        continue;
                    }

                    builder[result] = itemElement.GetAttributeInt("count", 0);
                }
            }

            return new OutpostFacilityManager(builder.ToImmutable());
        }

        public int GetCountByTag(Identifier tag)
        {
            if (cachedCounts.TryGetValue(tag, out int cachedCount))
            {
                return cachedCount;
            }

            int count = 0;
            foreach (var (itemPrefab, itemCount) in itemCountsOnOutpost)
            {
                if (itemPrefab.Tags.Contains(tag) || itemPrefab.Identifier == tag)
                {
                    count += itemCount;
                }
            }

            cachedCounts[tag] = count;
            return count;
        }

        public void Save(XElement element)
        {
            XElement saveElement = new XElement(nameof(OutpostFacilityManager));
            foreach (var (itemPrefab, count) in itemCountsOnOutpost)
            {
                XElement itemElement = new XElement(nameof(ItemPrefab),
                                                    new XAttribute("identifier", itemPrefab.Identifier),
                                                    new XAttribute("count", count));
                saveElement.Add(itemElement);
            }

            element.Add(saveElement);
        }
    }
}