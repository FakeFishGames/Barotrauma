using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Barotrauma.Extensions;

namespace Barotrauma
{
    abstract class AIObjective
    {
        public virtual float Devotion => AIObjectiveManager.baseDevotion;

        public abstract string DebugTag { get; }
        public virtual bool ForceRun => false;

        /// <summary>
        /// Run the main objective with all subobjectives concurrently?
        /// If false, the main objective will continue only when all the subobjectives have been removed (done).
        /// </summary>
        public virtual bool ConcurrentObjectives => false;

        protected readonly List<AIObjective> subObjectives = new List<AIObjective>();
        public float Priority { get; set; }
        public float PriorityModifier { get; private set; } = 1;
        public readonly Character character;
        public readonly AIObjectiveManager objectiveManager;
        public string Option { get; protected set; }

        protected bool abandon;

        /// <summary>
        /// Can the objective be completed. That is, does the objective have failing subobjectives or other conditions that prevent it from completing.
        /// </summary>
        public virtual bool CanBeCompleted => !abandon && subObjectives.None(so => !so.CanBeCompleted);

        /// <summary>
        /// When true, the objective is never completed, unless CanBeCompleted returns false.
        /// </summary>
        public virtual bool IsLoop { get; set; }
        public IEnumerable<AIObjective> SubObjectives => subObjectives;

        public IEnumerable<AIObjective> GetSubObjectivesRecursive() => SubObjectives.SelectManyRecursive(so => so.SubObjectives);

        public event Action Completed;

        protected HumanAIController HumanAIController => character.AIController as HumanAIController;
        protected IndoorsSteeringManager PathSteering => HumanAIController.PathSteering;
        protected SteeringManager SteeringManager => HumanAIController.SteeringManager;

        public AIObjective GetActiveObjective()
        {
            var subObjective = SubObjectives.FirstOrDefault();
            return subObjective == null ? this : subObjective.GetActiveObjective();
        }

        public AIObjective(Character character, AIObjectiveManager objectiveManager, float priorityModifier, string option = null)
        {
            this.objectiveManager = objectiveManager;
            this.character = character;
            Option = option ?? string.Empty;

            PriorityModifier = priorityModifier;
        }

        /// <summary>
        /// Makes the character act according to the objective, or according to any subobjectives that need to be completed before this one
        /// </summary>
        public void TryComplete(float deltaTime)
        {
            if (isCompleted) { return; }
            CheckState();
            CheckSubObjectives();
            foreach (AIObjective objective in subObjectives)
            {
                objective.TryComplete(deltaTime);
                if (!ConcurrentObjectives)
                {
                    return;
                }
            }
            Act(deltaTime);
        }

        // TODO: go through AIOperate methods where subobjectives are added and ensure that they add the subobjectives correctly -> use TryAddSubObjective method instead?
        public void AddSubObjective(AIObjective objective)
        {
            var type = objective.GetType();
            subObjectives.RemoveAll(o => o.GetType() == type);
            subObjectives.Add(objective);
        }

        public void RemoveSubObjective<T>(ref T objective) where T : AIObjective
        {
            if (objective != null)
            {
                if (subObjectives.Contains(objective))
                {
                    subObjectives.Remove(objective);
                }
                objective = null;
            }
        }

        public void SortSubObjectives()
        {
            if (subObjectives.None()) { return; }
            subObjectives.Sort((x, y) => y.GetPriority().CompareTo(x.GetPriority()));
            if (ConcurrentObjectives)
            {
                subObjectives.ForEach(so => so.SortSubObjectives());
            }
            else
            {
                subObjectives.First().SortSubObjectives();
            }
        }

        public virtual float GetPriority() => Priority * PriorityModifier;

        public virtual void Update(float deltaTime)
        {
            if (objectiveManager.CurrentOrder == this)
            {
                Priority = AIObjectiveManager.OrderPriority;
            }
            else if (objectiveManager.WaitTimer <= 0)
            {
                if (objectiveManager.CurrentObjective != null)
                {
                    if (objectiveManager.CurrentObjective == this || objectiveManager.CurrentObjective.subObjectives.Any(so => so == this))
                    {
                        Priority += Devotion * PriorityModifier * deltaTime;
                    }
                }
                Priority = MathHelper.Clamp(Priority, 0, 100);
            }
            subObjectives.ForEach(so => so.Update(deltaTime));
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

        /// <summary>
        /// Checks if the objective already is created and added in subobjectives. If not, creates it.
        /// Handles objectives that cannot be completed. If the objective has been removed form the subobjectives, a null value is assigned to the reference.
        /// Returns true if the objective was created.
        /// </summary>
        protected bool TryAddSubObjective<T>(ref T objective, Func<T> constructor, Action onAbandon = null) where T : AIObjective
        {
            if (objective != null)
            {
                // Sub objective already found, no need to do anything if it remains in the subobjectives
                // If the sub objective is removed -> it's either completed or impossible to complete.
                if (!subObjectives.Contains(objective))
                {
                    if (!objective.CanBeCompleted)
                    {
                        abandon = true;
                        onAbandon?.Invoke();
                    }
                    objective = null;
                }
                return false;
            }
            else
            {
                objective = constructor();
                if (!subObjectives.Contains(objective))
                {
                    AddSubObjective(objective);
                }
                return true;
            }
        }

        public virtual void OnSelected()
        {
            // Should we reset steering here?
            //if (!ConcurrentObjectives)
            //{
            //    SteeringManager.Reset();
            //}
        }

        protected virtual void OnCompleted()
        {
            Completed?.Invoke();
        }

        public virtual void Reset()
        {
            isCompleted = false;
            hasBeenChecked = false;
        }

        protected abstract void Act(float deltaTime);

        protected bool isCompleted;
        private bool hasBeenChecked;

        public bool IsCompleted
        {
            get
            {
                if (!hasBeenChecked)
                {
                    CheckState();
                }
                return isCompleted;
            }
            protected set
            {
                isCompleted = true;
            }
        }

        protected abstract bool Check();

        private bool CheckState()
        {
            hasBeenChecked = true;
            if (Check())
            {
                if (!isCompleted)
                {
                    OnCompleted();
                }
                isCompleted = true;
            }
            subObjectives.ForEach(so => so.CheckState());
            return isCompleted;
        }

        private void CheckSubObjectives()
        {
            for (int i = 0; i < subObjectives.Count; i++)
            {
                var subObjective = subObjectives[i];
                if (subObjective.IsCompleted)
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Removing SUBobjective {subObjective.DebugTag} of {DebugTag}, because it is completed.", Color.LightGreen);
#endif
                    subObjectives.Remove(subObjective);
                }
                else if (!subObjective.CanBeCompleted)
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Removing SUBobjective {subObjective.DebugTag} of {DebugTag}, because it cannot be completed.", Color.Red);
#endif
                    subObjectives.Remove(subObjective);
                }
            }
        }
    }
}
