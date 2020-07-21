using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveRepairItem : AIObjective
    {
        public override string DebugTag => "repair item";
        public override bool KeepDivingGearOn => true;

        public Item Item { get; private set; }

        private AIObjectiveGoTo goToObjective;
        private AIObjectiveContainItem refuelObjective;
        private float previousCondition = -1;
        private RepairTool repairTool;

        private bool IsRepairing => character.SelectedConstruction == Item && Item.GetComponent<Repairable>()?.CurrentFixer == character;
        private readonly bool isPriority;

        public AIObjectiveRepairItem(Character character, Item item, AIObjectiveManager objectiveManager, float priorityModifier = 1, bool isPriority = false)
            : base(character, objectiveManager, priorityModifier)
        {
            Item = item;
            this.isPriority = isPriority;
        }

        public override float GetPriority()
        {
            if (!IsAllowed)
            {
                Priority = 0;
                return Priority;
            }
            // TODO: priority list?
            // Ignore items that are being repaired by someone else.
            if (Item.Repairables.Any(r => r.CurrentFixer != null && r.CurrentFixer != character))
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
                    distanceFactor = MathHelper.Lerp(1, 0.25f, MathUtils.InverseLerp(0, 5000, dist));
                }
                float severity = isPriority ? 1 : AIObjectiveRepairItems.GetTargetPriority(Item, character, requiredSuccessFactor: objectiveManager.CurrentOrder != this ? AIObjectiveRepairItems.RequiredSuccessFactor : 0);
                float isSelected = IsRepairing ? 50 : 0;
                float devotion = (CumulatedDevotion + isSelected) / 100;
                float max = MathHelper.Min(AIObjectiveManager.OrderPriority - 1, 90);
                Priority = MathHelper.Lerp(0, max, MathHelper.Clamp(devotion + (severity * distanceFactor * PriorityModifier), 0, 1));
            }
            return Priority;
        }

        protected override bool Check()
        {
            IsCompleted = Item.IsFullCondition;
            if (IsCompleted && IsRepairing)
            {
                character?.Speak(TextManager.GetWithVariable("DialogItemRepaired", "[itemname]", Item.Name, true), null, 0.0f, "itemrepaired", 10.0f);
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
                            subObjectives.Add(new AIObjectiveGetItem(character, requiredItem.Identifiers, objectiveManager, true));
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
                var containedItems = repairTool.Item.ContainedItems;
                if (containedItems == null)
                {
#if DEBUG
                    DebugConsole.ThrowError($"{character.Name}: AIObjectiveRepairItem failed - the item \"" + repairTool + "\" has no proper inventory");
#endif
                    Abandon = true;
                    return;
                }
                // Drop empty tanks
                foreach (Item containedItem in containedItems)
                {
                    if (containedItem == null) { continue; }
                    if (containedItem.Condition <= 0.0f)
                    {
                        containedItem.Drop(character);
                    }
                }
                RelatedItem item = null;
                Item fuel = null;
                foreach (RelatedItem requiredItem in repairTool.requiredItems[RelatedItem.RelationType.Contained])
                {
                    item = requiredItem;
                    fuel = containedItems.FirstOrDefault(it => it.Condition > 0.0f && requiredItem.MatchesItem(it));
                    if (fuel != null) { break; }
                }
                if (fuel == null)
                {
                    RemoveSubObjective(ref goToObjective);
                    TryAddSubObjective(ref refuelObjective, () => new AIObjectiveContainItem(character, item.Identifiers, repairTool.Item.GetComponent<ItemContainer>(), objectiveManager), 
                        onCompleted: () => RemoveSubObjective(ref refuelObjective),
                        onAbandon: () => Abandon = true);
                    return;
                }
            }
            if (character.CanInteractWith(Item, out _, checkLinked: false))
            {
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
                        Abandon = repairable.DegreeOfSuccess(character) < repairable.DegreeOfSuccess(repairable.CurrentFixer);
                    }
                    if (!Abandon)
                    {
                        if (character.SelectedConstruction != Item)
                        {
                            if (!Item.TryInteract(character, ignoreRequiredItems: true, forceSelectKey: true) &&
                                !Item.TryInteract(character, ignoreRequiredItems: true, forceActionKey: true))
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
                        if (IsRepairing)
                        {
                            character.Speak(TextManager.GetWithVariable("DialogCannotRepair", "[itemname]", Item.Name, true), null, 0.0f, "cannotrepair", 10.0f);
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
                RemoveSubObjective(ref refuelObjective);
                // If cannot reach the item, approach it.
                TryAddSubObjective(ref goToObjective,
                    constructor: () =>
                    {
                        previousCondition = -1;
                        var objective = new AIObjectiveGoTo(Item, character, objectiveManager)
                        {
                            // Don't stop in ladders, because we can't interact with other items while holding the ladders.
                            endNodeFilter = node => node.Waypoint.Ladders == null
                        };
                        if (repairTool != null)
                        {
                            objective.CloseEnough = repairTool.Range * 0.75f;
                        }
                        return objective;
                    },                    
                    onAbandon: () =>
                    {
                        Abandon = true;
                        if (IsRepairing)
                        {
                            character.Speak(TextManager.GetWithVariable("DialogCannotRepair", "[itemname]", Item.Name, true), null, 0.0f, "cannotrepair", 10.0f);
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
                        foreach (var item in character.Inventory.Items)
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
            character.CursorPosition = Item.Position;
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
    }
}
