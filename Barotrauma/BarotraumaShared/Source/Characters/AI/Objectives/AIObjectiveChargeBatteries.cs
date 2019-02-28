using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class AIObjectiveChargeBatteries : AIObjective
    {
        public override string DebugTag => "charge batteries";

        private List<PowerContainer> targets = new List<PowerContainer>();
        private Dictionary<PowerContainer, AIObjectiveOperateItem> chargeObjectives = new Dictionary<PowerContainer, AIObjectiveOperateItem>();
        private string orderOption;
        
        public AIObjectiveChargeBatteries(Character character, string option) : base(character, option)
        {
            orderOption = option;
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (targets.None()) { GetTargets(); }
            if (targets.None()) { return 0; }
            float avgNeedOfCharge = targets.Average(b => 100 - b.ChargePercentage);
            if (objectiveManager.CurrentOrder == this && avgNeedOfCharge > 10)
            {
                return AIObjectiveManager.OrderPriority;
            }
            return MathHelper.Lerp(0, 50, avgNeedOfCharge / 100);
        }

        public override bool IsCompleted() => false;
        public override bool CanBeCompleted => true;

        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectiveChargeBatteries other && other.orderOption == orderOption;
        }

        protected override void Act(float deltaTime)
        {
            SyncRemovedObjectives(chargeObjectives, targets);
        }

        private void GetTargets()
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
                    if (!targets.Contains(battery))
                    {
                        targets.Add(battery);
                        if (battery.ChargePercentage < 90)
                        {
                            if (!chargeObjectives.TryGetValue(battery, out AIObjectiveOperateItem objective))
                            {
                                objective = new AIObjectiveOperateItem(battery, character, orderOption, false);
                                chargeObjectives.Add(battery, objective);
                                AddSubObjective(objective);
                            }
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
    }
}
