using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    class AIObjectiveFixLeak : AIObjective
    {
        private Gap leak;

        public Gap Leak
        {
            get { return leak; }
        }

        public AIObjectiveFixLeak(Gap leak, Character character)
            : base (character, "")
        {
            this.leak = leak;
        }

        public override float GetPriority(Character character)
        {
            if (leak.Open == 0.0f) return 0.0f;

            float leakSize = (leak.isHorizontal ? leak.Rect.Height : leak.Rect.Width) * Math.Max(leak.Open, 0.1f);

            float dist = Vector2.DistanceSquared(character.SimPosition, leak.SimPosition);
            dist = Math.Max(dist/100.0f, 1.0f);
            return Math.Min(leakSize/dist, 40.0f);
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveFixLeak fixLeak = otherObjective as AIObjectiveFixLeak;
            if (fixLeak == null) return false;
            return fixLeak.leak == leak;
        }

        protected override void Act(float deltaTime)
        {
            var weldingTool = character.Inventory.FindItem("Welding Tool");

            if (weldingTool == null)
            {
                subObjectives.Add(new AIObjectiveGetItem(character, "Welding Tool", true));
                return;
            }
            else
            {
                var containedItems = weldingTool.ContainedItems;
                if (containedItems == null) return;
                
                var fuelTank = Array.Find(containedItems, i => i.Name == "Welding Fuel Tank" && i.Condition > 0.0f);

                if (fuelTank == null)
                {
                    AddSubObjective(new AIObjectiveContainItem(character, "Welding Fuel Tank", weldingTool.GetComponent<ItemContainer>()));
                }
            }

            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null) return;

            if (Vector2.Distance(character.WorldPosition, leak.WorldPosition) > 300.0f)
            {
                AddSubObjective(new AIObjectiveGoTo(ConvertUnits.ToSimUnits(GetStandPosition()), character));
            }
            else
            {
                AddSubObjective(new AIObjectiveOperateItem(repairTool, character, "", leak));
            }            
        }

        private Vector2 GetStandPosition()
        {
            Vector2 standPos = leak.Position;
            var hull = leak.FlowTargetHull;

            if (hull == null) return standPos;
            
            if (leak.isHorizontal)
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
