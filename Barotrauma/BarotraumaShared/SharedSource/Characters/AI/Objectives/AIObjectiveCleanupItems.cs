using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveCleanupItems : AIObjectiveLoop<Item>
    {
        public override Identifier Identifier { get; set; } = "cleanup items".ToIdentifier();
        public override bool KeepDivingGearOn => true;
        public override bool AllowAutomaticItemUnequipping => false;
        protected override bool ForceOrderPriority => false;

        public readonly List<Item> prioritizedItems = new List<Item>();

        public static readonly Identifier AllowCleanupTag = "allowcleanup".ToIdentifier();

        protected override int MaxTargets => 100;

        public AIObjectiveCleanupItems(Character character, AIObjectiveManager objectiveManager, Item prioritizedItem = null, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier)
        {
            if (prioritizedItem != null)
            {
                prioritizedItems.Add(prioritizedItem);
            }
        }

        public AIObjectiveCleanupItems(Character character, AIObjectiveManager objectiveManager, IEnumerable<Item> prioritizedItems, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier)
        {
            this.prioritizedItems.AddRange(prioritizedItems.Where(i => i != null));
        }

        protected override float TargetEvaluation()
        {
            if (Targets.None()) { return 0; }
            if (objectiveManager.IsOrder(this))
            {
                float prio = objectiveManager.GetOrderPriority(this);
                if (subObjectives.All(so => so.SubObjectives.None()))
                {
                    // If none of the subobjectives have subobjectives, no valid container was found. Don't allow running.
                    ForceWalk = true;
                }
                return prio;
            }
            return AIObjectiveManager.RunPriority - 0.5f;
        }

        protected override bool Filter(Item target)
        {
            System.Diagnostics.Debug.Assert(target.GetComponent<Pickable>() is { } pickable && !pickable.IsAttached, "Invalid target in AIObjectiveCleanUpItems - the the objective should only be checking pickable, non-attached items.");
            System.Diagnostics.Debug.Assert(target.Prefab.PreferredContainers.Any(), "Invalid target in AIObjectiveCleanUpItems - the the objective should only be checking items that have preferred containers defined.");

            // If the target was selected as a valid target, we'll have to accept it so that the objective can be completed.
            // The validity changes when a character picks the item up.
            if (!IsValidTarget(target, character, checkInventory: true)) { return Objectives.ContainsKey(target) && IsItemInsideValidSubmarine(target, character); }
            if (target.CurrentHull.FireSources.Count > 0) { return false; }
            // Don't clean up items in rooms that have enemies inside.
            if (Character.CharacterList.Any(c => c.CurrentHull == target.CurrentHull && !HumanAIController.IsFriendly(c) && HumanAIController.IsActive(c))) { return false; }
            return true;
        }

        protected override IEnumerable<Item> GetList() => Item.CleanableItems;

        protected override AIObjective ObjectiveConstructor(Item item)
            => new AIObjectiveCleanupItem(item, character, objectiveManager, priorityModifier: PriorityModifier)
            {
                IsPriority = prioritizedItems.Contains(item)
            };

        protected override void OnObjectiveCompleted(AIObjective objective, Item target)
            => HumanAIController.RemoveTargets<AIObjectiveCleanupItems, Item>(character, target);

        public static bool IsItemInsideValidSubmarine(Item item, Character character)
        {
            if (item.CurrentHull == null) { return false; }
            if (item.Submarine == null) { return false; }
            if (item.Submarine.TeamID != character.TeamID) { return false; }
            if (character.Submarine != null && !character.Submarine.IsConnectedTo(item.Submarine)) { return false; }
            return true;
        }

        public static bool IsValidContainer(Item container, Character character, bool allowUnloading = true) =>
            allowUnloading &&
            container.HasTag(AllowCleanupTag) && 
            container.HasAccess(character) && 
            container.ParentInventory == null && container.OwnInventory != null && container.OwnInventory.AllItems.Any() && 
            container.GetComponent<ItemContainer>() != null &&
            IsItemInsideValidSubmarine(container, character) &&
            !container.IsClaimedByBallastFlora;

        public static bool IsValidTarget(Item item, Character character, bool checkInventory, bool allowUnloading = true)
        {
            if (item == null) { return false; }
            if ((item.SpawnedInCurrentOutpost && !item.AllowStealing) == character.IsOnPlayerTeam) { return false; }
            if (item.ParentInventory != null)
            {
                if (item.Container == null)
                {
                    // In a character inventory
                    return false;
                }
                if (!IsValidContainer(item.Container, character, allowUnloading)) { return false; }
            }
            if (!item.HasAccess(character)) { return false; }
            if (character != null && !IsItemInsideValidSubmarine(item, character)) { return false; }
            if (item.HasBallastFloraInHull) { return false; }
            var wire = item.GetComponent<Wire>();
            if (wire != null)
            {
                if (wire.Connections.Any()) { return false; }
            }
            else
            {
                var connectionPanel = item.GetComponent<ConnectionPanel>();
                if (connectionPanel != null && connectionPanel.Connections.Any(c => c.Wires.Count > 0))
                {
                    return false;
                }
            }
            if (!checkInventory)
            {
                return true;
            }
            return CanEquip(character, item, allowWearing: false);
        }

        public override void OnDeselected()
        {
            base.OnDeselected();
            foreach (var subObjective in SubObjectives)
            {
                if (subObjective is AIObjectiveCleanupItem cleanUpObjective)
                {
                    cleanUpObjective.DropTarget();
                }
            }
        }
    }
}
