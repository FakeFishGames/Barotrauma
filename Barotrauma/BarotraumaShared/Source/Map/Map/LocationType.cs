using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class LocationTypeChange
    {
        public readonly string ChangeTo;
        public readonly float Probability;
        public readonly int RequiredDuration;

        public LocationTypeChange(XElement element)
        {
            ChangeTo = element.GetAttributeString("type", "");
            Probability = element.GetAttributeFloat("probability", 1.0f);
            RequiredDuration = element.GetAttributeInt("requiredduration", 0);
        }
    }

    class LocationType
    {
        public static readonly List<LocationType> List = new List<LocationType>();
        
        private int commonness;

        private List<string> nameFormats;

        private Sprite symbolSprite;

        private Sprite backGround;

        //<name, commonness>
        private List<Tuple<JobPrefab, float>> hireableJobs;
        private float totalHireableWeight;

        //placeholder
        public readonly Color HaloColor;

        public List<int> AllowedZones = new List<int>();

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

        public Sprite Background
        {
            get { return backGround; }
        }

        private LocationType(XElement element)
        {
            Name = element.Name.ToString();
            DisplayName = element.GetAttributeString("name", "Name");

            commonness = element.GetAttributeInt("commonness", 1);

            nameFormats = new List<string>();
            foreach (XAttribute nameFormat in element.Element("nameformats").Attributes())
            {
                nameFormats.Add(nameFormat.Value);
            }

            HaloColor = element.GetAttributeColor("halo", Color.Transparent);

            string allowedZonesStr = element.GetAttributeString("allowedzones", "1,2,3,4,5,6,7,8,9");
            string[] zoneIndices = allowedZonesStr.Split(',');
            for (int i = 0; i < zoneIndices.Length; i++)
            {
                int zoneIndex = -1;
                if (!int.TryParse(zoneIndices[i].Trim(), out zoneIndex))
                {
                    DebugConsole.ThrowError("Error in locationtype config \"" + Name + "\" - \"" + zoneIndices[i] + "\" is not a valid zone index.");
                    continue;
                }
                AllowedZones.Add(zoneIndex);
            }

            hireableJobs = new List<Tuple<JobPrefab, float>>();
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "hireable":
                        string jobName = subElement.GetAttributeString("name", "");
                        JobPrefab jobPrefab = JobPrefab.List.Find(jp => jp.Name.ToLowerInvariant() == jobName.ToLowerInvariant());
                        if (jobPrefab == null)
                        {
                            DebugConsole.ThrowError("Invalid job name (" + jobName + ") in location type " + Name);
                            continue;
                        }
                        float jobCommonness = subElement.GetAttributeFloat("commonness", 1.0f);
                        totalHireableWeight += jobCommonness;
                        Tuple<JobPrefab, float> hireableJob = new Tuple<JobPrefab, float>(jobPrefab, jobCommonness);
                        hireableJobs.Add(hireableJob);
                        break;
                    case "symbol":
                        symbolSprite = new Sprite(subElement);
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

        public static LocationType Random(string seed = "", int? zone = null)
        {
            Debug.Assert(List.Count > 0, "LocationType.list.Count == 0, you probably need to initialize LocationTypes");

            if (!string.IsNullOrWhiteSpace(seed))
            {
                Rand.SetSyncedSeed(ToolBox.StringToInt(seed));
            }

            List<LocationType> allowedLocationTypes = zone.HasValue ? List.FindAll(lt => lt.AllowedZones.Contains(zone.Value)) : List;

            if (allowedLocationTypes.Count == 0)
            {
                DebugConsole.ThrowError("Could not generate a random location type - no location types for the zone " + zone + " found!");
            }

            int randInt = Rand.Int(allowedLocationTypes.Sum(lt => lt.commonness), Rand.RandSync.Server);
            foreach (LocationType type in allowedLocationTypes)
            {
                if (randInt < type.commonness) return type;
                randInt -= type.commonness;
            }

            return null;
        }

        public static void Init()
        {
            var locationTypeFiles = GameMain.SelectedPackage.GetFilesOfType(ContentType.LocationTypes);

            foreach (string file in locationTypeFiles)
            {
                XDocument doc = XMLExtensions.TryLoadXml(file);

                if (doc == null)
                {
                    return;
                }

                foreach (XElement element in doc.Root.Elements())
                {
                    LocationType locationType = new LocationType(element);
                    List.Add(locationType);
                }
            }

        }
    }
}
