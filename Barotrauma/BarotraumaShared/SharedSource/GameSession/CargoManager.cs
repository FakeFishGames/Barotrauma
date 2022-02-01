using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
#if SERVER
using Barotrauma.Networking;
#endif

namespace Barotrauma
{
    class PurchasedItem
    {
        public ItemPrefab ItemPrefab { get; }
        public int Quantity { get; set; }

        public PurchasedItem(ItemPrefab itemPrefab, int quantity)
        {
            ItemPrefab = itemPrefab;
            Quantity = quantity;
        }
    }

    class SoldItem
    {
        public ItemPrefab ItemPrefab { get; }
        public ushort ID { get; private set; }
        public bool Removed { get; set; }
        public byte SellerID { get; }
        public SellOrigin Origin { get;  }

        public enum SellOrigin
        {
            Character,
            Submarine
        }

        public SoldItem(ItemPrefab itemPrefab, ushort id, bool removed, byte sellerId, SellOrigin origin)
        {
            ItemPrefab = itemPrefab;
            ID = id;
            Removed = removed;
            SellerID = sellerId;
            Origin = origin;
        }

        public void SetItemId(ushort id)
        {
            if (ID != Entity.NullEntityID)
            {
                DebugConsole.ShowError("Error setting SoldItem.ID: ID has already been set and should not be changed.");
                return;
            }
            ID = id;
        }
    }

    partial class CargoManager
    {
        private class SoldEntity
        {
            public enum SellStatus
            {
                /// <summary>
                /// Entity sold in SP. Or, entity sold by client and confirmed by server in MP.
                /// </summary>
                Confirmed,
                /// <summary>
                /// Entity sold by client in MP. Client has received at least one update from server after selling, but this entity wasn't yet confirmed.
                /// </summary>
                Unconfirmed,
                /// <summary>
                /// Entity sold by client in MP. Client hasn't yet received an update from server after selling.
                /// </summary>
                Local
            }

            public Item Item { get; private set; }
            public ItemPrefab ItemPrefab { get; }
            public SellStatus Status { get; set; }

            public SoldEntity(Item item, SellStatus status)
            {
                Item = item;
                ItemPrefab = item?.Prefab;
                Status = status;
            }

            public SoldEntity(ItemPrefab itemPrefab, SellStatus status)
            {
                ItemPrefab = itemPrefab;
                Status = status;
            }

            public void SetItem(Item item)
            {
                if (Item != null)
                {
                    DebugConsole.ShowError($"Trying to set SoldEntity.Item, but it's already set!\n{Environment.StackTrace.CleanupStackTrace()}");
                    return;
                }
                Item = item;
            }
        }

        public const int MaxQuantity = 100;

        public List<PurchasedItem> ItemsInBuyCrate { get; } = new List<PurchasedItem>();
        public List<PurchasedItem> ItemsInSellCrate { get; } = new List<PurchasedItem>();
        public List<PurchasedItem> ItemsInSellFromSubCrate { get; } = new List<PurchasedItem>();
        public List<PurchasedItem> PurchasedItems { get; } = new List<PurchasedItem>();
        public List<SoldItem> SoldItems { get; } = new List<SoldItem>();

        private readonly CampaignMode campaign;

        private Location Location => campaign?.Map?.CurrentLocation;

        public Action OnItemsInBuyCrateChanged;
        public Action OnItemsInSellCrateChanged;
        public Action OnItemsInSellFromSubCrateChanged;
        public Action OnPurchasedItemsChanged;
        public Action OnSoldItemsChanged;
        
        public CargoManager(CampaignMode campaign)
        {
            this.campaign = campaign;
        }

        public void ClearItemsInBuyCrate()
        {
            ItemsInBuyCrate.Clear();
            OnItemsInBuyCrateChanged?.Invoke();
        }

        public void ClearItemsInSellCrate()
        {
            ItemsInSellCrate.Clear();
            OnItemsInSellCrateChanged?.Invoke();
        }

        public void ClearItemsInSellFromSubCrate()
        {
            ItemsInSellFromSubCrate.Clear();
            OnItemsInSellFromSubCrateChanged?.Invoke();
        }

        public void SetPurchasedItems(List<PurchasedItem> items)
        {
            PurchasedItems.Clear();
            PurchasedItems.AddRange(items);
            OnPurchasedItemsChanged?.Invoke();
        }

