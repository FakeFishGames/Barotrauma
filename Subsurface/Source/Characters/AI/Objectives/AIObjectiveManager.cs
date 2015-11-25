using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class AIObjectiveManager
    {
        private List<AIObjective> objectives;

        private Character character;
        
        public AIObjective CurrentObjective
        {
            get
            {
                return objectives.Any() ? objectives[0] : null;
            }
        }

        public AIObjectiveManager(Character character)
        {
            this.character = character;

            objectives = new List<AIObjective>();
        }

        public void AddObjective(AIObjective objective)
        {
            if (objectives.Find(o => o.IsDuplicate(objective)) != null) return;

            objectives.Add(objective);
        }

        public void UpdateObjectives()
        {
            if (!objectives.Any()) return;

            //remove completed objectives
            objectives = objectives.FindAll(o => !o.IsCompleted());

            //sort objectives according to priority
            objectives.Sort((x, y) => y.GetPriority(character).CompareTo(x.GetPriority(character)));

            if (character.AnimController.CurrentHull!=null)
            {
                var gaps = character.AnimController.CurrentHull.FindGaps();

                foreach (Gap gap in gaps)
                {
                    if (gap.linkedTo.Count > 1) continue;
                    AddObjective(new AIObjectiveFixLeak(gap, character));
                }
            }
        }

        public void DoCurrentObjective(float deltaTime)
        {
            if (!objectives.Any()) return;
            objectives[0].TryComplete(deltaTime);
        }
    }
}
