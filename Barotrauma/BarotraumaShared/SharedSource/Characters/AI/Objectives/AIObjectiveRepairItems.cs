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
        public override string DebugTag => "repair items";

        /// <summary>
        /// Should the character only attempt to fix items they have the skills to fix, or any damaged item
        /// </summary>
        public bool RequireAdequateSkills;

        /// <summary>
        /// If set, only fix items where required skill matches this.
        /// </summary>
        public string RelevantSkill;

        private readonly Item prioritizedItem;

        public override bool AllowMultipleInstances => true;

        public readonly static float RequiredSuccessFactor = 0.4f;

        public override bool IsDuplicate<T>(T otherObjective) => 
            (otherObjective as AIObjective) is AIObjectiveRepairItems repairObjective && repairObjective.RequireAdequateSkills == RequireAdequateSkills;

        public AIObjectiveRepairItems(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1, Item prioritizedItem = null)
            : base(character, objectiveManager, priorityModifier)
        {
            this.prioritizedItem = prioritizedItem;
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
            if (!IsValidTarget(item, character)) { return false; }
            if (item.CurrentHull.FireSources.Count > 0) { return false; }
            // Don't repair items in rooms that have enemies inside.
            if (Character.CharacterList.Any(c => c.CurrentHull == item.CurrentHull && !HumanAIController.IsFriendly(c) && HumanAIController.IsActive(c))) { return false; }
            if (!Objectives.ContainsKey(item))
            {
                if (item != character.SelectedConstruction)
                {
                    float condition = item.ConditionPercentage;
                    if (item.Repairables.All(r => condition >= r.RepairThreshold)) { return false; }
                }
            }
            if (!string.IsNullOrWhiteSpace(RelevantSkill))
            {
                if (item.Repairables.None(r => r.requiredSkills.Any(s => s.Identifier.Equals(RelevantSkill, StringComparison.OrdinalIgnoreCase)))) { return false; }
            }
            return true;
        }

        protected override float TargetEvaluation()
        {
            if (character.SelectedConstruction != null && Targets.Any(t => character.SelectedConstruction == t && t.ConditionPercentage < 100))
            {
                // Don't stop fixing until done
                return 100;
            }
            int otherFixers = HumanAIController.CountCrew(c => c != HumanAIController && c.ObjectiveManager.IsCurrentObjective<AIObjectiveRepairItems>() && !c.Character.IsIncapacitated, onlyBots: true);
            int items = Targets.Count;
            bool anyFixers = otherFixers > 0;
            float ratio = anyFixers ? items / (float)otherFixers : 1;
            if (objectiveManager.CurrentOrder == this)
            {
                return Targets.Sum(t => 100 - t.ConditionPercentage) * ratio;
            }
            else
            {
                if (anyFixers && (ratio <= 1 || otherFixers > 5 || otherFixers / (float)HumanAIController.CountCrew(onlyBots: true) > 0.75f))
                {
                    // Enough fixers
                    return 0;
                }
                if (RequireAdequateSkills)
                {
                    return Targets.Sum(t => GetTargetPriority(t, character, RequiredSuccessFactor)) * ratio;
                }
                else
                {
                    return Targets.Sum(t => 100 - t.ConditionPercentage) * ratio;
                }
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

        protected override IEnumerable<Item> GetList() => Item.ItemList;

        protected override AIObjective ObjectiveConstructor(Item item) 
            => new AIObjectiveRepairItem(character, item, objectiveManager, priorityModifier: PriorityModifier, isPriority: item == prioritizedItem);

        protected override void OnObjectiveCompleted(AIObjective objective, Item target)
            => HumanAIController.RemoveTargets<AIObjectiveRepairItems, Item>(character, target);

        public static bool IsValidTarget(Item item, Character character)
        {
            if (item == null) { return false; }
            if (item.NonInteractable) { return false; }
            if (item.IsFullCondition) { return false; }
            if (item.CurrentHull == null) { return false; }
            if (item.Submarine == null) { return false; }
            if (item.Submarine.TeamID != character.TeamID) { return false; }
            if (item.Repairables.None()) { return false; }
            if (character.Submarine != null)
            {
                if (item.Submarine.Info.Type != character.Submarine.Info.Type) { return false; }
                if (!character.Submarine.IsEntityFoundOnThisSub(item, true)) { return false; }
            }
            return true;
        }
    }
}
