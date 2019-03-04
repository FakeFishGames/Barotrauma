using System.Collections.Generic;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    class AIObjectiveRepairItems : AIMultiObjective<Item>
    {
        public override string DebugTag => "repair items";

        /// <summary>
        /// Should the character only attempt to fix items they have the skills to fix, or any damaged item
        /// </summary>
        public bool RequireAdequateSkills;

        public AIObjectiveRepairItems(Character character) : base(character, "") { }

        // TODO: This can allow two active repair items objectives, if RequireAdequateSkills is not at the same value. We don't want that.
        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectiveRepairItems repairItems && repairItems.RequireAdequateSkills == RequireAdequateSkills;

        protected override void FindTargets()
        {
            foreach (Item item in Item.ItemList)
            {
                if (ignoreList.Contains(item)) { continue; }
                if (item.IsFullCondition) { continue; }
                foreach (Submarine sub in Submarine.Loaded)
                {
                    if (sub.TeamID != character.TeamID) { continue; }
                    // If the character is inside, only take connected hulls into account.
                    if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(item, true)) { continue; }
                    foreach (Repairable repairable in item.Repairables)
                    {
                        if (item.Condition > repairable.ShowRepairUIThreshold) { continue; }
                        if (RequireAdequateSkills && !repairable.HasRequiredSkills(character)) { continue; }
                        if (!targets.Contains(item))
                        {
                            targets.Add(item);
                        }
                    }
                }
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
                        objective = new AIObjectiveRepairItem(character, item);
                        objectives.Add(item, objective);
                        AddSubObjective(objective);
                    }
                    break;
                }
            }
        }

        protected override float Average(Item item) => 100 - item.ConditionPercentage;

        protected override IEnumerable<Item> GetList() => Item.ItemList;
    }
}
