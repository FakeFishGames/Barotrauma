using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveManager
    {
        // TODO: expose
        public const float OrderPriority = 50.0f;
        // Constantly increases the priority of the selected objective, unless overridden
        public const float baseDevotion = 2;

        public List<AIObjective> Objectives { get; private set; }

        private Character character;

        /// <summary>
        /// When set above zero, the character will stand still doing nothing until the timer runs out. Only affects idling.
        /// </summary>
        public float WaitTimer;

        public AIObjective CurrentOrder { get; private set; }
        public AIObjective CurrentObjective { get; private set; }

        public AIObjectiveManager(Character character)
        {
            this.character = character;

            Objectives = new List<AIObjective>();
        }

        public void AddObjective(AIObjective objective)
        {
            var duplicate = Objectives.Find(o => o.IsDuplicate(objective));
            if (duplicate != null)
            {
                duplicate.Reset();
            }
            else
            {
                Objectives.Add(objective);
            }
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
            foreach (AIObjective objective in Objectives)
            {
                if (objective is T) return (T)objective;
            }
            return null;
        }

        private AIObjective GetCurrentObjective()
        {
            var previousObjective = CurrentObjective;
            var firstObjective = Objectives.FirstOrDefault();
            if (CurrentOrder != null && firstObjective != null && CurrentOrder.GetPriority(this) > firstObjective.GetPriority(this))
            {
                CurrentObjective = CurrentOrder;
            }
            else
            {
                CurrentObjective = firstObjective;
            }
            if (previousObjective != CurrentObjective)
            {
                CurrentObjective?.OnSelected();
            }
            return CurrentObjective;
        }

        public float GetCurrentPriority()
        {
            return CurrentObjective == null ? 0.0f : CurrentObjective.GetPriority(this);
        }

        public void UpdateObjectives(float deltaTime)
        {
            CurrentOrder?.Update(this, deltaTime);
            for (int i = 0; i < Objectives.Count; i++)
            {
                var objective = Objectives[i];
                if (objective.IsCompleted())
                {
#if DEBUG
                    DebugConsole.NewMessage($"Removing objective {objective.DebugTag}, because it is completed.");
#endif
                    Objectives.Remove(objective);
                }
                else if (!objective.CanBeCompleted)
                {
#if DEBUG
                    DebugConsole.NewMessage($"Removing objective {objective.DebugTag}, because it cannot be completed.");
#endif
                    Objectives.Remove(objective);
                }
                else
                {
                    objective.Update(this, deltaTime);
                }
            }
            GetCurrentObjective();
        }

        public void SortObjectives()
        {
            if (Objectives.Any())
            {
                Objectives.Sort((x, y) => y.GetPriority(this).CompareTo(x.GetPriority(this)));
            }
            CurrentObjective?.SortSubObjectives(this);
        }
        
        public void DoCurrentObjective(float deltaTime)
        {
            if (WaitTimer > 0.0f) { WaitTimer -= deltaTime; }
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
