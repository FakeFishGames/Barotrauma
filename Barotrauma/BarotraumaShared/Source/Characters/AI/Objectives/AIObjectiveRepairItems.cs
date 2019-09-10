using System.Collections.Generic;
using System.Linq;
using Barotrauma.Items.Components;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveRepairItems : AIObjectiveLoop<Item>
    {
        public override string DebugTag => "repair items";

        /// <summary>
        /// Should the character only attempt to fix items they have the skills to fix, or any damaged item
        /// </summary>
        public bool RequireAdequateSkills;

        public override bool AllowMultipleInstances => true;

        public override bool IsDuplicate<T>(T otherObjective) => otherObjective is AIObjectiveRepairItems repairObjective && repairObjective.RequireAdequateSkills == RequireAdequateSkills;

        public AIObjectiveRepairItems(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier) { }

        protected override void CreateObjectives()
        {
            foreach (var item in Targets)
            {
                foreach (Repairable repairable in item.Repairables)
                {
                    if (!Objectives.TryGetValue(item, out AIObjective objective))
                    {
                        objective = ObjectiveConstructor(item);
                        Objectives.Add(item, objective);
                        AddSubObjective(objective);
                    }
                    break;
                }
            }
        }

        protected override bool Filter(Item item)
        {
            if (!IsValidTarget(item, character)) { return false; }
            if (item.CurrentHull.FireSources.Count > 0) { return false; }
            // Don't repair items in rooms that have enemies inside.
            if (Character.CharacterList.Any(c => c.CurrentHull == item.CurrentHull && !HumanAIController.IsFriendly(c))) { return false; }
            if (!Objectives.ContainsKey(item))
            {
                if (item.Repairables.All(r => item.ConditionPercentage > r.ShowRepairUIThreshold)) { return false; }
            }
            if (RequireAdequateSkills)
            {
                if (item.Repairables.Any(r => !r.HasRequiredSkills(character))) { return false; }
            }
            return true;
        }

        protected override float TargetEvaluation() => Targets.Max(t => 100 - t.ConditionPercentage);
        protected override IEnumerable<Item> GetList() => Item.ItemList;

        protected override AIObjective ObjectiveConstructor(Item item) 
            => new AIObjectiveRepairItem(character, item, objectiveManager, PriorityModifier);

        protected override void OnObjectiveCompleted(AIObjective objective, Item target)
            => HumanAIController.RemoveTargets<AIObjectiveRepairItems, Item>(character, target);

        public static bool IsValidTarget(Item item, Character character)
        {
            if (item == null) { return false; }
            if (item.IsFullCondition) { return false; }
            if (item.CurrentHull == null) { return false; }
            if (item.Submarine == null) { return false; }
            if (item.Submarine.TeamID != character.TeamID) { return false; }
            if (item.Repairables.None()) { return false; }
            if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(item, true)) { return false; }
            return true;
        }
    }
}
