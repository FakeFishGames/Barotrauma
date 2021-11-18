#nullable enable
using Barotrauma.Extensions;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma
{
    class AIObjectivePrepare : AIObjective
    {
        public override string Identifier { get; set; } = "prepare";
        public override string DebugTag => $"{Identifier}";
        public override bool KeepDivingGearOn => true;

        private AIObjectiveGetItem? getSingleItemObjective;
        private AIObjectiveGetItems? getMultipleItemsObjective;
        private bool subObjectivesCreated;
        private readonly ImmutableArray<string> gearTags;
        private readonly HashSet<Item> items = new HashSet<Item>();
        public bool KeepActiveWhenReady { get; set; }
        public bool CheckInventory { get; set; }
        public bool FindAllItems { get; set; }
        public bool Equip { get; set; }
        public bool EvaluateCombatPriority { get; set; }

        private AIObjective? GetSubObjective() => getSingleItemObjective ?? getMultipleItemsObjective as AIObjective;

        public AIObjectivePrepare(Character character, AIObjectiveManager objectiveManager, IEnumerable<string> items, float priorityModifier = 1) 
            : base(character, objectiveManager, priorityModifier)
        {
            gearTags = items.ToImmutableArray();
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
                if (FindAllItems)
                {
                    if (!TryAddSubObjective(ref getMultipleItemsObjective, () => new AIObjectiveGetItems(character, objectiveManager, gearTags)
                    {
                        CheckInventory = CheckInventory,
                        Equip = Equip,
                        EvaluateCombatPriority = EvaluateCombatPriority,
                        RequireLoaded = true
                    },
                    onCompleted: () =>
                    {
                        if (KeepActiveWhenReady)
                        {
                            if (getMultipleItemsObjective != null)
                            {
                                foreach (var item in getMultipleItemsObjective.achievedItems)
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
                }
                else
                {
                    if (!TryAddSubObjective(ref getSingleItemObjective, () => new AIObjectiveGetItem(character, gearTags, objectiveManager, equip: Equip, checkInventory: CheckInventory)
                    {
                        EvaluateCombatPriority = EvaluateCombatPriority,
                        SpeakIfFails = true,
                        RequireLoaded = true
                    },
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
            RemoveSubObjective(ref getMultipleItemsObjective);
            RemoveSubObjective(ref getSingleItemObjective);
        }
    }
}
