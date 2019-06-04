using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveOperateItem : AIObjective
    {
        public override string DebugTag => "operate item";

        private ItemComponent component, controller;
        private Entity operateTarget;
        private bool isCompleted;
        private bool requireEquip;
        private bool useController;
        private AIObjectiveGoTo goToObjective;
        private AIObjectiveGetItem getItemObjective;

        public override bool CanBeCompleted => base.CanBeCompleted && (!useController || controller != null);

        public Entity OperateTarget => operateTarget;
        public ItemComponent Component => component;

        public override float GetPriority()
        {
            if (component.Item.ConditionPercentage <= 0) { return 0; }
            if (objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }
            if (component.Item.CurrentHull == null) { return 0; }
            if (component.Item.CurrentHull.FireSources.Count > 0) { return 0; }
            if (Character.CharacterList.Any(c => c.CurrentHull == component.Item.CurrentHull && !HumanAIController.IsFriendly(c))) { return 0; }
            float devotion = MathHelper.Min(10, Priority);
            float value = devotion + AIObjectiveManager.OrderPriority * PriorityModifier;
            float max = MathHelper.Min((AIObjectiveManager.OrderPriority - 1), 90);
            return MathHelper.Clamp(value, 0, max);
        }

        public AIObjectiveOperateItem(ItemComponent item, Character character, AIObjectiveManager objectiveManager, string option, bool requireEquip, Entity operateTarget = null, bool useController = false, float priorityModifier = 1) 
            : base (character, objectiveManager, priorityModifier, option)
        {
            this.component = item ?? throw new System.ArgumentNullException("item", "Attempted to create an AIObjectiveOperateItem with a null target.");
            this.requireEquip = requireEquip;
            this.operateTarget = operateTarget;
            this.useController = useController;
            if (useController)
            {
                //try finding the controller with the simpler non-recursive method first
                controller =
                        component.Item.GetConnectedComponents<Controller>().FirstOrDefault() ??
                        component.Item.GetConnectedComponents<Controller>(recursive: true).FirstOrDefault();
            }
        }

        protected override void Act(float deltaTime)
        {
            ItemComponent target = useController ? controller : component;
            if (useController && controller == null)
            {
                character.Speak(TextManager.GetWithVariable("DialogCantFindController", "[item]", component.Item.Name, true), null, 2.0f, "cantfindcontroller", 30.0f);
                abandon = true;
                return;
            }
            if (target.CanBeSelected)
            {
                if (character.CanInteractWith(target.Item, out _, checkLinked: false))
                {
                    // Don't allow to operate an item that someone already operates, unless this objective is an order
                    if (objectiveManager.CurrentOrder != this && Character.CharacterList.Any(c => c.SelectedConstruction == target.Item && c != character && HumanAIController.IsFriendly(c)))
                    {
                        abandon = true;
                        return;
                    }
                    if (character.SelectedConstruction != target.Item)
                    {
                        target.Item.TryInteract(character, false, true);
                    }
                    if (component.AIOperate(deltaTime, character, this))
                    {
                        isCompleted = true;
                    }
                }
                else
                {
                    TryAddSubObjective(ref goToObjective, () => new AIObjectiveGoTo(target.Item, character, objectiveManager));
                }
            }
            else
            {
                if (component.Item.GetComponent<Pickable>() == null)
                {
                    //controller/target can't be selected and the item cannot be picked -> objective can't be completed
                    abandon = true;
                    return;
                }
                else if (!character.Inventory.Items.Contains(component.Item))
                {
                    TryAddSubObjective(ref getItemObjective, () => new AIObjectiveGetItem(character, component.Item, objectiveManager, equip: true));
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
                            DebugConsole.ThrowError("AIObjectiveOperateItem failed - equipping item " + component.Item + " is required but the item has no Holdable component");
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
                        isCompleted = true;
                    }
                }
            }
        }

        public override bool IsCompleted() => isCompleted && !IsLoop;

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            if (!(otherObjective is AIObjectiveOperateItem operateItem)) { return false; }
            return (operateItem.component == component || otherObjective.Option == Option);
        }
    }
}
