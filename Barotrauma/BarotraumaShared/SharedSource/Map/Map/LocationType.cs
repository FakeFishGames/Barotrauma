using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class LocationType
    {
        public static readonly List<LocationType> List = new List<LocationType>();
        private readonly List<string> names;
        private readonly List<Sprite> portraits = new List<Sprite>();

        //<name, commonness>
        private readonly List<Tuple<JobPrefab, float>> hireableJobs;
        private readonly float totalHireableWeight;
        
        public Dictionary<int, float> CommonnessPerZone = new Dictionary<int, float>();

        public readonly string Identifier;
        public readonly string Name;

        public readonly float BeaconStationChance;

        public readonly CharacterTeamType OutpostTeam;

        public readonly List<LocationTypeChange> CanChangeTo = new List<LocationTypeChange>();

        public readonly List<string> MissionIdentifiers = new List<string>();
        public readonly List<string> MissionTags = new List<string>();

        public readonly List<string> HideEntitySubcategories = new List<string>();

        public bool IsEnterable { get; private set; }

        public bool UseInMainMenu
        {
            get;
            private set;
        }

        public List<string> NameFormats { get; private set; }

        public bool HasHireableCharacters
        {
            get { return hireableJobs.Any(); }
        }

        public bool HasOutpost
        {
            get;
            private set;
        }
        
        public string ReplaceInRadiation { get; }

        public Sprite Sprite { get; private set; }
        public Sprite RadiationSprite { get; }

        public Color SpriteColor
        {
            get;
            private set;
        }

        public override string ToString()
        {
            return $"LocationType (" + Identifier + ")";
        }

        private LocationType(XElement element)
        {
            Identifier = element.GetAttributeString("identifier", element.Name.ToString());
            Name = TextManager.Get("LocationName." + Identifier, fallBackTag: "unknown");

            BeaconStationChance = element.GetAttributeFloat("beaconstationchance", 0.0f);

            NameFormats = TextManager.GetAll("LocationNameFormat." + Identifier);
            UseInMainMenu = element.GetAttributeBool("useinmainmenu", false);
            HasOutpost = element.GetAttributeBool("hasoutpost", true);
            IsEnterable = element.GetAttributeBool("isenterable", HasOutpost);

            MissionIdentifiers = element.GetAttributeStringArray("missionidentifiers", new string[0]).ToList();
            MissionTags = element.GetAttributeStringArray("missiontags", new string[0]).ToList();

            HideEntitySubcategories = element.GetAttributeStringArray("hideentitysubcategories", new string[0]).ToList();

            ReplaceInRadiation = element.GetAttributeString(nameof(ReplaceInRadiation).ToLower(), "");

            string teamStr = element.GetAttributeString("outpostteam", "FriendlyNPC");
            Enum.TryParse(teamStr, out OutpostTeam);

            string nameFile = element.GetAttributeString("namefile", "Content/Map/locationNames.txt");
            try
            {
                names = File.ReadAllLines(nameFile).ToList();
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to read name file for location type \"" + Identifier + "\"!", e);
                names = new List<string>() { "Name file not found" };
            }

            string[] commonnessPerZoneStrs = element.GetAttributeStringArray("commonnessperzone", new string[] { "" });
            foreach (string commonnessPerZoneStr in commonnessPerZoneStrs)
            {
                string[] splitCommonnessPerZone = commonnessPerZoneStr.Split(':');                
                if (splitCommonnessPerZone.Length != 2 ||
                    !int.TryParse(splitCommonnessPerZone[0].Trim(), out int zoneIndex) ||
                    !float.TryParse(splitCommonnessPerZone[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float zoneCommonness))
                {
                    DebugConsole.ThrowError("Failed to read commonness values for location type \"" + Identifier + "\" - commonness should be given in the format \"zone0index: zone0commonness, zone1index: zone1commonness\"");
                    break;
                }
                CommonnessPerZone[zoneIndex] = zoneCommonness;
            }

            hireableJobs = new List<Tuple<JobPrefab, float>>();
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "hireable":
                        string jobIdentifier = subElement.GetAttributeString("identifier", "");
                        JobPrefab jobPrefab = null;
                        if (jobIdentifier == "")
                        {
                            DebugConsole.ThrowError("Error in location type \""+ Identifier + "\" - hireable jobs should be configured using identifiers instead of names.");
                        }
                        else
                        {
                            jobPrefab = JobPrefab.Get(jobIdentifier.ToLowerInvariant());
                        }
                        if (jobPrefab == null)
                        {
                            DebugConsole.ThrowError("Error in  in location type " + Identifier + " - could not find a job with the identifier \"" + jobIdentifier + "\".");
                            continue;
                        }
                        float jobCommonness = subElement.GetAttributeFloat("commonness", 1.0f);
                        totalHireableWeight += jobCommonness;
                        Tuple<JobPrefab, float> hireableJob = new Tuple<JobPrefab, float>(jobPrefab, jobCommonness);
                        hireableJobs.Add(hireableJob);
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
                }
            }
        }

        public JobPrefab GetRandomHireable()
        {
            float randFloat = Rand.Range(0.0f, totalHireableWeight, Rand.RandSync.Server);

            foreach (Tuple<JobPrefab, float> hireable in hireableJobs)
            {
                if (randFloat < hireable.Item2) return hireable.Item1;
                randFloat -= hireable.Item2;
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
            Debug.Assert(List.Count > 0, "LocationType.list.Count == 0, you probably need to initialize LocationTypes");

            List<LocationType> allowedLocationTypes = 
                List.FindAll(lt => (!zone.HasValue || lt.CommonnessPerZone.ContainsKey(zone.Value)) && (!requireOutpost || lt.HasOutpost));

            if (allowedLocationTypes.Count == 0)
            {
                DebugConsole.ThrowError("Could not generate a random location type - no location types for the zone " + zone + " found!");
            }

            if (zone.HasValue)
            {
                return ToolBox.SelectWeightedRandom(
                    allowedLocationTypes, 
                    allowedLocationTypes.Select(a => a.CommonnessPerZone[zone.Value]).ToList(),
                    rand);
            }
            else
            {
                return allowedLocationTypes[rand.Next() % allowedLocationTypes.Count];
            }
        }

        public static void Init()
        {
            List.Clear();
            var locationTypeFiles = GameMain.Instance.GetFilesOfType(ContentType.LocationTypes);
            if (!locationTypeFiles.Any())
            {
                DebugConsole.ThrowError("No location types configured in any of the selected content packages. Attempting to load from the vanilla content package...");
                locationTypeFiles = ContentPackage.GetFilesOfType(GameMain.VanillaContent.ToEnumerable(), ContentType.LocationTypes);
                if (!locationTypeFiles.Any())
                {
                    throw new Exception("No location types configured in any of the selected content packages. Please try uninstalling mods or reinstalling the game.");
                }
            }

            foreach (ContentFile file in locationTypeFiles)
            {
                XDocument doc = XMLExtensions.TryLoadXml(file.Path);
                if (doc == null) { continue; }
                var mainElement = doc.Root;
                if (doc.Root.IsOverride())
                {
                    mainElement = doc.Root.FirstElement();
                    DebugConsole.NewMessage($"Overriding all location types with '{file.Path}'", Color.Yellow);
                    List.Clear();
                }
                else if (List.Any())
                {
                    DebugConsole.NewMessage($"Loading additional location types from file '{file.Path}'");
                }
                foreach (XElement sourceElement in mainElement.Elements())
                {
                    var element = sourceElement;
                    bool allowOverriding = false;
                    if (sourceElement.IsOverride())
                    {
                        element = sourceElement.FirstElement();
                        allowOverriding = true;
                    }
                    string identifier = element.GetAttributeString("identifier", null);
                    if (string.IsNullOrWhiteSpace(identifier))
                    {
                        DebugConsole.ThrowError($"Error in '{file.Path}': No identifier defined for {element.Name.ToString()}");
                        continue;
                    }
                    var duplicate = List.FirstOrDefault(l => l.Identifier == identifier);
                    if (duplicate != null)
                    {
                        if (allowOverriding)
                        {
                            List.Remove(duplicate);
                            DebugConsole.NewMessage($"Overriding the location type with the identifier '{identifier}' with '{file.Path}'", Color.Yellow);
                        }
                        else
                        {
                            DebugConsole.ThrowError($"Error in '{file.Path}': Duplicate identifier defined with the identifier '{identifier}'");
                            continue;
                        }
                    }
                    LocationType locationType = new LocationType(element);
                    List.Add(locationType);
                }
            }

            foreach (EventSet eventSet in EventSet.List)
            {
                eventSet.CheckLocationTypeErrors();
            }
        }
    }
}
