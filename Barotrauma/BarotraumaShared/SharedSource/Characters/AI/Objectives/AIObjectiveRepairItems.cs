using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Items.Components;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class AIObjectiveRepairItems : AIObjectiveLoop<Item>
    {
        public override Identifier Identifier { get; set; } = "repair items".ToIdentifier();

        /// <summary>
        /// If set, only fix items where required skill matches this.
        /// </summary>
        public Identifier RelevantSkill;

        public Item PrioritizedItem { get; private set; }

        public override bool AllowMultipleInstances => true;
        public override bool AllowInAnySub => true;

        public readonly static float RequiredSuccessFactor = 0.4f;

        public override bool IsDuplicate<T>(T otherObjective) => otherObjective is AIObjectiveRepairItems repairObjective && objectiveManager.IsOrder(repairObjective) == objectiveManager.IsOrder(this);

        public AIObjectiveRepairItems(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1, Item prioritizedItem = null)
            : base(character, objectiveManager, priorityModifier)
        {
            PrioritizedItem = prioritizedItem;
        }

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
                        if (!subObjectives.Contains(objective))
                        {
                            subObjectives.Add(objective);
                        }
                        objective.Completed += () =>
                        {
                            Objectives.Remove(item);
                            OnObjectiveCompleted(objective, item);
                        };
                        objective.Abandoned += () =>
                        {
                            Objectives.Remove(item);
                            ignoreList.Add(item);
                            targetUpdateTimer = Math.Min(0.1f, targetUpdateTimer);
                        };
                    }
                    break;
                }
            }
        }

        protected override bool Filter(Item item)
        {
            if (!ViableForRepair(item, character, HumanAIController)) { return false; };
            if (!Objectives.ContainsKey(item))
            {
                if (item != character.SelectedItem)
                {
                    if (NearlyFullCondition(item)) { return false; }
                }
            }
            if (!RelevantSkill.IsEmpty)
            {
                if (item.Repairables.None(r => r.requiredSkills.Any(s => s.Identifier == RelevantSkill))) { return false; }
            }
            return !HumanAIController.IsItemRepairedByAnother(item, out _);
        }

        public static bool ViableForRepair(Item item, Character character, HumanAIController humanAIController)
        {
            if (!IsValidTarget(item, character)) { return false; }
            if (item.CurrentHull == null) { return true; }
            if (item.CurrentHull.FireSources.Count > 0) { return false; }
            // Don't repair items in rooms that have enemies inside.
            if (Character.CharacterList.Any(c => c.CurrentHull == item.CurrentHull && !humanAIController.IsFriendly(c) && HumanAIController.IsActive(c))) { return false; }
            return true;
        }

        public static bool NearlyFullCondition(Item item)
        {
            return item.Repairables.All(r => !r.IsBelowRepairThreshold);
        }

        protected override float TargetEvaluation()
        {
            var selectedItem = character.SelectedItem;
            if (selectedItem != null && AIObjectiveRepairItem.IsRepairing(character, selectedItem) && selectedItem.ConditionPercentage < 100)
            {
                // Don't stop fixing until completely done
                return 100;
            }
            int otherFixers = HumanAIController.CountCrew(c => c != HumanAIController && c.ObjectiveManager.IsCurrentObjective<AIObjectiveRepairItems>() && !c.Character.IsIncapacitated, onlyBots: true);
            int items = Targets.Count;
            if (items == 0)
            {
                return 0;
            }
            bool anyFixers = otherFixers > 0;
            float ratio = anyFixers ? items / (float)otherFixers : 1;
            if (objectiveManager.IsOrder(this))
            {
                return Targets.Sum(t => 100 - t.ConditionPercentage);
            }
            else
            {
                if (anyFixers && (ratio <= 1 || otherFixers > 5 || otherFixers / (float)HumanAIController.CountCrew(onlyBots: true) > 0.75f))
                {
                    // Enough fixers
                    return 0;
                }
                return Targets.Sum(t => GetTargetPriority(t, character, RequiredSuccessFactor)) * ratio;
            }
        }

        public static float GetTargetPriority(Item item, Character character, float requiredSuccessFactor = 0)
        {
            float damagePriority = MathHelper.Lerp(1, 0, item.Condition / item.MaxCondition);
            float successFactor = MathHelper.Lerp(0, 1, item.Repairables.Average(r => r.DegreeOfSuccess(character)));
            if (successFactor < requiredSuccessFactor)
            {
                return 0;
            }
            return MathHelper.Lerp(0, 100, MathHelper.Clamp(damagePriority * successFactor, 0, 1));
        }

        protected override IEnumerable<Item> GetList() => Item.RepairableItems;

        protected override AIObjective ObjectiveConstructor(Item item) 
            => new AIObjectiveRepairItem(character, item, objectiveManager, priorityModifier: PriorityModifier, isPriority: item == PrioritizedItem);

        protected override void OnObjectiveCompleted(AIObjective objective, Item target)
            => HumanAIController.RemoveTargets<AIObjectiveRepairItems, Item>(character, target);

        public static bool IsValidTarget(Item item, Character character)
        {
            if (item == null) { return false; }
            if (item.IgnoreByAI(character)) { return false; }
            if (!item.IsInteractable(character)) { return false; }
            if (item.IsFullCondition) { return false; }
            if (item.Submarine == null || character.Submarine == null) { return false; }
            if (item.IsClaimedByBallastFlora) { return false; } 
            //player crew ignores items in outposts
            if (character.IsOnPlayerTeam && item.Submarine.Info.IsOutpost) { return false; }
            if (!character.Submarine.IsEntityFoundOnThisSub(item, includingConnectedSubs: true)) { return false; }
            if (item.Repairables.None()) { return false; }

            System.Diagnostics.Debug.Assert(item.Repairables.Any(), "Invalid target in AIObjectiveRepairItems - the objective should only be checking items that have a Repairable component (Item.RepairableItems)");

            return true;
        }
    }
}
