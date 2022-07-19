using System;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class HumanPrefab : PrefabWithUintIdentifier
    {
        [Serialize("any", IsPropertySaveable.No)]
        public string Job { get; protected set; }

        [Serialize(1f, IsPropertySaveable.No)]
        public float Commonness { get; protected set; }

        [Serialize(1f, IsPropertySaveable.No)]
        public float HealthMultiplier { get; protected set; }

        [Serialize(1f, IsPropertySaveable.No)]
        public float HealthMultiplierInMultiplayer { get; protected set; }

        [Serialize(1f, IsPropertySaveable.No)]
        public float AimSpeed { get; protected set; }

        [Serialize(1f, IsPropertySaveable.No)]
        public float AimAccuracy { get; protected set; }

        private readonly HashSet<Identifier> moduleFlags = new HashSet<Identifier>();

        [Serialize("", IsPropertySaveable.Yes, "What outpost module tags does the NPC prefer to spawn in.")]
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
                        moduleFlags.Add(f.ToIdentifier());
                    }
                }
            }
        }


        private readonly HashSet<Identifier> spawnPointTags = new HashSet<Identifier>();

        [Serialize("", IsPropertySaveable.Yes, "Tag(s) of the spawnpoints the NPC prefers to spawn at.")]
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
                        spawnPointTags.Add(tag.ToIdentifier());
                    }
                }
            }
        }

        [Serialize(CampaignMode.InteractionType.None, IsPropertySaveable.No)]
        public CampaignMode.InteractionType CampaignInteractionType { get; protected set; }

        [Serialize(AIObjectiveIdle.BehaviorType.Passive, IsPropertySaveable.No)]
        public AIObjectiveIdle.BehaviorType Behavior { get; protected set; }

        [Serialize(float.PositiveInfinity, IsPropertySaveable.No)]
        public float ReportRange { get; protected set; }

        public Identifier[] PreferredOutpostModuleTypes { get; protected set; }

        public XElement Element { get; protected set; }
        

        public readonly Dictionary<XElement, float> ItemSets = new Dictionary<XElement, float>();
        public readonly Dictionary<XElement, float> CustomNPCSets = new Dictionary<XElement, float>();

        public HumanPrefab(ContentXElement element, ContentFile file) : base(file, element.GetAttributeIdentifier("identifier", ""))
        {
            SerializableProperty.DeserializeProperties(this, element);
            Job = Job.ToLowerInvariant();
            Element = element;
            element.GetChildElements("itemset").ForEach(e => ItemSets.Add(e, e.GetAttributeFloat("commonness", 1)));
            element.GetChildElements("character").ForEach(e => CustomNPCSets.Add(e, e.GetAttributeFloat("commonness", 1)));
            PreferredOutpostModuleTypes = element.GetAttributeIdentifierArray("preferredoutpostmoduletypes", Array.Empty<Identifier>());
        }

        public IEnumerable<Identifier> GetModuleFlags()
        {
            return moduleFlags;
        }

        public IEnumerable<Identifier> GetSpawnPointTags()
        {
            return spawnPointTags;
        }

        public JobPrefab GetJobPrefab(Rand.RandSync randSync = Rand.RandSync.Unsynced, Func<JobPrefab, bool> predicate = null)
        {
            return Job != null && Job != "any" ? JobPrefab.Get(Job) : JobPrefab.Random(randSync, predicate);
        }

        public void InitializeCharacter(Character npc, ISpatialEntity positionToStayIn = null)
        {
            var humanAI = npc.AIController as HumanAIController;
            if (humanAI != null)
            {
                var idleObjective = humanAI.ObjectiveManager.GetObjective<AIObjectiveIdle>();
                if (positionToStayIn != null && Behavior == AIObjectiveIdle.BehaviorType.StayInHull)
                {
                    idleObjective.TargetHull = AIObjectiveGoTo.GetTargetHull(positionToStayIn);
                    idleObjective.Behavior = AIObjectiveIdle.BehaviorType.StayInHull;
                }
                else
                {
                    idleObjective.Behavior = Behavior;
                    foreach (Identifier moduleType in PreferredOutpostModuleTypes)
                    {
                        idleObjective.PreferredOutpostModuleTypes.Add(moduleType);
                    }
                }
                humanAI.ReportRange = ReportRange;
                humanAI.AimSpeed = AimSpeed;
                humanAI.AimAccuracy = AimAccuracy;
            }
            if (CampaignInteractionType != CampaignMode.InteractionType.None)
            {
                (GameMain.GameSession.GameMode as CampaignMode)?.AssignNPCMenuInteraction(npc, CampaignInteractionType);
                if (positionToStayIn != null && humanAI != null)
                {
                    humanAI.ObjectiveManager.SetForcedOrder(new AIObjectiveGoTo(positionToStayIn, npc, humanAI.ObjectiveManager, repeat: true, getDivingGearIfNeeded: false, closeEnough: 200)
                    {
                        DebugLogWhenFails = false
                    });
                }
            }
        }

        public void GiveItems(Character character, Submarine submarine, Rand.RandSync randSync = Rand.RandSync.Unsynced, bool createNetworkEvents = true)
        {
            if (ItemSets == null || !ItemSets.Any()) { return; }
            var spawnItems = ToolBox.SelectWeightedRandom(ItemSets.Keys.ToList(), ItemSets.Values.ToList(), randSync);
            if (spawnItems != null)
            {
                foreach (XElement itemElement in spawnItems.GetChildElements("item"))
                {
                    InitializeItem(character, itemElement, submarine, this, createNetworkEvents: createNetworkEvents);
                }
            }
        }

        public CharacterInfo GetCharacterInfo(Rand.RandSync randSync = Rand.RandSync.Unsynced)
        {
            var characterElement = ToolBox.SelectWeightedRandom(CustomNPCSets.Keys.ToList(), CustomNPCSets.Values.ToList(), randSync);
            return characterElement != null ? new CharacterInfo(characterElement) : null;
        }

        public static void InitializeItem(Character character, XElement itemElement, Submarine submarine, HumanPrefab humanPrefab, Item parentItem = null, bool createNetworkEvents = true)
        {
            ItemPrefab itemPrefab;
            string itemIdentifier = itemElement.GetAttributeString("identifier", "");
            itemPrefab = MapEntityPrefab.FindByIdentifier(itemIdentifier.ToIdentifier()) as ItemPrefab;
            if (itemPrefab == null)
            {
                DebugConsole.ThrowError("Tried to spawn \"" + humanPrefab?.Identifier + "\" with the item \"" + itemIdentifier + "\". Matching item prefab not found.");
                return;
            }
            Item item = new Item(itemPrefab, character.Position, null);
#if SERVER
            if (GameMain.Server != null && Entity.Spawner != null && createNetworkEvents)
            {
                if (GameMain.Server.EntityEventManager.UniqueEvents.Any(ev => ev.Entity == item))
                {
                    string errorMsg = $"Error while spawning job items. Item {item.Name} created network events before the spawn event had been created.";
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("Job.InitializeJobItem:EventsBeforeSpawning", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                    GameMain.Server.EntityEventManager.UniqueEvents.RemoveAll(ev => ev.Entity == item);
                    GameMain.Server.EntityEventManager.Events.RemoveAll(ev => ev.Entity == item);
                }

                Entity.Spawner.CreateNetworkEvent(new EntitySpawner.SpawnEntity(item));
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
            IdCard idCardComponent = item.GetComponent<IdCard>();
            if (idCardComponent != null)
            {
                idCardComponent.Initialize(null, character);
                if (submarine != null && (submarine.Info.IsWreck || submarine.Info.IsOutpost))
                {
                    idCardComponent.SubmarineSpecificID = submarine.SubmarineSpecificIDTag;
                }

                var idCardTags = itemElement.GetAttributeStringArray("tags", Array.Empty<string>());
                foreach (string tag in idCardTags)
                {
                    item.AddTag(tag);
                }
            }            

            foreach (WifiComponent wifiComponent in item.GetComponents<WifiComponent>())
            {
                wifiComponent.TeamID = character.TeamID;
            }
            parentItem?.Combine(item, user: null);
            foreach (XElement childItemElement in itemElement.Elements())
            {
                InitializeItem(character, childItemElement, submarine, humanPrefab, item, createNetworkEvents);
            }
        }

        public override void Dispose() { }
    }
}
