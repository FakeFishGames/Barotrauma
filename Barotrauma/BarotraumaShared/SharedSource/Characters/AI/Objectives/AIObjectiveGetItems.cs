#nullable enable
using Barotrauma.Extensions;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma
{
    class AIObjectiveGetItems : AIObjective
    {
        public override Identifier Identifier { get; set; } = "get items".ToIdentifier();
        public override string DebugTag => $"{Identifier}";
        public override bool KeepDivingGearOn => true;
        public override bool AllowMultipleInstances => true;

        public bool AllowStealing { get; set; }
        public bool TakeWholeStack { get; set; }
        public bool AllowVariants { get; set; }
        public bool Equip { get; set; }
        public bool Wear { get; set; }
        public bool CheckInventory { get; set; }
        public bool EvaluateCombatPriority { get; set; }
        public bool CheckPathForEachItem { get; set; }
        public bool RequireLoaded { get; set; }
        public bool RequireAllItems { get; set; }

        private readonly ImmutableArray<Identifier> gearTags;
        private readonly ImmutableHashSet<Identifier> ignoredTags;
        private bool subObjectivesCreated;

        public readonly HashSet<Item> achievedItems = new HashSet<Item>();

        public AIObjectiveGetItems(Character character, AIObjectiveManager objectiveManager, IEnumerable<Identifier> identifiersOrTags, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier)
        {
            gearTags = AIObjectiveGetItem.ParseGearTags(identifiersOrTags).ToImmutableArray();
            ignoredTags = AIObjectiveGetItem.ParseIgnoredTags(identifiersOrTags).ToImmutableHashSet();
        }

        protected override bool CheckObjectiveSpecific() => subObjectivesCreated && subObjectives.None();

        protected override void Act(float deltaTime)
        {
            if (character.LockHands)
            {
                Abandon = true;
                return;
            }
            if (!subObjectivesCreated)
            {
                foreach (Identifier tag in gearTags)
                {
                    if (subObjectives.Any(so => so is AIObjectiveGetItem getItem && getItem.IdentifiersOrTags.Contains(tag))) { continue; }
                    int count = gearTags.Count(t => t == tag);
                    AIObjectiveGetItem? getItem = null;
                    TryAddSubObjective(ref getItem, () =>
                        new AIObjectiveGetItem(character, tag, objectiveManager, Equip, CheckInventory && count <= 1)
                        {
                            AllowVariants = AllowVariants,
                            Wear = Wear,
                            TakeWholeStack = TakeWholeStack,
                            AllowStealing = AllowStealing,
                            ignoredIdentifiersOrTags = ignoredTags,
                            CheckPathForEachItem = CheckPathForEachItem,
                            RequireLoaded = RequireLoaded,
                            ItemCount = count,
                            SpeakIfFails = RequireAllItems
                        },
                        onCompleted: () =>
                        {
                            var item = getItem?.TargetItem;
                            if (item?.IsOwnedBy(character) != null)
                            {
                                achievedItems.Add(item);
                            }
                        },
                        onAbandon: () =>
                        {
                            var item = getItem?.TargetItem;
                            if (item != null)
                            {
                                achievedItems.Remove(item);
                            }
                            RemoveSubObjective(ref getItem);
                            if (RequireAllItems)
                            {
                                Abandon = true;
                            }
                        });
                }
                subObjectivesCreated = true;
            }
        }

        public override void Reset()
        {
            base.Reset();
            subObjectivesCreated = false;
            achievedItems.Clear();
        }
    }
}
