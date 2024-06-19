using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    /// <summary>
    /// An objective that creates specific kinds of subobjectives for specific types of targets, and loops through those targets. 
    /// For example, a cleanup objective that loops through items that need to be cleaned up, or a "fix leaks" objective that loops through leaks that need welding.
    /// </summary>
    abstract class AIObjectiveLoop<T> : AIObjective
    {
        public HashSet<T> Targets { get; private set; } = new HashSet<T>();
        public Dictionary<T, AIObjective> Objectives { get; private set; } = new Dictionary<T, AIObjective>();
        protected HashSet<T> ignoreList = new HashSet<T>();
        private float ignoreListClearTimer;
        protected float targetUpdateTimer;
        protected virtual float TargetUpdateTimeMultiplier { get; } = 1;

        /// <summary>
        /// How often are the subobjectives synced based on the available targets?
        /// </summary>
        private float syncTimer;
        private readonly float syncTime = 1;

        /// <summary>
        /// By default, doesn't clear the list automatically
        /// </summary>
        protected virtual float IgnoreListClearInterval => 0;

        /// <summary>
        /// Contains targets that anyone in the same crew has reported about. Used for automatic the target has to be reported before it can be can be targeted, so characters don't magically know where e.g. enemies are.
        /// Ignored on orders: a bot explicitly ordered to repair leaks or fight intruders can find targets that haven't been reported.
        /// </summary>
        public HashSet<T> ReportedTargets { get; private set; } = new HashSet<T>();

        public bool AddTarget(T target)
        {
            if (character.IsDead) { return false; }
            if (ReportedTargets.Contains(target))
            {
                return false;
            }
            if (IsValidTarget(target))
            {
                ReportedTargets.Add(target);
                return true;
            }
            return false;
        }

        public AIObjectiveLoop(Character character, AIObjectiveManager objectiveManager, float priorityModifier, Identifier option = default) 
            : base(character, objectiveManager, priorityModifier, option) { }

        protected override void Act(float deltaTime) { }
        protected override bool CheckObjectiveSpecific() => false;
        public override bool CanBeCompleted => true;
        public override bool AbandonWhenCannotCompleteSubObjectives => false;
        public override bool AllowSubObjectiveSorting => true;
        protected override bool AllowWhileHandcuffed => false;
        protected override bool AbandonIfDisallowed => false;

        /// <summary>
        /// Makes the priority inversely proportional to the value returned by <see cref="GetTargetPriority"/>. 
        /// In other words, gives this objective a high priority when priority of the targets is low.
        /// </summary>
        public virtual bool InverseTargetPriority => false;
        protected virtual bool ResetWhenClearingIgnoreList => true;
        protected virtual bool ForceOrderPriority => true;

        protected virtual int MaxTargets => int.MaxValue;

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (IgnoreListClearInterval > 0)
            {
                if (ignoreListClearTimer > IgnoreListClearInterval)
                {
                    if (ResetWhenClearingIgnoreList)
                    {
                        Reset();
                    }
                    else
                    {
                        ignoreList.Clear();
                        ignoreListClearTimer = 0;
                    }
                }
                else
                {
                    ignoreListClearTimer += deltaTime;
                }
            }
            if (targetUpdateTimer <= 0)
            {
                UpdateTargets();
            }
            else
            {
                targetUpdateTimer -= deltaTime;
            }
            if (syncTimer <= 0)
            {
                syncTimer = Math.Min(syncTime * Rand.Range(0.9f, 1.1f), targetUpdateTimer);
                // Sync objectives, subobjectives and targets
                foreach (var objective in Objectives)
                {
                    var target = objective.Key;
                    if (!Targets.Contains(target))
                    {
                        subObjectives.Remove(objective.Value);
                    }
                }
                SyncRemovedObjectives(Objectives, GetList());
            }
            else
            {
                syncTimer -= deltaTime;
            }
        }

        // 
        /// <summary>
        /// The timer is set between 1 and 10 seconds, depending on the priority modifier and a random +-25%
        /// </summary>
        private float CalculateTargetUpdateTimer() => targetUpdateTimer = 1 / MathHelper.Clamp(PriorityModifier * Rand.Range(0.75f, 1.25f), 0.1f, 1) * TargetUpdateTimeMultiplier;

        public override void Reset()
        {
            base.Reset();
            ignoreList.Clear();
            ignoreListClearTimer = 0;
            UpdateTargets();
        }

        protected override float GetPriority()
        {
            if (!IsAllowed)
            {
                HandleDisallowed();
                return Priority;
            }
            // Allow the target value to be more than 100.
            float targetPriority = GetTargetPriority();
            if (InverseTargetPriority)
            {
                targetPriority = 100 - targetPriority;
            }
            var currentSubObjective = CurrentSubObjective;
            if (currentSubObjective != null && currentSubObjective.Priority > targetPriority)
            {
                // If the priority is higher than the target value, let's just use it.
                // The priority calculation is more precise, but it takes into account things like distances,
                // so it's better not to use it if it's lower than the rougher targetValue.
                targetPriority = currentSubObjective.Priority;
            }
            // If the target value is less than 1% of the max value, let's just treat it as zero.
            if (targetPriority < 1)
            {
                Priority = 0;
            }
            else
            {
                if (objectiveManager.IsOrder(this))
                {
                    Priority = ForceOrderPriority ? objectiveManager.GetOrderPriority(this) : targetPriority;
                }
                else
                {
                    float max = AIObjectiveManager.LowestOrderPriority - 1;
                    if (this is AIObjectiveRescueAll rescueObjective && rescueObjective.Targets.Contains(character))
                    {
                        // Allow higher prio
                        max = AIObjectiveManager.EmergencyObjectivePriority;
                    }
                    float value = MathHelper.Clamp((CumulatedDevotion + (targetPriority * PriorityModifier)) / 100, 0, 1);
                    Priority = MathHelper.Lerp(0, max, value);
                }
            }
            return Priority;
        }

        protected void UpdateTargets()
        {
            CalculateTargetUpdateTimer();
            Targets.Clear();
            FindTargets();
            CreateObjectives();
        }

        protected virtual void FindTargets()
        {
            foreach (T target in GetList())
            {
                // The bots always find targets when the objective is an order.
                if (!objectiveManager.IsOrder(this))
                {
                    // Battery or pump states cannot currently be reported (not implemented) and therefore we must ignore them -> the bots always know if they require attention.
                    bool ignore = this is AIObjectiveChargeBatteries || this is AIObjectivePumpWater || this is AIObjectiveFindThieves;
                    if (!ignore && !ReportedTargets.Contains(target)) { continue; }
                }
                if (!IsValidTarget(target)) { continue; }
                if (!ignoreList.Contains(target))
                {
                    Targets.Add(target);
                    if (Targets.Count > MaxTargets)
                    {
                        break;
                    }
                }
            }
        }

        protected virtual void CreateObjectives()
        {
            foreach (T target in Targets)
            {
                if (ignoreList.Contains(target)) { continue; }
                if (!Objectives.TryGetValue(target, out AIObjective objective))
                {
                    objective = ObjectiveConstructor(target);
                    Objectives.Add(target, objective);
                    if (!subObjectives.Contains(objective))
                    {
                        subObjectives.Add(objective);
                    }
                    objective.Completed += () =>
                    {
                        Objectives.Remove(target);
                        OnObjectiveCompleted(objective, target);
                    };
                    objective.Abandoned += () =>
                    {
                        Objectives.Remove(target);
                        ignoreList.Add(target);
                        targetUpdateTimer = Math.Min(0.1f, targetUpdateTimer);
                    };
                }
            }
        }

        protected abstract void OnObjectiveCompleted(AIObjective objective, T target);

        /// <summary>
        /// List of all possible items of the specified type. Used for filtering the removed objectives.
        /// </summary>
        protected abstract IEnumerable<T> GetList();

        /// <summary>
        /// Returns a priority value based on the current targets (e.g. high prio when there's lots of severe fires or leaks).
        /// The priority of this objective is based on the target priority.
        /// </summary>
        protected abstract float GetTargetPriority();

        protected abstract AIObjective ObjectiveConstructor(T target);
        protected abstract bool IsValidTarget(T target);
    }
}
