using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;

namespace Barotrauma.PerkBehaviors
{
    internal class SpawnItemPerk : PerkBase
    {
        public SpawnItemPerk(ContentXElement element, DisembarkPerkPrefab prefab) : base(element, prefab) { }

        public override PerkSimulation Simulation
            => PerkSimulation.ServerOnly;

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
                    SpawnItem(matchingItems.GetRandomUnsynced(), amount: 1);
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
                SpawnItem(prefab, amount);
            }
          
            void SpawnItem(ItemPrefab prefab, int amount)
            {
                SuitableContainers suitableContainers = FindSuitableContainers(prefab, teamSubmarine);

                if (!suitableContainers.Any())
                {
                    SpawnItemInCrate(prefab, teamSubmarine, amount);
                    return;
                }
                SpawnInContainer(prefab, amount, suitableContainers, teamSubmarine);
            }
        }

        private readonly record struct SuitableContainers(
            ICollection<ItemContainer> PriorityContainers,
            ICollection<ItemContainer> PreferredContainers,
            ICollection<ItemContainer> SecondaryContainers)
        {
            public bool Any()
                => PriorityContainers.Count > 0
                   || PreferredContainers.Count > 0
                   || SecondaryContainers.Count > 0;
        }

        private SuitableContainers FindSuitableContainers(ItemPrefab prefab, Submarine submarine)
        {
            HashSet<ItemContainer> priorityContainers = new();
            HashSet<ItemContainer> primaryContainers = new();
            HashSet<ItemContainer> secondaryContainers = new();

            foreach (Item item in submarine.GetItems(alsoFromConnectedSubs: true))
            {
                if (item.GetComponent<Fabricator>() != null || item.GetComponent<Deconstructor>() != null) { continue; }
                if (item.NonInteractable || item.NonPlayerTeamInteractable || item.IsHidden) { continue; }

                if (item.GetComponent<ItemContainer>() is { } container)
                {
                    if (!container.CanBeContained(prefab)) { continue; }

                    var tags = item.GetTags();

                    if (!PriorityContainerTag.IsEmpty && (tags.Contains(PriorityContainerTag) || item.Prefab.Identifier == PriorityContainerTag))
                    {
                        priorityContainers.Add(container);
                        continue;
                    }

                    if (prefab.PreferredContainers.Any(pc => pc.Primary.Any(tags.Contains)))
                    {
                        primaryContainers.Add(container);
                        continue;
                    }

                    if (prefab.PreferredContainers.Any(pc => pc.Secondary.Any(tags.Contains)))
                    {
                        secondaryContainers.Add(container);
                    }
                }
            }

            return new SuitableContainers(priorityContainers, primaryContainers, secondaryContainers);
        }

        private static void SpawnItemInCrate(ItemPrefab prefab, Submarine submarine, int amount)
        {
            var purchasedItem = new PurchasedItem(prefab, amount, buyer: null);
            CargoManager.DeliverItemsToSub(new []{ purchasedItem }, submarine, cargoManager: null, showNotification: false);
        }

        private static void SpawnInContainer(ItemPrefab prefab, int amount, SuitableContainers containers, Submarine submarine)
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
                }
            }

            void TryAllocate(ICollection<ItemContainer> targetContainers)
                => AllocateContainers(prefab, targetContainers, ref remaining, ref containerAllocation);

            foreach (var (container, howManyToPut) in containerAllocation)
            {
                for (int i = 0; i < howManyToPut; i++)
                {
                    SpawnItem(prefab, container);
                }
            }

            if (remaining > 0)
            {
                SpawnItemInCrate(prefab, submarine, remaining);
            }

            static void AllocateContainers(ItemPrefab prefab, ICollection<ItemContainer> containers, ref int remaining, ref Dictionary<ItemContainer, int> containerAllocation)
            {
                foreach (ItemContainer ic in containers)
                {
                    int fit = ic.Inventory.HowManyCanBePut(prefab);
                    if (fit <= 0) { continue; }

                    fit = Math.Min(fit, remaining);

                    containerAllocation.Add(ic, fit);
                    remaining -= fit;

                    if (remaining <= 0) { break; }
                }
            }

            static void SpawnItem(ItemPrefab itemPrefab, ItemContainer container)
            {
                if (container?.Item is null) { return; }

                Item item = new Item(itemPrefab, container.Item.Position, container.Item.Submarine);
                container.Inventory.TryPutItem(item, user: null);
                CargoManager.ItemSpawned(item);
#if SERVER
                Entity.Spawner?.CreateNetworkEvent(new EntitySpawner.SpawnEntity(item));
#endif
            }
        }
    }
}