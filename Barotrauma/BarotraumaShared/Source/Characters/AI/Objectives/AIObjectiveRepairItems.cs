using Barotrauma.Items.Components;
using System.Linq;
using System.Collections.Generic;

namespace Barotrauma
{
    class AIObjectiveRepairItems : AIObjective
    {
        public override string DebugTag => "repair items";

        private Dictionary<Item, AIObjectiveRepairItem> repairObjectives = new Dictionary<Item, AIObjectiveRepairItem>();

        /// <summary>
        /// Should the character only attempt to fix items they have the skills to fix, or any damaged item
        /// </summary>
        public bool RequireAdequateSkills;

        public AIObjectiveRepairItems(Character character) : base(character, "") { }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            GetBrokenItems();
            if (subObjectives.Count > 0 && objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority;
            }
            return 1.0f;
        }

        public override bool IsCompleted() => false;

        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectiveRepairItems repairItems && repairItems.RequireAdequateSkills == RequireAdequateSkills;

        protected override void Act(float deltaTime) => GetBrokenItems();

        private void GetBrokenItems()
        {
            foreach (Item item in Item.ItemList)
            {
                // Clear completed/impossible objectives.
                if (repairObjectives.TryGetValue(item, out AIObjectiveRepairItem objective))
                {
                    if (!subObjectives.Contains(objective))
                    {
                        repairObjectives.Remove(objective.Item);
                    }
                }
                if (!item.IsFullCondition)
                {
                    foreach (Repairable repairable in item.Repairables)
                    {
                        if (item.Condition > repairable.ShowRepairUIThreshold) { continue; }
                        if (RequireAdequateSkills && !repairable.HasRequiredSkills(character)) { continue; }
                        if (objective == null)
                        {
                            objective = new AIObjectiveRepairItem(character, item);
                            repairObjectives.Add(item, objective);
                            AddSubObjective(objective);
                        }
                        break;
                    }
                }
            }
        }
    }
}
