using Barotrauma;
using Barotrauma.Extensions;
using System.Globalization;
using Microsoft.Xna.Framework;
using System.Collections.Immutable;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework.Content;

namespace HomeAndHarbor
{
    /// <summary>
    /// Defines a work station an outpost NPC can be assigned to. 
    /// Includes optional features for configuring the NPC's behavior when operating the work station.
    /// </summary>
    public class WorkstationComponent : ResourceGeneratorBaseComponent
    {
        internal static readonly List<WorkstationComponent> WorkstationComponents = [];
        
        /// <summary>
        /// Fairly sparse update interval, because the NPCs are intended to mainly "give the impression they're working",
        /// and don't need to be very fast or efficient
        /// </summary>
        private new const float AIUpdateInterval = 10.0f;

        private float orderReassignmentTimer = 0f;

        [Serialize(false, IsPropertySaveable.No, description: "If true, AI operation is allowed to extend outside the submarine (eg. for fishing).")]
        public bool AllowOperateOutside
        {
            get;
            set;
        }

        [Serialize(1, IsPropertySaveable.No, "The workstations are ordered based on this value in the dweller UI, in descending order.")]
        public int MenuOrder { get; set; }

        public override bool AIOperateAllowedOutside => AllowOperateOutside; // Needed as AIOperateAllowedOutside is get-only

        /// <summary>
        /// This prevents AIObjectiveOperateItem from creating GoTo spam, and the dwellers
        /// don't need to be touching the workstation at all times anyway.
        /// </summary>
        public override bool AIOperateRemotely => true;

        [Serialize("", IsPropertySaveable.Yes, description: "Title given to the NPC assigned to this work station. Used as an identifier to find the text from a text file, or if not found, used as-is.")]
        public string JobTitle
        {
            get;
            set;
        } = string.Empty;

        [Serialize("", IsPropertySaveable.No, description: "Identifier of the job that will temporarily override the regular job of characters assigned to this workstation.")]
        public Identifier OverridingJobIdentifier
        {
            get;
            set;
        } = Identifier.Empty;

        [Serialize("", IsPropertySaveable.No,
            description: "If set, the NPC assigned to this workstation runs a store with this (pool of) identifier(s). The first is intended to be the store's own identifier, and its prices are used as-is. For the rest, everything they stock is sold here, at the most player-friendly price available among them (see WorkstationStoreInfo.TryOverridePriceInfo). This differs from normal store identifier pools, which pick the first price encountered in the list for each item.")]
        public Identifier StoreIdentifier
        {
            get;
            set;
        } = Identifier.Empty;

        /// <summary>
        /// Helper property to get the actual Job object from the OverridingJobIdentifier
        /// </summary>
        public Job? OverridingJob => OverridingJobIdentifier.IsEmpty ? null : new Job(JobPrefab.Get(OverridingJobIdentifier), isPvP: false);

        [Serialize(1, IsPropertySaveable.No, description: "How many NPCs can be assigned to this work station?")]
        public int MaxAssignedNPCs
        {
            get;
            set;
        }

        [Serialize(10.0f, IsPropertySaveable.No, description:
            "Priority given to the objective to operate the work station, between 0 and 100. " +
            "Note that the Idle objective all characters have has a priority of 1, so in practice this priority should have a priority above 1 for a bot to operate the workstation. " +
            "Setting it to 0 might be useful though if the bot is not supposed to pay any attention to the workstation, but e.g. just do the normal tasks of the job the workstation assigns them.")]
        public float ObjectivePriority
        {
            get;
            set;
        }

        [Serialize("", IsPropertySaveable.No, description: "Identifier of the skill associated with this workstation. Assigned characters can gain XP in it per world step, and the skill level scales resource output if the ResourceGenerator uses has skill-based output amounts defined.")]
        public Identifier SkillIdentifier
        {
            get;
            set;
        }

        [Serialize(0f, IsPropertySaveable.No, description: "Base skill gain passed to ApplySkillGain() per world step. Note that this value does not mean skill point, but something much smaller. The actual skill level increase is baseGain / max(skillLevel, 15)^1.5. To determine sensible ballpark for values, see SkillSettings.xml for comparison. Zero disables skill gain.")]
        public float SkillIncreasePerWorldStep
        {
            get;
            set;
        }
        [Serialize("", IsPropertySaveable.No, description:
            "Tags or identifiers of items that must be available in the outpost/sub for the workstation to generate resources as defined in the ResourceGenerator component.")]
        public Identifier[] ItemsRequiredForResourceGeneration
        {
            get;
            set;
        } = [];

        [Serialize("", IsPropertySaveable.No, description: "Tags or identifiers of items the character should bring to the workstation.")]
        public Identifier[] ItemsToTransportToWorkstation
        {
            get;
            set;
        } = [];
        
        [Serialize("", IsPropertySaveable.No, description:
            "Tags or identifiers of non-optional items the character should attempt to equip/wear." +
            " The character will not be assigned any further orders/objectives by the work station if they cannot find the items.")]
        public Identifier[] ItemsToEquip
        {
            get;
            set;
        } = [];

        [Serialize("", IsPropertySaveable.No, description: "Tags or identifiers of optional items the character should attempt to equip/wear.")]
        public Identifier[] ItemsToEquipOptional
        {
            get;
            set;
        } = [];

        [Serialize("", IsPropertySaveable.No, description: "Identifier of a the line the character is supposed to say if it can't find the items it wants to equip.")]
        public Identifier CantFindItemsToEquipIdentifier { get; set; }

        [Serialize("", IsPropertySaveable.No, description: "Tags or identifiers of items the character should carry in their inventory. Supports condition requirements (mainly for managing consumables): eg. 'oxygensource gte 50'.")]
        public Identifier[] ItemsToCarry
        {
            get;
            set;
        } = [];

