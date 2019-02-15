using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveGetItem : AIObjective
    {
        public override string DebugTag => "get item";

        public Func<Item, float> GetItemPriority;

        //can be either tags or identifiers
        private string[] itemIdentifiers;

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
            currSearchIndex = -1;
            this.equip = equip;
            this.targetItem = targetItem;
        }

        public AIObjectiveGetItem(Character character, string itemIdentifier, bool equip = false)
            : this(character, new string[] { itemIdentifier }, equip)
        {
        }

        public AIObjectiveGetItem(Character character, string[] itemIdentifiers, bool equip = false)
            : base(character, "")
        {
            canBeCompleted = true;
            currSearchIndex = -1;
            this.equip = equip;
            this.itemIdentifiers = itemIdentifiers;
            for (int i = 0; i < itemIdentifiers.Length; i++)
            {
                itemIdentifiers[i] = itemIdentifiers[i].ToLowerInvariant();
            }

            CheckInventory();
        }

        private void CheckInventory()
        {
            if (itemIdentifiers == null)
            {
                return;
            }

            for (int i = 0; i < character.Inventory.Items.Length; i++)
            {
                if (character.Inventory.Items[i] == null || character.Inventory.Items[i].Condition <= 0.0f) continue;
                if (itemIdentifiers.Any(id => character.Inventory.Items[i].Prefab.Identifier == id || character.Inventory.Items[i].HasTag(id)))
                {
                    targetItem = character.Inventory.Items[i];
                    moveToTarget = targetItem;
                    currItemPriority = 100.0f;
                    break;
                }
                //check items inside items (tool inside a toolbox etc)
                var containedItems = character.Inventory.Items[i].ContainedItems;
                if (containedItems != null)
                {
                    foreach (Item containedItem in containedItems)
                    {
                        if (containedItem == null || containedItem.Condition <= 0.0f) continue;
                        if (itemIdentifiers.Any(id => containedItem.Prefab.Identifier == id || containedItem.HasTag(id)))
                        {
                            targetItem = containedItem;
                            moveToTarget = character.Inventory.Items[i];
                            currItemPriority = 100.0f;
                            break;
                        }
                    }
                }
            }
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
                            if (!slots.HasFlag(character.Inventory.SlotTypes[i])) continue;

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

                if (targetSlot > -1 && !character.HasEquippedItem(targetItem))
                {
                    character.Inventory.TryPutItem(targetItem, targetSlot, false, false, character);
                }
            }
            else
            {
                if (goToObjective == null || moveToTarget != goToObjective.Target)
                {
                    //check if we're already looking for a diving gear
                    bool gettingDivingGear = (targetItem != null && targetItem.Prefab.Identifier == "divingsuit" || targetItem.HasTag("diving")) ||
                                            (itemIdentifiers != null && (itemIdentifiers.Contains("diving") || itemIdentifiers.Contains("divingsuit")));

                    //don't attempt to get diving gear to reach the destination if the item we're trying to get is diving gear
                    goToObjective = new AIObjectiveGoTo(moveToTarget, character, false, !gettingDivingGear);
                }

                goToObjective.TryComplete(deltaTime);
                if (!goToObjective.CanBeCompleted) targetItem = null;
            }

        }

        /// <summary>
        /// searches for an item that matches the desired item and adds a goto subobjective if one is found
        /// </summary>
        private void FindTargetItem()
        {
            if (itemIdentifiers == null)
            {
                if (targetItem == null) canBeCompleted = false;
                return;
            }

            float currDist = moveToTarget == null ? 0.0f : Vector2.DistanceSquared(moveToTarget.Position, character.Position);

            for (int i = 0; i < 10 && currSearchIndex < Item.ItemList.Count - 1; i++)
            {
                currSearchIndex++;

                var item = Item.ItemList[currSearchIndex];

                if (item.CurrentHull == null || item.Condition <= 0.0f) continue;
                if (IgnoreContainedItems && item.Container != null) continue;
                if (!itemIdentifiers.Any(id => item.Prefab.Identifier == id || item.HasTag(id))) continue;

                //if the item is inside a character's inventory, don't steal it unless the character is dead
                if (item.ParentInventory is CharacterInventory)
                {
                    if (item.ParentInventory.Owner is Character owner && !owner.IsDead) continue;
                }

                //if the item is inside an item, which is inside a character's inventory, don't steal it
                Item rootContainer = item.GetRootContainer();
                if (rootContainer != null && rootContainer.ParentInventory is CharacterInventory)
                {
                    if (rootContainer.ParentInventory.Owner is Character owner && !owner.IsDead) continue;
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
            if (currSearchIndex >= Item.ItemList.Count - 1 && targetItem == null) canBeCompleted = false;
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            AIObjectiveGetItem getItem = otherObjective as AIObjectiveGetItem;
            if (getItem == null) return false;
            if (getItem.equip != equip) return false;
            if (getItem.itemIdentifiers != null && itemIdentifiers != null)
            {
                if (getItem.itemIdentifiers.Length != itemIdentifiers.Length) return false;
                for (int i = 0; i < getItem.itemIdentifiers.Length; i++)
                {
                    if (getItem.itemIdentifiers[i] != itemIdentifiers[i]) return false;
                }
                return true;
            }
            else if (getItem.itemIdentifiers == null && itemIdentifiers == null)
            {
                return getItem.targetItem == targetItem;
            }

            return false;
        }

        public override bool IsCompleted()
        {
            if (itemIdentifiers != null)
            {
                foreach (string itemName in itemIdentifiers)
                {
                    var matchingItem = character.Inventory.FindItemByTag(itemName) ?? character.Inventory.FindItemByIdentifier(itemName);
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
