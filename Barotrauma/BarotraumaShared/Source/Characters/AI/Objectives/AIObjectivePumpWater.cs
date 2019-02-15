using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class AIObjectivePumpWater : AIObjective
    {
        public override string DebugTag => "pump water";

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
        }

        private void FindPumps()
        {
            lastFindPumpsTime = (float)Timing.TotalTime;

            pumps = new List<Pump>();
            foreach (Item item in Item.ItemList)
            {
                //don't attempt to use pumps outside the sub
                if (item.Submarine == null) { continue; }

                var pump = item.GetComponent<Pump>();
                if (pump == null) continue;

                if (item.HasTag("ballast")) continue;
                
                //if the pump is connected to an item with a steering component, it must be a ballast pump
                //(This may not work correctly if the signals are passed through some fancy circuit or a wifi component,
                //which is why sub creators are encouraged to tag the ballast pumps)
                bool connectedToSteering = false;
                foreach (Connection c in item.Connections)
                {
                    if (c.IsPower) continue;
                    if (item.GetConnectedComponentsRecursive<Steering>(c).Count > 0)
                    {
                        connectedToSteering = true;
                        break;
                    }
                }
                if (connectedToSteering) continue;

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


            foreach (Pump pump in pumps)
            {
                AddSubObjective(new AIObjectiveOperateItem(pump, character, orderOption, false));
            }
        }
    }
}
