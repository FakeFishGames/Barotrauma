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
        public const float baseDevotion = 3;

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

        public bool IsCurrentOrder<T>() where T : AIObjective => CurrentOrder is T;
        public bool IsCurrentObjective<T>() where T : AIObjective => CurrentObjective is T;
        public bool IsActiveObjective<T>() where T : AIObjective => GetActiveObjective() is T;

        public AIObjective GetActiveObjective() => CurrentObjective?.GetActiveObjective();
        /// <summary>
        /// Returns the last active objective of the specific type.
        /// </summary>
        public T GetActiveObjective<T>() where T : AIObjective => CurrentObjective?.GetSubObjectivesRecursive(includingSelf: true).LastOrDefault(so => so is T) as T;

        /// <summary>
        /// Returns all active objectives of the specific type. Creates a new collection -> don't use too frequently.
        /// </summary>
        public IEnumerable<T> GetActiveObjectives<T>() where T : AIObjective => CurrentObjective?.GetSubObjectivesRecursive(includingSelf: true).Where(so => so is T).Select(so => so as T);

        public bool HasActiveObjective<T>() where T : AIObjective => CurrentObjective is T || CurrentObjective != null && CurrentObjective.GetSubObjectivesRecursive().Any(so => so is T);

        public AIObjectiveManager(Character character)
        {
            this.character = character;
            CreateAutonomousObjectives();
        }

        public void AddObjective<T>(T objective) where T : AIObjective
        {
            if (objective == null)
            {
#if DEBUG
                DebugConsole.ThrowError("Attempted to add a null objective to AIObjectiveManager\n" + Environment.StackTrace);
#endif
                return;
            }
            // Can't use the generic type, because it's possible that the user of this method uses the base type AIObjective.
            // We need to get the highest type.
            var type = objective.GetType();
            if (objective.AllowMultipleInstances)
            {
                if (Objectives.FirstOrDefault(o => o.GetType() == type) is T existingObjective && existingObjective.IsDuplicate(objective))
                {
                    Objectives.Remove(existingObjective);
                }
            }
            else
            {
                Objectives.RemoveAll(o => o.GetType() == type);
            }
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
                var orderPrefab = Order.GetPrefab(automaticOrder.identifier);
                if (orderPrefab == null) { throw new Exception($"Could not find a matching prefab by the identifier: '{automaticOrder.identifier}'"); }
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
            _waitTimer = Math.Max(_waitTimer, Rand.Range(0.5f, 1f) * objectiveCount);
        }

        public void AddObjective<T>(T objective, float delay, Action callback = null) where T : AIObjective
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
                //round ended before the coroutine finished
                if (GameMain.GameSession == null || Level.Loaded == null) { return; }
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
            if (CurrentOrder != null && firstObjective != null && CurrentOrder.Priority > firstObjective.Priority)
            {
                CurrentObjective = CurrentOrder;
            }
            else
            {
                CurrentObjective = firstObjective;
            }
            if (previousObjective != CurrentObjective)
            {
                previousObjective?.OnDeselected();
                CurrentObjective?.OnSelected();
                GetObjective<AIObjectiveIdle>().CalculatePriority();
            }
            return CurrentObjective;
        }

        public float GetCurrentPriority()
        {
            return CurrentObjective == null ? 0.0f : CurrentObjective.Priority;
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
                Objectives.ForEach(o => o.GetPriority());
                Objectives.Sort((x, y) => y.Priority.CompareTo(x.Priority));
            }
            GetCurrentObjective()?.SortSubObjectives();
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
            switch (order.Identifier.ToLowerInvariant())
            {
                case "follow":
                    if (orderGiver == null) { return null; }
                    newObjective = new AIObjectiveGoTo(orderGiver, character, this, repeat: true, priorityModifier: priorityModifier)
                    {
                        CloseEnough = 100,
                        AllowGoingOutside = true,
                        IgnoreIfTargetDead = true,
                        followControlledCharacter = orderGiver == character,
                        mimic = true,
                        DialogueIdentifier = "dialogcannotreachplace"
                    };
                    break;
                case "wait":
                    newObjective = new AIObjectiveGoTo(character, character, this, repeat: true, priorityModifier: priorityModifier)
                    {
                        AllowGoingOutside = character.CurrentHull == null
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
                    newObjective = new AIObjectiveRepairItems(character, this, priorityModifier)
                    {
                        RequireAdequateSkills = option == "jobspecific"
                    };
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
                        // Don't override unless it's an order by a player
                        Override = orderGiver != null && orderGiver.IsPlayer
                    };
                    break;
                default:
                    if (order.TargetItemComponent == null) { return null; }
                    newObjective = new AIObjectiveOperateItem(order.TargetItemComponent, character, this, option, requireEquip: false, useController: order.UseController, priorityModifier: priorityModifier)
                    {
                        IsLoop = true,
                        // Don't override unless it's an order by a player
                        Override = orderGiver != null && orderGiver.IsPlayer
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
