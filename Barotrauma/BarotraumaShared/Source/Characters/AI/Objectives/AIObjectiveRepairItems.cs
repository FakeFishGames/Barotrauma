using System.Collections.Generic;
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

        public AIObjectiveRepairItems(Character character) : base(character, "") { }

        // TODO: This can allow two active repair items objectives, if RequireAdequateSkills is not at the same value. We don't want that.
        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectiveRepairItems repairItems && repairItems.RequireAdequateSkills == RequireAdequateSkills;

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
            bool ignore = ignoreList.Contains(item) || item.IsFullCondition;
            if (!ignore)
            {
                if (item.Submarine == null) { ignore = true; }
                else if (item.Submarine.TeamID != character.TeamID) { ignore = true; }
                else if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(item, true)) { ignore = true; }
                else
                {
                    if (item.Repairables.None()) { ignore = true; }
                    else
                    {
                        foreach (Repairable repairable in item.Repairables)
                        {
                            if (item.Condition > repairable.ShowRepairUIThreshold) { ignore = true; }
                            else if (RequireAdequateSkills && !repairable.HasRequiredSkills(character)) { ignore = true; }
                            if (ignore) { break; }
                        }
                    }
                }
            }
            return ignore;
        }

        protected override float Average(Item item) => 100 - item.ConditionPercentage;
        protected override IEnumerable<Item> GetList() => Item.ItemList;
        protected override AIObjective ObjectiveConstructor(Item item) => new AIObjectiveRepairItem(character, item);
    }
}
