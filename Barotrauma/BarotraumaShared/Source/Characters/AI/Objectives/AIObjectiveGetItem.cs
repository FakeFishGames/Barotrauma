using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveGetItem : AIObjective
    {
        public Func<Item, float> GetItemPriority;

        private string[] itemNames;

        private Item targetItem, moveToTarget;

        private int currSearchIndex;

        private bool canBeCompleted;

        public bool IgnoreContainedItems;

        private AIObjectiveGoTo goToObjective;

        private float currItemPriority;

        private bool equip;

        public override bool CanBeCompleted
        {
            get { return canBeCompleted; }
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }

            return 1.0f;
        }

        public AIObjectiveGetItem(Character character, Item targetItem, bool equip = false)
            : base(character, "")
        {
            canBeCompleted = true;

            this.equip = equip;

            currSearchIndex = 0;

            this.targetItem = targetItem;
        }

        public AIObjectiveGetItem(Character character, string itemName, bool equip = false)
            : this(character, new string[] { itemName }, equip)
        {
        }

        public AIObjectiveGetItem(Character character, string[] itemNames, bool equip = false)
            : base(character, "")
        {
            canBeCompleted = true;

            this.equip = equip;

            currSearchIndex = 0;

            this.itemNames = itemNames;
        }

        protected override void Act(float deltaTime)
        {
            FindTargetItem();
            if (targetItem == null || moveToTarget == null)
            {
                character?.AIController?.SteeringManager?.Reset();
                return;
            }

            if (Vector2.Distance(character.Position, moveToTarget.Position) < targetItem.InteractDistance * 2.0f)
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

                        for (int i = 0; i < character.Inventory.Items.Length; i++)
                        {
                            //slot not needed by the item, continue
                            if (!slots.HasFlag(CharacterInventory.limbSlots[i])) continue;

                            targetSlot = i;

                            //slot free, continue
                            if (character.Inventory.Items[i] == null) continue;

                            //try to move the existing item to LimbSlot.Any and continue if successful
                            if (character.Inventory.TryPutItem(character.Inventory.Items[i], character, new List<InvSlotType>() { InvSlotType.Any })) continue;

                            //if everything else fails, simply drop the existing item
                            character.Inventory.Items[i].Drop();
                        }
                    }
                }

                targetItem.TryInteract(character, false, true);

                if (targetSlot > -1 && character.Inventory.IsInLimbSlot(targetItem, InvSlotType.Any))
                {
                    character.Inventory.TryPutItem(targetItem, targetSlot, false, false, character);
                }
            }
            else
            {
                if (goToObjective == null || moveToTarget != goToObjective.Target)
                {
                    //check if we're already looking for a diving gear
                    bool gettingDivingGear = (targetItem != null && targetItem.Prefab.NameMatches("Diving Gear") || targetItem.HasTag("diving")) ||
                                            (itemNames != null && (itemNames.Contains("diving") || itemNames.Contains("Diving Gear")));

                    //don't attempt to get diving gear to reach the destination if the item we're trying to get is diving gear
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
            if (itemNames == null)
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
                if (!itemNames.Any(name => item.Prefab.NameMatches(name) || item.HasTag(name))) continue;

                //if the item is inside a character's inventory, don't steal it unless the character is dead
                if (item.ParentInventory is CharacterInventory)
                {
                    Character owner = item.ParentInventory.Owner as Character;
                    if (owner != null && !owner.IsDead) continue;
                }

                //if the item is inside an item, which is inside a character's inventory, don't steal it
                Item rootContainer = item.GetRootContainer();
                if (rootContainer != null && rootContainer.ParentInventory is CharacterInventory)
                {
                    Character owner = rootContainer.ParentInventory.Owner as Character;
                    if (owner != null && !owner.IsDead) continue;
                }

                float itemPriority = 0.0f;
                if (GetItemPriority != null)
                {
                    //ignore if the item has zero priority
                    itemPriority = GetItemPriority(item);
                    if (itemPriority <= 0.0f) continue;
                }

                itemPriority = itemPriority - Vector2.Distance((rootContainer ?? item).Position, character.Position) * 0.01f;

                //ignore if the item has a lower priority than the currently selected one
                if (moveToTarget != null && itemPriority < currItemPriority) continue;

                currItemPriority = itemPriority;

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
            if (getItem.equip != equip) return false;
            if (getItem.itemNames != null && itemNames != null)
            {
                if (getItem.itemNames.Length != itemNames.Length) return false;
                for (int i = 0; i < getItem.itemNames.Length; i++)
                {
                    if (getItem.itemNames[i] != itemNames[i]) return false;
                }
                return true;
            }
            else if (getItem.itemNames == null && itemNames == null)
            {
                return getItem.targetItem == targetItem;
            }

            return false;
        }

        public override bool IsCompleted()
        {
            if (itemNames != null)
            {
                foreach (string itemName in itemNames)
                {
                    var matchingItem = character.Inventory.FindItem(itemName);
                    if (matchingItem != null && (!equip || character.HasEquippedItem(matchingItem))) return true;
                }
                return false;

            }
            else if (targetItem != null)
            {
                return character.Inventory.Items.Contains(targetItem) && (!equip || character.HasEquippedItem(targetItem));
            }
            else
            {
                return false;
            }
        }
    }
}
