using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveCleanupItems : AIObjectiveLoop<Item>
    {
        public override string DebugTag => "cleanup items";
        public override bool KeepDivingGearOn => true;
        public override bool AllowAutomaticItemUnequipping => false;
        public override bool ForceOrderPriority => false;

        public readonly Item prioritizedItem;

        public AIObjectiveCleanupItems(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1, Item prioritizedItem = null)
            : base(character, objectiveManager, priorityModifier)
        {
            this.prioritizedItem = prioritizedItem;
        }

        protected override float TargetEvaluation() => Targets.Any() ? AIObjectiveManager.RunPriority - 1 : 0;

        protected override bool Filter(Item target)
        {
            // If the target was selected as a valid target, we'll have to accept it so that the objective can be completed.
            // The validity changes when a character picks the item up.
            if (!IsValidTarget(target, character)) { return Objectives.ContainsKey(target) && IsItemInsideValidSubmarine(target, character); }
            if (target.CurrentHull.FireSources.Count > 0) { return false; }
            // Don't repair items in rooms that have enemies inside.
            if (Character.CharacterList.Any(c => c.CurrentHull == target.CurrentHull && !HumanAIController.IsFriendly(c) && HumanAIController.IsActive(c))) { return false; }
            return true;
        }

        protected override IEnumerable<Item> GetList() => Item.ItemList;

        protected override AIObjective ObjectiveConstructor(Item item)
            => new AIObjectiveCleanupItem(item, character, objectiveManager, priorityModifier: PriorityModifier)
            {
                IsPriority = prioritizedItem == item
            };

        protected override void OnObjectiveCompleted(AIObjective objective, Item target)
            => HumanAIController.RemoveTargets<AIObjectiveCleanupItems, Item>(character, target);

        private static bool IsItemInsideValidSubmarine(Item item, Character character)
        {
            if (item.CurrentHull == null) { return false; }
            if (item.Submarine == null) { return false; }
            if (item.Submarine.TeamID != character.TeamID) { return false; }
            if (character.Submarine != null)
            {
                if (!character.Submarine.IsConnectedTo(item.Submarine)) { return false; }
            }
            return true;
        }

        public static bool IsValidTarget(Item item, Character character)
        {
            if (item == null) { return false; }
            if (item.NonInteractable) { return false; }
            if (item.ParentInventory != null) { return false; }
            if (character != null && !IsItemInsideValidSubmarine(item, character)) { return false; }
            //var rootContainer = item.GetRootContainer();
            //// Only target items lying on the ground (= not inside a container) (do we need this check?)
            //if (rootContainer != null) { return false; }
            var pickable = item.GetComponent<Pickable>();
            if (pickable == null) { return false; }
            if (pickable is Holdable h && h.Attachable && h.Attached) { return false; }
            var wire = item.GetComponent<Wire>();
            if (wire != null)
            {
                if (wire.Connections.Any()) { return false; }
            }
            else
            {
                var connectionPanel = item.GetComponent<ConnectionPanel>();
                if (connectionPanel != null && connectionPanel.Connections.Any(c => c.Wires.Any(w => w != null)))
                {
                    return false;
                }
            }
            return item.Prefab.PreferredContainers.Any();
        }
    }
}
