using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class AIObjectiveGetItem : AIObjective
    {
        private string itemName;

        private Item targetItem, moveToTarget;

        private int currSearchIndex;

        private bool canBeCompleted;

        public bool IgnoreContainedItems;

        private AIObjectiveGoTo goToObjective;

        private bool equip;

        public override bool CanBeCompleted
        {
            get { return canBeCompleted; }
        }

        public AIObjectiveGetItem(Character character, Item targetItem, bool equip = false)
            : base(character, "")
        {
            canBeCompleted = true;

            this.equip = equip;

            currSearchIndex = 0;

            this.targetItem = targetItem;
        }

        public AIObjectiveGetItem(Character character, string itemName, bool equip=false)
            : base (character, "")
        {
            canBeCompleted = true;

            this.equip = equip;

            currSearchIndex = 0;
            
            this.itemName = itemName;
        }

        protected override void Act(float deltaTime)
        {
            FindTargetItem();
            if (targetItem == null || moveToTarget == null) return;

            if (Vector2.Distance(character.Position, moveToTarget.Position) < targetItem.PickDistance*2.0f)
            {
                int targetSlot = -1;
                if (equip)
                {
                    var pickable = targetItem.GetComponent<Pickable>();
                    if (pickable == null)
                    {
                        canBeCompleted = false;
                        return;
                    }

                    //check if all the slots required by the item are free
                    foreach (InvSlotType slots in pickable.AllowedSlots)
                    {
                        if (slots.HasFlag(InvSlotType.Any)) continue;
                            
                        for (int i = 0; i<character.Inventory.Items.Length; i++)
                        {
                            //slot not needed by the item, continue
                            if (!slots.HasFlag(CharacterInventory.limbSlots[i])) continue;

                            targetSlot = i;

                            //slot free, continue
                            if (character.Inventory.Items[i] == null) continue;

                            //try to move the existing item to LimbSlot.Any and continue if successful
                            if (character.Inventory.TryPutItem(character.Inventory.Items[i], new List<InvSlotType>() { InvSlotType.Any }, false)) continue;

                            //if everything else fails, simply drop the existing item
                            character.Inventory.Items[i].Drop();
                        }
                    }
                }

                targetItem.Pick(character, false, true);

                if (targetSlot > -1 && character.Inventory.IsInLimbSlot(targetItem, InvSlotType.Any))
                {
                    character.Inventory.TryPutItem(targetItem, targetSlot, true, false);
                }
            }
            else
            {
                if (goToObjective == null) 
                {
                    bool gettingDivingGear = itemName == "diving" || itemName == "Diving Gear";
                    goToObjective = new AIObjectiveGoTo(moveToTarget, character, false, !gettingDivingGear);
                }

                goToObjective.TryComplete(deltaTime);
            }
            
        }

        /// <summary>
        /// searches for an item that matches the desired item and adds a goto subobjective if one is found
        /// </summary>
        private void FindTargetItem()
        {
            if (itemName == null)
            {
                if (targetItem == null) canBeCompleted = false;
                return;
            }

            float currDist = moveToTarget == null ? 0.0f : Vector2.DistanceSquared(moveToTarget.Position, character.Position);
            
            for (int i = 0; i < 10 && currSearchIndex < Item.ItemList.Count - 2; i++)
            {
                currSearchIndex++;

                var item = Item.ItemList[currSearchIndex];

                if (item.CurrentHull == null || item.Condition <= 0.0f) continue;
                if (IgnoreContainedItems && item.Container != null) continue;
                if (item.Name != itemName && !item.HasTag(itemName)) continue;

                //if the item is inside a character's inventory, don't steal it
                if (item.ParentInventory is CharacterInventory) continue;

                //if the item is inside an item, which is inside a character's inventory, don't steal it
                if (item.ParentInventory != null && item.ParentInventory.Owner is Item)
                {
                    if (((Item)item.ParentInventory.Owner).ParentInventory is CharacterInventory) continue;
                }

                //ignore if item is further away than the currently targeted item
                Item rootContainer = item.GetRootContainer();
                if (moveToTarget != null && Vector2.DistanceSquared((rootContainer ?? item).Position, character.Position) > currDist) continue;
                
                targetItem = item;
                moveToTarget = rootContainer ?? item;
            }

            //if searched through all the items and a target wasn't found, can't be completed
            if (currSearchIndex >= Item.ItemList.Count && targetItem == null) canBeCompleted = false;
        }
                
        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveGetItem getItem = otherObjective as AIObjectiveGetItem;
            if (getItem == null) return false;
            return (getItem.itemName == itemName);
        }

        public override bool IsCompleted()
        {
            if (itemName!=null)
            {
                return character.Inventory.FindItem(itemName) != null;
            }
            else if (targetItem!= null)
            {
                return character.Inventory.Items.Contains(targetItem);
            }
            else
            {
                return false;
            }
        }
    }
}
