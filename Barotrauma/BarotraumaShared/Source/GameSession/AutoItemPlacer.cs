using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    static class AutoItemPlacer
    {
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

            Dictionary<ItemContainer, int> placedItemCount = new Dictionary<ItemContainer, int>();
            Dictionary<ItemContainer, PreferredContainer> validContainers = new Dictionary<ItemContainer, PreferredContainer>();
            foreach (ItemPrefab itemPrefab in MapEntityPrefab.List.Where(mp => mp is ItemPrefab))
            {
                //gather containers the item can spawn in
                validContainers.Clear();
                foreach (PreferredContainer preferredContainer in itemPrefab.PreferredContainers)
                {
                    if (preferredContainer.SpawnProbability <= 0.0f && preferredContainer.MaxAmount <= 0) { continue; }
                    foreach (ItemContainer container in containers)
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

                foreach (KeyValuePair<ItemContainer, PreferredContainer> validContainer in validContainers)
                {
                    if (Rand.Range(0.0f, 1.0f) > validContainer.Value.SpawnProbability) { continue; }
                    int amount = Rand.Range(validContainer.Value.MinAmount, validContainer.Value.MaxAmount + 1);
                    for (int i = 0; i < amount; i++)
                    {
                        int placedCount = placedItemCount.ContainsKey(validContainer.Key) ? placedItemCount[validContainer.Key] : 0;
                        if (validContainer.Key.Inventory.Items.Count(it => it == null) - placedCount <= 0)
                        {
                            containers.Remove(validContainer.Key);
                            break;
                        }
                        if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                        {
                            Entity.Spawner.AddToSpawnQueue(itemPrefab, validContainer.Key.Inventory);
                            placedItemCount[validContainer.Key]++;
                        }
                        else
                        {
                            var item = new Item(itemPrefab, validContainer.Key.Item.Position, validContainer.Key.Item.Submarine);
                            validContainer.Key.Inventory.TryPutItem(item, null);
                        }
                    }
                }
            }

        }
        private static float GetSpawnPriority(PreferredContainer container)
        {
            return Math.Max(container.SpawnProbability, 0.1f) * (container.MinAmount + container.MaxAmount) / 2.0f;
        }
    }
}
