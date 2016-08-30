using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class Job
    {

        private readonly JobPrefab prefab;

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

        public XElement SpawnItems
        {
            get { return prefab.Items; }
        }

        //public List<bool> EquipSpawnItem
        //{
        //    get { return prefab.EquipItem; }
        //}

        public List<Skill> Skills
        {
            get { return skills.Values.ToList(); }
        }

        public Job(JobPrefab jobPrefab)
        {
            prefab = jobPrefab;

            skills = new Dictionary<string, Skill>();
            foreach (SkillPrefab skillPrefab in prefab.Skills)
            {
                skills.Add(skillPrefab.Name, new Skill(skillPrefab));
            }
        }

        public Job(XElement element)
        {
            string name = ToolBox.GetAttributeString(element, "name", "").ToLowerInvariant();
            prefab = JobPrefab.List.Find(jp => jp.Name.ToLowerInvariant() == name);

            skills = new Dictionary<string, Skill>();
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "skill") continue;
                string skillName = ToolBox.GetAttributeString(subElement, "name", "");
                if (string.IsNullOrEmpty(name)) continue;
                skills.Add(
                    skillName,
                    new Skill(skillName, ToolBox.GetAttributeInt(subElement, "level", 0)));
            }
        }
        
        public static Job Random()
        {
            JobPrefab prefab = JobPrefab.List[Rand.Int(JobPrefab.List.Count - 1, false)];

            return new Job(prefab);
        }

        public int GetSkillLevel(string skillName)
        {
            Skill skill = null;
            skills.TryGetValue(skillName, out skill);

            return (skill==null) ? 0 : skill.Level;
        }

        public void GiveJobItems(Character character, WayPoint spawnPoint)
        {
            if (SpawnItems == null) return;

            foreach (XElement itemElement in SpawnItems.Elements())
            {
                InitializeJobItem(character, spawnPoint, itemElement);
            }            
        }

        private void InitializeJobItem(Character character, WayPoint spawnPoint, XElement itemElement, Item parentItem = null)
        {
            string itemName = ToolBox.GetAttributeString(itemElement, "name", "");
              
            ItemPrefab itemPrefab = ItemPrefab.list.Find(ip => ip.Name == itemName) as ItemPrefab;
            if (itemPrefab == null)
            {
                DebugConsole.ThrowError("Tried to spawn ''" + Name + "'' with the item ''" + itemName + "''. Matching item prefab not found.");
                return;
            }

            Item item = new Item(itemPrefab, character.Position, null);

            if (ToolBox.GetAttributeBool(itemElement, "equip", false))
            {
                List<InvSlotType> allowedSlots = new List<InvSlotType>(item.AllowedSlots);
                allowedSlots.Remove(InvSlotType.Any);

                character.Inventory.TryPutItem(item, allowedSlots);
            }
            else
            {
                character.Inventory.TryPutItem(item, item.AllowedSlots);
            }

            if (item.Prefab.Name == "ID Card" && spawnPoint != null)
            {
                foreach (string s in spawnPoint.IdCardTags)
                {
                    item.AddTag(s);
                }
            }

            character.SpawnItems.Add(item);

            if (parentItem != null) parentItem.Combine(item);

            foreach (XElement childItemElement in itemElement.Elements())
            {
                InitializeJobItem(character, spawnPoint, childItemElement, item);
            } 
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
