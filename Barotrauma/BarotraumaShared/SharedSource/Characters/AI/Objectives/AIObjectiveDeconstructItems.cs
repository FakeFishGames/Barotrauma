using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using System.Collections.Generic;

namespace Barotrauma
{
    class AIObjectiveDeconstructItems : AIObjectiveLoop<Item>
    {
        public override Identifier Identifier { get; set; } = "deconstruct items".ToIdentifier();

        //Clear periodically, because we may ending up ignoring items when all deconstructors are full
        protected override float IgnoreListClearInterval => 30;

        protected override bool AllowInFriendlySubs => true;

        protected override int MaxTargets => 10;

        private bool checkedDeconstructorExists;

        public AIObjectiveDeconstructItems(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1)
            : base(character, objectiveManager, priorityModifier)
        {
        }

        public override void OnSelected()
        {
            base.OnSelected();
            if (!checkedDeconstructorExists)
            {
                if (character.Submarine == null ||
                    Item.ItemList.None(it => 
                        it.GetComponent<Deconstructor>() != null && 
                        it.IsInteractable(character) && 
                        character.Submarine.IsEntityFoundOnThisSub(it, includingConnectedSubs: true, allowDifferentTeam: true, allowDifferentType: true)))
                {
                    character.Speak(TextManager.Get("orderdialogself.deconstructitem.nodeconstructor").Value, delay: 5.0f, 
                        identifier: "nodeconstructor".ToIdentifier(), minDurationBetweenSimilar: 30.0f);
                    Abandon = true;
                }
                checkedDeconstructorExists = true;
            }
        }

        public override void Reset()
        {
            base.Reset();
            checkedDeconstructorExists = false;
        }

        protected override float GetTargetPriority()
        {
            if (Targets.None()) { return 0; }
            if (objectiveManager.IsOrder(this))
            {
                return objectiveManager.GetOrderPriority(this);
            }
            return AIObjectiveManager.RunPriority - 0.5f;
        }

        protected override bool IsValidTarget(Item target)
        {
            // If the target was selected as a valid target, we'll have to accept it so that the objective can be completed.
            // The validity changes when a character picks the item up.
            if (!IsValidTarget(target, character, checkInventory: true)) 
            { 
                return Objectives.ContainsKey(target) && AIObjectiveCleanupItems.IsItemInsideValidSubmarine(target, character); 
            }
            if (target.CurrentHull != null && target.CurrentHull.FireSources.Count > 0) { return false; }

            foreach (Character c in Character.CharacterList)
            {
                if (c == character || !HumanAIController.IsActive(c)) { continue; }
                if (c.CurrentHull == target.CurrentHull && !HumanAIController.IsFriendly(c))
                {
                    // Don't deconstruct items in rooms that have enemies inside.
                    return false;
                }
                else if (c.TeamID == character.TeamID && c.AIController is HumanAIController humanAi)
                {
                    if (humanAi.ObjectiveManager.CurrentObjective is AIObjectiveDeconstructItem deconstruct && deconstruct.Item == target)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        protected override IEnumerable<Item> GetList() => Item.DeconstructItems;

        protected override AIObjective ObjectiveConstructor(Item item)
            => new AIObjectiveDeconstructItem(item, character, objectiveManager, priorityModifier: PriorityModifier);

        protected override void OnObjectiveCompleted(AIObjective objective, Item target)
            => HumanAIController.RemoveTargets<AIObjectiveDeconstructItems, Item>(character, target);

        private static bool IsValidTarget(Item item, Character character, bool checkInventory)
        {
            if (item == null) { return false; }
            if (item.GetRootInventoryOwner() == character) { return true; }
            return AIObjectiveCleanupItems.IsValidTarget(
                item, 
                character, 
                checkInventory, 
                allowUnloading: true, 
                requireValidContainer: false, 
                ignoreItemsMarkedForDeconstruction: false);
        }

        public override void OnDeselected()
        {
            base.OnDeselected();
            foreach (var subObjective in SubObjectives)
            {
                if (subObjective is AIObjectiveDeconstructItem deconstructObjective)
                {
                    deconstructObjective.DropTarget();
                }
            }
        }
    }
}
