using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveRepairItem : AIObjective
    {
        public override Identifier Identifier { get; set; } = "repair item".ToIdentifier();

        public override bool AllowInAnySub => true;
        public override bool KeepDivingGearOn => Item?.CurrentHull == null;

        public Item Item { get; private set; }

        private AIObjectiveGoTo goToObjective;
        private AIObjectiveContainItem refuelObjective;
        private float previousCondition = -1;
        private RepairTool repairTool;

        private const float WaitTimeBeforeRepair = 0.5f;
        private float waitTimer;

        private bool IsRepairing() => IsRepairing(character, Item);
        private readonly bool isPriority;

        public static bool IsRepairing(Character character, Item item) => character.SelectedItem == item && item.Repairables.Any(r => r.CurrentFixer == character);

        public AIObjectiveRepairItem(Character character, Item item, AIObjectiveManager objectiveManager, float priorityModifier = 1, bool isPriority = false)
            : base(character, objectiveManager, priorityModifier)
        {
            Item = item;
            this.isPriority = isPriority;
        }

        protected override float GetPriority()
        {
            if (!IsAllowed || Item.IgnoreByAI(character))
            {
                Priority = 0;
                Abandon = true;
                if (IsRepairing())
                {
                    Item.Repairables.ForEach(r => r.StopRepairing(character));
                }
                return Priority;
            }
            if (HumanAIController.IsItemRepairedByAnother(Item, out _))
            {
                Priority = 0;
                IsCompleted = true;
            }
            else if (Item.IsClaimedByBallastFlora)
            {
                Priority = 0;
            }
            else
            {
                float distanceFactor = 1;
                if (!isPriority && Item.CurrentHull != character.CurrentHull)
                {
                    float yDist = Math.Abs(character.WorldPosition.Y - Item.WorldPosition.Y);
                    yDist = yDist > 100 ? yDist * 5 : 0;
                    float dist = Math.Abs(character.WorldPosition.X - Item.WorldPosition.X) + yDist;
                    distanceFactor = MathHelper.Lerp(1, 0.25f, MathUtils.InverseLerp(0, 4000, dist));
                }
                float requiredSuccessFactor = objectiveManager.HasOrder<AIObjectiveRepairItems>() ? 0 : AIObjectiveRepairItems.RequiredSuccessFactor;
                float severity = isPriority ? 1 : AIObjectiveRepairItems.GetTargetPriority(Item, character, requiredSuccessFactor) / 100;
                bool isSelected = IsRepairing();
                float selectedBonus = isSelected ? 100 - MaxDevotion : 0;
                float devotion = (CumulatedDevotion + selectedBonus) / 100;
                float reduction = isPriority ? 1 : isSelected ? 2 : 3;
                float max = AIObjectiveManager.LowestOrderPriority - reduction;
                float highestWeight = -1;
                foreach (Identifier tag in Item.Prefab.Tags)
                {
                    if (JobPrefab.ItemRepairPriorities.TryGetValue(tag, out float weight) && weight > highestWeight)
                    {
                        highestWeight = weight;
                    }
                }
                if (highestWeight == -1)
                {
                    // Predefined weight not found.
                    highestWeight = 1;
                }
                Priority = MathHelper.Lerp(0, max, MathHelper.Clamp(devotion + (severity * distanceFactor * highestWeight * PriorityModifier), 0, 1));
            }
            return Priority;
        }

        protected override bool CheckObjectiveSpecific()
        {
            IsCompleted = Item.IsFullCondition;
            if (character.IsOnPlayerTeam && IsCompleted && IsRepairing())
            {
                character.Speak(TextManager.GetWithVariable("DialogItemRepaired", "[itemname]", Item.Name, FormatCapitals.Yes).Value, null, 0.0f, "itemrepaired".ToIdentifier(), 10.0f);
            }
            return IsCompleted;
        }

        protected override void Act(float deltaTime)
        {
            // Only continue when the get item sub objectives have been completed.
            if (subObjectives.Any()) { return; }
            foreach (Repairable repairable in Item.Repairables)
            {
                if (!repairable.HasRequiredItems(character, false))
                {
                    //make sure we have all the items required to fix the target item
                    foreach (var kvp in repairable.requiredItems)
                    {
                        foreach (RelatedItem requiredItem in kvp.Value)
                        {
                            var getItemObjective = new AIObjectiveGetItem(character, requiredItem.Identifiers, objectiveManager, equip: true)
                            {
                                AllowVariants = requiredItem.AllowVariants
                            };
                            if (objectiveManager.IsCurrentOrder<AIObjectiveRepairItems>())
                            {
                                if (character.IsOnPlayerTeam)
                                {
                                    getItemObjective.Abandoned += () => character.Speak(TextManager.Get("dialogcannotfindrequireditemtorepair").Value, null, 0.0f, "dialogcannotfindrequireditemtorepair".ToIdentifier(), 10.0f);
                                }
                            }
                            subObjectives.Add(getItemObjective);
                        }
                    }
                    return;
                }
            }
            if (repairTool == null)
            {
                FindRepairTool();
            }
            if (repairTool != null)
            {
                if (repairTool.Item.OwnInventory == null)
                {
#if DEBUG
                    DebugConsole.ThrowError($"{character.Name}: AIObjectiveRepairItem failed - the item \"" + repairTool + "\" has no proper inventory");
#endif
                    Abandon = true;
                    return;
                }
                RelatedItem item = null;
                Item fuel = null;
                foreach (RelatedItem requiredItem in repairTool.requiredItems[RelatedItem.RelationType.Contained])
                {
                    item = requiredItem;
                    fuel = repairTool.Item.OwnInventory.AllItems.FirstOrDefault(it => it.Condition > 0.0f && requiredItem.MatchesItem(it));
                    if (fuel != null) { break; }
                }
                if (fuel == null)
                {
                    RemoveSubObjective(ref goToObjective);
                    TryAddSubObjective(ref refuelObjective, () => new AIObjectiveContainItem(character, item.Identifiers, repairTool.Item.GetComponent<ItemContainer>(), objectiveManager, spawnItemIfNotFound: character.TeamID == CharacterTeamType.FriendlyNPC)
                    {
                        RemoveExisting = true
                    },
                    onCompleted: () => RemoveSubObjective(ref refuelObjective),
                    onAbandon: () => Abandon = true);
                    return;
                }
            }
            if (character.CanInteractWith(Item, out _, checkLinked: false))
            {
                waitTimer += deltaTime;
                if (waitTimer < WaitTimeBeforeRepair) { return; }

                HumanAIController.FaceTarget(Item);
                if (repairTool != null)
                {
                    OperateRepairTool(deltaTime);
                }
                foreach (Repairable repairable in Item.Repairables)
                {
                    if (repairable.CurrentFixer != null && repairable.CurrentFixer != character)
                    {
                        // Someone else is repairing the target. Abandon the objective if the other is better at this than us.
                        Abandon = repairable.CurrentFixer.IsPlayer || repairable.DegreeOfSuccess(character) < repairable.DegreeOfSuccess(repairable.CurrentFixer);
                    }
                    if (!Abandon)
                    {
                        if (character.SelectedItem != Item)
                        {
                            if (Item.TryInteract(character, ignoreRequiredItems: true, forceSelectKey: true) ||
                                Item.TryInteract(character, ignoreRequiredItems: true, forceUseKey: true))
                            {
                                character.SelectedItem = Item;
                            }
                            else
                            {
                                Abandon = true;
                            }
                        }
                        if (previousCondition == -1)
                        {
                            previousCondition = Item.Condition;
                        }
                        else if (Item.Condition < previousCondition)
                        {
                            // If the current condition is less than the previous condition, we can't complete the task, so let's abandon it. The item is probably deteriorating at a greater speed than we can repair it.
                            Abandon = true;
                        }
                    }
                    if (Abandon)
                    {
                        if (character.IsOnPlayerTeam && IsRepairing())
                        {
                            character.Speak(TextManager.GetWithVariable("DialogCannotRepair", "[itemname]", Item.Name, FormatCapitals.Yes).Value, null, 0.0f, "cannotrepair".ToIdentifier(), 10.0f);
                        }
                        repairable.StopRepairing(character);
                    }
                    else if (repairable.CurrentFixer != character)
                    {
                        repairable.StartRepairing(character, Repairable.FixActions.Repair);
                    }
                    break;
                }
            }
            else
            {
                waitTimer = 0.0f;
                RemoveSubObjective(ref refuelObjective);
                // If cannot reach the item, approach it.
                TryAddSubObjective(ref goToObjective,
                    constructor: () =>
                    {
                        previousCondition = -1;
                        var objective = new AIObjectiveGoTo(Item, character, objectiveManager)
                        {
                            TargetName = Item.Name
                        };
                        if (repairTool != null)
                        {
                            objective.CloseEnough = AIObjectiveFixLeak.CalculateReach(repairTool, character);
                        }
                        return objective;
                    },                    
                    onAbandon: () =>
                    {
                        Abandon = true;
                        if (character.IsOnPlayerTeam && IsRepairing())
                        {
                            character.Speak(TextManager.GetWithVariable("DialogCannotRepair", "[itemname]", Item.Name, FormatCapitals.Yes).Value, null, 0.0f, "cannotrepair".ToIdentifier(), 10.0f);
                        }
                    });
            }
        }

        private void FindRepairTool()
        {
            foreach (Repairable repairable in Item.Repairables)
            {
                foreach (var kvp in repairable.requiredItems)
                {
                    foreach (RelatedItem requiredItem in kvp.Value)
                    {
                        foreach (var item in character.Inventory.AllItems)
                        {
                            if (requiredItem.MatchesItem(item))
                            {
                                repairTool = item.GetComponent<RepairTool>();
                            }
                        }
                    }
                }
            }
        }

        private void OperateRepairTool(float deltaTime)
        {
            character.CursorPosition = Item.WorldPosition;
            if (character.Submarine != null)
            {
                character.CursorPosition -= character.Submarine.Position;
            }
            if (repairTool.Item.RequireAimToUse)
            {
                character.SetInput(InputType.Aim, false, true);
            }
            Vector2 fromToolToTarget = Item.Position - repairTool.Item.Position;
            if (fromToolToTarget.LengthSquared() < MathUtils.Pow(repairTool.Range / 2, 2))
            {
                // Too close -> steer away
                character.AIController.SteeringManager.SteeringManual(deltaTime, Vector2.Normalize(character.SimPosition - Item.SimPosition) / 2);
            }
            else
            {
                character.AIController.SteeringManager.Reset();
            }
            if (VectorExtensions.Angle(VectorExtensions.Forward(repairTool.Item.body.TransformedRotation), fromToolToTarget) < MathHelper.PiOver4)
            {
                repairTool.Use(deltaTime, character);
            }
        }

        public override void Reset()
        {
            base.Reset();
            goToObjective = null;
            refuelObjective = null;
            previousCondition = -1;
            repairTool = null;
        }
    }
}
