using Barotrauma.Items.Components;
using System;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveDecontainItem : AIObjective
    {
        public override string Identifier { get; set; } = "decontain item";

        public Func<Item, float> GetItemPriority;

        //can either be a tag or an identifier
        private readonly string[] itemIdentifiers;
        private readonly ItemContainer sourceContainer;
        private ItemContainer targetContainer;
        private readonly Item targetItem;

        private AIObjectiveGetItem getItemObjective;
        private AIObjectiveContainItem containObjective;

        public AIObjectiveGetItem GetItemObjective => getItemObjective;
        public AIObjectiveContainItem ContainObjective => containObjective;

        public Item TargetItem => targetItem;
        public ItemContainer TargetContainer => targetContainer;

        public bool Equip { get; set; }

        public bool TakeWholeStack { get; set; }

        /// <summary>
        /// If true drops the item when containing the item fails.
        /// In both cases abandons the objective.
        /// Note that has no effect if the target container was not defined (always drops) -> completes when the item is dropped.
        /// </summary>
        public bool DropIfFails { get; set; } = true;

        public AIObjectiveDecontainItem(Character character, Item targetItem, AIObjectiveManager objectiveManager, ItemContainer sourceContainer = null, ItemContainer targetContainer = null, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier)
        {
            this.targetItem = targetItem;
            this.sourceContainer = sourceContainer;
            this.targetContainer = targetContainer;
        }

        public AIObjectiveDecontainItem(Character character, string itemIdentifier, AIObjectiveManager objectiveManager, ItemContainer sourceContainer, ItemContainer targetContainer = null, float priorityModifier = 1) 
            : this(character, new string[] { itemIdentifier }, objectiveManager, sourceContainer, targetContainer, priorityModifier) { }

        public AIObjectiveDecontainItem(Character character, string[] itemIdentifiers, AIObjectiveManager objectiveManager, ItemContainer sourceContainer, ItemContainer targetContainer = null, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier)
        {
            this.itemIdentifiers = itemIdentifiers;
            for (int i = 0; i < itemIdentifiers.Length; i++)
            {
                itemIdentifiers[i] = itemIdentifiers[i].ToLowerInvariant();
            }
            this.sourceContainer = sourceContainer;
            this.targetContainer = targetContainer;
        }

        protected override bool CheckObjectiveSpecific() => IsCompleted;

        protected override void Act(float deltaTime)
        {
            Item itemToDecontain = 
                targetItem ?? 
                sourceContainer.Inventory.FindItem(i => itemIdentifiers.Any(id => i.Prefab.Identifier == id || i.HasTag(id) && !i.IgnoreByAI(character)), recursive: false);

            if (itemToDecontain == null)
            {
                Abandon = true;
                return;
            }
            if (itemToDecontain.IgnoreByAI(character))
            {
                Abandon = true;
                return;
            }
            if (targetContainer == null)
            {
                if (sourceContainer == null)
                {
                    Abandon = true;
                    return;
                }
                if (itemToDecontain.Container != sourceContainer.Item)
                {
                    IsCompleted = true;
                    return;
                }
            }
            else if (targetContainer.Inventory.Contains(itemToDecontain))
            {
                IsCompleted = true;
                return;
            }
            if (getItemObjective == null && !itemToDecontain.IsOwnedBy(character))
            {
                TryAddSubObjective(ref getItemObjective,
                    constructor: () => new AIObjectiveGetItem(character, targetItem, objectiveManager, Equip) { TakeWholeStack = this.TakeWholeStack },
                    onAbandon: () => Abandon = true);
                return;
            }
            if (targetContainer != null)
            {
                TryAddSubObjective(ref containObjective,
                    constructor: () => new AIObjectiveContainItem(character, itemToDecontain, targetContainer, objectiveManager)
                    {
                        MoveWholeStack = TakeWholeStack,
                        Equip = Equip,
                        RemoveEmpty = false,
                        GetItemPriority = GetItemPriority,
                        ignoredContainerIdentifiers = sourceContainer != null ? new string[] { sourceContainer.Item.Prefab.Identifier } : null
                    },
                    onCompleted: () => IsCompleted = true,
                    onAbandon: () => Abandon = true);
            }
            else
            {
                itemToDecontain.Drop(character);
                IsCompleted = true;
            }
        }

        public override void Reset()
        {
            base.Reset();
            getItemObjective = null;
            containObjective = null;
        }

        protected override void OnAbandon()
        {
            base.OnAbandon();
            if (DropIfFails && targetItem != null && targetItem.IsOwnedBy(character))
            {
                targetItem.Drop(character);
            }
        }
    }
}
