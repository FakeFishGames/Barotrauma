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
        public ushort ID { get; }
        public bool Removed { get; set; }
        public byte SellerID { get; }

        public SoldItem(ItemPrefab itemPrefab, ushort id, bool removed, byte sellerId)
        {
            ItemPrefab = itemPrefab;
            ID = id;
            Removed = removed;
            SellerID = sellerId;
        }
    }

    partial class CargoManager
    {
        public const int MaxQuantity = 100;

        public List<PurchasedItem> ItemsInBuyCrate { get; } = new List<PurchasedItem>();
        public List<PurchasedItem> ItemsInSellCrate { get; } = new List<PurchasedItem>();
        public List<PurchasedItem> PurchasedItems { get; } = new List<PurchasedItem>();
        public List<SoldItem> SoldItems { get; } = new List<SoldItem>();

        private readonly CampaignMode campaign;

        private Location Location => campaign?.Map?.CurrentLocation;

        public Action OnItemsInBuyCrateChanged;
        public Action OnItemsInSellCrateChanged;
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

        public void SetPurchasedItems(List<PurchasedItem> items)
        {
            PurchasedItems.Clear();
            PurchasedItems.AddRange(items);
            OnPurchasedItemsChanged?.Invoke();
        }

        public void ModifyItemQuantityInBuyCrate(ItemPrefab itemPrefab, int changeInQuantity)
        {
            PurchasedItem itemInCrate = ItemsInBuyCrate.Find(i => i.ItemPrefab == itemPrefab);
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

        public void PurchaseItems(List<PurchasedItem> itemsToPurchase, bool removeFromCrate)
        {
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
                var itemValue = GetBuyValueAtCurrentLocation(item);
                campaign.Money -= itemValue;
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

        public int GetBuyValueAtCurrentLocation(PurchasedItem item) => item?.ItemPrefab != null && Location != null ?
            item.Quantity * Location.GetAdjustedItemBuyPrice(item.ItemPrefab) : 0;

        public int GetSellValueAtCurrentLocation(ItemPrefab itemPrefab, int quantity = 1) => itemPrefab != null && Location != null ?
            quantity * Location.GetAdjustedItemSellPrice(itemPrefab) : 0;

        public void CreatePurchasedItems()
        {
            CreateItems(PurchasedItems);
            OnPurchasedItemsChanged?.Invoke();
        }

        public static void CreateItems(List<PurchasedItem> itemsToSpawn)
        {
            if (itemsToSpawn.Count == 0) { return; }

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

#if CLIENT
            new GUIMessageBox("", TextManager.GetWithVariable("CargoSpawnNotification", "[roomname]", cargoRoom.DisplayName, true), new string[0], type: GUIMessageBox.Type.InGame, iconStyle: "StoreShoppingCrateIcon");
#else
            foreach (Client client in GameMain.Server.ConnectedClients)
            {
                ChatMessage msg = ChatMessage.Create("", $"CargoSpawnNotification~[roomname]=§{cargoRoom.RoomName}", ChatMessageType.ServerMessageBoxInGame, null);
                msg.IconStyle = "StoreShoppingCrateIcon";
                GameMain.Server.SendDirectChatMessage(msg, client);
            }
#endif

            Dictionary<ItemContainer, int> availableContainers = new Dictionary<ItemContainer, int>();
            ItemPrefab containerPrefab = null;
            foreach (PurchasedItem pi in itemsToSpawn)
            {
                float floorPos = cargoRoom.Rect.Y - cargoRoom.Rect.Height;

                Vector2 position = new Vector2(
                    cargoRoom.Rect.Width > 40 ? Rand.Range(cargoRoom.Rect.X + 20, cargoRoom.Rect.Right - 20) : cargoRoom.Rect.Center.X,
                    floorPos);

                //check where the actual floor structure is in case the bottom of the hull extends below it
                if (Submarine.PickBody(
                    ConvertUnits.ToSimUnits(new Vector2(position.X, cargoRoom.Rect.Y - cargoRoom.Rect.Height / 2)),
                    ConvertUnits.ToSimUnits(position),
                    collisionCategory: Physics.CollisionWall) != null)
                {
                    float floorStructurePos = ConvertUnits.ToDisplayUnits(Submarine.LastPickedPosition.Y);
                    if (floorStructurePos > floorPos)
                    {
                        floorPos = floorStructurePos;
                    }
                }
                position.Y = floorPos +  pi.ItemPrefab.Size.Y / 2;

                ItemContainer itemContainer = null;
                if (!string.IsNullOrEmpty(pi.ItemPrefab.CargoContainerIdentifier))
                {
                    itemContainer = availableContainers.Keys.ToList().Find(ac => 
                        ac.Item.Prefab.Identifier == pi.ItemPrefab.CargoContainerIdentifier || 
                        ac.Item.Prefab.Tags.Contains(pi.ItemPrefab.CargoContainerIdentifier.ToLowerInvariant()));

                    if (itemContainer == null)
                    {
                        containerPrefab = ItemPrefab.Prefabs.Find(ep => 
                            ep.Identifier == pi.ItemPrefab.CargoContainerIdentifier || 
                            (ep.Tags != null && ep.Tags.Contains(pi.ItemPrefab.CargoContainerIdentifier.ToLowerInvariant())));

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
#if SERVER
                        if (GameMain.Server != null)
                        {
                            Entity.Spawner.CreateNetworkEvent(itemContainer.Item, false);
                        }
#endif
                    }                    
                }
                for (int i = 0; i < pi.Quantity; i++)
                {
                    if (itemContainer == null)
                    {
                        //no container, place at the waypoint
                        if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                        {
                            Entity.Spawner.AddToSpawnQueue(pi.ItemPrefab, position, wp.Submarine, onSpawned: itemSpawned);
                        }
                        else
                        {
                            var item = new Item(pi.ItemPrefab, position, wp.Submarine);
                            itemSpawned(item);
                        }
                        continue;
                    }
                    //if the intial container has been removed due to it running out of space, add a new container
                    //of the same type and begin filling it
                    if (!availableContainers.ContainsKey(itemContainer))
                    {
                        Item containerItemOverFlow = new Item(containerPrefab, position, wp.Submarine);
                        itemContainer = containerItemOverFlow.GetComponent<ItemContainer>();
                        availableContainers.Add(itemContainer, itemContainer.Capacity);
#if SERVER
                        if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                        {
                            Entity.Spawner.CreateNetworkEvent(itemContainer.Item, false);
                        }
#endif
                    }

                    //place in the container
                    if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                    {
                        Entity.Spawner.AddToSpawnQueue(pi.ItemPrefab, itemContainer.Inventory, onSpawned: itemSpawned);
                    }
                    else
                    {
                        var item = new Item(pi.ItemPrefab, position, wp.Submarine);
                        itemContainer.Inventory.TryPutItem(item, null);
                        itemSpawned(item);
                    }

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

                    //reduce the number of available slots in the container
                    //if there is a container
                    if (availableContainers.ContainsKey(itemContainer))
                    {
                        availableContainers[itemContainer]--;
                    }
                    if (availableContainers.ContainsKey(itemContainer) && availableContainers[itemContainer] <= 0)
                    {
                        availableContainers.Remove(itemContainer);
                    }                    
                }
            }
            itemsToSpawn.Clear();
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
