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
            bool ignore = item.IsFullCondition;
            if (!ignore)
            {
                if (item.Submarine == null) { ignore = true; }
                else if (item.Submarine.TeamID != character.TeamID) { ignore = true; }
                else if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(item, true)) { ignore = true; }
                else
                {
                    if (item.Repairables.None()) { ignore = true; }
                    else if (item.CurrentHull != null || item.CurrentHull.FireSources.Count > 0 || Character.CharacterList.Any(c => c.CurrentHull == item.CurrentHull && !HumanAIController.IsFriendly(c))) { ignore = true; }
                    else
                    {
                        foreach (Repairable repairable in item.Repairables)
                        {
                            if (!objectives.ContainsKey(item) && item.Condition > repairable.ShowRepairUIThreshold)
                            {
                                ignore = true;
                            }
                            else if (RequireAdequateSkills && !repairable.HasRequiredSkills(character))
                            {
                                ignore = true;
                            }
                            if (ignore) { break; }
                        }
                    }
                }
            }
            return !ignore;
        }

        protected override float TargetEvaluation() => targets.Max(t => 100 - t.ConditionPercentage);
        protected override IEnumerable<Item> GetList() => Item.ItemList;
        protected override AIObjective ObjectiveConstructor(Item item) => new AIObjectiveRepairItem(character, item, objectiveManager, PriorityModifier);
    }
}
