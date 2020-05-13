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
        public virtual bool IgnoreUnsafeHulls => false;
        public virtual bool AbandonWhenCannotCompleteSubjectives => true;
        public virtual bool AllowSubObjectiveSorting => false;

        /// <summary>
        /// Can there be multiple objective instaces of the same type? Currently multiple instances allowed only for main objectives and the subobjectives of objetive loops.
        /// In theory, there could be multiple subobjectives of same type for concurrent objectives, but that would make things more complex -> potential issues
        /// </summary>
        public virtual bool AllowMultipleInstances => false;

        /// <summary>
        /// Run the main objective with all subobjectives concurrently?
        /// If false, the main objective will continue only when all the subobjectives have been removed (done).
        /// </summary>
        public virtual bool ConcurrentObjectives => false;

        public virtual bool KeepDivingGearOn => false;
        public virtual bool UnequipItems => false;
        public virtual bool AllowOutsideSubmarine => false;

        protected readonly List<AIObjective> subObjectives = new List<AIObjective>();
        private float _cumulatedDevotion;
        protected float CumulatedDevotion
        {
            get { return _cumulatedDevotion; }
            set { _cumulatedDevotion = MathHelper.Clamp(value, 0, MaxDevotion); }
        }

        protected virtual float MaxDevotion => 10;

        /// <summary>
        /// Final priority value after all calculations.
        /// </summary>
        public float Priority { get; set; }
        public float PriorityModifier { get; private set; } = 1;
        public readonly Character character;
        public readonly AIObjectiveManager objectiveManager;
        public string Option { get; private set; }

        private bool _abandon;
        public bool Abandon
        {
            get { return _abandon; }
            set
            {
                _abandon = value;
                if (_abandon)
                {
                    OnAbandon();
                }
            }
        }

        public virtual bool CanBeCompleted => !Abandon;

        /// <summary>
        /// When true, the objective is never completed, unless CanBeCompleted returns false.
        /// </summary>
        public virtual bool IsLoop { get; set; }
        public IEnumerable<AIObjective> SubObjectives => subObjectives;
        public AIObjective CurrentSubObjective => subObjectives.FirstOrDefault();

        private readonly List<AIObjective> all = new List<AIObjective>();
        public IEnumerable<AIObjective> GetSubObjectivesRecursive(bool includingSelf = false)
        {
            all.Clear();
            if (includingSelf)
            {
                all.Add(this);
            }
            foreach (var subObjective in subObjectives)
            {
                all.AddRange(subObjective.GetSubObjectivesRecursive(true));
            }
            return all;
        }

        public event Action Completed;
        public event Action Abandoned;
        public event Action Selected;
        public event Action Deselected;

        protected HumanAIController HumanAIController => character.AIController as HumanAIController;
        protected IndoorsSteeringManager PathSteering => HumanAIController.PathSteering;
        protected SteeringManager SteeringManager => HumanAIController.SteeringManager;

        public AIObjective GetActiveObjective()
        {
            var subObjective = CurrentSubObjective;
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
            if (CheckState()) { return; }
            // Not ready -> act (can't do foreach because it's possible that the collection is modified in event callbacks.
            for (int i = 0; i < subObjectives.Count; i++)
            {
                subObjectives[i].TryComplete(deltaTime);
                if (!ConcurrentObjectives) { return; }
            }
            Act(deltaTime);
        }

        // TODO: check turret aioperate
        public void AddSubObjective(AIObjective objective, bool addFirst = false)
        {
            var type = objective.GetType();
            subObjectives.RemoveAll(o => o.GetType() == type);
            if (addFirst)
            {
                subObjectives.Insert(0, objective);
            }
            else
            {
                subObjectives.Add(objective);
            }
        }

        /// <summary>
        /// This method allows multiple subobjectives of same type. Use with caution.
        /// </summary>
        public void AddSubObjectiveInQueue(AIObjective objective)
        {
            if (!subObjectives.Contains(objective))
            {
                subObjectives.Add(objective);
            }
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
            if (!AllowSubObjectiveSorting) { return; }
            if (subObjectives.None()) { return; }
            subObjectives.ForEach(so => so.GetPriority());
            subObjectives.Sort((x, y) => y.Priority.CompareTo(x.Priority));
            if (ConcurrentObjectives)
            {
                subObjectives.ForEach(so => so.SortSubObjectives());
            }
            else
            {
                subObjectives.First().SortSubObjectives();
            }
        }

        protected bool IsAllowed => AllowOutsideSubmarine || character.Submarine != null && character.Submarine.TeamID == character.TeamID && character.Submarine.Info.IsPlayer;

        /// <summary>
        /// Call this only when the priority needs to be recalculated. Use the cached Priority property when you don't need to recalculate.
        /// </summary>
        public virtual float GetPriority()
        {
            if (!IsAllowed)
            {
                Priority = 0;
                return Priority;
            }
            if (objectiveManager.CurrentOrder == this)
            {
                Priority = AIObjectiveManager.OrderPriority;
            }
            else
            {
                Priority = CumulatedDevotion;
            }
            return Priority;
        }

        private void UpdateDevotion(float deltaTime)
        {
            var currentObjective = objectiveManager.CurrentObjective;
            if (currentObjective != null && (currentObjective == this || currentObjective.subObjectives.Any(so => so == this)))
            {
                CumulatedDevotion += Devotion * deltaTime;
            }
        }

        public virtual bool IsDuplicate<T>(T otherObjective) where T : AIObjective => otherObjective.Option == Option;

        public virtual void Update(float deltaTime)
        {
            if (objectiveManager.CurrentOrder != this && objectiveManager.WaitTimer <= 0)
            {
                UpdateDevotion(deltaTime);
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
        /// Returns true if the objective was created and successfully added.
        /// </summary>
        protected bool TryAddSubObjective<T>(ref T objective, Func<T> constructor, Action onCompleted = null, Action onAbandon = null) where T : AIObjective
        {
            if (objective != null)
            {
                // Sub objective already found, no need to do anything if it remains in the subobjectives
                // If the sub objective is removed -> it's either completed or impossible to complete.
                if (!subObjectives.Contains(objective))
                {
                    objective = null;
                }
                return false;
            }
            else
            {
                objective = constructor();
                if (!subObjectives.Contains(objective))
                {
                    if (objective.AllowMultipleInstances)
                    {
                        subObjectives.Add(objective);
                    }
                    else
                    {
                        AddSubObjective(objective);
                    }
                    if (onCompleted != null)
                    {
                        objective.Completed += onCompleted;
                    }
                    if (onAbandon != null)
                    {
                        objective.Abandoned += onAbandon;
                    }
                    return true;
                }
#if DEBUG
                DebugConsole.ThrowError("Attempted to add a duplicate subobjective!\n" + Environment.StackTrace);
#endif
                return false;
            }
        }

        public virtual void OnSelected()
        {
            Reset();
            Selected?.Invoke();
        }

        public virtual void OnDeselected()
        {
            CumulatedDevotion = 0;
            Deselected?.Invoke();
        }

        protected virtual void OnCompleted()
        {
            Completed?.Invoke();
        }

        protected virtual void OnAbandon()
        {
            Abandoned?.Invoke();
        }

        public virtual void Reset()
        {
            isCompleted = false;
            hasBeenChecked = false;
            _abandon = false;
            CumulatedDevotion = 0;
        }

        protected abstract void Act(float deltaTime);

        private bool isCompleted;
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
                isCompleted = value;
            }
        }

        protected abstract bool Check();

        private bool CheckState()
        {
            hasBeenChecked = true;
            CheckSubObjectives();
            if (subObjectives.None())
            {
                if (Check())
                {
                    isCompleted = true;
                    OnCompleted();
                }
            }
            return isCompleted;
        }

        private void CheckSubObjectives()
        {
            for (int i = 0; i < subObjectives.Count; i++)
            {
                var subObjective = subObjectives[i];
                subObjective.CheckState();
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
                    if (AbandonWhenCannotCompleteSubjectives)
                    {
                        Abandon = true;
                    }
                }
            }
        }
    }
}
