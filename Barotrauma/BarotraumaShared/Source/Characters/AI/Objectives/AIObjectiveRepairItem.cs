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

        public override bool KeepDivingGearOn => true;

        public Item Item { get; private set; }

        private AIObjectiveGoTo goToObjective;

        private float previousCondition = -1;

        public AIObjectiveRepairItem(Character character, Item item, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier)
        {
            Item = item;
        }

        public override float GetPriority()
        {
            // TODO: priority list?
            if (Item.Repairables.None()) { return 0; }
            // Ignore items that are being repaired by someone else.
            if (Item.Repairables.Any(r => r.CurrentFixer != null && r.CurrentFixer != character)) { return 0; }
            if (Item.CurrentHull != null && (Item.CurrentHull.FireSources.Count > 0 || Character.CharacterList.Any(c => c.CurrentHull == Item.CurrentHull && !HumanAIController.IsFriendly(c)))) { return 0; }
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

        public override bool CanBeCompleted => !abandon;

        public override bool IsCompleted()
        {
            bool isCompleted = Item.IsFullCondition;
            if (isCompleted)
            {
                character?.Speak(TextManager.Get("DialogItemRepaired").Replace("[itemname]", Item.Name), null, 0.0f, "itemrepaired", 10.0f);
            }
            return isCompleted;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectiveRepairItem repairObjective && repairObjective.Item == Item;
        }

        protected override void Act(float deltaTime)
        {
            if (goToObjective != null && !subObjectives.Contains(goToObjective))
            {
                if (!goToObjective.IsCompleted() && !goToObjective.CanBeCompleted)
                {
                    abandon = true;
                    // TODO: Add: "Can't repair [item]!"
                    //character?.Speak(TextManager.Get("DialogCannotRepair").Replace("[itemname]", Item.Name), null, 0.0f, "cannotrepair", 10.0f);
                }
                goToObjective = null;
            }
            if (!abandon)
            {
                // Don't allow to repair items in rooms that have a fire or an enemy inside
                abandon = Item.CurrentHull != null && (Item.CurrentHull.FireSources.Count > 0 || Character.CharacterList.Any(c => c.CurrentHull == Item.CurrentHull && !HumanAIController.IsFriendly(c)));
            }
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
            if (repairTool == null)
            {
                FindRepairTool();
            }
            if (character.CanInteractWith(Item))
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
                            // TODO: Add: "Can't repair [item]!"
                            //character?.Speak(TextManager.Get("DialogCannotRepair").Replace("[itemname]", Item.Name), null, 0.0f, "cannotrepair", 10.0f);
                        }
                    }
                    repairable.CurrentFixer = abandon && repairable.CurrentFixer == character ? null : character;
                    break;
                }
            }
            else if (goToObjective == null || goToObjective.Target != Item)
            {
                previousCondition = -1;
                if (goToObjective != null)
                {
                    subObjectives.Remove(goToObjective);
                }
                goToObjective = new AIObjectiveGoTo(Item, character, objectiveManager);
                if (repairTool != null)
                {
                    //goToObjective.CloseEnough = (HumanAIController.AnimController.ArmLength + ConvertUnits.ToSimUnits(repairTool.Range)) * 0.75f;
                    goToObjective.CloseEnough = ConvertUnits.ToSimUnits(repairTool.Range);
                }
                AddSubObjective(goToObjective);
            }
        }

        private RepairTool repairTool;
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
