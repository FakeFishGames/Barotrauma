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

            foreach (ItemPrefab prefab in purchasedItems)
            {
                Vector2 position = new Vector2(
                    Rand.Range(cargoRoom.Rect.X + 20, cargoRoom.Rect.Right - 20),
                    cargoRoom.Rect.Y - cargoRoom.Rect.Height + prefab.Size.Y/2);

                if (GameMain.Server != null)
                {
                    Entity.Spawner.AddToSpawnQueue(prefab, position, wp.Submarine);
                }
                else
                {
                    new Item(prefab, position, wp.Submarine);
                }

            }

            purchasedItems.Clear();
        }
    }
}