        [Serialize("", IsPropertySaveable.No, description: "Tags or identifiers of optional items the character should carry in their inventory. Supports the same condition requirements as ItemsToCarry. Failing to find these doesn't prevent the workstation from assigning orders.")]
        public Identifier[] ItemsToCarryOptional
        {
            get;
            set;
        } = [];

        [Serialize("", IsPropertySaveable.No, description: "Identifier of a the line the character is supposed to say if it can't find the items it wants to carry.")]
        public Identifier CantFindItemsToCarryIdentifier { get; set; }

        [Serialize("", IsPropertySaveable.No, description: "Tags or identifiers of items the NPC should drop from their inventory. Supports condition requirements (mainly for managing consumables): eg. 'batterycell lt 50'.")]
        public Identifier[] ItemsToDrop
        {
            get;
            set;
        } = [];

        [Serialize("", IsPropertySaveable.No, description: "Tags or identifiers of items the NPC should attempt to operate.")]
        public Identifier[] ItemsToOperate
        {
            get;
            set;
        } = [];

        [Serialize("", IsPropertySaveable.No, description: "Which types of components should the character attempt to operate on ItemsToOperate?")]
        public Identifier[] ItemComponentsToOperate
        {
            get;
            set;
        } = [];

        [Serialize("", IsPropertySaveable.No, description: "Orders to assign to the NPC operating this workstation (eg. 'gardening', 'exploreoutside').")]
        public Identifier[] OrdersToAssign
        {
            get;
            set;
        } = [];

