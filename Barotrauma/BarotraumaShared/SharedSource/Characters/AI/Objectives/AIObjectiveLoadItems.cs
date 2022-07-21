using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveLoadItems : AIObjectiveLoop<Item>
    {
        public override Identifier Identifier { get; set; } = "load items".ToIdentifier();
        protected override float IgnoreListClearInterval => 20.0f;
        protected override bool ResetWhenClearingIgnoreList => false;

        private ImmutableArray<Identifier> TargetContainerTags { get; }
        private List<Item> TargetContainers { get; } = new List<Item>();
        private ItemCondition TargetCondition { get; }

        public enum ItemCondition
        {
            Empty,
            Full
        }

        public AIObjectiveLoadItems(Character character, AIObjectiveManager objectiveManager, Identifier option, ImmutableArray<Identifier> containerTags, Item targetContainer = null, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier, option)
        {
            if ((containerTags == null || containerTags.None()) && targetContainer == null)
            {
                Abandon = true;
                return;
            }
            else
            {
                TargetContainerTags = containerTags.ToImmutableArray();
            }
            if (targetContainer != null)
            {
                TargetContainers.Add(targetContainer);
            }
            else
            {
                foreach (Item item in Item.ItemList)
                {
                    if (!OrderPrefab.TargetItemsMatchItem(TargetContainerTags, item)) { continue; }
                    TargetContainers.Add(item);
                }
            }
            TargetCondition = option == "turretammo" ? ItemCondition.Empty : ItemCondition.Full;
        }

        protected override bool Filter(Item target)
        {
            //don't pass TargetContainerTags to the method (no need to filter by tags anymore, it's already done when populating TargetContainers)
            if (!IsValidTarget(target, character, null, TargetCondition)) { return false; }
            if (target.CurrentHull == null || target.CurrentHull.FireSources.Count > 0) { return false; }
            if (Character.CharacterList.Any(c => c.CurrentHull == target.CurrentHull && !HumanAIController.IsFriendly(c) && HumanAIController.IsActive(c))) { return false; }
            return true;
        }

        public static bool IsValidTarget(Item item, Character character, ImmutableArray<Identifier>? targetContainerTags = null, ItemCondition? targetCondition = null)
        {
            if (item == null || item.Removed) { return false; }
            if (targetContainerTags.HasValue && !OrderPrefab.TargetItemsMatchItem(targetContainerTags.Value, item)) { return false; }
            if (!(item.GetComponent<ItemContainer>() is ItemContainer container)) { return false; }
            if (container.Inventory == null) { return false; }
            if (targetCondition.HasValue && container.Inventory.IsFull() && container.Inventory.AllItems.None(i => ItemMatchesTargetCondition(i, targetCondition.Value))) { return false; }
            if (!AIObjectiveCleanupItems.IsItemInsideValidSubmarine(item, character)) { return false; }
            if (item.GetRootInventoryOwner() is Character owner && owner != character) { return false; }
            if (item.IsClaimedByBallastFlora) { return false; }
            if (!item.HasAccess(character)) { return false; }
            // Ignore items that require power but don't have it
            if (item.GetComponent<Powered>() is Powered powered && powered.PowerConsumption > 0 && powered.Voltage < powered.MinVoltage) { return false; }
            return true;
        }

        public static bool ItemMatchesTargetCondition(Item item, ItemCondition targetCondition)
        {
            if(item == null) { return false; }
            try
            {
                return targetCondition switch
                {
                    ItemCondition.Empty => item.Condition <= 0.1f,
                    ItemCondition.Full => item.IsFullCondition,
                    _ => throw new NotImplementedException(),
                };
            }
            catch (NotImplementedException)
            {
#if DEBUG
                DebugConsole.ShowError($"Unexpected target condition \"{targetCondition}\" in AIObjectiveLoadItems.ItemMatchesTargetCondition");
#endif
                return false;
            }
        }

        protected override IEnumerable<Item> GetList() => TargetContainers;

        protected override AIObjective ObjectiveConstructor(Item target)
            => new AIObjectiveLoadItem(target, TargetContainerTags, TargetCondition, Option, character, objectiveManager, PriorityModifier);

        protected override void OnObjectiveCompleted(AIObjective objective, Item target)
            => HumanAIController.RemoveTargets<AIObjectiveLoadItems, Item>(character, target);

        protected override float TargetEvaluation()
        {
            if (Targets.None()) { return 0; }
            if (objectiveManager.IsOrder(this))
            {
                float prio = objectiveManager.GetOrderPriority(this);
                if (subObjectives.All(so => so.SubObjectives.None() || so.Priority <= 0))
                {
                    ForceWalk = true;
                }
                return prio;
            }
            return AIObjectiveManager.RunPriority - 0.5f;
        }
    }
}