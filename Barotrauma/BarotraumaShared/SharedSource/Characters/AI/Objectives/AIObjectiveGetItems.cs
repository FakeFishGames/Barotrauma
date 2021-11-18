#nullable enable
using Barotrauma.Extensions;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma
{
    class AIObjectiveGetItems : AIObjective
    {
        public override string Identifier { get; set; } = "get items";
        public override string DebugTag => $"{Identifier}";
        public override bool KeepDivingGearOn => true;

        public bool AllowStealing { get; set; }
        public bool TakeWholeStack { get; set; }
        public bool AllowVariants { get; set; }
        public bool Equip { get; set; }
        public bool Wear { get; set; }
        public bool CheckInventory { get; set; }
        public bool EvaluateCombatPriority { get; set; }
        public bool CheckPathForEachItem { get; set; }
        public bool RequireLoaded { get; set; }

        private readonly ImmutableArray<string> gearTags;
        private readonly string[] ignoredTags;
        private bool subObjectivesCreated;

        public readonly HashSet<Item> achievedItems = new HashSet<Item>();

        public AIObjectiveGetItems(Character character, AIObjectiveManager objectiveManager, IEnumerable<string> identifiersOrTags, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier)
        {
            gearTags = AIObjectiveGetItem.ParseGearTags(identifiersOrTags).ToImmutableArray();
            ignoredTags = AIObjectiveGetItem.ParseIgnoredTags(identifiersOrTags).ToArray();
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
                foreach (string tag in gearTags)
                {
                    AIObjectiveGetItem? getItem = null;
                    TryAddSubObjective(ref getItem, () => 
                        new AIObjectiveGetItem(character, tag, objectiveManager, Equip, CheckInventory)
                        {
                            AllowVariants = AllowVariants,
                            Wear = Wear,
                            TakeWholeStack = TakeWholeStack,
                            AllowStealing = AllowStealing,
                            ignoredIdentifiersOrTags = ignoredTags,
                            CheckPathForEachItem = CheckPathForEachItem,
                            RequireLoaded = RequireLoaded
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
