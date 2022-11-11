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
    class LocationType : PrefabWithUintIdentifier
    {
        public static readonly PrefabCollection<LocationType> Prefabs = new PrefabCollection<LocationType>();

        private readonly List<string> names;
        private readonly List<Sprite> portraits = new List<Sprite>();

        //<name, commonness>
        private readonly ImmutableArray<(Identifier Name, float Commonness)> hireableJobs;
        private readonly float totalHireableWeight;

        public readonly Dictionary<int, float> CommonnessPerZone = new Dictionary<int, float>();
        public readonly Dictionary<int, int> MinCountPerZone = new Dictionary<int, int>();

        public readonly LocalizedString Name;
        public readonly LocalizedString Description;

        public readonly float BeaconStationChance;

        public readonly CharacterTeamType OutpostTeam;

        public readonly List<LocationTypeChange> CanChangeTo = new List<LocationTypeChange>();

        public readonly ImmutableArray<Identifier> MissionIdentifiers;
        public readonly ImmutableArray<Identifier> MissionTags;

        public readonly List<string> HideEntitySubcategories = new List<string>();

        public bool IsEnterable { get; private set; }

        public bool UseInMainMenu
        {
            get;
            private set;
        }

        private ImmutableArray<string>? nameFormats = null;
        public IReadOnlyList<string> NameFormats
        {
            get
            {
                nameFormats ??= TextManager.GetAll($"LocationNameFormat.{Identifier}").ToImmutableArray();
                return nameFormats;
            }
        }

        public bool HasHireableCharacters
        {
            get { return hireableJobs.Any(); }
        }

        public bool HasOutpost
        {
            get;
            private set;
        }

        public Identifier ReplaceInRadiation { get; }

        public Sprite Sprite { get; private set; }
        public Sprite RadiationSprite { get; }

        private readonly Identifier forceOutpostGenerationParamsIdentifier;

        /// <summary>
        /// If set to true, only event sets that explicitly define this location type in <see cref="EventSet.LocationTypeIdentifiers"/> can be selected at this location. Defaults to false.
        /// </summary>
        public bool IgnoreGenericEvents { get; }

        public Color SpriteColor
        {
            get;
            private set;
        }

        public float StoreMaxReputationModifier { get; } = 0.1f;
        public float StoreSellPriceModifier { get; } = 0.8f;
        public float DailySpecialPriceModifier { get; } = 0.5f;
        public float RequestGoodPriceModifier { get; } = 1.5f;
        public int StoreInitialBalance { get; } = 5000;
        /// <summary>
        /// In percentages
        /// </summary>
        public int StorePriceModifierRange { get; } = 5;
        public int DailySpecialsCount { get; } = 1;
        public int RequestedGoodsCount { get; } = 1;

        public override string ToString()
        {
            return $"LocationType (" + Identifier + ")";
        }

        public LocationType(ContentXElement element, LocationTypesFile file) : base(file, element.GetAttributeIdentifier("identifier", element.Name.LocalName))
        {
            Name = TextManager.Get("LocationName." + Identifier, "unknown");
            Description = TextManager.Get("LocationDescription." + Identifier, "");

            BeaconStationChance = element.GetAttributeFloat("beaconstationchance", 0.0f);

            UseInMainMenu = element.GetAttributeBool("useinmainmenu", false);
            HasOutpost = element.GetAttributeBool("hasoutpost", true);
            IsEnterable = element.GetAttributeBool("isenterable", HasOutpost);

            MissionIdentifiers = element.GetAttributeIdentifierArray("missionidentifiers", Array.Empty<Identifier>()).ToImmutableArray();
            MissionTags = element.GetAttributeIdentifierArray("missiontags", Array.Empty<Identifier>()).ToImmutableArray();

            HideEntitySubcategories = element.GetAttributeStringArray("hideentitysubcategories", Array.Empty<string>()).ToList();

            ReplaceInRadiation = element.GetAttributeIdentifier(nameof(ReplaceInRadiation), Identifier.Empty);

            forceOutpostGenerationParamsIdentifier = element.GetAttributeIdentifier("forceoutpostgenerationparams", Identifier.Empty);

            IgnoreGenericEvents = element.GetAttributeBool(nameof(IgnoreGenericEvents), false);

            string teamStr = element.GetAttributeString("outpostteam", "FriendlyNPC");
            Enum.TryParse(teamStr, out OutpostTeam);

            string[] rawNamePaths = element.GetAttributeStringArray("namefile", new string[] { "Content/Map/locationNames.txt" });
            names = new List<string>();
            foreach (string rawPath in rawNamePaths)
            {
                try
                {
                    var path = ContentPath.FromRaw(element.ContentPackage, rawPath.Trim());
                    names.AddRange(File.ReadAllLines(path.Value).ToList());
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

            string[] commonnessPerZoneStrs = element.GetAttributeStringArray("commonnessperzone", Array.Empty<string>());
            foreach (string commonnessPerZoneStr in commonnessPerZoneStrs)
            {
                string[] splitCommonnessPerZone = commonnessPerZoneStr.Split(':');                
                if (splitCommonnessPerZone.Length != 2 ||
                    !int.TryParse(splitCommonnessPerZone[0].Trim(), out int zoneIndex) ||
                    !float.TryParse(splitCommonnessPerZone[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float zoneCommonness))
                {
                    DebugConsole.ThrowError("Failed to read commonness values for location type \"" + Identifier + "\" - commonness should be given in the format \"zone1index: zone1commonness, zone2index: zone2commonness\"");
                    break;
                }
                CommonnessPerZone[zoneIndex] = zoneCommonness;
            }

            string[] minCountPerZoneStrs = element.GetAttributeStringArray("mincountperzone", Array.Empty<string>());
            foreach (string minCountPerZoneStr in minCountPerZoneStrs)
            {
                string[] splitMinCountPerZone = minCountPerZoneStr.Split(':');
                if (splitMinCountPerZone.Length != 2 ||
                    !int.TryParse(splitMinCountPerZone[0].Trim(), out int zoneIndex) ||
                    !int.TryParse(splitMinCountPerZone[1].Trim(), out int minCount))
                {
                    DebugConsole.ThrowError("Failed to read minimum count values for location type \"" + Identifier + "\" - minimum counts should be given in the format \"zone1index: zone1mincount, zone2index: zone2mincount\"");
                    break;
                }
                MinCountPerZone[zoneIndex] = minCount;
            }

            var hireableJobs = new List<(Identifier, float)>();
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "hireable":
                        Identifier jobIdentifier = subElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                        float jobCommonness = subElement.GetAttributeFloat("commonness", 1.0f);
                        totalHireableWeight += jobCommonness;
                        hireableJobs.Add((jobIdentifier, jobCommonness));
                        break;
                    case "symbol":
                        Sprite = new Sprite(subElement, lazyLoad: true);
                        SpriteColor = subElement.GetAttributeColor("color", Color.White);
                        break;
                    case "radiationsymbol":
                        RadiationSprite = new Sprite(subElement, lazyLoad: true);
                        break;
                    case "changeto":
                        CanChangeTo.Add(new LocationTypeChange(Identifier, subElement, requireChangeMessages: true));
                        break;
                    case "portrait":
                        var portrait = new Sprite(subElement, lazyLoad: true);
                        if (portrait != null)
                        {
                            portraits.Add(portrait);
                        }
                        break;
                    case "store":
                        StoreMaxReputationModifier = subElement.GetAttributeFloat("maxreputationmodifier", StoreMaxReputationModifier);
                        StoreSellPriceModifier = subElement.GetAttributeFloat("sellpricemodifier", StoreSellPriceModifier);
                        DailySpecialPriceModifier = subElement.GetAttributeFloat("dailyspecialpricemodifier", DailySpecialPriceModifier);
                        RequestGoodPriceModifier = subElement.GetAttributeFloat("requestgoodpricemodifier", RequestGoodPriceModifier);
                        StoreInitialBalance = subElement.GetAttributeInt("initialbalance", StoreInitialBalance);
                        StorePriceModifierRange = subElement.GetAttributeInt("pricemodifierrange", StorePriceModifierRange);
                        DailySpecialsCount = subElement.GetAttributeInt("dailyspecialscount", DailySpecialsCount);
                        RequestedGoodsCount = subElement.GetAttributeInt("requestedgoodscount", RequestedGoodsCount);
                        break;
                }
            }
            this.hireableJobs = hireableJobs.ToImmutableArray();
        }

        public JobPrefab GetRandomHireable()
        {
            float randFloat = Rand.Range(0.0f, totalHireableWeight, Rand.RandSync.ServerAndClient);

            foreach ((Identifier jobIdentifier, float commonness) in hireableJobs)
            {
                if (randFloat < commonness) { return JobPrefab.Prefabs[jobIdentifier]; }
                randFloat -= commonness;
            }

            return null;
        }

        public Sprite GetPortrait(int portraitId)
        {
            if (portraits.Count == 0) { return null; }
            return portraits[Math.Abs(portraitId) % portraits.Count];
        }

        public string GetRandomName(Random rand, IEnumerable<Location> existingLocations)
        {
            if (existingLocations != null)
            {
                var unusedNames = names.Where(name => !existingLocations.Any(l => l.BaseName == name)).ToList();
                if (unusedNames.Count > 0)
                {
                    return unusedNames[rand.Next() % unusedNames.Count];
                }
            }
            return names[rand.Next() % names.Count];
        }

        public static LocationType Random(Random rand, int? zone = null, bool requireOutpost = false)
        {
            Debug.Assert(Prefabs.Any(), "LocationType.list.Count == 0, you probably need to initialize LocationTypes");

            LocationType[] allowedLocationTypes =
                Prefabs.Where(lt => (!zone.HasValue || lt.CommonnessPerZone.ContainsKey(zone.Value)) && (!requireOutpost || lt.HasOutpost))
                    .OrderBy(p => p.UintIdentifier).ToArray();

            if (allowedLocationTypes.Length == 0)
            {
                DebugConsole.ThrowError("Could not generate a random location type - no location types for the zone " + zone + " found!");
            }

            if (zone.HasValue)
            {
                return ToolBox.SelectWeightedRandom(
                    allowedLocationTypes, 
                    allowedLocationTypes.Select(a => a.CommonnessPerZone[zone.Value]).ToArray(),
                    rand);
            }
            else
            {
                return allowedLocationTypes[rand.Next() % allowedLocationTypes.Length];
            }
        }

        public OutpostGenerationParams GetForcedOutpostGenerationParams()
        {
            if (OutpostGenerationParams.OutpostParams.TryGet(forceOutpostGenerationParamsIdentifier, out var parameters))
            {
                return parameters;
            }
            return null;
        }

        public override void Dispose() { }
    }
}
