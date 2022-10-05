using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
    class PurchasedItem
    {
        public ItemPrefab ItemPrefab => ItemPrefab.Prefabs[ItemPrefabIdentifier];
        public Identifier ItemPrefabIdentifier { get; }
        
        public int Quantity { get; set; }
        public bool? IsStoreComponentEnabled { get; set; }

        public readonly int BuyerCharacterInfoIdentifier;

        public PurchasedItem(ItemPrefab itemPrefab, int quantity, int buyerCharacterInfoId)
        {
            ItemPrefabIdentifier = itemPrefab.Identifier;
            Quantity = quantity;
            IsStoreComponentEnabled = null;
            BuyerCharacterInfoIdentifier = buyerCharacterInfoId;
        }

#if CLIENT
        public PurchasedItem(ItemPrefab itemPrefab, int quantity)
            : this(itemPrefab, quantity, buyer: null) { }
#endif
        public PurchasedItem(ItemPrefab itemPrefab, int quantity, Client buyer)
            : this(itemPrefab.Identifier, quantity, buyer) { }
        
        public PurchasedItem(Identifier itemPrefabId, int quantity, Client buyer)
        {
            ItemPrefabIdentifier = itemPrefabId;
            Quantity = quantity;
            IsStoreComponentEnabled = null;
            BuyerCharacterInfoIdentifier = buyer?.Character?.Info?.GetIdentifier() ?? Character.Controlled?.Info?.GetIdentifier() ?? 0;
        }

        public override string ToString()
        {
            return $"{ItemPrefab.Name} ({Quantity})";
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
                DebugConsole.LogError("Error setting SoldItem.ID: ID has already been set and should not be changed.");
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
                    DebugConsole.LogError($"Trying to set SoldEntity.Item, but it's already set!\n{Environment.StackTrace.CleanupStackTrace()}");
                    return;
                }
                Item = item;
            }
        }

        public const int MaxQuantity = 100;

        public Dictionary<Identifier, List<PurchasedItem>> ItemsInBuyCrate { get; } = new Dictionary<Identifier, List<PurchasedItem>>();
        public Dictionary<Identifier, List<PurchasedItem>> ItemsInSellCrate { get; } = new Dictionary<Identifier, List<PurchasedItem>>();
        public Dictionary<Identifier, List<PurchasedItem>> ItemsInSellFromSubCrate { get; } = new Dictionary<Identifier, List<PurchasedItem>>();
        public Dictionary<Identifier, List<PurchasedItem>> PurchasedItems { get; } = new Dictionary<Identifier, List<PurchasedItem>>();
        public Dictionary<Identifier, List<SoldItem>> SoldItems { get; } = new Dictionary<Identifier, List<SoldItem>>();

        private readonly CampaignMode campaign;

        private Location Location => campaign?.Map?.CurrentLocation;

        public readonly NamedEvent<CargoManager> OnItemsInBuyCrateChanged = new NamedEvent<CargoManager>();
        public readonly NamedEvent<CargoManager> OnItemsInSellCrateChanged = new NamedEvent<CargoManager>();
        public readonly NamedEvent<CargoManager> OnItemsInSellFromSubCrateChanged = new NamedEvent<CargoManager>();
        public readonly NamedEvent<CargoManager> OnPurchasedItemsChanged = new NamedEvent<CargoManager>();
        public readonly NamedEvent<CargoManager> OnSoldItemsChanged = new NamedEvent<CargoManager>();

        public CargoManager(CampaignMode campaign)
        {
            this.campaign = campaign;
        }

        private List<T> GetItems<T>(Identifier identifier, Dictionary<Identifier, List<T>> items, bool create = false)
        {
            if (items.TryGetValue(identifier, out var storeSpecificItems) && storeSpecificItems != null)
            {
                return storeSpecificItems;
            }
            else if (create)
            {
                storeSpecificItems = new List<T>();
                items.Add(identifier, storeSpecificItems);
                return storeSpecificItems;
            }
            else
            {
                return new List<T>();
            }
        }

        public List<PurchasedItem> GetBuyCrateItems(Identifier identifier, bool create = false) => GetItems(identifier, ItemsInBuyCrate, create);

        public List<PurchasedItem> GetBuyCrateItems(Location.StoreInfo store, bool create = false) => GetBuyCrateItems(store?.Identifier ?? Identifier.Empty, create);

        public PurchasedItem GetBuyCrateItem(Identifier identifier, ItemPrefab prefab) => GetBuyCrateItems(identifier)?.FirstOrDefault(i => i.ItemPrefab == prefab);

        public PurchasedItem GetBuyCrateItem(Location.StoreInfo store, ItemPrefab prefab) => GetBuyCrateItem(store?.Identifier ?? Identifier.Empty, prefab);

        public List<PurchasedItem> GetSellCrateItems(Identifier identifier, bool create = false) => GetItems(identifier, ItemsInSellCrate, create);

        public List<PurchasedItem> GetSellCrateItems(Location.StoreInfo store, bool create = false) => GetSellCrateItems(store?.Identifier ?? Identifier.Empty, create);

        public PurchasedItem GetSellCrateItem(Identifier identifier, ItemPrefab prefab) => GetSellCrateItems(identifier)?.FirstOrDefault(i => i.ItemPrefab == prefab);

        public PurchasedItem GetSellCrateItem(Location.StoreInfo store, ItemPrefab prefab) => GetSellCrateItem(store?.Identifier ?? Identifier.Empty, prefab);

        public List<PurchasedItem> GetSubCrateItems(Identifier identifier, bool create = false) => GetItems(identifier, ItemsInSellFromSubCrate, create);

        public List<PurchasedItem> GetSubCrateItems(Location.StoreInfo store, bool create = false) => GetSubCrateItems(store?.Identifier ?? Identifier.Empty, create);

        public PurchasedItem GetSubCrateItem(Identifier identifier, ItemPrefab prefab) => GetSubCrateItems(identifier)?.FirstOrDefault(i => i.ItemPrefab == prefab);

        public PurchasedItem GetSubCrateItem(Location.StoreInfo store, ItemPrefab prefab) => GetSubCrateItem(store?.Identifier ?? Identifier.Empty, prefab);

        public List<PurchasedItem> GetPurchasedItems(Identifier identifier, bool create = false) => GetItems(identifier, PurchasedItems, create);

        public List<PurchasedItem> GetPurchasedItems(Location.StoreInfo store, bool create = false) => GetPurchasedItems(store?.Identifier ?? Identifier.Empty, create);

        public PurchasedItem GetPurchasedItem(Identifier identifier, ItemPrefab prefab) => GetPurchasedItems(identifier)?.FirstOrDefault(i => i.ItemPrefab == prefab);

        public PurchasedItem GetPurchasedItem(Location.StoreInfo store, ItemPrefab prefab) => GetPurchasedItem(store?.Identifier ?? Identifier.Empty, prefab);

        public List<SoldItem> GetSoldItems(Identifier identifier, bool create = false) => GetItems(identifier, SoldItems, create);

        public List<SoldItem> GetSoldItems(Location.StoreInfo store, bool create = false) => GetSoldItems(store?.Identifier ?? Identifier.Empty, create);

        public void ClearItemsInBuyCrate()
        {
            ItemsInBuyCrate.Clear();
            OnItemsInBuyCrateChanged?.Invoke(this);
        }

        public void ClearItemsInSellCrate()
        {
            ItemsInSellCrate.Clear();
            OnItemsInSellCrateChanged?.Invoke(this);
        }

        public void ClearItemsInSellFromSubCrate()
        {
            ItemsInSellFromSubCrate.Clear();
            OnItemsInSellFromSubCrateChanged?.Invoke(this);
        }

        public void SetPurchasedItems(Dictionary<Identifier, List<PurchasedItem>> purchasedItems)
        {
            if (purchasedItems.Count == 0 && PurchasedItems.Count == 0) { return; }
            PurchasedItems.Clear();
            foreach (var entry in purchasedItems)
            {
                PurchasedItems.Add(entry.Key, entry.Value);
            }
            OnPurchasedItemsChanged?.Invoke(this);
        }

        public void ModifyItemQuantityInBuyCrate(Identifier storeIdentifier, ItemPrefab itemPrefab, int changeInQuantity, Client client = null)
        {
            if (GetBuyCrateItem(storeIdentifier, itemPrefab) is { } item)
            {
                item.Quantity += changeInQuantity;
                if (item.Quantity < 1)
                {
                    GetBuyCrateItems(storeIdentifier, create: true).Remove(item);
                }
            }
            else if (changeInQuantity > 0)
            {
                GetBuyCrateItems(storeIdentifier, create: true).Add(new PurchasedItem(itemPrefab, changeInQuantity, client));
            }
            OnItemsInBuyCrateChanged?.Invoke(this);
        }

        public void ModifyItemQuantityInSubSellCrate(Identifier storeIdentifier, ItemPrefab itemPrefab, int changeInQuantity, Client client = null)
        {
            if (GetSubCrateItem(storeIdentifier, itemPrefab) is { } item)
            {
                item.Quantity += changeInQuantity;
                if (item.Quantity < 1)
                {
                    GetSubCrateItems(storeIdentifier)?.Remove(item);
                }
            }
            else if (changeInQuantity > 0)
            {
                GetSubCrateItems(storeIdentifier, create: true).Add(new PurchasedItem(itemPrefab, changeInQuantity, client));
            }
            OnItemsInSellFromSubCrateChanged?.Invoke(this);
        }

