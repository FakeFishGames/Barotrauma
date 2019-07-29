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

        private readonly List<Sprite> portraits = new List<Sprite>();

        //<name, commonness>
        private List<Tuple<JobPrefab, float>> hireableJobs;
        private float totalHireableWeight;
        
        public Dictionary<int, float> CommonnessPerZone = new Dictionary<int, float>();

        public readonly string Identifier;
        public readonly string Name;

        public readonly List<LocationTypeChange> CanChangeTo = new List<LocationTypeChange>();

        public bool UseInMainMenu
        {
            get;
            private set;
        }
        
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

        public override string ToString()
        {
            return $"LocationType (" + Identifier + ")";
        }

        private LocationType(XElement element)
        {
            Identifier = element.GetAttributeString("identifier", element.Name.ToString());
            Name = TextManager.Get("LocationName." + Identifier);
            nameFormats = TextManager.GetAll("LocationNameFormat." + Identifier);
            UseInMainMenu = element.GetAttributeBool("useinmainmenu", false);

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
                            jobPrefab = JobPrefab.List[jobIdentifier.ToLowerInvariant()];
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
                        symbolSprite = new Sprite(subElement);
                        SpriteColor = subElement.GetAttributeColor("color", Color.White);
                        break;
                    case "changeto":
                        CanChangeTo.Add(new LocationTypeChange(Identifier, subElement));
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

        public string GetRandomName(Random rand)
        {
            return names[rand.Next() % names.Count];
        }

        public static LocationType Random(Random rand, int? zone = null)
        {
            Debug.Assert(List.Count > 0, "LocationType.list.Count == 0, you probably need to initialize LocationTypes");

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
                    rand);
            }
            else
            {
                return allowedLocationTypes[rand.Next() % allowedLocationTypes.Count];
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
