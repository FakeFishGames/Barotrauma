using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class HumanPrefab
    {
        [Serialize("notfound", false)]
        public string Identifier { get; protected set; }

        [Serialize("any", false)]
        public string Job { get; protected set; }

        [Serialize(1f, false)]
        public float Commonness { get; protected set; }

        [Serialize(1f, false)]
        public float HealthMultiplier { get; protected set; }

        private readonly HashSet<string> moduleFlags = new HashSet<string>();

        [Serialize("", true, "What outpost module tags does the NPC prefer to spawn in.")]
        public string ModuleFlags
        {
            get => string.Join(",", moduleFlags);
            set
            {
                moduleFlags.Clear();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    string[] splitFlags = value.Split(',');
                    foreach (var f in splitFlags)
                    {
                        moduleFlags.Add(f);
                    }
                }
            }
        }


        private readonly HashSet<string> spawnPointTags = new HashSet<string>();

        [Serialize("", true, "Tag(s) of the spawnpoints the NPC prefers to spawn at.")]
        public string SpawnPointTags
        {
            get => string.Join(",", spawnPointTags);
            set
            {
                spawnPointTags.Clear();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    string[] splitTags = value.Split(',');
                    foreach (var tag in splitTags)
                    {
                        spawnPointTags.Add(tag.ToLowerInvariant());
                    }
                }
            }
        }

        [Serialize("None", false)]
        public CampaignMode.InteractionType CampaignInteractionType { get; protected set; }

        [Serialize("Passive", false)]
        public AIObjectiveIdle.BehaviorType BehaviorType { get; protected set; }

        public List<string> PreferredOutpostModuleTypes { get; protected set; }

        public string OriginalName { get { return Identifier; } }


        public string FilePath { get; protected set; }

        public XElement Element { get; protected set; }
        

        public readonly Dictionary<XElement, float> ItemSets = new Dictionary<XElement, float>();

        public HumanPrefab(XElement element, string filePath)
        {
            FilePath = filePath;
            SerializableProperty.DeserializeProperties(this, element);
            Identifier = Identifier.ToLowerInvariant();
            Job = Job.ToLowerInvariant();
            Element = element;
            element.GetChildElements("itemset").ForEach(e => ItemSets.Add(e, e.GetAttributeFloat("commonness", 1)));
            PreferredOutpostModuleTypes = element.GetAttributeStringArray("preferredoutpostmoduletypes", new string[0], convertToLowerInvariant: true).ToList();
        }

        public IEnumerable<string> GetModuleFlags()
        {
            return moduleFlags;
        }

        public IEnumerable<string> GetSpawnPointTags()
        {
            return spawnPointTags;
        }

        public JobPrefab GetJobPrefab(Rand.RandSync randSync = Rand.RandSync.Unsynced)
        {
            return Job != null && Job != "any" ? JobPrefab.Get(Job) : JobPrefab.Random(randSync);
        }

        public void GiveItems(Character character, Submarine submarine, Rand.RandSync randSync = Rand.RandSync.Unsynced)
        {
            var spawnItems = ToolBox.SelectWeightedRandom(ItemSets.Keys.ToList(), ItemSets.Values.ToList(), randSync);
            foreach (XElement itemElement in spawnItems.GetChildElements("item"))
            {
                InitializeItems(character, itemElement, submarine);
            }
        }

        private void InitializeItems(Character character, XElement itemElement, Submarine submarine, Item parentItem = null)
        {
            ItemPrefab itemPrefab;
            string itemIdentifier = itemElement.GetAttributeString("identifier", "");
            itemPrefab = MapEntityPrefab.Find(null, itemIdentifier) as ItemPrefab;
            if (itemPrefab == null)
            {
                DebugConsole.ThrowError("Tried to spawn \"" + Identifier + "\" with the item \"" + itemIdentifier + "\". Matching item prefab not found.");
                return;
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
            if (item.Prefab.Identifier == "idcard" || item.Prefab.Identifier == "idcardwreck")
            {
                item.AddTag("name:" + character.Name);
                item.ReplaceTag("wreck_id", Level.Loaded.GetWreckIDTag("wreck_id", submarine));
                var job = character.Info?.Job;
                if (job != null)
                {
                    item.AddTag("job:" + job.Name);
                }
            }
            foreach (WifiComponent wifiComponent in item.GetComponents<WifiComponent>())
            {
                wifiComponent.TeamID = character.TeamID;
            }
            if (parentItem != null)
            {
                parentItem.Combine(item, user: null);
            }
            foreach (XElement childItemElement in itemElement.Elements())
            {
                InitializeItems(character, childItemElement, submarine, item);
            }
        }
    }
}
