using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveContainItem: AIObjective
    {
        public override string Identifier { get; set; } = "contain item";

        public Func<Item, float> GetItemPriority;

        public string[] ignoredContainerIdentifiers;
        public bool checkInventory = true;

        //if the item can't be found, spawn it in the character's inventory (used by outpost NPCs and in some cases also enemy NPCs, like pirates)
        private readonly bool spawnItemIfNotFound;

        //can either be a tag or an identifier
        public readonly string[] itemIdentifiers;
        public readonly ItemContainer container;
        private readonly Item item;
        public Item ItemToContain { get; private set; }

        private AIObjectiveGetItem getItemObjective;
        private AIObjectiveGoTo goToObjective;

        private readonly HashSet<Item> containedItems = new HashSet<Item>();

        public bool AllowToFindDivingGear { get; set; } = true;
        public bool AllowDangerousPressure { get; set; }
        public float ConditionLevel { get; set; } = 1;
        public bool Equip { get; set; }
        public bool RemoveEmpty { get; set; } = true;
        public bool RemoveExisting { get; set; }
        /// <summary>
        /// Only remove existing items when the contain target can't be put in the inventory
        /// </summary>
        public bool RemoveExistingWhenNecessary { get; set; }
        public Func<Item, bool> RemoveExistingPredicate { get; set; }
        public int? RemoveMax { get; set; }

        public bool MoveWholeStack { get; set; }

        private int _itemCount = 1;
        public int ItemCount
        {
            get { return _itemCount; }
            set
            {
                _itemCount = Math.Max(value, 1);
            }
        }

        public AIObjectiveContainItem(Character character, Item item, ItemContainer container, AIObjectiveManager objectiveManager, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier)
        {
            this.container = container;
            this.item = item;
        }

        public AIObjectiveContainItem(Character character, string itemIdentifier, ItemContainer container, AIObjectiveManager objectiveManager, float priorityModifier = 1, bool spawnItemIfNotFound = false)
            : this(character, new string[] { itemIdentifier }, container, objectiveManager, priorityModifier, spawnItemIfNotFound) { }

        public AIObjectiveContainItem(Character character, string[] itemIdentifiers, ItemContainer container, AIObjectiveManager objectiveManager, float priorityModifier = 1, bool spawnItemIfNotFound = false) 
            : base(character, objectiveManager, priorityModifier)
        {
            this.itemIdentifiers = itemIdentifiers;
            this.spawnItemIfNotFound = spawnItemIfNotFound;
            for (int i = 0; i < itemIdentifiers.Length; i++)
            {
                itemIdentifiers[i] = itemIdentifiers[i].ToLowerInvariant();
            }
            this.container = container;
        }

        protected override bool CheckObjectiveSpecific()
        {
            if (IsCompleted) { return true; }
            if (container == null || (container.Item != null && container.Item.IsThisOrAnyContainerIgnoredByAI(character)))
            {
                Abandon = true;
                return false;
            }
            if (item != null)
            {
                return container.Inventory.Contains(item);
            }
            else
            {
                int containedItemCount = 0;
                foreach (Item it in container.Inventory.AllItems)
                {
                    if (CheckItem(it))
                    {
                        containedItemCount++;
                    }
                }
                return containedItemCount >= ItemCount;
            }
        }

        private bool CheckItem(Item i) => itemIdentifiers.Any(id => i.Prefab.Identifier == id || i.HasTag(id)) && i.ConditionPercentage >= ConditionLevel && !i.IsThisOrAnyContainerIgnoredByAI(character);

        protected override void Act(float deltaTime)
        {
            if (container?.Item == null || container.Item.Removed || container.Item.IsThisOrAnyContainerIgnoredByAI(character))
            {
                Abandon = true;
                return;
            }
            ItemToContain = item ?? character.Inventory.FindItem(i => CheckItem(i) && i.Container != container.Item, recursive: true);
            if (ItemToContain != null)
            {
                if (!character.CanInteractWith(ItemToContain, checkLinked: false))
                {
                    Abandon = true;
                    return;
                }
                if (character.CanInteractWith(container.Item, checkLinked: false))
                {
                    if (RemoveExisting || (RemoveExistingWhenNecessary && !container.Inventory.CanBePut(item)))
                    {
                        HumanAIController.UnequipContainedItems(container.Item, predicate: RemoveExistingPredicate, unequipMax: RemoveMax);
                    }
                    else if (RemoveEmpty)
                    {
                        HumanAIController.UnequipEmptyItems(container.Item);
                    }
                    Inventory originalInventory = ItemToContain.ParentInventory;
                    var slots = originalInventory?.FindIndices(ItemToContain);
                    if (container.Inventory.TryPutItem(ItemToContain, null))
                    {
                        if (MoveWholeStack && slots != null)
                        {
                            foreach (int slot in slots)
                            {
                                foreach (Item item in originalInventory.GetItemsAt(slot).ToList())
                                {
                                    container.Inventory.TryPutItem(item, null);
                                }
                            }
                            IsCompleted = true;
                        }
                    }
                    else
                    {
                        if (ItemToContain.ParentInventory == character.Inventory && character.IsInFriendlySub)
                        {
                            ItemToContain.Drop(character);
                        }
                        Abandon = true;
                    }
                }
                else
                {
                    TryAddSubObjective(ref goToObjective, () => new AIObjectiveGoTo(container.Item, character, objectiveManager, getDivingGearIfNeeded: AllowToFindDivingGear)
                    {
                        TargetName = container.Item.Name,
                        AbortCondition = obj =>
                            container?.Item == null || container.Item.Removed || container.Item.IsThisOrAnyContainerIgnoredByAI(character) ||
                            ItemToContain == null || ItemToContain.Removed ||
                            !ItemToContain.IsOwnedBy(character) || container.Item.GetRootInventoryOwner() is Character c && c != character,
                        SpeakIfFails = !objectiveManager.IsCurrentOrder<AIObjectiveCleanupItems>()
                    },
                    onAbandon: () => Abandon = true,
                    onCompleted: () => RemoveSubObjective(ref goToObjective));
                }
            }
            else
            {
                if (character.Submarine == null)
                {
                    Abandon = true;
                }
                else
                {
                    // No matching items in the inventory, try to get an item
                    TryAddSubObjective(ref getItemObjective, () =>
                        new AIObjectiveGetItem(character, itemIdentifiers, objectiveManager, equip: Equip, checkInventory: checkInventory, spawnItemIfNotFound: spawnItemIfNotFound)
                        {
                            GetItemPriority = GetItemPriority,
                            ignoredContainerIdentifiers = ignoredContainerIdentifiers,
                            ignoredItems = containedItems,
                            AllowToFindDivingGear = AllowToFindDivingGear,
                            AllowDangerousPressure = AllowDangerousPressure,
                            TargetCondition = ConditionLevel,
                            ItemFilter = (Item potentialItem) => RemoveEmpty ? container.CanBeContained(potentialItem) : container.Inventory.CanBePut(potentialItem),
                            ItemCount = ItemCount,
                            TakeWholeStack = MoveWholeStack
                        }, onAbandon: () =>
                        {
                            Abandon = true;
                        }, onCompleted: () =>
                        {
                            if (getItemObjective?.TargetItem != null)
                            {
                                containedItems.Add(getItemObjective.TargetItem);
                            }
                            RemoveSubObjective(ref getItemObjective);
                        });
                }
            }
        }

        public override void Reset()
        {
            base.Reset();
            getItemObjective = null;
            goToObjective = null;
            containedItems.Clear();
        }
    }
}
