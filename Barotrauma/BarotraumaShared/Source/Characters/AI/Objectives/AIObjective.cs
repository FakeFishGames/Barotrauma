using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    abstract class AIObjective
    {
        public virtual float Devotion => AIObjectiveManager.baseDevotion;

        public abstract string DebugTag { get; }

        protected readonly List<AIObjective> subObjectives = new List<AIObjective>();
        protected float priority;
        protected readonly Character character;
        protected string option;

        public virtual bool CanBeCompleted => subObjectives.All(so => so.CanBeCompleted);
        public IEnumerable<AIObjective> SubObjectives => subObjectives;
        public AIObjective CurrentSubObjective { get; private set; }

        public string Option
        {
            get { return option; }
        }            

        public AIObjective(Character character, string option)
        {
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
            //subObjectives.RemoveAll(s => s.IsCompleted() || !s.CanBeCompleted || ShouldInterruptSubObjective(s));

            // For debugging
            for (int i = 0; i < subObjectives.Count; i++)
            {
                var subObjective = subObjectives[i];
                if (subObjective.IsCompleted())
                {
                    DebugConsole.NewMessage($"Removing subobjective {subObjective.DebugTag} of {DebugTag}, because it is completed.");
                    subObjectives.Remove(subObjective);
                }
                else if (!subObjective.CanBeCompleted)
                {
                    DebugConsole.NewMessage($"Removing subobjective {subObjective.DebugTag} of {DebugTag}, because it cannot be completed.");
                    subObjectives.Remove(subObjective);
                }
                else if (subObjective.ShouldInterruptSubObjective(subObjective))
                {
                    DebugConsole.NewMessage($"Removing subobjective {subObjective.DebugTag} of {DebugTag}, because it is interrupted.");
                    subObjectives.Remove(subObjective);
                }
            }

            if (!subObjectives.Contains(CurrentSubObjective))
            {
                CurrentSubObjective = null;
            }

            foreach (AIObjective objective in subObjectives)
            {
                objective.TryComplete(deltaTime);
                return;
            }

            Act(deltaTime);
        }

        public void AddSubObjective(AIObjective objective)
        {
            if (subObjectives.Any(o => o.IsDuplicate(objective))) return;

            subObjectives.Add(objective);
        }

        public void SortSubObjectives(AIObjectiveManager objectiveManager)
        {
            if (!subObjectives.Any()) return;
            subObjectives.Sort((x, y) => y.GetPriority(objectiveManager).CompareTo(x.GetPriority(objectiveManager)));
            CurrentSubObjective = SubObjectives.First();
            CurrentSubObjective.SortSubObjectives(objectiveManager);
        }

        public virtual float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }
            else if (objectiveManager.CurrentObjective == this)
            {
                priority += Devotion;
            }
            var subObjective = objectiveManager.CurrentObjective.CurrentSubObjective;
            if (subObjective != null && subObjective == this)
            {
                priority += Devotion;
            }
            return priority;
        }

        /// <summary>
        /// Checks if the subobjectives in the given collection are removed from the subobjectives. And if so, removes it also from the dictionary.
        /// </summary>
        protected void SyncRemovedObjectives<T1, T2>(Dictionary<T1, T2> dictionary, IEnumerable<T1> collection) where T2 : AIObjective
        {
            foreach (T1 key in collection)
            {
                if (dictionary.TryGetValue(key, out T2 objective))
                {
                    if (!subObjectives.Contains(objective))
                    {
                        dictionary.Remove(key);
                    }
                }
            }
        }

        protected virtual bool ShouldInterruptSubObjective(AIObjective subObjective) => false;

        protected abstract void Act(float deltaTime);
        
        public abstract bool IsCompleted();
        public abstract bool IsDuplicate(AIObjective otherObjective);
    }
}
