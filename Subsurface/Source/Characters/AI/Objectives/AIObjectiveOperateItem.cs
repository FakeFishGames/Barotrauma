using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class AIObjectiveOperateItem : AIObjective
    {
        private ItemComponent component;

        private Entity operateTarget;

        private bool isCompleted;

        private bool canBeCompleted;

        public override bool CanBeCompleted
        {
            get
            {
                return canBeCompleted;
            }
        }

        public Entity OperateTarget
        {
            get { return operateTarget; }
        }

        public AIObjectiveOperateItem(ItemComponent item, Character character, string option, Entity operateTarget = null, bool useController = false)
            :base (character, option)
        {
            component = item;

            this.operateTarget = operateTarget;

            if (useController)
            {
                var controllers = item.Item.GetConnectedComponents<Controller>();
                if (controllers.Any()) component = controllers[0];
            }


            canBeCompleted = true;
        }

        protected override void Act(float deltaTime)
        {
            if (component.CanBeSelected)
            { 
                if (Vector2.Distance(character.Position, component.Item.Position) < component.Item.PickDistance
                    || component.Item.IsInsideTrigger(character.WorldPosition))
                {
                    if (character.SelectedConstruction != component.Item && component.CanBeSelected)
                    {
                        component.Item.Pick(character, false, true);
                    }

                    if (component.AIOperate(deltaTime, character, this)) isCompleted = true;
                    return;
                }

                AddSubObjective(new AIObjectiveGoTo(component.Item, character));
            }
            else
            {
                if (!character.Inventory.Items.Contains(component.Item))
                {
                    AddSubObjective(new AIObjectiveGetItem(character, component.Item, true));
                }
                else
                {
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