#if SERVER
        public void OnNewItemsPurchased(Identifier storeIdentifier, List<PurchasedItem> newItems, Client client)
        {
            StringBuilder sb = new StringBuilder();
            int price = 0;
            Dictionary<ItemPrefab, int> buyValues = GetBuyValuesAtCurrentLocation(storeIdentifier, newItems.Select(i => i.ItemPrefab));
            foreach (PurchasedItem item in newItems)
            {
                int itemValue = item.Quantity * buyValues[item.ItemPrefab];
                GameAnalyticsManager.AddMoneySpentEvent(itemValue, GameAnalyticsManager.MoneySink.Store, item.ItemPrefab.Identifier.Value);
                sb.Append($"\n - {item.ItemPrefab.Name} x{item.Quantity}");
                price += itemValue;
            }
            GameServer.Log($"{NetworkMember.ClientLogName(client, client?.Name ?? "Unknown")} purchased {newItems.Count} item(s) for {TextManager.FormatCurrency(price)}{sb.ToString()}", ServerLog.MessageType.Money);
        }
#endif

        public void PurchaseItems(Identifier storeIdentifier, List<PurchasedItem> itemsToPurchase, bool removeFromCrate, Client client = null)
        {
            var store = Location.GetStore(storeIdentifier);
            if (store == null) { return; }
            var itemsPurchasedFromStore = GetPurchasedItems(storeIdentifier, create: true);
            // Check all the prices before starting the transaction to make sure the modifiers stay the same for the whole transaction
            var buyValues = GetBuyValuesAtCurrentLocation(storeIdentifier, itemsToPurchase.Select(i => i.ItemPrefab));
            var itemsInStoreCrate = GetBuyCrateItems(storeIdentifier, create: true);
            foreach (PurchasedItem item in itemsToPurchase)
            {
                // Exchange money
                int itemValue = item.Quantity * buyValues[item.ItemPrefab];
                if (!campaign.TryPurchase(client, itemValue)) { continue; }

                // Add to the purchased items
                var purchasedItem = itemsPurchasedFromStore.Find(pi => pi.ItemPrefab == item.ItemPrefab);
                if (purchasedItem != null)
                {
                    purchasedItem.Quantity += item.Quantity;
                }
                else
                {
                    purchasedItem = new PurchasedItem(item.ItemPrefab, item.Quantity, client);
                    itemsPurchasedFromStore.Add(purchasedItem);
                }
                if (GameMain.IsSingleplayer)
                {
                    GameAnalyticsManager.AddMoneySpentEvent(itemValue, GameAnalyticsManager.MoneySink.Store, item.ItemPrefab.Identifier.Value);
                }
                store.Balance += itemValue;
                if (removeFromCrate)
                {
                    // Remove from the shopping crate
                    if (itemsInStoreCrate.Find(pi => pi.ItemPrefab == item.ItemPrefab) is { } crateItem)
                    {
                        crateItem.Quantity -= item.Quantity;
                        if (crateItem.Quantity < 1) { itemsInStoreCrate.Remove(crateItem); }
                    }
                }
            }
            OnPurchasedItemsChanged?.Invoke(this);
        }

        public Dictionary<ItemPrefab, int> GetBuyValuesAtCurrentLocation(Identifier storeIdentifier, IEnumerable<ItemPrefab> items)
        {
            var buyValues = new Dictionary<ItemPrefab, int>();
            var store = Location?.GetStore(storeIdentifier);
            if (store == null) { return buyValues; }
            foreach (var item in items)
            {
                if (item == null) { continue; }
                if (!buyValues.ContainsKey(item))
                {
                    int buyValue = store?.GetAdjustedItemBuyPrice(item) ?? 0;
                    buyValues.Add(item, buyValue);
                }
            }
            return buyValues;
        }

        public Dictionary<ItemPrefab, int> GetSellValuesAtCurrentLocation(Identifier storeIdentifier, IEnumerable<ItemPrefab> items)
        {
            var sellValues = new Dictionary<ItemPrefab, int>();
            var store = Location?.GetStore(storeIdentifier);
            if (store == null) { return sellValues; }
            foreach (var item in items)
            {
                if (item == null) { continue; }
                if (!sellValues.ContainsKey(item))
                {
                    int sellValue = store?.GetAdjustedItemSellPrice(item) ?? 0;
                    sellValues.Add(item, sellValue);
                }
            }
            return sellValues;
        }

        public void CreatePurchasedItems()
        {
            purchasedIDCards.Clear();
            var items = new List<PurchasedItem>();
            foreach (var storeSpecificItems in PurchasedItems)
            {
                items.AddRange(storeSpecificItems.Value);
            }
            CreateItems(items, Submarine.MainSub, this);
            PurchasedItems.Clear();
            OnPurchasedItemsChanged?.Invoke(this);
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
                if (item.GetRootContainer() is Item rootContainer && rootContainer.HasTag("dontsellitems")) { return false; }
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
            if (confirmedItems != null && confirmedItems.Any(ci => ci.Item == item)) { return false; }
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
                if (confirmedItems != null)
                {
                    // Otherwise there must be no contained items or the contained items must be confirmed as sold
                    if (!containedItems.All(it => confirmedItems.Any(ci => ci.Item == it))) { return false; }
                }
            }
            return true;
        }

        public static ItemContainer GetOrCreateCargoContainerFor(ItemPrefab item, ISpatialEntity cargoRoomOrSpawnPoint, ref List<ItemContainer> availableContainers)
        {
            ItemContainer itemContainer = null;
            if (!string.IsNullOrEmpty(item.CargoContainerIdentifier))
            {
                itemContainer = availableContainers.Find(ac =>
                    ac.Inventory.CanBePut(item) &&
                    (ac.Item.Prefab.Identifier == item.CargoContainerIdentifier ||
                    ac.Item.Prefab.Tags.Contains(item.CargoContainerIdentifier)));

                if (itemContainer == null)
                {
                    ItemPrefab containerPrefab = ItemPrefab.Prefabs.Find(ep =>
                        ep.Identifier == item.CargoContainerIdentifier ||
                        (ep.Tags != null && ep.Tags.Contains(item.CargoContainerIdentifier)));

                    if (containerPrefab == null)
                    {
                        DebugConsole.AddWarning($"CargoManager: could not find the item prefab for container {item.CargoContainerIdentifier}!");
                        return null;
                    }

                    Vector2 containerPosition = cargoRoomOrSpawnPoint is Hull cargoRoom ?  GetCargoPos(cargoRoom, containerPrefab) : cargoRoomOrSpawnPoint.Position;
                    Item containerItem = new Item(containerPrefab, containerPosition, cargoRoomOrSpawnPoint.Submarine);
                    itemContainer = containerItem.GetComponent<ItemContainer>();
                    if (itemContainer == null)
                    {
                        DebugConsole.AddWarning($"CargoManager: No ItemContainer component found in {containerItem.Prefab.Identifier}!");
                        return null;
                    }
                    availableContainers.Add(itemContainer);
#if SERVER
                    if (GameMain.Server != null)
                    {
                        Entity.Spawner.CreateNetworkEvent(new EntitySpawner.SpawnEntity(itemContainer.Item));
                    }
#endif
                }
            }
            return itemContainer;
        }

        public static void CreateItems(List<PurchasedItem> itemsToSpawn, Submarine sub, CargoManager cargoManager)
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
                new GUIMessageBox("",
                    TextManager.GetWithVariable("CargoSpawnNotification",
                        "[roomname]",
                        cargoRoom.DisplayName,
                        FormatCapitals.Yes),
                    Array.Empty<LocalizedString>(),
                    type: GUIMessageBox.Type.InGame,
                    iconStyle: "StoreShoppingCrateIcon");
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
            foreach (PurchasedItem pi in itemsToSpawn)
            {
                Vector2 position = GetCargoPos(cargoRoom, pi.ItemPrefab);

                for (int i = 0; i < pi.Quantity; i++)
                {
                    var item = new Item(pi.ItemPrefab, position, wp.Submarine);
                    var itemContainer = GetOrCreateCargoContainerFor(pi.ItemPrefab, cargoRoom, ref availableContainers);
                    itemContainer?.Inventory.TryPutItem(item, null);
                    var idCard = item.GetComponent<IdCard>();
                    if (cargoManager != null && idCard != null && pi.BuyerCharacterInfoIdentifier != 0)
                    {
                        cargoManager.purchasedIDCards.Add((pi, idCard));
                    }
                    itemSpawned(pi, item);    
#if SERVER
                    Entity.Spawner?.CreateNetworkEvent(new EntitySpawner.SpawnEntity(item));
#endif
                    (itemContainer?.Item ?? item).CampaignInteractionType = CampaignMode.InteractionType.Cargo;    
                    static void itemSpawned(PurchasedItem purchased, Item item)
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

        private readonly List<(PurchasedItem purchaseInfo, IdCard idCard)> purchasedIDCards = new List<(PurchasedItem purchaseInfo, IdCard idCard)>();
        public void InitPurchasedIDCards()
        {
            foreach ((PurchasedItem purchased, IdCard idCard) in purchasedIDCards)
            {
                if (idCard != null && purchased.BuyerCharacterInfoIdentifier != 0)
                {
                    var owner = Character.CharacterList.Find(c => c.Info?.GetIdentifier() == purchased.BuyerCharacterInfoIdentifier);
                    if (owner?.Info != null)
                    {
                        var mainSubSpawnPoints = WayPoint.SelectCrewSpawnPoints(new List<CharacterInfo>() { owner.Info }, Submarine.MainSub);
                        idCard.Initialize(mainSubSpawnPoints.FirstOrDefault(), owner);
                    }
                }
            }
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
            foreach (var storeSpecificItems in PurchasedItems)
            {
                foreach (var item in storeSpecificItems.Value)
                {
                    if (item?.ItemPrefab == null) { continue; }
                    itemsElement.Add(new XElement("item",
                        new XAttribute("id", item.ItemPrefab.Identifier),
                        new XAttribute("qty", item.Quantity),
                        new XAttribute("storeid", storeSpecificItems.Key),
                        new XAttribute("buyer", item.BuyerCharacterInfoIdentifier)));
                }
            }
            parentElement.Add(itemsElement);
        }

        public void LoadPurchasedItems(XElement element)
        {
            var purchasedItems = new Dictionary<Identifier, List<PurchasedItem>>();
            if (element != null)
            {
                foreach (XElement itemElement in element.GetChildElements("item"))
                {
                    string prefabId = itemElement.GetAttributeString("id", null);
                    if (string.IsNullOrWhiteSpace(prefabId)) { continue; }
                    var prefab = ItemPrefab.Prefabs.Find(p => p.Identifier == prefabId);
                    if (prefab == null) { continue; }
                    int qty = itemElement.GetAttributeInt("qty", 0);
                    Identifier storeId = itemElement.GetAttributeIdentifier("storeid", "merchant");
                    int buyerId = itemElement.GetAttributeInt("buyer", 0);
                    if (!purchasedItems.TryGetValue(storeId, out var storeItems))
                    {
                        storeItems = new List<PurchasedItem>();
                        purchasedItems.Add(storeId, storeItems);
                    }
                    storeItems.Add(new PurchasedItem(prefab, qty, buyerId));
                }
            }
            SetPurchasedItems(purchasedItems);
        }
    }
}
