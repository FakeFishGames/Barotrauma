using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    abstract class AIMultiObjective<T> : AIObjective
    {
        protected List<T> targets = new List<T>();
        protected Dictionary<T, AIObjective> objectives = new Dictionary<T, AIObjective>();
        protected HashSet<T> ignoreList = new HashSet<T>();
        protected readonly float ignoreListClearInterval = 30;
        private float ignoreListTimer;
        protected readonly float targetUpdateInterval = 2;
        private float targetUpdateTimer;

        public AIMultiObjective(Character character, string option) : base(character, option)
        {
            FindTargets();
            CreateObjectives();
        }

        protected override void Act(float deltaTime) { }
        public override bool IsCompleted() => false;
        public override bool CanBeCompleted => true;

        public override void UpdatePriority(AIObjectiveManager objectiveManager, float deltaTime)
        {
            base.UpdatePriority(objectiveManager, deltaTime);
            if (ignoreListTimer > ignoreListClearInterval)
            {
                ignoreList.Clear();
                ignoreListTimer = 0;
                FindTargets();
            }
            else
            {
                ignoreListTimer += deltaTime;
            }
            if (targetUpdateTimer > targetUpdateInterval)
            {
                targetUpdateTimer = 0;
                FindTargets();
            }
            else
            {
                targetUpdateTimer += deltaTime;
            }
            foreach (var objective in objectives)
            {
                if (!objective.Value.CanBeCompleted)
                {
                    ignoreList.Add(objective.Key);
                }
            }
            SyncRemovedObjectives(objectives, targets);
            if (objectives.None())
            {
                CreateObjectives();
            }
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (character.Submarine == null) { return 0; }
            if (targets.None()) { return 0; }
            float avg = targets.Average(t => Average(t));
            if (objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority - MathHelper.Max(0, AIObjectiveManager.OrderPriority - avg);
            }
            return MathHelper.Lerp(0, AIObjectiveManager.OrderPriority, avg / 100);
        }

        protected abstract void FindTargets();
        protected abstract void CreateObjectives();
        protected abstract float Average(T target);
    }
}
