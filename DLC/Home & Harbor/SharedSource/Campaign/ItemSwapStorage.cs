using Barotrauma;
using Barotrauma.Networking;
using Barotrauma.Plugins;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace HomeAndHarbor
{
    /// <summary>
    /// Keeps track of outpost items that have been swapped out on the entire campaign.
    /// When swapping items, the old item gets put into this storage and the player
    /// can then swap it into another hardpoint for free.
    /// </summary>
    [RunConstructorOnInit]
    public class ItemSwapStorage
    {
        /// <summary>
        /// Used by the server to inform the client of changes to the item swap storage
        /// so the UI can correctly display when an item is free.
        /// </summary>
        /// <param name="Prefab">The target item prefab</param>
        /// <param name="Count">How many items are in the storage</param>
        [NetworkSerialize]
        public readonly record struct ItemSwapStorageData(Identifier Prefab, int Count) : INetSerializableStruct
        {
            public ItemPrefab? ItemPrefab => ItemPrefab.Prefabs.TryGet(Prefab, out ItemPrefab? prefab) ? prefab : null;
        }

        /// <summary>
        /// Used to sync the initial state of the item swap storage for clients.
        /// </summary>
        [NetworkSerialize]
        public readonly record struct ItemSwapStorageSyncData(ImmutableArray<ItemSwapStorageData> ItemSwapData) : INetSerializableStruct;

#if SERVER
        static ItemSwapStorage()
        {
            HookService.RegisterHook<PluginClientJoinedRoundDelegate>(SyncItemSwapStorage);
        }

        private static void SyncItemSwapStorage(Client client)
        {
            if (!ItemSwapStorage.TryGetItemSwapStorageForCurrentCampaign(out ItemSwapStorage? storage)) { return; }

            GameNetwork.SendToClient(client, Plugin.NetworkHeaders.SyncItemSwapStorage,
                new ItemSwapStorageSyncData(
                    storage.itemStorage
                        .Select(data => new ItemSwapStorageData(data.Key.Identifier, data.Value))
                        .ToImmutableArray()
                ));
        }
#elif CLIENT

        [NetworkHandler<Plugin.NetworkHeaders>(Plugin.NetworkHeaders.UpdateItemSwapStorage)]
        public static void UpdateItemSwapStorage(ItemSwapStorageData data)
        {
            if (!ItemSwapStorage.TryGetItemSwapStorageForCurrentCampaign(out ItemSwapStorage? storage)) { return; }

            if (data.ItemPrefab is not { } prefab) { return; }
            storage.SetItemCount(prefab, data.Count);
        }

        [NetworkHandler<Plugin.NetworkHeaders>(Plugin.NetworkHeaders.SyncItemSwapStorage)]
        public static void SyncItemSwapStorage(ItemSwapStorageSyncData data)
        {
            if (!ItemSwapStorage.TryGetItemSwapStorageForCurrentCampaign(out ItemSwapStorage? storage)) { return; }

            foreach (ItemSwapStorageData swapData in data.ItemSwapData)
            {
                UpdateItemSwapStorage(swapData);
            }
        }
#endif


        public const string ItemSwapStorageSaveElement = "ItemSwapStorage";

        private readonly Dictionary<ItemPrefab, int> itemStorage = new();

        public ItemSwapStorage(XElement? element)
        {
            if (element is null) { return; }

            foreach (XElement itemElement in element.Elements())
            {
                Identifier identifier = itemElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                if (identifier.IsEmpty) { continue; }

                if (!ItemPrefab.Prefabs.TryGet(identifier, out ItemPrefab? itemPrefab))
                {
                    DebugConsole.ThrowError($"ItemSwapStorage: Item prefab with identifier '{identifier}' not found.");
                    continue;
                }

                int count = itemElement.GetAttributeInt("count", 1);
                if (count <= 0) { continue; }

                itemStorage[itemPrefab] = count;
            }
        }

        public void TryDeductItem(ItemPrefab prefab)
        {
            if (itemStorage.TryGetValue(prefab, out int count) && count > 0)
            {
                int newCount = count - 1;
                if (newCount <= 0)
                {
                    itemStorage.Remove(prefab);
                }
                else
                {
                    itemStorage[prefab] = newCount;
                }

                BroadcastChangeToClient(prefab);
            }
        }

        public void StoreItem(ItemPrefab itemPrefab)
        {
            // TODO base the price on something else than this
            if (itemPrefab.SwappableItem is not { BasePrice: > 0 })
            {
                // Do not store items that are free to swap
                return;
            }

            int count = itemStorage.GetValueOrDefault(itemPrefab, 0);
            itemStorage[itemPrefab] = ++count;

            BroadcastChangeToClient(itemPrefab);
        }

        [Conditional("SERVER")]
        private void BroadcastChangeToClient(ItemPrefab prefab)
        {
#if SERVER
            GameNetwork.Broadcast(Plugin.NetworkHeaders.UpdateItemSwapStorage, new ItemSwapStorageData(prefab.Identifier, itemStorage.GetValueOrDefault(prefab)));
#endif
        }

        public int GetItemCount(ItemPrefab itemPrefab)
            => itemStorage.GetValueOrDefault(itemPrefab, 0);

        public void SetItemCount(ItemPrefab itemPrefab, int count)
        {
            if (count <= 0)
            {
                itemStorage.Remove(itemPrefab);
            }
            else
            {
                itemStorage[itemPrefab] = count;
            }
        }

        public XElement Save()
        {
            XElement element = new(ItemSwapStorageSaveElement);
            foreach (var (prefab, count) in itemStorage)
            {
                XElement itemElement = new("Item",
                                           new XAttribute("identifier", prefab.Identifier),
                                           new XAttribute("count", count));
                element.Add(itemElement);
            }

            return element;
        }

        public static bool TryGetItemSwapStorageForCurrentCampaign([NotNullWhen(true)] out ItemSwapStorage? storage)
            => TryGetItemSwapStorageForCampaign(GameMain.GameSession?.GameMode as CampaignMode, out storage);

        public static bool TryGetItemSwapStorageForCampaign(CampaignMode? campaignMode, [NotNullWhen(true)] out ItemSwapStorage? storage)
        {
            if (campaignMode is null)
            {
                storage = null;
                return false;
            }

            storage = campaignMode.GetOrAddExtraField(nameof(ItemSwapStorage), new ItemSwapStorage(null));
            return true;
        }
    }
}