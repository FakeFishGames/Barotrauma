using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barotrauma
{
    class AIObjectivePumpWater : AIObjective
    {
        private const float FindPumpsInterval = 5.0f;

        private string orderOption;
        private List<Pump> pumps;
        private float lastFindPumpsTime;

        public AIObjectivePumpWater(Character character, string option)
            : base(character, option)
        {
            orderOption = option;
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (Timing.TotalTime >= lastFindPumpsTime + FindPumpsInterval)
            {
                FindPumps();
            }

            if (objectiveManager.CurrentOrder == this && pumps.Count > 0)
            {
                return AIObjectiveManager.OrderPriority;
            }

            return 0.0f;
        }

        public override bool IsCompleted()
        {
            return false;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectivePumpWater;
        }

        protected override void Act(float deltaTime)
        {
            if (Timing.TotalTime < lastFindPumpsTime + FindPumpsInterval)
            {
                return;
            }

            FindPumps();
            foreach (Pump pump in pumps)
            {
                AddSubObjective(new AIObjectiveOperateItem(pump, character, orderOption, false));
            }
        }

        private void FindPumps()
        {
            lastFindPumpsTime = (float)Timing.TotalTime;

            pumps = new List<Pump>();
            foreach (Item item in Item.ItemList)
            {
                var pump = item.GetComponent<Pump>();
                if (pump == null) continue;

                //TODO: figure out if the pump is a ballast pump

                if (orderOption.ToLowerInvariant() == "stop pumping")
                {
                    if (!pump.IsActive || pump.FlowPercentage == 0.0f) continue;
                }
                else
                {
                    if (!pump.Item.InWater) continue;
                    if (pump.IsActive && pump.FlowPercentage <= -90.0f) continue;
                }

                pumps.Add(pump);
            }
        }
    }
}
