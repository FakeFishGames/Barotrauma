using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class AIObjectiveManager
    {
        // TODO: expose
        public const float OrderPriority = 70;
        public const float RunPriority = 50;
        // Constantly increases the priority of the selected objective, unless overridden
        public const float baseDevotion = 2;

        public List<AIObjective> Objectives { get; private set; } = new List<AIObjective>();

        private readonly Character character;


        private float _waitTimer;
        /// <summary>
        /// When set above zero, the character will stand still doing nothing until the timer runs out. Does not affect orders, find safety or combat.
        /// </summary>
        public float WaitTimer
        {
            get { return _waitTimer; }
            set
            {
                _waitTimer = IsAllowedToWait() ? value : 0;
            }
        }

        public AIObjective CurrentOrder { get; private set; }
        public AIObjective CurrentObjective { get; private set; }

        public bool IsCurrentObjective<T>() where T : AIObjective => CurrentObjective is T;

        public AIObjective GetActiveObjective() => CurrentObjective?.GetActiveObjective();

        public bool HasActiveObjective<T>() where T : AIObjective => CurrentObjective is T || CurrentObjective.GetSubObjectivesRecursive().Any(so => so is T);

        public AIObjectiveManager(Character character)
        {
            this.character = character;
            CreateAutonomousObjectives();
        }

        public void AddObjective(AIObjective objective)
        {
            if (objective == null)
            {
#if DEBUG
                DebugConsole.ThrowError("Attempted to add a null objective to AIObjectiveManager\n" + Environment.StackTrace);
#endif
                return;
            }
            var type = objective.GetType();
            Objectives.RemoveAll(o => o.GetType() == type);
            Objectives.Add(objective);
        }

        public Dictionary<AIObjective, CoroutineHandle> DelayedObjectives { get; private set; } = new Dictionary<AIObjective, CoroutineHandle>();

        public void CreateAutonomousObjectives()
        {
            foreach (var delayedObjective in DelayedObjectives)
            {
                CoroutineManager.StopCoroutines(delayedObjective.Value);
            }
            DelayedObjectives.Clear();
            Objectives.Clear();
            AddObjective(new AIObjectiveFindSafety(character, this));
            AddObjective(new AIObjectiveIdle(character, this));
            int objectiveCount = Objectives.Count;
            foreach (var automaticOrder in character.Info.Job.Prefab.AutomaticOrders)
            {
                var orderPrefab = Order.PrefabList.Find(o => o.AITag == automaticOrder.aiTag);
                if (orderPrefab == null) { throw new Exception("Could not find a matching prefab by ai tag: " + automaticOrder.aiTag); }
                // TODO: Similar code is used in CrewManager:815-> DRY
                var matchingItems = orderPrefab.ItemIdentifiers.Any() ?
                    Item.ItemList.FindAll(it => orderPrefab.ItemIdentifiers.Contains(it.Prefab.Identifier) || it.HasTag(orderPrefab.ItemIdentifiers)) :
                    Item.ItemList.FindAll(it => it.Components.Any(ic => ic.GetType() == orderPrefab.ItemComponentType));
                matchingItems.RemoveAll(it => it.Submarine != character.Submarine);
                var item = matchingItems.GetRandom();
                var order = new Order(
                    orderPrefab, 
                    item ?? character.CurrentHull as Entity, 
                    item?.Components.FirstOrDefault(ic => ic.GetType() == orderPrefab.ItemComponentType),
                    orderGiver: character);
                if (order == null) { continue; }
                var objective = CreateObjective(order, automaticOrder.option, character, automaticOrder.priorityModifier);
                if (objective != null)
                {
                    AddObjective(objective, delay: Rand.Value() / 2);
                    objectiveCount++;
                }
            }
            WaitTimer = Math.Max(WaitTimer, Rand.Range(0.5f, 1f) * objectiveCount);
        }

        public void AddObjective(AIObjective objective, float delay, Action callback = null)
        {
            if (objective == null)
            {
#if DEBUG
                DebugConsole.ThrowError($"{character.Name}: Attempted to add a null objective to AIObjectiveManager\n" + Environment.StackTrace);
#endif
                return;
            }
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

        public T GetObjective<T>() where T : AIObjective => Objectives.FirstOrDefault(o => o is T) as T;

        private AIObjective GetCurrentObjective()
        {
            var previousObjective = CurrentObjective;
            var firstObjective = Objectives.FirstOrDefault();
            if (CurrentOrder != null && firstObjective != null && CurrentOrder.GetPriority() > firstObjective.GetPriority())
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
                GetObjective<AIObjectiveIdle>().SetRandom();
            }
            return CurrentObjective;
        }

        public float GetCurrentPriority()
        {
            return CurrentObjective == null ? 0.0f : CurrentObjective.GetPriority();
        }

        public void UpdateObjectives(float deltaTime)
        {
            CurrentOrder?.Update(deltaTime);
            if (WaitTimer > 0)
            {
                WaitTimer -= deltaTime;
                return;
            }
            for (int i = 0; i < Objectives.Count; i++)
            {
                var objective = Objectives[i];
                if (objective.IsCompleted)
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Removing objective {objective.DebugTag}, because it is completed.", Color.LightGreen);
#endif
                    Objectives.Remove(objective);
                }
                else if (!objective.CanBeCompleted)
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Removing objective {objective.DebugTag}, because it cannot be completed.", Color.Red);
#endif
                    Objectives.Remove(objective);
                }
                else if (objective != CurrentOrder)
                {
                    objective.Update(deltaTime);
                }
            }
            GetCurrentObjective();
        }

        public void SortObjectives()
        {
            if (Objectives.Any())
            {
                Objectives.Sort((x, y) => y.GetPriority().CompareTo(x.GetPriority()));
            }
            // Not sure if we should mess with the subobjective order at all.
            //CurrentObjective?.SortSubObjectives();
        }
        
        public void DoCurrentObjective(float deltaTime)
        {
            if (WaitTimer <= 0)
            {
                CurrentObjective?.TryComplete(deltaTime);
            }
            else
            {
                character.AIController.SteeringManager.Reset();
            }
        }
        
        public void SetOrder(AIObjective objective)
        {
            CurrentOrder = objective;
        }

        public void SetOrder(Order order, string option, Character orderGiver)
        {
            CurrentOrder = CreateObjective(order, option, orderGiver);
            if (CurrentOrder == null)
            {
                // Recreate objectives, because some of them may be removed, if impossible to complete (e.g. due to path finding)
                CreateAutonomousObjectives();
            }
            else
            {
                CurrentOrder.Reset();
            }
        }

        public AIObjective CreateObjective(Order order, string option, Character orderGiver, float priorityModifier = 1)
        {
            if (order == null) { return null; }
            AIObjective newObjective;
            switch (order.AITag.ToLowerInvariant())
            {
                case "follow":
                    if (orderGiver == null) { return null; }
                    newObjective = new AIObjectiveGoTo(orderGiver, character, this, repeat: true, priorityModifier: priorityModifier)
                    {
                        CloseEnough = 100,
                        AllowGoingOutside = true,
                        IgnoreIfTargetDead = true,
                        followControlledCharacter = orderGiver == character,
                        mimic = true
                    };
                    break;
                case "wait":
                    newObjective = new AIObjectiveGoTo(character, character, this, repeat: true, priorityModifier: priorityModifier)
                    {
                        AllowGoingOutside = true
                    };
                    break;
                case "fixleaks":
                    newObjective = new AIObjectiveFixLeaks(character, this, priorityModifier);
                    break;
                case "chargebatteries":
                    newObjective = new AIObjectiveChargeBatteries(character, this, option, priorityModifier);
                    break;
                case "rescue":
                    newObjective = new AIObjectiveRescueAll(character, this, priorityModifier);
                    break;
                case "repairsystems":
                    newObjective = new AIObjectiveRepairItems(character, this, priorityModifier) { RequireAdequateSkills = option == "jobspecific" };
                    break;
                case "pumpwater":
                    newObjective = new AIObjectivePumpWater(character, this, option, priorityModifier: priorityModifier);
                    break;
                case "extinguishfires":
                    newObjective = new AIObjectiveExtinguishFires(character, this, priorityModifier);
                    break;
                case "fightintruders":
                    newObjective = new AIObjectiveFightIntruders(character, this, priorityModifier);
                    break;
                case "steer":
                    var steering = (order?.TargetEntity as Item)?.GetComponent<Steering>();
                    if (steering != null) steering.PosToMaintain = steering.Item.Submarine?.WorldPosition;
                    if (order.TargetItemComponent == null) { return null; }
                    newObjective = new AIObjectiveOperateItem(order.TargetItemComponent, character, this, option, requireEquip: false, useController: order.UseController, priorityModifier: priorityModifier)
                    {
                        IsLoop = true,
                        // Don't override auto pilot unless it's an order by a player
                        Override = orderGiver == Character.Controlled || orderGiver.IsRemotePlayer
                    };
                    break;
                default:
                    if (order.TargetItemComponent == null) { return null; }
                    newObjective = new AIObjectiveOperateItem(order.TargetItemComponent, character, this, option, requireEquip: false, useController: order.UseController, priorityModifier: priorityModifier)
                    {
                        IsLoop = true
                    };
                    break;
            }
            return newObjective;
        }

        private bool IsAllowedToWait()
        {
            if (CurrentOrder != null) { return false; }
            if (CurrentObjective is AIObjectiveCombat || CurrentObjective is AIObjectiveFindSafety) { return false; }
            if (character.AnimController.InWater) { return false; }
            if (character.IsClimbing) { return false; }
            if (character.AIController is HumanAIController humanAI)
            {
                if (humanAI.UnsafeHulls.Contains(character.CurrentHull)) { return false; }
            }
            if (AIObjectiveIdle.IsForbidden(character.CurrentHull)) { return false; }
            return true;
        }
    }
}
