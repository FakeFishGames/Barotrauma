using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public sealed class GoalSabotageItems : HumanoidGoal
        {
            private readonly string tag;
            private readonly float conditionThreshold;

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[tag]", "[target]", "[threshold]" });
            public override IEnumerable<string> InfoTextValues => base.InfoTextValues.Concat(new string[] { tag ?? "", targetItemPrefabName ?? "", string.Format("{0:0}", conditionThreshold) });

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            private readonly List<Item> targetItems = new List<Item>();
            private string targetItemPrefabName = null;

            public override bool Start(Traitor traitor)
            {
                if (!base.Start(traitor))
                {
                    return false;
                }
                foreach (var item in Item.ItemList)
                {
                    if (item.Submarine == null || item.Submarine.TeamID != Traitor.Character.TeamID)
                    {
                        continue;
                    }
                    if (item.Condition > conditionThreshold && (item.Prefab?.Identifier == tag || item.HasTag(tag)))
                    {
                        targetItems.Add(item);
                    }
                }
                if (targetItems.Count > 0)
                {
                    var textId = targetItems[0].Prefab.GetItemNameTextId();
                    targetItemPrefabName = TextManager.FormatServerMessage(textId) ?? targetItems[0].Prefab.Name;
                }
                return targetItems.Count > 0;
            }

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                isCompleted = targetItems.All(item => item.Condition <= conditionThreshold);
            }

            public GoalSabotageItems(string tag, float conditionThreshold) : base()
            {
                this.tag = tag;
                this.conditionThreshold = conditionThreshold;
                InfoTextId = "TraitorGoalSabotageInfo";
            }
        }
    }
}
