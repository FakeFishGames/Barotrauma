using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveRepairItems : AIObjective
    {
        public override string DebugTag => "repair items";

        private Dictionary<Item, AIObjectiveRepairItem> repairObjectives = new Dictionary<Item, AIObjectiveRepairItem>();
        private HashSet<Item> ignoreList = new HashSet<Item>();
        private readonly float ignoreListClearIntervalBase = 30;
        private readonly float ignoreListIntervalAddition = 10;
        private float ignoreListClearInterval;
        private float ignoreListTimer;

        /// <summary>
        /// Should the character only attempt to fix items they have the skills to fix, or any damaged item
        /// </summary>
        public bool RequireAdequateSkills;

        public AIObjectiveRepairItems(Character character) : base(character, "")
        {
            ignoreListClearInterval = ignoreListClearIntervalBase;
        }

        public override void UpdatePriority(AIObjectiveManager objectiveManager, float deltaTime)
        {
            base.UpdatePriority(objectiveManager, deltaTime);
            if (ignoreListTimer > ignoreListClearInterval)
            {
                if (ignoreList.Any())
                {
                    // Increase the clear interval if there are items in the list -> reduces spam if items are added on the list over and over again.
                    ignoreListClearInterval += ignoreListIntervalAddition;
                }
                else
                {
                    // Else reset the interval
                    ignoreListClearInterval = ignoreListClearIntervalBase;
                }
                ignoreList.Clear();
                ignoreListTimer = 0;
                GetBrokenItems();
            }
            else
            {
                ignoreListTimer += deltaTime;
            }
            foreach (var repairObjective in repairObjectives)
            {
                if (!repairObjective.Value.CanBeCompleted)
                {
                    ignoreList.Add(repairObjective.Key);
                }
            }
            SyncRemovedObjectives(repairObjectives, Item.ItemList);
            if (repairObjectives.None())
            {
                GetBrokenItems();
            }
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (character.Submarine == null) { return 0; }
            if (repairObjectives.None()) { return 0; }
            float avg = repairObjectives.Average(ro => 100 - ro.Key.ConditionPercentage);
            if (objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority - MathHelper.Max(0, AIObjectiveManager.OrderPriority - avg);
            }
            return MathHelper.Lerp(0, 50, avg / 100);
        }

        public override bool IsCompleted() => false;
        public override bool CanBeCompleted => true;

        // TODO: This can allow two active repair items objectives, if RequireAdequateSkills is not at the same value. We don't want that.
        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectiveRepairItems repairItems && repairItems.RequireAdequateSkills == RequireAdequateSkills;

        protected override void Act(float deltaTime) { }

        private void GetBrokenItems()
        {
            foreach (Item item in Item.ItemList)
            {
                if (item.IsFullCondition || ignoreList.Contains(item)) { continue; }
                foreach (Submarine sub in Submarine.Loaded)
                {
                    if (sub.TeamID != character.TeamID) { continue; }
                    // If the character is inside, only take connected hulls into account.
                    if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(item, true)) { continue; }
                    foreach (Repairable repairable in item.Repairables)
                    {
                        if (item.Condition > repairable.ShowRepairUIThreshold) { continue; }
                        if (RequireAdequateSkills && !repairable.HasRequiredSkills(character)) { continue; }
                        if (!repairObjectives.TryGetValue(item, out AIObjectiveRepairItem objective))
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