        public void ModifyItemQuantityInBuyCrate(ItemPrefab itemPrefab, int changeInQuantity)
        {
            var itemInCrate = ItemsInBuyCrate.Find(i => i.ItemPrefab == itemPrefab);
            if (itemInCrate != null)
            {
                itemInCrate.Quantity += changeInQuantity;
                if (itemInCrate.Quantity < 1)
                {
                    ItemsInBuyCrate.Remove(itemInCrate);
                }
            }
            else if(changeInQuantity > 0)
            {
                itemInCrate = new PurchasedItem(itemPrefab, changeInQuantity);
                ItemsInBuyCrate.Add(itemInCrate);
            }
            OnItemsInBuyCrateChanged?.Invoke();
        }

        public void ModifyItemQuantityInSubSellCrate(ItemPrefab itemPrefab, int changeInQuantity)
        {
            var itemInCrate = ItemsInSellFromSubCrate.Find(i => i.ItemPrefab == itemPrefab);
            if (itemInCrate != null)
            {
                itemInCrate.Quantity += changeInQuantity;
                if (itemInCrate.Quantity < 1)
                {
                    ItemsInSellFromSubCrate.Remove(itemInCrate);
                }
            }
            else if (changeInQuantity > 0)
            {
                itemInCrate = new PurchasedItem(itemPrefab, changeInQuantity);
                ItemsInSellFromSubCrate.Add(itemInCrate);
            }
            OnItemsInSellFromSubCrateChanged?.Invoke();
        }

        public void PurchaseItems(List<PurchasedItem> itemsToPurchase, bool removeFromCrate)
        {
            // Check all the prices before starting the transaction
            // to make sure the modifiers stay the same for the whole transaction
            Dictionary<ItemPrefab, int> buyValues = GetBuyValuesAtCurrentLocation(itemsToPurchase.Select(i => i.ItemPrefab));

            foreach (PurchasedItem item in itemsToPurchase)
            {
                // Add to the purchased items
                var purchasedItem = PurchasedItems.Find(pi => pi.ItemPrefab == item.ItemPrefab);
                if (purchasedItem != null)
                {
                    purchasedItem.Quantity += item.Quantity;
                }
                else
                {
                    purchasedItem = new PurchasedItem(item.ItemPrefab, item.Quantity);
                    PurchasedItems.Add(purchasedItem);
                }

                // Exchange money
                var itemValue = item.Quantity * buyValues[item.ItemPrefab];
                campaign.Money -= itemValue;
                GameAnalyticsManager.AddMoneySpentEvent(itemValue, GameAnalyticsManager.MoneySink.Store, item.ItemPrefab.Identifier);
                Location.StoreCurrentBalance += itemValue;

                if (removeFromCrate)
                {
                    // Remove from the shopping crate
                    var crateItem = ItemsInBuyCrate.Find(pi => pi.ItemPrefab == item.ItemPrefab);
                    if (crateItem != null)
                    {
                        crateItem.Quantity -= item.Quantity;
                        if (crateItem.Quantity < 1) { ItemsInBuyCrate.Remove(crateItem); }
                    }
                }
            }
            OnPurchasedItemsChanged?.Invoke();
        }

        public Dictionary<ItemPrefab, int> GetBuyValuesAtCurrentLocation(IEnumerable<ItemPrefab> items)
        {
            var buyValues = new Dictionary<ItemPrefab, int>();
            foreach (var item in items)
            {
                if (item == null) { continue; }
                if (!buyValues.ContainsKey(item))
                {
                    var buyValue = Location?.GetAdjustedItemBuyPrice(item) ?? 0;
                    buyValues.Add(item, buyValue);
                }
            }
            return buyValues;
        }

        public Dictionary<ItemPrefab, int> GetSellValuesAtCurrentLocation(IEnumerable<ItemPrefab> items)
        {
            var sellValues = new Dictionary<ItemPrefab, int>();
            foreach (var item in items)
            {
                if (item == null) { continue; }
                if (!sellValues.ContainsKey(item))
                {
                    var sellValue = Location?.GetAdjustedItemSellPrice(item) ?? 0;
                    sellValues.Add(item, sellValue);
                }
            }
            return sellValues;
        }

        public void CreatePurchasedItems()
        {
            CreateItems(PurchasedItems, Submarine.MainSub);
            OnPurchasedItemsChanged?.Invoke();
        }

        private Dictionary<ItemPrefab, int> UndeterminedSoldEntities { get; } = new Dictionary<ItemPrefab, int>();

