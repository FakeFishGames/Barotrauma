using System;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public class HumanPrefab : PrefabWithUintIdentifier
    {
        [Serialize("any", IsPropertySaveable.No)]
        public Identifier Job { get; protected set; }

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

        [Serialize(1f, IsPropertySaveable.No)]
        public float SkillMultiplier { get; protected set; }

        [Serialize(0, IsPropertySaveable.No)]
        public int ExperiencePoints { get; private set; }

        [Serialize(0, IsPropertySaveable.No)]
        public int BaseSalary { get; private set; }

        [Serialize(1f, IsPropertySaveable.No)]
        public float SalaryMultiplier { get; private set; }

        private readonly HashSet<Identifier> tags = new HashSet<Identifier>();

        [Serialize("", IsPropertySaveable.Yes)]
        public string Tags
        {
            get => string.Join(",", tags);
            set
            {
                tags.Clear();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    string[] splitTags = value.Split(',');
                    foreach (var tag in splitTags)
                    {
                        tags.Add(tag.ToIdentifier());
                    }
                }
            }
        }

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

        [Serialize(false, IsPropertySaveable.No, description: "If enabled, the NPC will not spawn if the specified spawn point tags can't be found.")]
        public bool RequireSpawnPointTag { get; protected set; }

        [Serialize(CampaignMode.InteractionType.None, IsPropertySaveable.No)]
        public CampaignMode.InteractionType CampaignInteractionType { get; protected set; }

        private readonly List<Identifier> storeIdentifiers = [];
        
        [Serialize("", IsPropertySaveable.No, description: "Store identifier(s) for this merchant NPC. Supports multiple identifiers separated by a semicolon. Merchants with the same combination of identifiers share the same store. If not set, defaults to the character's own identifier.")]
        public string StoreIdentifier
        {
            get => string.Join(";", storeIdentifiers);
            set
            {
                storeIdentifiers.Clear();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    foreach (string id in value.Split(';'))
                    {
                        string trimmedId = id.Trim();
                        if (!string.IsNullOrEmpty(trimmedId))
                        {
                            storeIdentifiers.Add(trimmedId.ToIdentifier());
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns the store identifier for this character's shop. For merchants with multiple store identifier pools, this is the semicolon-joined
        /// combination of all the identifiers. The first identifier determines the display name.
        /// Falls back to the character's own Identifier if no StoreIdentifier has been set.
        /// </summary>
        public Identifier GetStoreIdentifier() =>
            storeIdentifiers.Count > 0 ? StoreIdentifier.ToIdentifier() : Identifier;

        /// <summary>
        /// The <see cref="StoreIdentifier"/> pool of this merchant. Used for stacking merchants' store pools, and also
        /// for falling back their titles and names from left to right if those are not defined.
        /// </summary>
        public IReadOnlyList<Identifier> StoreIdentifiers => storeIdentifiers;

        [Serialize(AIObjectiveIdle.BehaviorType.Passive, IsPropertySaveable.No)]
        public AIObjectiveIdle.BehaviorType Behavior { get; protected set; }

        [Serialize(1.0f, IsPropertySaveable.No, description: 
            "Affects how far the character can hear sounds created by AI targets with the tag ProvocativeToHumanAI. "+
            "Used as a multiplier on the sound range of the target, e.g. a value of 0.5 would mean a target with a sound range of 1000 would need to be within 500 units for this character to hear it. "+
            "Only affects the \"fight intruders\" objective, which makes the character go and inspect noises.")]
        public float Hearing { get; set; } = 1.0f;

        [Serialize(float.PositiveInfinity, IsPropertySaveable.No)]
        public float ReportRange { get; protected set; }
        
        [Serialize(float.PositiveInfinity, IsPropertySaveable.No)]
        public float FindWeaponsRange { get; protected set; }

        public Identifier[] PreferredOutpostModuleTypes { get; protected set; }

        [Serialize("", IsPropertySaveable.No)]
        public Identifier Faction { get; set; }

        [Serialize("", IsPropertySaveable.No)]
        public Identifier Group { get; set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool AllowDraggingIndefinitely { get; set; }

        public XElement Element { get; protected set; }
        

        public readonly List<(ContentXElement element, float commonness)> ItemSets = new List<(ContentXElement element, float commonness)>();
        public readonly List<(ContentXElement element, float commonness)> CustomCharacterInfos = new List<(ContentXElement element, float commonness)>();

        public readonly Identifier NpcSetIdentifier;

        public HumanPrefab(ContentXElement element, ContentFile file, Identifier npcSetIdentifier) : base(file, element.GetAttributeIdentifier("identifier", ""))
        {
            SerializableProperty.DeserializeProperties(this, element);
            Element = element;
            element.GetChildElements("itemset").ForEach(e => ItemSets.Add((e, e.GetAttributeFloat("commonness", 1))));
            element.GetChildElements("character").ForEach(e => CustomCharacterInfos.Add((e, e.GetAttributeFloat("commonness", 1))));
            PreferredOutpostModuleTypes = element.GetAttributeIdentifierArray("preferredoutpostmoduletypes", Array.Empty<Identifier>());
            this.NpcSetIdentifier = npcSetIdentifier;
        }

        public IEnumerable<Identifier> GetTags()
        {
            return tags;
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
            return !Job.IsEmpty && Job != "any" ? JobPrefab.Get(Job) : JobPrefab.Random(randSync, predicate);
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
                humanAI.Hearing = Hearing;
                humanAI.ReportRange = ReportRange;
                humanAI.FindWeaponsRange = FindWeaponsRange;
                humanAI.AimSpeed = AimSpeed;
                humanAI.AimAccuracy = AimAccuracy;
            }
            if (CampaignInteractionType != CampaignMode.InteractionType.None)
            {
                (GameMain.GameSession.GameMode as CampaignMode)?.AssignNPCMenuInteraction(npc, CampaignInteractionType);
                if (positionToStayIn != null && humanAI != null)
                {
                    humanAI.ObjectiveManager.SetForcedOrder(new AIObjectiveGoTo(positionToStayIn, npc, humanAI.ObjectiveManager, repeat: true, getDivingGearIfNeeded: false, minimumCloseEnoughDistance: 200)
                    {
                        FaceTargetOnCompleted = false,
                        DebugLogWhenFails = false,
                        IsWaitOrder = true,
                        CloseEnough = 100
                    });
                }
            }
        }

        public bool GiveItems(Character character, Submarine submarine, WayPoint spawnPoint, Rand.RandSync randSync = Rand.RandSync.Unsynced, bool createNetworkEvents = true)
        {
            if (ItemSets == null || !ItemSets.Any()) { return false; }
            var spawnItems = ToolBox.SelectWeightedRandom(ItemSets, it => it.commonness, randSync).element;
            if (spawnItems != null)
            {
                foreach (ContentXElement itemElement in spawnItems.GetChildElements("item"))
                {
                    int amount = itemElement.GetAttributeInt("amount", 1);
                    for (int i = 0; i < amount; i++)
                    {
                        InitializeItem(character, itemElement, submarine, this, spawnPoint, createNetworkEvents: createNetworkEvents);
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Creates a character info from the human prefab. If there are custom character infos defined, those are used, otherwise a randomized info is generated.
        /// </summary>
        /// <param name="randSync"></param>
        /// <returns></returns>
        public CharacterInfo CreateCharacterInfo(Rand.RandSync randSync = Rand.RandSync.Unsynced)
        {
            var characterElement = ToolBox.SelectWeightedRandom(CustomCharacterInfos, info => info.commonness, randSync).element;
            CharacterInfo characterInfo;
            if (characterElement == null)
            {
                characterInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobOrJobPrefab: GetJobPrefab(randSync), randSync: randSync, humanPrefabIds: (NpcSetIdentifier, Identifier));
            }
            else
            {
                characterInfo = new CharacterInfo(characterElement, humanPrefabIds: (NpcSetIdentifier, Identifier));
            }
            if (characterInfo.Job != null && !MathUtils.NearlyEqual(SkillMultiplier, 1.0f))
            {
                foreach (var skill in characterInfo.Job.GetSkills())
                {
                    float newSkill = skill.Level * SkillMultiplier;
                    skill.IncreaseSkill(newSkill - skill.Level, canIncreasePastDefaultMaximumSkill: false);
                }
            }
            characterInfo.Salary = characterInfo.CalculateSalary(BaseSalary, SalaryMultiplier);
            characterInfo.GiveExperience(ExperiencePoints);
            return characterInfo;
        }
        
        /// <summary>
        /// Items marked to be spawned infinitely (by NPCs).
        /// </summary>
        private readonly Dictionary<Identifier, ItemPrefab> infiniteItems = new();
        public IReadOnlyCollection<ItemPrefab> InfiniteItems => infiniteItems.Values;

        public static void InitializeItem(Character character, ContentXElement itemElement, Submarine submarine, HumanPrefab humanPrefab, WayPoint spawnPoint = null, Item parentItem = null, bool createNetworkEvents = true)
        {
            ItemPrefab itemPrefab;
            string itemIdentifier = itemElement.GetAttributeString("identifier", "");
            itemPrefab = MapEntityPrefab.FindByIdentifier(itemIdentifier.ToIdentifier()) as ItemPrefab;
            if (itemPrefab == null)
            {
                DebugConsole.ThrowError("Tried to spawn \"" + humanPrefab?.Identifier + "\" with the item \"" + itemIdentifier + "\". Matching item prefab not found.",
                    contentPackage: itemElement?.ContentPackage);
                return;
            }
            Item item = new Item(itemPrefab, character.Position, submarine);
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
                item.ParentInventory = character.Inventory;
                Entity.Spawner.CreateNetworkEvent(new EntitySpawner.SpawnEntity(item));
            }
#endif
            if (itemElement.GetAttributeBool("equip", false))
            {
                List<InvSlotType> allowedSlots;

                IEnumerable<Wearable> wearableComponents = item.GetComponents<Wearable>();
                if (wearableComponents.Count() > 0)
                {
                    // If the item has wearable components, try to wear it instead by only allowing the slots
                    // used by those wearables rather than all slots the item itself could normally use.
                    allowedSlots = wearableComponents.SelectMany(static w => w.AllowedSlots).Distinct().ToList();
                }
                else
                {
                    allowedSlots = item.AllowedSlots.Distinct().ToList();
                }

                allowedSlots.Remove(InvSlotType.Any);

                item.UnequipAutomatically = false;
                character.Inventory.TryPutItem(item, null, allowedSlots, allowReplacingIntactLimb: true);
            }
            else
            {
                character.Inventory.TryPutItem(item, null, item.AllowedSlots, allowReplacingIntactLimb: true);
            }
            IdCard idCardComponent = item.GetComponent<IdCard>();
            if (idCardComponent != null)
            {
                idCardComponent.Initialize(spawnPoint, character);
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
            if (itemElement.GetAttributeBool(nameof(JobPrefab.JobItem.Infinite), false))
            { 
                humanPrefab.infiniteItems.TryAdd(itemPrefab.Identifier, itemPrefab);
            }
            foreach (ContentXElement childItemElement in itemElement.Elements())
            {
                int amount = childItemElement.GetAttributeInt(nameof(JobPrefab.JobItem.Amount), 1);
                for (int i = 0; i < amount; i++)
                {
                    InitializeItem(character, childItemElement, submarine, humanPrefab, spawnPoint, item, createNetworkEvents);
                }
            }
        }

        public override void Dispose() { }
    }
}
