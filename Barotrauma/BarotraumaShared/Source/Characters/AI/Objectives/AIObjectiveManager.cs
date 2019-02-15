using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveManager
    {
        public const float OrderPriority = 50.0f;

        private List<AIObjective> objectives;

        private Character character;

        /// <summary>
        /// When set above zero, the character will stand still doing nothing until the timer runs out (assuming they don't a high priority order active)
        /// </summary>
        public float WaitTimer;

        public AIObjective CurrentOrder { get; private set; }
        public AIObjective CurrentObjective { get; private set; }

        public AIObjectiveManager(Character character)
        {
            this.character = character;

            objectives = new List<AIObjective>();
        }

        public void AddObjective(AIObjective objective)
        {
            if (objectives.Find(o => o.IsDuplicate(objective)) != null) return;

            objectives.Add(objective);
        }

        public Dictionary<AIObjective, CoroutineHandle> DelayedObjectives { get; private set; } = new Dictionary<AIObjective, CoroutineHandle>();
        public void AddObjective(AIObjective objective, float delay, Action callback = null)
        {
            if (DelayedObjectives.TryGetValue(objective, out CoroutineHandle coroutine))
            {
                CoroutineManager.StopCoroutines(coroutine);
                DelayedObjectives.Remove(objective);
            }
            coroutine = CoroutineManager.InvokeAfter(() =>
            {
                DelayedObjectives.Remove(objective);
                AddObjective(objective);
                callback?.Invoke();
            }, delay);
            DelayedObjectives.Add(objective, coroutine);
        }

        public T GetObjective<T>() where T : AIObjective
        {
            foreach (AIObjective objective in objectives)
            {
                if (objective is T) return (T)objective;
            }
            return null;
        }

        private AIObjective GetCurrentObjective()
        {
            if (CurrentOrder != null &&
                (objectives.Count == 0 || CurrentOrder.GetPriority(this) > objectives[0].GetPriority(this)))
            {
                return CurrentOrder;
            }

            return objectives.Count == 0 ? null : objectives[0];
        }

        public float GetCurrentPriority()
        {
            var currentObjective = GetCurrentObjective();
            return currentObjective == null ? 0.0f : currentObjective.GetPriority(this);
        }

        public void UpdateObjectives()
        {
            if (!objectives.Any()) return;

            //remove completed objectives and ones that can't be completed
            objectives = objectives.FindAll(o => !o.IsCompleted() && o.CanBeCompleted);

            //sort objectives according to priority
            objectives.Sort((x, y) => y.GetPriority(this).CompareTo(x.GetPriority(this)));
            GetCurrentObjective()?.SortSubObjectives(this);
        }

        
        public void DoCurrentObjective(float deltaTime)
        {
            CurrentObjective = GetCurrentObjective();

            if (CurrentObjective == null || (CurrentObjective.GetPriority(this) < OrderPriority && WaitTimer > 0.0f))
            {
                WaitTimer -= deltaTime;
                character.AIController.SteeringManager.Reset();
                return;
            }

            CurrentObjective?.TryComplete(deltaTime);
        }
        
        public void SetOrder(AIObjective objective)
        {
            CurrentOrder = objective;
        }

        public void SetOrder(Order order, string option, Character orderGiver)
        {
            CurrentOrder = null;
            if (order == null) return;

            switch (order.AITag.ToLowerInvariant())
            {
                case "follow":
                    CurrentOrder = new AIObjectiveGoTo(orderGiver, character, true)
                    {
                        CloseEnough = 1.5f,
                        AllowGoingOutside = true,
                        IgnoreIfTargetDead = true,
                        FollowControlledCharacter = orderGiver == character
                    };
                    break;
                case "wait":
                    CurrentOrder = new AIObjectiveGoTo(character, character, true)
                    {
                        AllowGoingOutside = true
                    };
                    break;
                case "fixleaks":
                    CurrentOrder = new AIObjectiveFixLeaks(character);
                    break;
                case "chargebatteries":
                    CurrentOrder = new AIObjectiveChargeBatteries(character, option);
                    break;
                case "rescue":
                    CurrentOrder = new AIObjectiveRescueAll(character);
                    break;
                case "repairsystems":
                    CurrentOrder = new AIObjectiveRepairItems(character) { RequireAdequateSkills = option != "all" };
                    break;
                case "pumpwater":
                    CurrentOrder = new AIObjectivePumpWater(character, option);
                    break;
                case "extinguishfires":
                    CurrentOrder = new AIObjectiveExtinguishFires(character);
                    break;
                case "steer":
                    var steering = (order?.TargetEntity as Item)?.GetComponent<Steering>();
                    if (steering != null) steering.PosToMaintain = steering.Item.Submarine?.WorldPosition;
                    if (order.TargetItemComponent == null) return;
                    CurrentOrder = new AIObjectiveOperateItem(order.TargetItemComponent, character, option, false, null, order.UseController);
                    break;
                default:
                    if (order.TargetItemComponent == null) return;
                    CurrentOrder = new AIObjectiveOperateItem(order.TargetItemComponent, character, option, false, null, order.UseController);
                    break;
            }
        }
    }
}
