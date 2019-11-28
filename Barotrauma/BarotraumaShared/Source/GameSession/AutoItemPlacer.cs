using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    static class AutoItemPlacer
    {
        private static readonly List<Item> spawnedItems = new List<Item>();

        public static void Place(IEnumerable<Submarine> subs)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
            {
                DebugConsole.ThrowError("Clients are not allowed to use AutoItemPlacer.\n" + Environment.StackTrace);
                return;
            }

            List<ItemContainer> containers = new List<ItemContainer>();
            foreach (Item item in Item.ItemList)
            {
                if (!subs.Contains(item.Submarine)) { continue; }
                containers.AddRange(item.GetComponents<ItemContainer>());
            }

            List<ItemPrefab> prefabsWithContainer = new List<ItemPrefab>();
            List<ItemPrefab> prefabsWithoutContainer = new List<ItemPrefab>();
            foreach (MapEntityPrefab prefab in MapEntityPrefab.List)
            {
                if (!(prefab is ItemPrefab ip)) { continue; }

                if (ip.ConfigElement.Elements().Any(e => e.Name.ToString().ToLower() == typeof(ItemContainer).Name.ToString().ToLower()))
                {
                    prefabsWithContainer.Add(ip);
                }
                else
                {
                    prefabsWithoutContainer.Add(ip);
                }
            }

            spawnedItems.Clear();
            Dictionary<ItemContainer, PreferredContainer> validContainers = new Dictionary<ItemContainer, PreferredContainer>();
            //spawn items that have an ItemContainer component first so we can fill them up with items if needed (oxygen tanks inside the spawned diving masks, etc)
            foreach (ItemPrefab itemPrefab in prefabsWithContainer.OrderBy(sp => Rand.Int(int.MaxValue)).Concat(prefabsWithoutContainer.OrderBy(sp => Rand.Int(int.MaxValue))))
            {
                GetValidContainers(itemPrefab, containers, ref validContainers);
                foreach (KeyValuePair<ItemContainer, PreferredContainer> validContainer in validContainers)
                {
                    SpawnItem(itemPrefab, containers, validContainer);
                }
            }

            DebugConsole.NewMessage("Automatically placed items: ");
            foreach (string itemName in spawnedItems.Select(it => it.Name).Distinct())
            {
                DebugConsole.NewMessage(" - " + itemName + " x" + spawnedItems.Count(it => it.Name == itemName));
            }
        }

        private static void GetValidContainers(ItemPrefab itemPrefab, IEnumerable<ItemContainer> allContainers, ref Dictionary<ItemContainer, PreferredContainer> validContainers)
        {
            //gather containers the item can spawn in
            validContainers.Clear();
            foreach (PreferredContainer preferredContainer in itemPrefab.PreferredContainers)
            {
                if (preferredContainer.SpawnProbability <= 0.0f && preferredContainer.MaxAmount <= 0) { continue; }
                foreach (ItemContainer container in allContainers)
                {
                    if (!preferredContainer.Identifiers.Any(id => container.Item.Prefab.Identifier == id || container.Item.HasTag(id))) { continue; }

                    if (validContainers.ContainsKey(container))
                    {
                        //the container has already been marked as valid due to some other PreferredContainer
                        // -> override if this PreferredContainer has a higher chance of spawning more of the item
                        // (for example, an item with a 5% chance to spawn in a cabinet and 50% chance to spawn in a supplycabinet
                        //  has a 50% chance to spawn in an item with both tags cabinet and supplycabinet)
                        if (GetSpawnPriority(preferredContainer) > GetSpawnPriority(validContainers[container]))
                        {
                            validContainers[container] = preferredContainer;
                        }
                    }
                    else
                    {
                        validContainers.Add(container, preferredContainer);
                    }
                }
            }
        }

        private static void SpawnItem(ItemPrefab itemPrefab, List<ItemContainer> containers, KeyValuePair<ItemContainer, PreferredContainer> validContainer)
        {
            if (Rand.Range(0.0f, 1.0f) > validContainer.Value.SpawnProbability) { return; }
            int amount = Rand.Range(validContainer.Value.MinAmount, validContainer.Value.MaxAmount + 1);
            for (int i = 0; i < amount; i++)
            {
                if (!validContainer.Key.Inventory.Items.Any(it => it == null))
                {
                    containers.Remove(validContainer.Key);
                    break;
                }

                var item = new Item(itemPrefab, validContainer.Key.Item.Position, validContainer.Key.Item.Submarine);
                spawnedItems.Add(item);
#if SERVER
                Entity.Spawner.CreateNetworkEvent(item, remove: false);
#endif
                validContainer.Key.Inventory.TryPutItem(item, null);
                containers.AddRange(item.GetComponents<ItemContainer>());
                
            }
        }


        private static float GetSpawnPriority(PreferredContainer container)
        {
            return Math.Max(container.SpawnProbability, 0.1f) * (container.MinAmount + container.MaxAmount) / 2.0f;
        }
    }
}
