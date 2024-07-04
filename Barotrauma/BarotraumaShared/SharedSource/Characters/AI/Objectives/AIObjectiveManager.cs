using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking; // used by the server
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveManager
    {
        public enum ObjectiveType
        {
            None = 0,
            Order = 1,
            Objective = 2,
            
            MinValue = 0,
            MaxValue = 2
        }

        /// <summary>
        /// Highest possible priority for any objective. Used in certain cases where the character needs to react immediately to survive, 
        /// such as finding a suit when under pressure or getting out of a burning room.
        /// </summary>
        public const float MaxObjectivePriority = 100;
        /// <summary>
        /// Priority of objectives such as finding safety, rescuing someone in a critical state or defending against an attacker 
        /// (= objectives that are critical for saving the character's or someone else's life)
        /// </summary>
        public const float EmergencyObjectivePriority = 90;
        /// <summary>
        /// Maximum priority of an order given to the character (forced order, or the leftmost order in the crew list)
        /// </summary>
        public const float HighestOrderPriority = 70;
        /// <summary>
        /// Maximum priority of an order given to the character (rightmost order in the crew list)
        /// </summary>
        public const float LowestOrderPriority = 60;
        /// <summary>
        /// Objectives with a priority equal to or higher than this make the character run.
        /// </summary>
        public const float RunPriority = 50;
        // Constantly increases the priority of the selected objective, unless overridden
        public const float baseDevotion = 5;

        /// <summary>
        /// Excluding the current order.
        /// </summary>
        public List<AIObjective> Objectives { get; private set; } = new List<AIObjective>();

        private readonly Character character;

        public HumanAIController HumanAIController => character.AIController as HumanAIController;

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

        public List<Order> CurrentOrders { get; } = new List<Order>();
        /// <summary>
        /// The AIObjective in <see cref="CurrentOrders"/> with the highest <see cref="AIObjective.Priority"/>
        /// </summary>
        public AIObjective CurrentOrder
        {
            get
            {
                return ForcedOrder ?? currentOrder;
            }
            private set
            {
                currentOrder = value;
            }
        }
        private AIObjective currentOrder;
        public AIObjective ForcedOrder { get; private set; }
        
        /// <summary>
        /// Includes orders.
        /// </summary>
        public AIObjective CurrentObjective { get; private set; }

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
                DebugConsole.ThrowError("Attempted to add a null objective to AIObjectiveManager\n" + Environment.StackTrace.CleanupStackTrace());
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
        public bool FailedAutonomousObjectives { get; private set; }

        public bool FailedToFindDivingGearForDepth;

        private void ClearIgnored()
        {
            if (character.AIController is HumanAIController humanAi)
            {
                humanAi.UnreachableHulls.Clear();
                humanAi.IgnoredItems.Clear();
            }
        }

        public void CreateAutonomousObjectives()
        {
            if (character.IsDead)
            {
#if DEBUG
                DebugConsole.ThrowError("Attempted to create autonomous orders for a dead character");
#else
                return;
#endif
            }

            foreach (var delayedObjective in DelayedObjectives)
            {
                CoroutineManager.StopCoroutines(delayedObjective.Value);
            }

            var prevIdleObjective = GetObjective<AIObjectiveIdle>();

            DelayedObjectives.Clear();
            Objectives.Clear();
            FailedAutonomousObjectives = false;
            AddObjective(new AIObjectiveFindSafety(character, this));
            var newIdleObjective = new AIObjectiveIdle(character, this);
            if (prevIdleObjective != null)
            {
                newIdleObjective.TargetHull = prevIdleObjective.TargetHull;
                newIdleObjective.Behavior = prevIdleObjective.Behavior;
                prevIdleObjective.PreferredOutpostModuleTypes.ForEach(t => newIdleObjective.PreferredOutpostModuleTypes.Add(t));
            }
            AddObjective(newIdleObjective);

            int objectiveCount = Objectives.Count;
            if (character.Info?.Job != null)
            {
                foreach (var autonomousObjective in character.Info.Job.Prefab.AutonomousObjectives)
                {
                    var orderPrefab = OrderPrefab.Prefabs[autonomousObjective.Identifier] ?? throw new Exception($"Could not find a matching prefab by the identifier: '{autonomousObjective.Identifier}'");
                    Item item = null;
                    if (orderPrefab.MustSetTarget)
                    {
                        item = orderPrefab.GetMatchingItems(character.Submarine, mustBelongToPlayerSub: false, requiredTeam: character.Info.TeamID, interactableFor: character)?.GetRandomUnsynced();
                    }
                    var order = new Order(orderPrefab, autonomousObjective.Option, item ?? character.CurrentHull as Entity, orderPrefab.GetTargetItemComponent(item), orderGiver: character);
                    if (order == null) { continue; }
                    if ((order.IgnoreAtOutpost || autonomousObjective.IgnoreAtOutpost) && 
                        Level.IsLoadedFriendlyOutpost && character.TeamID != CharacterTeamType.FriendlyNPC && !character.IsFriendlyNPCTurnedHostile)
                    {
                        if (Submarine.MainSub != null && Submarine.MainSub.DockedTo.None(s => s.TeamID != CharacterTeamType.FriendlyNPC && s.TeamID != character.TeamID))
                        {
                            continue;
                        }
                    }
                    if (autonomousObjective.IgnoreAtNonOutpost && !Level.IsLoadedFriendlyOutpost)
                    {
                        continue;
                    }
                    var objective = CreateObjective(order, autonomousObjective.PriorityModifier);
                    if (objective != null && objective.CanBeCompleted)
                    {
                        AddObjective(objective, delay: Rand.Value() / 2);
                        objectiveCount++;
                    }
                }       
            }
            else
            {
                string warningMsg = character.Info == null ?
                    $"The character {character.DisplayName} has been set to use human ai, but has no {nameof(CharacterInfo)}. This may cause issues with the AI. Consider adding {nameof(CharacterPrefab.HasCharacterInfo)}=\"True\" to the character config." :
                    $"The character {character.DisplayName} has been set to use human ai, but has no job. This may cause issues with the AI. Consider configuring some jobs for the character type.";
                DebugConsole.AddWarning(warningMsg, character.Prefab.ContentPackage);
            }
   
            _waitTimer = Math.Max(_waitTimer, Rand.Range(0.5f, 1f) * objectiveCount);
        }

        public void AddObjective<T>(T objective, float delay, Action callback = null) where T : AIObjective
        {
            if (objective == null)
            {
#if DEBUG
                DebugConsole.ThrowError($"{character.Name}: Attempted to add a null objective to AIObjectiveManager\n" + Environment.StackTrace.CleanupStackTrace());
#endif
                return;
            }
            if (DelayedObjectives.TryGetValue(objective, out CoroutineHandle coroutine))
            {
                CoroutineManager.StopCoroutines(coroutine);
                DelayedObjectives.Remove(objective);
            }
            coroutine = CoroutineManager.Invoke(() =>
            {
                //round ended before the coroutine finished
#if CLIENT
                if (GameMain.GameSession == null || Level.Loaded == null && !(GameMain.GameSession.GameMode is TestGameMode)) { return; }
#else
                if (GameMain.GameSession == null || Level.Loaded == null) { return; }
#endif
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
            
            bool currentObjectiveIsOrder = CurrentOrder != null && firstObjective != null && CurrentOrder.Priority > firstObjective.Priority;
            
            CurrentObjective = currentObjectiveIsOrder ? CurrentOrder : firstObjective;

            if (previousObjective == CurrentObjective) { return CurrentObjective; }

            previousObjective?.OnDeselected();
            if (CurrentObjective != null)
            {
                CurrentObjective.OnSelected();
                GetObjective<AIObjectiveIdle>().CalculatePriority(Math.Max(CurrentObjective.Priority - 10, 0));   
            }
            if (GameMain.NetworkMember is { IsServer: true })
            {
                GameMain.NetworkMember.CreateEntityEvent(character,
                    new Character.ObjectiveManagerStateEventData(currentObjectiveIsOrder ? ObjectiveType.Order : ObjectiveType.Objective));
            }
            return CurrentObjective;
        }

        /// <summary>
        /// Returns the highest priority of the current objective and its subobjectives.
        /// </summary>
        public float GetCurrentPriority()
        {
            if (CurrentObjective == null) { return 0; }
            float subObjectivePriority = CurrentObjective.SubObjectives.Any() ? CurrentObjective.SubObjectives.Max(so => so.Priority) : 0;
            return Math.Max(CurrentObjective.Priority, subObjectivePriority);
        }

        public void UpdateObjectives(float deltaTime)
        {
            UpdateOrderObjective(ForcedOrder);

            if (CurrentOrders.Any())
            {
                foreach (var order in CurrentOrders)
                {
                    var orderObjective = order.Objective;
                    UpdateOrderObjective(orderObjective);
                }
            }

            void UpdateOrderObjective(AIObjective orderObjective)
            {
                if (orderObjective == null) { return; }
#if DEBUG
                // Note: don't automatically remove orders here. Removing orders needs to be done via dismissing.
                if (!orderObjective.CanBeCompleted)
                {
                    DebugConsole.NewMessage($"{character.Name}: ORDER {orderObjective.DebugTag}, CANNOT BE COMPLETED.", Color.Red);
                }
#endif
                orderObjective.Update(deltaTime);
            }

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
                    DebugConsole.NewMessage($"{character.Name}: Removing objective {objective.DebugTag}, because it is completed.", Color.LightBlue);
#endif
                    Objectives.Remove(objective);
                }
                else if (!objective.CanBeCompleted)
                {
#if DEBUG
                    DebugConsole.NewMessage($"{character.Name}: Removing objective {objective.DebugTag}, because it cannot be completed.", Color.Red);
#endif
                    Objectives.Remove(objective);
                    FailedAutonomousObjectives = true;
                }
                else
                {
                    objective.Update(deltaTime);
                }
            }
            GetCurrentObjective();
        }

        public void SortObjectives()
        {
            ForcedOrder?.CalculatePriority();
            AIObjective orderWithHighestPriority = null;
            float highestPriority = 0;
            for (int i = CurrentOrders.Count - 1; i >= 0; i--)
            {
                if (CurrentOrders.Count <= i) { break; }
                var orderObjective = CurrentOrders[i].Objective;
                if (orderObjective == null) { continue; }
                orderObjective.CalculatePriority();
                if (orderWithHighestPriority == null || orderObjective.Priority > highestPriority)
                {
                    orderWithHighestPriority = orderObjective;
                    highestPriority = orderObjective.Priority;
                }
            }
            CurrentOrder = orderWithHighestPriority;
            for (int i = Objectives.Count - 1; i >= 0; i--)
            {
                Objectives[i].CalculatePriority();
            }
            if (Objectives.Any())
            {
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

        public void SetForcedOrder(AIObjective objective)
        {
            ForcedOrder = objective;
        }

        public void ClearForcedOrder()
        {
            ForcedOrder = null;
            SortObjectives();
        }

        public void SetOrder(Order order, bool speak)
        {
            if (character.IsDead)
            {
#if DEBUG
                DebugConsole.ThrowError("Attempted to set an order for a dead character");
#else
                return;
#endif
            }
            ClearIgnored();

            if (order == null || order.IsDismissal)
            {
                if (order.Option != Identifier.Empty)
                {
                    if (CurrentOrders.Any(o => o.MatchesDismissedOrder(order.Option)))
                    {
                        var dismissedOrderInfo = CurrentOrders.First(o => o.MatchesDismissedOrder(order.Option));
                        CurrentOrders.Remove(dismissedOrderInfo);
                    }
                }
                else
                {
                    CurrentOrders.Clear();
                }
            }

            // Make sure the order priorities reflect those set by the player
            for (int i = CurrentOrders.Count - 1; i >= 0; i--)
            {
                if (CurrentOrders.Count <= i) { break; }
                var currentOrder = CurrentOrders[i];
                if (currentOrder.Objective == null || currentOrder.MatchesOrder(order))
                {
                    CurrentOrders.RemoveAt(i);
                    continue;
                }
                if (character.GetCurrentOrder(currentOrder) is Order currentOrderInfo)
                {
                    int currentPriority = currentOrderInfo.ManualPriority;
                    if (currentOrder.ManualPriority != currentPriority)
                    {
                        CurrentOrders[i] = currentOrder.WithManualPriority(currentPriority);
                    }
                }
                else
                {
                    CurrentOrders.RemoveAt(i);
                }
            }

            //reset this here so the bots can retry finding a better suit if it's needed for the new order
            FailedToFindDivingGearForDepth = false;

            var newCurrentObjective = CreateObjective(order);
            if (newCurrentObjective != null)
            {
                newCurrentObjective.Abandoned += () => DismissSelf(order);
                CurrentOrders.Add(order.WithObjective(newCurrentObjective));
            }
            if (!HasOrders())
            {
                // Recreate objectives, because some of them may be removed, if impossible to complete (e.g. due to path finding)
                CreateAutonomousObjectives();
            }
            else if (newCurrentObjective != null)
            {
                if (speak && character.IsOnPlayerTeam)
                {
                    LocalizedString msg = newCurrentObjective.IsAllowed ? TextManager.Get("DialogAffirmative") : TextManager.Get("DialogNegative");
                    character.Speak(msg.Value, delay: 1.0f);
                }
            }
        }

        public AIObjective CreateObjective(Order order, float priorityModifier = 1)
        {
            if (order == null || order.IsDismissal) { return null; }
            AIObjective newObjective;
            switch (order.Identifier.Value.ToLowerInvariant())
            {
                case "follow":
                    if (order.OrderGiver == null) { return null; }
                    newObjective = new AIObjectiveGoTo(order.OrderGiver, character, this, repeat: true, priorityModifier: priorityModifier)
                    {
                        CloseEnough = Rand.Range(80f, 100f),
                        CloseEnoughMultiplier = Math.Min(1 + HumanAIController.CountBotsInTheCrew(c => c.ObjectiveManager.HasOrder<AIObjectiveGoTo>(o => o.Target == order.OrderGiver)) * Rand.Range(0.8f, 1f), 4),
                        ExtraDistanceOutsideSub = 100,
                        ExtraDistanceWhileSwimming = 100,
                        AllowGoingOutside = true,
                        IgnoreIfTargetDead = true,
                        IsFollowOrder = true,
                        Mimic = character.IsOnPlayerTeam,
                        DialogueIdentifier = "dialogcannotreachplace".ToIdentifier()
                    };
                    break;
                case "wait":
                    newObjective = new AIObjectiveGoTo(order.TargetSpatialEntity ?? character, character, this, repeat: true, priorityModifier: priorityModifier)
                    {
                        AllowGoingOutside = true,
                        IsWaitOrder = true,
                        DebugLogWhenFails = false,
                        SpeakIfFails = false,
                        CloseEnough = 100
                    };
                    break;
                case "return":
                    newObjective = new AIObjectiveReturn(character, order.OrderGiver, this, priorityModifier: priorityModifier);
                    newObjective.Completed += () => DismissSelf(order);
                    break;
                case "fixleaks":
                    newObjective = new AIObjectiveFixLeaks(character, this, priorityModifier: priorityModifier, prioritizedHull: order.TargetEntity as Hull);
                    break;
                case "chargebatteries":
                    newObjective = new AIObjectiveChargeBatteries(character, this, order.Option, priorityModifier);
                    break;
                case "rescue":
                    newObjective = new AIObjectiveRescueAll(character, this, priorityModifier);
                    break;
                case "repairsystems":
                case "repairmechanical":
                case "repairelectrical":
                    newObjective = new AIObjectiveRepairItems(character, this, priorityModifier: priorityModifier, prioritizedItem: order.TargetEntity as Item)
                    {
                        RelevantSkill = order.AppropriateSkill,
                    };
                    break;
                case "pumpwater":
                    if (order.TargetItemComponent is Pump targetPump)
                    {
                        if (!order.TargetItemComponent.Item.IsInteractable(character)) { return null; }
                        newObjective = new AIObjectiveOperateItem(targetPump, character, this, order.Option, false, priorityModifier: priorityModifier)
                        {
                            Override = order.OrderGiver is { IsCommanding: true }
                        };
                        newObjective.Completed += () => DismissSelf(order);
                    }
                    else
                    {
                        newObjective = new AIObjectivePumpWater(character, this, order.Option, priorityModifier: priorityModifier);
                    }
                    break;
                case "extinguishfires":
                    newObjective = new AIObjectiveExtinguishFires(character, this, priorityModifier);
                    break;
                case "fightintruders":
                    newObjective = new AIObjectiveFightIntruders(character, this, priorityModifier);
                    break;
                case "assaultenemy":
                    newObjective = new AIObjectiveFightIntruders(character, this, priorityModifier)
                    {
                        TargetCharactersInOtherSubs = true
                    };
                    break;
                case "steer":
                    var steering = (order?.TargetEntity as Item)?.GetComponent<Steering>();
                    if (steering != null) { steering.PosToMaintain = steering.Item.Submarine?.WorldPosition; }
                    if (order.TargetItemComponent == null) { return null; }
                    if (!order.TargetItemComponent.Item.IsInteractable(character)) { return null; }
                    newObjective = new AIObjectiveOperateItem(order.TargetItemComponent, character, this, order.Option,
                        requireEquip: false, useController: order.UseController, controller: order.ConnectedController, priorityModifier: priorityModifier)
                    {
                        Repeat = true,
                        // Don't override unless it's an order by a player
                        Override = order.OrderGiver != null && order.OrderGiver.IsCommanding 
                    };
                    break;
                case "setchargepct":
                    newObjective = new AIObjectiveOperateItem(order.TargetItemComponent, character, this, order.Option, false, priorityModifier: priorityModifier)
                    {
                        Override = !character.IsDismissed,
                        completionCondition = () =>
                        {
                            if (float.TryParse(order.Option.Value, out float pct))
                            {
                                var targetRatio = Math.Clamp(pct, 0f, 1f);
                                var currentRatio = (order.TargetItemComponent as PowerContainer).RechargeRatio;
                                return Math.Abs(targetRatio - currentRatio) < 0.05f;
                            }
                            return true;
                        }
                    };
                    break;
                case "getitem":
                    newObjective = new AIObjectiveGetItem(character, order.TargetEntity as Item ?? order.TargetItemComponent?.Item, this, false, priorityModifier: priorityModifier)
                    {
                        MustBeSpecificItem = true
                    };
                    break;
                case "cleanupitems":
                    if (order.TargetEntity is Item targetItem)
                    {
                        if (targetItem.HasTag(Tags.AllowCleanup) && targetItem.ParentInventory == null && targetItem.OwnInventory != null)
                        {
                            // Target all items inside the container
                            newObjective = new AIObjectiveCleanupItems(character, this, targetItem.OwnInventory.AllItems, priorityModifier);
                        }
                        else
                        {
                            newObjective = new AIObjectiveCleanupItems(character, this, targetItem, priorityModifier);
                        }
                    }
                    else
                    {
                        newObjective = new AIObjectiveCleanupItems(character, this, priorityModifier: priorityModifier);
                    }
                    break;
                case "escapehandcuffs":
                    newObjective = new AIObjectiveEscapeHandcuffs(character, this, priorityModifier: priorityModifier);
                    break;
                case "findthieves":
                    newObjective = new AIObjectiveFindThieves(character, this, priorityModifier: priorityModifier);
                    break;
                case "prepareforexpedition":
                    newObjective = new AIObjectivePrepare(character, this, order.GetTargetItems(order.Option), order.RequireItems)
                    {
                        KeepActiveWhenReady = true,
                        CheckInventory = true,
                        Equip = false,
                        FindAllItems = true,
                        RequireNonEmpty = false
                    };
                    break;
                case "findweapon":
                    AIObjectivePrepare prepareObjective;
                    if (order.TargetEntity is Item tItem)
                    {
                        prepareObjective = new AIObjectivePrepare(character, this, targetItem: tItem);
                    }
                    else
                    {
                        prepareObjective = new AIObjectivePrepare(character, this, order.GetTargetItems(order.Option), order.RequireItems)
                        {
                            CheckInventory = false,
                            EvaluateCombatPriority = true,
                            FindAllItems = false,
                            RequireNonEmpty = true
                        };
                    }
                    prepareObjective.KeepActiveWhenReady = false;
                    prepareObjective.Equip = true;
                    newObjective = prepareObjective;
                    newObjective.Completed += () => DismissSelf(order);
                    break;
                case "loaditems":
                    newObjective = new AIObjectiveLoadItems(character, this, order.Option, order.GetTargetItems(order.Option), order.TargetEntity as Item, priorityModifier);
                    break;
                case "deconstructitems":
                    newObjective = new AIObjectiveDeconstructItems(character, this, priorityModifier);
                    break;
                case "inspectnoises":
                    newObjective = new AIObjectiveInspectNoises(character, this, priorityModifier);
                    break;
                default:
                    if (order.TargetItemComponent == null) { return null; }
                    if (!order.TargetItemComponent.Item.IsInteractable(character)) { return null; }
                    newObjective = new AIObjectiveOperateItem(order.TargetItemComponent, character, this, order.Option,
                        requireEquip: false, useController: order.UseController, controller: order.ConnectedController, priorityModifier: priorityModifier)
                    {
                        Repeat = true,
                        // Don't override unless it's an order by a player
                        Override = order.OrderGiver != null && order.OrderGiver.IsCommanding
                    };
                    if (newObjective.Abandon) { return null; }
                    break;
            }
            if (newObjective != null)
            {
                newObjective.Identifier = order.Identifier;
            }
            newObjective.IgnoreAtOutpost = order.IgnoreAtOutpost;
            return newObjective;
        }

        /// <summary>
        /// Sets the order as dismissed, and enables the option to reissue the order on the crew list. 
        /// Note that this is not the same thing as just removing the order entirely!
        /// </summary>
        /// <param name="order"></param>
        private void DismissSelf(Order order)
        {
            var currentOrder = CurrentOrders.FirstOrDefault(oi => oi.MatchesOrder(order.Identifier, order.Option));
            if (currentOrder == null)
            {
#if DEBUG
                DebugConsole.ThrowError("Tried to self-dismiss an order, but no matching current order was found");
#endif
                return;
            }

            Order dismissOrder = currentOrder.GetDismissal();
#if CLIENT
            if (GameMain.GameSession?.CrewManager is CrewManager cm && cm.IsSinglePlayer)
            {
                character.SetOrder(dismissOrder, isNewOrder: true, speak: false);
            }
#else
            GameMain.Server?.SendOrderChatMessage(new OrderChatMessage(dismissOrder, character, character));
            SetOrder(dismissOrder, speak: false);
#endif
        }

        private bool IsAllowedToWait()
        {
            if (!character.IsOnPlayerTeam) { return false; }
            if (HasOrders()) { return false; }
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

        /// <summary>
        /// Only checks the current order. Deprecated, use pattern matching instead.
        /// </summary>
        public bool IsCurrentOrder<T>() where T : AIObjective => CurrentOrder is T;
        
        /// <summary>
        /// Checks the current objective (which can be an order too). Deprecated, use pattern matching instead.
        /// </summary>
        public bool IsCurrentObjective<T>() where T : AIObjective => CurrentObjective is T;
        
        /// <summary>
        /// Checks if any objectives or orders are of the specified type. Regardless of whether the objective is active or inactive.
        /// </summary>
        public bool HasObjectiveOrOrder<T>() where T : AIObjective =>  Objectives.Any(o => o is T) || HasOrder<T>();

        public AIObjective GetActiveObjective() => CurrentObjective?.GetActiveObjective();

        /// <summary>
        /// Return the first order whose objective is of the given type. Can return null.
        /// </summary>
        public T GetOrder<T>() where T : AIObjective => CurrentOrders.FirstOrDefault(o => o.Objective is T)?.Objective as T;

        /// <summary>
        /// Return the first order with the specified objective. Can return null.
        /// </summary>
        public Order GetOrder(AIObjective objective) => CurrentOrders.FirstOrDefault(o => o.Objective == objective);
        
        /// <summary>
        /// Returns the last active objective of the specified objective type.
        /// Should generally be used to get the active objective (or subobjective) of objectives that don't sort their subobjectives by priority (see <see cref="AIObjective.AllowSubObjectiveSorting"/>.
        /// </summary>
        /// <returns>The last active objective of the specified type if found.
        /// </returns>
        public T GetLastActiveObjective<T>() where T : AIObjective
            => CurrentObjective?.GetSubObjectivesRecursive(includingSelf: true).LastOrDefault(so => so is T) as T;
        
        /// <summary>
        /// Returns the first active objective of the specified objective type.
        /// Should generally be used to get the active objective (or subobjective) of objectives that sort their subobjectives by priority, such as those that inherit <see cref="AIObjectiveLoop"/>.
        /// </summary>
        /// <returns>
        /// The first active objective of the specified type if found.
        /// </returns>
        public T GetFirstActiveObjective<T>() where T : AIObjective
            => CurrentObjective?.GetSubObjectivesRecursive(includingSelf: true).FirstOrDefault(so => so is T) as T;

        /// <summary>
        /// Returns all active objectives of the specific type.
        /// </summary>
        public IEnumerable<T> GetActiveObjectives<T>() where T : AIObjective
        {
            if (CurrentObjective == null) { return Enumerable.Empty<T>(); }
            return CurrentObjective.GetSubObjectivesRecursive(includingSelf: true).OfType<T>();
        }

        public bool HasActiveObjective<T>() where T : AIObjective => CurrentObjective is T || CurrentObjective != null && CurrentObjective.GetSubObjectivesRecursive().Any(so => so is T);

        public bool IsOrder(AIObjective objective)
        {
            if (objective == ForcedOrder) { return true; }
            foreach (var order in CurrentOrders)
            {
                if (order.Objective == objective) { return true; }
            }
            return false;
        }

        public bool HasOrders()
        {
            return ForcedOrder != null || CurrentOrders.Any();
        }

        public bool HasOrder<T>(Func<T, bool> predicate = null) where T : AIObjective =>
                ForcedOrder is T forcedOrder && (predicate == null || predicate(forcedOrder)) || 
                CurrentOrders.Any(o => o.Objective is T order && (predicate == null || predicate(order)));

        public float GetOrderPriority(AIObjective objective)
        {
            if (objective == ForcedOrder)
            {
                return HighestOrderPriority;
            }
            var currentOrder = CurrentOrders.FirstOrDefault(o => o.Objective == objective);
            if (currentOrder.Objective == null)
            {
                return HighestOrderPriority;
            }
            else if (currentOrder.ManualPriority > 0)
            {
                if (objective.ForceHighestPriority)
                {
                    return HighestOrderPriority;
                }
                if (objective.PrioritizeIfSubObjectivesActive && objective.SubObjectives.Any())
                {
                    return HighestOrderPriority;
                }
                return MathHelper.Lerp(LowestOrderPriority, HighestOrderPriority - 1, MathUtils.InverseLerp(1, CharacterInfo.HighestManualOrderPriority, currentOrder.ManualPriority));
            }
#if DEBUG
            DebugConsole.AddWarning("Error in order priority: shouldn't return 0!");
#endif
            return 0;
        }

        public Order GetCurrentOrderInfo()
        {
            if (currentOrder == null) { return null; }
            return CurrentOrders.FirstOrDefault(o => o.Objective == CurrentOrder);
        }
    }
}
