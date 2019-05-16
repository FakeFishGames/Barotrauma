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

        public AIObjectiveFixLeak(Gap leak, Character character) : base (character, "")
        {
            Leak = leak;
        }

        public override bool IsCompleted()
        {
            return Leak.Open <= 0.0f || Leak.Removed;
        }

        public override bool CanBeCompleted => !abandon && base.CanBeCompleted;

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (leak.Open == 0.0f) { return 0.0f; }

            float leakSize = (leak.IsHorizontal ? leak.Rect.Height : leak.Rect.Width) * Math.Max(leak.Open, 0.1f);

            float dist = Vector2.DistanceSquared(character.SimPosition, leak.SimPosition);
            dist = Math.Max(dist / 100.0f, 1.0f);
            return Math.Min(leakSize / dist, 40.0f);
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            if (!(otherObjective is AIObjectiveFixLeak fixLeak)) { return false; }
            return fixLeak.Leak == Leak;
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

            float reach = ConvertUnits.ToSimUnits(repairTool.Range);
            bool canReach = ConvertUnits.ToSimUnits(gapDiff.Length()) < reach;
            if (canReach)
            {
                Limb sightLimb = null;
                if (character.Inventory.IsInLimbSlot(repairTool.Item, InvSlotType.RightHand))
                {
                    sightLimb = character.AnimController.GetLimb(LimbType.RightHand);
                }
                else if (character.Inventory.IsInLimbSlot(repairTool.Item, InvSlotType.LeftHand))
                {
                    sightLimb = character.AnimController.GetLimb(LimbType.LeftHand);
                }
                canReach = character.CanSeeTarget(leak, sightLimb);
            }
            else
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
            Vector2 standPos = Leak.Position;
            var hull = Leak.FlowTargetHull;
            if (hull == null) { return standPos; }
            if (Leak.IsHorizontal)
            {
                standPos += Vector2.UnitX * Math.Sign(hull.Position.X - Leak.Position.X) * Leak.Rect.Width;
            }
            else
            {
                standPos += Vector2.UnitY * Math.Sign(hull.Position.Y - Leak.Position.Y) * Leak.Rect.Height;
            }
            return standPos;            
        }
    }
}
