using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectivePumpWater : AIObjective
    {
        public override string DebugTag => "pump water";

        private const float findPumpsInterval = 5.0f;

        private string orderOption;
        private readonly Dictionary<Pump, AIObjectiveOperateItem> objectives = new Dictionary<Pump, AIObjectiveOperateItem>();
        private readonly List<Pump> targetPumps = new List<Pump>();
        private readonly List<Pump> availablePumps;
        private float findPumpsTimer;

        public AIObjectivePumpWater(Character character, string option) : base(character, option)
        {
            orderOption = option;
            var allPumps = character.Submarine.GetItems(true).Select(i => i.GetComponent<Pump>()).Where(p => p != null);
            availablePumps = allPumps.Where(p => !p.Item.HasTag("ballast") && p.Item.Connections.None(c => c.IsPower && p.Item.GetConnectedComponentsRecursive<Steering>(c).None())).ToList();
        }

        public override void UpdatePriority(AIObjectiveManager objectiveManager, float deltaTime)
        {
            if (findPumpsTimer < findPumpsInterval)
            {
                findPumpsTimer += deltaTime;
            }
            else
            {
                FindPumps();
            }
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (objectiveManager.CurrentOrder == this && targetPumps.Count > 0)
            {
                return AIObjectiveManager.OrderPriority;
            }
            return 0.0f;
        }

        public override bool IsCompleted() => false;
        public override bool CanBeCompleted => true;

        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectivePumpWater;

        protected override void Act(float deltaTime)
        {
            SyncRemovedObjectives(objectives, targetPumps);
            foreach (Pump pump in targetPumps)
            {
                if (!objectives.TryGetValue(pump, out AIObjectiveOperateItem obj))
                {
                    obj = new AIObjectiveOperateItem(pump, character, orderOption, false);
                    AddSubObjective(obj);
                }
            }
        }

        private void FindPumps()
        {
            findPumpsTimer = 0;
            targetPumps.Clear();
            foreach (Pump pump in availablePumps)
            {
                if (orderOption.ToLowerInvariant() == "stop pumping")
                {
                    if (!pump.IsActive || pump.FlowPercentage == 0.0f) continue;
                }
                else
                {
                    if (!pump.Item.InWater) continue;
                    if (pump.IsActive && pump.FlowPercentage <= -90.0f) continue;
                }
                targetPumps.Add(pump);
            }
        }
    }
}
