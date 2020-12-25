using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveOperateItem : AIObjective
    {
        public override string DebugTag => $"operate item {component.Name}";
        public override bool AllowAutomaticItemUnequipping => true;
        public override bool AllowMultipleInstances => true;
        public override bool AllowInAnySub => true;

        private ItemComponent component, controller;
        private Entity operateTarget;
        private bool requireEquip;
        private bool useController;
        private AIObjectiveGoTo goToObjective;
        private AIObjectiveGetItem getItemObjective;

        public bool Override { get; set; } = true;

        public override bool CanBeCompleted => base.CanBeCompleted && (!useController || controller != null);

        public override bool IsDuplicate<T>(T otherObjective) => base.IsDuplicate(otherObjective) && otherObjective is AIObjectiveOperateItem operateObjective && operateObjective.component == component;

        public Entity OperateTarget => operateTarget;
        public ItemComponent Component => component;

        public ItemComponent GetTarget() => useController ? controller : component;

        public Func<bool> completionCondition;
        private bool isDoneOperating;

        public override float GetPriority()
        {
            bool isOrder = objectiveManager.CurrentOrder == this;
            if (!IsAllowed || character.LockHands)
            {
                Priority = 0;
                Abandon = !isOrder;
                return Priority;
            }
            if (component.Item.ConditionPercentage <= 0)
            {
                Priority = 0;
            }
            else
            {
                if (isOrder)
                {
                    Priority = AIObjectiveManager.OrderPriority;
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
                var reactor = component?.Item.GetComponent<Reactor>();
                if (reactor != null)
                {
                    if (!isOrder)
                    {
                        if (reactor.LastUserWasPlayer && character.TeamID != Character.TeamType.FriendlyNPC ||
                            HumanAIController.IsTrueForAnyCrewMember(c =>
                                c.ObjectiveManager.CurrentOrder is AIObjectiveOperateItem operateOrder && operateOrder.GetTarget() == target))
                        {
                            Priority = 0;
                            return Priority;
                        }
                    }
                    switch (Option)
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
                            if (objectiveManager.CurrentOrder is AIObjectiveOperateItem operateOrder && operateOrder != this && operateOrder.GetTarget() == target && operateOrder.Option != Option)
                            {
                                Priority = 0;
                                return Priority;
                            }
                            break;
                    }
                }
                if (targetItem.CurrentHull == null ||
                    targetItem.Submarine != character.Submarine && !isOrder ||
                    targetItem.CurrentHull.FireSources.Any() ||
                    HumanAIController.IsItemOperatedByAnother(target, out _) ||
                    Character.CharacterList.Any(c => c.CurrentHull == targetItem.CurrentHull && !HumanAIController.IsFriendly(c) && HumanAIController.IsActive(c)))
                {
                    Priority = 0;
                }
                else
                {
                    float value = CumulatedDevotion + (AIObjectiveManager.OrderPriority * PriorityModifier);
                    float max = isOrder ? MathHelper.Min(AIObjectiveManager.OrderPriority, 90) : AIObjectiveManager.RunPriority - 1;
                    if (!isOrder && reactor != null && reactor.PowerOn && Option == "powerup")
                    {
                        // Decrease the priority when targeting a reactor that is already on.
                        value /= 2;
                    }
                    Priority = MathHelper.Clamp(value, 0, max);
                }
            }
            return Priority;
        }

        public AIObjectiveOperateItem(ItemComponent item, Character character, AIObjectiveManager objectiveManager, string option, bool requireEquip,
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
            else if (target.Item.NonInteractable)
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
                character.Speak(TextManager.GetWithVariable("DialogCantFindController", "[item]", component.Item.Name, true), null, 2.0f, "cantfindcontroller", 30.0f);
                Abandon = true;
                return;
            }
            // If this is not an order...
            if (objectiveManager.CurrentOrder != this)
            {
                // Don't allow to operate an item that someone with a better skills already operates
                if (HumanAIController.IsItemOperatedByAnother(target, out _))
                {
                    // Don't abandon
                    return;
                }
                if (component.Item.IgnoreByAI || (useController && controller.Item.IgnoreByAI))
                {
                    Abandon = true;
                    return;
                }
            }
            if (operateTarget != null)
            {
                if (HumanAIController.IsTrueForAnyCrewMember(other => other != HumanAIController && other.ObjectiveManager.GetActiveObjective() is AIObjectiveOperateItem operateObjective && operateObjective.operateTarget == operateTarget))
                {
                    // Another crew member is already targeting this entity.
                    Abandon = true;
                    return;
                }
            }
            if (target.CanBeSelected)
            {
                if (!character.IsClimbing && character.CanInteractWith(target.Item, out _, checkLinked: false))
                {
                    HumanAIController.FaceTarget(target.Item);
                    if (character.SelectedConstruction != target.Item)
                    {
                        target.Item.TryInteract(character, false, true);
                    }
                    if (component.AIOperate(deltaTime, character, this))
                    {
                        isDoneOperating = completionCondition == null || completionCondition();
                    }
                }
                else
                {
                    TryAddSubObjective(ref goToObjective, () => new AIObjectiveGoTo(target.Item, character, objectiveManager, closeEnough: 50)
                    {
                        DialogueIdentifier = "dialogcannotreachtarget",
                        TargetName = target.Item.Name,
                        endNodeFilter = node => node.Waypoint.Ladders == null
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
                else if (!character.Inventory.Items.Contains(component.Item))
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
                            if (character.Inventory.Items[i] != null)
                            {
                                //try to put the item in an Any slot, and drop it if that fails
                                if (!character.Inventory.Items[i].AllowedSlots.Contains(InvSlotType.Any) ||
                                    !character.Inventory.TryPutItem(character.Inventory.Items[i], character, new List<InvSlotType>() { InvSlotType.Any }))
                                {
                                    character.Inventory.Items[i].Drop(character);
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
                    if (component.AIOperate(deltaTime, character, this))
                    {
                        isDoneOperating = completionCondition == null || completionCondition();
                    }
                }
            }
        }

        protected override bool Check() => isDoneOperating && !IsLoop;

        public override void Reset()
        {
            base.Reset();
            goToObjective = null;
            getItemObjective = null;
        }
    }
}
