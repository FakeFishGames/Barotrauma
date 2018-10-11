using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class LocationType
    {
        public static readonly List<LocationType> List = new List<LocationType>();
        
        private List<string> nameFormats;
        private List<string> names;

        private Sprite symbolSprite;

        private Sprite backGround;

        //<name, commonness>
        private List<Tuple<JobPrefab, float>> hireableJobs;
        private float totalHireableWeight;
        
        public Dictionary<int, float> CommonnessPerZone = new Dictionary<int, float>();

        public readonly string Name;

        public readonly string DisplayName;

        public readonly List<LocationTypeChange> CanChangeTo = new List<LocationTypeChange>();
        
        public List<string> NameFormats
        {
            get { return nameFormats; }
        }

        public bool HasHireableCharacters
        {
            get { return hireableJobs.Any(); }
        }

        public Sprite Sprite
        {
            get { return symbolSprite; }
        }

        public Color SpriteColor
        {
            get;
            private set;
        }

        public Sprite Background
        {
            get { return backGround; }
        }

        private LocationType(XElement element)
        {
            Name = element.Name.ToString();
            DisplayName = element.GetAttributeString("name", "Name");
            
            nameFormats = new List<string>();
            foreach (XAttribute nameFormat in element.Element("nameformats").Attributes())
            {
                nameFormats.Add(nameFormat.Value);
            }

            string nameFile = element.GetAttributeString("namefile", "Content/Map/locationNames.txt");
            try
            {
                names = File.ReadAllLines(nameFile).ToList();
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Failed to read name file for location type \""+Name+"\"!", e);
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
                    DebugConsole.ThrowError("Failed to read commonness values for location type \"" + Name + "\" - commonness should be given in the format \"zone0index: zone0commonness, zone1index: zone1commonness\"");
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
                        JobPrefab jobPrefab = JobPrefab.List.Find(jp => jp.Identifier.ToLowerInvariant() == jobIdentifier.ToLowerInvariant());
                        if (jobPrefab == null)
                        {
                            DebugConsole.ThrowError("Invalid job name (" + jobIdentifier + ") in location type " + Name);
                            continue;
                        }
                        float jobCommonness = subElement.GetAttributeFloat("commonness", 1.0f);
                        totalHireableWeight += jobCommonness;
                        Tuple<JobPrefab, float> hireableJob = new Tuple<JobPrefab, float>(jobPrefab, jobCommonness);
                        hireableJobs.Add(hireableJob);
                        break;
                    case "symbol":
                        symbolSprite = new Sprite(subElement);
                        SpriteColor = subElement.GetAttributeColor("color", Color.White);
                        break;
                    case "changeto":
                        CanChangeTo.Add(new LocationTypeChange(subElement));
                        break;
                }
            }

            string backgroundPath = element.GetAttributeString("background", "");
            backGround = new Sprite(backgroundPath, Vector2.Zero);
        }

        public JobPrefab GetRandomHireable()
        {
            float randFloat = Rand.Range(0.0f, totalHireableWeight);

            foreach (Tuple<JobPrefab, float> hireable in hireableJobs)
            {
                if (randFloat < hireable.Item2) return hireable.Item1;
                randFloat -= hireable.Item2;
            }

            return null;
        }

        public string GetRandomName()
        {
            return names[Rand.Int(names.Count, Rand.RandSync.Server)];
        }

        public static LocationType Random(string seed = "", int? zone = null)
        {
            Debug.Assert(List.Count > 0, "LocationType.list.Count == 0, you probably need to initialize LocationTypes");

            if (!string.IsNullOrWhiteSpace(seed))
            {
                Rand.SetSyncedSeed(ToolBox.StringToInt(seed));
            }

            List<LocationType> allowedLocationTypes = zone.HasValue ? List.FindAll(lt => lt.CommonnessPerZone.ContainsKey(zone.Value)) : List;

            if (allowedLocationTypes.Count == 0)
            {
                DebugConsole.ThrowError("Could not generate a random location type - no location types for the zone " + zone + " found!");
            }

            if (zone.HasValue)
            {
                return ToolBox.SelectWeightedRandom(
                    allowedLocationTypes, 
                    allowedLocationTypes.Select(a => a.CommonnessPerZone[zone.Value]).ToList(), 
                    Rand.RandSync.Server);
            }
            else
            {
                return allowedLocationTypes[Rand.Int(allowedLocationTypes.Count, Rand.RandSync.Server)];
            }
        }

        public static void Init()
        {
            var locationTypeFiles = GameMain.Instance.GetFilesOfType(ContentType.LocationTypes);

            foreach (string file in locationTypeFiles)
            {
                XDocument doc = XMLExtensions.TryLoadXml(file);
                if (doc?.Root == null) continue;                

                foreach (XElement element in doc.Root.Elements())
                {
                    LocationType locationType = new LocationType(element);
                    List.Add(locationType);
                }
            }
        }
    }
}
