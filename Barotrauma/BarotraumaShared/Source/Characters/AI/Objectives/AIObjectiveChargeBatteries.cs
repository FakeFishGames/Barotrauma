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

        private List<PowerContainer> batteries = new List<PowerContainer>();
        private Dictionary<PowerContainer, AIObjectiveOperateItem> chargeObjectives = new Dictionary<PowerContainer, AIObjectiveOperateItem>();
        private string orderOption;

        private HashSet<PowerContainer> ignoreList = new HashSet<PowerContainer>();
        private readonly float ignoreListClearIntervalBase = 30;
        private readonly float ignoreListIntervalAddition = 10;
        private float ignoreListClearInterval;
        private float ignoreListTimer;

        public AIObjectiveChargeBatteries(Character character, string option) : base(character, option)
        {
            orderOption = option;
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
                FindBatteries();
                CreateObjectives();
            }
            else
            {
                ignoreListTimer += deltaTime;
            }
            foreach (var objective in chargeObjectives)
            {
                if (!objective.Value.CanBeCompleted)
                {
                    ignoreList.Add(objective.Key);
                }
            }
            SyncRemovedObjectives(chargeObjectives, batteries);
            if (batteries.None())
            {
                FindBatteries();
            }
            if (chargeObjectives.None())
            {
                CreateObjectives();
            }
        }

        public override float GetPriority(AIObjectiveManager objectiveManager)
        {
            if (character.Submarine == null) { return 0; }
            if (batteries.None() || chargeObjectives.None()) { return 0; }
            float avgNeedOfCharge = batteries.Average(b => 100 - b.ChargePercentage);
            if (objectiveManager.CurrentOrder == this)
            {
                return AIObjectiveManager.OrderPriority - MathHelper.Max(0, AIObjectiveManager.OrderPriority - avgNeedOfCharge);
            }
            return MathHelper.Lerp(0, 50, avgNeedOfCharge / 100);
        }

        public override bool IsCompleted() => false;
        public override bool CanBeCompleted => true;

        // TODO: currently there can be multiple objectives with different order options. We don't want that
        public override bool IsDuplicate(AIObjective otherObjective)
        {
            return otherObjective is AIObjectiveChargeBatteries other && other.orderOption == orderOption;
        }

        protected override void Act(float deltaTime) { }

        private void FindBatteries()
        {
            batteries.Clear();
            foreach (Item item in Item.ItemList)
            {
                if (item.Prefab.Identifier != "battery" && !item.HasTag("battery")) { continue; }
                foreach (Submarine sub in Submarine.Loaded)
                {
                    if (sub.TeamID != character.TeamID) { continue; }
                    // If the character is inside, only take items in connected hulls into account.
                    if (character.Submarine != null && !character.Submarine.IsEntityFoundOnThisSub(item, true)) { continue; }
                    var battery = item.GetComponent<PowerContainer>();
                    if (!batteries.Contains(battery))
                    {
                        batteries.Add(battery);
                    }
                }
            }
            if (batteries.None())
            {
                character.Speak(TextManager.Get("DialogNoBatteries"), null, 4.0f, "nobatteries", 10.0f);
            }
            else
            {
                batteries.Sort((x, y) => x.ChargePercentage.CompareTo(y.ChargePercentage));
            }
        }

        private void CreateObjectives()
        {
            foreach (var battery in batteries)
            {
                if (ignoreList.Contains(battery)) { continue; }
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
