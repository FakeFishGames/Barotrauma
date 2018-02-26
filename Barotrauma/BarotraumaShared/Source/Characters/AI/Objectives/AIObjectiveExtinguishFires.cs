using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveExtinguishFires : AIObjective
    {
        public AIObjectiveExtinguishFires(Character character) : 
            base(character, "")
        {
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (objectiveManager.CurrentObjective == this)
            {
                return AIObjectiveManager.OrderPriority;
            }

            return Hull.hullList.Count(h => h.FireSources.Count > 0) * 10;
        }

        public override bool IsCompleted()
        {
            return !Hull.hullList.Any(h => h.FireSources.Count > 0);
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectiveExtinguishFires;
        }

        protected override void Act(float deltaTime)
        {
            foreach (Hull hull in Hull.hullList)
            {
                if (hull.FireSources.Count > 0)
                {
                    AddSubObjective(new AIObjectiveExtinguishFire(character, hull));
                }
            }
        }
    }
}
