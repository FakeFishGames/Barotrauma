using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class AIObjective
    {
        protected List<AIObjective> subObjectives;

        protected float priority;

        public virtual bool IsCompleted()
        {
            return false;
        }

        public AIObjective()
        {
            subObjectives = new List<AIObjective>();
        }

        /// <summary>
        /// makes the character act according to the objective, or according to any subobjectives that
        /// need to be completed before this one
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

        public virtual float GetPriority(Character character)
        {
            return 0.0f;
        }

        public virtual bool IsDuplicate(AIObjective otherObjective)
        {
            return true;
        }
    }
}
