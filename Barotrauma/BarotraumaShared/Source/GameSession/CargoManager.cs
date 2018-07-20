using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class CargoManager
    {
        private readonly List<ItemPrefab> purchasedItems;

        private readonly CampaignMode campaign;

        public Action OnItemsChanged;

        public List<ItemPrefab> PurchasedItems
        {
            get { return purchasedItems; }
        }
        
        public CargoManager(CampaignMode campaign)
        {
            purchasedItems = new List<ItemPrefab>();
            this.campaign = campaign;
        }

        public void SetPurchasedItems(List<ItemPrefab> items)
        {
            purchasedItems.Clear();
            purchasedItems.AddRange(items);

            OnItemsChanged?.Invoke();
        }

        public void PurchaseItem(ItemPrefab item)
        {
            campaign.Money -= item.Price;
            purchasedItems.Add(item);

            OnItemsChanged?.Invoke();
        }

        public void SellItem(ItemPrefab item)
        {
            campaign.Money += item.Price;
            purchasedItems.Remove(item);

            OnItemsChanged?.Invoke();
        }

        public int GetTotalItemCost()
        {
            return purchasedItems.Sum(i => i.Price);
        }

        public void CreateItems()
        {
            CreateItems(purchasedItems);
            OnItemsChanged?.Invoke();
        }

        public static void CreateItems(List<ItemPrefab> itemsToSpawn)
        {
            WayPoint wp = WayPoint.GetRandom(SpawnType.Cargo, null, Submarine.MainSub);

            if (wp == null)
            {
                DebugConsole.ThrowError("The submarine must have a waypoint marked as Cargo for bought items to be placed correctly!");
                return;
            }

            Hull cargoRoom = Hull.FindHull(wp.WorldPosition);

            if (cargoRoom == null)
            {
                DebugConsole.ThrowError("A waypoint marked as Cargo must be placed inside a room!");
                return;
            }

            Dictionary<ItemContainer, int> availableContainers = new Dictionary<ItemContainer, int>();
            foreach (ItemPrefab prefab in itemsToSpawn)
            {
                Vector2 position = new Vector2(
                    Rand.Range(cargoRoom.Rect.X + 20, cargoRoom.Rect.Right - 20),
                    cargoRoom.Rect.Y - cargoRoom.Rect.Height + prefab.Size.Y / 2);

                ItemContainer itemContainer = null;
                if (!string.IsNullOrEmpty(prefab.CargoContainerName))
                {
                    itemContainer = availableContainers.Keys.ToList().Find(ac => 
                        ac.Item.Prefab.NameMatches(prefab.CargoContainerName) || 
                        ac.Item.Prefab.Tags.Contains(prefab.CargoContainerName.ToLowerInvariant()));

                    if (itemContainer == null)
                    {
                        var containerPrefab = MapEntityPrefab.List.Find(ep => 
                            ep.NameMatches(prefab.CargoContainerName) || 
                            (ep.Tags != null && ep.Tags.Contains(prefab.CargoContainerName.ToLowerInvariant()))) as ItemPrefab;

                        if (containerPrefab == null)
                        {
                            DebugConsole.ThrowError("Cargo spawning failed - could not find the item prefab for container \"" + containerPrefab.Name + "\"!");
                            continue;
                        }

                        Item containerItem = new Item(containerPrefab, position, wp.Submarine);
                        itemContainer = containerItem.GetComponent<ItemContainer>();
                        if (itemContainer == null)
                        {
                            DebugConsole.ThrowError("Cargo spawning failed - container \"" + containerItem.Name + "\" does not have an ItemContainer component!");
                            continue;
                        }
                        availableContainers.Add(itemContainer, itemContainer.Capacity);
                        if (GameMain.Server != null)
                        {
                            Entity.Spawner.CreateNetworkEvent(itemContainer.Item, false);
                        }
                    }                    
                }

                if (itemContainer == null)
                {
                    //no container, place at the waypoint
                    if (GameMain.Server != null)
                    {
                        Entity.Spawner.AddToSpawnQueue(prefab, position, wp.Submarine);
                    }
                    else
                    {
                        new Item(prefab, position, wp.Submarine);
                    }
                }
                else
                {
                    //place in the container
                    if (GameMain.Server != null)
                    {
                        Entity.Spawner.AddToSpawnQueue(prefab, itemContainer.Inventory);
                    }
                    else
                    {
                        var item = new Item(prefab, position, wp.Submarine);
                        itemContainer.Inventory.TryPutItem(item, null);
                    }

                    //reduce the number of available slots in the container
                    availableContainers[itemContainer]--;
                    if (availableContainers[itemContainer] <= 0)
                    {
                        availableContainers.Remove(itemContainer);
                    }
                }
            }

            itemsToSpawn.Clear();
        }
    }
}
