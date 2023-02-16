using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveOperateItem : AIObjective
    {
        public override Identifier Identifier { get; set; } = "operate item".ToIdentifier();
        public override string DebugTag =>  $"{Identifier} {component.Name}";

        public override bool AllowAutomaticItemUnequipping => true;
        public override bool AllowMultipleInstances => true;
        public override bool AllowInAnySub => true;
        public override bool PrioritizeIfSubObjectivesActive => component != null && (component is Reactor || component is Turret);

        private readonly ItemComponent component, controller;
        private readonly Entity operateTarget;
        private readonly bool requireEquip;
        private readonly bool useController;
        private AIObjectiveGoTo goToObjective;
        private AIObjectiveGetItem getItemObjective;

        /// <summary>
        /// If undefined, a default filter will be used.
        /// </summary>
        public Func<PathNode, bool> EndNodeFilter;

        public bool Override { get; set; } = true;

        public override bool CanBeCompleted => base.CanBeCompleted && (!useController || controller != null);

        public override bool IsDuplicate<T>(T otherObjective) => base.IsDuplicate(otherObjective) && otherObjective is AIObjectiveOperateItem operateObjective && operateObjective.component == component;

        public Entity OperateTarget => operateTarget;
        public ItemComponent Component => component;

        public ItemComponent GetTarget() => useController ? controller : component;

        public Func<bool> completionCondition;
        private bool isDoneOperating;

        public float? OverridePriority = null;

        protected override float GetPriority()
        {
            bool isOrder = objectiveManager.IsOrder(this);
            if (!IsAllowed || character.LockHands)
            {
                Priority = 0;
                Abandon = !isOrder;
                return Priority;
            }
            if (!isOrder && component.Item.ConditionPercentage <= 0)
            {
                Priority = 0;
            }
            else
            {
                if (OverridePriority.HasValue)
                {
                    Priority = OverridePriority.Value;
                }
                else if (isOrder)
                {
                    Priority = objectiveManager.GetOrderPriority(this);
                }
                ItemComponent target = GetTarget();
                Item targetItem = target?.Item;
                if (targetItem == null)
                {
#if DEBUG
                    DebugConsole.ThrowError("Item or component of AI Objective Operate item was null. This shouldn't happen.");
#endif
                    Abandon = true;
                    Priority = 0;
                    return Priority;
                }
                else if (targetItem.IsClaimedByBallastFlora)
                {
                    Priority = 0;
                    return Priority;
                }
                var reactor = component?.Item.GetComponent<Reactor>();
                if (reactor != null)
                {
                    if (!isOrder)
                    {
                        if (reactor.LastUserWasPlayer && character.TeamID != CharacterTeamType.FriendlyNPC)
                        {
                            // The reactor was previously operated by a player -> ignore.
                            Priority = 0;
                            return Priority;
                        }
                    }
                    switch (Option.Value.ToLowerInvariant())
                    {
                        case "shutdown":
                            if (!reactor.PowerOn)
                            {
                                Priority = 0;
                                return Priority;
                            }
                            break;
                        case "powerup":
                            // Check that we don't already have another order that is targeting the same item.
                            // Without this the autonomous objective will tell the bot to turn the reactor on again.
                            if (IsAnotherOrderTargetingSameItem(objectiveManager.ForcedOrder) || objectiveManager.CurrentOrders.Any(o => IsAnotherOrderTargetingSameItem(o.Objective)))
                            {
                                Priority = 0;
                                return Priority;
                            }
                            bool IsAnotherOrderTargetingSameItem(AIObjective objective)
                            {
                                return objective is AIObjectiveOperateItem operateObjective && operateObjective != this && operateObjective.GetTarget() == target && operateObjective.Option != Option;
                            }
                            break;
                    }
                }
                else if (!isOrder)
                {
                    var steering = component?.Item.GetComponent<Steering>();
                    if (steering != null && (steering.AutoPilot || HumanAIController.IsTrueForAnyCrewMember(c => c != HumanAIController && c.Character.IsCaptain)))
                    {
                        // Ignore if already set to autopilot or if there's a captain onboard
                        Priority = 0;
                        return Priority;
                    }
                }
                if (targetItem.CurrentHull == null ||
                    targetItem.Submarine != character.Submarine && !isOrder ||
                    targetItem.CurrentHull.FireSources.Any() ||
                    HumanAIController.IsItemOperatedByAnother(target, out _) ||
                    Character.CharacterList.Any(c => c.CurrentHull == targetItem.CurrentHull && !HumanAIController.IsFriendly(c) && HumanAIController.IsActive(c))
                    || component.Item.IgnoreByAI(character) || useController && controller.Item.IgnoreByAI(character))
                {
                    Priority = 0;
                }
                else
                {
                    if (isOrder)
                    {
                        float max = objectiveManager.GetOrderPriority(this);
                        float value = CumulatedDevotion + (max * PriorityModifier);
                        Priority = MathHelper.Clamp(value, 0, max);
                    }
                    else if (!OverridePriority.HasValue)
                    {
                        float value = CumulatedDevotion + (AIObjectiveManager.LowestOrderPriority * PriorityModifier);
                        float max = AIObjectiveManager.LowestOrderPriority - 1;
                        if (reactor != null && reactor.PowerOn && reactor.FissionRate > 1 && reactor.AutoTemp && Option == "powerup")
                        {
                            // Already on, no need to operate.
                            value = 0;
                        }
                        Priority = MathHelper.Clamp(value, 0, max);
                    }
                }
            }
            return Priority;
        }

        public AIObjectiveOperateItem(ItemComponent item, Character character, AIObjectiveManager objectiveManager, Identifier option, bool requireEquip,
            Entity operateTarget = null, bool useController = false, ItemComponent controller = null, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier, option)
        {
            component = item ?? throw new ArgumentNullException("item", "Attempted to create an AIObjectiveOperateItem with a null target.");
            this.requireEquip = requireEquip;
            this.operateTarget = operateTarget;
            this.useController = useController;
            if (useController) { this.controller = controller ?? component?.Item?.FindController(); }
            var target = GetTarget();
            if (target == null)
            {
                Abandon = true;
#if DEBUG
                throw new Exception("target null");
#endif
            }
            else if (!target.Item.IsInteractable(character))
            {
                Abandon = true;
            }
        }

        protected override void Act(float deltaTime)
        {
            if (character.LockHands)
            {
                Abandon = true;
                return;
            }
            ItemComponent target = GetTarget();
            if (useController && controller == null)
            {
                if (character.IsOnPlayerTeam)
                {
                    character.Speak(TextManager.GetWithVariable("DialogCantFindController", "[item]", component.Item.Name).Value, delay: 2.0f, identifier: "cantfindcontroller".ToIdentifier(), minDurationBetweenSimilar: 30.0f);
                }
                Abandon = true;
                return;
            }
            if (operateTarget != null)
            {
                if (HumanAIController.IsTrueForAnyCrewMember(other => other != HumanAIController && other.Character.IsBot && other.ObjectiveManager.GetActiveObjective() is AIObjectiveOperateItem operateObjective && operateObjective.operateTarget == operateTarget))
                {
                    // Another crew member is already targeting this entity (leak).
                    Abandon = true;
                    return;
                }
            }
            if (target.CanBeSelected)
            {
                if (!character.IsClimbing && character.CanInteractWith(target.Item, out _, checkLinked: false))
                {
                    if (target.Item.GetComponent<Controller>() is not Controller { ControlCharacterPose: true })
                    {
                        HumanAIController.FaceTarget(target.Item);
                    }
                    else
                    {
                        HumanAIController.SteeringManager.Reset();
                    }
                    if (character.SelectedItem != target.Item && character.SelectedSecondaryItem != target.Item)
                    {
                        target.Item.TryInteract(character, forceSelectKey: true);
                    }
                    if (component.CrewAIOperate(deltaTime, character, this))
                    {
                        isDoneOperating = completionCondition == null || completionCondition();
                    }
                }
                else
                {
                    TryAddSubObjective(ref goToObjective, () => new AIObjectiveGoTo(target.Item, character, objectiveManager, closeEnough: 50)
                    {
                        TargetName = target.Item.Name,
                        endNodeFilter = EndNodeFilter ?? AIObjectiveGetItem.CreateEndNodeFilter(target.Item)
                    },
                        onAbandon: () => Abandon = true,
                        onCompleted: () => RemoveSubObjective(ref goToObjective));
                }
            }
            else
            {
                if (component.Item.GetComponent<Pickable>() == null)
                {
                    //controller/target can't be selected and the item cannot be picked -> objective can't be completed
                    Abandon = true;
                    return;
                }
                else if (!character.Inventory.Contains(component.Item))
                {
                    TryAddSubObjective(ref getItemObjective, () => new AIObjectiveGetItem(character, component.Item, objectiveManager, equip: true),
                        onAbandon: () => Abandon = true,
                        onCompleted: () => RemoveSubObjective(ref getItemObjective));
                }
                else
                {
                    if (requireEquip && !character.HasEquippedItem(component.Item))
                    {
                        //the item has to be equipped before using it if it's holdable
                        var holdable = component.Item.GetComponent<Holdable>();
                        if (holdable == null)
                        {
#if DEBUG
                            DebugConsole.ThrowError($"{character.Name}: AIObjectiveOperateItem failed - equipping item " + component.Item + " is required but the item has no Holdable component");
#endif
                            return;
                        }
                        for (int i = 0; i < character.Inventory.Capacity; i++)
                        {
                            if (character.Inventory.SlotTypes[i] == InvSlotType.Any || !holdable.AllowedSlots.Any(s => s.HasFlag(character.Inventory.SlotTypes[i])))
                            {
                                continue;
                            }
                            //equip slot already taken
                            var existingItem = character.Inventory.GetItemAt(i);
                            if (existingItem != null)
                            {
                                //try to put the item in an Any slot, and drop it if that fails
                                if (!existingItem.AllowedSlots.Contains(InvSlotType.Any) ||
                                    !character.Inventory.TryPutItem(existingItem, character, new List<InvSlotType>() { InvSlotType.Any }))
                                {
                                    existingItem.Drop(character);
                                }
                            }
                            if (character.Inventory.TryPutItem(component.Item, i, true, false, character))
                            {
                                component.Item.Equip(character);
                                break;
                            }
                        }
                        return;
                    }
                    if (component.CrewAIOperate(deltaTime, character, this))
                    {
                        isDoneOperating = completionCondition == null || completionCondition();
                    }
                }
            }
        }

        protected override bool CheckObjectiveSpecific() => isDoneOperating && !IsLoop;

        public override void Reset()
        {
            base.Reset();
            goToObjective = null;
            getItemObjective = null;
        }
    }
}
