using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    class AIObjectiveFixLeak : AIObjective
    {
        public override string DebugTag => "fix leak";

        private readonly Gap leak;

        private AIObjectiveGoTo gotoObjective;
        private AIObjectiveOperateItem operateObjective;

        private bool pathUnreachable;
        
        public Gap Leak
        {
            get { return leak; }
        }

        public AIObjectiveFixLeak(Gap leak, Character character)
            : base (character, "")
        {
            this.leak = leak;
        }

        public override bool IsCompleted()
        {
            return leak.Open <= 0.0f || leak.Removed || pathUnreachable;
        }

        public override bool CanBeCompleted => !pathUnreachable && base.CanBeCompleted;

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (leak.Open == 0.0f) { return 0.0f; }
            if (pathUnreachable) { return 0.0f; }

            float leakSize = (leak.IsHorizontal ? leak.Rect.Height : leak.Rect.Width) * Math.Max(leak.Open, 0.1f);

            float dist = Vector2.DistanceSquared(character.SimPosition, leak.SimPosition);
            dist = Math.Max(dist / 100.0f, 1.0f);
            return Math.Min(leakSize / dist, 40.0f);
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveFixLeak fixLeak = otherObjective as AIObjectiveFixLeak;
            if (fixLeak == null) return false;
            return fixLeak.leak == leak;
        }

        protected override void Act(float deltaTime)
        {
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
                
                var fuelTank = Array.Find(containedItems, i => i.HasTag("weldingfueltank") && i.Condition > 0.0f);
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
            if (!character.AnimController.InWater && character.AnimController is HumanoidAnimController humanoidController && 
                Math.Abs(gapDiff.X) < 100 && gapDiff.Y < 0.0f && gapDiff.Y > -150)
            {
                ((HumanoidAnimController)character.AnimController).Crouching = true;
            }

            if (gotoObjective != null)
            {
                // Check if the objective is already removed -> completed/impossible
                if (!subObjectives.Contains(gotoObjective))
                {
                    gotoObjective = null;
                }
            }
            else
            {
                var objective = new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character);
                if (!objective.IsCompleted())
                {
                    pathUnreachable = !objective.CanBeCompleted;
                    if (!pathUnreachable)
                    {
                        AddSubObjective(objective);
                        gotoObjective = objective;
                    }
                }
                else
                {
                    gotoObjective = null;
                }
            }
            if (gotoObjective == null)
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
