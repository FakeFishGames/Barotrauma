using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class LocationType
    {
        private static List<LocationType> list = new List<LocationType>();
        //sum of the commonness-values of each location type
        private static int totalWeight;

        private string name;

        private int commonness;

        private List<string> nameFormats;

        private Sprite symbolSprite;

        private Sprite backGround;

        //<name, commonness>
        private List<Tuple<JobPrefab, float>> hireableJobs;
        private float totalHireableWeight;
        
        public string Name
        {
            get { return name; }
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

        public Sprite Background
        {
            get { return backGround; }
        }

        private LocationType(XElement element)
        {
            name = element.Name.ToString();

            commonness = ToolBox.GetAttributeInt(element, "commonness", 1);
            totalWeight += commonness;

            nameFormats = new List<string>();
            foreach (XAttribute nameFormat in element.Element("nameformats").Attributes())
            {
                nameFormats.Add(nameFormat.Value);
            }

            hireableJobs = new List<Tuple<JobPrefab, float>>();
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "hireable") continue;

                string jobName = ToolBox.GetAttributeString(subElement, "name", "");

                JobPrefab jobPrefab = JobPrefab.List.Find(jp => jp.Name.ToLowerInvariant() == jobName.ToLowerInvariant());
                if (jobPrefab==null)
                {
                    DebugConsole.ThrowError("Invalid job name ("+jobName+") in location type "+name);
                }

                float jobCommonness = ToolBox.GetAttributeFloat(subElement, "commonness", 1.0f);
                totalHireableWeight += jobCommonness;

                Tuple<JobPrefab, float> hireableJob = new Tuple<JobPrefab, float>(jobPrefab, jobCommonness);

                hireableJobs.Add(hireableJob);
            }

            string spritePath = ToolBox.GetAttributeString(element, "symbol", "Content/Map/beaconSymbol.png");
            symbolSprite = new Sprite(spritePath, new Vector2(0.5f, 0.5f));

            string backgroundPath = ToolBox.GetAttributeString(element, "background", "");
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

        public static LocationType Random(string seed = "")
        {
            Debug.Assert(list.Count > 0, "LocationType.list.Count == 0, you probably need to initialize LocationTypes");

            if (!string.IsNullOrWhiteSpace(seed))
            {
                Rand.SetSyncedSeed(ToolBox.StringToInt(seed));
            }

            int randInt = Rand.Int(totalWeight, false);

            foreach (LocationType type in list)
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
                XDocument doc = ToolBox.TryLoadXml(file);

                if (doc==null)
                {
                    return;
                }

                foreach (XElement element in doc.Root.Elements())
                {
                    LocationType locationType = new LocationType(element);
                    list.Add(locationType);
                }
            }

        }
    }
}
