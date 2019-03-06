using Barotrauma.Items.Components;
using Barotrauma.Extensions;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveChargeBatteries : AIObjectiveLoop<PowerContainer>
    {
        public override string DebugTag => "charge batteries";
        private readonly IEnumerable<PowerContainer> batteryList;

        public AIObjectiveChargeBatteries(Character character, string option) : base(character, option)
        {
            batteryList = Item.ItemList.Select(i => i.GetComponent<PowerContainer>()).Where(b => b != null);
        }

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectiveChargeBatteries other && other.Option == Option;
        }

        protected override void FindTargets()
        {
            foreach (Item item in Item.ItemList)
            {
                if (item.Prefab.Identifier != "battery" && !item.HasTag("battery")) { continue; }
                if (item.Submarine == null) { continue; }
                if (item.Submarine.TeamID != character.TeamID) { continue; }
                if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(item, true)) { continue; }
                var battery = item.GetComponent<PowerContainer>();
                if (battery != null)
                {
                    if (!ignoreList.Contains(battery))
                    {
                        if (!targets.Contains(battery))
                        {
                            targets.Add(battery);
                        }
                    }
                }
            }
            if (targets.None())
            {
                character.Speak(TextManager.Get("DialogNoBatteries"), null, 4.0f, "nobatteries", 10.0f);
            }
            else
            {
                targets.Sort((x, y) => x.ChargePercentage.CompareTo(y.ChargePercentage));
            }
        }

        protected override bool Filter(PowerContainer battery) => true;
        protected override float Average(PowerContainer battery) => 100 - battery.ChargePercentage;
        protected override IEnumerable<PowerContainer> GetList() => batteryList;
        protected override AIObjective ObjectiveConstructor(PowerContainer battery) => new AIObjectiveOperateItem(battery, character, Option, false);
    }
}
