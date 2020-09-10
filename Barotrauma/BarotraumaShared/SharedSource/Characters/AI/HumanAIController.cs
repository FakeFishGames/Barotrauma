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

        const float reactionTime = 0.3f;
        const float crouchRaycastInterval = 1;
        const float sortObjectiveInterval = 1;
        const float clearUnreachableInterval = 30;

        private float flipTimer;
        private const float FlipInterval = 0.5f;

        public static float HULL_SAFETY_THRESHOLD = 50;

        public readonly HashSet<Hull> UnreachableHulls = new HashSet<Hull>();
        public readonly HashSet<Hull> UnsafeHulls = new HashSet<Hull>();
        public readonly List<Item> IgnoredItems = new List<Item>();

        private class HullSafety
        {
            public float safety;
            public float timer;

            public bool IsStale => timer <= 0;

            public HullSafety(float safety)
            {
                Reset(safety);
            }

            public void Reset(float safety)
            {
                this.safety = safety;
                // How long before the hull safety is considered stale
                timer = 0.5f;
            }

            /// <summary>
            /// Returns true when the safety is stale
            /// </summary>
            public bool Update(float deltaTime)
            {
                timer = Math.Max(timer - deltaTime, 0);
                return IsStale;
            }
        }

        private readonly Dictionary<Hull, HullSafety> knownHulls = new Dictionary<Hull, HullSafety>();

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

        public float CurrentHullSafety { get; private set; } = 100;

        private readonly Dictionary<Character, float> damageDoneByAttacker = new Dictionary<Character, float>();
        private readonly List<Character> attackers = new List<Character>();


        public HumanAIController(Character c) : base(c)
        {
            if (!c.IsHuman)
            {
                throw new Exception($"Tried to create a human ai controller for a non-human: {c.SpeciesName}!");
            }
            insideSteering = new IndoorsSteeringManager(this, true, false);
            outsideSteering = new SteeringManager(this);
            objectiveManager = new AIObjectiveManager(c);
            reactTimer = GetReactionTime();
            sortTimer = Rand.Range(0f, sortObjectiveInterval);
            InitProjSpecific();
        }
        partial void InitProjSpecific();

        public override void Update(float deltaTime)
        {
            if (DisableCrewAI || Character.IsIncapacitated || Character.Removed) { return; }
            base.Update(deltaTime);

            foreach (var values in knownHulls)
            {
                HullSafety hullSafety = values.Value;
                hullSafety.Update(deltaTime);
            }

            if (unreachableClearTimer > 0)
            {
                unreachableClearTimer -= deltaTime;
            }
            else
            {
                unreachableClearTimer = clearUnreachableInterval;
                UnreachableHulls.Clear();
                IgnoredItems.Clear();
            }

            // Use the pathfinding also outside of the sub, but not farther than the extents of the sub + 500 units.
            if (Character.Submarine != null || SelectedAiTarget?.Entity?.Submarine is Submarine sub && sub != null &&
                    Vector2.DistanceSquared(Character.WorldPosition, sub.WorldPosition) < MathUtils.Pow(Math.Max(sub.Borders.Size.X, sub.Borders.Size.Y) / 2 + 500, 2))
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

            //slowly forget about damage done by attackers
            foreach (Character enemy in attackers)
            {
                if (damageDoneByAttacker[enemy] > 0)
                {
                    damageDoneByAttacker[enemy] -= deltaTime * 0.01f;
                }
            }

            if (reactTimer > 0.0f)
            {
                reactTimer -= deltaTime;
                if (findItemState != FindItemState.None)
                {
                    // Update every frame only when seeking items
                    UnequipUnnecessaryItems();
                }
            }
            else
            {
                if (Character.CurrentHull != null)
                {
                    if (Character.TeamID == Character.TeamType.FriendlyNPC)
                    {
                        // Outpost npcs don't inform each other about threads, like crew members do.
                        VisibleHulls.ForEach(h => RefreshHullSafety(h));
                    }
                    else
                    {
                        VisibleHulls.ForEach(h => PropagateHullSafety(Character, h));
                    }
                }
                if (Character.SpeechImpediment < 100.0f)
                {
                    if (Character.Submarine != null && Character.Submarine.TeamID == Character.TeamID && !Character.Submarine.Info.IsWreck)
                    {
                        ReportProblems();
                    }
                    UpdateSpeaking();
                }
                UnequipUnnecessaryItems();
                reactTimer = GetReactionTime();
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
                        run = Math.Abs(xDiff) > 500;
                    }
                }
            }
            steeringManager.Update(Character.AnimController.GetCurrentSpeed(run && Character.CanRun));

            bool ignorePlatforms = Character.AnimController.TargetMovement.Y < -0.5f && (-Character.AnimController.TargetMovement.Y > Math.Abs(Character.AnimController.TargetMovement.X));
            if (steeringManager == insideSteering)
            {
                var currPath = PathSteering.CurrentPath;
                if (currPath != null && currPath.CurrentNode != null)
                {
                    if (currPath.CurrentNode.SimPosition.Y < Character.AnimController.GetColliderBottom().Y)
                    {
                        // Don't allow to jump from too high.
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
            Character.AnimController.TargetMovement = Character.ApplyMovementLimits(targetMovement, AnimController.GetCurrentSpeed(run));

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
            if (Character.LockHands) { return; }
            if (ObjectiveManager.CurrentObjective == null) { return; }
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
                            if (FindSuitableContainer(extinguisher, out Item targetContainer))
                            {
                                findItemState = FindItemState.None;
                                itemIndex = 0;
                                if (targetContainer != null)
                                {
                                    var decontainObjective = new AIObjectiveDecontainItem(Character, extinguisher, ObjectiveManager, targetContainer: targetContainer.GetComponent<ItemContainer>());
                                    decontainObjective.Abandoned += () => IgnoredItems.Add(targetContainer);
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
                    bool shouldKeepTheGearOn = Character.AnimController.HeadInWater
                        || ObjectiveManager.IsCurrentObjective<AIObjectiveFindSafety>()
                        || ObjectiveManager.CurrentObjective.GetSubObjectivesRecursive(true).Any(o => o.KeepDivingGearOn);
                    bool oxygenLow = !Character.AnimController.HeadInWater && Character.OxygenAvailable < CharacterHealth.LowOxygenThreshold;
                    if (oxygenLow)
                    {
                        shouldKeepTheGearOn = false;
                    }
                    bool removeDivingSuit = !shouldKeepTheGearOn;
                    bool takeMaskOff = !shouldKeepTheGearOn;
                    if (!shouldKeepTheGearOn && !oxygenLow)
                    {
                        if (ObjectiveManager.IsCurrentObjective<AIObjectiveIdle>())
                        {
                            removeDivingSuit = true;
                            takeMaskOff = true;
                        }
                        else
                        {
                            bool removeSuit = false;
                            bool removeMask = false;
                            foreach (var objective in ObjectiveManager.CurrentObjective.GetSubObjectivesRecursive(includingSelf: true))
                            {
                                if (objective is AIObjectiveGoTo gotoObjective)
                                {
                                    bool insideSteering = SteeringManager == PathSteering && PathSteering.CurrentPath != null && !PathSteering.IsPathDirty;
                                    Hull targetHull = gotoObjective.GetTargetHull();
                                    bool targetIsOutside = (gotoObjective.Target != null && targetHull == null) || (insideSteering && PathSteering.CurrentPath.HasOutdoorsNodes);
                                    if (targetIsOutside || NeedsDivingGear(Character, targetHull, out _))
                                    {
                                        removeDivingSuit = false;
                                        takeMaskOff = false;
                                        break;
                                    }
                                    else if (gotoObjective.mimic)
                                    {
                                        if (!removeSuit)
                                        {
                                            removeDivingSuit = !HasDivingSuit(gotoObjective.Target as Character);
                                            if (removeDivingSuit)
                                            {
                                                removeSuit = true;
                                            }
                                        }
                                        if (!removeMask)
                                        {
                                            takeMaskOff = !HasDivingMask(gotoObjective.Target as Character);
                                            if (takeMaskOff)
                                            {
                                                removeMask = true;
                                            }
                                        }
                                    }
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
                                        if (FindSuitableContainer(divingSuit, out Item targetContainer))
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
                                                    IgnoredItems.Add(targetContainer);
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
                                if (Character.HasEquippedItem("divingmask"))
                                {
                                    var mask = Character.Inventory.FindItemByTag("divingmask");
                                    if (mask != null)
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
                                                if (FindSuitableContainer(mask, out Item targetContainer))
                                                {
                                                    findItemState = FindItemState.None;
                                                    itemIndex = 0;
                                                    if (targetContainer != null)
                                                    {
                                                        var decontainObjective = new AIObjectiveDecontainItem(Character, mask, ObjectiveManager, targetContainer: targetContainer.GetComponent<ItemContainer>());
                                                        decontainObjective.Abandoned += () => IgnoredItems.Add(targetContainer);
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
                                if (FindSuitableContainer(item, out Item targetContainer))
                                {
                                    findItemState = FindItemState.None;
                                    itemIndex = 0;
                                    if (targetContainer != null)
                                    {
                                        var decontainObjective = new AIObjectiveDecontainItem(Character, item, ObjectiveManager, targetContainer: targetContainer.GetComponent<ItemContainer>());
                                        decontainObjective.Abandoned += () => IgnoredItems.Add(targetContainer);
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
        public bool FindSuitableContainer(Item containableItem, out Item suitableContainer)
        {
            suitableContainer = null;
            if (Character.FindItem(ref itemIndex, out Item targetContainer, ignoredItems: IgnoredItems, customPriorityFunction: i =>
            {
                var container = i.GetComponent<ItemContainer>();
                if (container == null) { return 0; }
                if (container.Inventory.IsFull()) { return 0; }
                if (container.ShouldBeContained(containableItem, out bool isRestrictionsDefined))
                {
                    if (isRestrictionsDefined)
                    {
                        return 4;
                    }
                    else
                    {
                        if (containableItem.Prefab.IsContainerPreferred(container, out bool isPreferencesDefined, out bool isSecondary))
                        {
                            return isPreferencesDefined ? isSecondary ? 2 : 3 : 1;
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
            Hull targetHull = null;
            if (Character.CurrentHull != null)
            {
                bool isFighting = ObjectiveManager.HasActiveObjective<AIObjectiveCombat>();
                bool isFleeing = ObjectiveManager.HasActiveObjective<AIObjectiveFindSafety>();
                foreach (var hull in VisibleHulls)
                {
                    foreach (Character target in Character.CharacterList)
                    {
                        if (target.CurrentHull != hull || !target.Enabled) { continue; }
                        if (AIObjectiveFightIntruders.IsValidTarget(target, Character))
                        {
                            if (AddTargets<AIObjectiveFightIntruders, Character>(Character, target) && newOrder == null)
                            {
                                var orderPrefab = Order.GetPrefab("reportintruders");
                                newOrder = new Order(orderPrefab, hull, null, orderGiver: Character);
                                targetHull = hull;
                            }
                        }
                    }
                    if (AIObjectiveExtinguishFires.IsValidTarget(hull, Character))
                    {
                        if (AddTargets<AIObjectiveExtinguishFires, Hull>(Character, hull) && newOrder == null)
                        {
                            var orderPrefab = Order.GetPrefab("reportfire");
                            newOrder = new Order(orderPrefab, hull, null, orderGiver: Character);
                            targetHull = hull;
                        }
                    }
                    if (!isFighting)
                    {
                        foreach (var gap in hull.ConnectedGaps)
                        {
                            if (AIObjectiveFixLeaks.IsValidTarget(gap, Character))
                            {
                                if (AddTargets<AIObjectiveFixLeaks, Gap>(Character, gap) && newOrder == null && !gap.IsRoomToRoom)
                                {
                                    var orderPrefab = Order.GetPrefab("reportbreach");
                                    newOrder = new Order(orderPrefab, hull, null, orderGiver: Character);
                                    targetHull = hull;
                                }
                            }
                        }
                        if (!isFleeing)
                        {
                            foreach (Character target in Character.CharacterList)
                            {
                                if (target.CurrentHull != hull) { continue; }
                                if (AIObjectiveRescueAll.IsValidTarget(target, Character))
                                {
                                    if (AddTargets<AIObjectiveRescueAll, Character>(Character, target) && newOrder == null && !ObjectiveManager.HasActiveObjective<AIObjectiveRescue>())
                                    {
                                        var orderPrefab = Order.GetPrefab("requestfirstaid");
                                        newOrder = new Order(orderPrefab, hull, null, orderGiver: Character);
                                        targetHull = hull;
                                    }
                                }
                            }
                            foreach (Item item in Item.ItemList)
                            {
                                if (item.CurrentHull != hull) { continue; }
                                if (AIObjectiveRepairItems.IsValidTarget(item, Character))
                                {
                                    if (item.Repairables.All(r => item.ConditionPercentage > r.RepairIconThreshold)) { continue; }
                                    if (AddTargets<AIObjectiveRepairItems, Item>(Character, item) && newOrder == null && !ObjectiveManager.HasActiveObjective<AIObjectiveRepairItem>())
                                    {
                                        var orderPrefab = Order.GetPrefab("reportbrokendevices");
                                        newOrder = new Order(orderPrefab, hull, item.Repairables?.FirstOrDefault(), orderGiver: Character);
                                        targetHull = hull;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            if (newOrder != null)
            {
                if (Character.TeamID == Character.TeamType.FriendlyNPC)
                {
                    Character.Speak(newOrder.GetChatMessage("", targetHull?.DisplayName, givingOrderToSelf: false), ChatMessageType.Default, 
                        identifier: newOrder.Prefab.Identifier + (targetHull?.DisplayName ?? "null"), 
                        minDurationBetweenSimilar: 60.0f);
                }
                else if (GameMain.GameSession?.CrewManager != null && GameMain.GameSession.CrewManager.AddOrder(newOrder, newOrder.FadeOutTime))
                {
                    Character.Speak(newOrder.GetChatMessage("", targetHull?.DisplayName, givingOrderToSelf: false), ChatMessageType.Order);
#if SERVER
                    GameMain.Server.SendOrderChatMessage(new OrderChatMessage(newOrder, "", targetHull, null, Character));
#endif
                }
            }
        }

        private void UpdateSpeaking()
        {
            if (Character.Oxygen < 20.0f)
            {
                Character.Speak(TextManager.Get("DialogLowOxygen"), null, Rand.Range(0.5f, 5.0f), "lowoxygen", 30.0f);
            }

            if (Character.Bleeding > 2.0f)
            {
                Character.Speak(TextManager.Get("DialogBleeding"), null, Rand.Range(0.5f, 5.0f), "bleeding", 30.0f);
            }

            if (Character.PressureTimer > 50.0f && Character.CurrentHull != null)
            {                
                Character.Speak(TextManager.GetWithVariable("DialogPressure", "[roomname]", Character.CurrentHull.DisplayName, true), null, Rand.Range(0.5f, 5.0f), "pressure", 30.0f);
            }
        }

        public override void OnAttacked(Character attacker, AttackResult attackResult)
        {
            // excluding poisons etc
            float realDamage = attackResult.Damage;
            // including poisons etc
            float totalDamage = realDamage;
            foreach (Affliction affliction in attackResult.Afflictions)
            {
                totalDamage -= affliction.Prefab.KarmaChangeOnApplied * affliction.Strength;
            }
            if (totalDamage <= 0) { return; }
            if (attacker != null) 
            {
                if (!damageDoneByAttacker.ContainsKey(attacker))
                {
                    damageDoneByAttacker[attacker] = 0.0f;
                }
                damageDoneByAttacker[attacker] += totalDamage;
                attackers.Add(attacker);
            }
            if (ObjectiveManager.CurrentObjective is AIObjectiveFightIntruders) { return; }
            if (attacker == null || attacker.IsDead || attacker.Removed)
            {
                // Don't react on the damage if there's no attacker.
                // We might consider launching the retreat combat objective in some cases, so that the bot does not just stand somewhere getting damaged and dying.
                // But fires and enemies should already be handled by the FindSafetyObjective.
                return;
                // Ignore damage from falling etc that we shouldn't react to.
                //if (Character.LastDamageSource == null) { return; }
                //AddCombatObjective(AIObjectiveCombat.CombatMode.Retreat, Rand.Range(0.5f, 1f, Rand.RandSync.Unsynced));
            }
            else if (realDamage <= 0 && (attacker.IsBot || attacker.TeamID == Character.TeamID))
            {
                // Don't react on damage that is entirely based on karma penalties (medics, poisons etc), unless applier is player
                return;
            }
            else if (IsFriendly(attacker))
            {
                if (attacker.AnimController.Anim == Barotrauma.AnimController.Animation.CPR && attacker.SelectedCharacter == Character)
                {
                    // Don't attack characters that damage you while doing cpr, because let's assume that they are helping you.
                    // Should not cancel any existing ai objectives (so that if the character attacked you and then helped, we still would want to retaliate).
                    return;
                }
                if (attacker.IsBot)
                {
                    // Don't retaliate on damage done by human ai, because we know it's accidental
                    AddCombatObjective(AIObjectiveCombat.CombatMode.Retreat, attacker, GetReactionTime() * 2);
                }
                else
                {
                    if (Character.IsSecurity)
                    {
                        // TODO
                    }
                    else
                    {
                        Character.Speak(TextManager.Get("DialogAttackedByFriendly"), null, 0.50f, "attackedbyfriendly", minDurationBetweenSimilar: 30.0f);
                    }
                    if (Character.TeamID == Character.TeamType.FriendlyNPC && !Character.TurnedHostileByEvent)
                    {
                        // Inform other characters in the same team
                        foreach (Character otherCharacter in Character.CharacterList)
                        {
                            if (otherCharacter == Character || otherCharacter.TeamID != Character.TeamID || otherCharacter.IsDead ||
                                otherCharacter.Info?.Job == null ||
                                !(otherCharacter.AIController is HumanAIController otherHumanAI) ||
                                otherCharacter.TurnedHostileByEvent) 
                            {
                                continue; 
                            }
                            bool isWitnessing = otherHumanAI.VisibleHulls.Contains(Character.CurrentHull) || otherHumanAI.VisibleHulls.Contains(attacker.CurrentHull);
                            if (otherCharacter.IsSecurity)
                            {
                                // Alert all the security officers magically
                                float delay = isWitnessing ? GetReactionTime() * 2 : Rand.Range(2.0f, 5.0f, Rand.RandSync.Unsynced);
                                otherHumanAI.AddCombatObjective(DetermineCombatMode(otherCharacter), attacker, delay);
                            }
                            else if (isWitnessing)
                            {
                                // Other witnesses retreat to safety
                                otherHumanAI.AddCombatObjective(AIObjectiveCombat.CombatMode.Retreat, attacker, GetReactionTime());
                            }
                        }
                        (GameMain.GameSession?.GameMode as CampaignMode)?.OutpostNPCAttacked(Character, attacker, attackResult);                        
                    }

                    if (attacker.TeamID != Character.TeamID)
                    {
                        AddCombatObjective(DetermineCombatMode(Character), attacker, GetReactionTime());
                    }
                    else
                    {
                        // Don't react on minor (accidental) dmg done by characters that are in the same team
                        if (GetDamageDoneByAttacker(attacker) < 10)
                        {
                            if (!Character.IsSecurity)
                            {
                                AddCombatObjective(AIObjectiveCombat.CombatMode.Retreat, attacker, GetReactionTime() * 2);
                            }
                        }
                        else
                        {
                            AddCombatObjective(DetermineCombatMode(Character, dmgThreshold: 20, allowOffensive: false), attacker, GetReactionTime() * 2);
                        }
                    }
                }
            }
            else
            {
                AddCombatObjective(DetermineCombatMode(Character), attacker);
            }

            AIObjectiveCombat.CombatMode DetermineCombatMode(Character c, float dmgThreshold = 10, bool allowOffensive = true)
            {
                if (!IsFriendly(attacker))
                {
                    return c.IsSecurity ? AIObjectiveCombat.CombatMode.Offensive : AIObjectiveCombat.CombatMode.Defensive;
                }
                else
                {
                    if (GetDamageDoneByAttacker(attacker) > dmgThreshold)
                    {
                        return c.IsSecurity && allowOffensive ? AIObjectiveCombat.CombatMode.Offensive : AIObjectiveCombat.CombatMode.Defensive;
                    }
                    else
                    {
                        return c.IsSecurity ? AIObjectiveCombat.CombatMode.Arrest : AIObjectiveCombat.CombatMode.Retreat;
                    }
                }
            }
        }

        private void AddCombatObjective(AIObjectiveCombat.CombatMode mode, Character attacker, float delay = 0, Func<bool> abortCondition = null, Action onAbort = null, bool allowHoldFire = false)
        {
            if (ObjectiveManager.CurrentObjective is AIObjectiveCombat combatObjective)
            {
                // Don't replace offensive mode with something else
                if (combatObjective.Mode == AIObjectiveCombat.CombatMode.Offensive && mode != AIObjectiveCombat.CombatMode.Offensive) { return; }
                if (combatObjective.Mode != mode || combatObjective.Enemy != attacker || (combatObjective.Enemy == null && attacker == null))
                {
                    // Replace the old objective with the new.
                    ObjectiveManager.Objectives.Remove(combatObjective);
                    ObjectiveManager.AddObjective(CreateCombatObjective());
                }
            }
            else
            {
                if (delay > 0)
                {
                    ObjectiveManager.AddObjective(CreateCombatObjective(), delay);
                }
                else
                {
                    ObjectiveManager.AddObjective(CreateCombatObjective());
                }
            }

            AIObjectiveCombat CreateCombatObjective()
            {
                var objective = new AIObjectiveCombat(Character, attacker, mode, objectiveManager)
                {
                    HoldPosition = Character.Info?.Job?.Prefab.Identifier == "watchman",
                    abortCondition = abortCondition,
                    allowHoldFire = allowHoldFire,
                };
                if (onAbort != null)
                {
                    objective.Abandoned += onAbort;
                }
                return objective;
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
        }

        public override void SelectTarget(AITarget target)
        {
            SelectedAiTarget = target;
        }

        private void CheckCrouching(float deltaTime)
        {
            crouchRaycastTimer -= deltaTime;
            if (crouchRaycastTimer > 0.0f) { return; }

            crouchRaycastTimer = crouchRaycastInterval;

            //start the raycast in front of the character in the direction it's heading to
            Vector2 startPos = Character.SimPosition;
            startPos.X += MathHelper.Clamp(Character.AnimController.TargetMovement.X, -1.0f, 1.0f);

            //do a raycast upwards to find any walls
            float minCeilingDist = Character.AnimController.Collider.height / 2 + Character.AnimController.Collider.radius + 0.1f;

            shouldCrouch = Submarine.PickBody(startPos, startPos + Vector2.UnitY * minCeilingDist, null, Physics.CollisionWall, customPredicate: (fixture) => { return !(fixture.Body.UserData is Submarine); }) != null;
        }

        public bool AllowCampaignInteraction()
        {
            if (Character == null || Character.Removed || Character.IsIncapacitated) { return false; }

            switch (ObjectiveManager.CurrentObjective)
            {
                case AIObjectiveCombat _:
                case AIObjectiveFindSafety _:
                case AIObjectiveExtinguishFires _:
                case AIObjectiveFightIntruders _:
                case AIObjectiveFixLeaks _:
                    return false;
            }
            return true;
        }

        public bool TryToMoveItem(Item item, Inventory targetInventory, bool dropIfCannotMove = true)
        {
            var pickable = item.GetComponent<Pickable>();
            if (pickable == null) { return false; }
            int targetSlot = -1;
            //check if all the slots required by the item are free
            foreach (InvSlotType slots in pickable.AllowedSlots)
            {
                if (slots.HasFlag(InvSlotType.Any)) { continue; }
                for (int i = 0; i < targetInventory.Items.Length; i++)
                {
                    if (targetInventory is CharacterInventory characterInventory)
                    {
                        //slot not needed by the item, continue
                        if (!slots.HasFlag(characterInventory.SlotTypes[i])) { continue; }
                    }
                    targetSlot = i;
                    //slot free, continue
                    var otherItem = targetInventory.Items[i];
                    if (otherItem == null) { continue; }
                    //try to move the existing item to LimbSlot.Any and continue if successful
                    if (otherItem.AllowedSlots.Contains(InvSlotType.Any) && targetInventory.TryPutItem(otherItem, Character, new List<InvSlotType>() { InvSlotType.Any }))
                    {
                        continue;
                    }
                    if (dropIfCannotMove)
                    {
                        //if everything else fails, simply drop the existing item
                        otherItem.Drop(Character);
                    }
                }
            }
            return targetInventory.TryPutItem(item, targetSlot, false, false, Character);
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
        public static bool HasDivingSuit(Character character, float conditionPercentage = 0) => HasItem(character, "divingsuit", out _, "oxygensource", conditionPercentage, requireEquipped: true);

        /// <summary>
        /// Check whether the character has a diving mask in usable condition plus some oxygen.
        /// </summary>
        public static bool HasDivingMask(Character character, float conditionPercentage = 0) => HasItem(character, "divingmask", out _, "oxygensource", conditionPercentage, requireEquipped: true);

        public static bool HasItem(Character character, string tagOrIdentifier, out Item item, string containedTag = null, float conditionPercentage = 0, bool requireEquipped = false)
        {
            item = null;
            if (character == null) { return false; }
            if (character.Inventory == null) { return false; }
            item = character.Inventory.FindItemByIdentifier(tagOrIdentifier) ?? character.Inventory.FindItemByTag(tagOrIdentifier);
            return item != null &&
                item.ConditionPercentage >= conditionPercentage &&
                (!requireEquipped || character.HasEquippedItem(item)) &&
                (containedTag == null ||
                (item.ContainedItems != null &&
                item.ContainedItems.Any(i => i.ItemTags.HasTag(containedTag) && i.ConditionPercentage > conditionPercentage)));
        }

        public static void ItemTaken(Item item, Character character)
        {
            if (item == null || character == null || item.GetComponent<LevelResource>() != null) { return; }
            Character thief = character;
            bool someoneSpoke = false;

            if (item.SpawnedInOutpost && thief.TeamID != Character.TeamType.FriendlyNPC && !item.ItemTags.HasTag("handlocker"))
            {
                foreach (Character otherCharacter in Character.CharacterList)
                {
                    if (otherCharacter == thief || otherCharacter.TeamID == thief.TeamID || otherCharacter.IsDead ||
                        otherCharacter.Info?.Job == null ||
                        !(otherCharacter.AIController is HumanAIController otherHumanAI) ||
                        !otherHumanAI.VisibleHulls.Contains(thief.CurrentHull))
                    {
                        continue;
                    }
                    //if (!otherCharacter.IsFacing(thief.WorldPosition)) { continue; }
                    if (!otherCharacter.CanSeeCharacter(thief)) { continue; }
                    if (!someoneSpoke)
                    {
                        if (!item.StolenDuringRound && GameMain.GameSession?.Campaign?.Map?.CurrentLocation != null)
                        {
                            var reputationLoss = MathHelper.Clamp(
                                (item.Prefab.GetMinPrice() ?? 0) * Reputation.ReputationLossPerStolenItemPrice, 
                                Reputation.MinReputationLossPerStolenItem, Reputation.MaxReputationLossPerStolenItem);
                            GameMain.GameSession.Campaign.Map.CurrentLocation.Reputation.Value -= reputationLoss;
                        }
                        item.StolenDuringRound = true;
                        otherCharacter.Speak(TextManager.Get("dialogstealwarning"), null, Rand.Range(0.5f, 1.0f), "thief", 10.0f);
                        someoneSpoke = true;
                    }
                    // React if we are security
                    if (!TriggerSecurity(otherHumanAI))
                    {
                        // Else call the others
                        foreach (Character security in Character.CharacterList.Where(c => c.TeamID == otherCharacter.TeamID).OrderByDescending(c => Vector2.DistanceSquared(thief.WorldPosition, c.WorldPosition)))
                        {
                            if (TriggerSecurity(security.AIController as HumanAIController))
                            {
                                // Only alert one guard at a time
                                break;
                            }
                        }
                    }
                }
            }
            else if (item.OwnInventory?.FindItem(it => it.SpawnedInOutpost, true) is { } foundItem)
            {
                ItemTaken(foundItem, character);
            }

            bool TriggerSecurity(HumanAIController humanAI)
            {
                if (humanAI == null) { return false; }
                if (!humanAI.Character.IsSecurity) { return false; }
                if (humanAI.ObjectiveManager.IsCurrentObjective<AIObjectiveCombat>()) { return false; }
                humanAI.AddCombatObjective(AIObjectiveCombat.CombatMode.Arrest, thief, delay: GetReactionTime(),
                    abortCondition: () => thief.Inventory.FindItem(it => it != null && it.StolenDuringRound, true) == null,
                    onAbort: () =>
                    {
                        if (item != null && !item.Removed && humanAI != null && !humanAI.ObjectiveManager.IsCurrentObjective<AIObjectiveGetItem>())
                        {
                            humanAI.ObjectiveManager.AddObjective(new AIObjectiveGetItem(humanAI.Character, item, humanAI.ObjectiveManager, equip: false)
                            {
                                BasePriority = 10
                            });
                        }
                    },
                    allowHoldFire: true);
                return true;
            }
        }

        // 0.225 - 0.375
        private static float GetReactionTime() => reactionTime * Rand.Range(0.75f, 1.25f);

        /// <summary>
        /// Updates the hull safety for all ai characters in the team. The idea is that the crew communicates (magically) via radio about the threads.
        /// The safety levels need to be calculated for each bot individually, because the formula takes into account things like current orders.
        /// There's now a cached value per each hull, which should prevent too frequent calculations.
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
                            if (item.Repairables.All(r => item.ConditionPercentage >= r.RepairThreshold)) { continue; }
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

        public float GetDamageDoneByAttacker(Character attacker)
        {
            if (!damageDoneByAttacker.TryGetValue(attacker, out float dmg))
            {
                dmg = 0;
            }
            return dmg;
        }

        private void StoreHullSafety(Hull hull, HullSafety safety)
        {
            if (knownHulls.ContainsKey(hull))
            {
                // Update existing. Shouldn't currently happen, but things might change.
                knownHulls[hull] = safety;
            }
            else
            {
                // Add new
                knownHulls.Add(hull, safety);
            }
        }

        private float CalculateHullSafety(Hull hull, Character character, IEnumerable<Hull> visibleHulls = null)
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
            bool ignoreFire = objectiveManager.HasActiveObjective<AIObjectiveExtinguishFire>();
            bool ignoreWater = HasDivingSuit(character);
            bool ignoreOxygen = ignoreWater || HasDivingMask(character);
            bool ignoreEnemies = ObjectiveManager.IsCurrentObjective<AIObjectiveFightIntruders>();
            float safety = CalculateHullSafety(hull, visibleHulls, character, ignoreWater, ignoreOxygen, ignoreFire, ignoreEnemies);
            if (isCurrentHull)
            {
                CurrentHullSafety = safety;
            }
            return safety;
        }

        private static float CalculateHullSafety(Hull hull, IEnumerable<Hull> visibleHulls, Character character, bool ignoreWater = false, bool ignoreOxygen = false, bool ignoreFire = false, bool ignoreEnemies = false)
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
                float calculateFire(Hull h) => h.FireSources.Count * 0.5f + h.FireSources.Sum(fs => fs.DamageRange) / h.Size.X;
                // Even the smallest fire reduces the safety by 50%
                float fire = visibleHulls == null ? calculateFire(hull) : visibleHulls.Sum(h => calculateFire(h));
                fireFactor = MathHelper.Lerp(1, 0, MathHelper.Clamp(fire, 0, 1));
            }
            float enemyFactor = 1;
            if (!ignoreEnemies)
            {
                bool isValidTarget(Character e) => IsActive(e) && !IsFriendly(character, e);
                int enemyCount = visibleHulls == null ?
                    Character.CharacterList.Count(e => isValidTarget(e) && e.CurrentHull == hull) :
                    Character.CharacterList.Count(e => isValidTarget(e) && visibleHulls.Contains(e.CurrentHull));
                // The hull safety decreases 90% per enemy up to 100% (TODO: test smaller percentages)
                enemyFactor = MathHelper.Lerp(1, 0, MathHelper.Clamp(enemyCount * 0.9f, 0, 1));
            }
            float safety = oxygenFactor * waterFactor * fireFactor * enemyFactor;
            return MathHelper.Clamp(safety * 100, 0, 100);
        }

        public float GetHullSafety(Hull hull, Character character, IEnumerable<Hull> visibleHulls = null)
        {
            if (!knownHulls.TryGetValue(hull, out HullSafety hullSafety))
            {
                hullSafety = new HullSafety(CalculateHullSafety(hull, character, visibleHulls));
                StoreHullSafety(hull, hullSafety);
            }
            else if (hullSafety.IsStale)
            {
                hullSafety.Reset(CalculateHullSafety(hull, character, visibleHulls));
            }
            return hullSafety.safety;
        }

        public static float GetHullSafety(Hull hull, IEnumerable<Hull> visibleHulls, Character character, bool ignoreWater = false, bool ignoreOxygen = false, bool ignoreFire = false, bool ignoreEnemies = false)
        {
            HullSafety hullSafety;
            if (character.AIController is HumanAIController controller)
            {
                if (!controller.knownHulls.TryGetValue(hull, out hullSafety))
                {
                    hullSafety = new HullSafety(CalculateHullSafety(hull, visibleHulls, character, ignoreWater, ignoreOxygen, ignoreFire, ignoreEnemies));
                    controller.StoreHullSafety(hull, hullSafety);
                }
                else if (hullSafety.IsStale)
                {
                    hullSafety.Reset(CalculateHullSafety(hull, visibleHulls, character, ignoreWater, ignoreOxygen, ignoreFire, ignoreEnemies));
                }
            }
            else
            {
#if DEBUG
                DebugConsole.ThrowError("Cannot store the hull safety, because was unable to cast the AIController as HumanAIController. This should never happen!");
#endif
                return CalculateHullSafety(hull, visibleHulls, character, ignoreWater, ignoreOxygen, ignoreFire, ignoreEnemies);
            }
            return hullSafety.safety;
        }

        public void FaceTarget(ISpatialEntity target) => Character.AnimController.TargetDir = target.WorldPosition.X > Character.WorldPosition.X ? Direction.Right : Direction.Left;

        public static bool IsFriendly(Character me, Character other, bool onlySameTeam = false)
        {
            bool sameTeam = me.TeamID == other.TeamID;
            // Only enemies are in the Team "None"
            bool friendlyTeam = me.TeamID != Character.TeamType.None && other.TeamID != Character.TeamType.None;
            bool teamGood = sameTeam || friendlyTeam && !onlySameTeam;
            if (!teamGood) { return false; }
            bool speciesGood = other.SpeciesName == me.SpeciesName || other.Params.CompareGroup(me.Params.Group);
            if (!speciesGood) { return false; }
            if (me.TeamID == Character.TeamType.FriendlyNPC && other.TeamID == Character.TeamType.Team1 && GameMain.GameSession?.GameMode is CampaignMode campaign)
            {
                var reputation = campaign.Map?.CurrentLocation?.Reputation;
                if (reputation != null && reputation.NormalizedValue < Reputation.HostileThreshold)
                {
                    return false;
                }
            }
            return true;
        }

        public static bool IsActive(Character other) => other != null && !other.Removed && !other.IsDead && !other.IsUnconscious;

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

        public static int CountCrew(Character character, Func<HumanAIController, bool> predicate = null, bool onlyActive = true, bool onlyBots = false)
        {
            if (character == null) { return 0; }
            int count = 0;
            foreach (var other in Character.CharacterList)
            {
                if (onlyActive && !IsActive(other))
                {
                    continue;
                }
                if (onlyBots && other.IsPlayer)
                {
                    continue;
                }
                if (FilterCrewMember(character, other))
                {
                    if (predicate == null || predicate(other.AIController as HumanAIController))
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

        public static bool IsItemOperatedByAnother(Character character, ItemComponent target, out Character operatingCharacter)
        {
            operatingCharacter = null;
            if (target?.Item == null) { return false; }
            foreach (var c in Character.CharacterList)
            {
                if (character != null && c == character) { continue; }
                if (character?.AIController is HumanAIController humanAi && !humanAi.IsFriendly(c)) { continue; }
                if (c.SelectedConstruction != target.Item) { continue; }
                operatingCharacter = c;
                // If the other character is player, don't try to operate
                if (c.IsRemotePlayer || Character.Controlled == c) { return true; }
                if (c.AIController is HumanAIController controllingHumanAi)
                {
                    // If the other character is ordered to operate the item, let him do it
                    if (controllingHumanAi.ObjectiveManager.IsCurrentOrder<AIObjectiveOperateItem>())
                    {
                        return true;
                    }
                    else
                    {
                        if (character == null)
                        {
                            return true;
                        }
                        else if (target is Steering)
                        {
                            // Steering is hard-coded -> cannot use the required skills collection defined in the xml
                            return character.GetSkillLevel("helm") <= c.GetSkillLevel("helm");
                        }
                        else
                        {
                            return target.DegreeOfSuccess(character) <= target.DegreeOfSuccess(c);
                        }
                    }
                }
                else
                {
                    // Shouldn't go here, unless we allow non-humans to operate items
                    return false;
                }

            }
            return false;
        }

        #region Wrappers
        public bool IsFriendly(Character other) => IsFriendly(Character, other);
        public void DoForEachCrewMember(Action<HumanAIController> action) => DoForEachCrewMember(Character, action);
        public bool IsTrueForAnyCrewMember(Func<HumanAIController, bool> predicate) => IsTrueForAnyCrewMember(Character, predicate);
        public bool IsTrueForAllCrewMembers(Func<HumanAIController, bool> predicate) => IsTrueForAllCrewMembers(Character, predicate);
        public int CountCrew(Func<HumanAIController, bool> predicate = null, bool onlyActive = true, bool onlyBots = false) => CountCrew(Character, predicate, onlyActive, onlyBots);
        public bool IsItemOperatedByAnother(ItemComponent target, out Character operatingCharacter) => IsItemOperatedByAnother(Character, target, out operatingCharacter);
        #endregion
    }
}
