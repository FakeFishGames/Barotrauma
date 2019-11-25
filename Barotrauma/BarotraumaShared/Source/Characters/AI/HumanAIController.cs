using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    partial class HumanAIController : AIController
    {
        public static bool DisableCrewAI;

        private AIObjectiveManager objectiveManager;
        
        private float sortTimer;
        private float crouchRaycastTimer;
        private float reactTimer;
        private float unreachableClearTimer;
        private bool shouldCrouch;

        const float reactionTime = 0.5f;
        const float crouchRaycastInterval = 1;
        const float sortObjectiveInterval = 1;
        const float clearUnreachableInterval = 30;

        private float flipTimer;
        private const float FlipInterval = 0.5f;

        public static float HULL_SAFETY_THRESHOLD = 50;

        public HashSet<Hull> UnreachableHulls { get; private set; } = new HashSet<Hull>();
        public HashSet<Hull> UnsafeHulls { get; private set; } = new HashSet<Hull>();

        private SteeringManager outsideSteering, insideSteering;

        public IndoorsSteeringManager PathSteering => insideSteering as IndoorsSteeringManager;
        public HumanoidAnimController AnimController => Character.AnimController as HumanoidAnimController;

        public override AIObjectiveManager ObjectiveManager
        {
            get { return objectiveManager; }
        }

        public Order CurrentOrder
        {
            get;
            private set;
        }

        public string CurrentOrderOption
        {
            get;
            private set;
        }

        public float CurrentHullSafety { get; private set; }

        public HumanAIController(Character c) : base(c)
        {
            if (!c.IsHuman)
            {
                throw new System.Exception($"Tried to create a human ai controller for a non-human: {c.SpeciesName}!");
            }
            insideSteering = new IndoorsSteeringManager(this, true, false);
            outsideSteering = new SteeringManager(this);
            objectiveManager = new AIObjectiveManager(c);
            reactTimer = Rand.Range(0f, reactionTime);
            sortTimer = Rand.Range(0f, sortObjectiveInterval);
            InitProjSpecific();
        }
        partial void InitProjSpecific();

        public override void Update(float deltaTime)
        {
            if (DisableCrewAI || Character.IsUnconscious || Character.Removed) { return; }
            base.Update(deltaTime);

            if (unreachableClearTimer > 0)
            {
                unreachableClearTimer -= deltaTime;
            }
            else
            {
                unreachableClearTimer = clearUnreachableInterval;
                UnreachableHulls.Clear();
                ignoredContainers.Clear();
            }

            float maxDistanceToSub = 3000;
            if (Character.Submarine != null || SelectedAiTarget?.Entity?.Submarine != null && 
                    Vector2.DistanceSquared(Character.WorldPosition, SelectedAiTarget.Entity.Submarine.WorldPosition) < maxDistanceToSub * maxDistanceToSub)
            {
                if (steeringManager != insideSteering)
                {
                    insideSteering.Reset();
                }
                steeringManager = insideSteering;
            }
            else
            {
                if (steeringManager != outsideSteering)
                {
                    outsideSteering.Reset();
                }
                steeringManager = outsideSteering;
            }

            AnimController.Crouching = shouldCrouch;
            CheckCrouching(deltaTime);
            Character.ClearInputs();
            
            if (sortTimer > 0.0f)
            {
                sortTimer -= deltaTime;
            }
            else
            {
                objectiveManager.SortObjectives();
                sortTimer = sortObjectiveInterval;
            }
            objectiveManager.UpdateObjectives(deltaTime);

            if (reactTimer > 0.0f)
            {
                reactTimer -= deltaTime;
            }
            else
            {
                if (Character.CurrentHull != null)
                {
                    VisibleHulls.ForEach(h => PropagateHullSafety(Character, h));
                }
                if (Character.SpeechImpediment < 100.0f)
                {
                    ReportProblems();
                    UpdateSpeaking();
                }
                reactTimer = reactionTime * Rand.Range(0.75f, 1.25f);
            }

            if (objectiveManager.CurrentObjective == null) { return; }

            objectiveManager.DoCurrentObjective(deltaTime);
            bool run = objectiveManager.CurrentObjective.ForceRun || objectiveManager.GetCurrentPriority() > AIObjectiveManager.RunPriority;
            if (ObjectiveManager.CurrentObjective is AIObjectiveGoTo goTo && goTo.Target != null)
            {
                if (Character.CurrentHull == null)
                {
                    run = Vector2.DistanceSquared(Character.WorldPosition, goTo.Target.WorldPosition) > 300 * 300;
                }
                else
                {
                    float yDiff = goTo.Target.WorldPosition.Y - Character.WorldPosition.Y;
                    if (Math.Abs(yDiff) > 100)
                    {
                        run = true;
                    }
                    else
                    {
                        float xDiff = goTo.Target.WorldPosition.X - Character.WorldPosition.X;
                        run = Math.Abs(xDiff) > 300;
                    }
                }
            }
            if (run)
            {
                run = !AnimController.Crouching && !AnimController.IsMovingBackwards;
            }
            float currentSpeed = Character.AnimController.GetCurrentSpeed(run);
            steeringManager.Update(currentSpeed);

            bool ignorePlatforms = Character.AnimController.TargetMovement.Y < -0.5f &&
                (-Character.AnimController.TargetMovement.Y > Math.Abs(Character.AnimController.TargetMovement.X));

            if (steeringManager == insideSteering)
            {
                var currPath = PathSteering.CurrentPath;
                if (currPath != null && currPath.CurrentNode != null)
                {
                    if (currPath.CurrentNode.SimPosition.Y < Character.AnimController.GetColliderBottom().Y)
                    {
                        // Don't allow to jump from too high. The formula might require tweaking.
                        float allowedJumpHeight = Character.AnimController.ImpactTolerance / 2;
                        float height = Math.Abs(currPath.CurrentNode.SimPosition.Y - Character.SimPosition.Y);
                        ignorePlatforms = height < allowedJumpHeight;
                    }
                }

                if (Character.IsClimbing && PathSteering.IsNextLadderSameAsCurrent)
                {
                    Character.AnimController.TargetMovement = new Vector2(0.0f, Math.Sign(Character.AnimController.TargetMovement.Y));
                }
            }

            Character.AnimController.IgnorePlatforms = ignorePlatforms;

            Vector2 targetMovement = AnimController.TargetMovement;

            if (!Character.AnimController.InWater)
            {
                targetMovement = new Vector2(Character.AnimController.TargetMovement.X, MathHelper.Clamp(Character.AnimController.TargetMovement.Y, -1.0f, 1.0f));
            }

            float maxSpeed = Character.ApplyTemporarySpeedLimits(currentSpeed);
            targetMovement.X = MathHelper.Clamp(targetMovement.X, -maxSpeed, maxSpeed);
            targetMovement.Y = MathHelper.Clamp(targetMovement.Y, -maxSpeed, maxSpeed);

            //apply speed multiplier if 
            //  a. it's boosting the movement speed and the character is trying to move fast (= running)
            //  b. it's a debuff that decreases movement speed
            float speedMultiplier = Character.SpeedMultiplier;
            if (run || speedMultiplier <= 0.0f) targetMovement *= speedMultiplier;
            Character.ResetSpeedMultiplier();   // Reset, items will set the value before the next update

            if (Character.AnimController.InWater && targetMovement.LengthSquared() < 0.000001f)
            {
                bool isAiming = false;
                var holdable = Character.SelectedConstruction?.GetComponent<Holdable>();
                if (holdable != null)
                {
                    isAiming = holdable.ControlPose;
                }
                bool swimInPlace = !isAiming;
                if (swimInPlace && ObjectiveManager.GetActiveObjective() is AIObjectiveGoTo goToObjective)
                {
                    if (goToObjective.Target != Character)
                    {
                        swimInPlace = false;
                    }
                }
                if (swimInPlace)
                {
                    // Swim in place so that we don't fall motionless and look dead.
                    targetMovement = new Vector2(targetMovement.X, Rand.Range(-0.001f, 0.001f));
                }
            }

            Character.AnimController.TargetMovement = targetMovement;

            if (!Character.LockHands)
            {
                UnequipUnnecessaryItems();
            }

            flipTimer -= deltaTime;
            if (flipTimer <= 0.0f)
            {
                Direction newDir = Character.AnimController.TargetDir;
                if (Character.IsKeyDown(InputType.Aim))
                {
                    var cursorDiffX = Character.CursorPosition.X - Character.Position.X;
                    if (cursorDiffX > 10.0f)
                    {
                        newDir = Direction.Right;
                    }
                    else if (cursorDiffX < -10.0f)
                    {
                        newDir = Direction.Left;
                    }
                    if (Character.SelectedConstruction != null) Character.SelectedConstruction.SecondaryUse(deltaTime, Character);
                }
                else if (Math.Abs(Character.AnimController.TargetMovement.X) > 0.1f && !Character.AnimController.InWater)
                {
                    newDir = Character.AnimController.TargetMovement.X > 0.0f ? Direction.Right : Direction.Left;
                }
                if (newDir != Character.AnimController.TargetDir)
                {
                    Character.AnimController.TargetDir = newDir;
                    flipTimer = FlipInterval;
                }
            }
        }

        private void UnequipUnnecessaryItems()
        {
            if (ObjectiveManager.HasActiveObjective<AIObjectiveDecontainItem>()) { return; }
            if (findItemState == FindItemState.None || findItemState == FindItemState.Extinguisher)
            {
                if (!ObjectiveManager.IsCurrentObjective<AIObjectiveExtinguishFires>() && !objectiveManager.HasActiveObjective<AIObjectiveExtinguishFire>())
                {
                    var extinguisher = Character.Inventory.FindItemByTag("extinguisher");
                    if (extinguisher != null && Character.HasEquippedItem(extinguisher))
                    {
                        if (ObjectiveManager.GetCurrentPriority() >= AIObjectiveManager.RunPriority)
                        {
                            extinguisher.Drop(Character);
                        }
                        else
                        {
                            findItemState = FindItemState.Extinguisher;
                            if (FindSuitableContainer(Character, extinguisher, out Item targetContainer))
                            {
                                findItemState = FindItemState.None;
                                itemIndex = 0;
                                if (targetContainer != null)
                                {
                                    var decontainObjective = new AIObjectiveDecontainItem(Character, extinguisher, ObjectiveManager, targetContainer: targetContainer.GetComponent<ItemContainer>());
                                    decontainObjective.Abandoned += () => ignoredContainers.Add(targetContainer);
                                    ObjectiveManager.CurrentObjective.AddSubObjective(decontainObjective, addFirst: true);
                                    return;
                                }
                                else
                                {
                                    extinguisher.Drop(Character);
                                }
                            }
                        }
                    }
                }
            }
            if (findItemState == FindItemState.None || findItemState == FindItemState.DivingSuit || findItemState == FindItemState.DivingMask)
            {
                if (!NeedsDivingGear(Character, Character.CurrentHull, out _))
                {
                    bool oxygenLow = Character.OxygenAvailable < CharacterHealth.LowOxygenThreshold;
                    bool shouldKeepTheGearOn = Character.AnimController.HeadInWater
                        || Character.CurrentHull.WaterPercentage > 50
                        || ObjectiveManager.IsCurrentObjective<AIObjectiveFindSafety>() 
                        || ObjectiveManager.CurrentObjective.GetSubObjectivesRecursive(true).Any(o => o.KeepDivingGearOn);
                    bool removeDivingSuit = !Character.AnimController.HeadInWater && oxygenLow;
                    AIObjectiveGoTo gotoObjective = ObjectiveManager.CurrentOrder as AIObjectiveGoTo;
                    if (!removeDivingSuit)
                    {
                        bool targetHasNoSuit = gotoObjective != null && gotoObjective.mimic && !HasDivingSuit(gotoObjective.Target as Character);
                        removeDivingSuit = !shouldKeepTheGearOn && (gotoObjective == null || targetHasNoSuit);
                    }
                    bool takeMaskOff = !Character.AnimController.HeadInWater && oxygenLow;
                    if (!takeMaskOff && Character.CurrentHull.WaterPercentage < 40)
                    {
                        bool targetHasNoMask = gotoObjective != null && gotoObjective.mimic && !HasDivingMask(gotoObjective.Target as Character);
                        takeMaskOff = !shouldKeepTheGearOn && (gotoObjective == null || targetHasNoMask);
                    }
                    if (gotoObjective != null)
                    {
                        if (gotoObjective.Target is Hull h)
                        {
                            if (NeedsDivingGear(Character, h, out _))
                            {
                                removeDivingSuit = false;
                                takeMaskOff = false;
                            }
                        }
                        else if (gotoObjective.Target is Character c)
                        {
                            if (NeedsDivingGear(Character, c.CurrentHull, out _))
                            {
                                removeDivingSuit = false;
                                takeMaskOff = false;
                            }
                        }
                        else if (gotoObjective.Target is Item i)
                        {
                            if (NeedsDivingGear(Character, i.CurrentHull, out _))
                            {
                                removeDivingSuit = false;
                                takeMaskOff = false;
                            }
                        }
                    }
                    if (findItemState == FindItemState.None || findItemState == FindItemState.DivingSuit)
                    {
                        if (removeDivingSuit)
                        {
                            var divingSuit = Character.Inventory.FindItemByTag("divingsuit");
                            if (divingSuit != null)
                            {
                                if (oxygenLow || ObjectiveManager.GetCurrentPriority() >= AIObjectiveManager.RunPriority)
                                {
                                    divingSuit.Drop(Character);
                                }
                                else
                                {
                                    findItemState = FindItemState.DivingSuit;
                                    if (FindSuitableContainer(Character, divingSuit, out Item targetContainer))
                                    {
                                        findItemState = FindItemState.None;
                                        itemIndex = 0;
                                        if (targetContainer != null)
                                        {
                                            var decontainObjective = new AIObjectiveDecontainItem(Character, divingSuit, ObjectiveManager, targetContainer: targetContainer.GetComponent<ItemContainer>())
                                            {
                                                DropIfFailsToContain = false
                                            };
                                            decontainObjective.Abandoned += () =>
                                            {
                                                ignoredContainers.Add(targetContainer);
                                            };
                                            ObjectiveManager.CurrentObjective.AddSubObjective(decontainObjective, addFirst: true);
                                            return;
                                        }
                                        else
                                        {
                                            divingSuit.Drop(Character);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    if (findItemState == FindItemState.None || findItemState == FindItemState.DivingMask)
                    {
                        if (takeMaskOff)
                        {
                            var mask = Character.Inventory.FindItemByTag("divingmask");
                            if (mask != null && Character.Inventory.IsInLimbSlot(mask, InvSlotType.Head))
                            {
                                if (!mask.AllowedSlots.Contains(InvSlotType.Any) || !Character.Inventory.TryPutItem(mask, Character, new List<InvSlotType>() { InvSlotType.Any }))
                                {
                                    if (oxygenLow || ObjectiveManager.GetCurrentPriority() >= AIObjectiveManager.RunPriority)
                                    {
                                        mask.Drop(Character);
                                    }
                                    else
                                    {
                                        findItemState = FindItemState.DivingMask;
                                        if (FindSuitableContainer(Character, mask, out Item targetContainer))
                                        {
                                            findItemState = FindItemState.None;
                                            itemIndex = 0;
                                            if (targetContainer != null)
                                            {
                                                var decontainObjective = new AIObjectiveDecontainItem(Character, mask, ObjectiveManager, targetContainer: targetContainer.GetComponent<ItemContainer>());
                                                decontainObjective.Abandoned += () => ignoredContainers.Add(targetContainer);
                                                ObjectiveManager.CurrentObjective.AddSubObjective(decontainObjective, addFirst: true);
                                                return;
                                            }
                                            else
                                            {
                                                mask.Drop(Character);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            if (findItemState == FindItemState.None || findItemState == FindItemState.OtherItem)
            {
                if (!ObjectiveManager.CurrentObjective.UnequipItems || !ObjectiveManager.GetActiveObjective().UnequipItems) { return; }
                if (ObjectiveManager.HasActiveObjective<AIObjectiveContainItem>() || ObjectiveManager.HasActiveObjective<AIObjectiveDecontainItem>()) { return; }
                foreach (var item in Character.Inventory.Items)
                {
                    if (item == null) { continue; }
                    if (Character.HasEquippedItem(item) && 
                        (Character.Inventory.IsInLimbSlot(item, InvSlotType.RightHand) || 
                        Character.Inventory.IsInLimbSlot(item, InvSlotType.LeftHand) ||
                        Character.Inventory.IsInLimbSlot(item, InvSlotType.RightHand | InvSlotType.LeftHand)))
                    {
                        if (!item.AllowedSlots.Contains(InvSlotType.Any) || !Character.Inventory.TryPutItem(item, Character, new List<InvSlotType>() { InvSlotType.Any }))
                        {
                            if (FindSuitableContainer(Character, item, out Item targetContainer))
                            {
                                findItemState = FindItemState.None;
                                itemIndex = 0;
                                if (targetContainer != null)
                                {
                                    var decontainObjective = new AIObjectiveDecontainItem(Character, item, ObjectiveManager, targetContainer: targetContainer.GetComponent<ItemContainer>());
                                    decontainObjective.Abandoned += () => ignoredContainers.Add(targetContainer);
                                    ObjectiveManager.CurrentObjective.AddSubObjective(decontainObjective, addFirst: true);
                                    return;
                                }
                                else
                                {
                                    item.Drop(Character);
                                }
                            }
                            else
                            {
                                findItemState = FindItemState.OtherItem;
                            }
                        }
                    }
                }
            }
        }

        private enum FindItemState
        {
            None,
            DivingSuit,
            DivingMask,
            Extinguisher,
            OtherItem
        }
        private FindItemState findItemState;
        private int itemIndex;
        private List<Item> ignoredContainers = new List<Item>();
        public bool FindSuitableContainer(Character character, Item containableItem, out Item suitableContainer)
        {
            suitableContainer = null;
            if (character.FindItem(ref itemIndex, out Item targetContainer, ignoredItems: ignoredContainers, customPriorityFunction: i =>
            {
                var container = i.GetComponent<ItemContainer>();
                if (container == null) { return 0; }
                if (container.Inventory.IsFull()) { return 0; }
                if (container.ShouldBeContained(containableItem, out bool isRestrictionsDefined))
                {
                    if (isRestrictionsDefined)
                    {
                        return 3;
                    }
                    else
                    {
                        if (containableItem.Prefab.IsContainerPreferred(container, out bool isPreferencesDefined))
                        {
                            return isPreferencesDefined ? 2 : 1;
                        }
                        else
                        {
                            return isPreferencesDefined ? 0 : 1;
                        }
                    }
                }
                else
                {
                    return 0;
                }
            }))
            {
                suitableContainer = targetContainer;
                return true;
            }
            return false;
        }

        protected void ReportProblems()
        {
            Order newOrder = null;
            if (Character.CurrentHull != null)
            {
                foreach (var hull in VisibleHulls)
                {
                    foreach (Character c in Character.CharacterList)
                    {
                        if (c.CurrentHull != hull || !c.Enabled) { continue; }
                        if (AIObjectiveFightIntruders.IsValidTarget(c, Character))
                        {
                            AddTargets<AIObjectiveFightIntruders, Character>(Character, c);
                            if (newOrder == null)
                            {
                                var orderPrefab = Order.GetPrefab("reportintruders");
                                newOrder = new Order(orderPrefab, c.CurrentHull, null, orderGiver: Character);
                            }
                        }
                    }
                    if (AIObjectiveExtinguishFires.IsValidTarget(hull, Character))
                    {
                        AddTargets<AIObjectiveExtinguishFires, Hull>(Character, hull);
                        if (newOrder == null)
                        {
                            var orderPrefab = Order.GetPrefab("reportfire");
                            newOrder = new Order(orderPrefab, hull, null, orderGiver: Character);
                        }
                    }
                    foreach (Character c in Character.CharacterList)
                    {
                        if (c.CurrentHull != hull) { continue; }
                        if (AIObjectiveRescueAll.IsValidTarget(c, Character))
                        {
                            if (AddTargets<AIObjectiveRescueAll, Character>(c, Character))
                            {
                                if (newOrder == null)
                                {
                                    var orderPrefab = Order.GetPrefab("requestfirstaid");
                                    newOrder = new Order(orderPrefab, c.CurrentHull, null, orderGiver: Character);
                                }
                            }
                        }
                    }
                    foreach (var gap in hull.ConnectedGaps)
                    {
                        if (AIObjectiveFixLeaks.IsValidTarget(gap, Character))
                        {
                            AddTargets<AIObjectiveFixLeaks, Gap>(Character, gap);
                            if (newOrder == null && !gap.IsRoomToRoom)
                            {
                                var orderPrefab = Order.GetPrefab("reportbreach");
                                newOrder = new Order(orderPrefab, hull, null, orderGiver: Character);
                            }
                        }
                    }
                    foreach (Item item in Item.ItemList)
                    {
                        if (item.CurrentHull != hull) { continue; }
                        if (AIObjectiveRepairItems.IsValidTarget(item, Character))
                        {
                            if (item.Repairables.All(r => item.ConditionPercentage >= r.AIRepairThreshold)) { continue; }
                            AddTargets<AIObjectiveRepairItems, Item>(Character, item);
                            if (newOrder == null)
                            {
                                var orderPrefab = Order.GetPrefab("reportbrokendevices");
                                newOrder = new Order(orderPrefab, item.CurrentHull, item.Repairables?.FirstOrDefault(), orderGiver: Character);
                            }
                        }
                    }
                }
            }
            if (newOrder != null)
            {
                if (GameMain.GameSession?.CrewManager != null && GameMain.GameSession.CrewManager.AddOrder(newOrder, newOrder.FadeOutTime))
                {
                    Character.Speak(newOrder.GetChatMessage("", Character.CurrentHull?.DisplayName, givingOrderToSelf: false), ChatMessageType.Order);
#if SERVER
                    GameMain.Server.SendOrderChatMessage(new OrderChatMessage(newOrder, "", Character.CurrentHull, null, Character));
#endif
                }
            }
        }

        private void UpdateSpeaking()
        {
            if (Character.Oxygen < 20.0f)
            {
                Character.Speak(TextManager.Get("DialogLowOxygen"), null, 0, "lowoxygen", 30.0f);
            }

            if (Character.Bleeding > 2.0f)
            {
                Character.Speak(TextManager.Get("DialogBleeding"), null, 0, "bleeding", 30.0f);
            }

            if (Character.PressureTimer > 50.0f && Character.CurrentHull != null)
            {                
                Character.Speak(TextManager.GetWithVariable("DialogPressure", "[roomname]", Character.CurrentHull.DisplayName, true), null, 0, "pressure", 30.0f);
            }
        }

        public override void OnAttacked(Character attacker, AttackResult attackResult)
        {
            float damage = attackResult.Damage;
            if (damage <= 0) { return; }
            if (ObjectiveManager.CurrentObjective is AIObjectiveFightIntruders) { return; }
            if (attacker == null || attacker.IsDead || attacker.Removed)
            {
                // Ignore damage from falling etc that we shouldn't react to.
                if (Character.LastDamageSource == null) { return; }
                AddCombatObjective(AIObjectiveCombat.CombatMode.Retreat, Rand.Range(0.5f, 1f, Rand.RandSync.Unsynced));
            }
            else if (IsFriendly(attacker))
            {
                if (attacker.AnimController.Anim == Barotrauma.AnimController.Animation.CPR && attacker.SelectedCharacter == Character)
                {
                    // Don't attack characters that damage you while doing cpr, because let's assume that they are helping you.
                    // Should not cancel any existing ai objectives (so that if the character attacked you and then helped, we still would want to retaliate).
                    return;
                }
                if (!attacker.IsRemotePlayer && Character.Controlled != attacker && attacker.AIController != null && attacker.AIController.Enabled)
                {
                    // Don't retaliate on damage done by friendly ai, because we know that it's accidental
                    AddCombatObjective(AIObjectiveCombat.CombatMode.Retreat, Rand.Range(0.5f, 1f, Rand.RandSync.Unsynced));
                }
                else
                {
                    // If not on the same team, always stay defensive
                    if (attacker.TeamID != Character.TeamID)
                    {
                        AddCombatObjective(AIObjectiveCombat.CombatMode.Defensive, Rand.Range(0.5f, 1f, Rand.RandSync.Unsynced));
                    }
                    else
                    {
                        float currentVitality = Character.CharacterHealth.Vitality;
                        float dmgPercentage = damage / currentVitality * 100;
                        if (dmgPercentage < currentVitality / 10)
                        {
                            // Don't retaliate on minor (accidental) dmg done by characters that are in the same team
                            AddCombatObjective(AIObjectiveCombat.CombatMode.Retreat, Rand.Range(0.5f, 1f, Rand.RandSync.Unsynced));
                        }
                        else
                        {
                            AddCombatObjective(AIObjectiveCombat.CombatMode.Defensive, Rand.Range(0.5f, 1f, Rand.RandSync.Unsynced));
                        }
                    }
                }
            }
            else
            {
                AddCombatObjective(AIObjectiveCombat.CombatMode.Defensive);
            }

            void AddCombatObjective(AIObjectiveCombat.CombatMode mode, float delay = 0)
            {
                bool holdPosition = Character.Info?.Job?.Prefab.Identifier == "watchman";
                if (ObjectiveManager.CurrentObjective is AIObjectiveCombat combatObjective)
                {
                    if (combatObjective.Enemy != attacker || (combatObjective.Enemy == null && attacker == null))
                    {
                        // Replace the old objective with the new.
                        ObjectiveManager.Objectives.Remove(combatObjective);
                        objectiveManager.AddObjective(new AIObjectiveCombat(Character, attacker, mode, objectiveManager) { HoldPosition = holdPosition});
                    }
                }
                else
                {
                    if (delay > 0)
                    {
                        objectiveManager.AddObjective(new AIObjectiveCombat(Character, attacker, mode, objectiveManager) { HoldPosition = holdPosition }, delay);
                    }
                    else
                    {
                        objectiveManager.AddObjective(new AIObjectiveCombat(Character, attacker, mode, objectiveManager) { HoldPosition = holdPosition });
                    }
                }
            }
        }

        public void SetOrder(Order order, string option, Character orderGiver, bool speak = true)
        {
            CurrentOrderOption = option;
            CurrentOrder = order;
            objectiveManager.SetOrder(order, option, orderGiver);
            if (ObjectiveManager.CurrentOrder != null && speak && Character.SpeechImpediment < 100.0f)
            {
                if (ObjectiveManager.CurrentOrder is AIObjectiveRepairItems repairItems && repairItems.Targets.None())
                {
                    Character.Speak(TextManager.Get("DialogNoRepairTargets"), null, 3.0f, "norepairtargets");
                }
                else if (ObjectiveManager.CurrentOrder is AIObjectiveChargeBatteries chargeBatteries && chargeBatteries.Targets.None())
                {
                    Character.Speak(TextManager.Get("DialogNoBatteries"), null, 3.0f, "nobatteries");
                }
                else if (ObjectiveManager.CurrentOrder is AIObjectiveExtinguishFires extinguishFires && extinguishFires.Targets.None())
                {
                    Character.Speak(TextManager.Get("DialogNoFire"), null, 3.0f, "nofire");
                }
                else if (ObjectiveManager.CurrentOrder is AIObjectiveFixLeaks fixLeaks && fixLeaks.Targets.None())
                {
                    Character.Speak(TextManager.Get("DialogNoLeaks"), null, 3.0f, "noleaks");
                }
                else if (ObjectiveManager.CurrentOrder is AIObjectiveFightIntruders fightIntruders && fightIntruders.Targets.None())
                {
                    Character.Speak(TextManager.Get("DialogNoEnemies"), null, 3.0f, "noenemies");
                }
                else if (ObjectiveManager.CurrentOrder is AIObjectiveRescueAll rescueAll && rescueAll.Targets.None())
                {
                    Character.Speak(TextManager.Get("DialogNoRescueTargets"), null, 3.0f, "norescuetargets");                    
                }
                else if (ObjectiveManager.CurrentOrder is AIObjectivePumpWater pumpWater && pumpWater.Targets.None())
                {
                    Character.Speak(TextManager.Get("DialogNoPumps"), null, 3.0f, "nopumps");
                }
                else
                {
                    Character.Speak(TextManager.Get("DialogAffirmative"), null, 1.0f);
                }
            }
            SetOrderProjSpecific(order);
        }

        partial void SetOrderProjSpecific(Order order);

        public override void SelectTarget(AITarget target)
        {
            SelectedAiTarget = target;
        }

        private void CheckCrouching(float deltaTime)
        {
            crouchRaycastTimer -= deltaTime;
            if (crouchRaycastTimer > 0.0f) return;

            crouchRaycastTimer = crouchRaycastInterval;

            //start the raycast in front of the character in the direction it's heading to
            Vector2 startPos = Character.SimPosition;
            startPos.X += MathHelper.Clamp(Character.AnimController.TargetMovement.X, -1.0f, 1.0f);

            //do a raycast upwards to find any walls
            float minCeilingDist = Character.AnimController.Collider.height / 2 + Character.AnimController.Collider.radius + 0.1f;
            shouldCrouch = Submarine.PickBody(startPos, startPos + Vector2.UnitY * minCeilingDist, null, Physics.CollisionWall) != null;
        }

        public static bool NeedsDivingGear(Character character, Hull hull, out bool needsSuit)
        {
            needsSuit = false;
            if (hull == null || 
                hull.WaterPercentage > 80 || 
                (hull.LethalPressure > 0 && character.PressureProtection <= 0) || 
                (hull.ConnectedGaps.Any() && hull.ConnectedGaps.Max(g => AIObjectiveFixLeaks.GetLeakSeverity(g)) > 60))
            {
                needsSuit = true;
                return true;
            }
            if (hull.WaterPercentage > 60 || hull.OxygenPercentage < CharacterHealth.LowOxygenThreshold)
            {
                return true;
            }
            return false;
        }


        public static bool HasDivingGear(Character character, float conditionPercentage = 0) => HasDivingSuit(character, conditionPercentage) || HasDivingMask(character, conditionPercentage);

        /// <summary>
        /// Check whether the character has a diving suit in usable condition plus some oxygen.
        /// </summary>
        public static bool HasDivingSuit(Character character, float conditionPercentage = 0) => HasItem(character, "divingsuit", "oxygensource", conditionPercentage);

        /// <summary>
        /// Check whether the character has a diving mask in usable condition plus some oxygen.
        /// </summary>
        public static bool HasDivingMask(Character character, float conditionPercentage = 0) => HasItem(character, "divingmask", "oxygensource", conditionPercentage);

        public static bool HasItem(Character character, string identifier, string containedTag, float conditionPercentage = 0)
        {
            if (character == null) { return false; }
            if (character.Inventory == null) { return false; }
            var item = character.Inventory.FindItemByIdentifier(identifier) ?? character.Inventory.FindItemByTag(identifier);
            return item != null &&
                item.ConditionPercentage > conditionPercentage &&
                character.HasEquippedItem(item) &&
                (containedTag == null ||
                (item.ContainedItems != null &&
                item.ContainedItems.Any(i => i.HasTag(containedTag) && i.ConditionPercentage > conditionPercentage)));
        }

        /// <summary>
        /// Updates the hull safety for all ai characters in the team.
        /// </summary>
        public static void PropagateHullSafety(Character character, Hull hull)
        {
            DoForEachCrewMember(character, (humanAi) => humanAi.RefreshHullSafety(hull));
        }

        private void RefreshHullSafety(Hull hull)
        {
            if (GetHullSafety(hull, Character, VisibleHulls) > HULL_SAFETY_THRESHOLD)
            {
                UnsafeHulls.Remove(hull);
            }
            else
            {
                UnsafeHulls.Add(hull);
            }
        }

        public static void RefreshTargets(Character character, Order order, Hull hull)
        {
            switch (order.Identifier)
            {
                case "reportfire":
                    AddTargets<AIObjectiveExtinguishFires, Hull>(character, hull);
                    break;
                case "reportbreach":
                    foreach (var gap in hull.ConnectedGaps)
                    {
                        if (AIObjectiveFixLeaks.IsValidTarget(gap, character))
                        {
                            AddTargets<AIObjectiveFixLeaks, Gap>(character, gap);
                        }
                    }
                    break;
                case "reportbrokendevices":
                    foreach (var item in Item.ItemList)
                    {
                        if (item.CurrentHull != hull) { continue; }
                        if (AIObjectiveRepairItems.IsValidTarget(item, character))
                        {
                            if (item.Repairables.All(r => item.ConditionPercentage >= r.AIRepairThreshold)) { continue; }
                            AddTargets<AIObjectiveRepairItems, Item>(character, item);
                        }
                    }
                    break;
                case "reportintruders":
                    foreach (var enemy in Character.CharacterList)
                    {
                        if (enemy.CurrentHull != hull) { continue; }
                        if (AIObjectiveFightIntruders.IsValidTarget(enemy, character))
                        {
                            AddTargets<AIObjectiveFightIntruders, Character>(character, enemy);
                        }
                    }
                    break;
                case "requestfirstaid":
                    foreach (var c in Character.CharacterList)
                    {
                        if (c.CurrentHull != hull) { continue; }
                        if (AIObjectiveRescueAll.IsValidTarget(c, character))
                        {
                            AddTargets<AIObjectiveRescueAll, Character>(character, c);
                        }
                    }
                    break;
                default:
#if DEBUG
                    DebugConsole.ThrowError(order.Identifier + " not implemented!");
#endif
                    break;
            }
        }

        private static bool AddTargets<T1, T2>(Character caller, T2 target) where T1 : AIObjectiveLoop<T2>
        {
            bool targetAdded = false;
            DoForEachCrewMember(caller, humanAI =>
            {
                var objective = humanAI.ObjectiveManager.GetObjective<T1>();
                if (objective != null)
                {
                    if (!targetAdded && objective.AddTarget(target))
                    {
                        targetAdded = true;
                    }
                }
            });
            return targetAdded;
        }

        public static void RemoveTargets<T1, T2>(Character caller, T2 target) where T1 : AIObjectiveLoop<T2>
        {
            DoForEachCrewMember(caller, humanAI =>
                humanAI.ObjectiveManager.GetObjective<T1>()?.ReportedTargets.Remove(target));
        }

        public float GetHullSafety(Hull hull, Character character, IEnumerable<Hull> visibleHulls = null)
        {
            bool isCurrentHull = character == Character && character.CurrentHull == hull;
            if (hull == null)
            {
                if (isCurrentHull)
                {
                    CurrentHullSafety = 0;
                }
                return CurrentHullSafety;
            }
            if (isCurrentHull && visibleHulls == null)
            {
                // Use the cached visible hulls
                visibleHulls = VisibleHulls;
            }
            // TODO: should we calculate the visible hulls for each hull? -> could be a bit heavy.
            bool ignoreFire = ObjectiveManager.IsCurrentObjective<AIObjectiveExtinguishFires>() || objectiveManager.HasActiveObjective<AIObjectiveExtinguishFire>();
            bool ignoreWater = HasDivingSuit(character);
            bool ignoreOxygen = ignoreWater || HasDivingMask(character);
            bool ignoreEnemies = ObjectiveManager.IsCurrentObjective<AIObjectiveFightIntruders>();
            float safety = GetHullSafety(hull, visibleHulls, character, ignoreWater, ignoreOxygen, ignoreFire, ignoreEnemies);
            if (isCurrentHull)
            {
                CurrentHullSafety = safety;
            }
            return safety;
        }

        public static float GetHullSafety(Hull hull, IEnumerable<Hull> visibleHulls, Character character, bool ignoreWater = false, bool ignoreOxygen = false, bool ignoreFire = false, bool ignoreEnemies = false)
        {
            if (hull == null) { return 0; }
            if (hull.LethalPressure > 0 && character.PressureProtection <= 0) { return 0; }
            float oxygenFactor = ignoreOxygen ? 1 : MathHelper.Lerp(0.25f, 1, hull.OxygenPercentage / 100);
            float waterFactor = ignoreWater ? 1 : MathHelper.Lerp(1, 0.25f, hull.WaterPercentage / 100);
            if (!character.NeedsAir)
            {
                oxygenFactor = 1;
                waterFactor = 1;
            }
            float fireFactor = 1;
            if (!ignoreFire)
            {
                Func<Hull, float> calculateFire = h => h.FireSources.Count * 0.5f + h.FireSources.Sum(fs => fs.DamageRange) / h.Size.X;
                // Even the smallest fire reduces the safety by 50%
                float fire = visibleHulls == null ? calculateFire(hull) : visibleHulls.Sum(h => calculateFire(h));
                fireFactor = MathHelper.Lerp(1, 0, MathHelper.Clamp(fire, 0, 1));
            }
            float enemyFactor = 1;
            if (!ignoreEnemies)
            {
                Func<Character, bool> isValidTarget = e => IsActive(e) && !IsFriendly(character, e);
                int enemyCount = visibleHulls == null ?
                    Character.CharacterList.Count(e => e.CurrentHull == hull && isValidTarget(e)) :
                    Character.CharacterList.Count(e => visibleHulls.Contains(e.CurrentHull) && isValidTarget(e));
                // The hull safety decreases 90% per enemy up to 100% (TODO: test smaller percentages)
                enemyFactor = MathHelper.Lerp(1, 0, MathHelper.Clamp(enemyCount * 0.9f, 0, 1));
            }
            float safety = oxygenFactor * waterFactor * fireFactor * enemyFactor;
            return MathHelper.Clamp(safety * 100, 0, 100);
        }

        public void FaceTarget(ISpatialEntity target) => Character.AnimController.TargetDir = target.WorldPosition.X > Character.WorldPosition.X ? Direction.Right : Direction.Left;

        public static bool IsFriendly(Character me, Character other) => 
            (other.TeamID == me.TeamID || 
            other.TeamID == Character.TeamType.FriendlyNPC || 
            me.TeamID == Character.TeamType.FriendlyNPC) && (other.SpeciesName == me.SpeciesName || other.Params.CompareGroup(me.Params.Group));

        public static bool IsActive(Character other) => !other.Removed && !other.IsDead && !other.IsUnconscious;

        public static bool IsTrueForAllCrewMembers(Character character, Func<HumanAIController, bool> predicate)
        {
            if (character == null) { return false; }
            foreach (var c in Character.CharacterList)
            {
                if (FilterCrewMember(character, c))
                {
                    if (!predicate(c.AIController as HumanAIController))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public static bool IsTrueForAnyCrewMember(Character character, Func<HumanAIController, bool> predicate)
        {
            if (character == null) { return false; }
            foreach (var c in Character.CharacterList)
            {
                if (FilterCrewMember(character, c))
                {
                    if (predicate(c.AIController as HumanAIController))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static int CountCrew(Character character, Func<HumanAIController, bool> predicate = null)
        {
            if (character == null) { return 0; }
            int count = 0;
            foreach (var c in Character.CharacterList)
            {
                if (FilterCrewMember(character, c))
                {
                    if (predicate == null || predicate(c.AIController as HumanAIController))
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        public static void DoForEachCrewMember(Character character, Action<HumanAIController> action)
        {
            if (character == null) { return; }
            foreach (var c in Character.CharacterList)
            {
                if (FilterCrewMember(character, c))
                {
                    action(c.AIController as HumanAIController);
                }
            }
        }

        private static bool FilterCrewMember(Character self, Character other) => other != null && !other.IsDead && !other.Removed && other.AIController is HumanAIController humanAi && humanAi.IsFriendly(self);

        #region Wrappers
        public bool IsFriendly(Character other) => IsFriendly(Character, other);
        public void DoForEachCrewMember(Action<HumanAIController> action) => DoForEachCrewMember(Character, action);
        public bool IsTrueForAnyCrewMember(Func<HumanAIController, bool> predicate) => IsTrueForAnyCrewMember(Character, predicate);
        public bool IsTrueForAllCrewMembers(Func<HumanAIController, bool> predicate) => IsTrueForAllCrewMembers(Character, predicate);
        public int CountCrew(Func<HumanAIController, bool> predicate = null) => CountCrew(Character, predicate);
        #endregion
    }
}
