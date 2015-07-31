using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace Subsurface
{
    class JobPrefab
    {
        public static List<JobPrefab> List;

        string name;
        string description;

        //how many crew members can have the job (only one captain etc)        
        private int maxNumber;

        //how many crew members are REQUIRED to have a job 
        //(i.e. if one captain is required, one captain is chosen even if all the players have set captain to lowest preference)
        private int minNumber;

        //if set to true, a client that has chosen this as their preferred job will get it no matter what
        public bool AllowAlways
        {
            get;
            private set;
        }
        
        //names of the items the character spawns with
        public List<string> ItemNames;

        public Dictionary<string, Vector2> Skills;

        public string Name
        {
            get { return name; }
        }

        public string Description
        {
            get { return description; }
        }

        public int MaxNumber
        {
            get { return maxNumber; }
        }

        public int MinNumber
        {
            get { return minNumber; }
        }

        public JobPrefab(XElement element)
        {
            name = element.Name.ToString();

            description = ToolBox.GetAttributeString(element, "description", "");

            minNumber = ToolBox.GetAttributeInt(element, "minnumber", 0);
            maxNumber = ToolBox.GetAttributeInt(element, "maxnumber", 10);

            AllowAlways = ToolBox.GetAttributeBool(element, "allowalways", false);

            ItemNames = new List<string>();

            Skills = new Dictionary<string, Vector2>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "item":
                        string itemName = ToolBox.GetAttributeString(subElement, "name", "");
                        if (!string.IsNullOrEmpty(itemName)) ItemNames.Add(itemName);
                        break;
                    case "skills":
                        LoadSkills(subElement);
                        break;
                }
            }
        }

        public static JobPrefab Random()
        {
            return List[Rand.Int(List.Count)];
        }

        private void LoadSkills(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                string skillName = ToolBox.GetAttributeString(subElement, "name", "");

                if (string.IsNullOrEmpty(skillName) || Skills.ContainsKey(skillName)) continue;

                var levelString = ToolBox.GetAttributeString(subElement, "level", "");
                if (levelString.Contains(","))
                {
                    Skills.Add(skillName, ToolBox.ParseToVector2(levelString, false));
                }
                else
                {
                    float skillLevel = float.Parse(levelString, CultureInfo.InvariantCulture);
                    Skills.Add(skillName, new Vector2(skillLevel, skillLevel));
                }

            }
        }


        public static void LoadAll(List<string> filePaths)
        {
            List = new List<JobPrefab>();

            foreach (string filePath in filePaths)
            {
                XDocument doc = ToolBox.TryLoadXml(filePath);
                if (doc == null) return;

                foreach (XElement element in doc.Root.Elements())
                {
                    JobPrefab job = new JobPrefab(element);
                    List.Add(job);
                }
            }
        }
    }
}
