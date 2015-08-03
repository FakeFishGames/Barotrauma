using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Subsurface
{
    class Skill
    {
        string name;
        int level;

        public string Name
        {
            get { return name; }
        }

        public int Level
        {
            get { return level; }
        }

        public Skill(string name, int level)
        {
            this.name = name;
            this.level = level;
        }
    }

    class Job
    {

        private JobPrefab prefab;

        private Dictionary<string, Skill> skills;

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

        public List<bool> EquipSpawnItem
        {
            get { return prefab.EquipItem; }
        }

        public List<Skill> Skills
        {
            get { return skills.Values.ToList(); }
        }

        //public List<float> SkillLevels
        //{
        //    get { return skills.Values.ToList(); }
        //}

        public Job(JobPrefab jobPrefab)
        {
            prefab = jobPrefab;

            skills = new Dictionary<string, Skill>();
            foreach (KeyValuePair<string, Vector2> skill in prefab.Skills)
            {
                skills.Add(
                    skill.Key, 
                    new Skill( skill.Key, (int)Rand.Range(skill.Value.X, skill.Value.Y, false)));
            }
        }

        public Job(XElement element)
        {
            string name = ToolBox.GetAttributeString(element, "name", "").ToLower();
            prefab = JobPrefab.List.Find(jp => jp.Name.ToLower() == name);

            skills = new Dictionary<string, Skill>();
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLower() != "skill") continue;
                string skillName = ToolBox.GetAttributeString(subElement, "name", "");
                if (string.IsNullOrEmpty(name)) continue;
                skills.Add(
                    skillName,
                    new Skill(skillName, ToolBox.GetAttributeInt(subElement, "level", 0)));
            }
        }
        
        public static Job Random()
        {
            JobPrefab prefab = JobPrefab.List[Rand.Int(JobPrefab.List.Count-1, false)];

            return new Job(prefab);
        }

        public int GetSkillLevel(string skillName)
        {
            Skill skill = null;
            skills.TryGetValue(skillName, out skill);

            return (skill==null) ? 0 : skill.Level;
        }

        public virtual XElement Save(XElement parentElement)
        {
            XElement jobElement = new XElement("job");

            jobElement.Add(new XAttribute("name", Name));

            foreach (KeyValuePair<string, Skill> skill in skills)
            {
                jobElement.Add(new XElement("skill", new XAttribute("name", skill.Value.Name), new XAttribute("level", skill.Value.Level)));
            }
            
            parentElement.Add(jobElement);
            return jobElement;
        }
    }
}