        public IEnumerable<Item> GetSellableItemsFromSub()
        {
            if (Submarine.MainSub == null) { return new List<Item>(); }
            var confirmedSoldEntities = Enumerable.Empty<SoldEntity>();
            UndeterminedSoldEntities.Clear();
#if CLIENT
            confirmedSoldEntities = GetConfirmedSoldEntities();
            foreach (var soldEntity in SoldEntities)
            {
                if (soldEntity.Item != null) { continue; }
                if (UndeterminedSoldEntities.TryGetValue(soldEntity.ItemPrefab, out int count))
                {
                    UndeterminedSoldEntities[soldEntity.ItemPrefab] = count + 1;
                }
                else
                {
                    UndeterminedSoldEntities.Add(soldEntity.ItemPrefab, 1);
                }
            }
#endif
            return Submarine.MainSub.GetItems(true).FindAll(item =>
            {
                if (!IsItemSellable(item, confirmedSoldEntities)) { return false; }
                if (item.GetRootInventoryOwner() is Character) { return false; }
                if (!item.Components.All(c => !(c is Holdable h) || !h.Attachable || !h.Attached)) { return false; }
                if (!item.Components.All(c => !(c is Wire w) || w.Connections.All(c => c == null))) { return false; }
                if (!ItemAndAllContainersInteractable(item)) { return false; }
                if (item.GetRootContainer() is Item rootContainer && rootContainer.HasTag("donttakeitems")) { return false; }
                return true;
            }).Distinct();

            static bool ItemAndAllContainersInteractable(Item item)
            {
                do
                {
                    if (!item.IsPlayerTeamInteractable) { return false; }
                    item = item.Container;
                } while (item != null);
                return true;
            }
        }

        private bool IsItemSellable(Item item, IEnumerable<SoldEntity> confirmedItems)
        {
            if (item.Removed) { return false; }
            if (!item.Prefab.CanBeSold) { return false; }
            if (item.SpawnedInCurrentOutpost) { return false; }
            if (!item.Prefab.AllowSellingWhenBroken && item.ConditionPercentage < 90.0f) { return false; }
            if (confirmedItems.Any(ci => ci.Item == item)) { return false; }
            if (UndeterminedSoldEntities.TryGetValue(item.Prefab, out int count))
            {
                int newCount = count - 1;
                if (newCount > 0)
                {
                    UndeterminedSoldEntities[item.Prefab] = newCount;
                }
                else
                {
                    UndeterminedSoldEntities.Remove(item.Prefab);
                }
                return false;
            }
            if (item.OwnInventory?.Container is ItemContainer itemContainer)
            {
                var containedItems = item.ContainedItems;
                if (containedItems.None()) { return true; }
                // Allow selling the item if contained items are unsellable and set to be removed on deconstruct
                if (itemContainer.RemoveContainedItemsOnDeconstruct && containedItems.All(it => !it.Prefab.CanBeSold)) { return true; }
                // Otherwise there must be no contained items or the contained items must be confirmed as sold
                if (!containedItems.All(it => confirmedItems.Any(ci => ci.Item == it))) { return false; }
            }
            return true;
        }

