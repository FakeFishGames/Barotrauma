using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    static class AutoItemPlacer
    {
        private static readonly List<Item> spawnedItems = new List<Item>();

        public static bool OutputDebugInfo = false;

        public static void PlaceIfNeeded(GameMode gameMode)
        {
            if (GameMain.NetworkMember != null && !GameMain.NetworkMember.IsServer) { return; }
            
            CampaignMode campaign = gameMode as CampaignMode;
            if (campaign == null || !campaign.InitialSuppliesSpawned)
            {
                for (int i = 0; i < Submarine.MainSubs.Length; i++)
                {
                    if (Submarine.MainSubs[i] == null) { continue; }
                    List<Submarine> subs = new List<Submarine>() { Submarine.MainSubs[i] };
                    subs.AddRange(Submarine.MainSubs[i].DockedTo.Where(d => !d.Info.IsOutpost));
                    Place(subs);
                }
                if (campaign != null) { campaign.InitialSuppliesSpawned = true; }
            }
            foreach (var wreck in Submarine.Loaded)
            {
                if (wreck.Info.IsWreck)
                {
                    Place(wreck.ToEnumerable());
                }
            }
        }

        private static void Place(IEnumerable<Submarine> subs)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
            {
                DebugConsole.ThrowError("Clients are not allowed to use AutoItemPlacer.\n" + Environment.StackTrace);
                return;
            }

            int itemCountApprox = MapEntityPrefab.List.Count() / 3;
            var containers = new List<ItemContainer>(70 + 30 * subs.Count());
            var prefabsWithContainer = new List<ItemPrefab>(itemCountApprox / 3);
            var prefabsWithoutContainer = new List<ItemPrefab>(itemCountApprox);
            var removals = new List<ItemPrefab>();

            foreach (Item item in Item.ItemList)
            {
                if (!subs.Contains(item.Submarine)) { continue; }
                containers.AddRange(item.GetComponents<ItemContainer>());
            }
            containers.Shuffle();

            foreach (MapEntityPrefab prefab in MapEntityPrefab.List)
            {
                if (!(prefab is ItemPrefab ip)) { continue; }

                if (ip.ConfigElement.Elements().Any(e => string.Equals(e.Name.ToString(), typeof(ItemContainer).Name.ToString(), StringComparison.OrdinalIgnoreCase)))
                {
                    prefabsWithContainer.Add(ip);
                }
                else
                {
                    prefabsWithoutContainer.Add(ip);
                }
            }

            spawnedItems.Clear();
            var validContainers = new Dictionary<ItemContainer, PreferredContainer>();
            prefabsWithContainer.Shuffle();
            // Spawn items that have an ItemContainer component first so we can fill them up with items if needed (oxygen tanks inside the spawned diving masks, etc)
            for (int i = 0; i < prefabsWithContainer.Count; i++)
            {
                var itemPrefab = prefabsWithContainer[i];
                if (itemPrefab == null) { continue; }
                if (SpawnItems(itemPrefab))
                {
                    removals.Add(itemPrefab);
                }
            }
            // Remove containers that we successfully spawned items into so that they are not counted in in the second pass.
            removals.ForEach(i => prefabsWithContainer.Remove(i));
            // Another pass for items with containers because also they can spawn inside other items (like smg magazine)
            prefabsWithContainer.ForEach(i => SpawnItems(i));
            // Spawn items that don't have containers last
            prefabsWithoutContainer.Shuffle();
            prefabsWithoutContainer.ForEach(i => SpawnItems(i));

            if (OutputDebugInfo)
            {
                var subNames = subs.Select(s => s.Info.Name).ToList();
                DebugConsole.NewMessage($"Automatically placed items in { string.Join(", ", subNames) }:");
                foreach (string itemName in spawnedItems.Select(it => it.Name).Distinct())
                {
                    DebugConsole.NewMessage(" - " + itemName + " x" + spawnedItems.Count(it => it.Name == itemName));
                }
            }

            bool SpawnItems(ItemPrefab itemPrefab)
            {
                if (itemPrefab == null)
                {
                    string errorMsg = "Error in AutoItemPlacer.SpawnItems - itemPrefab was null.\n"+Environment.StackTrace;
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("AutoItemPlacer.SpawnItems:ItemNull", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    return false;
                }
                bool success = false;
                foreach (PreferredContainer preferredContainer in itemPrefab.PreferredContainers)
                {
                    if (preferredContainer.SpawnProbability <= 0.0f || preferredContainer.MaxAmount <= 0) { continue; }
                    validContainers = GetValidContainers(preferredContainer, containers, validContainers, primary: true);
                    if (validContainers.None())
                    {
                        validContainers = GetValidContainers(preferredContainer, containers, validContainers, primary: false);
                    }
                    foreach (var validContainer in validContainers)
                    {
                        if (SpawnItem(itemPrefab, containers, validContainer))
                        {
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

        private static bool SpawnItem(ItemPrefab itemPrefab, List<ItemContainer> containers, KeyValuePair<ItemContainer, PreferredContainer> validContainer)
        {
            bool success = false;
            if (Rand.Value() > validContainer.Value.SpawnProbability) { return false; }
            // Don't add dangerously reactive materials in thalamus wrecks 
            if (validContainer.Key.Item.Submarine.WreckAI != null && itemPrefab.Tags.Contains("explodesinwater"))
            {
                return false;
            }
            int amount = Rand.Range(validContainer.Value.MinAmount, validContainer.Value.MaxAmount + 1);
            for (int i = 0; i < amount; i++)
            {
                if (validContainer.Key.Inventory.IsFull())
                {
                    containers.Remove(validContainer.Key);
                    break;
                }

                var item = new Item(itemPrefab, validContainer.Key.Item.Position, validContainer.Key.Item.Submarine);
                foreach (WifiComponent wifiComponent in item.GetComponents<WifiComponent>())
                {
                    wifiComponent.TeamID = validContainer.Key.Item.Submarine.TeamID;
                }
                spawnedItems.Add(item);
#if SERVER
                Entity.Spawner.CreateNetworkEvent(item, remove: false);
#endif
                validContainer.Key.Inventory.TryPutItem(item, null);
                containers.AddRange(item.GetComponents<ItemContainer>());
                success = true;
            }
            return success;
        }
    }
}
