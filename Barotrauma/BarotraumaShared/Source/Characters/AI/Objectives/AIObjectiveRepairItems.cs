using System.Collections.Generic;
using System.Linq;
using Barotrauma.Items.Components;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveRepairItems : AIObjectiveLoop<Item>
    {
        public override string DebugTag => "repair items";
        public override bool KeepDivingGearOn => true;

        /// <summary>
        /// Should the character only attempt to fix items they have the skills to fix, or any damaged item
        /// </summary>
        public bool RequireAdequateSkills;

        public AIObjectiveRepairItems(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier) { }

        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectiveRepairItems repairItems && repairItems.RequireAdequateSkills == RequireAdequateSkills;

        protected override void FindTargets()
        {
            base.FindTargets();
            if (targets.None() && objectiveManager.CurrentOrder == this)
            {
                character.Speak(TextManager.Get("DialogNoRepairTargets"), null, 3.0f, "norepairtargets", 30.0f);
            }
        }

        protected override void CreateObjectives()
        {
            foreach (var item in targets)
            {
                foreach (Repairable repairable in item.Repairables)
                {
                    if (!objectives.TryGetValue(item, out AIObjective objective))
                    {
                        objective = ObjectiveConstructor(item);
                        objectives.Add(item, objective);
                        AddSubObjective(objective);
                    }
                    break;
                }
            }
        }

        protected override bool Filter(Item item)
        {
            if (item == null) { return false; }
            if (item.IsFullCondition) { return false; }
            if (item.CurrentHull == null) { return false; }
            if (item.Submarine == null) { return false; }
            if (item.Submarine.TeamID != character.TeamID) { return false; }
            if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(item, true)) { return false; }
            if (item.Repairables.None()) { return false; }
            if (item.CurrentHull.FireSources.Count > 0 || Character.CharacterList.Any(c => c.CurrentHull == item.CurrentHull && !HumanAIController.IsFriendly(c))) { return false; }
            foreach (Repairable repairable in item.Repairables)
            {
                if (!objectives.ContainsKey(item) && item.Condition > repairable.ShowRepairUIThreshold)
                {
                    return false;
                }
                else if (RequireAdequateSkills && !repairable.HasRequiredSkills(character))
                {
                    return false;
                }
            }
            return true;
        }

        protected override float TargetEvaluation() => targets.Max(t => 100 - t.ConditionPercentage);
        protected override IEnumerable<Item> GetList() => Item.ItemList;
        protected override AIObjective ObjectiveConstructor(Item item) => new AIObjectiveRepairItem(character, item, objectiveManager, PriorityModifier);
    }
}
