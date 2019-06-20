using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using Barotrauma.Extensions;
using FarseerPhysics;

namespace Barotrauma
{
    class AIObjectiveRepairItem : AIObjective
    {
        public override string DebugTag => "repair item";

        public Item Item { get; private set; }

        private AIObjectiveGoTo goToObjective;
        private AIObjectiveContainItem refuelObjective;
        private float previousCondition = -1;
        private RepairTool repairTool;

        public AIObjectiveRepairItem(Character character, Item item, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier)
        {
            Item = item;
        }

        public override float GetPriority()
        {
            // TODO: priority list?
            // Ignore items that are being repaired by someone else.
            if (Item.Repairables.Any(r => r.CurrentFixer != null && r.CurrentFixer != character)) { return 0; }
            // Vertical distance matters more than horizontal (climbing up/down is harder than moving horizontally)
            float dist = Math.Abs(character.WorldPosition.X - Item.WorldPosition.X) + Math.Abs(character.WorldPosition.Y - Item.WorldPosition.Y) * 2.0f;
            float distanceFactor = MathHelper.Lerp(1, 0.5f, MathUtils.InverseLerp(0, 10000, dist));
            float damagePriority = MathHelper.Lerp(1, 0, Item.Condition / Item.MaxCondition);
            float successFactor = MathHelper.Lerp(0, 1, Item.Repairables.Average(r => r.DegreeOfSuccess(character)));
            float isSelected = character.SelectedConstruction == Item ? 50 : 0;
            float devotion = (Math.Min(Priority, 10) + isSelected) / 100;
            float max = MathHelper.Min(AIObjectiveManager.OrderPriority - 1, 90);
            return MathHelper.Lerp(0, max, MathHelper.Clamp(devotion + damagePriority * distanceFactor * successFactor * PriorityModifier, 0, 1));
        }

        public override bool IsCompleted()
        {
            bool isCompleted = Item.IsFullCondition;
            if (isCompleted)
            {                
                character?.Speak(TextManager.GetWithVariable("DialogItemRepaired", "[itemname]", Item.Name, true), null, 0.0f, "itemrepaired", 10.0f);
            }
            return isCompleted;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectiveRepairItem repairObjective && repairObjective.Item == Item;
        }

        protected override void Act(float deltaTime)
        {
            foreach (Repairable repairable in Item.Repairables)
            {
                if (!repairable.HasRequiredItems(character, false))
                {
                    //make sure we have all the items required to fix the target item
                    foreach (var kvp in repairable.requiredItems)
                    {
                        foreach (RelatedItem requiredItem in kvp.Value)
                        {
                            AddSubObjective(new AIObjectiveGetItem(character, requiredItem.Identifiers, objectiveManager, true));
                        }
                    }
                    return;
                }
            }
            // Only continue when the get item sub objectives have been completed.
            if (subObjectives.Any()) { return; }
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
                    DebugConsole.ThrowError("AIObjectiveRepairItem failed - the item \"" + repairTool + "\" has no proper inventory");
#endif
                    abandon = true;
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
                    TryAddSubObjective(ref refuelObjective, () => new AIObjectiveContainItem(character, item.Identifiers, repairTool.Item.GetComponent<ItemContainer>(), objectiveManager));
                    return;
                }
            }
            if (character.CanInteractWith(Item, out _, checkLinked: false))
            {
                if (repairTool != null)
                {
                    OperateRepairTool(deltaTime);
                }
                foreach (Repairable repairable in Item.Repairables)
                {
                    if (repairable.CurrentFixer != null && repairable.CurrentFixer != character)
                    {
                        // Someone else is repairing the target. Abandon the objective if the other is better at this then us.
                        abandon = repairable.DegreeOfSuccess(character) < repairable.DegreeOfSuccess(repairable.CurrentFixer);
                    }
                    if (!abandon)
                    {
                        if (character.SelectedConstruction != Item)
                        {
                            Item.TryInteract(character, true, true);
                        }
                        if (previousCondition == -1)
                        {
                            previousCondition = Item.Condition;
                        }
                        else if (Item.Condition < previousCondition)
                        {
                            // If the current condition is less than the previous condition, we can't complete the task, so let's abandon it. The item is probably deteriorating at a greater speed than we can repair it.
                            abandon = true;
                            character?.Speak(TextManager.GetWithVariable("DialogCannotRepair", "[itemname]", Item.Name, true), null, 0.0f, "cannotrepair", 10.0f);
                        }
                    }
                    repairable.CurrentFixer = abandon && repairable.CurrentFixer == character ? null : character;
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
                        var objective = new AIObjectiveGoTo(Item, character, objectiveManager);
                        if (repairTool != null)
                        {
                            objective.CloseEnough = repairTool.Range * 0.75f;
                        }
                        return objective;
                    },                    
                    onAbandon: () => character.Speak(TextManager.GetWithVariable("DialogCannotRepair", "[itemname]", Item.Name, true), null, 0.0f, "cannotrepair", 10.0f));
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
