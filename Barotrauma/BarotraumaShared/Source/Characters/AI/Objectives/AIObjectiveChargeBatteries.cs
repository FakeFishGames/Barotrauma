using Barotrauma.Items.Components;
using Barotrauma.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveChargeBatteries : AIObjectiveLoop<PowerContainer>
    {
        public override string DebugTag => "charge batteries";
        private IEnumerable<PowerContainer> batteryList;

        public AIObjectiveChargeBatteries(Character character, AIObjectiveManager objectiveManager, string option, float priorityModifier) 
            : base(character, objectiveManager, priorityModifier, option) { }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectiveChargeBatteries other && other.Option == Option;
        }

        protected override void FindTargets()
        {
            base.FindTargets();
            if (targets.None() && objectiveManager.CurrentOrder == this)
            {
                character.Speak(TextManager.Get("DialogNoBatteries"), null, 4.0f, "nobatteries", 10.0f);
            }
        }

        protected override bool Filter(PowerContainer battery)
        {
            var item = battery.Item;
            if (item.Submarine == null) { return false; }
            if (item.Submarine.TeamID != character.TeamID) { return false; }
            if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(item, true)) { return false; }
            return true;
        }

        protected override float TargetEvaluation() => targets.Max(t => 100 - t.ChargePercentage);
        protected override IEnumerable<PowerContainer> GetList()
        {
            if (batteryList == null)
            {
                batteryList = Item.ItemList.Select(i => i.GetComponent<PowerContainer>()).Where(b => b != null);
            }
            return batteryList;
        }

        protected override AIObjective ObjectiveConstructor(PowerContainer battery) 
            => new AIObjectiveOperateItem(battery, character, objectiveManager, Option, false, priorityModifier: PriorityModifier) { IsLoop = true };
    }
}
