using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Subsurface
{
    class Job
    {

        private JobPrefab prefab;

        private Dictionary<string, float> skills;

        public string Name
        {
            get { return prefab.Name; }
        }

        public string Description
        {
            get { return prefab.Description; }
        }

        public JobPrefab Prefab
        {
            get { return prefab; }
        }

        public List<string> SpawnItemNames
        {
            get { return prefab.ItemNames; }
        }


        public Job(JobPrefab jobPrefab)
        {
            prefab = jobPrefab;

            skills = new Dictionary<string, float>();
            foreach (KeyValuePair<string, Vector2> skill in prefab.Skills)
            {
                skills.Add(skill.Key, Rand.Range(skill.Value.X, skill.Value.Y, false));
            }
        }

        public Job(XElement element)
        {
            string name = ToolBox.GetAttributeString(element, "name", "").ToLower();
            prefab = JobPrefab.List.Find(jp => jp.Name.ToLower() == name);

            foreach (XElement subElement in element.Elements())
            {
                skills.Add(subElement.Name.ToString(), ToolBox.GetAttributeFloat(subElement, "level", 0.0f));
            }
        }

        public static Job Random()
        {
            JobPrefab prefab = JobPrefab.List[Rand.Int(JobPrefab.List.Count-1, false)];

            return new Job(prefab);
        }

        public float GetSkill(string skillName)
        {
            float skillLevel = 0.0f;
            skills.TryGetValue(skillName.ToLower(), out skillLevel);

            return skillLevel;
        }

        public virtual XElement Save(XElement parentElement)
        {
            XElement jobElement = new XElement("job");

            jobElement.Add(new XAttribute("name", Name));

            foreach (KeyValuePair<string, float> skill in skills)
            {
                jobElement.Add(new XElement(skill.Key, new XAttribute("level", skill.Value)));
            }
            
            parentElement.Add(jobElement);
            return jobElement;
        }
    }
}
