using Barotrauma.Abilities;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public partial class Location
    {
        public class TakenItem
        {
            public readonly ushort OriginalID;
            public readonly ushort ModuleIndex;
            public readonly Identifier Identifier;
            public readonly int OriginalContainerIndex;

            public TakenItem(Identifier identifier, UInt16 originalID, int originalContainerIndex, ushort moduleIndex)
            {
                OriginalID = originalID;
                OriginalContainerIndex = originalContainerIndex;
                ModuleIndex = moduleIndex;
                Identifier = identifier;
            }

            public TakenItem(Item item)
            {
                System.Diagnostics.Debug.Assert(item.OriginalModuleIndex >= 0, "Trying to add a non-outpost item to a location's taken items");

                OriginalContainerIndex = item.OriginalContainerIndex;
                OriginalID = item.ID;
                ModuleIndex = (ushort) item.OriginalModuleIndex;
                Identifier = ((MapEntity)item).Prefab.Identifier;
            }

            public bool IsEqual(TakenItem obj)
            {
                return obj.OriginalID == OriginalID && obj.OriginalContainerIndex == OriginalContainerIndex && obj.ModuleIndex == ModuleIndex && obj.Identifier == Identifier;
            }

            public bool Matches(Item item)
            {
                if (item.OriginalContainerIndex != Entity.NullEntityID)
                {
                    return item.OriginalContainerIndex == OriginalContainerIndex && item.OriginalModuleIndex == ModuleIndex && ((MapEntity)item).Prefab.Identifier == Identifier;
                }
                else
                {
                    return item.ID == OriginalID && item.OriginalModuleIndex == ModuleIndex && ((MapEntity)item).Prefab.Identifier == Identifier;
                }
            }
        }

        public int LocationIndex
        {
            get
            {
                if (GameMain.GameSession.Campaign?.Map is { } map)
                {
                    return map.Locations.IndexOf(this);
                }

                return -1;
            }
        }

        public readonly List<LocationConnection> Connections = new List<LocationConnection>();

        public const int MaxNameLength = 32;

        public LocalizedString DisplayName { get; private set; }

        public delegate Submarine OutpostLoadingDelegate(OutpostGenerationParams generationParams, LocationType locationType, Location location, bool onlyEntrance = false, bool allowInvalidOutpost = false);

        /// <summary>
        /// When set, this delegate is used to load the outpost instead of generating it procedurally.
        /// </summary>
        public OutpostLoadingDelegate LoadOutpost;

        public Identifier NameIdentifier => nameIdentifier;

        private int nameFormatIndex;
        private Identifier nameIdentifier;

        public int NameFormatIndex => nameFormatIndex;

        /// <summary>
        /// For backwards compatibility: a non-localizable name from the old text files.
        /// </summary>
        private string rawName;

        private LocationType addInitialMissionsForType;

        /// <summary>
        /// "Discovered" means the location is visible on the map (not hidden by the fog of war). Not the same thing as <see cref="Visited"/>
        /// </summary>
        public bool Discovered => GameMain.GameSession?.Map?.IsDiscovered(this) ?? false;

        /// <summary>
        /// Has the location been visited by the player?
        /// </summary>
        public bool Visited => GameMain.GameSession?.Map?.IsVisited(this) ?? false;

        /// <summary>
        /// How many times the location has been visited?
        /// </summary>
        public int TimesVisited;        

        /// <summary>
        /// How many "world steps" (<see cref="Map.ProgressWorld(CampaignMode)"/> must pass for the stores to be reset in the location?
        /// Mainly an optimization
        /// </summary>
        public const int ClearStoresDelay = 10;

        /// <summary>
        /// How many "world steps" (<see cref="Map.ProgressWorld(CampaignMode)"/> have passed since this location was last visited?
        /// </summary>
        public int WorldStepsSinceVisited;

        public readonly Dictionary<LocationTypeChange.Modifier, int> ProximityTimer = new Dictionary<LocationTypeChange.Modifier, int>();
        public (LocationTypeChange typeChange, int delay, MissionPrefab parentMission)? PendingLocationTypeChange
        {
            get;
            set;
        }
        public int LocationTypeChangeCooldown;

        /// <summary>
        /// Is some mission blocking this location from changing its type, or have location type changes been forcibly disabled on the location?
        /// </summary>
        public bool LocationTypeChangesBlocked => 
            DisallowLocationTypeChanges || 
            Type.DisallowLocationTypeChanges ||
            availableMissions.Any(m => m.BlocksLocationTypeChanges);

        public bool DisallowLocationTypeChanges;

        public Biome Biome { get; set; }

        public Vector2 MapPosition { get; private set; }

        public LocationType Type { get; private set; }

        public LocationType OriginalType { get; set; }

        public LevelData LevelData { get; set; }

        public int PortraitId { get; private set; }

        [MaybeNull, AllowNull]
        public Faction Faction { get; set; }

        public Faction SecondaryFaction { get; set; }
        
        /// <summary>
        /// The faction that originally owned the location. In a standard case, set after generating the map.
        /// </summary>
        public Faction OriginalFaction { get; set; }

        /// <summary>
        /// The faction that previously owned the location.
        /// </summary>
        public Faction PreviousFaction { get; set; }

        /// <summary>
        /// Not used by the vanilla game. Can be used by code mods to change the color of the location icon on the campaign map.
        /// </summary>
        public Color? OverrideIconColor;

        public Reputation Reputation => Faction?.Reputation;

        /// <summary>
        /// Is the faction that controls this location hostile to the player?
        /// </summary>
        public bool IsFactionHostile => Faction?.IsFactionHostile ?? false;

        public int MapIndex
        {
            get
            {
                if (GameMain.GameSession?.Map is { } map)
                {
                    return map.Locations.IndexOf(this);
                }

                return -1;
            }
        }

        /// <summary>
        /// The number of NPCs initially spawning in the outpost (disregarding killed ones). 
        /// Not known until the outpost has been generated at least once, until that the value is -1.
        /// </summary>
        public int InitialPopulation { get; set; } = -1;

        /// <summary>
        /// The base population in the outpost (disregarding killed ones). Normally always the same as <see cref="InitialPopulation"/>, 
        /// but can be modified externally (by e.g. mods) to affect the population of the outpost.
        /// </summary>
        public int BasePopulation { get; set; } = -1;

        /// <summary>
        /// Current population of the outpost, taking killed characters into account. -1 if unknown (if the location has not been visited yet and <see cref="InitialPopulation"/> not determined).
        /// </summary>
        public int Population
        {
            get
            {
                //unknown
                if (BasePopulation == -1) { return -1; }
                //no population
                if (BasePopulation == 0) { return 0; }
                return Math.Max(0, BasePopulation - killedCharacters.Count(character => character.WorldStepsSinceKilled < Type.ReplaceKilledCharactersAfter));
            }
        }

        /// <summary>
        /// Population in the outpost relative to the initial population. A value between 0 and 1.
        /// </summary>
        public float PopulationPercentage
        {
            get 
            {
                //base population unknown, assume full population
                if (BasePopulation == -1) { return 1.0f; }
                //no population, population percentage must be zero
                if (BasePopulation == 0) { return 0.0f; }
                //initial population unknown (base population may have been set or modified despite the outpost not having generated yet?)
                // -> assume full population
                if (InitialPopulation == -1) { return 1.0f; }
                return Math.Max(Population / (float)BasePopulation, 0.0f);
            }
        }

        /// <summary>
        /// Is the population below the threshold at which killed NPCs will no longer be replaced and the outpost starts a countdown to becoming abandoned?
        /// </summary>
        public bool IsBelowInsufficientPopulationThreshold =>
            InitialPopulation > -1 &&
            PopulationPercentage < Type.InsufficientPopulationThreshold;

        public int TurnsInRadiation { get; set; }

        #region Store

        public class StoreInfo
        {
            public Identifier Identifier { get; private set; }
            /// <summary>
            /// All store identifier pools this store draws items from. Typically just one, but supports multiple for merchants that should sell items from several pools.
            /// </summary>
            public IReadOnlyList<Identifier> StoreIdentifiers { get; private set; }
            public Identifier MerchantFaction { get; private set; }
            public int Balance { get; set; }
            public List<PurchasedItem> Stock { get; } = new List<PurchasedItem>();
            public List<ItemPrefab> DailySpecials { get; } = new List<ItemPrefab>();
            public List<ItemPrefab> RequestedGoods { get; } = new List<ItemPrefab>();
            /// <summary>
            /// In percentages. Larger values make buying more expensive and selling less profitable, and vice versa.
            /// </summary>
            public int PriceModifier { get; set; }

            public int StepsSinceSpecialsUpdated { get; set; }

            public Location Location { get; }

            /// <summary>
            /// The maximum effect positive reputation can have on store prices (e.g. 0.5 = 50% discount with max reputation).
            /// </summary>
            private float MaxReputationModifier => Location.StoreMaxReputationModifier;

            /// <summary>
            /// The minimum effect negative reputation can have on store prices (e.g. 0.5 = 50% price increase with minimum reputation).
            /// </summary>
            private float MinReputationModifier => Location.StoreMinReputationModifier;

            /// <summary>
            /// Identifies a store with custom behavior (a subclass defined in a plugin) in the save file, so it can be loaded
            /// as the same kind of store again (see <see cref="PluginLoadStoreDelegate"/>). Empty for normal stores.
            /// </summary>
            public virtual Identifier CustomStoreType => Identifier.Empty;

            /// <summary>
            /// Should the store be kept when the location's stores are recreated, for example after the location type changes?
            /// By default, the store is kept if the location's type still has that merchant, the stores of the previous type's merchants are removed.
            /// </summary>
            public virtual bool KeepWhenStoresRecreated => Location.StoreIdentifiers.Contains(Identifier);

            /// <summary>
            /// The name shown in the store UI. By default, the first store identifier in the pool list (read left to right) that has
            /// a matching text tag is used, so it's not necessary to add a custom name for all specialized merchants.
            /// Falls back to the generic 'store' name if none of them have one.
            /// </summary>
            public virtual LocalizedString DisplayName
            {
                get
                {
                    var storeName = TextManager.Get(StoreIdentifiers.Select(static storeIdentifier
                        => $"storename.{storeIdentifier}".ToIdentifier()).ToArray());
                    if (storeName.IsNullOrEmpty())
                    {
                        storeName = TextManager.Get("store");
                    }
                    return storeName;
                }
            }

            protected StoreInfo(Location location)
            {
                Location = location;
            }

            /// <summary>
            /// Parses the store identifiers from a semicolon-delimited store key into a list.
            /// </summary>
            private static Identifier[] ParseStoreIdentifiers(Identifier storeKey)
            {
                string value = storeKey.Value;
                if (!value.Contains(';')) { return [storeKey]; }
                return value.Split(';').Select(static s => s.ToIdentifier()).ToArray();
            }

            /// <summary>
            /// Create new StoreInfo
            /// </summary>
            public StoreInfo(Location location, Identifier identifier) : this(location)
            {
                Identifier = identifier;
                StoreIdentifiers = ParseStoreIdentifiers(identifier);
                Balance = location.StoreInitialBalance;
                Stock = CreateStock();
                GenerateSpecials();
                GeneratePriceModifier();
            }

            /// <summary>
            /// Load previously saved StoreInfo
            /// </summary>
            public StoreInfo(Location location, XElement storeElement) : this(location)
            {
                Identifier = storeElement.GetAttributeIdentifier("identifier", "");
                StoreIdentifiers = ParseStoreIdentifiers(Identifier);
                MerchantFaction = storeElement.GetAttributeIdentifier(nameof(MerchantFaction), "");
                Balance = storeElement.GetAttributeInt("balance", location.StoreInitialBalance);
                PriceModifier = storeElement.GetAttributeInt("pricemodifier", 0);
                StepsSinceSpecialsUpdated = storeElement.GetAttributeInt(nameof(StepsSinceSpecialsUpdated), 0);
                
                foreach (var stockElement in storeElement.GetChildElements("stock"))
                {
                    var identifier = stockElement.GetAttributeIdentifier("id", Identifier.Empty);
                    if (identifier.IsEmpty || ItemPrefab.FindByIdentifier(identifier) is not ItemPrefab prefab) { continue; }
                    int qty = stockElement.GetAttributeInt("qty", 0);
                    if (qty < 1) { continue; }
                    Stock.Add(new PurchasedItem(prefab, qty, buyer: null));
                }
                if (storeElement.GetChildElement("dailyspecials") is XElement specialsElement)
                {
                    var loadedDailySpecials = LoadStoreSpecials(specialsElement);
                    DailySpecials.AddRange(loadedDailySpecials);
                }
                if (storeElement.GetChildElement("requestedgoods") is XElement goodsElement)
                {
                    var loadedRequestedGoods = LoadStoreSpecials(goodsElement);
                    RequestedGoods.AddRange(loadedRequestedGoods);
                }

                static List<ItemPrefab> LoadStoreSpecials(XElement element)
                {
                    var specials = new List<ItemPrefab>();
                    foreach (var childElement in element.GetChildElements("item"))
                    {
                        var id = childElement.GetAttributeIdentifier("id", Identifier.Empty);
                        if (id.IsEmpty || ItemPrefab.FindByIdentifier(id) is not ItemPrefab prefab) { continue; }
                        specials.Add(prefab);
                    }
                    return specials;
                }
            }

            /// <summary>
            /// Creates the initial stock of the item for this store, taking into account how much the store
            /// has left of the items it only sells a limited total of (see <see cref="PriceInfo.LifetimeAvailable"/>).
            /// </summary>
            public PurchasedItem CreateInitialStockItem(ItemPrefab itemPrefab, PriceInfo priceInfo)
            {
                return CreateInitialStockItem(Location, itemPrefab, priceInfo,
                    lifetimeStockLeft: priceInfo.HasLifetimeLimit ?
                        Location.GetLifetimeStockLeft(Identifier, itemPrefab.Identifier, priceInfo) :
                        null);
            }

            /// <summary>
            /// Creates the initial stock of the item without knowing which store it's for, which means
            /// <see cref="PriceInfo.LifetimeAvailable"/> can't be taken into account.
            /// Prefer the instance method when possible.
            /// </summary>
            public static PurchasedItem CreateInitialStockItem(Location location, ItemPrefab itemPrefab, PriceInfo priceInfo)
            {
                return CreateInitialStockItem(location, itemPrefab, priceInfo, lifetimeStockLeft: null);
            }

            /// <param name="lifetimeStockLeft">
            /// How many of the item the store can still sell in total, or null if the total isn't limited.
            /// </param>
            private static PurchasedItem CreateInitialStockItem(Location location, ItemPrefab itemPrefab, PriceInfo priceInfo, int? lifetimeStockLeft)
            {
                int quantity;
                if (lifetimeStockLeft.HasValue)
                {
                    // The store puts out as many as it can of what it has left, but never more than that
                    quantity = Math.Min(priceInfo.MaxAvailableAmount, lifetimeStockLeft.Value);
                }
                else
                {
                    quantity = Rand.Range(priceInfo.MinAvailableAmount, priceInfo.MaxAvailableAmount + 1);
                    //simulate stores stocking up if the location hasn't been visited in a while
                    quantity = Math.Min(quantity + location.WorldStepsSinceVisited, priceInfo.MaxAvailableAmount);
                }

                // The hooks get the final say (eg. they can go over the lifetime limit)
                foreach (var hook in PluginData.GetHooks<PluginCampaignItemModifyAvailability>())
                {
                    quantity = hook(location, itemPrefab, priceInfo, quantity);
                }

                return new PurchasedItem(itemPrefab, quantity, buyer: null);
            }

            /// <summary>
            /// Lets the store decide which PriceInfo applies to the item.
            /// The requirements of an overriding PriceInfo are still checked when buying, see <see cref="MeetsPurchaseRequirements"/>.
            /// </summary>
            /// <returns>True if the store overrides the item's PriceInfo.</returns>
            public virtual bool TryOverridePriceInfo(ItemPrefab item, out PriceInfo priceInfo)
            {
                priceInfo = null;
                return false;
            }

            /// <summary>
            /// Does the location meet the requirements of the PriceInfo (level difficulty, faction) for the item to be sold in this store?
            /// </summary>
            public virtual bool MeetsPurchaseRequirements(PriceInfo priceInfo)
            {
                Identifier? faction = Location.Faction?.Prefab.Identifier;
                Identifier? secondaryFaction = Location.SecondaryFaction?.Prefab.Identifier;
                return
                    (Location.LevelData?.Difficulty ?? 0) >= priceInfo.MinLevelDifficulty &&
                    (priceInfo.RequiredFaction.IsEmpty || faction == priceInfo.RequiredFaction || secondaryFaction == priceInfo.RequiredFaction) &&
                    (!priceInfo.MinReputation.Any() || priceInfo.MinReputation.Any(p => faction == p.Key || secondaryFaction == p.Key));
            }

            public virtual List<PurchasedItem> CreateStock()
            {
                var stock = new List<PurchasedItem>();
                foreach (var prefab in ItemPrefab.Prefabs)
                {
                    if (!prefab.CanBeBoughtFrom(this, out var priceInfo)) { continue; }
                    stock.Add(CreateInitialStockItem(prefab, priceInfo));
                }
                return stock;
            }

            public void AddStock(List<SoldItem> items)
            {
                if (items == null || items.None()) { return; }
                DebugConsole.NewMessage($"Adding items to stock for \"{Identifier}\" at \"{Location}\"", Color.Purple, debugOnly: true);
                foreach (var item in items)
                {
                    if (Stock.FirstOrDefault(i => i.ItemPrefab == item.ItemPrefab) is PurchasedItem stockItem)
                    {
                        if (item.ItemPrefab.GetPriceInfo(this) is { HasLifetimeLimit: true } priceInfo)
                        {
                            // The items return to the store's limited total supply, but the shelf still only holds MaxAvailableAmount at a time
                            int amountLeft = Location.IncreaseLifetimeStockLeft(Identifier, item.ItemPrefab.Identifier, 1, priceInfo);
                            stockItem.Quantity = Math.Min(stockItem.Quantity + 1, Math.Min(priceInfo.MaxAvailableAmount, amountLeft));
                        }
                        else
                        {
                            stockItem.Quantity += 1;
                        }
                        DebugConsole.NewMessage($"Added 1x {item.ItemPrefab.Name}, new total: {stockItem.Quantity}", Color.Cyan, debugOnly: true);
                    }
                    else
                    {
                        DebugConsole.NewMessage($"{item.ItemPrefab.Name} not sold at location, can't add", Color.Cyan, debugOnly: true);
                    }
                }
            }

            public void RemoveStock(List<PurchasedItem> items)
            {
                if (items == null || items.None()) { return; }
                DebugConsole.NewMessage($"Removing items from stock for \"{Identifier}\" at \"{Location}\"", Color.Purple, debugOnly: true);
                foreach (PurchasedItem item in items)
                {
                    if (Stock.FirstOrDefault(i => i.ItemPrefab == item.ItemPrefab) is PurchasedItem stockItem)
                    {
                        int quantityBeforeRemoving = stockItem.Quantity;
                        stockItem.Quantity = Math.Max(stockItem.Quantity - item.Quantity, 0);
                        DebugConsole.NewMessage($"Removed {item.Quantity}x {item.ItemPrefab.Name}, new total: {stockItem.Quantity}", Color.Cyan, debugOnly: true);
                        if (item.ItemPrefab.GetPriceInfo(this) is { HasLifetimeLimit: true } priceInfo)
                        {
                            // What's taken off the shelf also comes out of the store's limited total supply of the item
                            Location.ReduceLifetimeStockLeft(Identifier, item.ItemPrefab.Identifier,
                                quantityBeforeRemoving - stockItem.Quantity, priceInfo);
                        }
                    }
                }
            }

            public void GenerateSpecials()
            {
                var availableStock = new Dictionary<ItemPrefab, float>();
                foreach (var stockItem in Stock)
                {
                    if (stockItem.Quantity < 1) { continue; }
                    float weight = 1.0f;
                    if (stockItem.ItemPrefab.GetPriceInfo(this) is PriceInfo priceInfo)
                    {
                        if (!priceInfo.CanBeSpecial) { continue; }
                        // Don't discount items the location is marking up
                        if (IsMarkedUp(stockItem.ItemPrefab, priceInfo)) { continue; }
                        int baseQuantity = priceInfo.MinAvailableAmount;
                        weight += (stockItem.Quantity - baseQuantity) / Math.Max(1.0f, baseQuantity);
                        if (weight < 0.0f) { continue; }
                    }
                    availableStock.Add(stockItem.ItemPrefab, weight);
                }

                DailySpecials.Clear();
                int extraSpecialSalesCount = GetExtraSpecialSalesCount();
                for (int i = 0; i < Location.DailySpecialsCount + extraSpecialSalesCount; i++)
                {
                    if (availableStock.None()) { break; }
                    var item = ToolBox.SelectWeightedRandom(availableStock.Keys.ToList(), availableStock.Values.ToList(), Rand.RandSync.Unsynced);
                    if (item == null) { break; }
                    DailySpecials.Add(item);
                    availableStock.Remove(item);
                }

                RequestedGoods.Clear();
                for (int i = 0; i < Location.RequestedGoodsCount; i++)
                {
                    var selectedPrefab = ItemPrefab.Prefabs.GetRandom(prefab =>
                        prefab.CanBeSold && !DailySpecials.Contains(prefab) && !RequestedGoods.Contains(prefab) &&
                        prefab.GetPriceInfo(this) is PriceInfo pi && pi.CanBeSpecial, Rand.RandSync.Unsynced);
                    if (selectedPrefab == null) { break; }
                    RequestedGoods.Add(selectedPrefab);
                }

                StepsSinceSpecialsUpdated = 0;
            }

            /// <summary>
            /// Is the buy price of the item increased by a plugin price multiplier (e.g. a shortage at the outpost)?
            /// </summary>
            public bool IsMarkedUp(ItemPrefab item, PriceInfo priceInfo = null)
            {
                return GetPluginBuyPriceMultiplier(item, priceInfo) > 1.0f;
            }

            private float GetPluginBuyPriceMultiplier(ItemPrefab item, PriceInfo priceInfo)
            {
                float multiplier = 1.0f;
                foreach (var hook in PluginData.GetHooks<PluginCampaignItemBuyPriceMultiplier>())
                {
                    multiplier *= hook(this, item, priceInfo);
                }
                return multiplier;
            }

            public void GeneratePriceModifier()
            {
                PriceModifier = Rand.Range(-Location.StorePriceModifierRange, Location.StorePriceModifierRange + 1);
            }

            /// <param name="priceInfo">If null, item.GetPriceInfo() will be used to get it.</param>
            /// /// <param name="considerDailySpecials">If false, the price won't be affected by <see cref="DailySpecialPriceModifier"/></param>
            public int GetAdjustedItemBuyPrice(ItemPrefab item, PriceInfo priceInfo = null, bool considerDailySpecials = true)
            {
                priceInfo ??= item?.GetPriceInfo(this);
                if (priceInfo == null) { return 0; }
                float price = Location.StoreBuyPriceModifier * priceInfo.Price;
                // Adjust by random price modifier
                price = (100 + PriceModifier) / 100.0f * price;
                price *= priceInfo.BuyingPriceMultiplier;
                // Adjust by daily special status
                if (considerDailySpecials && DailySpecials.Contains(item))
                {
                    price = Location.DailySpecialPriceModifier * price;
                }
                // Adjust by requested good status (to avoid the store selling items that it requests potentially for less than it pays for them)
                if (RequestedGoods.Contains(item))
                {
                    price = Location.RequestGoodBuyPriceModifier * price;
                }
                // Adjust by current reputation
                price *= GetReputationModifier(true);

                price *= GetPluginBuyPriceMultiplier(item, priceInfo);

                // Adjust by campaign difficulty settings
                if (GameMain.GameSession?.Campaign is CampaignMode campaign)
                {
                    price *= campaign.Settings.ShopPriceMultiplier;
                }

                var characters = GameSession.GetSessionCrewCharacters(CharacterType.Both);
                if (characters.Any())
                {
                    var faction = GetMerchantOrLocationFactionIdentifier();
                    if (!faction.IsEmpty && GameMain.GameSession.Campaign.GetFactionAffiliation(faction) is FactionAffiliation.Positive)
                    {
                        price *= 1f - characters.Max(static c => c.GetStatValue(StatTypes.StoreBuyMultiplierAffiliated, includeSaved: false));
                        price *= 1f - characters.Max(static c => c.Info.GetSavedStatValue(StatTypes.StoreBuyMultiplierAffiliated, Tags.StatIdentifierTargetAll));
                        price *= 1f - characters.Max(c => GetAffiliatedStatValuesForItem(c, item, priceInfo, faction));
                    }
                    price *= 1f - characters.Max(static c => c.GetStatValue(StatTypes.StoreBuyMultiplier, includeSaved: false));
                    price *= 1f - characters.Max(c => GetStatValuesForItem(c, item, StatTypes.StoreBuyMultiplier));
                }

                static float GetStatValuesForItem(Character character, ItemPrefab item, StatTypes statType)
                {
                    float statValueSum = 0.0f;
                    foreach (Identifier itemTag in item.Tags)
                    {
                        statValueSum += character.Info.GetSavedStatValue(statType, itemTag);
                    }
                    return statValueSum;
                }

                static float GetAffiliatedStatValuesForItem(Character character, ItemPrefab item, PriceInfo priceInfo, Identifier faction)
                {
                    float statValueSum = GetStatValuesForItem(character, item, StatTypes.StoreBuyMultiplierAffiliated);
                    // items that can only be bought from this faction count as faction-unique even if they don't have the faction tag
                    if (priceInfo.RequiredFaction == faction && !item.Tags.Contains(faction))
                    {
                        statValueSum += character.Info.GetSavedStatValue(StatTypes.StoreBuyMultiplierAffiliated, faction);
                    }
                    return statValueSum;
                }

                // Price should never go below 1 mk
                return Math.Max((int)price, 1);
            }

            /// <param name="priceInfo">If null, item.GetPriceInfo() will be used to get it.</param>
            /// <param name="considerRequestedGoods">If false, the price won't be affected by <see cref="Barotrauma.Location.RequestGoodSellPriceModifier"/></param>
            public int GetAdjustedItemSellPrice(ItemPrefab item, PriceInfo priceInfo = null, bool considerRequestedGoods = true)
            {
                priceInfo ??= item?.GetPriceInfo(this);
                if (priceInfo == null) { return 0; }
                float price = Location.StoreSellPriceModifier * priceInfo.Price;
                // Adjust by random price modifier
                price = (100 - PriceModifier) / 100.0f * price;
                // Adjust by requested good status
                if (considerRequestedGoods && RequestedGoods.Contains(item))
                {
                    price = Location.RequestGoodSellPriceModifier * price;
                }
                // Adjust by location reputation
                price *= GetReputationModifier(false);

                foreach (var hook in PluginData.GetHooks<PluginCampaignItemSellPriceMultiplier>())
                {
                    price *= hook(this, item, priceInfo);
                }

                var characters = GameSession.GetSessionCrewCharacters(CharacterType.Both);
                if (characters.Any())
                {
                    price *= 1f + characters.Max(static c => c.GetStatValue(StatTypes.StoreSellMultiplier, includeSaved: false));
                    price *= 1f + characters.Max(c => GetMultiplierForItem(c, item));

                    float GetMultiplierForItem(Character character, ItemPrefab item)
                    {
                        return 
                            item.Tags.Sum(tag => character.Info.GetSavedStatValue(StatTypes.StoreSellMultiplier, tag)) +
                            character.Info.GetSavedStatValue(StatTypes.StoreSellMultiplier, Tags.StatIdentifierTargetAll);
                    }
                }

                // Price should never go below 1 mk
                return Math.Max((int)price, 1);
            }

            public void SetMerchantFaction(Identifier factionIdentifier)
            {
                MerchantFaction = factionIdentifier;
            }

            public Identifier GetMerchantOrLocationFactionIdentifier()
            {
                return MerchantFaction.IfEmpty(Location.Faction?.Prefab.Identifier ?? Identifier.Empty);
            }

            public virtual float GetReputationModifier(bool buying)
            {
                var factionIdentifier = GetMerchantOrLocationFactionIdentifier();
                var reputation = GameMain.GameSession.Campaign.GetFaction(factionIdentifier)?.Reputation;
                if (reputation == null) { return 1.0f; }
                if (buying)
                {
                    if (reputation.Value > 0.0f)
                    {
                        return MathHelper.Lerp(1.0f, 1.0f - MaxReputationModifier, reputation.Value / reputation.MaxReputation);
                    }
                    else
                    {
                        return MathHelper.Lerp(1.0f, 1.0f + MinReputationModifier, reputation.Value / reputation.MinReputation);
                    }
                }
                else
                {
                    if (reputation.Value > 0.0f)
                    {
                        return MathHelper.Lerp(1.0f, 1.0f + MaxReputationModifier, reputation.Value / reputation.MaxReputation);
                    }
                    else
                    {
                        return MathHelper.Lerp(1.0f, 1.0f - MinReputationModifier, reputation.Value / reputation.MinReputation);
                    }
                }
            }

            /// <summary>
            /// Note: Virtual, so that subclasses can add their own data to the element, then read it back in their <see cref="StoreInfo"/> constructor.
            /// </summary>
            public virtual XElement Save()
            {
                var storeElement = new XElement("store",
                    new XAttribute(nameof(Identifier), Identifier.Value),
                    new XAttribute(nameof(MerchantFaction), MerchantFaction),
                    new XAttribute(nameof(Balance), Balance),
                    new XAttribute(nameof(PriceModifier), PriceModifier),
                    new XAttribute(nameof(StepsSinceSpecialsUpdated), StepsSinceSpecialsUpdated));
                if (!CustomStoreType.IsEmpty)
                {
                    storeElement.Add(new XAttribute(nameof(CustomStoreType), CustomStoreType));
                }
                foreach (PurchasedItem item in Stock)
                {
                    if (item?.ItemPrefab == null) { continue; }
                    storeElement.Add(new XElement("stock",
                        new XAttribute("id", item.ItemPrefab.Identifier),
                        new XAttribute("qty", item.Quantity)));
                }
                if (DailySpecials.Any())
                {
                    var dailySpecialElement = new XElement("dailyspecials");
                    foreach (var item in DailySpecials)
                    {
                        dailySpecialElement.Add(new XElement("item",
                            new XAttribute("id", item.Identifier)));
                    }
                    storeElement.Add(dailySpecialElement);
                }
                if (RequestedGoods.Any())
                {
                    var requestedGoodsElement = new XElement("requestedgoods");
                    foreach (var item in RequestedGoods)
                    {
                        requestedGoodsElement.Add(new XElement("item",
                            new XAttribute("id", item.Identifier)));
                    }
                    storeElement.Add(requestedGoodsElement);
                }
                return storeElement;
            }

            public override string ToString()
            {
                return Identifier.Value;
            }
        }

        public Dictionary<Identifier, StoreInfo> Stores { get; private set; }

        public float StoreMaxReputationModifier => Type.StoreMaxReputationModifier;
        private float StoreMinReputationModifier => Type.StoreMinReputationModifier;
        private float StoreSellPriceModifier => Type.StoreSellPriceModifier;
        private float StoreBuyPriceModifier => Type.StoreBuyPriceModifier;
        private float DailySpecialPriceModifier => Type.DailySpecialPriceModifier;
        private float RequestGoodBuyPriceModifier => Type.RequestGoodBuyPriceModifier;
        private float RequestGoodSellPriceModifier => Type.RequestGoodPriceModifier;
        public int StoreInitialBalance => Type.StoreInitialBalance;
        private int StorePriceModifierRange => Type.StorePriceModifierRange;

        /// <summary>
        /// How many map progress steps it takes before the daily specials and requested goods should be updated.
        /// </summary>
        public const int SpecialsUpdateInterval = 3;

        public int DailySpecialsCount => Type.DailySpecialsCount;
        public int RequestedGoodsCount => Type.RequestedGoodsCount;
        public HashSet<Identifier> StoreIdentifiers { get; } = new HashSet<Identifier>();

        /// <summary>
        /// Should the stores be recreated when the round ends? Set to true if the location type changed during a round.
        /// </summary>
        private bool recreateStoresAfterRound;

        /// <summary>
        /// Stores added after a mid-round location type change are already built for the new type,
        /// so the deferred recreation at the end of the round shouldn't reroll their stock, specials or prices.
        /// </summary>
        private readonly HashSet<StoreInfo> storesAddedAfterTypeChange = new HashSet<StoreInfo>();

        /// <summary>
        /// How much the stores have left of the items they only sell a limited lifetime total of (see <see cref="PriceInfo.LifetimeAvailable"/>).
        /// Kept outside the stores to persist the lifetime stock over the stores reloading and regenerating.
        /// Key => store identifier, value => amount left per item.
        /// </summary>
        private readonly Dictionary<Identifier, Dictionary<Identifier, int>> lifetimeStockLeft = new Dictionary<Identifier, Dictionary<Identifier, int>>();

        /// <summary>
        /// How much of the item the store can still sell before running out of it for good.
        /// </summary>
        private int GetLifetimeStockLeft(Identifier storeIdentifier, Identifier itemIdentifier, PriceInfo priceInfo)
        {
            if (lifetimeStockLeft.TryGetValue(storeIdentifier, out var storeStock) &&
                storeStock.TryGetValue(itemIdentifier, out int amountLeft))
            {
                return amountLeft;
            }
            return priceInfo.LifetimeAvailable;
        }

        private void ReduceLifetimeStockLeft(Identifier storeIdentifier, Identifier itemIdentifier, int amount, PriceInfo priceInfo)
        {
            if (amount <= 0) { return; }
            SetLifetimeStockLeft(storeIdentifier, itemIdentifier,
                Math.Max(GetLifetimeStockLeft(storeIdentifier, itemIdentifier, priceInfo) - amount, 0));
        }

        private int IncreaseLifetimeStockLeft(Identifier storeIdentifier, Identifier itemIdentifier, int amount, PriceInfo priceInfo)
        {
            int amountLeft = GetLifetimeStockLeft(storeIdentifier, itemIdentifier, priceInfo);
            if (amount <= 0)
            {
                return amountLeft;
            }
            amountLeft += amount;
            SetLifetimeStockLeft(storeIdentifier, itemIdentifier, amountLeft);
            return amountLeft;
        }

        private void SetLifetimeStockLeft(Identifier storeIdentifier, Identifier itemIdentifier, int amountLeft)
        {
            if (!lifetimeStockLeft.TryGetValue(storeIdentifier, out var storeStock))
            {
                storeStock = new Dictionary<Identifier, int>();
                lifetimeStockLeft.Add(storeIdentifier, storeStock);
            }
            storeStock[itemIdentifier] = amountLeft;
        }

        #endregion

        private const float MechanicalMaxDiscountPercentage = 50.0f;
        private const float HealMaxDiscountPercentage = 10.0f;

        private readonly List<TakenItem> takenItems = new List<TakenItem>();
        public IEnumerable<TakenItem> TakenItems
        {
            get { return takenItems; }
        }

        public class KilledCharacter
        {
            public readonly int Identifier;
            public int WorldStepsSinceKilled;

            public KilledCharacter(int identifier, int worldStepsSinceKilled = 0)
            {
                Identifier = identifier;
                WorldStepsSinceKilled = worldStepsSinceKilled;
            }

            public KilledCharacter(XElement element) : 
                this(
                    element.GetAttributeInt("id", 0), 
                    element.GetAttributeInt(nameof(WorldStepsSinceKilled), 0))
            {
            }

            public void Save(XElement parentElement)
            {
                parentElement.Add(
                    new XElement(nameof(KilledCharacter),
                        new XAttribute("id", Identifier),
                        new XAttribute(nameof(WorldStepsSinceKilled), WorldStepsSinceKilled)));
            }

            public override bool Equals(object obj)
            {
                if (obj is KilledCharacter other)
                {
                    return Identifier == other.Identifier;
                }
                return false;
            }

            public override int GetHashCode()
            {
                return Identifier.GetHashCode();
            }
        }

        private readonly HashSet<KilledCharacter> killedCharacters = new HashSet<KilledCharacter>();
        public IEnumerable<KilledCharacter> KilledCharacters => killedCharacters;

        private readonly List<Mission> availableMissions = new List<Mission>();
        public IEnumerable<Mission> AvailableMissions
        {
            get 
            {
                availableMissions.RemoveAll(static m => m.ShouldBeRemovedFromLocation());
                return availableMissions; 
            }
        }

        /// <summary>
        /// Missions that are available and visible in menus (<see cref="MissionPrefab.ShowInMenus"/>)
        /// </summary>
        public IEnumerable<Mission> AvailableAndVisibleMissions => AvailableMissions.Where(m => m.Prefab.ShowInMenus);

        private readonly List<Mission> selectedMissions = new List<Mission>();
        public IEnumerable<Mission> SelectedMissions
        {
            get 
            {
                selectedMissions.RemoveAll(m => !availableMissions.Contains(m));
                return selectedMissions; 
            }
        }

        public void SelectMission(Mission mission)
        {
            if (!SelectedMissions.Contains(mission) && mission != null)
            {
                selectedMissions.Add(mission);
                selectedMissions.Sort((m1, m2) => availableMissions.IndexOf(m1).CompareTo(availableMissions.IndexOf(m2)));
            }
        }

        public void DeselectMission(Mission mission)
        {
            selectedMissions.Remove(mission);
        }


        public List<int> GetSelectedMissionIndices()
        {
            List<int> selectedMissionIndices = new List<int>();
            foreach (Mission mission in SelectedMissions)
            {
                if (availableMissions.Contains(mission))
                {
                    selectedMissionIndices.Add(availableMissions.IndexOf(mission));
                }
            }
            return selectedMissionIndices;
        }

        public void SetSelectedMissionIndices(IEnumerable<int> missionIndices)
        {
            selectedMissions.Clear();
            foreach (int missionIndex in missionIndices)
            {
                if (missionIndex < 0 || missionIndex >= availableMissions.Count)
                {
                    DebugConsole.ThrowError($"Failed to select a mission in location \"{DisplayName}\". Mission index out of bounds ({missionIndex}, available missions: {availableMissions.Count})");
                    break;
                }
                selectedMissions.Add(availableMissions[missionIndex]);
            }
        }

        private float priceMultiplier = 1.0f;
        public float PriceMultiplier
        {
            get { return priceMultiplier; }
            set { priceMultiplier = MathHelper.Clamp(value, 0.1f, 10.0f); }
        }
        
        private float mechanicalpriceMultiplier = 1.0f;
        public float MechanicalPriceMultiplier
        {
            get => mechanicalpriceMultiplier;
            set => mechanicalpriceMultiplier = MathHelper.Clamp(value, 0.1f, 10.0f);
        }

        public string LastTypeChangeMessage;

        public int TimeSinceLastTypeChange;

        public bool IsGateBetweenBiomes;

        private readonly struct LoadedMission
        {
            public readonly MissionPrefab MissionPrefab;
            public readonly int TimesAttempted;
            public readonly int OriginLocationIndex;
            public readonly int DestinationIndex;
            public readonly bool SelectedMission;
            public readonly int WorldStepsSinceUnlocked;
            public readonly int WorldStepsUntilExpiry;
            public readonly ImmutableArray<int> PathLocationIndices;

            public LoadedMission(XElement element)
            {
                var id = element.GetAttributeIdentifier("prefabid", Identifier.Empty);
                MissionPrefab = MissionPrefab.Prefabs.TryGet(id, out var prefab) ? prefab : null;                
                TimesAttempted = element.GetAttributeInt("timesattempted", 0);
                OriginLocationIndex = element.GetAttributeInt("origin", -1);
                DestinationIndex = element.GetAttributeInt("destinationindex", -1);
                SelectedMission = element.GetAttributeBool("selected", false);
                WorldStepsSinceUnlocked = element.GetAttributeInt(nameof(WorldStepsSinceUnlocked), -1);
                WorldStepsUntilExpiry = element.GetAttributeInt(nameof(WorldStepsUntilExpiry), -1);
                PathLocationIndices = element.GetAttributeIntArray(nameof(PathLocationIndices), Array.Empty<int>()).ToImmutableArray();
            }
        }

        private List<LoadedMission> loadedMissions;

        public HireManager HireManager;

        public override string ToString()
        {
            return $"Location ({DisplayName ?? "null"})";
        }

        public Location(Vector2 mapPosition, int? zone, Identifier? biomeId, Random rand, bool requireOutpost = false, LocationType forceLocationType = null, IEnumerable<Location> existingLocations = null)
        {
            Type = OriginalType = forceLocationType ?? LocationType.Random(rand, zone, biomeId, requireOutpost);
            AssignRandomName(Type, rand, existingLocations);
            MapPosition = mapPosition;
            PortraitId = ToolBox.StringToInt(nameIdentifier.Value);
            Connections = new List<LocationConnection>(); 
        }

        /// <summary>
        /// Create a location from save data
        /// </summary>
        public Location(CampaignMode campaign, XElement element)
        {
            Identifier locationTypeId = element.GetAttributeIdentifier("type", "");
            bool typeNotFound = GetTypeOrFallback(locationTypeId, out LocationType type);
            Type = type;

            Identifier originalLocationTypeId = element.GetAttributeIdentifier("originaltype", locationTypeId);
            GetTypeOrFallback(originalLocationTypeId, out LocationType originalType);
            OriginalType = originalType;

            nameIdentifier = element.GetAttributeIdentifier(nameof(nameIdentifier), "");
            if (nameIdentifier.IsEmpty)
            {
                //backwards compatibility
                DisplayName = element.GetAttributeString("name", "");
                rawName = element.GetAttributeString("rawname", element.GetAttributeString("basename", DisplayName.Value));
                nameIdentifier = rawName.ToIdentifier();
            }
            else
            {
                nameFormatIndex = element.GetAttributeInt(nameof(nameFormatIndex), 0); 
                DisplayName = GetName(Type, nameFormatIndex, nameIdentifier);
            }

            LoadChangingProperties(element, campaign);

            MapPosition = element.GetAttributeVector2("position", Vector2.Zero);
            IsGateBetweenBiomes = element.GetAttributeBool(nameof(IsGateBetweenBiomes), false);

            Identifier biomeId = element.GetAttributeIdentifier("biome", Identifier.Empty);
            if (biomeId != Identifier.Empty)
            {
                if (Biome.Prefabs.TryGet(biomeId, out Biome biome))
                {
                    Biome = biome;
                }
                else
                {
                    DebugConsole.ThrowError($"Error while loading the campaign map: could not find a biome with the identifier \"{biomeId}\".");
                }
            }

            if (!typeNotFound)
            {
                for (int i = 0; i < Type.CanChangeTo.Count; i++)
                {
                    for (int j = 0; j < Type.CanChangeTo[i].Modifiers.Count; j++)
                    {
                        ProximityTimer.Add(Type.CanChangeTo[i].Modifiers[j], element.GetAttributeInt("proximitytimer" + i + "-" + j, 0));
                    }
                }

                LoadLocationTypeChange(element);
            }

            string[] takenItemStr = element.GetAttributeStringArray("takenitems", Array.Empty<string>());
            foreach (string takenItem in takenItemStr)
            {
                string[] takenItemSplit = takenItem.Split(';');
                if (takenItemSplit.Length != 4)
                {
                    DebugConsole.ThrowError($"Error in saved location: could not parse taken item data \"{takenItem}\"");
                    continue;
                }
                if (!ushort.TryParse(takenItemSplit[1], out ushort id))
                {
                    DebugConsole.ThrowError($"Error in saved location: could not parse taken item id \"{takenItemSplit[1]}\"");
                    continue;
                }
                if (!int.TryParse(takenItemSplit[2], out int containerIndex))
                {
                    DebugConsole.ThrowError($"Error in saved location: could not parse taken container index \"{takenItemSplit[2]}\"");
                    continue;
                }
                if (!ushort.TryParse(takenItemSplit[3], out ushort moduleIndex))
                {
                    DebugConsole.ThrowError($"Error in saved location: could not parse taken item module index \"{takenItemSplit[3]}\"");
                    continue;
                }
                takenItems.Add(new TakenItem(takenItemSplit[0].ToIdentifier(), id, containerIndex, moduleIndex));
            }

            //backwards compatibility (previously we didn't track how long it's been since the character was killed)
            var killedCharacterIdentifiers = element.GetAttributeIntArray("killedcharacters", Array.Empty<int>()).ToHashSet();
            foreach (int killedCharacterIdentifier in killedCharacterIdentifiers)
            {
                killedCharacters.Add(new KilledCharacter(killedCharacterIdentifier));
            }

            System.Diagnostics.Debug.Assert(Type != null, $"Could not find the location type \"{locationTypeId}\"!");
            Type ??= LocationType.Prefabs.First();

            LevelData = new LevelData(element.GetChildElement("Level"), clampDifficultyToBiome: false);

            PortraitId = ToolBox.StringToInt(!rawName.IsNullOrEmpty() ? rawName : nameIdentifier.Value);

            LoadStores(element);
            LoadMissions(element);

            bool GetTypeOrFallback(Identifier identifier, out LocationType type)
            {
                if (!LocationType.Prefabs.TryGet(identifier, out type))
                {
                    //turn lairs into abandoned outposts
                    if (identifier == "lair")
                    {
                        LocationType.Prefabs.TryGet("Abandoned".ToIdentifier(), out type);
                        addInitialMissionsForType = Type;
                    }
                    if (type == null)
                    {
                        DebugConsole.AddWarning($"Could not find location type \"{identifier}\". Using location type \"None\" instead.");
                        LocationType.Prefabs.TryGet("None".ToIdentifier(), out type);                
                        type ??= LocationType.Prefabs.First();                        
                    }
                    if (type != null)
                    {
                        element.SetAttributeValue("type", type.Identifier.ToString());
                    }
                    return false;
                }
                return true;
            }

            foreach (var hook in PluginData.GetHooks<PluginLocationLoadDelegate>())
            {
                hook(this, campaign, element);
            }
        }

        /// <summary>
        /// Load the values of properties that can change mid-campaign and need to be updated when a client receives a new campaign save from the server
        /// </summary>
        public void LoadChangingProperties(XElement element, CampaignMode campaign)
        {
            PriceMultiplier = element.GetAttributeFloat(nameof(PriceMultiplier), 1.0f);
            MechanicalPriceMultiplier = element.GetAttributeFloat(nameof(MechanicalPriceMultiplier), 1.0f);
            TurnsInRadiation = element.GetAttributeInt(nameof(TurnsInRadiation).ToLower(), 0);
            WorldStepsSinceVisited = element.GetAttributeInt(nameof(WorldStepsSinceVisited), 0);
            TimesVisited = element.GetAttributeInt(nameof(TimesVisited), 0);
            InitialPopulation = element.GetAttributeInt(nameof(InitialPopulation), -1);
            BasePopulation = element.GetAttributeInt(nameof(BasePopulation), -1);

            var factionIdentifier = element.GetAttributeIdentifier(nameof(Faction), Identifier.Empty);
            Faction = factionIdentifier.IsEmpty ? null : campaign.GetFaction(factionIdentifier);

            var secondaryFactionIdentifier = element.GetAttributeIdentifier(nameof(SecondaryFaction), Identifier.Empty);
            SecondaryFaction = secondaryFactionIdentifier.IsEmpty ? null : campaign.GetFaction(secondaryFactionIdentifier);
            
            var originalFactionIdentifier = element.GetAttributeIdentifier(nameof(OriginalFaction), Identifier.Empty);
            OriginalFaction = originalFactionIdentifier.IsEmpty ? null : campaign.GetFaction(originalFactionIdentifier);

            var previousFactionIdentifier = element.GetAttributeIdentifier(nameof(PreviousFaction), Identifier.Empty);
            PreviousFaction = previousFactionIdentifier.IsEmpty ? null : campaign.GetFaction(previousFactionIdentifier);

            killedCharacters.Clear();
            foreach (XElement killedCharacterElement in element.GetChildElements("killedcharacter"))
            {
                killedCharacters.Add(new KilledCharacter(killedCharacterElement));
            }

            foreach (var storeElement in element.GetChildElements("store"))
            {
                var identifier = storeElement.GetAttributeIdentifier("identifier", "");
                if (Stores != null && Stores.TryGetValue(identifier, out StoreInfo store))
                {
                    store.StepsSinceSpecialsUpdated = storeElement.GetAttributeInt(nameof(StoreInfo.StepsSinceSpecialsUpdated), 0);
                }
            }
        }

        public void LoadLocationTypeChange(XElement locationElement)
        {
            TimeSinceLastTypeChange = locationElement.GetAttributeInt("timesincelasttypechange", 0);
            LocationTypeChangeCooldown = locationElement.GetAttributeInt("locationtypechangecooldown", 0);
            LastTypeChangeMessage = locationElement.GetAttributeString("lasttypechangemessage", string.Empty);
            foreach (var subElement in locationElement.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "pendinglocationtypechange":
                        int timer = subElement.GetAttributeInt("timer", 0);
                        if (subElement.Attribute("index") != null)
                        {
                            int locationTypeChangeIndex = subElement.GetAttributeInt("index", 0);
                            if (locationTypeChangeIndex < 0 || locationTypeChangeIndex >= Type.CanChangeTo.Count)
                            {
                                DebugConsole.AddWarning($"Failed to activate a location type change in the location \"{DisplayName}\". Location index out of bounds ({locationTypeChangeIndex}).");
                                continue;
                            }
                            PendingLocationTypeChange = (Type.CanChangeTo[locationTypeChangeIndex], timer, null);
                        }
                        else
                        {
                            Identifier missionIdentifier = subElement.GetAttributeIdentifier("missionidentifier", "");
                            var mission = MissionPrefab.Prefabs[missionIdentifier];
                            if (mission == null)
                            {
                                DebugConsole.AddWarning($"Failed to activate a location type change from the mission \"{missionIdentifier}\" in location \"{DisplayName}\". Matching mission not found.");
                                continue;
                            }
                            PendingLocationTypeChange = (mission.LocationTypeChangeOnCompleted, timer, mission);
                        }
                        break;
                }
            }
        }

        public void LoadMissions(XElement locationElement)
        {
            if (locationElement.GetChildElement("missions") is XElement missionsElement)
            {
                loadedMissions = new List<LoadedMission>();
                foreach (XElement childElement in missionsElement.GetChildElements("mission"))
                {
                    var loadedMission = new LoadedMission(childElement);
                    if (loadedMission.MissionPrefab != null)
                    {
                        loadedMissions.Add(loadedMission);
                    }
                }
            }
        }

        public static Location CreateRandom(Vector2 position, int? zone, Identifier? biomeId, Random rand, bool requireOutpost, LocationType forceLocationType = null, IEnumerable<Location> existingLocations = null)
        {
            return new Location(position, zone, biomeId, rand, requireOutpost, forceLocationType, existingLocations);
        }

        public void ChangeType(CampaignMode campaign, LocationType newType, bool createStores = true, bool unlockInitialMissions = true, bool createNetworkEvent = false)
        {
            LocationType previousType = Type;
            if (newType == previousType) { return; }

            if (newType == null)
            {
                DebugConsole.ThrowError($"Failed to change the type of the location \"{DisplayName}\" to null.\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            Type = newType;

            if (rawName != null)
            {
                DebugConsole.Log($"Location {rawName} changed it's type from {Type} to {newType}");
                DisplayName =
                    Type.NameFormats == null || !Type.NameFormats.Any() ?
                    rawName :
                    Type.NameFormats[nameFormatIndex % Type.NameFormats.Count].Replace("[name]", rawName);
            }
            else
            {
                DebugConsole.Log($"Location {DisplayName.Value} changed it's type from {Type} to {newType}");
                DisplayName =
                    Type.NameFormats == null || !Type.NameFormats.Any() ?
                    TextManager.Get(nameIdentifier) :
                    Type.NameFormats[nameFormatIndex % Type.NameFormats.Count].Replace("[name]", TextManager.Get(nameIdentifier).Value);
            }

            if (!Type.ForceLocationName.IsEmpty)
            {
                nameIdentifier = Type.ForceLocationName;
                DisplayName = TextManager.Get(nameIdentifier).Fallback(nameIdentifier.Value);
            }

            TryAssignFactionBasedOnLocationType(campaign);

            PreviousFaction = Faction;

            // Changing an empty map node into a new outpost
            if (Type.HasOutpost && Type.OutpostTeam == CharacterTeamType.FriendlyNPC)
            {
                // If there is no hard-coded faction, let plugins provide faction candidates
                // (with weights) and randomize between them and the base game
                if (Type.Faction == Identifier.Empty &&
                    TrySelectFactionIncludingPlugins(campaign) is OutpostFactionCandidate selectedCandidate)
                {
                    Faction = selectedCandidate.PrimaryFaction;
                    SecondaryFaction = selectedCandidate.SecondaryFaction;
                }
                else // No plugins or none of them got selected, use default random assignment
                {
                    if (Type.Faction == Identifier.Empty)
                    {
                        Faction ??= campaign.GetRandomFaction(Rand.RandSync.Unsynced);
                    }
                    if (Type.SecondaryFaction == Identifier.Empty)
                    {
                        SecondaryFaction ??= campaign.GetRandomSecondaryFaction(Rand.RandSync.Unsynced);
                    }
                }
            }
            else
            {
                if (Type.Faction == Identifier.Empty) { Faction = null; }
                if (Type.SecondaryFaction == Identifier.Empty) { SecondaryFaction = null; }
            }

            //initial population no longer known
            InitialPopulation = BasePopulation = -1;

            if (unlockInitialMissions && !IsCriticallyRadiated())
            {
                UnlockInitialMissions(Rand.RandSync.Unsynced);
            }

            if (campaign?.Map is Map map)
            {
                foreach (Location location in map.Locations)
                {
                    foreach (Mission mission in location.AvailableMissions)
                    {
                        mission.CheckDestinationStillValid();
                    }
                }
            }

            if (createStores)
            {
                // If there was a location type change during a round, the merchants in the level still use the current stores,
                // recreating them should wait until the round ends
                if (GameMain.GameSession is { IsRunning: true })
                {
                    recreateStoresAfterRound = true;
                    storesAddedAfterTypeChange.Clear();
                }
                else
                {
                    CreateStores(force: true);
                }
            }
            else
            {
                ClearStores();
            }

            // The hireable jobs and factions may have changed so reset the characters (clients receive them from the server)
            if (GameMain.NetworkMember is not { IsClient: true })
            {
                ResetHireableCharacters();
            }

            foreach (var hook in PluginData.GetHooks<PluginOnLocationTypeChangedDelegate>())
            {
                hook(this, previousType);
            }

#if SERVER
            if (createNetworkEvent)
            {
                foreach (var client in GameMain.Server.ConnectedClients)
                {
                    ModifyLocationAction.SendNetworkEvent(this, client);
                }
            }
            // If mid-round, the clients need the refreshed hires right away so their hires don't contradict the server
            if (GameMain.GameSession is { IsRunning: true } && campaign is MultiPlayerCampaign mpCampaign && this == mpCampaign.CurrentLocation)
            {
                mpCampaign.SendCrewState(createNotification: false);
            }
#endif
        }

        public void TryAssignFactionBasedOnLocationType(CampaignMode campaign)
        {
            if (campaign == null) { return; }
            if (Type.Faction != Identifier.Empty)
            {
                Faction = Type.Faction == "None" ? null : TryFindFaction(Type.Faction);
            }
            if (Type.SecondaryFaction != Identifier.Empty)
            {
                SecondaryFaction = Type.SecondaryFaction == "None" ? null : TryFindFaction(Type.SecondaryFaction);
            }

            Faction TryFindFaction(Identifier identifier)
            {
                var faction = campaign.GetFaction(identifier);
                if (faction == null)
                {
                    DebugConsole.ThrowError($"Error in location type \"{Type.Identifier}\": failed to find a faction with the identifier \"{identifier}\".",
                        contentPackage: Type.ContentPackage);
                }
                return faction;
            }
        }

        /// <summary>
        /// Collect faction candidates from plugins and perform weighted selection.
        /// A plugin will always override both primary and secondary faction, even
        /// if the latter might be empty, to avoid weird lore-incompatible mixes.
        /// </summary>
        /// <returns>The selected candidate, or null if the base game's default selection should be used.</returns>
        private OutpostFactionCandidate? TrySelectFactionIncludingPlugins(CampaignMode campaign)
        {
            var candidates = new List<OutpostFactionCandidate>();
            foreach (var hook in PluginData.GetHooks<PluginNewOutpostFactionCandidateDelegate>())
            {
                hook(this, campaign, candidates);
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            // Check for override first (first one simply wins, absolute overrides aren't intended to be mixed)
            foreach (OutpostFactionCandidate candidate in candidates)
            {
                if (candidate.OverrideCompletely)
                {
                    return candidate;
                }
            }

            // Add base game default as an option represented by null
            var candidatesWithDefault = candidates.Select(c => (OutpostFactionCandidate?)c).Append(null).ToList();

            return ToolBox.SelectWeightedRandom(
                candidatesWithDefault,
                c => c?.Weight ?? 1f, // Base game default option has weight 1
                Rand.RandSync.Unsynced);
        }

        public int GetAvailableMissionIndex(Mission mission)
        {
            return availableMissions.IndexOf(mission);
        }

        public void UnlockInitialMissions(Rand.RandSync randSync = Rand.RandSync.ServerAndClient)
        {
            if (Type.MissionIdentifiers.Any())
            {
                UnlockMissionByIdentifier(Type.MissionIdentifiers.GetRandom(randSync), invokingContentPackage: Type.ContentPackage);
            }
            if (Type.MissionTags.Any())
            {
                UnlockMissionByTag(Type.MissionTags.GetRandom(randSync), invokingContentPackage: Type.ContentPackage);
            }
        }

        public Mission UnlockMission(MissionPrefab missionPrefab, LocationConnection connection)
        {
            if (HasDuplicateMission(missionPrefab)) { return null; }
            if (AvailableMissions.Any(m => !m.Prefab.AllowOtherMissionsInLevel)) { return null; }
            var newMission = InstantiateMission(missionPrefab, connection);
            TryRemovingExistingMissionsIfNeeded(newMission, out bool isBlockedByOtherMissions);
            if (isBlockedByOtherMissions) { return null; }
            AddMission(newMission);
            return newMission;
        }

        /// <summary>
        /// "Forcibly" unlocks a mission, ignoring any restrictions that would normally prevent it from being unlocked (e.g. duplicate missions, other missions in the level, etc.)
        /// </summary>
        public Mission ForceUnlockMission(MissionPrefab missionPrefab, LocationConnection connection)
        {
            var newMission = InstantiateMission(missionPrefab, connection);
            AddMission(newMission);
            return newMission;
        }

        public Mission UnlockMission(MissionPrefab missionPrefab)
        {
            if (HasDuplicateMission(missionPrefab)) { return null; }
            if (AvailableMissions.Any(m => !m.Prefab.AllowOtherMissionsInLevel)) { return null; }
            var newMission = InstantiateMission(missionPrefab);
            TryRemovingExistingMissionsIfNeeded(newMission, out bool isBlockedByOtherMissions);
            if (isBlockedByOtherMissions) { return null; }
            AddMission(newMission);
            return newMission;
        }

        /// <summary>
        /// "Forcibly" unlocks a mission, ignoring any restrictions that would normally prevent it from being unlocked (e.g. duplicate missions, other missions in the level, etc.)
        /// </summary>
        public Mission ForceUnlockMission(MissionPrefab missionPrefab)
        {
            var newMission = InstantiateMission(missionPrefab);
            AddMission(newMission);
            return newMission;
        }

        public Mission UnlockMissionByIdentifier(Identifier identifier, LocationConnection forceConnection = null, ContentPackage invokingContentPackage = null)
        {
            if (AvailableMissions.Any(m => !m.Prefab.AllowOtherMissionsInLevel)) { return null; }

            var missionPrefab = MissionPrefab.Prefabs.Find(mp => mp.Identifier == identifier);

            if (missionPrefab == null)
            {
                DebugConsole.ThrowError($"Failed to unlock a mission with the identifier \"{identifier}\": matching mission not found.", 
                    contentPackage: invokingContentPackage);
            }
            else
            {
                if (HasDuplicateMission(missionPrefab)) { return null; }
                var mission =
                    forceConnection == null ?
                    InstantiateMission(missionPrefab, out LocationConnection _) :
                    InstantiateMission(missionPrefab, forceConnection);
                if (mission == null)
                {
                    DebugConsole.ThrowError($"Failed to unlock mission \"{missionPrefab.Identifier}\": no suitable location or connection to unlock in.",
                        contentPackage: invokingContentPackage);
                    return null;
                }
                //don't allow duplicate missions in the same connection
                if (HasDuplicateMission(missionPrefab, mission.Locations))
                {
                    return null;
                }
                TryRemovingExistingMissionsIfNeeded(mission, out bool isBlockedByOtherMissions);
                if (isBlockedByOtherMissions) { return null; }
                AddMission(mission);
                DebugConsole.NewMessage($"Unlocked a mission by \"{identifier}\".", debugOnly: true);
                return mission;
            }
            return null;
        }

        /// <summary>
        /// Is there already a mission of the same type available, preventing another one from being unlocked?
        /// Always false if the prefab allows multiple missions of the same type in the same level.
        /// </summary>
        /// <param name="missionLocations">If set to an array with two Locations, only missions that take place
        /// between these specific locations are taken into account (rather than checking all missions available from this location).
        /// For automatically selected missions, missions available from the location at the other end are checked too:
        /// a mission from A to B and from B to A would both get selected when traversing the level in either direction.
        /// Selectable missions (e.g. cargo from A to B and from B to A) are allowed.</param>
        private bool HasDuplicateMission(MissionPrefab missionPrefab, Location[] missionLocations = null)
        {
            if (missionPrefab.AllowMultipleInSameLevel) { return false; }
            if (missionLocations == null)
            {
                return AvailableMissions.Any(m => m.Prefab == missionPrefab);
            }
            if (HasDuplicate(this)) { return true; }
            if (missionPrefab.IsSelectedAutomatically)
            {
                //for auto-selected missions, check the location at the other end too
                Location otherLocation = missionLocations[0] == this ? missionLocations[1] : missionLocations[0];
                if (otherLocation != null && otherLocation != this && HasDuplicate(otherLocation)) { return true; }
            }
            return false;

            bool HasDuplicate(Location location)
            {
                return location.AvailableMissions.Any(m =>
                    m.Prefab == missionPrefab &&
                    m.Locations.Contains(missionLocations[0]) && m.Locations.Contains(missionLocations[1]));
            }
        }

        /// <summary>
        /// Attempts to remove existing missions in the level if the new mission requires it or if there are missions of the same type that aren't allowed to take place in the same level.
        /// </summary>
        /// <param name="isBlockedByOtherMissions">True if there were missions in the level that block the new mission from being added, but which couldn't be removed or replaced.</param>
        private void TryRemovingExistingMissionsIfNeeded(Mission mission, out bool isBlockedByOtherMissions)
        {
            isBlockedByOtherMissions = false;

            if (mission.Prefab.ReplaceOtherMissionsInLevel)
            {
                foreach (var availableMission in AvailableMissions.ToList())
                {
                    if (availableMission.Prefab.AllowReplacingWithOtherMissions &&
                        //same location or path
                        availableMission.Locations.Contains(mission.Locations[0]) && availableMission.Locations.Contains(mission.Locations[1]))
                    {
                        availableMissions.Remove(availableMission);
                    }
                }
            }
            foreach (var availableMission in AvailableMissions.ToList())
            {
                if (availableMission.Prefab.DisallowMissionsOfSameType && availableMission.Prefab.Type == mission.Prefab.Type &&
                    //same location or path
                    availableMission.Locations.Contains(mission.Locations[0]) && availableMission.Locations.Contains(mission.Locations[1]))
                {
                    if (availableMission.Prefab.AllowReplacingWithMissionOfSameType)
                    {
                        availableMissions.Remove(availableMission);
                        break;
                    }
                    else
                    {
                        isBlockedByOtherMissions = true;
                        return;
                    }
                }
            }
        }

        public Mission UnlockMissionByTag(Identifier tag, Random random = null, LocationConnection forceConnection = null, ContentPackage invokingContentPackage = null, float? overrideLevelDifficulty = null)
        {
            if (AvailableMissions.Any(m => !m.Prefab.AllowOtherMissionsInLevel)) { return null; }
            var matchingMissions = MissionPrefab.Prefabs.Where(mp => mp.Tags.Contains(tag));
            if (matchingMissions.None())
            {
                DebugConsole.ThrowError($"Failed to unlock a mission with the tag \"{tag}\": no matching missions found.", contentPackage: invokingContentPackage);
            }
            else
            {
                var unusedMissions = matchingMissions.Where(m => !HasDuplicateMission(m));
                if (unusedMissions.Any())
                {
                    var suitableMissions = unusedMissions.Where(m => Connections.Any(c => m.IsAllowed(this, c.OtherLocation(this)) || m.IsAllowed(this, this)));
                    //if we must choose a specific connection, filter out missions that are disallowed in it
                    if (forceConnection != null)
                    {
                        suitableMissions = suitableMissions.Where(m =>
                            !m.DisallowMissionsOfSameType ||
                            forceConnection.AvailableMissions.None(existingMission => existingMission.Prefab.Type == m.Type));
                        //must choose a specific connection but no mission is suitable, can't unlock anything
                        if (suitableMissions.None()) { return null; }
                    }

                    if (suitableMissions.None())
                    {
                        suitableMissions = unusedMissions;
                    }

                    float levelDifficulty = overrideLevelDifficulty ?? LevelData.Difficulty;
                    var filteredMissions = suitableMissions.Where(m => levelDifficulty >= m.MinLevelDifficulty && levelDifficulty <= m.MaxLevelDifficulty);
                    if (filteredMissions.None())
                    {
                        DebugConsole.AddWarning($"No suitable mission matching the level difficulty {levelDifficulty} found with the tag \"{tag}\". Ignoring the restriction.",
                            contentPackage: invokingContentPackage);
                    }
                    else
                    {
                        suitableMissions = filteredMissions;
                    }
                    MissionPrefab missionPrefab =
                        random != null ?
                        ToolBox.SelectWeightedRandom(suitableMissions, m => m.Commonness, random) :
                        ToolBox.SelectWeightedRandom(suitableMissions, m => m.Commonness, Rand.RandSync.Unsynced);

                    var mission =
                        forceConnection == null ?
                        InstantiateMission(missionPrefab, out LocationConnection _) :
                        InstantiateMission(missionPrefab, forceConnection);
                    if (mission == null)
                    {
                        DebugConsole.ThrowError($"Failed to unlock mission \"{missionPrefab.Identifier}\": no suitable location or connection to unlock in.",
                            contentPackage: invokingContentPackage);
                        return null;
                    }
                    //don't allow duplicate missions in the same connection
                    if (HasDuplicateMission(missionPrefab, mission.Locations))
                    {
                        return null;
                    }

                    var connection = GetConnection(mission.Locations[1]);

                    TryRemovingExistingMissionsIfNeeded(mission, out bool isBlockedByOtherMissions);
                    if (isBlockedByOtherMissions) { return null; }
                    AddMission(mission);
                    DebugConsole.NewMessage($"Unlocked a random mission by \"{tag}\": {mission.Prefab.Identifier} (difficulty level: {LevelData.Difficulty})", debugOnly: true);
                    return mission;
                }
                else if (availableMissions.Any())
                {
                    DebugConsole.NewMessage(
                        $"Failed to unlock a mission with the tag \"{tag}\" in the location \"{DisplayName}\":" +
                        " The location already has the same or similar missions unlocked, preventing futher ones from being added.");
                }
                else
                {
                    DebugConsole.AddWarning($"Failed to unlock a mission with the tag \"{tag}\" in the location \"{DisplayName}\": No suitable missions found.",
                        contentPackage: invokingContentPackage);

                }
            }

            return null;
        }

        public void AddMission(Mission mission)
        {
            if (!mission.Prefab.AllowOtherMissionsInLevel)
            {
                availableMissions.Clear();
            }
            System.Diagnostics.Debug.Assert(!availableMissions.Contains(mission), $"Attempted to the mission \"{mission.Prefab.Name}\" to the location \"{DisplayName}\" twice!");
            availableMissions.Add(mission);
#if CLIENT
            GameMain.GameSession?.Campaign?.CampaignUI?.RefreshLocationInfo();
            if (!mission.Prefab.MapNotificationOnUnlock.IsNullOrEmpty() &&
                /*only create the notification for the start location (so it doesn't appear twice)*/
                this == mission.Locations[0])
            {
                GameMain.GameSession?.Campaign?.Map.AddMapNotification(
                    TextManager.GetWithVariables(mission.Prefab.MapNotificationOnUnlock,
                    ("[location]", $"‖color:gui.yellow‖{mission.Locations[0].DisplayName}‖end‖" ),
                    ("[location1]", $"‖color:gui.yellow‖{mission.Locations[0].DisplayName}‖end‖"),
                    ("[location2]", $"‖color:gui.yellow‖{mission.Destination.DisplayName}‖end‖")),
                    relatedLocation: mission.Locations[0]);
            }
#else
            (GameMain.GameSession?.Campaign as MultiPlayerCampaign)?.IncrementLastUpdateIdForFlag(MultiPlayerCampaign.NetFlags.MapAndMissions);
#endif
        }

        private Mission InstantiateMission(MissionPrefab prefab, out LocationConnection connection)
        {
            if (prefab.IsAllowed(this, this))
            {
                connection = null;
                return InstantiateMission(prefab);
            }
            else if (prefab.AllowedLocationTypes.Any() && prefab.AllowedConnectionTypes.None())
            {
                //mission is configured to trigger in locations, not connections -> we can't trigger it
                connection = null;
                return null;
            }

            var suitableConnections = Connections.Where(c => 
                    prefab.IsAllowed(this, c.OtherLocation(this)) &&
                    c.AvailableMissions.None(m => m.Prefab.DisallowMissionsOfSameType && m.Prefab.Type == prefab.Type));
            if (suitableConnections.None())
            {
                suitableConnections = Connections.ToList();
            }
            //prefer connections that haven't been passed through, and connections with fewer available missions
            connection = ToolBox.SelectWeightedRandom(
                suitableConnections.ToList(),
                suitableConnections.Select(c => GetConnectionWeight(this, c)).ToList(),
                Rand.RandSync.Unsynced);

            static float GetConnectionWeight(Location location, LocationConnection c)
            {
                Location destination = c.OtherLocation(location);
                if (destination == null) { return 0; }

                int visibleMissionCount = location.availableMissions.Count(m => m.ShowInMenus && m.Locations.Contains(destination));
                if (destination.Type.TryToGuaranteeAsMissionDestination && visibleMissionCount == 0)
                {
                    return 100000.0f;
                }

                float minWeight = 0.0001f;
                float lowWeight = 0.2f;
                float normalWeight = 1.0f;
                float maxWeight = 2.0f;
                float weight = c.Passed ? lowWeight : normalWeight;
                if (location.Biome.AllowedZones.Contains(1))
                {
                    // In the first biome, give a stronger preference for locations that are farther to the right)
                    float diff = destination.MapPosition.X - location.MapPosition.X;
                    if (diff < 0)
                    {
                        weight *= 0.1f;
                    }
                    else
                    {
                        float maxRelevantDiff = 300;
                        weight = MathHelper.Lerp(weight, maxWeight, MathUtils.InverseLerp(0, maxRelevantDiff, diff));
                    }
                }
                else if (destination.MapPosition.X > location.MapPosition.X)
                {
                    weight *= 2.0f;
                }
                if (visibleMissionCount > 0) 
                { 
                    weight /= visibleMissionCount * 2;
                }
                if (destination.IsRadiated())
                {
                    weight *= 0.001f;
                }
                return MathHelper.Clamp(weight, minWeight, maxWeight) * destination.Type.MissionDestinationPreferability;
            }

            return InstantiateMission(prefab, connection);
        }

        private Mission InstantiateMission(MissionPrefab prefab, LocationConnection connection)
        {
            Location destination = connection.OtherLocation(this);
            var mission = prefab.Instantiate(new Location[] { this, destination }, Submarine.MainSub);
            mission.AdjustLevelData(connection.LevelData);
            return mission;
        }

        private Mission InstantiateMission(MissionPrefab prefab)
        {
            var mission = prefab.Instantiate(new Location[] { this, this }, Submarine.MainSub);
            mission.AdjustLevelData(LevelData);
            return mission;
        }

        public void InstantiateLoadedMissions(Map map)
        {
            availableMissions.Clear();
            selectedMissions.Clear();
            if (loadedMissions != null && loadedMissions.Any()) 
            { 
                foreach (LoadedMission loadedMission in loadedMissions)
                {
                    Location destination;
                    if (loadedMission.DestinationIndex >= 0 && loadedMission.DestinationIndex < map.Locations.Count)
                    {
                        destination = map.Locations[loadedMission.DestinationIndex];
                    }
                    else
                    {
                        destination = Connections.First().OtherLocation(this);
                    }
                    var mission = loadedMission.MissionPrefab.Instantiate([this, destination], Submarine.MainSub);
                    if (loadedMission.OriginLocationIndex >= 0 && loadedMission.OriginLocationIndex < map.Locations.Count)
                    {
                        mission.OriginLocation = map.Locations[loadedMission.OriginLocationIndex];
                    }
                    mission.WorldStepsSinceUnlocked = loadedMission.WorldStepsSinceUnlocked;
                    mission.WorldStepsUntilExpiry = loadedMission.WorldStepsUntilExpiry;
                    mission.TimesAttempted = loadedMission.TimesAttempted;
                    List<Location> pathToDestination = new List<Location>();
                    foreach (int pathLocationIndex in loadedMission.PathLocationIndices)
                    {
                        if (pathLocationIndex >= 0 && pathLocationIndex < map.Locations.Count)
                        {
                            pathToDestination.Add(map.Locations[pathLocationIndex]);
                        }
                        else
                        {
                            DebugConsole.AddWarning($"Failed to load the remaining path for the multi-level mission {mission.Name} ({mission.Prefab.Identifier}): location index {pathLocationIndex} out of bounds.");
                            break;
                        }
                    }
                    if (pathToDestination.Any())
                    {
                        mission.SetPathToDestination(pathToDestination);
                    }

                    availableMissions.Add(mission);
                    if (loadedMission.SelectedMission) { selectedMissions.Add(mission); }

                    var levelData = destination == this ? LevelData : Connections.FirstOrDefault(c => c.OtherLocation(this) == destination)?.LevelData;
                    if (levelData != null)
                    {
                        mission.AdjustLevelData(levelData);
                    }
                }
                loadedMissions = null;
            }
            if (addInitialMissionsForType != null)
            {
                if (addInitialMissionsForType.MissionIdentifiers.Any())
                {
                    UnlockMissionByIdentifier(addInitialMissionsForType.MissionIdentifiers.GetRandomUnsynced(), invokingContentPackage: Type.ContentPackage);
                }
                if (addInitialMissionsForType.MissionTags.Any())
                {
                    UnlockMissionByTag(addInitialMissionsForType.MissionTags.GetRandomUnsynced(), invokingContentPackage: Type.ContentPackage);
                }
                addInitialMissionsForType = null;
            }
        }

        /// <summary>
        /// Removes all unlocked missions from the location
        /// </summary>
        public void ClearMissions()
        {
            availableMissions.Clear();
            selectedMissions.Clear();
        }

        /// <summary>
        /// Replaces the available missions with the specified ones, in the specified order. Used by clients to make the missions match the server's.
        /// </summary>
        public void SetAvailableMissions(IEnumerable<Mission> missions)
        {
            availableMissions.Clear();
            availableMissions.AddRange(missions);
            selectedMissions.RemoveAll(m => !availableMissions.Contains(m));
#if CLIENT
            GameMain.GameSession?.Campaign?.CampaignUI?.RefreshLocationInfo();
#endif
        }

        public bool HasOutpost()
        {
            if (!Type.HasOutpost) { return false; }

            return !IsCriticallyRadiated();
        }

        public bool IsCriticallyRadiated()
        {
            if (GameMain.GameSession?.Map?.Radiation != null)
            {
                return TurnsInRadiation > GameMain.GameSession.Map.Radiation.Params.CriticalRadiationThreshold;
            }

            return false;
        }

        public LocationType GetLocationTypeToDisplay(out Identifier overrideDescriptionIdentifier)
        {
            overrideDescriptionIdentifier = Identifier.Empty;
            if (IsCriticallyRadiated() && !Type.ReplaceInRadiation.IsEmpty)
            {
                if (LocationType.Prefabs.TryGet(Type.ReplaceInRadiation, out LocationType newLocationType))
                {
                    if (!newLocationType.DescriptionInRadiation.IsEmpty)
                    {
                        overrideDescriptionIdentifier = newLocationType.DescriptionInRadiation;
                    }
                    return newLocationType;
                }
                else
                {
                    DebugConsole.ThrowError($"Error when trying to get a new location type for an irradiated location - location type \"{newLocationType}\" not found.");
                }
            }
            return Type;
        }

        public LocationType GetLocationTypeToDisplay()
        {
            return GetLocationTypeToDisplay(out _);
        }

        public LocationConnection GetConnection(Location destination)
        {
            return Connections.FirstOrDefault(c => c.OtherLocation(this) == destination);
        }

        /// <summary>
        /// Returns missions originating from this location that take place in the specified connection.
        /// </summary>
        public IEnumerable<Mission> GetMissionsInConnection(LocationConnection connection)
        {
            System.Diagnostics.Debug.Assert(Connections.Contains(connection));
            return AvailableMissions.Where(m => m.Locations[1] == connection.OtherLocation(this));
        }
        
        public void RemoveHireableCharacter(CharacterInfo character)
        {
            if (!HasHireableCharacters())
            {
                DebugConsole.ThrowErrorLocalized("Cannot hire a character from location \"" + DisplayName + "\" - the location has no hireable characters.\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }
            if (HireManager == null)
            {
                DebugConsole.ThrowErrorLocalized("Cannot hire a character from location \"" + DisplayName + "\" - hire manager has not been instantiated.\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            HireManager.RemoveCharacter(character);
        }

        public bool HasHireableCharacters()
        {
            return
                Type.HasHireableCharacters ||
                (Faction != null && Faction.Prefab.HireableCharacters.Any()) ||
                (SecondaryFaction != null && SecondaryFaction.Prefab.HireableCharacters.Any());
        }

        public IEnumerable<CharacterInfo> GetHireableCharacters()
        {
            if (!HasHireableCharacters())
            {
                return [];
            }

            HireManager ??= new HireManager();
            if (!HireManager.AvailableCharacters.Any())
            {
                HireManager.GenerateCharacters(location: this, amount: HireManager.MaxAvailableCharacters);
            }
            return HireManager.AvailableCharacters;
        }

        public void ForceHireableCharacters(IEnumerable<CharacterInfo> hireableCharacters)
        {
            HireManager ??= new HireManager();
            HireManager.AvailableCharacters = hireableCharacters.ToList();
        }

        /// <summary>
        /// Discards the current hireable characters so they get regenerated on next access
        /// </summary>
        public void ResetHireableCharacters()
        {
            if (HireManager == null) { return; }
            HireManager.PendingHires.Clear();
            HireManager.Remove();
        }

        public void AssignRandomName(LocationType type, Random rand, IEnumerable<Location> existingLocations)
        {
            if (!type.ForceLocationName.IsEmpty)
            {
                nameIdentifier = type.ForceLocationName;
                DisplayName = TextManager.Get(nameIdentifier).Fallback(nameIdentifier.Value);
                return;
            }
            nameIdentifier = type.GetRandomNameId(rand, existingLocations);
            if (nameIdentifier.IsEmpty)
            {
                //backwards compatibility
                rawName = type.GetRandomRawName(rand, existingLocations);
                if (rawName.IsNullOrEmpty())
                {
                    DebugConsole.ThrowError($"Failed to generate a name for a location of the type {type.Identifier}. No names found in localization files or the .txt files.");
                    rawName = "none";
                }
                nameIdentifier = rawName.ToIdentifier();
                if (type.NameFormats == null || !type.NameFormats.Any())
                {
                    DisplayName = rawName;
                }
                else
                {
                    nameFormatIndex = rand.Next() % type.NameFormats.Count;
                    DisplayName = type.NameFormats[nameFormatIndex].Replace("[name]", rawName);
                }
            }
            else
            {
                if (type.NameFormats == null || !type.NameFormats.Any())
                {
                    DisplayName = TextManager.Get(nameIdentifier).Fallback(nameIdentifier.Value);
                    return;
                }
                nameFormatIndex = rand.Next() % type.NameFormats.Count;
                DisplayName = GetName(Type, nameFormatIndex, nameIdentifier);
            }
        }

        public static LocalizedString GetName(Identifier locationTypeIdentifier, int nameFormatIndex, Identifier nameId)
        {
            if (LocationType.Prefabs.TryGet(locationTypeIdentifier, out LocationType locationType))
            {
                return GetName(locationType, nameFormatIndex, nameId);
            }
            else
            {
                DebugConsole.ThrowError($"Could not find the location type {locationTypeIdentifier}.\n" + Environment.StackTrace.CleanUpPath());
                return new RawLString(nameId.Value);
            }
        }

        public static LocalizedString GetName(LocationType type, int nameFormatIndex, Identifier nameId)
        {
            if (type?.NameFormats == null || !type.NameFormats.Any() || nameFormatIndex < 0)
            {
                return TextManager.Get(nameId).Fallback(nameId.Value);
            }
            return type.NameFormats[nameFormatIndex % type.NameFormats.Count].Replace("[name]", TextManager.Get(nameId).Value);
        }

        /// <summary>
        /// Forces the name to a localized string fetched using the identifier.
        /// </summary>
        public void ForceName(Identifier nameId)
        {
            rawName = string.Empty;
            nameIdentifier = nameId;
            DisplayName = TextManager.Get(nameId).Fallback(nameId.Value);
        }

        /// <summary>
        /// Sanitizes the name: limiting the length to <see cref="MaxNameLength"/> and removing disallowed symbols.
        /// </summary>
        /// <returns>Returns an empty string if the resulting name is null, empty or whitespace.</returns>
        public static string SanitizeName(string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) { return string.Empty; }

            //reuse client name sanitization (removes disallowed symbols and limits the length)
            newName = Networking.Client.SanitizeName(newName, maxLength: MaxNameLength);

            if (string.IsNullOrWhiteSpace(newName)) 
            { 
                return string.Empty;
            }
            else
            {
                return newName;
            }
        }

        /// <summary>
        /// Forces the name to an unlocalized string.
        /// </summary>
        public bool TryForceRawName(string rawName)
        {
            rawName = SanitizeName(rawName);
            if (string.IsNullOrWhiteSpace(rawName)) { return false; }
            this.rawName = rawName;
            nameIdentifier = Identifier.Empty;
            DisplayName = rawName;

            return true;
        }

        /// <param name="createMissingStores">
        /// Should stores be created for the location's merchants that don't have one in the element?
        /// True when loading a save, which may predate merchants added by updates or mods.
        /// False when the element is the server's current state of the stores.
        /// </param>
        public void LoadStores(XElement locationElement, bool createMissingStores = true)
        {
            UpdateStoreIdentifiers();
            Stores?.Clear();

            // This needs to be loaded before any stores are created so they'll know their lifetime-limited stock
            lifetimeStockLeft.Clear();
            if (locationElement.GetChildElement("lifetimestock") is XElement lifetimeStockElement)
            {
                foreach (var storeStockElement in lifetimeStockElement.GetChildElements("store"))
                {
                    Identifier storeIdentifier = storeStockElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                    if (storeIdentifier.IsEmpty) { continue; }
                    foreach (var itemStockElement in storeStockElement.GetChildElements("item"))
                    {
                        Identifier itemIdentifier = itemStockElement.GetAttributeIdentifier("id", Identifier.Empty);
                        if (itemIdentifier.IsEmpty) { continue; }
                        SetLifetimeStockLeft(storeIdentifier, itemIdentifier,
                            Math.Max(itemStockElement.GetAttributeInt("left", 0), 0));
                    }
                }
            }

            bool hasStores = false;
            foreach (var storeElement in locationElement.GetChildElements("store"))
            {
                hasStores = true;
                Stores ??= new Dictionary<Identifier, StoreInfo>();
                var identifier = storeElement.GetAttributeIdentifier("identifier", "");
                if (identifier.IsEmpty)
                {
                    // Previously saved store data (with no identifier) is discarded and new store data will be created
                    continue;
                }
                StoreIdentifiers.Add(identifier);                
                if (!Stores.ContainsKey(identifier))
                {
                    Stores.Add(identifier, LoadStore(storeElement));
                }
                else
                {
                    string msg = $"Error loading store info for \"{identifier}\" at location {DisplayName} of type \"{Type.Identifier}\": duplicate identifier.";
                    DebugConsole.ThrowError(msg);
                    GameAnalyticsManager.AddErrorEventOnce("Location.LoadStore:DuplicateStoreInfo", GameAnalyticsManager.ErrorSeverity.Error, msg);
                    continue;
                }
            }
            // Backwards compatibility / mod compatibility: create new stores for any identifiers not present in the save data
            if (hasStores && createMissingStores)
            {
                foreach (var id in StoreIdentifiers)
                {
                    AddNewStore(id);
                }
            }
        }

        private StoreInfo LoadStore(XElement savedStoreElement)
        {
            // Plugins can load their custom stores, the first one to return a store wins
            foreach (var hook in PluginData.GetHooks<PluginLoadStoreDelegate>())
            {
                if (hook(this, savedStoreElement) is StoreInfo store)
                {
                    return store;
                }
            }
            return new StoreInfo(this, savedStoreElement);
        }

        public void SaveStores(XElement locationElement)
        {
            if (Stores != null)
            {
                foreach (var store in Stores.Values)
                {
                    locationElement.Add(store.Save());
                }
            }

            if (lifetimeStockLeft.Any())
            {
                var lifetimeStockElement = new XElement("lifetimestock");
                foreach (var storeStock in lifetimeStockLeft)
                {
                    var storeStockElement = new XElement("store",
                        new XAttribute("identifier", storeStock.Key));
                    foreach (var itemStock in storeStock.Value)
                    {
                        storeStockElement.Add(new XElement("item",
                            new XAttribute("id", itemStock.Key),
                            new XAttribute("left", itemStock.Value)));
                    }
                    lifetimeStockElement.Add(storeStockElement);
                }
                locationElement.Add(lifetimeStockElement);
            }
        }

        public bool IsRadiated() => GameMain.GameSession?.Map?.Radiation != null && GameMain.GameSession.Map.Radiation.Enabled && GameMain.GameSession.Map.Radiation.DepthInRadiation(this) > 0;

        /// <summary>
        /// Mark the items that have been taken from the outpost to prevent them from spawning when re-entering the outpost. Also give a rep penalty for any items that were stolen.
        /// </summary>
        public void RegisterTakenItems(IEnumerable<Item> items)
        {
            foreach (Item item in items)
            {
                if (takenItems.Any(it => it.Matches(item) && it.OriginalID == item.ID)) { continue; }
                if (item.IsSalvageMissionItem) { continue; }
                if (item.OriginalModuleIndex > -1)
                {
                    takenItems.Add(new TakenItem(item));
                }

                if (Level.IsLoadedOutpost &&
                    item.StolenDuringRound &&
                    item.Illegitimate)
                {
                    float reputationLoss = HumanAIController.ApplyStealingReputationLoss(item);
                    DebugConsole.NewMessage($"Applying reputation loss for stealing {item.Name} ({-reputationLoss}).", Color.MonoGameOrange);
                    //the theft has now been "paid for" with the reputation loss: stop treating the item as stolen contraband,
                    //so picking it up again later doesn't trigger security reactions or an additional reputation loss
                    item.AllowStealing = true;
                }
            }
        }

        /// <summary>
        /// Mark the characters who have been killed to prevent them from spawning when re-entering the outpost
        /// </summary>
        public void RegisterKilledCharacters(IEnumerable<Character> characters)
        {
            if (killedCharactersResetThisRound)
            {
                killedCharactersResetThisRound = false;
                return;
            }
            foreach (Character character in characters)
            {
                if (character?.Info == null) { continue; }
                killedCharacters.Add(new KilledCharacter(character.Info.GetIdentifier()));
            }
        }

        public void UnregisterKilledCharacters(IEnumerable<KilledCharacter> killedCharactersToUnregister)
        {
            foreach (var killedCharacter in killedCharactersToUnregister)
            {
                killedCharacters.Remove(killedCharacter);
            }
        }

        private bool killedCharactersResetThisRound;
        /// <summary>
        /// Clears the list of killed characters, making the population go back to <see cref="BasePopulation"/>.
        /// </summary>
        public void ResetKilledCharacters()
        {
            killedCharacters.Clear();
            killedCharactersResetThisRound = true;
        }

        public void RemoveTakenItems()
        {
            foreach (TakenItem takenItem in takenItems)
            {
                Item item = Item.ItemList.Find(it => takenItem.Matches(it));
                item?.Remove();
            }
        }

        public int GetAdjustedMechanicalCost(int cost)
        {
            float discount = 0.0f;
            if (Reputation != null)
            {
                discount = Reputation.Value / Reputation.MaxReputation * (MechanicalMaxDiscountPercentage / 100.0f);
            }
            return (int)Math.Ceiling((1.0f - discount) * cost * MechanicalPriceMultiplier);
        }

        public int GetAdjustedHealCost(int cost)
        {
            float discount = 0.0f;
            if (Reputation != null)
            {
                discount = Reputation.Value / Reputation.MaxReputation * (HealMaxDiscountPercentage / 100.0f);
            }           
            return (int) Math.Ceiling((1.0f - discount) * cost * PriceMultiplier);
        }

        public StoreInfo GetStore(Identifier identifier)
        {
            if (Stores != null && Stores.TryGetValue(identifier, out var store))
            {
                return store;
            }
            return null;
        }

        /// <summary>
        /// Create stores and stocks for the location. If the location already has stores, the method will not do anything unless the "force" argument is true. />
        /// </summary>
        /// <param name="force">If true, the stores will be recreated if they already exists.</param>
        public void CreateStores(bool force = false)
        {
            // In multiplayer, stores should be created by the server and loaded from save data by clients
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (!force && Stores != null) { return; }
            UpdateStoreIdentifiers();
            if (Stores != null)
            {
                // Remove any stores with no corresponding merchants at the location
                foreach (var storeIdentifier in Stores.Keys)
                {
                    if (!Stores[storeIdentifier].KeepWhenStoresRecreated)
                    {
                        Stores.Remove(storeIdentifier);
                    }
                }
                // Refresh all the kept stores, including ones kept for other reasons than the location's merchants (see KeepWhenStoresRecreated)
                foreach (var store in Stores.Values)
                {
                    if (storesAddedAfterTypeChange.Contains(store))
                    {
                        continue;
                    }
                    
                    store.Balance = Math.Max(store.Balance, StoreInitialBalance);
                    var newStock = store.CreateStock();
                    foreach (var oldStockItem in store.Stock)
                    {
                        if (newStock.Find(i => i.ItemPrefab == oldStockItem.ItemPrefab) is { } newStockItem)
                        {
                            if (oldStockItem.Quantity > newStockItem.Quantity)
                            {
                                newStockItem.Quantity = oldStockItem.Quantity;
                            }
                        }
                    }
                    store.Stock.Clear();
                    store.Stock.AddRange(newStock);
                    store.GenerateSpecials();
                    store.GeneratePriceModifier();
                }
                foreach (var identifier in StoreIdentifiers)
                {
                    // Does nothing if the store was kept
                    AddNewStore(identifier);
                }
            }
            else
            {
                foreach (var identifier in StoreIdentifiers)
                {
                    AddNewStore(identifier);
                }
            }
            NotifyStoresChanged();
        }

        public void UpdateStores(bool createStoresIfNotCreated = true)
        {
            // In multiplayer, stores should be updated by the server and loaded from save data by clients
            if (GameMain.NetworkMember is { IsClient: true }) { return; }
            if (Stores == null)
            {
                if (createStoresIfNotCreated) { CreateStores(); }
                return;
            }
            var storesToRemove = new HashSet<Identifier>();
            foreach (var store in Stores.Values)
            {
                if (store.Balance < StoreInitialBalance)
                {
                    store.Balance = Math.Min(store.Balance + (int)(StoreInitialBalance / 10.0f), StoreInitialBalance);
                }
                var stock = new List<PurchasedItem>(store.Stock);
                var stockToRemove = new List<PurchasedItem>();

                foreach (var itemPrefab in ItemPrefab.Prefabs)
                {
                    var existingStock = stock.FirstOrDefault(s => s.ItemPrefabIdentifier == itemPrefab.Identifier);
                    if (itemPrefab.CanBeBoughtFrom(store, out PriceInfo priceInfo))
                    {
                        if (existingStock == null)
                        {
                            //can be bought from the location, but not in stock - some new item added by an update or mod?
                            stock.Add(store.CreateInitialStockItem(itemPrefab, priceInfo));
                        }
                        else
                        {
                            int maxAmount = priceInfo.HasLifetimeLimit ?
                                Math.Min(priceInfo.MaxAvailableAmount, GetLifetimeStockLeft(store.Identifier, itemPrefab.Identifier, priceInfo)) :
                                priceInfo.MaxAvailableAmount;
                            existingStock.Quantity =
                                Math.Min(
                                    existingStock.Quantity + 1,
                                    maxAmount);
                        }
                    }
                    else if (existingStock != null)
                    {
                        stockToRemove.Add(existingStock);
                    }
                }

                stockToRemove.ForEach(i => stock.Remove(i));
                store.Stock.Clear();
                store.Stock.AddRange(stock);
                store.GeneratePriceModifier();
                store.StepsSinceSpecialsUpdated++;
            }

            foreach (var identifier in storesToRemove)
            {
                Stores.Remove(identifier);
            }
            foreach (var identifier in StoreIdentifiers)
            {
                AddNewStore(identifier);
            }
            NotifyStoresChanged();
        }

        public void UpdateSpecials()
        {
            if (GameMain.NetworkMember is { IsClient: true } || Stores is null) { return; }

            int extraSpecialSalesCount = GetExtraSpecialSalesCount();

            foreach (StoreInfo store in Stores.Values)
            {
                if (store.StepsSinceSpecialsUpdated < SpecialsUpdateInterval && store.DailySpecials.Count == DailySpecialsCount + extraSpecialSalesCount &&
                    // Regenerate if some of the specials have been marked up since they were generated
                    store.DailySpecials.None(item => store.IsMarkedUp(item)))
                {
                    continue; 
                }

                store.GenerateSpecials();
                NotifyStoresChanged();
            }
        }

        private void UpdateStoreIdentifiers()
        {
            StoreIdentifiers.Clear();
            foreach (var outpostParam in OutpostGenerationParams.OutpostParams)
            {
                if (!outpostParam.AllowedLocationTypes.Contains(Type.Identifier)) { continue; }
                foreach (var identifier in outpostParam.GetStoreIdentifiers())
                {
                    StoreIdentifiers.Add(identifier);
                }
            }
            foreach (var identifier in Type.ForcedStoreIdentifiers)
            {
                StoreIdentifiers.Add(identifier);
            }
        }

        public void AddNewStore(Identifier identifier)
        {
            if (Stores != null && Stores.ContainsKey(identifier)) { return; }
            AddOrReplaceStore(new StoreInfo(this, identifier));
        }

        /// <summary>
        /// Adds the store to the location, replacing any existing store with the same identifier.
        /// </summary>
        public void AddOrReplaceStore(StoreInfo store)
        {
            Stores ??= new Dictionary<Identifier, StoreInfo>();
            Stores[store.Identifier] = store;
            if (recreateStoresAfterRound)
            {
                storesAddedAfterTypeChange.Add(store);
            }
            NotifyStoresChanged();
        }

        private void NotifyStoresChanged()
        {
#if SERVER
            (GameMain.GameSession?.Campaign as MultiPlayerCampaign)?.NotifyStoresChanged(this);
#endif
        }

        public void AddStock(Dictionary<Identifier, List<SoldItem>> items)
        {
            if (items == null) { return; }
            foreach (var storeItems in items)
            {
                if (GetStore(storeItems.Key) is { } store)
                {
                    store.AddStock(storeItems.Value);
                }
            }
        }

        /// <summary>
        /// Removes all information about stores from the location (can be used to avoid storing unnecessary 
        /// store info about locations that haven't been visited in a long time). The stores are automatically
        /// recreated when the player enters the location.
        /// </summary>
        public void ClearStores()
        {
            Stores = null;
            recreateStoresAfterRound = false;
            storesAddedAfterTypeChange.Clear();
            NotifyStoresChanged();
        }

        /// <summary>
        /// Recreates the stores if the location type changed during the round, called when the round has ended.
        /// </summary>
        public void RecreateStoresIfTypeChangedDuringRound()
        {
            if (!recreateStoresAfterRound) { return; }
            recreateStoresAfterRound = false;
            CreateStores(force: true);
            storesAddedAfterTypeChange.Clear();
        }

        public void RemoveStock(Dictionary<Identifier, List<PurchasedItem>> items)
        {
            if (items == null) { return; }
            foreach (var storeItems in items)
            {
                if (GetStore(storeItems.Key) is { } store)
                {
                    store.RemoveStock(storeItems.Value);
                }
            }
        }

        public static int GetExtraSpecialSalesCount()
        {
            var characters = GameSession.GetSessionCrewCharacters(CharacterType.Both);
            if (!characters.Any()) { return 0; }
            return characters.Max(static c => (int)c.GetStatValue(StatTypes.ExtraSpecialSalesCount));
        }

        public bool CanHaveSubsForSale()
        {
            return HasOutpost() && CanHaveCampaignInteraction(CampaignMode.InteractionType.PurchaseSub);
        }

        public int HighestSubmarineTierAvailable(SubmarineClass submarineClass = SubmarineClass.Undefined)
        {
            if (CanHaveSubsForSale())
            {
                if (Biome == null) { return SubmarineInfo.HighestTier; }
                return Biome.HighestSubmarineTierAvailable(submarineClass, Type.SubmarineTierLocationType.IsEmpty ? Type.Identifier : Type.SubmarineTierLocationType);
            }
            return 0;
        }

        public bool IsSubmarineAvailable(SubmarineInfo info)
        {
            if (info.SoldByFaction.Any())
            {
                if (Faction == null) { return false; }
                if (!info.SoldByFaction.Contains(Faction.Prefab.Identifier)) { return false; }
            }
            else
            {
                //SoldByFaction is empty = can be by default sold by any faction, but the location type can override that
                if (!Type.SellSubmarinesSoldByAnyFaction) { return false; }
            }
            if (Biome == null) { return true; }
            return Biome.IsSubmarineAvailable(info, Type.SubmarineTierLocationType.IsEmpty ? Type.Identifier : Type.SubmarineTierLocationType);
        }

        private  bool CanHaveCampaignInteraction(CampaignMode.InteractionType interactionType)
        {
            return LevelData != null &&
                LevelData.OutpostGenerationParamsExist &&
                LevelData.GetSuitableOutpostGenerationParams(this, LevelData).Any(p => p.CanHaveCampaignInteraction(interactionType));
        }

        public void Reset(CampaignMode campaign)
        {
            if (Type != OriginalType && !DisallowLocationTypeChanges)
            {
                ChangeType(campaign, OriginalType);
                PendingLocationTypeChange = null;
            }
            //otherwise the previous type change would still be shown in the news and in the type change icon's tooltip
            LastTypeChangeMessage = string.Empty;
            ClearStores();
            ClearMissions();
            LevelData?.EventHistory?.Clear();
            UnlockInitialMissions();
        }

        public XElement Save(Map map, XElement parentElement)
        {
            var locationElement = new XElement("location",
                new XAttribute("type", Type.Identifier),
                new XAttribute("originaltype", (OriginalType ?? Type).Identifier),
                /*not used currently (we load the nameIdentifier instead), 
                 * but could make sense to include still for backwards compatibility reasons*/
                new XAttribute("name", DisplayName),
                new XAttribute("biome", Biome?.Identifier.Value ?? string.Empty),
                new XAttribute("position", XMLExtensions.Vector2ToString(MapPosition)),
                new XAttribute(nameof(PriceMultiplier), PriceMultiplier),
                new XAttribute(nameof(IsGateBetweenBiomes), IsGateBetweenBiomes),
                new XAttribute(nameof(MechanicalPriceMultiplier), MechanicalPriceMultiplier),
                new XAttribute(nameof(TimeSinceLastTypeChange), TimeSinceLastTypeChange),
                new XAttribute(nameof(LastTypeChangeMessage), LastTypeChangeMessage ?? string.Empty),
                new XAttribute(nameof(TurnsInRadiation).ToLower(), TurnsInRadiation),
                new XAttribute(nameof(WorldStepsSinceVisited), WorldStepsSinceVisited),
                new XAttribute(nameof(TimesVisited), TimesVisited));

            if (InitialPopulation > -1)
            {
                locationElement.Add(new XAttribute(nameof(InitialPopulation), InitialPopulation));
            }
            if (BasePopulation > -1)
            {
                locationElement.Add(new XAttribute(nameof(BasePopulation), BasePopulation));
            }

            if (!rawName.IsNullOrEmpty())
            {
                locationElement.Add(new XAttribute(nameof(rawName), rawName));
            }
            else
            {
                locationElement.Add(new XAttribute(nameof(nameIdentifier), nameIdentifier));
                locationElement.Add(new XAttribute(nameof(nameFormatIndex), nameFormatIndex));
            }

            if (Faction != null)
            {
                locationElement.Add(new XAttribute(nameof(Faction), Faction.Prefab.Identifier));
            }
            if (SecondaryFaction != null)
            {
                locationElement.Add(new XAttribute(nameof(SecondaryFaction), SecondaryFaction.Prefab.Identifier));
            }
            if (OriginalFaction != null)
            {
                locationElement.Add(new XAttribute(nameof(OriginalFaction), OriginalFaction.Prefab.Identifier)); 
            }
            if (PreviousFaction != null)
            {
                locationElement.Add(new XAttribute(nameof(PreviousFaction), PreviousFaction.Prefab.Identifier)); 
            }

            LevelData.Save(locationElement);

            for (int i = 0; i < Type.CanChangeTo.Count; i++)
            {
                for (int j = 0; j < Type.CanChangeTo[i].Modifiers.Count; j++)
                {
                    if (ProximityTimer.ContainsKey(Type.CanChangeTo[i].Modifiers[j]))
                    {
                        locationElement.Add(new XAttribute("proximitytimer" + i + "-" + j, ProximityTimer[Type.CanChangeTo[i].Modifiers[j]]));
                    }
                }
            }

            if (PendingLocationTypeChange.HasValue)
            {
                var changeElement = new XElement("pendinglocationtypechange", new XAttribute("timer", PendingLocationTypeChange.Value.delay));
                if (PendingLocationTypeChange.Value.parentMission != null)
                {
                    changeElement.Add(new XAttribute("missionidentifier", PendingLocationTypeChange.Value.parentMission.Identifier));
                    locationElement.Add(changeElement);
                }
                else
                {
                    int index = Type.CanChangeTo.IndexOf(PendingLocationTypeChange.Value.typeChange);
                    changeElement.Add(new XAttribute("index", index));
                    if (index == -1)
                    {
                        DebugConsole.AddWarning($"Invalid location type change in the location \"{DisplayName}\". Unknown type change ({PendingLocationTypeChange.Value.typeChange.ChangeToType}).");
                    }
                    else
                    {
                        locationElement.Add(changeElement);
                    }
                }
            }

            if (LocationTypeChangeCooldown > 0)
            {
                locationElement.Add(new XAttribute("locationtypechangecooldown", LocationTypeChangeCooldown));
            }

            if (takenItems.Any())
            {
                locationElement.Add(new XAttribute(
                    "takenitems",
                    string.Join(',', takenItems.Select(it => it.Identifier + ";" + it.OriginalID + ";" + it.OriginalContainerIndex + ";" + it.ModuleIndex))));
            }
            if (killedCharacters.Any())
            {
                foreach (var killedCharacter in KilledCharacters)
                {
                    killedCharacter.Save(locationElement);
                }
            }

            SaveStores(locationElement);

            if (AvailableMissions is List<Mission> missions && missions.Any())
            {
                var missionsElement = new XElement("missions");
                foreach (Mission mission in missions)
                {
                    var location = mission.Locations.All(l => l == this) ? this : mission.Locations.FirstOrDefault(l => l != this);
                    int destinationIndex = map.Locations.IndexOf(location);
                    int originIndex = map.Locations.IndexOf(mission.OriginLocation);

                    var missionElement =
                        new XElement("mission",
                            new XAttribute("prefabid", mission.Prefab.Identifier),
                            new XAttribute("destinationindex", destinationIndex),
                            new XAttribute(nameof(Mission.TimesAttempted), mission.TimesAttempted),
                            new XAttribute("origin", originIndex),
                            new XAttribute("worldstepssinceunlocked", mission.WorldStepsSinceUnlocked),
                            new XAttribute("selected", selectedMissions.Contains(mission)));
                    if (mission.WorldStepsUntilExpiry > -1)
                    {
                        missionElement.Add(new XAttribute(nameof(mission.WorldStepsUntilExpiry), mission.WorldStepsUntilExpiry));
                    }
                    if (mission.PathToDestination != null)
                    {
                        missionElement.Add(
                            new XAttribute(nameof(LoadedMission.PathLocationIndices), string.Join(',', mission.PathToDestination.Select(map.Locations.IndexOf))));
                    }
                    missionsElement.Add(missionElement);

                }
                locationElement.Add(missionsElement);
            }

            foreach (var hook in PluginData.GetHooks<PluginLocationSaveDelegate>())
            {
                hook(this, map, locationElement);
            }

            parentElement.Add(locationElement);

            return locationElement;
        }

        public void Remove()
        {
            LoadOutpost = null;
            RemoveProjSpecific();
        }

        public void RemoveProjSpecific()
        {
            HireManager?.Remove();
        }

        public class AbilityLocation : AbilityObject, IAbilityLocation
        {
            public AbilityLocation(Location location)
            {
                Location = location;
            }

            public Location Location { get; set; }
        }
    }
}

