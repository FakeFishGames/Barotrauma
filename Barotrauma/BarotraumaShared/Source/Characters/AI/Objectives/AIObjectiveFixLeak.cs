using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveFixLeak : AIObjective
    {
        public override string DebugTag => "fix leak";

        public override bool KeepDivingGearOn => true;
        public override bool ForceRun => true;

        private readonly Gap leak;

        private AIObjectiveFindDivingGear findDivingGear;
        private AIObjectiveGoTo gotoObjective;
        private AIObjectiveOperateItem operateObjective;
        
        public Gap Leak
        {
            get { return leak; }
        }

        public AIObjectiveFixLeak(Gap leak, Character character, float priorityModifier = 1) : base (character, "", priorityModifier)
        {
            this.leak = leak;
        }

        public override bool IsCompleted()
        {
            return leak.Open <= 0.0f || leak.Removed;
        }

        public override bool CanBeCompleted => !abandon && base.CanBeCompleted;

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (leak.Open == 0.0f) { return 0.0f; }

            //float leakSize = (leak.IsHorizontal ? leak.Rect.Height : leak.Rect.Width) * Math.Max(leak.Open, 0.1f);
            //float dist = Vector2.DistanceSquared(character.SimPosition, leak.SimPosition);
            //dist = Math.Max(dist / 100.0f, 1.0f);
            //return Math.Min(leakSize / dist, 40.0f);

            float leakSize = (leak.IsHorizontal ? leak.Rect.Height : leak.Rect.Width) * Math.Max(leak.Open, 0.1f);
            float dist = Vector2.DistanceSquared(character.SimPosition, leak.SimPosition);
            dist = Math.Max(dist / 100.0f, 1.0f);
            float maxMultiplier = MathHelper.Min(PriorityModifier, 1);
            float max = MathHelper.Min((AIObjectiveManager.OrderPriority + 20) * maxMultiplier, 90);
            return MathHelper.Clamp(Priority + leakSize / dist, 0, max);
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveFixLeak fixLeak = otherObjective as AIObjectiveFixLeak;
            if (fixLeak == null) return false;
            return fixLeak.leak == leak;
        }

        protected override void Act(float deltaTime)
        {
            if (!leak.IsRoomToRoom)
            {
                if (findDivingGear == null)
                {
                    findDivingGear = new AIObjectiveFindDivingGear(character, true);
                    AddSubObjective(findDivingGear);
                }
                else if (!findDivingGear.CanBeCompleted)
                {
                    abandon = true;
                    return;
                }
            }

            var weldingTool = character.Inventory.FindItemByTag("weldingtool");

            if (weldingTool == null)
            {
                AddSubObjective(new AIObjectiveGetItem(character, "weldingtool", true));
                return;
            }
            else
            {
                var containedItems = weldingTool.ContainedItems;
                if (containedItems == null) return;
                
                var fuelTank = containedItems.FirstOrDefault(i => i.HasTag("weldingfueltank") && i.Condition > 0.0f);
                if (fuelTank == null)
                {
                    AddSubObjective(new AIObjectiveContainItem(character, "weldingfueltank", weldingTool.GetComponent<ItemContainer>()));
                    return;
                }
            }

            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null) { return; }

            Vector2 gapDiff = leak.WorldPosition - character.WorldPosition;

            // TODO: use the collider size/reach?
            if (!character.AnimController.InWater && Math.Abs(gapDiff.X) < 100 && gapDiff.Y < 0.0f && gapDiff.Y > -150)
            {
                HumanAIController.AnimController.Crouching = true;
            }

            //float reach = HumanAIController.AnimController.ArmLength + ConvertUnits.ToSimUnits(repairTool.Range);
            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool cannotReach = ConvertUnits.ToSimUnits(gapDiff.Length()) > reach;
            if (cannotReach)
            {
                if (gotoObjective != null)
                {
                    // Check if the objective is already removed -> completed/impossible
                    if (!subObjectives.Contains(gotoObjective))
                    {
                        if (!gotoObjective.CanBeCompleted)
                        {
                            abandon = true;
                        }
                        gotoObjective = null;
                        return;
                    }
                }
                else
                {
                    gotoObjective = new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character)
                    {
                        CloseEnough = reach
                    };
                    if (!subObjectives.Contains(gotoObjective))
                    {
                        AddSubObjective(gotoObjective);
                    }
                }
            }
            if (gotoObjective == null || gotoObjective.IsCompleted())
            {
                if (operateObjective == null)
                {
                    operateObjective = new AIObjectiveOperateItem(repairTool, character, "", true, leak);
                    AddSubObjective(operateObjective);
                }
                else if (!subObjectives.Contains(operateObjective))
                {
                    operateObjective = null;
                }
            }   
        }

        private Vector2 GetStandPosition()
        {
            Vector2 standPos = leak.Position;
            var hull = leak.FlowTargetHull;

            if (hull == null) return standPos;
            
            if (leak.IsHorizontal)
            {
                standPos += Vector2.UnitX * Math.Sign(hull.Position.X - leak.Position.X) * leak.Rect.Width;
            }
            else
            {
                standPos += Vector2.UnitY * Math.Sign(hull.Position.Y - leak.Position.Y) * leak.Rect.Height;
            }

            return standPos;            
        }
    }
}
