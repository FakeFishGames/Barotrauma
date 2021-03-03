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
            public readonly ushort OriginalContainerID;
            public readonly ushort ModuleIndex;
            public readonly string Identifier;

            public TakenItem(string identifier, UInt16 originalID, UInt16 originalContainerID, ushort moduleIndex)
            {
                OriginalID = originalID;
                OriginalContainerID = originalContainerID;
                ModuleIndex = moduleIndex;
                Identifier = identifier;
            }

            public TakenItem(Item item)
            {
                System.Diagnostics.Debug.Assert(item.OriginalModuleIndex >= 0, "Trying to add a non-outpost item to a location's taken items");

                if (item.OriginalContainerID != Entity.NullEntityID)
                {
                    OriginalContainerID = item.OriginalContainerID;
                }

                OriginalID = item.ID;
                ModuleIndex = (ushort) item.OriginalModuleIndex;
                Identifier = item.prefab.Identifier;
            }

            public bool IsEqual(TakenItem obj)
            {
                return obj.OriginalID == OriginalID && obj.OriginalContainerID == OriginalContainerID && obj.ModuleIndex == ModuleIndex && obj.Identifier == Identifier;
            }

            public bool Matches(Item item)
            {
                if (item.OriginalContainerID != Entity.NullEntityID)
                {
                    return item.OriginalContainerID == OriginalContainerID && item.OriginalModuleIndex == ModuleIndex && item.prefab.Identifier == Identifier;
                }
                else
                {
                    return item.ID == OriginalID && item.OriginalModuleIndex == ModuleIndex && item.prefab.Identifier == Identifier;
                }
            }
        }

        public readonly List<LocationConnection> Connections = new List<LocationConnection>();

        private string baseName;
        private int nameFormatIndex;

        public bool Discovered;

        public readonly Dictionary<LocationTypeChange.Requirement, int> ProximityTimer = new Dictionary<LocationTypeChange.Requirement, int>();
        public (LocationTypeChange typeChange, int delay, MissionPrefab parentMission)? PendingLocationTypeChange;
        public int LocationTypeChangeCooldown;

        public readonly int ZoneIndex;

        public string BaseName { get => baseName; }

        public string Name { get; private set; }

        public Biome Biome { get; set; }

        public Vector2 MapPosition { get; private set; }

        public LocationType Type { get; private set; }

        public LevelData LevelData { get; set; }

        public int PortraitId { get; private set; }

        public Reputation Reputation { get; set; }

        public int TurnsInRadiation { get; set; }

        #region Store

        private const float StoreMaxReputationModifier = 0.1f;
        private const float StoreSellPriceModifier = 0.8f;
        private const float DailySpecialPriceModifier = 0.9f;
        private const float RequestGoodPriceModifier = 1.5f;
        public const int StoreInitialBalance = 5000;
        /// <summary>
        /// In percentages
        /// </summary>
        private const int StorePriceModifierRange = 5;
        /// <summary>
        /// In percentages. Larger values make buying more expensive and selling less profitable, and vice versa.
        /// </summary>
        public int StorePriceModifier { get; private set; }

        public Color BalanceColor => ActiveStoreBalanceStatus.Color;
        public StoreBalanceStatus ActiveStoreBalanceStatus { get; private set; }
        private static StoreBalanceStatus DefaultBalanceStatus { get; } = new StoreBalanceStatus(1.0f, 1.0f, Color.White);
        private static List<StoreBalanceStatus> StoreBalanceStatuses { get; } = new List<StoreBalanceStatus>
        {
            new StoreBalanceStatus(0.5f, 0.75f, Color.Orange),
            new StoreBalanceStatus(0.25f, 0.2f, Color.Red),
        };

        public struct StoreBalanceStatus
        {
            public float PercentageOfInitialBalance { get; }
            public float SellPriceModifier { get; }
            public Color Color { get; }

            public StoreBalanceStatus(float percentage, float sellPriceModifier, Color color)
            {
                PercentageOfInitialBalance = percentage;
                SellPriceModifier = sellPriceModifier;
                Color = color;
            }
        }

        private int storeCurrentBalance;
        public int StoreCurrentBalance
        {
            get
            {
                return storeCurrentBalance;
            }
            set
            {
                storeCurrentBalance = value;
                ActiveStoreBalanceStatus = GetStoreBalanceStatus(value);
            }
        }

        public List<PurchasedItem> StoreStock { get; set; }
        public List<ItemPrefab> DailySpecials { get; } = new List<ItemPrefab>();
        public List<ItemPrefab> RequestedGoods { get; } = new List<ItemPrefab>();

        /// <summary>
        /// How many map progress steps it takes before the discounts should be updated.
        /// </summary>
        private const int SpecialsUpdateInterval = 3;
        private const int DailySpecialsCount = 3;
        private const int RequestedGoodsCount = 3;
        private int StepsSinceSpecialsUpdated { get; set; }

        #endregion

        private const float MechanicalMaxDiscountPercentage = 50.0f;

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

        public Mission SelectedMission
        {
            get;
            set;
        }

        public int SelectedMissionIndex
        {
            get
            {
                if (SelectedMission == null) { return -1; }
                return availableMissions.IndexOf(SelectedMission);
            }
            set
            {
                if (value < 0 || value >= AvailableMissions.Count())
                {
                    SelectedMission = null;
                    return;
                }
                SelectedMission = availableMissions[value];
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

        public Location(Vector2 mapPosition, int? zone, Random rand, bool requireOutpost = false, LocationType? forceLocationType = null, IEnumerable<Location> existingLocations = null)
        {
            Type = forceLocationType ?? LocationType.Random(rand, zone, requireOutpost);
            Name = RandomName(Type, rand, existingLocations);
            MapPosition = mapPosition;
            PortraitId = ToolBox.StringToInt(Name);
            Connections = new List<LocationConnection>(); 
        }

        public Location(XElement element)
        {
            string locationType = element.GetAttributeString("type", "");
            Type = LocationType.List.Find(lt => lt.Identifier.Equals(locationType, StringComparison.OrdinalIgnoreCase));
            bool typeNotFound = false;
            if (Type == null)
            {
                DebugConsole.AddWarning($"Could not find location type \"{locationType}\". Using location type \"None\" instead.");
                Type = LocationType.List.Find(lt => lt.Identifier.Equals("None", StringComparison.OrdinalIgnoreCase));
                Type ??= LocationType.List.First();
                typeNotFound = true;
            }

            baseName        = element.GetAttributeString("basename", "");
            Name            = element.GetAttributeString("name", "");
            MapPosition     = element.GetAttributeVector2("position", Vector2.Zero);
            Discovered      = element.GetAttributeBool("discovered", false);
            PriceMultiplier = element.GetAttributeFloat("pricemultiplier", 1.0f);
            IsGateBetweenBiomes         = element.GetAttributeBool("isgatebetweenbiomes", false);
            MechanicalPriceMultiplier   = element.GetAttributeFloat("mechanicalpricemultipler", 1.0f);
            TurnsInRadiation            = element.GetAttributeInt(nameof(TurnsInRadiation).ToLower(), 0);

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

            string[] takenItemStr = element.GetAttributeStringArray("takenitems", new string[0]);
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
                if (!ushort.TryParse(takenItemSplit[2], out ushort containerId))
                {
                    DebugConsole.ThrowError($"Error in saved location: could not parse taken container id \"{takenItemSplit[2]}\"");
                    continue;
                }
                if (!ushort.TryParse(takenItemSplit[3], out ushort moduleIndex))
                {
                    DebugConsole.ThrowError($"Error in saved location: could not parse taken item module index \"{takenItemSplit[3]}\"");
                    continue;
                }
                takenItems.Add(new TakenItem(takenItemSplit[0], id, containerId, moduleIndex));
            }

            killedCharacterIdentifiers = element.GetAttributeIntArray("killedcharacters", new int[0]).ToHashSet();

            System.Diagnostics.Debug.Assert(Type != null, $"Could not find the location type \"{locationType}\"!");
            if (Type == null)
            {
                Type = LocationType.List.First();
            }

            LevelData = new LevelData(element.Element("Level"));

            PortraitId = ToolBox.StringToInt(Name);

            LoadStore(element);
            LoadMissions(element);
        }

        public void LoadLocationTypeChange(XElement locationElement)
        {
            TimeSinceLastTypeChange = locationElement.GetAttributeInt("timesincelasttypechange", 0);
            LocationTypeChangeCooldown = locationElement.GetAttributeInt("locationtypechangecooldown", 0);
            foreach (XElement subElement in locationElement.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "pendinglocationtypechange":
                        int timer = subElement.GetAttributeInt("timer", 0);
                        if (subElement.Attribute("index") != null)
                        {
                            int locationTypeChangeIndex = subElement.GetAttributeInt("index", 0);
                            PendingLocationTypeChange = (Type.CanChangeTo[locationTypeChangeIndex], timer, null);
                        }
                        else
                        {
                            string missionIdentifier = subElement.GetAttributeString("missionidentifier", "");
                            var mission = MissionPrefab.List.Find(mp => mp.Identifier.Equals(missionIdentifier, StringComparison.OrdinalIgnoreCase));
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
                    var prefab = MissionPrefab.List.Find(p => p.Identifier.Equals(id, StringComparison.OrdinalIgnoreCase));
                    if (prefab == null) { continue; }
                    var destination = childElement.GetAttributeInt("destinationindex", -1);
                    var selected = childElement.GetAttributeBool("selected", false);
                    loadedMissions.Add(new LoadedMission(prefab, destination, selected));
                }
            }
        }


        public static Location CreateRandom(Vector2 position, int? zone, Random rand, bool requireOutpost, LocationType? forceLocationType = null, IEnumerable<Location> existingLocations = null)
        {
            return new Location(position, zone, rand, requireOutpost, forceLocationType, existingLocations);
        }

        public void ChangeType(LocationType newType)
        {
            if (newType == Type) { return; }

            DebugConsole.Log("Location " + baseName + " changed it's type from " + Type + " to " + newType);

            Type = newType;
            Name = Type.NameFormats == null ? baseName : Type.NameFormats[nameFormatIndex % Type.NameFormats.Count].Replace("[name]", baseName);

            if (Type.MissionIdentifiers.Any())
            {
                UnlockMissionByIdentifier(Type.MissionIdentifiers.GetRandom());
            }
            if (Type.MissionTags.Any())
            {
                UnlockMissionByTag(Type.MissionTags.GetRandom());
            }

            CreateStore(force: true);
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

        public MissionPrefab UnlockMissionByIdentifier(string identifier)
        {
            if (AvailableMissions.Any(m => m.Prefab.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase))) { return null; }

            var missionPrefab = MissionPrefab.List.Find(mp => mp.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase));
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
                return missionPrefab;
            }
            return null;
        }

        public MissionPrefab UnlockMissionByTag(string tag)
        {
            var matchingMissions = MissionPrefab.List.FindAll(mp => mp.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)));
            if (!matchingMissions.Any())
            {
                DebugConsole.ThrowError($"Failed to unlock a mission with the tag \"{tag}\": no matching missions not found.");
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
                    return missionPrefab;
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
                suitableConnections.Select(c => (c.Passed ? 1.0f : 5.0f) / Math.Max(availableMissions.Count(m => m.Locations.Contains(c.OtherLocation(this))), 1.0f)).ToList(),
                Rand.RandSync.Unsynced);            

            return InstantiateMission(prefab, connection);
        }

        private Mission InstantiateMission(MissionPrefab prefab, LocationConnection connection)
        {
            Location destination = connection.OtherLocation(this);
            var mission = prefab.Instantiate(new Location[] { this, destination });
            mission.AdjustLevelData(connection.LevelData);
            return mission;
        }

        private Mission InstantiateMission(MissionPrefab prefab)
        {
            var mission = prefab.Instantiate(new Location[] { this, this });
            mission.AdjustLevelData(LevelData);
            return mission;
        }

        public void InstantiateLoadedMissions(Map map)
        {
            availableMissions.Clear();
            if (loadedMissions == null || loadedMissions.None()) { return; }
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
                var mission = loadedMission.MissionPrefab.Instantiate(new Location[] { this, destination });
                availableMissions.Add(mission);
                if (loadedMission.SelectedMission) { SelectedMission = mission; }
            }
            loadedMissions = null;
        }

        /// <summary>
        /// Removes all unlocked missions from the location
        /// </summary>
        public void ClearMissions()
        {
            availableMissions.Clear();
            SelectedMissionIndex = -1;
        }

        public bool HasOutpost()
        {
            if (!Type.HasOutpost) { return false; }

            return !IsCriticallyRadiated();
        }

        public bool IsCriticallyRadiated()
        {
            if (GameMain.GameSession is { Campaign: { Map: { } map } })
            {
                return TurnsInRadiation > map.Radiation.Params.CriticalRadiationThreshold;
            }

            return false;
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

        public void LoadStore(XElement locationElement)
        {
            StoreStock?.Clear();
            DailySpecials.Clear();
            RequestedGoods.Clear();

            if (locationElement.GetChildElement("store") is XElement storeElement)
            {
                StoreCurrentBalance = storeElement.GetAttributeInt("balance", StoreInitialBalance);
                StorePriceModifier = storeElement.GetAttributeInt("pricemodifier", 0);

                StoreStock ??= new List<PurchasedItem>();
                foreach (XElement stockElement in storeElement.GetChildElements("stock"))
                {
                    var id = stockElement.GetAttributeString("id", null);
                    if (string.IsNullOrWhiteSpace(id)) { continue; }
                    var prefab = ItemPrefab.Prefabs.Find(p => p.Identifier == id);
                    if (prefab == null) { continue; }
                    var qty = stockElement.GetAttributeInt("qty", 0);
                    if (qty < 1) { continue; }
                    StoreStock.Add(new PurchasedItem(prefab, qty));
                }

                StepsSinceSpecialsUpdated = storeElement.GetAttributeInt("stepssincespecialsupdated", 0);

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
                    List<ItemPrefab> specials = new List<ItemPrefab>();
                    foreach (var childElement in element.GetChildElements("item"))
                    {
                        var id = childElement.GetAttributeString("id", null);
                        if (string.IsNullOrWhiteSpace(id)) { continue; }
                        var prefab = ItemPrefab.Find(null, id);
                        if (prefab == null) { continue; }
                        specials.Add(prefab);
                    }
                    return specials;
                }
            }
        }

        public bool IsRadiated() => GameMain.GameSession is { Campaign: { Map: { Radiation: { Enabled: true } radiation } } } && radiation.Contains(this);

        private List<PurchasedItem> CreateStoreStock()
        {
            var stock = new List<PurchasedItem>();
            foreach (ItemPrefab prefab in ItemPrefab.Prefabs)
            {
                if (prefab.CanBeBoughtAtLocation(this, out PriceInfo priceInfo))
                {
                    int quantity = PriceInfo.DefaultAmount;
                    if (priceInfo.MaxAvailableAmount > 0)
                    {
                        if (priceInfo.MaxAvailableAmount > priceInfo.MinAvailableAmount)
                        {
                            quantity = Rand.Range(priceInfo.MinAvailableAmount, priceInfo.MaxAvailableAmount);
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
                    stock.Add(new PurchasedItem(prefab, quantity));
                }
            }
            return stock;
        }

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

        /// <param name="priceInfo">If null, item.GetPriceInfo() will be used to get it.</param>
        /// /// <param name="considerDailySpecials">If false, the price won't be affected by <see cref="DailySpecialPriceModifier"/></param>
        public int GetAdjustedItemBuyPrice(ItemPrefab item, PriceInfo priceInfo = null, bool considerDailySpecials = true)
        {
            priceInfo ??= item?.GetPriceInfo(this);
            if (priceInfo == null) { return 0; }
            float price = priceInfo.Price;

            // Adjust by random price modifier
            price = ((100 + StorePriceModifier) / 100.0f) * price;

            // Adjust by daily special status
            if (considerDailySpecials && DailySpecials.Contains(item))
            {
                price = DailySpecialPriceModifier * price;
            }

            // Adjust by current location reputation
            if (Reputation.Value > 0.0f)
            {
                price = MathHelper.Lerp(1.0f, 1.0f - StoreMaxReputationModifier, Reputation.Value / Reputation.MaxReputation) * price;
            }
            else
            {
                price = MathHelper.Lerp(1.0f, 1.0f + StoreMaxReputationModifier, Reputation.Value / Reputation.MinReputation) * price;
            }

            // Price should never go below 1 mk
            return Math.Max((int)price, 1);
        }

        /// <param name="priceInfo">If null, item.GetPriceInfo() will be used to get it.</param>
        /// <param name="considerRequestedGoods">If false, the price won't be affected by <see cref="RequestGoodPriceModifier"/></param>
        public int GetAdjustedItemSellPrice(ItemPrefab item, PriceInfo priceInfo = null, bool considerRequestedGoods = true)
        {
            priceInfo ??= item?.GetPriceInfo(this);
            if (priceInfo == null) { return 0; }
            float price = StoreSellPriceModifier * priceInfo.Price;

            // Adjust by random price modifier
            price = ((100 - StorePriceModifier) / 100.0f) * price;

            // Adjust by current store balance
            price = ActiveStoreBalanceStatus.SellPriceModifier * price;

            // Adjust by requested good status
            if (considerRequestedGoods && RequestedGoods.Contains(item))
            {
                price = RequestGoodPriceModifier * price;
            }

            // Adjust by current location reputation
            if (Reputation.Value > 0.0f)
            {
                price = MathHelper.Lerp(1.0f, 1.0f + StoreMaxReputationModifier, Reputation.Value / Reputation.MaxReputation) * price;
            }
            else
            {
                price = MathHelper.Lerp(1.0f, 1.0f - StoreMaxReputationModifier, Reputation.Value / Reputation.MinReputation) * price;
            }

            // Price should never go below 1 mk
            return Math.Max((int)price, 1);
        }

        public int GetAdjustedMechanicalCost(int cost)
        {
            float discount = Reputation.Value / Reputation.MaxReputation * (MechanicalMaxDiscountPercentage / 100.0f);
            return (int) Math.Ceiling((1.0f - discount) * cost * MechanicalPriceMultiplier);
        }

        /// <param name="force">If true, the store will be recreated if it already exists.</param>
        public void CreateStore(bool force = false)
        {
            // In multiplayer, stores should be created by the server and loaded from save data by clients
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            if (!force && StoreStock != null) { return; }

            if (StoreStock != null)
            {
                StoreCurrentBalance = Math.Max(StoreCurrentBalance, StoreInitialBalance);
                var newStock = CreateStoreStock();
                foreach (PurchasedItem oldStockItem in StoreStock)
                {
                    if (newStock.Find(i => i.ItemPrefab == oldStockItem.ItemPrefab) is PurchasedItem newStockItem)
                    {
                        if (oldStockItem.Quantity > newStockItem.Quantity)
                        {
                            newStockItem.Quantity = oldStockItem.Quantity;
                        }
                    }
                }
                StoreStock = newStock;
            }
            else
            {
                StoreCurrentBalance = StoreInitialBalance;
                StoreStock = CreateStoreStock();
            }

            GenerateRandomPriceModifier();
            CreateStoreSpecials();
        }

        public void UpdateStore()
        {
            // In multiplayer, stores should be updated by the server and loaded from save data by clients
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            if (StoreStock == null)
            {
                CreateStore();
                return;
            }

            if (StoreCurrentBalance < StoreInitialBalance)
            {
                StoreCurrentBalance = Math.Min(StoreCurrentBalance + (int)(StoreInitialBalance / 10.0f), StoreInitialBalance);
            }

            GenerateRandomPriceModifier();

            var stock = StoreStock;
            var stockToRemove = new List<PurchasedItem>();
            foreach (PurchasedItem item in stock)
            {
                if (item.ItemPrefab.CanBeBoughtAtLocation(this, out PriceInfo priceInfo))
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
            StoreStock = stock;

            if (++StepsSinceSpecialsUpdated >= SpecialsUpdateInterval)
            {
                CreateStoreSpecials();
            }
        }

        private void GenerateRandomPriceModifier()
        {
            StorePriceModifier = Rand.Range(-StorePriceModifierRange, StorePriceModifierRange);
        }

        private void CreateStoreSpecials()
        {
            DailySpecials.Clear();
            var availableStock = new Dictionary<ItemPrefab, float>();
            foreach (var stockItem in StoreStock)
            {
                if (stockItem.Quantity < 1) { continue; }
                var weight = 1.0f;
                var priceInfo = stockItem.ItemPrefab.GetPriceInfo(this);
                if (priceInfo != null)
                {
                    if (!priceInfo.CanBeSpecial) { continue; }
                    var baseQuantity = priceInfo.MinAvailableAmount > 0 ? priceInfo.MinAvailableAmount : PriceInfo.DefaultAmount;
                    weight += (float)(stockItem.Quantity - baseQuantity) / baseQuantity;
                    if (weight < 0.0f) { continue; }
                }
                availableStock.Add(stockItem.ItemPrefab, weight);
            }
            for (int i = 0; i < DailySpecialsCount; i++)
            {
                if (availableStock.None()) { break; }
                var item = ToolBox.SelectWeightedRandom(availableStock.Keys.ToList(), availableStock.Values.ToList(), Rand.RandSync.Unsynced);
                if (item == null) { break; }
                DailySpecials.Add(item);
                availableStock.Remove(item);
            }

            RequestedGoods.Clear();
            for (int i = 0; i < RequestedGoodsCount; i++)
            {
                var item = ItemPrefab.Prefabs.GetRandom(p =>
                    p.CanBeSold && !RequestedGoods.Contains(p) &&
                    p.GetPriceInfo(this) is PriceInfo pi && pi.CanBeSpecial);
                if (item == null) { break; }
                RequestedGoods.Add(item);
            }

            StepsSinceSpecialsUpdated = 0;
        }

        public void AddToStock(List<SoldItem> items)
        {
            if (StoreStock == null || items == null) { return; }
#if DEBUG
            if (items.Any()) { DebugConsole.NewMessage("Adding items to stock at " + Name, Color.Purple); }
#endif
            foreach (SoldItem item in items)
            {
                if (StoreStock.FirstOrDefault(i => i.ItemPrefab == item.ItemPrefab) is PurchasedItem stockItem)
                {
                    stockItem.Quantity += 1;
#if DEBUG
                    DebugConsole.NewMessage("Added 1x " + item.ItemPrefab.Name + ", new total: " + stockItem.Quantity, Color.Cyan);
#endif
                }
#if DEBUG
                else
                {
                    DebugConsole.NewMessage(item.ItemPrefab.Name + " not sold at location, can't add", Color.Cyan);
                }
#endif
            }
        }

        public void RemoveFromStock(List<PurchasedItem> items)
        {
            if (StoreStock == null || items == null) { return; }
#if DEBUG
            if (items.Any()) { DebugConsole.NewMessage("Removing items from stock at " + Name, Color.Purple); }
#endif
            foreach (PurchasedItem item in items)
            {
                if (StoreStock.FirstOrDefault(i => i.ItemPrefab == item.ItemPrefab) is PurchasedItem stockItem)
                {
                    stockItem.Quantity = Math.Max(stockItem.Quantity - item.Quantity, 0);
#if DEBUG
                    DebugConsole.NewMessage("Removed " + item.Quantity + "x " + item.ItemPrefab.Name + ", new total: " + stockItem.Quantity, Color.Cyan);
#endif
                }
            }
        }

        public static StoreBalanceStatus GetStoreBalanceStatus(int balance)
        {
            StoreBalanceStatus nextStatus = DefaultBalanceStatus;
            foreach (var balanceStatus in StoreBalanceStatuses)
            {
                if (balanceStatus.PercentageOfInitialBalance < nextStatus.PercentageOfInitialBalance &&
                    ((float)balance / StoreInitialBalance) < balanceStatus.PercentageOfInitialBalance)
                {
                    nextStatus = balanceStatus;
                }
            }
            return nextStatus;
        }

        public XElement Save(Map map, XElement parentElement)
        {
            var locationElement = new XElement("location",
                new XAttribute("type", Type.Identifier),
                new XAttribute("basename", BaseName),
                new XAttribute("name", Name),
                new XAttribute("discovered", Discovered),
                new XAttribute("position", XMLExtensions.Vector2ToString(MapPosition)),
                new XAttribute("pricemultiplier", PriceMultiplier),
                new XAttribute("isgatebetweenbiomes", IsGateBetweenBiomes),
                new XAttribute("mechanicalpricemultipler", MechanicalPriceMultiplier),
                new XAttribute("timesincelasttypechange", TimeSinceLastTypeChange),
                new XAttribute(nameof(TurnsInRadiation).ToLower(), TurnsInRadiation));
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
                }
                else
                {
                    changeElement.Add(new XAttribute("index", Type.CanChangeTo.IndexOf(PendingLocationTypeChange.Value.typeChange)));
                }
                locationElement.Add(changeElement);
            }

            if (LocationTypeChangeCooldown > 0)
            {
                locationElement.Add(new XAttribute("locationtypechangecooldown", LocationTypeChangeCooldown));
            }

            if (takenItems.Any())
            {
                locationElement.Add(new XAttribute(
                    "takenitems",
                    string.Join(',', takenItems.Select(it => it.Identifier + ";" + it.OriginalID + ";" + it.OriginalContainerID + ";" + it.ModuleIndex))));
            }
            if (killedCharacterIdentifiers.Any())
            {
                locationElement.Add(new XAttribute("killedcharacters", string.Join(',', killedCharacterIdentifiers)));
            }

            if (StoreStock != null)
            {
                var storeElement = new XElement("store",
                    new XAttribute("balance", StoreCurrentBalance),
                    new XAttribute("pricemodifier", StorePriceModifier),
                    new XAttribute("stepssincespecialsupdated", StepsSinceSpecialsUpdated));

                foreach (PurchasedItem item in StoreStock)
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

                locationElement.Add(storeElement);
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
                        new XAttribute("selected", mission == SelectedMission)));
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
    }
}
