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
            objectives.Sort((x, y) => x.GetPriority(character).CompareTo(y.GetPriority(character)));            
            
        }

        public void DoCurrentObjective(float deltaTime)
        {
            if (!objectives.Any()) return;
            objectives[0].TryComplete(deltaTime, character);
        }
    }
}
