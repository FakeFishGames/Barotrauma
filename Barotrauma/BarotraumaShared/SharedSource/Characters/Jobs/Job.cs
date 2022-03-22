using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class Job
    {
        private readonly JobPrefab prefab;

        private readonly Dictionary<string, Skill> skills;

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
        
        public List<Skill> Skills
        {
            get { return skills.Values.ToList(); }
        }

        public int Variant;

        public Skill PrimarySkill { get; }

        public Job(JobPrefab jobPrefab, Rand.RandSync randSync = Rand.RandSync.Unsynced, int variant = 0)
        {
            prefab = jobPrefab;
            Variant = variant;

            skills = new Dictionary<string, Skill>();
            foreach (SkillPrefab skillPrefab in prefab.Skills)
            {
                var skill = new Skill(skillPrefab, randSync);
                skills.Add(skillPrefab.Identifier, skill);
                if (skillPrefab.IsPrimarySkill) { PrimarySkill = skill; }
            }
        }

        public Job(XElement element)
        {
            string identifier = element.GetAttributeString("identifier", "").ToLowerInvariant();
            JobPrefab p;
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
                if (!subElement.Name.ToString().Equals("skill", System.StringComparison.OrdinalIgnoreCase)) { continue; }
                string skillIdentifier = subElement.GetAttributeString("identifier", "");
                if (string.IsNullOrEmpty(skillIdentifier)) { continue; }
                var skill = new Skill(skillIdentifier, subElement.GetAttributeFloat("level", 0));
                skills.Add(skillIdentifier, skill);
                if (skillIdentifier == prefab.PrimarySkill?.Identifier) { PrimarySkill = skill; }
            }
        }

        public static Job Random(Rand.RandSync randSync = Rand.RandSync.Unsynced)
        {
            var prefab = JobPrefab.Random(randSync);
            var variant = Rand.Range(0, prefab.Variants, randSync);
            return new Job(prefab, randSync, variant);
        } 

        public float GetSkillLevel(string skillIdentifier)
        {
            if (string.IsNullOrWhiteSpace(skillIdentifier)) { return 0.0f; }
            skills.TryGetValue(skillIdentifier, out Skill skill);
            return (skill == null) ? 0.0f : skill.Level;
        }

        public void IncreaseSkillLevel(string skillIdentifier, float increase, bool increasePastMax)
        {
            if (skills.TryGetValue(skillIdentifier, out Skill skill))
            {
                skill.IncreaseSkill(increase, increasePastMax);
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
            if (!prefab.ItemSets.TryGetValue(Variant, out var spawnItems)) { return; }

            foreach (XElement itemElement in spawnItems.GetChildElements("Item"))
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
                    GameAnalyticsManager.AddErrorEventOnce("Job.InitializeJobItem:EventsBeforeSpawning", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                    GameMain.Server.EntityEventManager.UniqueEvents.RemoveAll(ev => ev.Entity == item);
                    GameMain.Server.EntityEventManager.Events.RemoveAll(ev => ev.Entity == item);
                }

                Entity.Spawner.CreateNetworkEvent(item, false);
            }
#endif

            if (itemElement.GetAttributeBool("equip", false))
            {
                //if the item is both pickable and wearable, try to wear it instead of picking it up
                List<InvSlotType> allowedSlots =
                   item.GetComponents<Pickable>().Count() > 1 ?
                   new List<InvSlotType>(item.GetComponent<Wearable>()?.AllowedSlots ?? item.GetComponent<Pickable>().AllowedSlots) :
                   new List<InvSlotType>(item.AllowedSlots);
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

            if (item.Prefab.Identifier == "idcard")
            {
                if (spawnPoint != null)
                {
                    foreach (string s in spawnPoint.IdCardTags)
                    {
                        item.AddTag(s);
                        if (!string.IsNullOrWhiteSpace(spawnPoint.IdCardDesc)) { item.Description = spawnPoint.IdCardDesc; }
                    }
                }
                item.AddTag("name:" + character.Name);
                item.AddTag("job:" + Name);

                IdCard idCardComponent = item.GetComponent<IdCard>();
                idCardComponent?.Initialize(character.Info);
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
