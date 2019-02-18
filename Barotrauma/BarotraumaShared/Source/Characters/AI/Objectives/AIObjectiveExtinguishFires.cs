using System.Linq;
using System.Collections.Generic;

namespace Barotrauma
{
    class AIObjectiveExtinguishFires : AIObjective
    {
        public override string DebugTag => "extinguish fires";
        private List<Hull> hullList = new List<Hull>();

        public AIObjectiveExtinguishFires(Character character) : 
            base(character, "")
        {
            if (character.Submarine != null)
            {
                var connectedSubs = character.Submarine.GetConnectedSubs();
                hullList = Hull.hullList.Where(h => h.Submarine == character.Submarine || connectedSubs.Any(s => s == h.Submarine)).ToList();
            }
            if (!hullList.Any(h => h.FireSources.Count > 0))
            {
                character?.Speak(TextManager.Get("DialogNoFire"), null, 3.0f, "nofire", 30.0f);
            }
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (objectiveManager.CurrentObjective == this)
            {
                return AIObjectiveManager.OrderPriority;
            }

            return hullList.Count(h => h.FireSources.Count > 0) * 10;
        }

        public override bool IsCompleted()
        {
            return !hullList.Any(h => h.FireSources.Count > 0);
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectiveExtinguishFires;
        }

        protected override void Act(float deltaTime)
        {
            foreach (Hull hull in hullList)
            {
                if (hull.FireSources.Count > 0)
                {
                    AddSubObjective(new AIObjectiveExtinguishFire(character, hull));
                }
            }
        }
    }
}
