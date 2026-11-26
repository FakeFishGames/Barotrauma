using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;

namespace Barotrauma.PerkBehaviors
{
    public class SpawnItemPerk : PerkBase
    {
        public SpawnItemPerk(ContentXElement element, DisembarkPerkPrefab prefab) : base(element, prefab) { }

        public override PerkSimulation Simulation
            => PerkSimulation.ServerOnly;

        /// <summary>
        /// Spawn the items where the crew spawns, so e.g. in outpost combat missions they're in the outpost instead of the (possibly far away) sub.
        /// </summary>
        public override bool AppliesToSpawnLocation => true;

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier Identifier { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier Tag { get; set; }

        [Serialize(0, IsPropertySaveable.Yes)]
        public int MinAmount { get; set; }

        [Serialize(0f, IsPropertySaveable.Yes)]
        public float PerPlayer { get; set; }

        /// <summary>
        /// When set to non-empty value, the perk will prioritize spawning items in containers
        /// with this tag or identifier over the item's primary and secondary preferred containers.
        /// </summary>
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier PriorityContainerTag { get; set; }

        public override void ApplyOnRoundStart(IReadOnlyCollection<Character> teamCharacters, Submarine teamSubmarine)
        {
            if (teamSubmarine is null) { return; }

            if (Entity.Spawner is null)
            {
                DebugConsole.ThrowError($"{nameof(SpawnItemPerk)} ({Prefab.Identifier}) failed to spawn items because EntitySpawner is null.");
                return;
            }

            int amount = Math.Max(MinAmount, (int)MathF.Ceiling(PerPlayer * teamCharacters.Count));

            if (Identifier.IsEmpty)
            {
                if (Tag.IsEmpty)
                {
                    DebugConsole.ThrowError($"{nameof(SpawnItemPerk)} ({Prefab.Identifier}) failed to spawn items: neither identifier or tag is set.",
                        contentPackage: Prefab.ContentPackage);
                    return;
                }
                var matchingItems = ItemPrefab.Prefabs.Where(ip => ip.Tags.Contains(Tag));
                if (matchingItems.None())
                {
                    DebugConsole.ThrowError($"{nameof(SpawnItemPerk)} ({Prefab.Identifier}) failed to spawn items: no items found with the tag \"{Tag}\".",
                        contentPackage: Prefab.ContentPackage);
                    return;
                }
                for (int i = 0; i < amount; i++)
                {
                    SpawnItem(matchingItems.GetRandomUnsynced(), amount: 1, teamSubmarine, PriorityContainerTag, fallbackContainerTag: Identifier.Empty, spawnInCrates: true, showCargoIcon: true);
                }
            }
            else
            {

                ItemPrefab prefab = ItemPrefab.Find(null, Identifier);
                if (prefab is null)
                {
                    DebugConsole.ThrowError($"{nameof(SpawnItemPerk)} ({Prefab.Identifier}) failed to spawn items because the ItemPrefab \"{Identifier}\" was not found.",
                        contentPackage: Prefab.ContentPackage);
                    return;
                }
                SpawnItem(prefab, amount, teamSubmarine, PriorityContainerTag, fallbackContainerTag: Identifier.Empty, spawnInCrates: true, showCargoIcon: true);
            }
        }

        /// <summary>
        /// Spawns items in the submarine's containers.
        /// </summary>
        /// <param name="prefab">Item prefab to spawn.</param>
        /// <param name="amount">How many items to spawn.</param>
        /// <param name="submarine">Submarine to spawn the items in.</param>
        /// <param name="priorityContainerTag">
        /// When not empty, the spawned items will be prioritized to spawn in containers with this tag or identifier.
        /// </param>
        /// <param name="fallbackContainerTag">
        /// When not empty, the spawned items will be placed in containers with this tag or identifier if no other suitable containers are found. Otherwise, the items will be either ignored or spawned in a crate.
        /// </param>
        /// <param name="spawnInCrates">
        /// When true, the items will be spawned in crates if no suitable containers are found. Otherwise, the items will be ignored.
        /// </param>
        /// <returns>List of spawned items.</returns>
        public static ImmutableArray<Item> SpawnItem(ItemPrefab prefab, int amount, Submarine submarine, Identifier priorityContainerTag, Identifier fallbackContainerTag, bool spawnInCrates, bool showCargoIcon)
        {
            var builder = ImmutableArray.CreateBuilder<Item>();
            SuitableContainers suitableContainers = FindSuitableContainers(prefab, submarine, priorityContainerTag, fallbackContainerTag);

            if (!suitableContainers.Any())
            {
                if (spawnInCrates)
                {
                    foreach (Item item in SpawnItemInCrate(prefab, submarine, amount, showCargoIcon: showCargoIcon))
                    {
                        builder.Add(item);
                    }
                }

                return builder.ToImmutable();
            }

            foreach (Item item in SpawnInContainer(prefab, amount, suitableContainers, submarine, spawnInCrates: true, showCargoIcon: showCargoIcon))
            {
                builder.Add(item);
            }

            return builder.ToImmutable();
        }

        private readonly record struct SuitableContainers(
            ICollection<ItemContainer> PriorityContainers,
            ICollection<ItemContainer> PreferredContainers,
            ICollection<ItemContainer> SecondaryContainers,
            ICollection<ItemContainer> FallbackContainers)
        {
            public bool Any()
                => PriorityContainers.Count > 0
                   || PreferredContainers.Count > 0
                   || SecondaryContainers.Count > 0
                   || FallbackContainers.Count > 0;
        }

        private static SuitableContainers FindSuitableContainers(ItemPrefab prefab, Submarine submarine, Identifier priorityContainerTag, Identifier fallbackContainerTag)
        {
            List<ItemContainer> priorityContainers = [],
                                   primaryContainers = [],
                                   secondaryContainers = [],
                                   fallbackContainers = [];

            foreach (Item containerItem in submarine.GetItems(alsoFromConnectedSubs: true))
            {
                if (containerItem.GetComponent<Fabricator>() != null || containerItem.GetComponent<Deconstructor>() != null) { continue; }
                if (containerItem.NonInteractable || containerItem.NonPlayerTeamInteractable || containerItem.IsHidden) { continue; }

                //do not put in containers that are allowed to be stolen from (e.g. thrash cans)
                //this method is also used by the DLC-only ItemResource, which shouldn't be putting any resources in thrash cans
                if (containerItem.Prefab.AllowStealingContainedItems) { continue; }

                if (containerItem.GetComponent<ItemContainer>() is { } container)
                {
                    if (!container.CanBeContained(prefab)) { continue; }

                    var containerTags = containerItem.GetTags();

                    if (!priorityContainerTag.IsEmpty && (containerTags.Contains(priorityContainerTag) || containerItem.Prefab.Identifier == priorityContainerTag))
                    {
                        priorityContainers.Add(container);
                        continue;
                    }

                    if (prefab.PreferredContainers.Any(pc => pc.Primary.Any(containerTags.Contains) || pc.Primary.Contains(containerItem.Prefab.Identifier)))
                    {
                        primaryContainers.Add(container);
                        continue;
                    }

                    if (prefab.PreferredContainers.Any(pc => pc.Secondary.Any(containerTags.Contains) || pc.Primary.Contains(containerItem.Prefab.Identifier)))
                    {
                        secondaryContainers.Add(container);
                    }

                    if (!fallbackContainerTag.IsEmpty && (containerTags.Contains(fallbackContainerTag) || containerItem.Prefab.Identifier == fallbackContainerTag))
                    {
                        fallbackContainers.Add(container);
                    }
                }
            }

            //sort the selected primary and secondary containers according to the order of the preferred containers in the item prefab
            //(i.e. preferring ammo boxes to be placed in loaders rather than ammo shelves)
            primaryContainers.Sort((selectedContainer1, selectedContainer2) =>
                GetPreferredPrimaryContainerIndex(prefab, selectedContainer1)
                .CompareTo(GetPreferredPrimaryContainerIndex(prefab, selectedContainer2)));
            secondaryContainers.Sort((selectedContainer1, selectedContainer2) =>
                GetPreferredSecondaryContainerIndex(prefab, selectedContainer1)
                .CompareTo(GetPreferredSecondaryContainerIndex(prefab, selectedContainer2)));

            static int GetPreferredPrimaryContainerIndex(ItemPrefab itemToPlace, ItemContainer selectedContainer)
            {
                return itemToPlace.PreferredContainers
                    .IndexOf(pc => 
                        pc.Primary.Any(selectedContainer.Item.GetTags().Contains));
            }
            static int GetPreferredSecondaryContainerIndex(ItemPrefab itemToPlace, ItemContainer selectedContainer)
            {
                return itemToPlace.PreferredContainers
                    .IndexOf(pc => 
                        pc.Secondary.Any(selectedContainer.Item.GetTags().Contains));
            }

            return new SuitableContainers(priorityContainers, primaryContainers, secondaryContainers, fallbackContainers);
        }

        private static IEnumerable<Item> SpawnItemInCrate(ItemPrefab prefab, Submarine submarine, int amount, bool showCargoIcon)
        {
            var purchasedItem = new PurchasedItem(prefab, amount, buyer: null);
            foreach (Item item in CargoManager.DeliverItemsToSub([purchasedItem], submarine, cargoManager: null, showNotification: false, showCargoIcon: showCargoIcon))
            {
                yield return item;
            }
        }

        private static IEnumerable<Item> SpawnInContainer(ItemPrefab prefab, int amount, SuitableContainers containers, Submarine submarine, bool spawnInCrates, bool showCargoIcon)
        {
            Dictionary<ItemContainer, int> containerAllocation = new();

            int remaining = amount;

            TryAllocate(containers.PriorityContainers);
            if (remaining > 0)
            {
                TryAllocate(containers.PreferredContainers);
                if (remaining > 0)
                {
                    TryAllocate(containers.SecondaryContainers);
                    if (remaining > 0)
                    {
#if DEBUG
                        var allocationBeforeFallback = new Dictionary<ItemContainer, int>(containerAllocation);
#endif
                        TryAllocate(containers.FallbackContainers);
#if DEBUG
                        //only the DLC-only ItemResource uses a fallback tag, so this means the resource's items weren't placed in any container meant for them
                        int spawnedInFallback = 0;
                        List<string> fallbackContainerNames = [];
                        foreach (ItemContainer ic in containers.FallbackContainers)
                        {
                            if (!containerAllocation.TryGetValue(ic, out int allocatedNow)) { continue; }

                            allocationBeforeFallback.TryGetValue(ic, out int allocatedBefore);
                            int allocatedToFallback = allocatedNow - allocatedBefore;
                            if (allocatedToFallback <= 0) { continue; }

                            spawnedInFallback += allocatedToFallback;
                            fallbackContainerNames.Add($"{ic.Item.Prefab.Identifier} [{string.Join(",", ic.Item.GetTags())}]");
                        }
                        if (spawnedInFallback > 0)
                        {
                            int preferredContainerCount = containers.PriorityContainers.Count + containers.PreferredContainers.Count + containers.SecondaryContainers.Count;
                            string reason = preferredContainerCount > 0 ?
                                $"all {preferredContainerCount} suitable primary/secondary container(s) for {prefab.Identifier} were full" :
                                $"no suitable primary or secondary container found for {prefab.Identifier}";
                            DebugConsole.AddWarning(
                                $"Using fallback containers in \"{submarine?.Info?.Name}\": {reason}, " +
                                $"{spawnedInFallback} spawned in fallback containers instead: {string.Join(", ", fallbackContainerNames)}. " +
                                "Consider adding an outpost-valid PreferredContainer to the item, or more space in its containers.");
                        }
#endif
                    }
                }
            }

            void TryAllocate(ICollection<ItemContainer> targetContainers)
                => AllocateContainers(prefab, targetContainers, ref remaining, ref containerAllocation);

            foreach (var (container, howManyToPut) in containerAllocation)
            {
                for (int i = 0; i < howManyToPut; i++)
                {
                    if (SpawnItemInContainer(prefab, container) is { } item)
                    {
                        yield return item;
                    }
                }
            }

            if (remaining > 0 && spawnInCrates)
            {
                var items = SpawnItemInCrate(prefab, submarine, remaining, showCargoIcon: showCargoIcon);
                foreach (Item item in items)
                {
                    yield return item;
                }
            }
            yield break;

            static void AllocateContainers(ItemPrefab prefab, ICollection<ItemContainer> containers, ref int remaining, ref Dictionary<ItemContainer, int> containerAllocation)
            {
                foreach (ItemContainer ic in containers)
                {
                    int fit = ic.Inventory.HowManyCanBePut(prefab);
                    if (fit <= 0) { continue; }

                    fit = Math.Min(fit, remaining);

                    if (containerAllocation.ContainsKey(ic))
                    {
                        containerAllocation[ic] += fit;
                    }
                    else
                    {
                        containerAllocation.Add(ic, fit);
                    }
                    remaining -= fit;

                    if (remaining <= 0) { break; }
                }
            }

            [return: MaybeNull]
            static Item SpawnItemInContainer(ItemPrefab itemPrefab, ItemContainer container)
            {
                if (container?.Item is null) { return null; }

                Item item = new Item(itemPrefab, container.Item.Position, container.Item.Submarine);
                container.Inventory.TryPutItem(item, user: null);
                CargoManager.ItemSpawned(item);
#if SERVER
                Entity.Spawner?.CreateNetworkEvent(new EntitySpawner.SpawnEntity(item));
#endif
                return item;
            }
        }
    }
}