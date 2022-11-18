using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    static class AutoItemPlacer
    {
        public static bool OutputDebugInfo = false;

        public static void SpawnItems(Identifier? startItemSet = null)
        {
            if (GameMain.NetworkMember != null && !GameMain.NetworkMember.IsServer) { return; }

            //player has more than one sub = we must have given the start items already
            bool startItemsGiven = GameMain.GameSession?.OwnedSubmarines != null && GameMain.GameSession.OwnedSubmarines.Count > 1;
            if (!startItemsGiven)
            {
                for (int i = 0; i < Submarine.MainSubs.Length; i++)
                {
                    var sub = Submarine.MainSubs[i];
                    if (sub == null || sub.Info.InitialSuppliesSpawned || !sub.Info.IsPlayer) { continue; }
                    //1st pass: items defined in the start item set, only spawned in the main sub (not drones/shuttles or other linked subs)
                    SpawnStartItems(sub, startItemSet);
                    //2nd pass: items defined using preferred containers, spawned in the main sub and all the linked subs (drones, shuttles etc)
                    var subs = sub.GetConnectedSubs().Where(s => s.TeamID == sub.TeamID);
                    CreateAndPlace(subs);
                    subs.ForEach(s => s.Info.InitialSuppliesSpawned = true);
                    sub.CheckFuel();
                }
            }

            //spawn items in wrecks, beacon stations and pirate subs
            foreach (var sub in Submarine.Loaded)
            {
                if (sub.Info.Type == SubmarineType.Player || 
                    sub.Info.Type == SubmarineType.Outpost || 
                    sub.Info.Type == SubmarineType.OutpostModule)
                {
                    continue;
                }
                if (sub.Info.InitialSuppliesSpawned) { continue; }
                CreateAndPlace(sub.ToEnumerable());
                sub.Info.InitialSuppliesSpawned = true;
            }

            if (Level.Loaded?.StartOutpost != null && Level.Loaded.Type == LevelData.LevelType.Outpost)
            {
                var sub = Level.Loaded.StartOutpost;
                if (!sub.Info.InitialSuppliesSpawned)
                {
                    Rand.SetSyncedSeed(ToolBox.StringToInt(sub.Info.Name));
                    CreateAndPlace(sub.ToEnumerable());
                    sub.Info.InitialSuppliesSpawned = true;
                }
            }
        }

        public static void RegenerateLoot(Submarine sub, ItemContainer regeneratedContainer)
        {
            CreateAndPlace(sub.ToEnumerable(), regeneratedContainer: regeneratedContainer);
        }

        public static Identifier DefaultStartItemSet = new Identifier("normal");

        /// <summary>
        /// Spawns the items defined in the start item set in the specified sub.
        /// </summary>
        private static void SpawnStartItems(Submarine sub, Identifier? startItemSet)
        {
            Identifier setIdentifier = startItemSet ?? DefaultStartItemSet;
            if (!StartItemSet.Sets.TryGet(setIdentifier, out StartItemSet itemSet))
            {
                DebugConsole.AddWarning($"Couldn't find a start item set matching the identifier \"{setIdentifier}\"!");
                if (!StartItemSet.Sets.TryGet(DefaultStartItemSet, out StartItemSet defaultSet))
                {
                    DebugConsole.ThrowError($"Couldn't find the default start item set \"{DefaultStartItemSet}\"!");
                    return;
                }
                itemSet = defaultSet;
            }
            WayPoint wp = WayPoint.GetRandom(SpawnType.Cargo, null, sub);
            ISpatialEntity initialSpawnPos;
            if (wp?.CurrentHull == null)
            {
                var spawnHull = Hull.HullList.Where(h => h.Submarine == sub && !h.IsWetRoom).GetRandomUnsynced();
                if (spawnHull == null)
                {
                    DebugConsole.AddWarning($"Failed to spawn start items in the sub. No cargo waypoint or dry hulls found to spawn the items in.");
                    return;
                }
                initialSpawnPos = spawnHull;
            }
            else
            {
                initialSpawnPos = wp;
            }
            var newItems = new List<Item>();
            foreach (var startItem in itemSet.Items)
            {
                if (!ItemPrefab.Prefabs.TryGet(startItem.Item, out ItemPrefab itemPrefab))
                {
                    DebugConsole.AddWarning($"Cannot find a start item with with the identifier \"{startItem.Item}\"");
                    continue;
                }
                for (int i = 0; i < startItem.Amount; i++)
                {
                    var item = new Item(itemPrefab, initialSpawnPos.Position, sub, callOnItemLoaded: false);
                    // Is this necessary?
                    foreach (WifiComponent wifiComponent in item.GetComponents<WifiComponent>())
                    {
                        wifiComponent.TeamID = sub.TeamID;
                    }
                    newItems.Add(item);
                }
            }
            var cargoContainers = new List<ItemContainer>();
            foreach (var item in newItems)
            {
#if SERVER
                Entity.Spawner.CreateNetworkEvent(new EntitySpawner.SpawnEntity(item));
#endif
                foreach (ItemComponent ic in item.Components)
                {
                    ic.OnItemLoaded();
                }
                var container = sub.FindContainerFor(item, onlyPrimary: true);
                if (container == null)
                {
                    var cargoContainer = CargoManager.GetOrCreateCargoContainerFor(item.Prefab, initialSpawnPos, ref cargoContainers);
                    container = cargoContainer?.Item;
                }
                container?.OwnInventory.TryPutItem(item, user: null);
            }
        }

        private static void CreateAndPlace(IEnumerable<Submarine> subs, ItemContainer regeneratedContainer = null)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
            {
                DebugConsole.ThrowError("Clients are not allowed to use AutoItemPlacer.\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            List<Item> itemsToSpawn = new List<Item>(100);

            int itemCountApprox = MapEntityPrefab.List.Count() / 3;
            var containers = new List<ItemContainer>(70 + 30 * subs.Count());
            var prefabsItemsCanSpawnIn = new List<ItemPrefab>(itemCountApprox / 3);
            var singlePrefabs = new List<ItemPrefab>(itemCountApprox);
            var removals = new List<ItemPrefab>();

            // generate loot only for a specific container if defined
            if (regeneratedContainer != null)
            {
                containers.Add(regeneratedContainer);
            }
            else
            {
                foreach (Item item in Item.ItemList)
                {
                    if (!subs.Contains(item.Submarine)) { continue; }
                    if (item.GetRootInventoryOwner() is Character) { continue; }
                    if (item.NonInteractable) { continue; }
                    containers.AddRange(item.GetComponents<ItemContainer>());
                }
                containers.Shuffle(Rand.RandSync.ServerAndClient);
            }

            var itemPrefabs = ItemPrefab.Prefabs.OrderBy(p => p.UintIdentifier);
            foreach (ItemPrefab ip in itemPrefabs)
            {
                if (ip.PreferredContainers.None()) { continue; }
                if (ip.ConfigElement.Elements().Any(e => string.Equals(e.Name.ToString(), typeof(ItemContainer).Name.ToString(), StringComparison.OrdinalIgnoreCase)) && itemPrefabs.Any(ip2 => CanSpawnIn(ip2, ip)))
                {
                    prefabsItemsCanSpawnIn.Add(ip);
                }
                else
                {
                    singlePrefabs.Add(ip);
                }
            }

            bool CanSpawnIn(ItemPrefab item, ItemPrefab container)
            {
                foreach (var preferredContainer in item.PreferredContainers)
                {
                    if (ItemPrefab.IsContainerPreferred(preferredContainer.Primary, container.Identifier.ToEnumerable().Union(container.Tags))) { return true; }
                }
                return false;
            }

            var validContainers = new Dictionary<ItemContainer, PreferredContainer>();
            prefabsItemsCanSpawnIn.Shuffle(Rand.RandSync.ServerAndClient);
            // Spawn items that other items can spawn in first so we can fill them up with items if needed (oxygen tanks inside the spawned diving masks, etc)
            for (int i = 0; i < prefabsItemsCanSpawnIn.Count; i++)
            {
                var itemPrefab = prefabsItemsCanSpawnIn[i];
                if (itemPrefab == null) { continue; }
                SpawnItems(itemPrefab);
            }

            // Spawn items that nothing can spawn in last
            singlePrefabs.Shuffle(Rand.RandSync.ServerAndClient);
            singlePrefabs.ForEach(i => SpawnItems(i));

            if (OutputDebugInfo)
            {
                var subNames = subs.Select(s => s.Info.Name).ToList();
                DebugConsole.NewMessage($"Automatically placed items in { string.Join(", ", subNames) }:");
                foreach (string itemName in itemsToSpawn.Select(it => it.Name).Distinct())
                {
                    DebugConsole.NewMessage(" - " + itemName + " x" + itemsToSpawn.Count(it => it.Name == itemName));
                }
            }

            if (GameMain.GameSession?.Level != null &&
                GameMain.GameSession.Level.Type == LevelData.LevelType.Outpost &&
                GameMain.GameSession.StartLocation?.TakenItems != null)
            {
                foreach (Location.TakenItem takenItem in GameMain.GameSession.StartLocation.TakenItems)
                {
                    var matchingItem = itemsToSpawn.Find(it => takenItem.Matches(it));
                    if (matchingItem == null) { continue; }
                    if (OutputDebugInfo)
                    {
                        DebugConsole.NewMessage($"Removing the stolen item: {matchingItem.Prefab.Identifier} ({matchingItem.ID})");
                    }
                    var containedItems = itemsToSpawn.FindAll(it => it.ParentInventory?.Owner == matchingItem);
                    matchingItem.Remove();
                    itemsToSpawn.Remove(matchingItem);
                    foreach (Item containedItem in containedItems)
                    {
                        containedItem.Remove();
                        itemsToSpawn.Remove(containedItem);
                    }
                }
            }
            foreach (Item item in itemsToSpawn)
            {
#if SERVER
                Entity.Spawner.CreateNetworkEvent(new EntitySpawner.SpawnEntity(item));
#endif
                foreach (ItemComponent ic in item.Components)
                {
                    ic.OnItemLoaded();
                }
            }

            bool SpawnItems(ItemPrefab itemPrefab)
            {
                if (itemPrefab == null)
                {
                    string errorMsg = "Error in AutoItemPlacer.SpawnItems - itemPrefab was null.\n" + Environment.StackTrace.CleanupStackTrace();
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("AutoItemPlacer.SpawnItems:ItemNull", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                    return false;
                }
                bool success = false;
                bool isCampaign = GameMain.GameSession?.GameMode is CampaignMode;
                foreach (PreferredContainer preferredContainer in itemPrefab.PreferredContainers)
                {
                    if (preferredContainer.CampaignOnly && !isCampaign) { continue; }
                    if (preferredContainer.NotCampaign && isCampaign) { continue; }
                    if (preferredContainer.SpawnProbability <= 0.0f || preferredContainer.MaxAmount <= 0 && preferredContainer.Amount <= 0) { continue; }
                    validContainers = GetValidContainers(preferredContainer, containers, validContainers, primary: true);
                    if (validContainers.None())
                    {
                        validContainers = GetValidContainers(preferredContainer, containers, validContainers, primary: false);
                    }
                    foreach (var validContainer in validContainers)
                    {
                        var newItems = CreateItems(itemPrefab, containers, validContainer);
                        if (newItems.Any())
                        {
                            itemsToSpawn.AddRange(newItems);
                            success = true;
                        }
                    }
                }
                return success;
            }
        }

        private static Dictionary<ItemContainer, PreferredContainer> GetValidContainers(PreferredContainer preferredContainer, IEnumerable<ItemContainer> allContainers, Dictionary<ItemContainer, PreferredContainer> validContainers, bool primary)
        {
            validContainers.Clear();
            foreach (ItemContainer container in allContainers)
            {
                if (!container.AutoFill) { continue; }
                if (primary)
                {
                    if (!ItemPrefab.IsContainerPreferred(preferredContainer.Primary, container)) { continue; }
                }
                else
                {
                    if (!ItemPrefab.IsContainerPreferred(preferredContainer.Secondary, container)) { continue; }
                }
                if (!validContainers.ContainsKey(container))
                {
                    validContainers.Add(container, preferredContainer);
                }
            }
            return validContainers;
        }

        private static List<Item> CreateItems(ItemPrefab itemPrefab, List<ItemContainer> containers, KeyValuePair<ItemContainer, PreferredContainer> validContainer)
        {
            List<Item> newItems = new List<Item>();
            if (Rand.Value(Rand.RandSync.ServerAndClient) > validContainer.Value.SpawnProbability) { return newItems; }
            // Don't add dangerously reactive materials in thalamus wrecks 
            if (validContainer.Key.Item.Submarine.WreckAI != null && itemPrefab.Tags.Contains("explodesinwater"))
            {
                return newItems;
            }
            int amount = validContainer.Value.Amount;
            if (amount == 0)
            {
                amount = Rand.Range(validContainer.Value.MinAmount, validContainer.Value.MaxAmount + 1, Rand.RandSync.ServerAndClient);
            }
            for (int i = 0; i < amount; i++)
            {
                if (validContainer.Key.Inventory.IsFull(takeStacksIntoAccount: true))
                {
                    containers.Remove(validContainer.Key);
                    break;
                }
                var existingItem = validContainer.Key.Inventory.AllItems.FirstOrDefault(it => it.Prefab == itemPrefab);
                int quality = existingItem?.Quality ?? Quality.GetSpawnedItemQuality(validContainer.Key.Item.Submarine, Level.Loaded, Rand.RandSync.ServerAndClient);
                if (!validContainer.Key.Inventory.CanBePut(itemPrefab, quality: quality)) { break; }
                var item = new Item(itemPrefab, validContainer.Key.Item.Position, validContainer.Key.Item.Submarine, callOnItemLoaded: false)
                {
                    SpawnedInCurrentOutpost = validContainer.Key.Item.SpawnedInCurrentOutpost,
                    AllowStealing = validContainer.Key.Item.AllowStealing,
                    Quality = quality,
                    OriginalModuleIndex = validContainer.Key.Item.OriginalModuleIndex,
                    OriginalContainerIndex = 
                        Item.ItemList.Where(it => it.Submarine == validContainer.Key.Item.Submarine && it.OriginalModuleIndex == validContainer.Key.Item.OriginalModuleIndex).ToList().IndexOf(validContainer.Key.Item)
                };
                foreach (WifiComponent wifiComponent in item.GetComponents<WifiComponent>())
                {
                    wifiComponent.TeamID = validContainer.Key.Item.Submarine.TeamID;
                }
                newItems.Add(item);
                validContainer.Key.Inventory.TryPutItem(item, null, createNetworkEvent: false);
                containers.AddRange(item.GetComponents<ItemContainer>());
            }
            return newItems;
        }
    }
}
