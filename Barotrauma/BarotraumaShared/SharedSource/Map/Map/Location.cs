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
                OriginalID = item.OriginalID;
                ModuleIndex = (ushort)item.OriginalModuleIndex;
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
                    return item.OriginalID == OriginalID && item.OriginalModuleIndex == ModuleIndex && item.prefab.Identifier == Identifier;
                }
            }
        }

        public readonly List<LocationConnection> Connections = new List<LocationConnection>();
        
        private string baseName;
        private int nameFormatIndex;

        public bool Discovered;

        public int TypeChangeTimer;

        public string BaseName { get => baseName; }

        public string Name { get; private set; }

        public Biome Biome { get; set; }

        public Vector2 MapPosition { get; private set; }

        public LocationType Type { get; private set; }

        public LevelData LevelData { get; set; }

        private float normalizedDepth;
        public float NormalizedDepth
        {
            get { return normalizedDepth; }
            set
            {
                if (!MathUtils.IsValid(value)) { return; }
                normalizedDepth = MathHelper.Clamp(value, 0.0f, 1.0f);
            }
        }

        public int PortraitId { get; private set; }

        public Reputation Reputation { get; set; }

        private const float StoreMaxReputationModifier = 0.1f;
        private const float StoreSellPriceModifier = 0.8f;
        private const float MechanicalMaxDiscountPercentage = 50.0f;
        public const int StoreInitialBalance = 5000;
        public int StoreCurrentBalance { get; set; }
        public List<PurchasedItem> StoreStock { get; set; }

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
                availableMissions.RemoveAll(m => m.Completed);
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

        public Location(Vector2 mapPosition, int? zone, Random rand, bool requireOutpost = false, IEnumerable<Location> existingLocations = null)
        {
            Type = LocationType.Random(rand, zone, requireOutpost);
            Name = RandomName(Type, rand, existingLocations);
            MapPosition = mapPosition;
            PortraitId = ToolBox.StringToInt(Name);
            Connections = new List<LocationConnection>();
        }

        public Location(XElement element)
        {
            string locationType = element.GetAttributeString("type", "");
            Type = LocationType.List.Find(lt => lt.Identifier.Equals(locationType, StringComparison.OrdinalIgnoreCase));
            baseName        = element.GetAttributeString("basename", "");
            Name            = element.GetAttributeString("name", "");
            MapPosition     = element.GetAttributeVector2("position", Vector2.Zero);
            NormalizedDepth = element.GetAttributeFloat("normalizeddepth", 0.0f);
            TypeChangeTimer = element.GetAttributeInt("changetimer", 0);
            Discovered      = element.GetAttributeBool("discovered", false);
            PriceMultiplier = element.GetAttributeFloat("pricemultiplier", 1.0f);
            MechanicalPriceMultiplier = element.GetAttributeFloat("mechanicalpricemultipler", 1.0f);

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
            
            if (element.GetChildElement("store") is XElement storeElement)
            {
                StoreCurrentBalance = storeElement.GetAttributeInt("balance", StoreInitialBalance);
                StoreStock = LoadStoreStock(storeElement);
            }

            LoadMissions(element);
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


        public static Location CreateRandom(Vector2 position, int? zone, Random rand, bool requireOutpost, IEnumerable<Location> existingLocations = null)
        {
            return new Location(position, zone, rand, requireOutpost, existingLocations);
        }

        public void ChangeType(LocationType newType)
        {
            if (newType == Type) { return; }

            DebugConsole.Log("Location " + baseName + " changed it's type from " + Type + " to " + newType);

            Type = newType;
            Name = Type.NameFormats[nameFormatIndex % Type.NameFormats.Count].Replace("[name]", baseName);
            CreateStore(force: true);
        }

        public void UnlockMission(MissionPrefab missionPrefab, LocationConnection connection)
        {
            if (AvailableMissions.Any(m => m.Prefab == missionPrefab)) { return; }
            availableMissions.Add(InstantiateMission(missionPrefab, connection));
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
                var mission = InstantiateMission(missionPrefab);
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
                    var suitableMissions = unusedMissions.Where(m => Connections.Any(c => m.IsAllowed(this, c.OtherLocation(this))));
                    if (!suitableMissions.Any())
                    {
                        suitableMissions = unusedMissions;
                    }
                    MissionPrefab missionPrefab = suitableMissions.GetRandom();
                    var mission = InstantiateMission(missionPrefab);
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

        private Mission InstantiateMission(MissionPrefab prefab, LocationConnection connection = null)
        {
            if (connection == null)
            {
                var suitableConnections = Connections.Where(c => prefab.IsAllowed(this, c.OtherLocation(this)));
                if (!suitableConnections.Any())
                {
                    suitableConnections = Connections;
                }
                //prefer connections that haven't been passed through, and connections with fewer available missions
                connection = ToolBox.SelectWeightedRandom(
                    suitableConnections.ToList(),
                    suitableConnections.Select(c => (c.Passed ? 1.0f : 5.0f) / Math.Max(availableMissions.Count(m => m.Locations.Contains(c.OtherLocation(this))), 1.0f)).ToList(),
                    Rand.RandSync.Unsynced);
            }

            Location destination = connection.OtherLocation(this);
            return prefab.Instantiate(new Location[] { this, destination });
        }

        public void InstantiateLoadedMissions(Map map)
        {
            availableMissions.Clear();
            if (loadedMissions == null || loadedMissions.None()) { return; }
            foreach (LoadedMission loadedMission in loadedMissions)
            {
                Location destination = null;
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

        public IEnumerable<Mission> GetMissionsInConnection(LocationConnection connection)
        {
            System.Diagnostics.Debug.Assert(Connections.Contains(connection));
            return AvailableMissions.Where(m => m.Locations[1] == connection.OtherLocation(this));
        }
        
        public void RemoveHireableCharacter(CharacterInfo character)
        {
            if (!Type.HasHireableCharacters)
            {
                DebugConsole.ThrowError("Cannot hire a character from location \"" + Name + "\" - the location has no hireable characters.\n" + Environment.StackTrace);
                return;
            }
            if (HireManager == null)
            {
                DebugConsole.ThrowError("Cannot hire a character from location \"" + Name + "\" - hire manager has not been instantiated.\n" + Environment.StackTrace);
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

        private List<PurchasedItem> CreateStoreStock()
        {
            var stock = new List<PurchasedItem>();
            foreach (ItemPrefab prefab in ItemPrefab.Prefabs)
            {
                if (prefab.CanBeBoughtAtLocation(this, out PriceInfo priceInfo))
                {
                    var quantity = priceInfo.MinAvailableAmount > 0 ? priceInfo.MinAvailableAmount :
                        (priceInfo.MaxAvailableAmount > 0 ? Math.Min(priceInfo.MaxAvailableAmount, 5) : 5);
                    stock.Add(new PurchasedItem(prefab, quantity));
                }
            }
            return stock;
        }

        public static List<PurchasedItem> LoadStoreStock(XElement storeElement)
        {
            var stock = new List<PurchasedItem>();
            if (storeElement == null) { return stock; }
            foreach (XElement stockElement in storeElement.GetChildElements("stock"))
            {
                var id = stockElement.GetAttributeString("id", null);
                if (string.IsNullOrWhiteSpace(id)) { continue; }
                var prefab = ItemPrefab.Prefabs.Find(p => p.Identifier == id);
                if (prefab == null) { continue; }
                var qty = stockElement.GetAttributeInt("qty", 0);
                if (qty < 1) { continue; }
                stock.Add(new PurchasedItem(prefab, qty));
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
                if (takenItems.Any(it => it.Matches(item) && it.OriginalID == item.OriginalID)) { continue; }
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

        public int GetAdjustedItemBuyPrice(PriceInfo priceInfo)
        {
            // TODO: Check priceInfo.CanBeBought
            if (priceInfo == null) { return 0; }
            var price = priceInfo.Price;
            if (Reputation.Value > 0.0f)
            {
                price = (int)(MathHelper.Lerp(1.0f, 1.0f - StoreMaxReputationModifier, Reputation.Value / Reputation.MaxReputation) * price);
            }
            else
            {
                price = (int)(MathHelper.Lerp(1.0f, 1.0f + StoreMaxReputationModifier, Reputation.Value / Reputation.MinReputation) * price);
            }
            // Item price should never go below 1 mk
            return Math.Max(price, 1);
        }

        /// <summary>
        /// If item.GetPriceInfo() returns null, this will return 0
        /// </summary>
        public int GetAdjustedItemBuyPrice(ItemPrefab item) => GetAdjustedItemBuyPrice(item?.GetPriceInfo(this));

        public int GetAdjustedItemSellPrice(PriceInfo priceInfo)
        {
            if (priceInfo == null) { return 0; }
            var price = (int)(StoreSellPriceModifier * priceInfo.Price);
            if (Reputation.Value > 0.0f)
            {
                price = (int)(MathHelper.Lerp(1.0f, 1.0f + StoreMaxReputationModifier, Reputation.Value / Reputation.MaxReputation) * price);
            }
            else
            {
                price = (int)(MathHelper.Lerp(1.0f, 1.0f - StoreMaxReputationModifier, Reputation.Value / Reputation.MinReputation) * price);
            }
            // Item price should never go below 1 mk
            return Math.Max(price, 1);
        }

        /// <summary>
        /// If item.GetPriceInfo() returns null, this will return 0
        /// </summary>
        public int GetAdjustedItemSellPrice(ItemPrefab item) => GetAdjustedItemSellPrice(item?.GetPriceInfo(this));

        public int GetAdjustedMechanicalCost(int cost)
        {
            float discount = Reputation.Value / Reputation.MaxReputation * (MechanicalMaxDiscountPercentage / 100.0f);
            return (int) Math.Ceiling((1.0f - discount) * cost * MechanicalPriceMultiplier);
        }

        /// <summary>
        /// If 'force' is true, the stock will be recreated even if it has been created previously already.
        /// This is used when (at least) when the type of the location changes.
        /// </summary>
        public void CreateStore(bool force = false)
        {
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
        }

        public void UpdateStore()
        {
            if (StoreStock == null)
            {
                CreateStore();
                return;
            }

            if (StoreCurrentBalance < StoreInitialBalance)
            {
                StoreCurrentBalance = Math.Min(StoreCurrentBalance + (int)(StoreInitialBalance / 10.0f), StoreInitialBalance);
            }

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

        public XElement Save(Map map, XElement parentElement)
        {
            var locationElement = new XElement("location",
                new XAttribute("type", Type.Identifier),
                new XAttribute("basename", BaseName),
                new XAttribute("name", Name),
                new XAttribute("discovered", Discovered),
                new XAttribute("position", XMLExtensions.Vector2ToString(MapPosition)),
                new XAttribute("normalizeddepth", NormalizedDepth.ToString("G", CultureInfo.InvariantCulture)),
                new XAttribute("pricemultiplier", PriceMultiplier),
                new XAttribute("mechanicalpricemultipler", MechanicalPriceMultiplier));
            LevelData.Save(locationElement);

            if (TypeChangeTimer > 0)
            {
                locationElement.Add(new XAttribute("changetimer", TypeChangeTimer));
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
                var storeElement = new XElement("store", new XAttribute("balance", StoreCurrentBalance));
                foreach (PurchasedItem item in StoreStock)
                {
                    if (item?.ItemPrefab == null) { continue; }
                    storeElement.Add(new XElement("stock",
                        new XAttribute("id", item.ItemPrefab.Identifier),
                        new XAttribute("qty", item.Quantity)));
                }
                locationElement.Add(storeElement);
            }

            if (AvailableMissions is List<Mission> missions && missions.Any())
            {
                var missionsElement = new XElement("missions");
                foreach (Mission mission in missions)
                {
                    var location = mission.Locations.FirstOrDefault(l => l != this);
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
