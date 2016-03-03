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

        private Item targetItem;

        private int currSearchIndex;

        private bool canBeCompleted;

        public bool IgnoreContainedItems;

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
            if (targetItem != null)
            {
                if (Vector2.Distance(character.Position, targetItem.Position) < targetItem.PickDistance)
                {
                    int targetSlot = -1;
                    if (equip)
                    {
                        var pickable = targetItem.GetComponent<Pickable>();
                        //check if all the slots required by the item are free
                        foreach (LimbSlot slots in pickable.AllowedSlots)
                        {
                            if (slots.HasFlag(LimbSlot.Any)) continue;
                            
                            for (int i = 0; i<character.Inventory.Items.Length; i++)
                            {
                                //slot not needed by the item, continue
                                if (!slots.HasFlag(CharacterInventory.limbSlots[i])) continue;

                                targetSlot = i;

                                //slot free, continue
                                if (character.Inventory.Items[i] == null) continue;

                                //try to move the existing item to LimbSlot.Any and continue if successful
                                if (character.Inventory.TryPutItem(character.Inventory.Items[i], new List<LimbSlot>() { LimbSlot.Any }, false)) continue;

                                //if everything else fails, simply drop the existing item
                                character.Inventory.Items[i].Drop();
                            }
                        }
                    }

                    targetItem.Pick(character, false, true);

                    if (targetSlot>-1 && character.Inventory.IsInLimbSlot(targetItem, LimbSlot.Any))
                    {
                        character.Inventory.TryPutItem(targetItem, targetSlot, true, false);
                    }

                }

                return;
            }
            

            for (int i = 0; i<10 && currSearchIndex<Item.ItemList.Count-2; i++)
            {
                currSearchIndex++;

                //don't try to get items from outside the sub
                if (Item.ItemList[currSearchIndex].CurrentHull == null) continue;

                if (!Item.ItemList[currSearchIndex].HasTag(itemName) && Item.ItemList[currSearchIndex].Name != itemName) continue;
                if (IgnoreContainedItems && Item.ItemList[currSearchIndex].Container != null) continue;
                if (Item.ItemList[currSearchIndex].ParentInventory is CharacterInventory) continue;
                
                targetItem = Item.ItemList[currSearchIndex];

                AddGoToObjective(targetItem);


                return;
            }

            if (currSearchIndex >= Item.ItemList.Count) canBeCompleted = false;
        }

        private void AddGoToObjective(Item item)
        {
            Item moveToTarget = item;
            while (moveToTarget.Container != null)
            {
                moveToTarget = moveToTarget.Container;
            }

            AddSubObjective(new AIObjectiveGoTo(moveToTarget, character));
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
