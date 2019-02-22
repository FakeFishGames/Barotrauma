using System.Linq;
using System.Collections.Generic;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveExtinguishFires : AIObjective
    {
        public override string DebugTag => "extinguish fires";
        private List<Hull> hullList = new List<Hull>();
        private Dictionary<Hull, AIObjectiveExtinguishFire> extinguishObjectives = new Dictionary<Hull, AIObjectiveExtinguishFire>();

        public AIObjectiveExtinguishFires(Character character) : 
            base(character, "")
        {
            if (character.Submarine != null)
            {
                var connectedSubs = character.Submarine.GetConnectedSubs();
                hullList = Hull.hullList.Where(h => h.Submarine == character.Submarine || connectedSubs.Any(s => s == h.Submarine)).ToList();
            }
            if (IsCompleted())
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
            return hullList.None(h => h.FireSources.Count > 0);
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectiveExtinguishFires;
        }

        protected override void Act(float deltaTime)
        {
            SyncRemovedObjectives(extinguishObjectives, hullList);
            foreach (Hull hull in hullList)
            {
                if (hull.FireSources.Count > 0 && !extinguishObjectives.TryGetValue(hull, out AIObjectiveExtinguishFire objective))
                {
                    objective = new AIObjectiveExtinguishFire(character, hull);
                    extinguishObjectives.Add(hull, objective);
                    AddSubObjective(objective);
                }
            }
        }
    }
}
