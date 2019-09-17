using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    abstract class AIObjectiveLoop<T> : AIObjective
    {
        public HashSet<T> Targets { get; private set; } = new HashSet<T>();
        public Dictionary<T, AIObjective> Objectives { get; private set; } = new Dictionary<T, AIObjective>();
        protected HashSet<T> ignoreList = new HashSet<T>();
        private float ignoreListTimer;
        private float targetUpdateTimer;

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

        public AIObjectiveLoop(Character character, AIObjectiveManager objectiveManager, float priorityModifier, string option = null) 
            : base(character, objectiveManager, priorityModifier, option) { }

        protected override void Act(float deltaTime) { }
        protected override bool Check() => false;
        public override bool CanBeCompleted => true;
        public override bool AbandonWhenCannotCompleteSubjectives => false;
        public override bool AllowSubObjectiveSorting => true;
        public override bool ReportFailures => false;

        public override bool IsLoop { get => true; set => throw new System.Exception("Trying to set the value for IsLoop from: " + System.Environment.StackTrace); }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (IgnoreListClearInterval > 0)
            {
                if (ignoreListTimer > IgnoreListClearInterval)
                {
                    Reset();
                }
                else
                {
                    ignoreListTimer += deltaTime;
                }
            }
            if (targetUpdateTimer < 0)
            {
                UpdateTargets();
            }
            else
            {
                targetUpdateTimer -= deltaTime;
            }
            // Sync objectives, subobjectives and targets
            foreach (var objective in Objectives)
            {
                var target = objective.Key;
                //if (!objective.Value.CanBeCompleted && !ignoreList.Contains(target))
                //{
                //    // TODO: leaks that cannot be accessed from inside cause FixLeak objective to fail, but for some reason it's not ignored. Make sure that it is.
                //    ignoreList.Add(target);
                //    targetUpdateTimer = 0;
                //}
                if (!Targets.Contains(target))
                {
                    subObjectives.Remove(objective.Value);
                }
            }
            SyncRemovedObjectives(Objectives, GetList());
            if (Objectives.None() && Targets.Any())
            {
                CreateObjectives();
            }
        }

        // the timer is set between 1 and 10 seconds, depending on the priority modifier and a random +-25%
        private float SetTargetUpdateTimer() => targetUpdateTimer = 1 / MathHelper.Clamp(PriorityModifier * Rand.Range(0.75f, 1.25f), 0.1f, 1);

        public override void Reset()
        {
            base.Reset();
            ignoreList.Clear();
            ignoreListTimer = 0;
            UpdateTargets();
        }

        public override float GetPriority()
        {
            if (character.LockHands) { return 0; }
            if (character.Submarine == null) { return 0; }
            if (Targets.None()) { return 0; }
            // Allow the target value to be more than 100.
            float targetValue = TargetEvaluation();
            // If the target value is less than 1% of the max value, let's just treat it as zero.
            if (targetValue < 1) { return 0; }
            if (objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }
            float max = MathHelper.Min(AIObjectiveManager.OrderPriority - 1, 90);
            float devotion = MathHelper.Min(10, Priority);
            float value = MathHelper.Clamp((devotion + targetValue * PriorityModifier) / 100, 0, 1);
            return MathHelper.Lerp(0, max, value);
        }

        protected void UpdateTargets()
        {
            SetTargetUpdateTimer();
            Targets.Clear();
            FindTargets();
            CreateObjectives();
        }

        protected virtual void FindTargets()
        {
            foreach (T target in GetList())
            {
                // The bots always find targets when the objective is an order.
                if (objectiveManager.CurrentOrder != this)
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
                        targetUpdateTimer = 0;
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
