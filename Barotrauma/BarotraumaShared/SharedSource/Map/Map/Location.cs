using Barotrauma.Abilities;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class Location
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

        public readonly List<LocationConnection> Connections = new List<LocationConnection>();

        private string baseName;
        private int nameFormatIndex;

        private LocationType addInitialMissionsForType;

        public bool Discovered { get; private set; }

        public readonly Dictionary<LocationTypeChange.Requirement, int> ProximityTimer = new Dictionary<LocationTypeChange.Requirement, int>();
        public (LocationTypeChange typeChange, int delay, MissionPrefab parentMission)? PendingLocationTypeChange;
        public int LocationTypeChangeCooldown;

        public string BaseName { get => baseName; }

        public string Name { get; private set; }

        public Biome Biome { get; set; }

        public Vector2 MapPosition { get; private set; }

        public LocationType Type { get; private set; }

        public LocationType OriginalType { get; private set; }

        public LevelData LevelData { get; set; }

        public int PortraitId { get; private set; }

        public Reputation Reputation { get; set; }

        public int TurnsInRadiation { get; set; }

        #region Store

        public class StoreInfo
        {
            public Identifier Identifier { get; }
            public int Balance { get; set; }
            public List<PurchasedItem> Stock { get; } = new List<PurchasedItem>();
            public List<ItemPrefab> DailySpecials { get; } = new List<ItemPrefab>();
            public List<ItemPrefab> RequestedGoods { get; } = new List<ItemPrefab>();
            /// <summary>
            /// In percentages. Larger values make buying more expensive and selling less profitable, and vice versa.
            /// </summary>
            public int PriceModifier { get; set; }
            public Location Location { get; }

            private StoreInfo(Location location)
            {
                Location = location;
            }

            /// <summary>
            /// Create new StoreInfo
            /// </summary>
            public StoreInfo(Location location, Identifier identifier) : this(location)
            {
                Identifier = identifier;
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
                Balance = storeElement.GetAttributeInt("balance", location.StoreInitialBalance);
                PriceModifier = storeElement.GetAttributeInt("pricemodifier", 0);
                // Backwards compatibility: before introducing support for multiple stores, this value was saved as a store element attribute
                if (storeElement.Attribute("stepssincespecialsupdated") != null)
                {
                    location.StepsSinceSpecialsUpdated = storeElement.GetAttributeInt("stepssincespecialsupdated", 0);
                }
                foreach (var stockElement in storeElement.GetChildElements("stock"))
                {
                    var identifier = stockElement.GetAttributeIdentifier("id", Identifier.Empty);
                    if (identifier.IsEmpty || !(ItemPrefab.FindByIdentifier(identifier) is ItemPrefab prefab)) { continue; }
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
                        if (id.IsEmpty || !(ItemPrefab.FindByIdentifier(id) is ItemPrefab prefab)) { continue; }
                        specials.Add(prefab);
                    }
                    return specials;
                }
            }

            public List<PurchasedItem> CreateStock()
            {
                var stock = new List<PurchasedItem>();
                foreach (var prefab in ItemPrefab.Prefabs)
                {
                    if (!prefab.CanBeBoughtFrom(this, out var priceInfo)) { continue; }
                    int quantity = PriceInfo.DefaultAmount;
                    if (priceInfo.MaxAvailableAmount > 0)
                    {
                        if (priceInfo.MaxAvailableAmount > priceInfo.MinAvailableAmount)
                        {
                            quantity = Rand.Range(priceInfo.MinAvailableAmount, priceInfo.MaxAvailableAmount + 1);
                        }
                        else
                        {
                            quantity = priceInfo.MaxAvailableAmount;
                        }
                    }
                    else if (priceInfo.MinAvailableAmount > 0)
                    {
                        quantity = priceInfo.MinAvailableAmount;
                    }
                    stock.Add(new PurchasedItem(prefab, quantity, buyer: null));
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
                        stockItem.Quantity += 1;
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
                        stockItem.Quantity = Math.Max(stockItem.Quantity - item.Quantity, 0);
                        DebugConsole.NewMessage($"Removed {item.Quantity}x {item.ItemPrefab.Name}, new total: {stockItem.Quantity}", Color.Cyan, debugOnly: true);
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
                        var baseQuantity = priceInfo.MinAvailableAmount > 0 ? priceInfo.MinAvailableAmount : PriceInfo.DefaultAmount;
                        weight += (float)(stockItem.Quantity - baseQuantity) / baseQuantity;
                        if (weight < 0.0f) { continue; }
                    }
                    availableStock.Add(stockItem.ItemPrefab, weight);
                }
                DailySpecials.Clear();
                int extraSpecialSalesCount = Location.GetExtraSpecialSalesCount();
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
                    var item = ItemPrefab.Prefabs.GetRandom(p =>
                        p.CanBeSold && !RequestedGoods.Contains(p) &&
                        p.GetPriceInfo(this) is PriceInfo pi && pi.CanBeSpecial, Rand.RandSync.Unsynced);
                    if (item == null) { break; }
                    RequestedGoods.Add(item);
                }
                Location.StepsSinceSpecialsUpdated = 0;
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
                float price = priceInfo.Price;
                // Adjust by random price modifier
                price = (100 + PriceModifier) / 100.0f * price;
                price *= priceInfo.BuyingPriceMultiplier;
                // Adjust by daily special status
                if (considerDailySpecials && DailySpecials.Contains(item))
                {
                    price = Location.DailySpecialPriceModifier * price;
                }
                // Adjust by current location reputation
                price *= Location.GetStoreReputationModifier(true);
                // Price should never go below 1 mk
                return Math.Max((int)price, 1);
            }

            /// <param name="priceInfo">If null, item.GetPriceInfo() will be used to get it.</param>
            /// <param name="considerRequestedGoods">If false, the price won't be affected by <see cref="RequestGoodPriceModifier"/></param>
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
                    price = Location.RequestGoodPriceModifier * price;
                }
                // Adjust by current location reputation
                price *= Location.GetStoreReputationModifier(false);
                // Price should never go below 1 mk
                return Math.Max((int)price, 1);
            }

            public override string ToString()
            {
                return Identifier.Value;
            }
        }

        public Dictionary<Identifier, StoreInfo> Stores { get; set; }

        private float StoreMaxReputationModifier => Type.StoreMaxReputationModifier;
        private float StoreSellPriceModifier => Type.StoreSellPriceModifier;
        private float DailySpecialPriceModifier => Type.DailySpecialPriceModifier;
        private float RequestGoodPriceModifier => Type.RequestGoodPriceModifier;
        public int StoreInitialBalance => Type.StoreInitialBalance;
        private int StorePriceModifierRange => Type.StorePriceModifierRange;

        /// <summary>
        /// How many map progress steps it takes before the discounts should be updated.
        /// </summary>
        private const int SpecialsUpdateInterval = 3;
        public int DailySpecialsCount => Type.DailySpecialsCount;
        public int RequestedGoodsCount => Type.RequestedGoodsCount;
        private int StepsSinceSpecialsUpdated { get; set; }
        public HashSet<Identifier> StoreIdentifiers { get; } = new HashSet<Identifier>();

        #endregion

        private const float MechanicalMaxDiscountPercentage = 50.0f;
        private const float HealMaxDiscountPercentage = 10.0f;

        private readonly List<TakenItem> takenItems = new List<TakenItem>();
        public IEnumerable<TakenItem> TakenItems
        {
            get { return takenItems; }
        }

        private readonly HashSet<int> killedCharacterIdentifiers = new HashSet<int>();
        public IEnumerable<int> KilledCharacterIdentifiers
        {
            get { return killedCharacterIdentifiers; }
        }

        private readonly List<Mission> availableMissions = new List<Mission>();
        public IEnumerable<Mission> AvailableMissions
        {
            get 
            {
                availableMissions.RemoveAll(m => m.Completed || (m.Failed && !m.Prefab.AllowRetry));
                return availableMissions; 
            }
        }

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
                    DebugConsole.ThrowError($"Failed to select a mission in location \"{Name}\". Mission index out of bounds ({missionIndex}, available missions: {availableMissions.Count})");
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

        private struct LoadedMission
        {
            public MissionPrefab MissionPrefab { get; }
            public int DestinationIndex { get; }
            public bool SelectedMission { get; }

            public LoadedMission(MissionPrefab prefab, int destinationIndex, bool selectedMission)
            {
                MissionPrefab = prefab;
                DestinationIndex = destinationIndex;
                SelectedMission = selectedMission;
            }
        }

        private List<LoadedMission> loadedMissions;

        public HireManager HireManager;
                
        public override string ToString()
        {
            return $"Location ({Name ?? "null"})";
        }

        public Location(Vector2 mapPosition, int? zone, Random rand, bool requireOutpost = false, LocationType forceLocationType = null, IEnumerable<Location> existingLocations = null)
        {
            Type = OriginalType = forceLocationType ?? LocationType.Random(rand, zone, requireOutpost);
            Name = RandomName(Type, rand, existingLocations);
            MapPosition = mapPosition;
            PortraitId = ToolBox.StringToInt(Name);
            Connections = new List<LocationConnection>(); 
        }

        /// <summary>
        /// Create a location from save data
        /// </summary>
        public Location(XElement element)
        {
            Identifier locationTypeId = element.GetAttributeIdentifier("type", "");
            bool typeNotFound = GetTypeOrFallback(locationTypeId, out LocationType type);
            Type = type;

            Identifier originalLocationTypeId = element.GetAttributeIdentifier("originaltype", locationTypeId);
            GetTypeOrFallback(originalLocationTypeId, out LocationType originalType);
            OriginalType = originalType;

            baseName        = element.GetAttributeString("basename", "");
            Name            = element.GetAttributeString("name", "");
            MapPosition     = element.GetAttributeVector2("position", Vector2.Zero);
            Discovered      = element.GetAttributeBool("discovered", false);
            PriceMultiplier = element.GetAttributeFloat("pricemultiplier", 1.0f);
            IsGateBetweenBiomes         = element.GetAttributeBool("isgatebetweenbiomes", false);
            MechanicalPriceMultiplier   = element.GetAttributeFloat("mechanicalpricemultipler", 1.0f);
            TurnsInRadiation            = element.GetAttributeInt(nameof(TurnsInRadiation).ToLower(), 0);
            StepsSinceSpecialsUpdated   = element.GetAttributeInt("stepssincespecialsupdated", 0);

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
                    for (int j = 0; j < Type.CanChangeTo[i].Requirements.Count; j++)
                    {
                        ProximityTimer.Add(Type.CanChangeTo[i].Requirements[j], element.GetAttributeInt("proximitytimer" + i + "-" + j, 0));
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

            killedCharacterIdentifiers = element.GetAttributeIntArray("killedcharacters", Array.Empty<int>()).ToHashSet();

            System.Diagnostics.Debug.Assert(Type != null, $"Could not find the location type \"{locationTypeId}\"!");
            if (Type == null)
            {
                Type = LocationType.Prefabs.First();
            }

            LevelData = new LevelData(element.Element("Level"));

            PortraitId = ToolBox.StringToInt(Name);

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
                        if (type == null)
                        {
                            type = LocationType.Prefabs.First();
                        }
                    }
                    if (type != null)
                    {
                        element.SetAttributeValue("type", type.Identifier);
                    }
                    return false;
                }
                return true;
            }
        }

        public void LoadLocationTypeChange(XElement locationElement)
        {
            TimeSinceLastTypeChange = locationElement.GetAttributeInt("timesincelasttypechange", 0);
            LocationTypeChangeCooldown = locationElement.GetAttributeInt("locationtypechangecooldown", 0);
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
                                DebugConsole.AddWarning($"Failed to activate a location type change in the location \"{Name}\". Location index out of bounds ({locationTypeChangeIndex}).");
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
                                DebugConsole.AddWarning($"Failed to activate a location type change from the mission \"{missionIdentifier}\" in location \"{Name}\". Matching mission not found.");
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
                    var id = childElement.GetAttributeString("prefabid", null);
                    if (string.IsNullOrWhiteSpace(id)) { continue; }
                    var prefab = MissionPrefab.Prefabs.Find(p => p.Identifier == id);
                    if (prefab == null) { continue; }
                    var destination = childElement.GetAttributeInt("destinationindex", -1);
                    var selected = childElement.GetAttributeBool("selected", false);
                    loadedMissions.Add(new LoadedMission(prefab, destination, selected));
                }
            }
        }

        public static Location CreateRandom(Vector2 position, int? zone, Random rand, bool requireOutpost, LocationType forceLocationType = null, IEnumerable<Location> existingLocations = null)
        {
            return new Location(position, zone, rand, requireOutpost, forceLocationType, existingLocations);
        }

        public void ChangeType(LocationType newType)
        {
            if (newType == Type) { return; }

            if (newType == null)
            {
                DebugConsole.ThrowError($"Failed to change the type of the location \"{Name}\" to null.\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            DebugConsole.Log("Location " + baseName + " changed it's type from " + Type + " to " + newType);

            Type = newType;
            Name = Type.NameFormats == null || !Type.NameFormats.Any() ? baseName : Type.NameFormats[nameFormatIndex % Type.NameFormats.Count].Replace("[name]", baseName);

            if (Type.MissionIdentifiers.Any())
            {
                UnlockMissionByIdentifier(Type.MissionIdentifiers.GetRandomUnsynced());
            }
            if (Type.MissionTags.Any())
            {
                UnlockMissionByTag(Type.MissionTags.GetRandomUnsynced());
            }

            CreateStores(force: true);
        }

        public void UnlockInitialMissions()
        {
            if (Type.MissionIdentifiers.Any())
            {
                UnlockMissionByIdentifier(Type.MissionIdentifiers.GetRandom(Rand.RandSync.ServerAndClient));
            }
            if (Type.MissionTags.Any())
            {
                UnlockMissionByTag(Type.MissionTags.GetRandom(Rand.RandSync.ServerAndClient));
            }
        }

        public void UnlockMission(MissionPrefab missionPrefab, LocationConnection connection)
        {
            if (AvailableMissions.Any(m => m.Prefab == missionPrefab)) { return; }
            var mission = InstantiateMission(missionPrefab, connection);
            availableMissions.Add(mission);
#if CLIENT
            GameMain.GameSession?.Campaign?.CampaignUI?.RefreshLocationInfo();
#endif
        }

        public void UnlockMission(MissionPrefab missionPrefab)
        {
            if (AvailableMissions.Any(m => m.Prefab == missionPrefab)) { return; }
            var mission = InstantiateMission(missionPrefab);
            availableMissions.Add(mission);
#if CLIENT
            GameMain.GameSession?.Campaign?.CampaignUI?.RefreshLocationInfo();
#endif
        }

        public Mission UnlockMissionByIdentifier(Identifier identifier)
        {
            if (AvailableMissions.Any(m => m.Prefab.Identifier == identifier)) { return null; }

            var missionPrefab = MissionPrefab.Prefabs.Find(mp => mp.Identifier == identifier);
            if (missionPrefab == null)
            {
                DebugConsole.ThrowError($"Failed to unlock a mission with the identifier \"{identifier}\": matching mission not found.");
            }
            else
            {
                var mission = InstantiateMission(missionPrefab, out LocationConnection connection);
                //don't allow duplicate missions in the same connection
                if (AvailableMissions.Any(m => m.Prefab == missionPrefab && m.Locations.Contains(mission.Locations[0]) && m.Locations.Contains(mission.Locations[1])))
                {
                    return null;
                }
                availableMissions.Add(mission);
#if CLIENT
                GameMain.GameSession?.Campaign?.CampaignUI?.RefreshLocationInfo();
#endif
                return mission;
            }
            return null;
        }

        public Mission UnlockMissionByTag(Identifier tag)
        {
            var matchingMissions = MissionPrefab.Prefabs.Where(mp => mp.Tags.Any(t => t == tag));
            if (!matchingMissions.Any())
            {
                DebugConsole.ThrowError($"Failed to unlock a mission with the tag \"{tag}\": no matching missions found.");
            }
            else
            {
                var unusedMissions = matchingMissions.Where(m => !availableMissions.Any(mission => mission.Prefab == m));
                if (unusedMissions.Any())
                {
                    var suitableMissions = unusedMissions.Where(m => Connections.Any(c => m.IsAllowed(this, c.OtherLocation(this)) || m.IsAllowed(this, this)));
                    if (!suitableMissions.Any())
                    {
                        suitableMissions = unusedMissions;
                    }
                    MissionPrefab missionPrefab = ToolBox.SelectWeightedRandom(suitableMissions.ToList(), suitableMissions.Select(m => (float)m.Commonness).ToList(), Rand.RandSync.Unsynced);
                    var mission = InstantiateMission(missionPrefab, out LocationConnection connection);
                    //don't allow duplicate missions in the same connection
                    if (AvailableMissions.Any(m => m.Prefab == missionPrefab && m.Locations.Contains(mission.Locations[0]) && m.Locations.Contains(mission.Locations[1])))
                    {
                        return null;
                    }
                    availableMissions.Add(mission);
#if CLIENT
                    GameMain.GameSession?.Campaign?.CampaignUI?.RefreshLocationInfo();
#endif
                    return mission;
                }
                else
                {
                    DebugConsole.AddWarning($"Failed to unlock a mission with the tag \"{tag}\": all available missions have already been unlocked.");
                }
            }

            return null;
        }

        private Mission InstantiateMission(MissionPrefab prefab, out LocationConnection connection)
        {
            if (prefab.IsAllowed(this, this))
            {
                connection = null;
                return InstantiateMission(prefab);
            }

            var suitableConnections = Connections.Where(c => prefab.IsAllowed(this, c.OtherLocation(this)));
            if (!suitableConnections.Any())
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
                int missionCount = location.availableMissions.Count(m => m.Locations.Contains(destination));
                if (missionCount > 0) 
                { 
                    weight /= missionCount * 2;
                }
                if (destination.IsRadiated())
                {
                    weight *= 0.001f;
                }
                return MathHelper.Clamp(weight, minWeight, maxWeight);
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
                    var mission = loadedMission.MissionPrefab.Instantiate(new Location[] { this, destination }, Submarine.MainSub);
                    availableMissions.Add(mission);
                    if (loadedMission.SelectedMission) { selectedMissions.Add(mission); }
                }
                loadedMissions = null;
            }
            if (addInitialMissionsForType != null)
            {
                if (addInitialMissionsForType.MissionIdentifiers.Any())
                {
                    UnlockMissionByIdentifier(addInitialMissionsForType.MissionIdentifiers.GetRandomUnsynced());
                }
                if (addInitialMissionsForType.MissionTags.Any())
                {
                    UnlockMissionByTag(addInitialMissionsForType.MissionTags.GetRandomUnsynced());
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

        public LocationType GetLocationType()
        {
            if (IsCriticallyRadiated() && LocationType.Prefabs[Type.ReplaceInRadiation] is { } newLocationType)
            {
                return newLocationType;
            }

            return Type;
        }

        public IEnumerable<Mission> GetMissionsInConnection(LocationConnection connection)
        {
            System.Diagnostics.Debug.Assert(Connections.Contains(connection));
            return AvailableMissions.Where(m => m.Locations[1] == connection.OtherLocation(this));
        }
        
        public void RemoveHireableCharacter(CharacterInfo character)
        {
            if (!Type.HasHireableCharacters)
            {
                DebugConsole.ThrowError("Cannot hire a character from location \"" + Name + "\" - the location has no hireable characters.\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }
            if (HireManager == null)
            {
                DebugConsole.ThrowError("Cannot hire a character from location \"" + Name + "\" - hire manager has not been instantiated.\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            HireManager.RemoveCharacter(character);
        }

        public IEnumerable<CharacterInfo> GetHireableCharacters()
        {
            if (!Type.HasHireableCharacters)
            {
                return Enumerable.Empty<CharacterInfo>();
            }

            HireManager ??= new HireManager();

            if (!HireManager.AvailableCharacters.Any())
            {
                HireManager.GenerateCharacters(location: this, amount: HireManager.MaxAvailableCharacters);
            }
            return HireManager.AvailableCharacters;
        }

        private string RandomName(LocationType type, Random rand, IEnumerable<Location> existingLocations)
        {
            baseName = type.GetRandomName(rand, existingLocations);
            if (type.NameFormats == null || !type.NameFormats.Any()) { return baseName; }
            nameFormatIndex = rand.Next() % type.NameFormats.Count;
            return type.NameFormats[nameFormatIndex].Replace("[name]", baseName);
        }

        public void LoadStores(XElement locationElement)
        {
            UpdateStoreIdentifiers();
            Stores?.Clear();
            foreach (var storeElement in locationElement.GetChildElements("store"))
            {
                Stores ??= new Dictionary<Identifier, StoreInfo>();
                var identifier = storeElement.GetAttributeIdentifier("identifier", "");
                if (identifier.IsEmpty)
                {
                    // Previously saved store data (with no identifier) is discarded and new store data will be created
                    continue;
                }
                if (StoreIdentifiers.Contains(identifier))
                {
                    if (!Stores.ContainsKey(identifier))
                    {
                        Stores.Add(identifier, new StoreInfo(this, storeElement));
                    }
                    else
                    {
                        string msg = $"Error loading store info for \"{identifier}\" at location {Name} of type \"{Type.Identifier}\": duplicate identifier.";
                        DebugConsole.ThrowError(msg);
                        GameAnalyticsManager.AddErrorEventOnce("Location.LoadStore:DuplicateStoreInfo", GameAnalyticsManager.ErrorSeverity.Error, msg);
                        continue;
                    }
                }
                else
                {
                    string msg = $"Error loading store info for \"{identifier}\" at location {Name} of type \"{Type.Identifier}\": location shouldn't contain a store with this identifier.";
                    DebugConsole.ThrowError(msg);
                    GameAnalyticsManager.AddErrorEventOnce("Location.LoadStore:IncorrectStoreIdentifier", GameAnalyticsManager.ErrorSeverity.Error, msg);
                    continue;
                }
            }
            // Backwards compatibility: create new stores for any identifiers not present in the save data
            foreach (var id in StoreIdentifiers)
            {
                AddNewStore(id);
            }
        }

        public bool IsRadiated() => GameMain.GameSession?.Map?.Radiation != null && GameMain.GameSession.Map.Radiation.Enabled && GameMain.GameSession.Map.Radiation.Contains(this);

        /// <summary>
        /// Mark the items that have been taken from the outpost to prevent them from spawning when re-entering the outpost
        /// </summary>
        public void RegisterTakenItems(IEnumerable<Item> items)
        {
            foreach (Item item in items)
            {
                if (takenItems.Any(it => it.Matches(item) && it.OriginalID == item.ID)) { continue; }
                if (item.OriginalModuleIndex < 0)
                {
                    DebugConsole.ThrowError("Tried to register a non-outpost item as being taken from the outpost.");
                    continue;
                }
                takenItems.Add(new TakenItem(item));
            }
        }

        /// <summary>
        /// Mark the characters who have been killed to prevent them from spawning when re-entering the outpost
        /// </summary>
        public void RegisterKilledCharacters(IEnumerable<Character> characters)
        {
            foreach (Character character in characters)
            {
                if (character?.Info == null) { continue; }
                killedCharacterIdentifiers.Add(character.Info.GetIdentifier());
            }
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
            float discount = Reputation.Value / Reputation.MaxReputation * (MechanicalMaxDiscountPercentage / 100.0f);
            return (int) Math.Ceiling((1.0f - discount) * cost * MechanicalPriceMultiplier);
        }

        public int GetAdjustedHealCost(int cost)
        {
            float discount = Reputation.Value / Reputation.MaxReputation * (HealMaxDiscountPercentage / 100.0f);
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
                    if (!StoreIdentifiers.Contains(storeIdentifier))
                    {
                        Stores.Remove(storeIdentifier);
                    }
                }
                foreach (var identifier in StoreIdentifiers)
                {
                    if (Stores.TryGetValue(identifier, out var store))
                    {
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
                    else
                    {
                        AddNewStore(identifier);
                    }
                }
            }
            else
            {
                foreach (var identifier in StoreIdentifiers)
                {
                    AddNewStore(identifier);
                }
            }
        }

        public void UpdateStores()
        {
            // In multiplayer, stores should be updated by the server and loaded from save data by clients
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }
            if (Stores == null)
            {
                CreateStores();
                return;
            }
            var storesToRemove = new HashSet<Identifier>();
            foreach (var store in Stores.Values)
            {
                if (!StoreIdentifiers.Contains(store.Identifier))
                {
                    storesToRemove.Add(store.Identifier);
                    continue;
                }
                if (store.Balance < StoreInitialBalance)
                {
                    store.Balance = Math.Min(store.Balance + (int)(StoreInitialBalance / 10.0f), StoreInitialBalance);
                }
                var stock = new List<PurchasedItem>(store.Stock);
                var stockToRemove = new List<PurchasedItem>();
                foreach (var item in stock)
                {
                    if (item.ItemPrefab.CanBeBoughtFrom(store, out PriceInfo priceInfo))
                    {
                        item.Quantity += 1;
                        if (priceInfo.MaxAvailableAmount > 0)
                        {
                            item.Quantity = Math.Min(item.Quantity, priceInfo.MaxAvailableAmount);
                        }
                        else
                        {
                            item.Quantity = Math.Min(item.Quantity, CargoManager.MaxQuantity);
                        }
                    }
                    else
                    {
                        stockToRemove.Add(item);
                    }
                }
                stockToRemove.ForEach(i => stock.Remove(i));
                store.Stock.Clear();
                store.Stock.AddRange(stock);
                int extraSpecialSalesCount = GetExtraSpecialSalesCount();
                if (++StepsSinceSpecialsUpdated >= SpecialsUpdateInterval || store.DailySpecials.Count() != DailySpecialsCount + extraSpecialSalesCount)
                {
                    store.GenerateSpecials();
                }
                store.GeneratePriceModifier();
            }
            foreach (var identifier in storesToRemove)
            {
                Stores.Remove(identifier);
            }
            foreach (var identifier in StoreIdentifiers)
            {
                AddNewStore(identifier);
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
        }

        private void AddNewStore(Identifier identifier)
        {
            Stores ??= new Dictionary<Identifier, StoreInfo>();
            if (Stores.ContainsKey(identifier)) { return; }
            var newStore = new StoreInfo(this, identifier);
            Stores.Add(identifier, newStore);
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

        public float GetStoreReputationModifier(bool buying)
        {
            if (buying)
            {
                if (Reputation.Value > 0.0f)
                {
                    return MathHelper.Lerp(1.0f, 1.0f - StoreMaxReputationModifier, Reputation.Value / Reputation.MaxReputation);
                }
                else
                {
                    return MathHelper.Lerp(1.0f, 1.0f + StoreMaxReputationModifier, Reputation.Value / Reputation.MinReputation);
                }
            }
            else
            {
                if (Reputation.Value > 0.0f)
                {
                    return MathHelper.Lerp(1.0f, 1.0f + StoreMaxReputationModifier, Reputation.Value / Reputation.MaxReputation);
                }
                else
                {
                    return MathHelper.Lerp(1.0f, 1.0f - StoreMaxReputationModifier, Reputation.Value / Reputation.MinReputation);
                }
            }
        }

        public int GetExtraSpecialSalesCount()
        {
            var characters = GameSession.GetSessionCrewCharacters(CharacterType.Both);
            if (!characters.Any()) { return 0; }
            return characters.Sum(c => (int)c.GetStatValue(StatTypes.ExtraSpecialSalesCount));
        }

        public void Discover(bool checkTalents = true)
        {
            if (Discovered) { return; }
            Discovered = true;
            if (checkTalents)
            {
                GameSession.GetSessionCrewCharacters(CharacterType.Both).ForEach(c => c.CheckTalents(AbilityEffectType.OnLocationDiscovered, new AbilityLocation(this)));
            }
        }

        public void Reset()
        {
            if (Type != OriginalType)
            {
                ChangeType(OriginalType);
                PendingLocationTypeChange = null;
            }
            CreateStores(force: true);
            ClearMissions();
            LevelData?.EventHistory?.Clear();
            UnlockInitialMissions();
            Discovered = false;
        }

        public XElement Save(Map map, XElement parentElement)
        {
            var locationElement = new XElement("location",
                new XAttribute("type", Type.Identifier),
                new XAttribute("originaltype", (Type ?? OriginalType).Identifier),
                new XAttribute("basename", BaseName),
                new XAttribute("name", Name),
                new XAttribute("biome", Biome?.Identifier.Value ?? string.Empty),
                new XAttribute("discovered", Discovered),
                new XAttribute("position", XMLExtensions.Vector2ToString(MapPosition)),
                new XAttribute("pricemultiplier", PriceMultiplier),
                new XAttribute("isgatebetweenbiomes", IsGateBetweenBiomes),
                new XAttribute("mechanicalpricemultipler", MechanicalPriceMultiplier),
                new XAttribute("timesincelasttypechange", TimeSinceLastTypeChange),
                new XAttribute(nameof(TurnsInRadiation).ToLower(), TurnsInRadiation),
                new XAttribute("stepssincespecialsupdated", StepsSinceSpecialsUpdated));
            LevelData.Save(locationElement);

            for (int i = 0; i < Type.CanChangeTo.Count; i++)
            {
                for (int j = 0; j < Type.CanChangeTo[i].Requirements.Count; j++)
                {
                    if (ProximityTimer.ContainsKey(Type.CanChangeTo[i].Requirements[j]))
                    {
                        locationElement.Add(new XAttribute("proximitytimer" + i + "-" + j, ProximityTimer[Type.CanChangeTo[i].Requirements[j]]));
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
                        DebugConsole.AddWarning($"Invalid location type change in the location \"{Name}\". Unknown type change ({PendingLocationTypeChange.Value.typeChange.ChangeToType}).");
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
            if (killedCharacterIdentifiers.Any())
            {
                locationElement.Add(new XAttribute("killedcharacters", string.Join(',', killedCharacterIdentifiers)));
            }

            if (Stores != null)
            {
                foreach (var store in Stores.Values)
                {
                    var storeElement = new XElement("store",
                        new XAttribute("identifier", store.Identifier.Value),
                        new XAttribute("balance", store.Balance),
                        new XAttribute("pricemodifier", store.PriceModifier));
                    foreach (PurchasedItem item in store.Stock)
                    {
                        if (item?.ItemPrefab == null) { continue; }
                        storeElement.Add(new XElement("stock",
                            new XAttribute("id", item.ItemPrefab.Identifier),
                            new XAttribute("qty", item.Quantity)));
                    }
                    if (store.DailySpecials.Any())
                    {
                        var dailySpecialElement = new XElement("dailyspecials");
                        foreach (var item in store.DailySpecials)
                        {
                            dailySpecialElement.Add(new XElement("item",
                                new XAttribute("id", item.Identifier)));
                        }
                        storeElement.Add(dailySpecialElement);
                    }
                    if (store.RequestedGoods.Any())
                    {
                        var requestedGoodsElement = new XElement("requestedgoods");
                        foreach (var item in store.RequestedGoods)
                        {
                            requestedGoodsElement.Add(new XElement("item",
                                new XAttribute("id", item.Identifier)));
                        }
                        storeElement.Add(requestedGoodsElement);
                    }
                    locationElement.Add(storeElement);
                }
            }

            if (AvailableMissions is List<Mission> missions && missions.Any())
            {
                var missionsElement = new XElement("missions");
                foreach (Mission mission in missions)
                {
                    var location = mission.Locations.All(l => l == this) ? this : mission.Locations.FirstOrDefault(l => l != this);
                    var i = map.Locations.IndexOf(location);
                    missionsElement.Add(new XElement("mission",
                        new XAttribute("prefabid", mission.Prefab.Identifier),
                        new XAttribute("destinationindex", i),
                        new XAttribute("selected", selectedMissions.Contains(mission))));
                }
                locationElement.Add(missionsElement);
            }

            parentElement.Add(locationElement);

            return locationElement;
        }

        public void Remove()
        {
            RemoveProjSpecific();
        }

        public void RemoveProjSpecific()
        {
            HireManager?.Remove();
        }

        class AbilityLocation : AbilityObject, IAbilityLocation
        {
            public AbilityLocation(Location location)
            {
                Location = location;
            }

            public Location Location { get; set; }
        }
    }
}
