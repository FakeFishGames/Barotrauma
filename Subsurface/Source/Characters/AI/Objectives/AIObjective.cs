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

        protected Character character;

        public virtual bool IsCompleted()
        {
            return false;
        }

        public virtual bool CanBeCompleted
        {
            get { return false; }
        }

        public AIObjective(Character character)
        {
            subObjectives = new List<AIObjective>();

            this.character = character;
        }

        /// <summary>
        /// makes the character act according to the objective, or according to any subobjectives that
        /// need to be completed before this one
        /// </summary>
        /// <param name="character">the character who's trying to achieve the objective</param>
        public void TryComplete(float deltaTime)
        {
            foreach (AIObjective objective in subObjectives)
            {
                if (objective.IsCompleted()) continue;

                objective.TryComplete(deltaTime);
                return;
            }

            Act(deltaTime);
        }

        protected virtual void Act(float deltaTime) { }

        public virtual float GetPriority(Character character)
        {
            return 0.0f;
        }

        public virtual bool IsDuplicate(AIObjective otherObjective)
        {
            throw new NotImplementedException();
        }
    }
}
