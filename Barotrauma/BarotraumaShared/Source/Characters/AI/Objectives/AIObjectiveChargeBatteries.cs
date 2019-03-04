using Barotrauma.Items.Components;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveChargeBatteries : AIMultiObjective<PowerContainer>
    {
        public override string DebugTag => "charge batteries";
        private string orderOption;

        public AIObjectiveChargeBatteries(Character character, string option) : base(character, option)
        {
            orderOption = option;
        }

        // TODO: currently there can be multiple objectives with different order options. We don't want that
        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectiveChargeBatteries other && other.orderOption == orderOption;
        }

        protected override void FindTargets()
        {
            targets.Clear();
            foreach (Item item in Item.ItemList)
            {
                if (item.Prefab.Identifier != "battery" && !item.HasTag("battery")) { continue; }
                foreach (Submarine sub in Submarine.Loaded)
                {
                    if (sub.TeamID != character.TeamID) { continue; }
                    // If the character is inside, only take items in connected hulls into account.
                    if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(item, true)) { continue; }
                    var battery = item.GetComponent<PowerContainer>();
                    if (ignoreList.Contains(battery)) { continue; }
                    if (!targets.Contains(battery))
                    {
                        targets.Add(battery);
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

        protected override void CreateObjectives()
        {
            foreach (var battery in targets)
            {
                if (!objectives.TryGetValue(battery, out AIObjective objective))
                {
                    objective = new AIObjectiveOperateItem(battery, character, orderOption, false);
                    objectives.Add(battery, objective);
                    AddSubObjective(objective);
                }
            }
        }

        protected override float Average(PowerContainer battery) => 100 - battery.ChargePercentage;
    }
}