        [Serialize(0f, IsPropertySaveable.No, description: "If set to above zero, orders will only be reassigned at this interval (in seconds). Useful for giving the workstation assignees some idle time too. If zero or unset, orders are checked and assigned every AI update, which may be shorter or longer than this period.")]
        public float OrderReassignmentInterval
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "If true, orders will only be assigned when the NPC is idle (not busy with autonomous objectives like firefighting, fleeing etc).")]
        public bool OrdersRequireIdle
        {
            get;
            set;
        }

        [Serialize(0f, IsPropertySaveable.No, description: "If set to a non-zero value, the NPC will be forced to stay within this distance of the workstation (considered a 'desk job') and won't have separate idle behavior. If this value isn't set, the NPC will follow normal idle behavior, wandering the workstation's hull, and is free to perform other tasks when necessary (eg. fight fires). Values above 0 get clamped to 50 minimum.")]
        public float DeskJobWanderDistance
        {
            get;
            set;
        }

        [Serialize("128,128,128,255", IsPropertySaveable.No, description: "Color used to indicate this workstation type in the UI.")]
        public Color IndicatorColor
        {
            get;
            set;
        }

        [Serialize(CampaignMode.InteractionType.None, IsPropertySaveable.No, description: "Campaign interaction type set for NPCs assigned to this workstation. Determines what menu the player sees when interacting with the NPC (eg. store, HR, medical etc).")]
        public CampaignMode.InteractionType CampaignInteractionType
        {
            get;
            set;
        }

        [Serialize("", IsPropertySaveable.No, description: "Identifier of a ScriptedEvent to run while an NPC is assigned to this workstation, giving that role its own dialogue. The ConversationActions in the event need to use ConversationSpeakerTag as their speakertag. Takes precedence over CampaignInteractionType.")]
        public Identifier ConversationEventIdentifier
        {
            get;
            set;
        }

        [Serialize("", IsPropertySaveable.No, description: "Tag the NPCs assigned to this workstation are registered under, so the ConversationActions in ConversationEventIdentifier can find them as the speaker.")]
        public Identifier ConversationSpeakerTag
        {
            get;
            set;
        }

        /// <summary>
        /// Represents an item requirement with optional condition threshold using PropertyConditional operators.
        /// For example just "identifier" or optionally "identifier gte 50" (identifier, operator, value).
        /// </summary>
        private readonly record struct ItemRequirement(Identifier IdentifierOrTag, PropertyConditional.ComparisonOperatorType Operator, float? ConditionThreshold)
        {
            public static ItemRequirement Parse(string requirement)
            {
                if (string.IsNullOrWhiteSpace(requirement))
                {
                    return new ItemRequirement(Identifier.Empty, PropertyConditional.ComparisonOperatorType.None, null);
                }

                // Split into parts: "oxygensource gte 50" -> ["oxygensource", "gte", "50"]
                var parts = requirement.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 2)
                {
                    // Plain identifier, no condition requirement
                    return new ItemRequirement(requirement.Trim().ToIdentifier(), PropertyConditional.ComparisonOperatorType.None, null);
                }

                string identifierPart = parts[0];
                // Rejoin everything after the identifier for operator extraction: "gte 50"
                string operatorAndValue = string.Join(' ', parts.Skip(1));

                var (op, valueStr) = PropertyConditional.ExtractComparisonOperatorFromConditionString(operatorAndValue);

                if (op != PropertyConditional.ComparisonOperatorType.None &&
                    op != PropertyConditional.ComparisonOperatorType.Equals &&
                    float.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float threshold))
                {
                    return new ItemRequirement(identifierPart.ToIdentifier(), op, threshold);
                }

                DebugConsole.AddWarning($"Could not parse operator syntax for '{requirement.Trim()}'");
                return new ItemRequirement(Identifier.Empty, PropertyConditional.ComparisonOperatorType.None, null);
            }

            public bool MeetsCondition(float conditionPercentage)
            {
                return !ConditionThreshold.HasValue ?
                    true :
                    PropertyConditional.CompareFloat(conditionPercentage, ConditionThreshold.Value, Operator);
            }
        }

        private readonly Dictionary<Character, AIObjective> assignedObjectives = new Dictionary<Character, AIObjective>();

        private ScriptedEvent? conversationEvent; // In MP, this only ever runs on the server

        private List<ItemRequirement>? parsedItemsToCarry;
        private List<ItemRequirement>? parsedItemsToCarryOptional;
        private List<ItemRequirement>? parsedItemsToDrop;
        private List<ItemRequirement> GetParsedItemsToCarry()
            => parsedItemsToCarry ??= ParseItemRequirements(ItemsToCarry);

        private List<ItemRequirement> GetParsedItemsToCarryOptional()
            => parsedItemsToCarryOptional ??= ParseItemRequirements(ItemsToCarryOptional);

        private List<ItemRequirement> GetParsedItemsToDrop()
            => parsedItemsToDrop ??= ParseItemRequirements(ItemsToDrop);

        private static List<ItemRequirement> ParseItemRequirements(Identifier[] identifiers)
            => identifiers
                .Where(id => !id.IsEmpty)
                .Select(id => ItemRequirement.Parse(id.Value))
                .ToList();

        public int AssignedNPCCount => assignedObjectives.Count;

        public WorkstationComponent(Item item, ContentXElement element) : base(item, element)
        {
            //must be selectable for NPCs to operate
            canBeSelected = true;

            WorkstationComponents.Add(this);

            // Pass the workstation's skill identifier to the resource generator for production scaling
            ResourceGenerator.SetSkillIdentifier(SkillIdentifier);

            // Validate that the workstation has a skill identifier set, and that all produced resources have skill-based amounts
            if (SkillIdentifier.IsEmpty)
            {
                foreach (var resource in ResourceGenerator.ProducedResourcesList)
                {
                    if (resource.AmountAtMinSkill.HasValue || resource.AmountAtMaxSkill.HasValue)
                    {
                        DebugConsole.AddWarning($"'{item.Prefab.Identifier}' WorkstationComponent: ProducedResource '{resource.Identifier}' defines skill-based amounts (AmountAtMinSkill/AmountAtMaxSkill) but no SkillIdentifier is set on the WorkstationComponent.");
                        break;
                    }
                }
            }
        }

        protected override bool ShouldResourceGeneratorBeActive()
        {
            if (assignedObjectives.Count == 0) { return false; }
            if (ItemsRequiredForResourceGeneration.Any())
            {
                var availableItems = item.Submarine?.GetItems(alsoFromConnectedSubs: true) ?? [];
                foreach (var character in GetAssignedCharacters)
                {
                    availableItems.AddRange(character.Inventory.FindAllItems(recursive: true));
                }
                foreach (var requiredItem in ItemsRequiredForResourceGeneration)
                {
                    if (availableItems.None(availableItem => availableItem.HasTag(requiredItem) || availableItem.Prefab.Identifier == requiredItem)) { return false; }
                }
            }
            return true;
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            WorkstationComponents.Remove(this);
        }

        public static WorkstationComponent? FindWorkstationWithID(int uniqueId)
        {
            return WorkstationComponents.FirstOrDefault(w => w.UniqueId == uniqueId);
        }

        public bool HasCharacterAssigned(Character character)
        {
            return assignedObjectives.ContainsKey(character);
        }

        public IEnumerable<Character> GetAssignedCharacters
            => assignedObjectives.Keys.Distinct();

        public void AssignCharacter(Character character)
        {
            // If character is already assigned to this workstation, don't reassign
            if (HasCharacterAssigned(character)) { return; }

            // Apply job override if this workstation has one configured
            if (OverridingJob != null)
            {
                character.Info.JobOverride = OverridingJob;
                if (character.AIController is HumanAIController humanAiController)
                {
                    humanAiController.ObjectiveManager.CreateAutonomousObjectives();
                }
            }

            // Set up campaign interactions (store, role dialogue, etc.)
            InitializeInteractions(character);

            // Apply the workstation's job title to the character (if there is one)
            if (!string.IsNullOrEmpty(JobTitle))
            {
                character.Info.Title = TextManager.Get(JobTitle);
            }

            if (character.AIController is HumanAIController humanAi && OrderPrefab.Prefabs.TryGet("operateworkstation", out OrderPrefab? orderPrefab) && orderPrefab != null)
            {
                DismissShipCommandManagerOrders(character);
                Order order = new Order(orderPrefab, item, this);
                var objective = humanAi.SetOrder(order);
                if (objective != null)
                {
                    if (objective is AIObjectiveOperateItem operateObjective)
                    {
                        //the workstation creates subobjectives such as finding optional gear, failing those should not make the bot abandon the whole objective
                        operateObjective.AbandonWhenCannotCompleteSubObjectives = false;
                        operateObjective.AllowReplacementByShipCommandManager = false;
                        operateObjective.OverridePriority = ObjectivePriority;
                    }
                    assignedObjectives.Add(character, objective);
                }
                else
                {
                    DebugConsole.ThrowError("Failed to create an objective for operating a workstation.");
                    return;
                }

            }

            UpdateResourceGeneratorSkillLevel();

            // For non-desk jobs, set the idle behavior (to stay in the workstation's hull),
            // desk jobs use a repeating GoTo sub-objective in CrewAIOperate instead
            bool isDeskJob = DeskJobWanderDistance > 0;
            if (!isDeskJob)
            {
                SetIdleBehaviorNearWorkstation(character);
            }

            RefreshIsActive();
        }

        /// <summary>
        /// Dismisses the orders given to the character by a <see cref="ShipCommandManager"/> commander (e.g. operating a turret).
        /// </summary>
        private static void DismissShipCommandManagerOrders(Character character)
        {
            if (character.CurrentOrders == null) { return; }
            var ordersToDismiss = character.CurrentOrders
                .Where(o => o is { IsDismissal: false } && o.OrderGiver?.AIController is HumanAIController { ShipCommandManager: not null })
                .ToList();
            foreach (Order order in ordersToDismiss)
            {
                character.SetOrder(order.GetDismissal(), isNewOrder: false, speak: false, force: true);
            }
        }

        public void UnassignCharacter(Character character)
        {
            if (assignedObjectives.TryGetValue(character, out AIObjective? objective) && objective != null)
            {
                if (!character.IsDead && !character.Removed)
                {
                    objective.Abandon = true;
                }
                assignedObjectives.Remove(character);
            }
            else
            {
                DebugConsole.NewMessage($"WorkstationComponent {UniqueId}: No objective found for {character.DisplayName}, character was not assigned to this workstation");
            }

            UpdateResourceGeneratorSkillLevel();

            ClearInteractions(character);

            // Clear job override if this workstation had one configured
            if (OverridingJob != null)
            {
                character.Info.JobOverride = null;
            }

            // Clear the workstation's job title from the character (if any)
            if (!string.IsNullOrEmpty(JobTitle))
            {
                character.Info.Title = string.Empty;
            }

            // Forget everything about the workstation, where it was, how to behave there etc
            OutpostDwellerManager.ClearAIState(character);

            ReturnNoLongerNeededItems(character);

            RefreshIsActive();
        }

        /// <summary>
        /// Makes the bot return items this workstation made them fetch to where they belong.
        /// </summary>
        private void ReturnNoLongerNeededItems(Character character)
        {
            if (character.AIController is not HumanAIController humanAi) { return; }
            if (!OrderPrefab.Prefabs.TryGet("cleanupitems".ToIdentifier(), out OrderPrefab? cleanUpOrder) || cleanUpOrder == null)
            {
                DebugConsole.ThrowError("Could not find the cleanupitems order prefab.");
                return;
            }
            var requirements = GetParsedItemsToCarry().Concat(GetParsedItemsToCarryOptional());
            //return the items that this workstation made the character get
            foreach (var item in character.Inventory.FindAllItems(recursive: true))
            {
                if (ItemsToEquip.Contains(item.Prefab.Identifier) || item.HasTag(ItemsToEquip) ||
                    ItemsToEquipOptional.Contains(item.Prefab.Identifier) || item.HasTag(ItemsToEquipOptional) ||
                    ItemsToCarry.Contains(item.Prefab.Identifier) || item.HasTag(ItemsToCarry) ||
                    requirements.Any(r => r.IdentifierOrTag == item.Prefab.Identifier || item.HasTag(r.IdentifierOrTag)))
                {
                    createReturnItemObjective(item);
                }
            }
            //a special case for workstations that give the bot an order to find a weapon:
            //see if the bot has a weapon and try to return it.
            //this is not 100 % reliable, the bot may have some other weapon they've previously gotten from somewhere else,
            //unrelated to the workstation - but it's probably ok to make the bot put it back anyway
            if (OrdersToAssign.Contains(Tags.FindWeaponOrder))
            {
                var weapon = character.Inventory.AllItems.MaxBy(it => it.Components.Max(c => c.CombatPriority));
                if (weapon != null && weapon.HasTag(Tags.Weapon))
                {
                    createReturnItemObjective(weapon);
                }
            }

            void createReturnItemObjective(Item itemToReturn)
            {
                var cleanUpObjective = new AIObjectiveCleanupItem(itemToReturn, character, humanAi.ObjectiveManager)
                {
                    //slightly elevated priority, returning the items to the appropriate places is more important
                    //than the order for getting a new uniform we create below
                    Priority = AIObjectiveManager.LowestOrderPriority + 1,
                    //if this workstation has a container the items can be returned to, do that
                    PreferredContainer = item.GetComponent<ItemContainer>()
                };
                humanAi.ObjectiveManager.AddObjective(cleanUpObjective);
            }

            //make sure the character has some clothes (in case they e.g. fetched a security uniform after being assigned to a guardpost)...
            var getClothesObjective = new AIObjectiveGetItem(character, "clothing".ToIdentifier(), humanAi.ObjectiveManager, equip: true)
            {
                Wear = true,
                StoreUnequipped = false,
                //needs to be the base priority, the objective would just override the Priority value
                BasePriority = AIObjectiveManager.LowestOrderPriority,
                //...but not the ones this workstation made them get
                ignoredIdentifiersOrTags = ItemsToEquip.Union(ItemsToEquipOptional).ToImmutableHashSet()
            };
            humanAi.ObjectiveManager.AddObjective(getClothesObjective);
        }

        public void ClearAllAssignments()
        {
            foreach (var kvp in assignedObjectives.ToList())
            {
                if (kvp.Value != null)
                {
                    kvp.Value.Abandon = true;
                }
            }

            assignedObjectives.Clear();
            ResourceGenerator.IsActive = false;
            UpdateResourceGeneratorSkillLevel();
        }

        /// <summary>
        /// Initializes campaign interactions for a character assigned to this workstation.
        /// </summary>
        public void InitializeInteractions(Character character)
        {
            // Role-specific dialogue, defined as a ScriptedEvent in XML
            if (!ConversationEventIdentifier.IsEmpty)
            {
                SetupConversationInteraction(character);
            }
            // Otherwise, open a campaign menu (store, HR, medical etc) directly
            else if (CampaignInteractionType != CampaignMode.InteractionType.None)
            {
                SetupMenuInteraction(character);
            }
        }

        /// <summary>
        /// Clears the campaign interactions of a dweller no longer assigned to this workstation.
        /// </summary>
        private void ClearInteractions(Character character)
        {
            if (!ConversationEventIdentifier.IsEmpty)
            {
                ClearConversationInteraction(character);
            }
            else if (CampaignInteractionType != CampaignMode.InteractionType.None)
            {
                ClearMenuInteraction(character);
            }
        }

        public override bool Select(Character character)
        {
            // Prevent crew tab from toggling if a player happens to interact with this component
            if (character.IsPlayer) { return false; }

            return base.Select(character);
        }

        public override bool CrewAIOperate(float deltaTime, Character character, AIObjectiveOperateItem operateWorkstationObjective)
        {
            if (character.AIController is not HumanAIController humanAi) { return false; }

            // AIObjectiveOperateItem only calls this when it has no active sub-objectives, so nothing else is steering the bot right now.
            // Reset every frame, otherwise the bot keeps moving in the direction of a sub-objective that just completed or got removed (walking against walls/doors).
            // Doesn't restrict roaming jobs: their movement comes from the sub-objectives, which steer on their own.
            humanAi.SteeringManager.Reset();

            // If the orders are configured to be periodic, check if they should be assigned now
            if (OrderReassignmentInterval > 0)
            {
                orderReassignmentTimer -= deltaTime;
                if (orderReassignmentTimer <= 0)
                {
                    orderReassignmentTimer = OrderReassignmentInterval;

                    if (FindItemsToCarry(character, operateWorkstationObjective, GetParsedItemsToCarry()) &&
                        FindItemsToEquip(character, operateWorkstationObjective, ItemsToEquip))
                    {
                        //search for optional items too if the mandatory ones can be found
                        FindItemsToCarry(character, operateWorkstationObjective, GetParsedItemsToCarryOptional());
                        FindItemsToEquip(character, operateWorkstationObjective, ItemsToEquipOptional);
                        //assign orders regardless if the optional items are found or not
                        TryAddOrderObjectives(character, humanAi, operateWorkstationObjective);
                    }
                }
            }

            aiUpdateTimer -= deltaTime;
            if (aiUpdateTimer > 0.0f) { return false; }

            //NOTE: this call can be fairly expensive if we need to check ItemsRequiredForResourceGeneration,
            //so it's important it stays here where it only runs periodically
            RefreshIsActive();

            RestoreMenuInteraction(character);

            aiUpdateTimer = AIUpdateInterval;

            bool isDeskJob = DeskJobWanderDistance > 0;

            // Drop items matching ItemsToDrop criteria before fetching any replacements
            DropItemsToDrop(character);

            // Ensure the character has the items required to equip and carry
            bool hasItemsToCarry = FindItemsToCarry(character, operateWorkstationObjective, GetParsedItemsToCarry());
            bool hasItemsToEquip = FindItemsToEquip(character, operateWorkstationObjective, ItemsToEquip);
            // Try finding optional items too, but don't prevent the orders from being created even if the items can't be found
            FindItemsToCarry(character, operateWorkstationObjective, GetParsedItemsToCarryOptional());
            FindItemsToEquip(character, operateWorkstationObjective, ItemsToEquipOptional);

            if (hasItemsToCarry && hasItemsToEquip)
            {
                // For "desk jobs" that need to stay close to the workstation, add a repeating GoTo objective to keep them there.
                // Otherwise, the default idle behavior (set in SetIdleBehaviorNearWorkstation) is used instead, freeing the dweller to fulfill their tasks as the need arises.
                if (isDeskJob)
                {
                    TryAddDeskJobGoToSubObjective(character, operateWorkstationObjective);
                }
                // If no custom interval is set, try assign the orders now (=the period is every AI update)
                if (OrderReassignmentInterval <= 0)
                {
                    TryAddOrderObjectives(character, humanAi, operateWorkstationObjective);
                }
            }

            FindItemsToOperate(character, operateWorkstationObjective);

            return false;
        }

        /// <summary>
        /// Attempts to add objectives for any orders configured for this workstation.
        /// </summary>
        private void TryAddOrderObjectives(Character character, HumanAIController humanAi, AIObjectiveOperateItem operateWorkstationObjective)
        {
            // If OrdersRequireIdle is set, only assign orders when the bot isn't busy with anything else
            if (OrdersRequireIdle &&
                humanAi.ObjectiveManager.CurrentObjective is not AIObjectiveIdle and not AIObjectiveOperateItem)
            {
                return;
            }

            foreach (var orderIdentifier in OrdersToAssign)
            {
                if (!OrderPrefab.Prefabs.TryGet(orderIdentifier, out OrderPrefab? orderPrefab) || orderPrefab == null)
                {
                    DebugConsole.AddWarning($"Could not find prefab for order '{orderIdentifier}' configured for workstation '{item.Prefab.Identifier}'");
                    continue;
                }

                Item? orderTargetItem = null;
                if (orderPrefab.MustSetTarget)
                {
                    orderTargetItem = orderPrefab.GetMatchingItems(item.Submarine, mustBelongToPlayerSubOrOutpost: false, requiredTeam: item.Submarine.TeamID, interactableFor: character)?.GetRandomUnsynced();
                }
                var order = new Order(orderPrefab, option: Identifier.Empty, orderTargetItem ?? character.CurrentHull as Entity, orderPrefab.GetTargetItemComponent(orderTargetItem));
                var newObjective = humanAi.ObjectiveManager.CreateObjective(order, isSubObjective: true);
                if (newObjective == null)
                {
                    DebugConsole.AddWarning($"Failed creating new objective from order '{order.Identifier}'");
                    continue;
                }

                // Add the workstation-specific objectives as sub-objectives of the workstation operate item objective
                operateWorkstationObjective.AddSubObjective(newObjective, addFirst: true);

                // Special case for doctors
                if (newObjective is AIObjectiveRescueAll rescueObjective)
                {
                    rescueObjective.OnlyTreatAutomaticallyIfMedicOrOrdered = false;
                }
                //special case for security officers: the normal behavior of the "findweapon" order is to make the bot find some weapon,
                //regardless if they have some weapon in their inventory or not. They also don't evaluate all the items they have access to and choose the best weapon,
                //but rather pick the first valid weapon they can find.
                //here we instead make the bot find the absolute best weapon they have access to
                if (orderPrefab.Identifier == Tags.FindWeaponOrder &&
                    newObjective is AIObjectivePrepare prepareObjective)
                {
                    prepareObjective.EvaluateAllItems = true;
                    //we don't want the security officer to keep complaining about failing to find a better weapon
                    prepareObjective.SpeakIfFails = false;
                };
            }

            TryAddItemTransportObjectives(character, operateWorkstationObjective);
        }

        /// <summary>
        /// Adds a repeating GoTo sub-objective for desk jobs, but only if one doesn't already exist.
        /// </summary>
        private void TryAddDeskJobGoToSubObjective(Character character, AIObjectiveOperateItem objective)
        {
            if (objective.SubObjectives.None(
                subObjective => subObjective is AIObjectiveGoTo goTo && goTo.Target == item))
            {
                objective.AddSubObjective(new AIObjectiveGoTo(item, character, objective.objectiveManager,
                    repeat: true,
                    minimumCloseEnoughDistance: DeskJobWanderDistance));
            }
        }

        /// <summary>
        /// Drops items matching the ItemsToDrop criteria, including their condition.
        /// Useful for discarding depleted consumables like oxygen and batteries.
        /// </summary>
        private void DropItemsToDrop(Character character)
        {
            var requirements = GetParsedItemsToDrop();
            if (requirements.Count == 0) { return; }

            foreach (var requirement in requirements)
            {
                if (requirement.IdentifierOrTag.IsEmpty) { continue; }

                foreach (var item in character.Inventory.AllItemsMod)
                {
                    bool matchesIdentifierOrTag =
                        item.Prefab.Identifier == requirement.IdentifierOrTag ||
                        item.HasTag(requirement.IdentifierOrTag);

                    if (matchesIdentifierOrTag && requirement.MeetsCondition(item.ConditionPercentage))
                    {
                        item.Drop(character);
                        (character.AIController as HumanAIController)?.StoreItem(item);
                    }
                }
            }
        }

        /// <summary>
        /// Makes the NPC attempt to find and equip the items defined in ItemsToEquip.
        /// Returns true if all required items are already in inventory.
        /// </summary>
        private bool FindItemsToEquip(Character character, AIObjectiveOperateItem objective, IEnumerable<Identifier> identifiersOrTags)
        {
            bool hasAllItems = true;
            foreach (var identifierOrTag in identifiersOrTags)
            {
                Item? existingItem =
                    character.Inventory.FindItemByTag(identifierOrTag, recursive: true) ??
                    character.Inventory.FindItemByIdentifier(identifierOrTag, recursive: true);

                // Check if item is actually equipped (in hands) or worn
                bool isEquipped = character.HasEquippedItem(identifierOrTag);
                if (existingItem == null || !isEquipped)
                {
                    // Check if a GetItem sub-objective for this identifier already exists
                    bool alreadyHasGetItem = objective.SubObjectives.Any(
                        sub => sub is AIObjectiveGetItem getItem && getItem.IdentifiersOrTags.Contains(identifierOrTag));

                    if (!alreadyHasGetItem)
                    {
                        objective.AddSubObjective(new AIObjectiveGetItem(character, identifierOrTag, objective.objectiveManager, equip: true)
                        {
                            Wear = true,
                            //the workstation's gear replaces what the character was wearing, don't switch back to the old gear later
                            StoreUnequipped = false,
                            SpeakIfFails = !CantFindItemsToEquipIdentifier.IsEmpty,
                            CannotFindDialogueIdentifierOverride = CantFindItemsToEquipIdentifier,
                            MinSpeakInterval = 60.0f
                        },
                        allowMultipleInstances: true);
                    }
                    hasAllItems = false;
                }
            }
            return hasAllItems;
        }

        /// <summary>
        /// Makes the NPC attempt to transport items to this workstation's inventory.
        /// </summary>
        private void TryAddItemTransportObjectives(Character character, AIObjectiveOperateItem objective)
        {
            Dictionary<Identifier, int> itemCounts = [];
            foreach (Identifier item in ItemsToTransportToWorkstation)
            {
                if (!itemCounts.TryGetValue(item, out int currentValue))
                {
                    itemCounts.Add(item, 1);
                }
                else
                {
                    itemCounts[item]++;
                }
            }
            
            foreach ((Identifier tagOrIdentifier, int count) in itemCounts)
            {
                if (Item.OwnInventory.AllItems.Count(item => item.HasIdentifierOrTag(tagOrIdentifier)) >= count)
                {
                    continue;
                }

                bool alreadyHasContainItem = objective.SubObjectives.Any(
                    sub => sub is AIObjectiveContainItem getItem &&
                            getItem.itemIdentifiers.Contains(tagOrIdentifier));

                if (!alreadyHasContainItem)
                {
                    // Character is not already fetching this item, add an objective for it
                    var containItemObjective = new AIObjectiveContainItem(
                        character, tagOrIdentifier, Item.OwnInventory.Container, objective.objectiveManager);

                    containItemObjective.ignoredContainerIdentifiers = Item.Prefab.Tags;
                    containItemObjective.ItemCount = count;
                    containItemObjective.checkInventory = false;

                    objective.AddSubObjective(containItemObjective);
                }
            }
        }

        /// <summary>
        /// Makes the NPC attempt to find and pocket (not equip) the given items (ItemsToCarry or ItemsToCarryOptional).
        /// Supports condition requirements (eg. "oxygensource gt 50").
        /// Returns true if all the required items are already in inventory with acceptable condition.
        /// </summary>
        private bool FindItemsToCarry(Character character, AIObjectiveOperateItem objective, List<ItemRequirement> requirements)
        {
            if (requirements.Count == 0) { return true; }

            foreach (var requirement in requirements)
            {
                if (requirement.IdentifierOrTag.IsEmpty) { continue; }

                var matchingItems = character.Inventory.AllItems
                    .Where(item => item.Prefab.Identifier == requirement.IdentifierOrTag ||
                                    item.HasTag(requirement.IdentifierOrTag));
                var itemsMeetingCondition = matchingItems
                    .Where(item => requirement.MeetsCondition(item.ConditionPercentage));

                if (itemsMeetingCondition.None())
                {
                    bool alreadyHasGetItem = objective.SubObjectives.Any(
                        sub => sub is AIObjectiveGetItem getItem &&
                               getItem.IdentifiersOrTags.Contains(requirement.IdentifierOrTag));

                    if (!alreadyHasGetItem)
                    {
                        // Character is not already fetching this item, add an objective for it
                        var getItemObjective = new AIObjectiveGetItem(
                            character, requirement.IdentifierOrTag, objective.objectiveManager, equip: false)
                        {
                            SpeakIfFails = !CantFindItemsToCarryIdentifier.IsEmpty,
                            CannotFindDialogueIdentifierOverride = CantFindItemsToCarryIdentifier,
                            MinSpeakInterval = 60.0f
                        };

                        // Add condition requirement if there is one
                        if (requirement.ConditionThreshold.HasValue)
                        {
                            var req = requirement; // Capture for closure
                            getItemObjective.ItemFilter = item => req.MeetsCondition(item.ConditionPercentage);
                        }

                        objective.AddSubObjective(getItemObjective);
                    }

                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Finds items to operate based on ItemsToOperate and ItemComponentsToOperate configuration.
        /// Only adds sub-objectives for components that don't already have one.
        /// </summary>
        private void FindItemsToOperate(Character character, AIObjectiveOperateItem objective)
        {
            if (character.Submarine == null) { return; }
            if (ItemsToOperate.Length == 0 || ItemComponentsToOperate.Length == 0) { return; }

            var itemList = character.Submarine.GetItems(alsoFromConnectedSubs: true);

            foreach (var potentialItemToOperate in itemList)
            {
                if (!potentialItemToOperate.HasIdentifierOrTags(ItemsToOperate) || !potentialItemToOperate.IsInteractable(character))
                {
                    continue;
                }

                foreach (var componentToOperate in ItemComponentsToOperate)
                {
                    var component = potentialItemToOperate.Components.FirstOrDefault(c => c.Name == componentToOperate);
                    if (component == null) { continue; }

                    // Check if an OperateItem sub-objective for this component already exists
                    bool alreadyHasOperateItem = objective.SubObjectives.Any(
                        sub => sub is AIObjectiveOperateItem opItem && opItem.Component == component);

                    if (!alreadyHasOperateItem)
                    {
                        objective.AddSubObjective(new AIObjectiveOperateItem(component, character, objective.objectiveManager, option: Identifier.Empty, requireEquip: false));
                    }
                }
            }
        }

        /// <summary>
        /// Sets up the menu (<see cref="CampaignInteractionType"/>) the character opens when interacted with.
        /// </summary>
        private void SetupMenuInteraction(Character character)
        {
            if (GameMain.GameSession?.GameMode is not CampaignMode campaignMode)
            {
                return;
            }

            Identifier? merchantIdentifier = null;
            if (!StoreIdentifier.IsEmpty)
            {
                merchantIdentifier = StoreIdentifier;

                // Create the store in the current location if it doesn't already exist
                if (campaignMode.Map?.CurrentLocation is Location currentLocation)
                {
                    WorkstationStoreInfo.CreateOrConvert(currentLocation, StoreIdentifier);
                }
            }

            // Don't take over an event's conversation, RestoreMenuInteraction gives the menu once it has ended.
            bool eventConversationActive = GameMain.NetworkMember is { IsClient: true }
                ? character.CampaignInteractionType == CampaignMode.InteractionType.Talk
                : HasActiveEventConversation(character);
            if (!eventConversationActive)
            {
                // Any conversation still referenced at this point is from an event that has already ended
                character.ActiveConversation = null;

                campaignMode.AssignNPCMenuInteraction(character, CampaignInteractionType, merchantIdentifier);
            }
#if SERVER
            // Clients make the above decision locally with less information (eg. can't tell a stale Talk interaction apart), so the server's result overrides theirs
            GameNetwork.CreateVanillaEntityEvent(character, new Character.AssignCampaignInteractionEventData());
#endif
        }

        /// <summary>
        /// Clear the menu interaction the workstation set up on the character, but only if it's still there.
        /// Something else (eg. an event's conversation) may have replaced it after the workstation set it up, and that shouldn't be wiped.
        /// </summary>
        private void ClearMenuInteraction(Character character)
        {
            if (GameMain.GameSession?.GameMode is not CampaignMode campaignMode) { return; }

            if (!StoreIdentifier.IsEmpty)
            {
                character.MerchantIdentifier = Identifier.Empty;

                // NOTE: We intentionally do NOT remove the store from the location
                // This ensures that the store's inventory persists even when no NPC is assigned!
                // The store will become accessible again when a new NPC is assigned to this workstation.
            }

            if (character.CampaignInteractionType != CampaignInteractionType)
            {
                return;
            }

            campaignMode.AssignNPCMenuInteraction(character, CampaignMode.InteractionType.None);
        }

        /// <summary>
        /// Gives the menu interaction back to an assigned character if some event conversation replaced it but is now gone.
        /// </summary>
        private void RestoreMenuInteraction(Character character)
        {
            if (GameMain.NetworkMember is { IsClient: true }) { return; }
            if (!ConversationEventIdentifier.IsEmpty || CampaignInteractionType == CampaignMode.InteractionType.None) { return; }

            if (HasActiveEventConversation(character))
            {
                return;
            }

            // Now we know any talk interaction whose event has already ended is stale and shouldn't block setting up the new interaction
            if (character.CampaignInteractionType is not (CampaignMode.InteractionType.None or CampaignMode.InteractionType.Talk))
            {
                return;
            }

            SetupMenuInteraction(character);
        }

        /// <summary>
        /// Is the character a speaker in an event conversation that's still running?
        /// Note: Only known locally in SP or on the server in MP.
        /// </summary>
        private static bool HasActiveEventConversation(Character character)
        {
            return
                character.ActiveConversation?.ParentEvent is { IsFinished: false } parentEvent &&
                GameMain.GameSession?.EventManager is { } eventManager &&
                eventManager.ActiveEvents.Contains(parentEvent);
        }

        /// <summary>
        /// Starts the ScriptedEvent that holds this workstation's role-specific dialogue and registers the character
        /// as a speaker the ConversationActions in the event look for.
        /// <br/><br/>
        /// Each workstation (not just workstation type) has its own conversation that can only be on one dweller at a
        /// time and accessed by one player at a time. If workstation assignees change, the conversation moves to another
        /// one as long as there are assignees left.
        /// </summary>
        private void SetupConversationInteraction(Character character)
        {
            if (GameMain.GameSession is not { } session) { return; }

            if (GameMain.NetworkMember is { IsClient: true }) { return; }

            if (ConversationSpeakerTag.IsEmpty)
            {
                DebugConsole.ThrowError($"The workstation '{item.Prefab.Identifier}' has a {nameof(ConversationEventIdentifier)} but no {nameof(ConversationSpeakerTag)}, the conversation would never find a speaker.");
                return;
            }

            // The event is shared by all the NPCs assigned to this workstation, so only start it if it's not already running
            if (conversationEvent is not { IsFinished: false } || !session.EventManager.ActiveEvents.Contains(conversationEvent))
            {
                conversationEvent = null;

                if (EventSet.GetEventPrefab(ConversationEventIdentifier) is not EventPrefab eventPrefab)
                {
                    DebugConsole.ThrowError($"Could not find the EventPrefab '{ConversationEventIdentifier}' configured for the workstation '{item.Prefab.Identifier}'.");
                    return;
                }

                if (eventPrefab.CreateInstance(session.EventManager.RandomSeed) is not ScriptedEvent scriptedEvent)
                {
                    DebugConsole.ThrowError($"The event '{ConversationEventIdentifier}' configured for the workstation '{item.Prefab.Identifier}' is not a ScriptedEvent.");
                    return;
                }

                session.EventManager.ActivateEvent(scriptedEvent);
                conversationEvent = scriptedEvent;
            }

            conversationEvent.AddTarget(ConversationSpeakerTag, character);

            if (RegisterSpeakerAsOutpostNPC && Level.Loaded?.StartOutpost is { } startOutpost)
            {
                startOutpost.Info.ClearOutpostNPCIdentifierOrTag(ConversationSpeakerTag);
                startOutpost.Info.AddOutpostNPCIdentifierOrTag(character, ConversationSpeakerTag);
            }
        }

        /// <summary>
        /// A bit of a jury-rig: the outpost manager's speaker tag is also registered outpost-wide, so vanilla events that
        /// look for 'outpostmanager' (eg. missions) can find them. The speaker tag isn't the right place to decide which
        /// NPCs other events target, so this is an exception that *should not* be extended. If other roles ever need outpost
        /// NPC tags, we should turn this into a dedicated attribute (instead of riding on ConversationSpeakerTag).
        /// </summary>
        private bool RegisterSpeakerAsOutpostNPC => ConversationSpeakerTag == "outpostmanager";

        /// <summary>
        /// Unregisters the character as a speaker and clears any talk interaction related to the conversation on them.
        /// The event itself only stops when there are no dwellers assigned to that particular workstation anymore.
        /// </summary>
        private void ClearConversationInteraction(Character character)
        {
            conversationEvent?.RemoveTarget(ConversationSpeakerTag, character);
            if (RegisterSpeakerAsOutpostNPC)
            {
                Level.Loaded?.StartOutpost?.Info.RemoveOutpostNPCIdentifierOrTag(character, ConversationSpeakerTag);
            }

            // Only clear the talk interaction if it belongs to this workstation's conversation, the NPC can have others as well
            if (conversationEvent != null && character.ActiveConversation?.ParentEvent == conversationEvent)
            {
                // Finishing the event doesn't reset its actions, so the talk interaction has to be cleared manually
                if (GameMain.GameSession?.GameMode is CampaignMode campaignMode)
                {
                    campaignMode.AssignNPCMenuInteraction(character, CampaignMode.InteractionType.None);
                }
                character.SetCustomInteract(null, null);
                character.ActiveConversation = null;
#if SERVER
                GameNetwork.CreateVanillaEntityEvent(character, new Character.AssignCampaignInteractionEventData());
#endif
            }

            // Stop the event once it has no speakers left.
            if (conversationEvent != null && GetAssignedCharacters.None())
            {
                conversationEvent.Finish();
                conversationEvent = null;
            }
        }

        /// <summary>
        /// Recalculates and stores the average skill level of all currently assigned characters in the resource generator,
        /// so it is available for resource production calculations even when away and characters aren't loaded.
        /// </summary>
        private void UpdateResourceGeneratorSkillLevel()
        {
            if (ResourceGenerator.SkillIdentifier.IsEmpty)
            {
                return;
            }

            if (assignedObjectives.Count == 0)
            {
                ResourceGenerator.SetAssignedSkillLevel(0f);
                return;
            }

            float totalSkill = 0f;
            foreach (Character character in assignedObjectives.Keys)
            {
                totalSkill += character.GetSkillLevel(ResourceGenerator.SkillIdentifier);
            }
            ResourceGenerator.SetAssignedSkillLevel(totalSkill / assignedObjectives.Count);
        }

        /// <summary>
        /// Sets the idle behavior for a character assigned to this workstation.
        /// The character will stay in the workstation's hull when idle, rather than wandering the outpost.
        /// Navigation to the workstation is handled by the operate order itself.
        /// </summary>
        private void SetIdleBehaviorNearWorkstation(Character character)
        {
            if (character.AIController is not HumanAIController humanAI) { return; }

            if (item.CurrentHull is Hull hull &&
                humanAI.ObjectiveManager.GetObjective<AIObjectiveIdle>() is AIObjectiveIdle idleObjective)
            {
                idleObjective.TargetHull = hull;
                idleObjective.Behavior = AIObjectiveIdle.BehaviorType.StayInHull;
            }
        }
    }
}
