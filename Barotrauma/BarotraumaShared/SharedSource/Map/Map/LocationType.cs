using Barotrauma.Extensions;
using Barotrauma.IO;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace Barotrauma
{
    public class LocationType : PrefabWithUintIdentifier
    {
        public static readonly PrefabCollection<LocationType> Prefabs = new PrefabCollection<LocationType>();

        public readonly ContentXElement ConfigElement;

        private readonly ImmutableArray<string> rawNames;
        private readonly ImmutableArray<Sprite> portraits;

        //<name, commonness>
        private readonly ImmutableArray<(Identifier Identifier, float Commonness, bool AlwaysAvailableIfMissingFromCrew)> hireableJobs;
        private readonly float totalHireableWeight;

        public readonly LocalizedString Name;

        public readonly LocalizedString Description;

        /// <summary>
        /// Forces all locations of this LocationType to use the given name. Can either be a text tag or the actual name.
        /// </summary>
        public readonly Identifier ForceLocationName; 

        public readonly float BeaconStationChance;

        public readonly CharacterTeamType OutpostTeam;

        /// <summary>
        /// Should automatic docking always be refused when approaching the outpost in this location?
        /// </summary>
        public readonly bool AlwaysRefuseAutoDocking;

        /// <summary>
        /// Should automatic docking be refused when entering this type of outpost for the first time?
        /// </summary>
        public readonly bool RefuseAutoDockingOnFirstVisit;

        /// <summary>
        /// Custom text displayed when auto docking is refused in this location.
        /// </summary>
        public readonly string AutoDockingRefuseText;

        /// <summary>
        /// Is this location type considered valid for e.g. events and missions that should be available in "any outpost"?
        /// </summary>
        public bool IsValidForAnyOutpostEventOrMission;

        public readonly List<LocationTypeChange> CanChangeTo = new List<LocationTypeChange>();

        /// <summary>
        /// If set, the type of the location can never be changed (e.g. by missions, events, or a player claiming the outpost)
        /// </summary>
        public bool DisallowLocationTypeChanges;

        public readonly ImmutableArray<Identifier> MissionIdentifiers;
        public readonly ImmutableArray<Identifier> MissionTags;

        public abstract class AreaSettingData
        {
            public int? MinCount { get; }
            public int? MaxCount { get; }
            public float Commonness { get; }

            /// <summary>
            /// Desired position in the biome or difficulty zone. A value between 0 and 1, where 0 is the left side of the zone/biome, and 1 the right side.
            /// </summary>
            public float? DesiredPosition { get; }
            
            public bool HasCounts => MinCount.HasValue && MaxCount.HasValue && (MaxCount > 0 || MinCount > 0);
            public virtual bool HasValidData => true;

            internal AreaSettingData(int? minCount, int? maxCount, float commonness, float? desiredPosition)
            {
                MinCount = minCount;
                MaxCount = maxCount;
                Commonness = commonness;
                DesiredPosition = desiredPosition;
            }

            public virtual bool MatchesRemainingCount(MapLocationTypeGenerator.LocationTypeCount locationTypeCount)
            {
                return false;
            }

            public virtual bool MatchesLocation(Map map, Location location)
            {
                return false;
            }
            
            public virtual bool MatchesZone(int zoneIndex)
            {
                return false;
            }
            
            public virtual bool MatchesBiome(Identifier biomeIdentifier)
            {
                return false;
            }
            
            public virtual bool Matches(int? zone = null, Identifier? biomeId = null)
            {
                return false;
            }
        }

        public class BiomeSettingData : AreaSettingData
        {
            public Identifier BiomeIdentifier { get; }

            public override bool HasValidData => Biome.Prefabs.ContainsKey(BiomeIdentifier);

            public BiomeSettingData(Identifier biomeIdentifier, int? minCount, int? maxCount, float commonness, float? desiredPosition, LocationType locationType)
                : base(minCount, maxCount, commonness, desiredPosition)
            {
                if (minCount > maxCount)
                {
                    DebugConsole.AddWarning($"Error in location type {locationType.Identifier}: minimum count larger than maximum count in biome {biomeIdentifier}.", 
                        contentPackage: locationType.ContentPackage);
                }
                BiomeIdentifier = biomeIdentifier;
            }
            
            public override bool MatchesRemainingCount(MapLocationTypeGenerator.LocationTypeCount locationTypeCount)
            {
                return locationTypeCount.BiomeId == BiomeIdentifier;
            }

            public override bool MatchesLocation(Map map, Location location)
            {
                return BiomeIdentifier == location.Biome?.Identifier;
            }
            
            public override bool MatchesBiome(Identifier biomeIdentifier)
            {
                return BiomeIdentifier == biomeIdentifier;
            }
            
            public override bool Matches(int? zone = null, Identifier? biomeId = null)
            {
                return biomeId.HasValue && biomeId == BiomeIdentifier;
            }
        }
        
        public class DifficultyZoneSettingData : AreaSettingData
        {
            public int DifficultyZone { get; }

            public DifficultyZoneSettingData(int difficultyZone, int? minCount, int? maxCount, float commonness, float? desiredPosition, LocationType locationType)
                : base(minCount, maxCount, commonness, desiredPosition)
            {
                if (minCount > maxCount)
                {
                    DebugConsole.AddWarning($"Error in location type {locationType.Identifier}: minimum count larger than maximum count in difficulty zone {difficultyZone}.",
                        contentPackage: locationType.ContentPackage);
                }
                DifficultyZone = difficultyZone;
            }
            
            public override bool MatchesRemainingCount(MapLocationTypeGenerator.LocationTypeCount locationTypeCount)
            {
                return locationTypeCount.DifficultyZone == DifficultyZone;
            }
            
            public override bool MatchesLocation(Map map, Location location)
            {
                return DifficultyZone == map.GetZoneIndex(location.MapPosition.X);
            }
            
            public override bool MatchesZone(int zoneIndex)
            {
                return DifficultyZone == zoneIndex;
            }
            
            public override bool Matches(int? zone = null, Identifier? biomeId = null)
            {
                return zone.HasValue && zone == DifficultyZone;
            }
        }

        public readonly List<AreaSettingData> AreaSettings = new List<AreaSettingData>();

        public readonly List<string> HideEntitySubcategories;

        public enum BiomeGateSetting
        {
            /// <summary>
            /// Can be used as a gate between biomes, but not required
            /// </summary>
            Allow,
            /// <summary>
            /// Cannot be used as a gate between biomes
            /// </summary>
            Deny, 
            /// <summary>
            /// Must be used as a gate between biomes during map generation
            /// </summary>
            Force 
        }
        
        public BiomeGateSetting BiomeGate { get; private set; }
       
        public bool ForceAsStartOutpost { get; private set; }

        /// <summary>
        /// Can this location type be used in the random, non-campaign levels that don't take place in any specific zone
        /// </summary>
        public bool AllowInRandomLevels { get; private set; }

        public bool UsePortraitInRandomLoadingScreens
        {
            get;
            private set;
        }

        private readonly ImmutableArray<Identifier>? nameIdentifiers = null;

        private LanguageIdentifier nameFormatLanguage;

        private ImmutableArray<string>? nameFormats = null;
        public IReadOnlyList<string> NameFormats
        {
            get
            {
                if (nameFormats == null || GameSettings.CurrentConfig.Language != nameFormatLanguage)
                {
                    nameFormats = TextManager.GetAll($"LocationNameFormat.{Identifier}").ToImmutableArray();
                    nameFormatLanguage = GameSettings.CurrentConfig.Language;
                }
                return nameFormats;
            }
        }

        /// <summary>
        /// Does this location type by default have any hireable characters? Note that the faction the location belongs to might offer some hires on top of this.
        /// Use <see cref="Location.HasHireableCharacters"/> unless you want to specifically exclude the faction-specific hires.
        /// </summary>
        public bool HasHireableCharacters
        {
            get { return hireableJobs.Any(); }
        }

        public bool HasOutpost
        {
            get;
            private set;
        }

        /// <summary>
        /// How many world steps it takes until killed characters are replaced with new ones?
        /// </summary>
        public int ReplaceKilledCharactersAfter;

        /// <summary>
        /// What percentage of the default population must be alive before killed characters stop 
        /// being replaced and the outpost starts a countdown to being abandoned?
        /// </summary>
        public float InsufficientPopulationThreshold;

        public Identifier ReplaceInRadiation { get; }

        public Identifier DescriptionInRadiation { get; }

        /// <summary>
        /// If set, forces the location to be assigned to this faction. Set to "None" if you don't want the location to be assigned to any faction.
        /// </summary>
        public Identifier Faction { get; }

        /// <summary>
        /// If set, forces the location to be assigned to this secondary faction. Set to "None" if you don't want the location to be assigned to any secondary faction.
        /// </summary>
        public Identifier SecondaryFaction { get; }

        public Sprite Sprite { get; private set; }
        public Sprite RadiationSprite { get; }

        /// <summary>
        /// If enabled, an indicator (icon and an arrow pointing towards the location) will be shown on the map when the location is off-screen. Defaults to false.
        /// </summary>
        public bool ShowOffScreenIndicator { get; private set; }

        private readonly Identifier forceOutpostGenerationParamsIdentifier;

        /// <summary>
        /// Can be used to make the location type use the same background music tracks as another location type.
        /// </summary>
        public readonly Identifier BackgroundMusicLocationType;

        /// <summary>
        /// If set to true, only event sets that explicitly define this location type in <see cref="EventSet.LocationTypeIdentifiers"/> can be selected at this location. Defaults to false.
        /// </summary>
        public bool IgnoreGenericEvents { get; }
        
        /// <summary>
        /// Used as criteria for validating if a given event set is suitable for this locationType.
        /// For example, if set to "city", events that appear in "city" type locations can also appear here (in addition to events configured to be suitable specifically for this location type).
        /// </summary>
        public Identifier EventLocationType { get; private set; }

        /// <summary>
        /// Can be used to make the submarine tiers available from the given location type also available from this location type.
        /// For example, if set to "city", submarine tiers that can be purchased from the "city" type locations can also be purchased from here.
        /// </summary>
        public Identifier SubmarineTierLocationType { get; private set; }

        /// <summary>
        /// If set, outpost modules configured to be suitable for the specified location type can also be used in this type of location.
        /// </summary>
        public Identifier UseOutpostModulesOfLocationType { get; set; }

        /// <summary>
        /// Should submarines that can be sold by any faction (= submarines without any <see cref="SubmarineInfo.SoldByFaction"/> restrictions) be sellable at this location type?
        /// </summary>
        public bool SellSubmarinesSoldByAnyFaction { get; private set; }

        public Color SpriteColor
        {
            get;
            private set;
        }

        /// <summary>
        /// Identifiers of the stores that a location of this type is forced to have.
        /// Can be used to allow stores that are normally not available in this location type, in case there's e.g. ways for events to spawn some special merchants in the location,
        /// without that merchant being present in the outpost generation parameters.
        /// </summary>
        public ImmutableArray<Identifier> ForcedStoreIdentifiers;

        public float StoreMaxReputationModifier { get; } = 0.1f;
        public float StoreMinReputationModifier { get; } = 1.0f;
        public float StoreSellPriceModifier { get; } = 0.3f;
        public float StoreBuyPriceModifier { get; } = 1f;
        public float DailySpecialPriceModifier { get; } = 0.5f;
        public float RequestGoodPriceModifier { get; } = 2f;
        public float RequestGoodBuyPriceModifier { get; } = 5f;
        public int StoreInitialBalance { get; } = 5000;
        /// <summary>
        /// In percentages
        /// </summary>
        public int StorePriceModifierRange { get; } = 5;

        /// <summary>
        /// Default number of daily specials in stores. Defaults to 1. Can be overridden by the Store definition.
        /// </summary>
        public int DailySpecialsCount { get; } = 1;

        /// <summary>
        /// Default number of requested goods in stores. Defaults to 1. Can be overridden by the Store definition.
        /// </summary>
        public int RequestedGoodsCount { get; } = 1;

        public float ShipyardPriceMultiplier { get; } = 1.0f;

        /// <summary>
        /// When unlocking a mission, the destination is chosen semi-randomly, preferring paths with less missions, and paths that lead further on the map.
        /// This can be used to increase/decrease the preference of choosing a path leading to this location type. Used as a multiplier for the weights of the paths (e.g. 10 would mean the path is x10 more likely to be selected).
        /// </summary>
        public float MissionDestinationPreferability { get; } = 1;

        /// <summary>
        /// When unlocking a mission, the destination is chosen semi-randomly, preferring paths with less missions, and paths that lead further on the map.
        /// This can be used to ensure at least one mission is unlocked on the path leading to this location type, even if it requires backtracking.
        /// </summary>
        public bool TryToGuaranteeAsMissionDestination { get; }

        public readonly bool ShowSonarMarker;

        public override string ToString()
        {
            return $"{nameof(LocationType)} ({Identifier})";
        }

        public LocationType(ContentXElement element, LocationTypesFile file) : base(file, element.GetAttributeIdentifier("identifier", element.Name.LocalName))
        {
            ConfigElement = element;

            Name = TextManager.Get("LocationName." + Identifier, "unknown");
            Description = TextManager.Get("LocationDescription." + Identifier, "");
            
            // for location types based on others, e.g., Named Unique outpost, we may want to override the name of the type to still say Outpost on the map:
            var forceNameId = element.GetAttributeIdentifier("ForceLocationTypeName", string.Empty);
            if (!forceNameId.IsEmpty)
            {
                var forcedName = TextManager.Get("LocationName." + forceNameId);
                if (!forcedName.IsNullOrEmpty())
                {
                    Name = forcedName;
                }
            }
            
            var forceDescriptionId = element.GetAttributeIdentifier("ForceLocationTypeDescription", string.Empty);
            if (!forceDescriptionId.IsEmpty)
            {
                var forcedDescription = TextManager.Get("LocationDescription." + forceDescriptionId);
                if (!forcedDescription.IsNullOrEmpty())
                {
                    Description = forcedDescription;
                }
            }
            
            BeaconStationChance = element.GetAttributeFloat("beaconstationchance", 0.0f);

            UsePortraitInRandomLoadingScreens = element.GetAttributeBool(nameof(UsePortraitInRandomLoadingScreens), true);
            HasOutpost = element.GetAttributeBool("hasoutpost", true);
            bool allowAsBiomeGateLegacy = element.GetAttributeBool("allowasbiomegate", true);
            BiomeGate = element.GetAttributeEnum("BiomeGate", def: allowAsBiomeGateLegacy ? BiomeGateSetting.Allow : BiomeGateSetting.Deny);
            if (BiomeGate != BiomeGateSetting.Deny && !HasOutpost)
            {
                DebugConsole.AddWarning($"Potential error in location type {Identifier}: the location is set to be allowed as a biome gate, but will never be chosen as one because it has no outpost.",
                    contentPackage: ContentPackage);
            }

            ForceAsStartOutpost = element.GetAttributeBool(nameof(ForceAsStartOutpost), false);
            AllowInRandomLevels = element.GetAttributeBool(nameof(AllowInRandomLevels), true);

            ShowOffScreenIndicator = element.GetAttributeBool(nameof(ShowOffScreenIndicator), false);

            Faction = element.GetAttributeIdentifier(nameof(Faction), Identifier.Empty);
            SecondaryFaction = element.GetAttributeIdentifier(nameof(SecondaryFaction), Identifier.Empty);

            ShowSonarMarker = element.GetAttributeBool("showsonarmarker", true);

            MissionIdentifiers = element.GetAttributeIdentifierArray("missionidentifiers", Array.Empty<Identifier>()).ToImmutableArray();
            MissionTags = element.GetAttributeIdentifierArray("missiontags", Array.Empty<Identifier>()).ToImmutableArray();

            HideEntitySubcategories = element.GetAttributeStringArray("hideentitysubcategories", Array.Empty<string>()).ToList();

            ReplaceInRadiation = element.GetAttributeIdentifier(nameof(ReplaceInRadiation), Identifier.Empty);
            DescriptionInRadiation = element.GetAttributeIdentifier(nameof(DescriptionInRadiation), "locationdescription.abandonedirradiated");

            ReplaceKilledCharactersAfter = element.GetAttributeInt(nameof(ReplaceKilledCharactersAfter), 5);
            InsufficientPopulationThreshold = element.GetAttributeFloat(nameof(InsufficientPopulationThreshold), 0.25f);

            forceOutpostGenerationParamsIdentifier = element.GetAttributeIdentifier("forceoutpostgenerationparams", Identifier.Empty);
            BackgroundMusicLocationType = element.GetAttributeIdentifier(nameof(BackgroundMusicLocationType), Identifier.Empty);

            IgnoreGenericEvents = element.GetAttributeBool(nameof(IgnoreGenericEvents), false);
            
            EventLocationType = element.GetAttributeIdentifier(nameof(EventLocationType), Identifier.Empty);
            SubmarineTierLocationType = element.GetAttributeIdentifier(nameof(SubmarineTierLocationType), Identifier.Empty);
            UseOutpostModulesOfLocationType = element.GetAttributeIdentifier(nameof(UseOutpostModulesOfLocationType), Identifier.Empty);
            SellSubmarinesSoldByAnyFaction = element.GetAttributeBool(nameof(SellSubmarinesSoldByAnyFaction), true);

            MissionDestinationPreferability = element.GetAttributeFloat(nameof(MissionDestinationPreferability), 1.0f);
            TryToGuaranteeAsMissionDestination = element.GetAttributeBool(nameof(TryToGuaranteeAsMissionDestination), false);

            DisallowLocationTypeChanges = element.GetAttributeBool(nameof(DisallowLocationTypeChanges), false);

            IsValidForAnyOutpostEventOrMission = element.GetAttributeBool(nameof(IsValidForAnyOutpostEventOrMission), def:
                // For backwards compatibility:
                element.GetAttributeBool("IsAnyOutpost", def:
                    HasOutpost));

            AlwaysRefuseAutoDocking = element.GetAttributeBool(nameof(AlwaysRefuseAutoDocking), def: false);
            RefuseAutoDockingOnFirstVisit = element.GetAttributeBool(nameof(RefuseAutoDockingOnFirstVisit), def: false);
            AutoDockingRefuseText = element.GetAttributeString(nameof(AutoDockingRefuseText), def: "");

            ForcedStoreIdentifiers = element.GetAttributeIdentifierArray("forcedstoreidentifiers", Array.Empty<Identifier>()).ToImmutableArray();

            string teamStr = element.GetAttributeString("outpostteam", "FriendlyNPC");
            Enum.TryParse(teamStr, out OutpostTeam);

            if (element.GetAttribute(nameof(ForceLocationName)) != null ||
                element.GetAttribute("name") != null)
            {
                ForceLocationName = element.GetAttributeIdentifier(nameof(ForceLocationName), 
                    //backwards compatibility
                    def: element.GetAttributeIdentifier("name", string.Empty));
            }
            else
            {
                var names = new List<string>();
                //backwards compatibility for location names defined in a text file
                string[] rawNamePaths = element.GetAttributeStringArray("namefile", Array.Empty<string>());
                if (rawNamePaths.Any())
                {
                    foreach (string rawPath in rawNamePaths)
                    {
                        try
                        {
                            var path = ContentPath.FromRaw(element.ContentPackage, rawPath.Trim());
                            names.AddRange(File.ReadAllLines(path.Value, catchUnauthorizedAccessExceptions: false).ToList());
                        }
                        catch (Exception e)
                        {
                            DebugConsole.ThrowError($"Failed to read name file \"rawPath\" for location type \"{Identifier}\"!", e);
                        }
                    }
                    if (!names.Any())
                    {
                        names.Add("ERROR: No names found");
                    }
                    this.rawNames = names.ToImmutableArray();
                }
                else
                {
                    nameIdentifiers = element.GetAttributeIdentifierArray("nameidentifiers", new Identifier[] { Identifier }).ToImmutableArray();
                }
            }

            string[] commonnessPerZoneStrs = element.GetAttributeStringArray("commonnessperzone", Array.Empty<string>());
            foreach (string commonnessPerZoneStr in commonnessPerZoneStrs)
            {
                string[] splitCommonnessPerZone = commonnessPerZoneStr.Split(':');
                if (splitCommonnessPerZone.Length != 2 ||
                    !int.TryParse(splitCommonnessPerZone[0].Trim(), out int zoneIndex) ||
                    !float.TryParse(splitCommonnessPerZone[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float zoneCommonness))
                {
                    DebugConsole.ThrowError("Failed to read commonness values for location type \"" + Identifier + "\" - commonness should be given in the format \"zone1index: zone1commonness, zone2index: zone2commonness\"", contentPackage: element.ContentPackage);
                    break;
                }

                if (zoneCommonness <= 0.0f) { continue; }

                AugmentDifficultyZoneSettings(zoneIndex, zoneCommonness, minCount: null);
            }

            string[] minCountPerZoneStrs = element.GetAttributeStringArray("mincountperzone", Array.Empty<string>());
            foreach (string minCountPerZoneStr in minCountPerZoneStrs)
            {
                string[] splitMinCountPerZone = minCountPerZoneStr.Split(':');
                if (splitMinCountPerZone.Length != 2 ||
                    !int.TryParse(splitMinCountPerZone[0].Trim(), out int zoneIndex) ||
                    !int.TryParse(splitMinCountPerZone[1].Trim(), out int minCount))
                {
                    DebugConsole.ThrowError("Failed to read minimum zone count values for location type \"" + Identifier + 
                                            "\" - minimum zone counts should be given in the format \"zone1index: zone1mincount, zone2index: zone2mincount\"", contentPackage: element.ContentPackage);
                    break;
                }

                if (minCount <= 0) { continue; }

                AugmentDifficultyZoneSettings(zoneIndex, zoneCommonness: null, minCount);
            }

            void AugmentDifficultyZoneSettings(int zoneIndex, float? zoneCommonness, int? minCount)
            {

                var existingSettings = AreaSettings.Find(areaSettingData => areaSettingData is DifficultyZoneSettingData difficultyZoneSettingData && 
                                                                                          difficultyZoneSettingData.DifficultyZone == zoneIndex);
                
                if (existingSettings != null)
                {
                    int index = AreaSettings.IndexOf(existingSettings);
                    AreaSettings[index] = new DifficultyZoneSettingData(zoneIndex, 
                        minCount ?? existingSettings.MinCount, 
                        //note that assigning minCount to maxCount is intentional here:
                        //previously it was only possible to define minCount (essentially the same as just defining "count" now)
                        maxCount: minCount ?? existingSettings.MaxCount, 
                        commonness: zoneCommonness ?? existingSettings.Commonness, desiredPosition: null, locationType: this);
                }
                else
                {
                    AreaSettings.Add(new DifficultyZoneSettingData(zoneIndex, minCount ?? 0, maxCount: minCount ?? 0, commonness: zoneCommonness ?? 0, desiredPosition: null, locationType: this));
                }
            }
            
            var portraitsList = new List<Sprite>();
            var hireableJobsList = new List<(Identifier, float, bool)>();
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "hireable":
                        Identifier jobIdentifier = subElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                        float jobCommonness = subElement.GetAttributeFloat("commonness", 1.0f);
                        bool availableIfMissing = subElement.GetAttributeBool("AlwaysAvailableIfMissingFromCrew", false);
                        totalHireableWeight += jobCommonness;
                        hireableJobsList.Add((jobIdentifier, jobCommonness, availableIfMissing));
                        break;
                    case "symbol":
                        Sprite = new Sprite(subElement, lazyLoad: true);
                        SpriteColor = subElement.GetAttributeColor("color", Color.White);
                        break;
                    case "radiationsymbol":
                        RadiationSprite = new Sprite(subElement, lazyLoad: true);
                        break;
                    case "changeto":
                        CanChangeTo.Add(new LocationTypeChange(Identifier, subElement, requireChangeMessages: true, parentDebugName: ToString()));
                        break;
                    case "portrait":
                        var portrait = new Sprite(subElement, lazyLoad: true);
                        if (portrait != null)
                        {
#if CLIENT
                            if (!File.Exists(portrait.FilePath))
                            {
                                DebugConsole.AddSafeError($"Error in location type \"{Identifier}\": cannot find the location portrait \"{portrait.FilePath}\".");
                            }
                            else
                            {
                                portraitsList.Add(portrait);
                            }
#elif SERVER
                            // Add without checking the path, since servers don't parse the file path of the sprite
                            portraitsList.Add(portrait);
#endif
                        }
                        break;
                    case "store":
                        StoreMaxReputationModifier = subElement.GetAttributeFloat("maxreputationmodifier", StoreMaxReputationModifier);
                        StoreBuyPriceModifier = subElement.GetAttributeFloat("buypricemodifier", StoreBuyPriceModifier);
                        StoreMinReputationModifier = subElement.GetAttributeFloat("minreputationmodifier", StoreMaxReputationModifier);
                        StoreSellPriceModifier = subElement.GetAttributeFloat("sellpricemodifier", StoreSellPriceModifier);
                        DailySpecialPriceModifier = subElement.GetAttributeFloat("dailyspecialpricemodifier", DailySpecialPriceModifier);
                        RequestGoodPriceModifier = subElement.GetAttributeFloat("requestgoodpricemodifier", RequestGoodPriceModifier);
                        RequestGoodBuyPriceModifier = subElement.GetAttributeFloat("requestgoodbuypricemodifier", RequestGoodBuyPriceModifier);
                        StoreInitialBalance = subElement.GetAttributeInt("initialbalance", StoreInitialBalance);
                        StorePriceModifierRange = subElement.GetAttributeInt("pricemodifierrange", StorePriceModifierRange);
                        DailySpecialsCount = subElement.GetAttributeInt("dailyspecialscount", DailySpecialsCount);
                        RequestedGoodsCount = subElement.GetAttributeInt("requestedgoodscount", RequestedGoodsCount);
                        ShipyardPriceMultiplier = subElement.GetAttributeFloat(nameof(ShipyardPriceMultiplier), ShipyardPriceMultiplier);
                        break;
                    case "areasettings":
                        ParseAreaSettings(subElement);
                        break;
                }
            }
            this.portraits = portraitsList.ToImmutableArray();
            this.hireableJobs = hireableJobsList.ToImmutableArray();
            
            void ParseAreaSettings(ContentXElement areaSettingsElement)
            {
                Identifier biomeIdentifier = areaSettingsElement.GetAttributeIdentifier("biome", Identifier.Empty);
                int zone = areaSettingsElement.GetAttributeInt("zone", 0);
                
                if (biomeIdentifier == Identifier.Empty && zone == 0)
                {
                    DebugConsole.ThrowError("Failed to read area settings for locationType \"" + Identifier + "\" - biome identifier and zone are both missing.", contentPackage: element.ContentPackage);
                    return;
                }
                
                if (biomeIdentifier != Identifier.Empty && zone != 0)
                {
                    DebugConsole.ThrowError("Failed to read area settings for locationType \"" + Identifier + "\" - both biome identifier and zone are defined. Must be one or the other.", contentPackage: element.ContentPackage);
                    return;
                }

                bool HasComma(string intAttributeName)
                {
                    var attr = areaSettingsElement.GetAttribute(intAttributeName);
                    if (attr == null) { return false;}
                    return attr.Value.Contains(',');
                }

                if (HasComma("mincount") || HasComma("maxcount") || HasComma("count"))
                {
                    DebugConsole.LogError($"AreaSettings for locationType {Identifier} has comma inside int count attribute. This causes the resulting parse to combine the numbers, resulting in incorrect amount of locations.",
                        contentPackage: ContentPackage);
                }
                
                int? minCount = areaSettingsElement.GetAttributeNullableInt("mincount");
                int? maxCount = areaSettingsElement.GetAttributeNullableInt("maxcount");
                int? count = areaSettingsElement.GetAttributeNullableInt("count");
                float? desiredPosition = areaSettingsElement.GetAttributeNullableFloat("desiredposition");
                float commonness = areaSettingsElement.GetAttributeFloat("commonness", 0);
                
                // if set, count overrides min and max count to eliminate randomness
                if (count.HasValue)
                {
                    minCount = count;
                    maxCount = count;
                }
                else if (minCount.HasValue && maxCount.HasValue && minCount <= 0 && maxCount <= 0)
                {
                    DebugConsole.AddWarning("Failed to read count value for location type \"" + Identifier + "\" - both min and max count are 0.", contentPackage: element.ContentPackage);
                    return;
                }
                
                if (biomeIdentifier != Identifier.Empty)
                {
                    AreaSettings.Add(new BiomeSettingData(biomeIdentifier, minCount, maxCount, commonness, desiredPosition, locationType: this));
                }
                else
                {
                    AreaSettings.Add(new DifficultyZoneSettingData(zone, minCount, maxCount, commonness, desiredPosition, locationType: this));
                }
            }
        }

        public IEnumerable<JobPrefab> GetHireablesMissingFromCrew()
        {
            if (GameMain.GameSession?.CrewManager != null)
            {
                var missingJobs = hireableJobs
                    .Where(j => j.AlwaysAvailableIfMissingFromCrew)
                    .Where(j => GameMain.GameSession.CrewManager.GetCharacterInfos().None(c => c.Job?.Prefab.Identifier == j.Identifier));
                if (missingJobs.Any())
                {
                    foreach (var missingJob in missingJobs)
                    {
                        if (JobPrefab.Prefabs.TryGet(missingJob.Identifier, out JobPrefab job))
                        {
                            yield return job;
                        }
                    }
                }
            }
        }

        public JobPrefab GetRandomHireable()
        {
            //hires are generated by the server and sent to clients, and the server may generate them at any point
            //(e.g. during level generation), which would make the synced RNG go out of sync

            //there is also no need for this to be deterministic, the hireables aren't saved: the server regenerates them when needed and sends them to the clients.
            Identifier selectedJobId = hireableJobs.GetRandomByWeight(j => j.Commonness, Rand.RandSync.Unsynced).Identifier;
            if (JobPrefab.Prefabs.TryGet(selectedJobId, out JobPrefab job))
            {
                return job;
            }
            return null;
        }

        public Sprite GetPortrait(int randomSeed)
        {
            if (portraits.Length == 0) { return null; }
            return portraits[Math.Abs(randomSeed) % portraits.Length];
        }

        public Identifier GetRandomNameId(Random rand, IEnumerable<Location> existingLocations)
        {
            if (nameIdentifiers == null)
            {
                return Identifier.Empty;
            }
            List<Identifier> nameIds = new List<Identifier>();
            foreach (var nameId in nameIdentifiers)
            {
                int index = 0;
                while (true)
                {
                    Identifier tag = $"LocationName.{nameId}.{index}".ToIdentifier();
                    if (TextManager.ContainsTag(tag, TextManager.DefaultLanguage))
                    {
                        nameIds.Add(tag);
                        index++;
                    }
                    else
                    {
                        if (index == 0)
                        {
                            DebugConsole.ThrowError($"Could not find any location names for the location type {Identifier}. Name identifier: {nameId}");
                        }
                        break;
                    }
                }
            }
            if (nameIds.None())
            {
                return Identifier.Empty;
            }
            if (existingLocations != null)
            {
                var unusedNameIds = nameIds.FindAll(nameId => existingLocations.None(l => l.NameIdentifier == nameId));
                if (unusedNameIds.Count > 0)
                {
                    return unusedNameIds[rand.Next() % unusedNameIds.Count];
                }
            }
            return nameIds[rand.Next() % nameIds.Count];
        }

        /// <summary>
        /// For backwards compatibility. Chooses a random name from the names defined in the .txt name files (<see cref="rawNamePaths"/>).
        /// </summary>
        public string GetRandomRawName(Random rand, IEnumerable<Location> existingLocations)
        {
            if (rawNames == null || rawNames.None()) { return string.Empty; }
            if (existingLocations != null)
            {
                var unusedNames = rawNames.Where(name => !existingLocations.Any(l => l.DisplayName.Value == name)).ToList();
                if (unusedNames.Count > 0)
                {
                    return unusedNames[rand.Next() % unusedNames.Count];
                }
            }
            return rawNames[rand.Next() % rawNames.Length];
        }

        public static LocationType Random(Random rand, int? zone = null, Identifier? biomeId = null, bool requireOutpost = false, Func<LocationType, bool> predicate = null)
        {
            Debug.Assert(Prefabs.Any(), "LocationType.list.Count == 0, you probably need to initialize LocationTypes");

            LocationType[] allowedLocationTypes =
                Prefabs.Where(lt =>
                    (predicate == null || predicate(lt)) && IsValid(lt))
                    .OrderBy(p => p.UintIdentifier).ToArray();

            bool IsValid(LocationType locationType)
            {
                if (requireOutpost && !locationType.HasOutpost) { return false; }

                bool validZone = !zone.HasValue || locationType.AreaSettings.Any(areaSetting => areaSetting.MatchesZone(zone.Value));
                bool validBiome = !biomeId.HasValue || locationType.AreaSettings.Any(areaSetting => areaSetting.MatchesBiome(biomeId.Value));

                if (!validZone && !validBiome) { return false; }

                //if zone is not defined, this is a "random" (non-campaign) level
                //-> don't choose location types that aren't allowed in those
                if (!zone.HasValue && !biomeId.HasValue && !locationType.AllowInRandomLevels)
                {
                    return false;
                }
                
                return true;
            }

            if (allowedLocationTypes.Length == 0)
            {
                DebugConsole.ThrowError("Could not generate a random location type - no location types for the zone " + zone + " found!");
            }

            if (zone.HasValue || biomeId.HasValue)
            {
                return ToolBox.SelectWeightedRandom(
                    allowedLocationTypes, 
                    allowedLocationTypes.Select(allowedType => 
                        allowedType.AreaSettings.Find(areaSetting => areaSetting.MatchesZone(zone.Value) || areaSetting.MatchesBiome(biomeId.Value))?.Commonness ?? 0).ToArray(),
                    rand);
            }
            else
            {
                return allowedLocationTypes[rand.Next() % allowedLocationTypes.Length];
            }
        }
        
        public bool IsValidForZoneOrBiome(int? zone, Identifier? biomeIdentifier)
        {
            //if zone is not defined, this is a "random" (non-campaign) level
            //-> don't choose location types that aren't allowed in those
            if (!zone.HasValue && !AllowInRandomLevels) { return false; }
            
            if (!zone.HasValue && !biomeIdentifier.HasValue) { return true; }
            
            return AreaSettings.Any(setting => setting.Matches(zone, biomeIdentifier));
        }

        public OutpostGenerationParams GetForcedOutpostGenerationParams()
        {
            if (OutpostGenerationParams.OutpostParams.TryGet(forceOutpostGenerationParamsIdentifier, out var parameters))
            {
                return parameters;
            }
            return null;
        }
        
        public bool HasCounts()
        {
            return AreaSettings.Any(setting => setting.HasCounts);
        }

        public override void Dispose() { }
    }
}
