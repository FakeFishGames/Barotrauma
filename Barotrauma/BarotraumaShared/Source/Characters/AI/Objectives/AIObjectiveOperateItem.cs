using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveOperateItem : AIObjective
    {
        private ItemComponent component, controller;

        private Entity operateTarget;

        private bool isCompleted;

        private bool canBeCompleted;

        private bool requireEquip;

        private bool useController;

        private AIObjectiveGoTo gotoObjective;

        public override bool CanBeCompleted
        {
            get
            {
                if (gotoObjective != null && !gotoObjective.CanBeCompleted) return false;

                if (useController && controller == null) return false;

                return canBeCompleted;
            }
        }

        public Entity OperateTarget
        {
            get { return operateTarget; }
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }

            return 1.0f;
        }

        public AIObjectiveOperateItem(ItemComponent item, Character character, string option, bool requireEquip, Entity operateTarget = null, bool useController = false)
            : base (character, option)
        {
            this.component = item ?? throw new System.ArgumentNullException("item", "Attempted to create an AIObjectiveOperateItem with a null target.");
            this.requireEquip = requireEquip;
            this.operateTarget = operateTarget;
            this.useController = useController;

            if (useController)
            {
                var controllers = component.Item.GetConnectedComponents<Controller>();
                if (controllers.Any()) controller = controllers[0];
            }

            canBeCompleted = true;
        }

        protected override void Act(float deltaTime)
        {
            ItemComponent target = useController ? controller : component;

            if (useController && controller == null)
            {
                character.Speak(TextManager.Get("DialogCantFindController").Replace("[item]", component.Item.Name), null, 2.0f, "cantfindcontroller", 30.0f);
                return;
            }

            if (target.CanBeSelected)
            { 
                if (Vector2.Distance(character.Position, target.Item.Position) < target.Item.InteractDistance
                    || target.Item.IsInsideTrigger(character.WorldPosition))
                {
                    if (character.SelectedConstruction != target.Item && target.CanBeSelected)
                    {
                        target.Item.TryInteract(character, false, true);
                    }

                    if (component.AIOperate(deltaTime, character, this)) isCompleted = true;
                    return;
                }

                AddSubObjective(gotoObjective = new AIObjectiveGoTo(target.Item, character));
            }
            else
            {
                if (!character.Inventory.Items.Contains(component.Item))
                {
                    AddSubObjective(new AIObjectiveGetItem(character, component.Item, true));
                }
                else
                {
                    if (requireEquip && !character.HasEquippedItem(component.Item))
                    {
                        //the item has to be equipped before using it if it's holdable
                        var holdable = component.Item.GetComponent<Holdable>();
                        if (holdable == null)
                        {
                            DebugConsole.ThrowError("AIObjectiveOperateItem failed - equipping item " + component.Item + " is required but the item has no Holdable component");
                            return;
                        }

                        for (int i = 0; i < character.Inventory.Capacity; i++)
                        {
                            if (character.Inventory.SlotTypes[i] == InvSlotType.Any ||
                                !holdable.AllowedSlots.Any(s => s.HasFlag(character.Inventory.SlotTypes[i])))
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
                                    character.Inventory.Items[i].Drop();
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

                    if (component.AIOperate(deltaTime, character, this)) isCompleted = true;
                }
            }
        }

        public override bool IsCompleted()
        {
            return isCompleted;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveOperateItem operateItem = otherObjective as AIObjectiveOperateItem;
            if (operateItem == null) return false;

            return (operateItem.component == component);
        }
    }
}
