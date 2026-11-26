using Barotrauma;
using Barotrauma.Plugins;
using System.Xml.Linq;

namespace HomeAndHarbor
{
    /// <summary>
    /// Custom store implementation for workstation storefronts to control item selection and discounts.
    /// </summary>
    [RunConstructorOnInit]
    public class WorkstationStoreInfo : Location.StoreInfo
    {
        public static readonly Identifier StoreType = "workstation".ToIdentifier();

        public override Identifier CustomStoreType => StoreType;

        /// <summary>
        /// The storefront only exists while the players own the outpost. If the outpost is ever lost, the original merchants take over again.
        /// </summary>
        public override bool KeepWhenStoresRecreated => PersistentOutpostData.GetPersistentOutpostData(Location) is { IsPlayerOwned: true };

        public override LocalizedString DisplayName => TextManager.Get("dwellerjob.storekeeper");

        static WorkstationStoreInfo()
        {
            HookService.RegisterHook<PluginLoadStoreDelegate>(OnLoadStore);
        }

        /// <summary>
        /// Create a new store.
        /// </summary>
        public WorkstationStoreInfo(Location location, Identifier identifier) : base(location, identifier) { }

        /// <summary>
        /// Load a previously saved store.
        /// </summary>
        public WorkstationStoreInfo(Location location, XElement storeElement) : base(location, storeElement) { }

        private static Location.StoreInfo? OnLoadStore(Location location, XElement storeElement)
        {
            if (storeElement.GetAttributeIdentifier(nameof(CustomStoreType), Identifier.Empty) != StoreType) { return null; }
            return new WorkstationStoreInfo(location, storeElement);
        }

        /// <summary>
        /// Makes sure the location has a storefront, by creating one or converting the normal store that's there, keeping its stock.
        /// </summary>
        /// <param name="identifier">See <see cref="WorkstationComponent.StoreIdentifier"/>.</param>
        public static void CreateOrConvert(Location location, Identifier identifier)
        {
            // Clients don't create stores, they get them from the server
            if (GameMain.NetworkMember is { IsClient: true }) { return; }

            var existingStore = location.GetStore(identifier);
            if (existingStore is WorkstationStoreInfo) { return; }

            WorkstationStoreInfo store;
            if (existingStore != null)
            {
                store = new WorkstationStoreInfo(location, existingStore.Save());
                // Add the items the normal store didn't sell
                foreach (ItemPrefab prefab in ItemPrefab.Prefabs)
                {
                    if (store.Stock.Any(s => s.ItemPrefab == prefab)) { continue; }
                    if (!prefab.CanBeBoughtFrom(store, out PriceInfo priceInfo)) { continue; }
                    store.Stock.Add(store.CreateInitialStockItem(prefab, priceInfo));
                }
            }
            else
            {
                store = new WorkstationStoreInfo(location, identifier);
            }
            location.AddOrReplaceStore(store);
        }

        /// <summary>
        /// Unlike normal stores, which use the price of the first store identifier in the pool that has one, the storefront
        /// sells anything any of the merchants in its pool sells but at the most player-friendly price among them.
        /// The first identifier is the storefront's own, and a price defined explicitly for it is used as is, not overridden.
        /// </summary>
        public override bool TryOverridePriceInfo(ItemPrefab item, out PriceInfo? priceInfo)
        {
            priceInfo = null;

            // A price defined for the storefront itself is found by the normal store identifier pool lookup, so not overridden
            if (item.TryGetStorePriceInfo(StoreIdentifiers[0], out _))
            {
                return false;
            }

            int bestAvailability = 0;
            foreach (Identifier storeIdentifier in StoreIdentifiers.Skip(1))
            {
                if (!item.TryGetStorePriceInfo(storeIdentifier, out PriceInfo storePriceInfo)) { continue; }
                // Filter before picking the best, so a requirement on the cheapest price doesn't hide the item when another merchant sells it without one
                if (!storePriceInfo.CanBeBought || !MeetsPurchaseRequirements(storePriceInfo)) { continue; }

                // Prefer lower prices and higher availability on the assumption that they most closely reflect the situation where the storekeeper is your friend
                int availability = (storePriceInfo.MinAvailableAmount + storePriceInfo.MaxAvailableAmount) / 2;
                if (priceInfo == null ||
                    storePriceInfo.Price < priceInfo.Price ||
                    (storePriceInfo.Price == priceInfo.Price && availability > bestAvailability))
                {
                    priceInfo = storePriceInfo;
                    bestAvailability = availability;
                }
            }
            return priceInfo != null;
        }

        /// <summary>
        /// Override reputation modifier to provide maximum faction discount/bonus in player owned outposts.
        /// </summary>
        public override float GetReputationModifier(bool buying)
        {
            if (buying)
            {
                // Return maximum discount (lowest price multiplier)
                return 1.0f - Location.StoreMaxReputationModifier;
            }
            else
            {
                // Return maximum selling bonus (highest price multiplier)
                return 1.0f + Location.StoreMaxReputationModifier;
            }
        }
    }
}