        public static void CreateItems(List<PurchasedItem> itemsToSpawn, Submarine sub)
        {
            if (itemsToSpawn.Count == 0) { return; }

            WayPoint wp = WayPoint.GetRandom(SpawnType.Cargo, null, sub);
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

            if (sub == Submarine.MainSub)
            {
#if CLIENT
                new GUIMessageBox("", TextManager.GetWithVariable("CargoSpawnNotification", "[roomname]", cargoRoom.DisplayName, true), new string[0], type: GUIMessageBox.Type.InGame, iconStyle: "StoreShoppingCrateIcon");
#else
                foreach (Client client in GameMain.Server.ConnectedClients)
                {
                    ChatMessage msg = ChatMessage.Create("",
                       TextManager.ContainsTag(cargoRoom.RoomName) ? $"CargoSpawnNotification~[roomname]=§{cargoRoom.RoomName}" : $"CargoSpawnNotification~[roomname]={cargoRoom.RoomName}", 
                       ChatMessageType.ServerMessageBoxInGame, null);
                    msg.IconStyle = "StoreShoppingCrateIcon";
                    GameMain.Server.SendDirectChatMessage(msg, client);
                }
#endif
            }

            List<ItemContainer> availableContainers = new List<ItemContainer>();
            ItemPrefab containerPrefab = null;
            foreach (PurchasedItem pi in itemsToSpawn)
            {
                Vector2 position = GetCargoPos(cargoRoom, pi.ItemPrefab);

                for (int i = 0; i < pi.Quantity; i++)
                {
                    ItemContainer itemContainer = null;
                    if (!string.IsNullOrEmpty(pi.ItemPrefab.CargoContainerIdentifier))
                    {
                        itemContainer = availableContainers.Find(ac => 
                            ac.Inventory.CanBePut(pi.ItemPrefab) &&
                            (ac.Item.Prefab.Identifier == pi.ItemPrefab.CargoContainerIdentifier || 
                            ac.Item.Prefab.Tags.Contains(pi.ItemPrefab.CargoContainerIdentifier.ToLowerInvariant())));

                        if (itemContainer == null)
                        {
                            containerPrefab = ItemPrefab.Prefabs.Find(ep => 
                                ep.Identifier == pi.ItemPrefab.CargoContainerIdentifier || 
                                (ep.Tags != null && ep.Tags.Contains(pi.ItemPrefab.CargoContainerIdentifier.ToLowerInvariant())));

                            if (containerPrefab == null)
                            {
                                DebugConsole.ThrowError("Cargo spawning failed - could not find the item prefab for container \"" + pi.ItemPrefab.CargoContainerIdentifier + "\"!");
                                continue;
                            }

                            Vector2 containerPosition = GetCargoPos(cargoRoom, containerPrefab);
                            Item containerItem = new Item(containerPrefab, containerPosition, wp.Submarine);
                            itemContainer = containerItem.GetComponent<ItemContainer>();
                            if (itemContainer == null)
                            {
                                DebugConsole.ThrowError("Cargo spawning failed - container \"" + containerItem.Name + "\" does not have an ItemContainer component!");
                                continue;
                            }
                            availableContainers.Add(itemContainer);
#if SERVER
                            if (GameMain.Server != null)
                            {
                                Entity.Spawner.CreateNetworkEvent(itemContainer.Item, false);
                            }
#endif
                        }
                    }

                    var item = new Item(pi.ItemPrefab, position, wp.Submarine);
                    itemContainer?.Inventory.TryPutItem(item, null);
                    itemSpawned(item);
#if SERVER
                    Entity.Spawner?.CreateNetworkEvent(item, false);
#endif
                    static void itemSpawned(Item item)
                    {
                        Submarine sub = item.Submarine ?? item.GetRootContainer()?.Submarine;
                        if (sub != null)
                        {
                            foreach (WifiComponent wifiComponent in item.GetComponents<WifiComponent>())
                            {
                                wifiComponent.TeamID = sub.TeamID;
                            }
                        }
                    }               
                }
            }
            itemsToSpawn.Clear();
        }

        public static Vector2 GetCargoPos(Hull hull, ItemPrefab itemPrefab)
        {
            float floorPos = hull.Rect.Y - hull.Rect.Height;

            Vector2 position = new Vector2(
                hull.Rect.Width > 40 ? Rand.Range(hull.Rect.X + 20f, hull.Rect.Right - 20f) : hull.Rect.Center.X,
                floorPos);

            //check where the actual floor structure is in case the bottom of the hull extends below it
            if (Submarine.PickBody(
                ConvertUnits.ToSimUnits(new Vector2(position.X, hull.Rect.Y - hull.Rect.Height / 2)),
                ConvertUnits.ToSimUnits(position),
                collisionCategory: Physics.CollisionWall) != null)
            {
                float floorStructurePos = ConvertUnits.ToDisplayUnits(Submarine.LastPickedPosition.Y);
                if (floorStructurePos > floorPos)
                {
                    floorPos = floorStructurePos;
                }
            }

            position.Y = floorPos + itemPrefab.Size.Y / 2;

            return position;
        }

        public void SavePurchasedItems(XElement parentElement)
        {
            var itemsElement = new XElement("cargo");
            foreach (PurchasedItem item in PurchasedItems)
            {
                if (item?.ItemPrefab == null) { continue; }
                itemsElement.Add(new XElement("item",
                    new XAttribute("id", item.ItemPrefab.Identifier),
                    new XAttribute("qty", item.Quantity)));
            }
            parentElement.Add(itemsElement);
        }

        public void LoadPurchasedItems(XElement element)
        {
            var purchasedItems = new List<PurchasedItem>();
            if (element != null)
            {
                foreach (XElement itemElement in element.GetChildElements("item"))
                {
                    var id = itemElement.GetAttributeString("id", null);
                    if (string.IsNullOrWhiteSpace(id)) { continue; }
                    var prefab = ItemPrefab.Prefabs.Find(p => p.Identifier == id);
                    if (prefab == null) { continue; }
                    var qty = itemElement.GetAttributeInt("qty", 0);
                    purchasedItems.Add(new PurchasedItem(prefab, qty));
                }
            }
            SetPurchasedItems(purchasedItems);
        }
    }
}
