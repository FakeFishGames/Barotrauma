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
        
        //names of the items the character spawns with
        public List<string> itemNames;

        public Dictionary<string, Vector2> skills;

        public string Name
        {
            get { return name; }
        }

        public string Description
        {
            get { return description; }
        }

        //public float GetSkill(string skillName)
        //{
        //    float skillLevel = 0.0f;
        //    if (skills.TryGetValue(skillName.ToLower(), out skillLevel))
        //    {
        //        return skillLevel;
        //    }
        //    else
        //    {
        //        DebugConsole.ThrowError("Skill ''"+skillName+" not found!");
        //        return skillLevel;
        //    }
        //}

        public JobPrefab(XElement element)
        {
            name = element.Name.ToString();

            description = ToolBox.GetAttributeString(element, "description", "");

            itemNames = new List<string>();

            skills = new Dictionary<string, Vector2>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString())
                {
                    case "item":
                        string itemName = ToolBox.GetAttributeString(subElement, "name", "");
                        if (!string.IsNullOrEmpty(itemName)) itemNames.Add(itemName);
                        break;
                    case "skills":
                        LoadSkills(subElement);
                        break;
                }
            }
        }

        private void LoadSkills(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                string skillName = subElement.Name.ToString().ToLower();
                if (skills.ContainsKey(skillName)) continue;

                var levelAttribute = subElement.Attribute("level").ToString();
                if (levelAttribute.Contains("'"))
                {
                    skills.Add(skillName, ToolBox.ParseToVector2(levelAttribute, false));
                }
                else
                {
                    float skillLevel = float.Parse(levelAttribute, CultureInfo.InvariantCulture);
                    skills.Add(skillName, new Vector2(skillLevel, skillLevel));
                }

            }
        }


        public static void LoadAll(string filePath)
        {
            List = new List<JobPrefab>();

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
