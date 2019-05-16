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
        public virtual bool KeepDivingGearOn => false;

        protected readonly List<AIObjective> subObjectives = new List<AIObjective>();
        public float Priority { get; set; }
        public float PriorityModifier { get; private set; }
        protected readonly Character character;
        protected string option;
        protected bool abandon;

        public virtual bool CanBeCompleted => !abandon && subObjectives.All(so => so.CanBeCompleted);
        public IEnumerable<AIObjective> SubObjectives => subObjectives;
        public AIObjective CurrentSubObjective { get; private set; }

        protected HumanAIController HumanAIController => character.AIController as HumanAIController;
        protected IndoorsSteeringManager PathSteering => HumanAIController.PathSteering;
        protected SteeringManager SteeringManager => HumanAIController.SteeringManager;

        public string Option
        {
            get { return option; }
        }            

        public AIObjective(Character character, string option, float priorityModifier)
        {
            this.character = character;
            this.option = option;
            PriorityModifier = priorityModifier;
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
            for (int i = 0; i < subObjectives.Count; i++)
            {
                var subObjective = subObjectives[i];
                if (subObjective.IsCompleted())
                {
#if DEBUG
                    DebugConsole.NewMessage($"Removing subobjective {subObjective.DebugTag} of {DebugTag}, because it is completed.");
#endif
                    subObjectives.Remove(subObjective);
                }
                else if (!subObjective.CanBeCompleted)
                {
#if DEBUG
                    DebugConsole.NewMessage($"Removing subobjective {subObjective.DebugTag} of {DebugTag}, because it cannot be completed.");
#endif
                    subObjectives.Remove(subObjective);
                }
                else if (subObjective.ShouldInterruptSubObjective(subObjective))
                {
#if DEBUG
                    DebugConsole.NewMessage($"Removing subobjective {subObjective.DebugTag} of {DebugTag}, because it is interrupted.");
#endif
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
            if (subObjectives.None()) { return; }
            subObjectives.Sort((x, y) => y.GetPriority(objectiveManager).CompareTo(x.GetPriority(objectiveManager)));
            CurrentSubObjective = SubObjectives.First();
            CurrentSubObjective.SortSubObjectives(objectiveManager);
        }

        public virtual float GetPriority(AIObjectiveManager objectiveManager) => Priority * PriorityModifier;

        public virtual void Update(AIObjectiveManager objectiveManager, float deltaTime)
        {
            var subObjective = objectiveManager.CurrentObjective?.CurrentSubObjective;
            if (objectiveManager.CurrentOrder == this)
            {
                Priority = AIObjectiveManager.OrderPriority;
            }
            else if (objectiveManager.CurrentObjective == this || subObjective == this)
            {
                Priority += Devotion * PriorityModifier * deltaTime;
            }
            Priority = MathHelper.Clamp(Priority, 0, 100);
            subObjectives.ForEach(so => so.Update(objectiveManager, deltaTime));
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
        public virtual void OnSelected() { }
        public virtual void Reset() { }

        protected abstract void Act(float deltaTime);
        
        public abstract bool IsCompleted();
        public abstract bool IsDuplicate(AIObjective otherObjective);
    }
}
