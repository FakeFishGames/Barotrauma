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

        protected string option;

        public virtual bool IsCompleted()
        {
            return false;
        }

        public virtual bool CanBeCompleted
        {
            get { return true; }
        }

        public string Option
        {
            get { return option; }
        }
            

        public AIObjective(Character character, string option)
        {
            subObjectives = new List<AIObjective>();

            this.character = character;

            this.option = option;

#if DEBUG
            IsDuplicate(null);
#endif
        }

        /// <summary>
        /// makes the character act according to the objective, or according to any subobjectives that
        /// need to be completed before this one
        /// </summary>
        public void TryComplete(float deltaTime)
        {
            subObjectives.RemoveAll(s => s.IsCompleted() || !s.CanBeCompleted);

            foreach (AIObjective objective in subObjectives)
            {
                objective.TryComplete(deltaTime);
                return;
            }

            Act(deltaTime);
        }

        protected virtual void Act(float deltaTime) { }

        public void AddSubObjective(AIObjective objective)
        {
            if (subObjectives.Any(o => o.IsDuplicate(objective))) return;

            subObjectives.Add(objective);
        }

        public virtual float GetPriority(Character character)
        {
            return 0.0f;
        }

        public virtual bool IsDuplicate(AIObjective otherObjective)
        {
#if DEBUG
            throw new NotImplementedException();
#else
            return (this.GetType() == otherObjective.GetType());
#endif

        }
    }
}
