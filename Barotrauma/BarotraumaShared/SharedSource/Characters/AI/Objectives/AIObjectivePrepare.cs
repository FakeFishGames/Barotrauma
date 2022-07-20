using Barotrauma.Extensions;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma
{
    class AIObjectivePrepare : AIObjective
    {
        public override Identifier Identifier { get; set; } = "prepare".ToIdentifier();
        public override string DebugTag => $"{Identifier}";
        public override bool KeepDivingGearOn => true;
        public override bool KeepDivingGearOnAlsoWhenInactive => true;
        public override bool PrioritizeIfSubObjectivesActive => true;

        private AIObjectiveGetItem getSingleItemObjective;
        private AIObjectiveGetItems getAllItemsObjective;
        private AIObjectiveGetItems getMultipleItemsObjective;
        private bool subObjectivesCreated;
        private readonly Item targetItem;
        private readonly ImmutableArray<Identifier> requiredItems;
        private readonly ImmutableArray<Identifier> optionalItems;
        private readonly HashSet<Item> items = new HashSet<Item>();
        public bool KeepActiveWhenReady { get; set; }
        public bool CheckInventory { get; set; }
        public bool FindAllItems { get; set; }
        public bool Equip { get; set; }
        public bool EvaluateCombatPriority { get; set; }

        private AIObjective GetSubObjective()
        {
            if (getSingleItemObjective != null) { return getSingleItemObjective; }
            if (getAllItemsObjective == null || getAllItemsObjective.IsCompleted)
            {
                return getMultipleItemsObjective;
            }
            return getAllItemsObjective;
        }
        public AIObjectivePrepare(Character character, AIObjectiveManager objectiveManager, Item targetItem, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier)
        {
            this.targetItem = targetItem;
        }

        public AIObjectivePrepare(Character character, AIObjectiveManager objectiveManager, IEnumerable<Identifier> optionalItems, IEnumerable<Identifier> requiredItems = null, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier)
        {
            this.optionalItems = optionalItems.ToImmutableArray();
            if (requiredItems != null)
            {
                this.requiredItems = requiredItems.ToImmutableArray();
            }
        }

        protected override bool CheckObjectiveSpecific() => IsCompleted;

        protected override float GetPriority()
        {
            if (!IsAllowed)
            {
                Priority = 0;
                Abandon = true;
                return Priority;
            }
            Priority = objectiveManager.GetOrderPriority(this);
            var subObjective = GetSubObjective();
            if (subObjective != null && subObjective.IsCompleted)
            {
                Priority = 0;
                items.RemoveWhere(i => i == null || i.Removed || !i.IsOwnedBy(character));
                if (items.None())
                {
                    Abandon = true;

                }
                else if (items.Any(i => i.Components.Any(i => !i.IsLoaded(character))))
                {
                    Reset();
                }
            }
            return Priority;
        }

        protected override void Act(float deltaTime)
        {
            if (character.LockHands)
            {
                Abandon = true;
                return;
            }
            if (!subObjectivesCreated)
            {
                if (FindAllItems && targetItem == null)
                {
                    getMultipleItemsObjective = CreateObjectives(optionalItems, requireAll: false);
                    if (requiredItems != null && requiredItems.Any())
                    {
                        getAllItemsObjective = CreateObjectives(requiredItems, requireAll: true);
                    }
                    AIObjectiveGetItems CreateObjectives(IEnumerable<Identifier> itemTags, bool requireAll)
                    {
                        AIObjectiveGetItems objectiveReference = null;
                        if (!TryAddSubObjective(ref objectiveReference, () => new AIObjectiveGetItems(character, objectiveManager, itemTags)
                        {
                            CheckInventory = CheckInventory,
                            Equip = Equip,
                            EvaluateCombatPriority = EvaluateCombatPriority,
                            RequireLoaded = true,
                            RequireAllItems = requireAll
                        },
                        onCompleted: () =>
                        {
                            if (KeepActiveWhenReady)
                            {
                                if (objectiveReference != null)
                                {
                                    foreach (var item in objectiveReference.achievedItems)
                                    {
                                        if (item?.IsOwnedBy(character) != null)
                                        {
                                            items.Add(item);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                IsCompleted = true;
                            }
                        },
                        onAbandon: () => Abandon = true))
                        {
                            Abandon = true;
                        }
                        return objectiveReference;
                    }
                }
                else
                {
                    Func<AIObjectiveGetItem> getItemConstructor;
                    if (targetItem != null)
                    {
                        getItemConstructor = () => new AIObjectiveGetItem(character, targetItem, objectiveManager, equip: Equip)
                        {
                            SpeakIfFails = true
                        };
                    }
                    else
                    {
                        IEnumerable<Identifier> allItems = optionalItems;
                        if (requiredItems != null && requiredItems.Any())
                        {
                            allItems = requiredItems;
                        }
                        getItemConstructor = () => new AIObjectiveGetItem(character, allItems, objectiveManager, equip: Equip, checkInventory: CheckInventory)
                        {
                            EvaluateCombatPriority = EvaluateCombatPriority,
                            SpeakIfFails = true,
                            RequireLoaded = true
                        };
                    }
                    if (!TryAddSubObjective(ref getSingleItemObjective, getItemConstructor,
                    onCompleted: () =>
                    {
                        if (KeepActiveWhenReady)
                        {
                            if (getSingleItemObjective != null)
                            {
                                var item = getSingleItemObjective?.TargetItem;
                                if (item?.IsOwnedBy(character) != null)
                                {
                                    items.Add(item);
                                }
                            }
                        }
                        else
                        {
                            IsCompleted = true;
                        }
                    },
                    onAbandon: () => Abandon = true))
                    {
                        Abandon = true;
                    }
                }
                subObjectivesCreated = true;
            }
        }

        public override void Reset()
        {
            base.Reset();
            items.Clear();
            subObjectivesCreated = false;
            getMultipleItemsObjective = null;
            getSingleItemObjective = null;
            getAllItemsObjective = null;
        }
    }
}
