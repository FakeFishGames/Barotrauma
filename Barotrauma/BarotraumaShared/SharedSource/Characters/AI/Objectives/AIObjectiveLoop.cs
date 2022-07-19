using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    abstract class AIObjectiveLoop<T> : AIObjective
    {
        public HashSet<T> Targets { get; private set; } = new HashSet<T>();
        public Dictionary<T, AIObjective> Objectives { get; private set; } = new Dictionary<T, AIObjective>();
        protected HashSet<T> ignoreList = new HashSet<T>();
        private float ignoreListTimer;
        protected float targetUpdateTimer;
        protected virtual float TargetUpdateTimeMultiplier { get; } = 1;

        private float syncTimer;
        private readonly float syncTime = 1;

        // By default, doesn't clear the list automatically
        protected virtual float IgnoreListClearInterval => 0;

        public HashSet<T> ReportedTargets { get; private set; } = new HashSet<T>();

        public bool AddTarget(T target)
        {
            if (character.IsDead) { return false; }
            if (ReportedTargets.Contains(target))
            {
                return false;
            }
            if (Filter(target))
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
        public override bool AbandonWhenCannotCompleteSubjectives => false;
        public override bool AllowSubObjectiveSorting => true;
        public virtual bool InverseTargetEvaluation => false;
        protected virtual bool ResetWhenClearingIgnoreList => true;
        protected virtual bool ForceOrderPriority => true;

        public override bool IsLoop { get => true; set => throw new Exception("Trying to set the value for IsLoop from: " + System.Environment.StackTrace.CleanupStackTrace()); }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (IgnoreListClearInterval > 0)
            {
                if (ignoreListTimer > IgnoreListClearInterval)
                {
                    if (ResetWhenClearingIgnoreList)
                    {
                        Reset();
                    }
                    else
                    {
                        ignoreList.Clear();
                        ignoreListTimer = 0;
                    }
                }
                else
                {
                    ignoreListTimer += deltaTime;
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
                        var subObjective = objective.Value;
                        if (CurrentSubObjective == subObjective)
                        {
                            CurrentSubObjective.Abandon = !CurrentSubObjective.IsCompleted;
                        }
                        subObjectives.Remove(subObjective);
                    }
                }
                SyncRemovedObjectives(Objectives, GetList());
            }
            else
            {
                syncTimer -= deltaTime;
            }
        }

        // the timer is set between 1 and 10 seconds, depending on the priority modifier and a random +-25%
        private float CalculateTargetUpdateTimer() => targetUpdateTimer = 1 / MathHelper.Clamp(PriorityModifier * Rand.Range(0.75f, 1.25f), 0.1f, 1) * TargetUpdateTimeMultiplier;

        public override void Reset()
        {
            base.Reset();
            ignoreList.Clear();
            ignoreListTimer = 0;
            UpdateTargets();
        }

        protected override float GetPriority()
        {
            if (!IsAllowed)
            {
                Priority = 0;
                return Priority;
            }
            if (character.LockHands)
            {
                Priority = 0;
            }
            else
            {
                // Allow the target value to be more than 100.
                float targetValue = TargetEvaluation();
                if (InverseTargetEvaluation)
                {
                    targetValue = 100 - targetValue;
                }
                var currentSubObjective = CurrentSubObjective;
                if (currentSubObjective != null && currentSubObjective.Priority > targetValue)
                {
                    // If the priority is higher than the target value, let's just use it.
                    // The priority calculation is more precise, but it takes into account things like distances,
                    // so it's better not to use it if it's lower than the rougher targetValue.
                    targetValue = currentSubObjective.Priority;
                }
                // If the target value is less than 1% of the max value, let's just treat it as zero.
                if (targetValue < 1)
                {
                    Priority = 0;
                }
                else
                {
                    if (objectiveManager.IsOrder(this))
                    {
                        Priority = ForceOrderPriority ? objectiveManager.GetOrderPriority(this) : targetValue;
                    }
                    else
                    {
                        float max = AIObjectiveManager.LowestOrderPriority - 1;
                        float value = MathHelper.Clamp((CumulatedDevotion + (targetValue * PriorityModifier)) / 100, 0, 1);
                        Priority = MathHelper.Lerp(0, max, value);
                    }
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
                    bool ignore = this is AIObjectiveChargeBatteries || this is AIObjectivePumpWater;
                    if (!ignore && !ReportedTargets.Contains(target)) { continue; }
                }
                if (!Filter(target)) { continue; }
                if (!ignoreList.Contains(target))
                {
                    Targets.Add(target);
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

        protected abstract float TargetEvaluation();

        protected abstract AIObjective ObjectiveConstructor(T target);
        protected abstract bool Filter(T target);
    }
}
