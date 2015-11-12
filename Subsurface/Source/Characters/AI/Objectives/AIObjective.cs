using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class AIObjective
    {
        protected List<AIObjective> subObjectives;

        public virtual bool IsCompleted()
        {
            return true;
        }

        public AIObjective()
        {
            subObjectives = new List<AIObjective>();
        }

        /// <summary>
        /// makes the character act according to the objective, or according to any subobjectives that
        /// need to be completed before this one (starting from the one with the highest priority)
        /// </summary>
        /// <param name="character">the character who's trying to achieve the objective</param>
        public void TryComplete(float deltaTime, Character character)
        {
            foreach (AIObjective objective in subObjectives)
            {
                if (objective.IsCompleted()) continue;

                objective.TryComplete(deltaTime, character);
                return;
            }

            Act(deltaTime, character);
        }

        protected virtual void Act(float deltaTime, Character character) { }
    }
}
