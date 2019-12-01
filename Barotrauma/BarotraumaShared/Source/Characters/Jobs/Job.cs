using Barotrauma.Items.Components;
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
        
        public List<Skill> Skills
        {
            get { return skills.Values.ToList(); }
        }

        public int Variant;

        public Job(JobPrefab jobPrefab, int variant = 0)
        {
            prefab = jobPrefab;
            Variant = variant;

            skills = new Dictionary<string, Skill>();
            foreach (SkillPrefab skillPrefab in prefab.Skills)
            {
                skills.Add(skillPrefab.Identifier, new Skill(skillPrefab));
            }
        }

        public Job(XElement element)
        {
            string identifier = element.GetAttributeString("identifier", "").ToLowerInvariant();
            JobPrefab p = null;
            if (!JobPrefab.Prefabs.ContainsKey(identifier))
            {
                DebugConsole.ThrowError($"Could not find the job {identifier}. Giving the character a random job.");
                p = JobPrefab.Random();
            }
            else
            {
                p = JobPrefab.Prefabs[identifier];
            }
            prefab = p;
            skills = new Dictionary<string, Skill>();
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "skill") { continue; }
                string skillIdentifier = subElement.GetAttributeString("identifier", "");
                if (string.IsNullOrEmpty(skillIdentifier)) { continue; }
                skills.Add(
                    skillIdentifier,
                    new Skill(skillIdentifier, subElement.GetAttributeFloat("level", 0)));
            }
        }
        
        public static Job Random(Rand.RandSync randSync = Rand.RandSync.Unsynced) => new Job(JobPrefab.Random(randSync));

        public float GetSkillLevel(string skillIdentifier)
        {
            skills.TryGetValue(skillIdentifier, out Skill skill);

            return (skill == null) ? 0.0f : skill.Level;
        }

        public void IncreaseSkillLevel(string skillIdentifier, float increase)
        {
            if (skills.TryGetValue(skillIdentifier, out Skill skill))
            {
                skill.Level += increase;
            }
            else
            {
                skills.Add(
                    skillIdentifier,
                    new Skill(skillIdentifier, increase));
            }
        }

        public void GiveJobItems(Character character, WayPoint spawnPoint = null)
        {
            if (SpawnItems == null) return;

            foreach (XElement itemElement in SpawnItems.Elements())
            {
                InitializeJobItem(character, itemElement, spawnPoint);
            }            
        }

        private void InitializeJobItem(Character character, XElement itemElement, WayPoint spawnPoint = null, Item parentItem = null)
        {
            ItemPrefab itemPrefab;
            if (itemElement.Attribute("name") != null)
            {
                string itemName = itemElement.Attribute("name").Value;
                DebugConsole.ThrowError("Error in Job config (" + Name + ") - use item identifiers instead of names to configure the items.");
                itemPrefab = MapEntityPrefab.Find(itemName) as ItemPrefab;
                if (itemPrefab == null)
                {
                    DebugConsole.ThrowError("Tried to spawn \"" + Name + "\" with the item \"" + itemName + "\". Matching item prefab not found.");
                    return;
                }
            }
            else
            {
                string itemIdentifier = itemElement.GetAttributeString("identifier", "");
                itemPrefab = MapEntityPrefab.Find(null, itemIdentifier) as ItemPrefab;
                if (itemPrefab == null)
                {
                    DebugConsole.ThrowError("Tried to spawn \"" + Name + "\" with the item \"" + itemIdentifier + "\". Matching item prefab not found.");
                    return;
                }
            }

            Item item = new Item(itemPrefab, character.Position, null);

#if SERVER
            if (GameMain.Server != null && Entity.Spawner != null)
            {
                if (GameMain.Server.EntityEventManager.UniqueEvents.Any(ev => ev.Entity == item))
                {
                    string errorMsg = $"Error while spawning job items. Item {item.Name} created network events before the spawn event had been created.";
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("Job.InitializeJobItem:EventsBeforeSpawning", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    GameMain.Server.EntityEventManager.UniqueEvents.RemoveAll(ev => ev.Entity == item);
                    GameMain.Server.EntityEventManager.Events.RemoveAll(ev => ev.Entity == item);
                }

                Entity.Spawner.CreateNetworkEvent(item, false);
            }
#endif

            if (itemElement.GetAttributeBool("equip", false))
            {
                List<InvSlotType> allowedSlots = new List<InvSlotType>(item.AllowedSlots);
                allowedSlots.Remove(InvSlotType.Any);

                character.Inventory.TryPutItem(item, null, allowedSlots);
            }
            else
            {
                character.Inventory.TryPutItem(item, null, item.AllowedSlots);
            }

            Wearable wearable = ((List<ItemComponent>)item.Components)?.Find(c => c is Wearable) as Wearable;
            if (wearable != null)
            {
                if (Variant > 0 && Variant <= wearable.Variants)
                {
                    wearable.Variant = Variant;
                }
                else
                {
                    wearable.Variant = wearable.Variant; //force server event
                    if (wearable.Variants > 0 && Variant == 0)
                    {
                        //set variant to the same as the wearable to get the rest of the character's gear
                        //to use the same variant (if possible)
                        Variant = wearable.Variant;
                    }
                }
            }

            if (item.Prefab.Identifier == "idcard" && spawnPoint != null)
            {
                foreach (string s in spawnPoint.IdCardTags)
                {
                    item.AddTag(s);
                }
                item.AddTag("name:" + character.Name);
                item.AddTag("job:" + Name);
                if (!string.IsNullOrWhiteSpace(spawnPoint.IdCardDesc))
                    item.Description = spawnPoint.IdCardDesc;
            }

            foreach (WifiComponent wifiComponent in item.GetComponents<WifiComponent>())
            {
                wifiComponent.TeamID = character.TeamID;
            }
            
            if (parentItem != null) parentItem.Combine(item, user: null);

            foreach (XElement childItemElement in itemElement.Elements())
            {
                InitializeJobItem(character, childItemElement, spawnPoint, item);
            } 
        }

        public XElement Save(XElement parentElement)
        {
            XElement jobElement = new XElement("job");

            jobElement.Add(new XAttribute("name", Name));
            jobElement.Add(new XAttribute("identifier", prefab.Identifier));

            foreach (KeyValuePair<string, Skill> skill in skills)
            {
                jobElement.Add(new XElement("skill", new XAttribute("identifier", skill.Value.Identifier), new XAttribute("level", skill.Value.Level)));
            }
            
            parentElement.Add(jobElement);
            return jobElement;
        }
    }
}
