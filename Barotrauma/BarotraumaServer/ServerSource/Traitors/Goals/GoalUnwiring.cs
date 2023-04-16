using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public sealed class GoalUnwiring : HumanoidGoal
        {
            private readonly string tag;

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[targetname]", "[connectionname]" });
            public override IEnumerable<string> InfoTextValues(Traitor traitor) => base.InfoTextValues(traitor).Concat(new string[] { targetItemPrefabName ?? "", targetConnectionDisplayName ?? targetConnectionName });

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            private readonly List<ConnectionPanel> targetConnectionPanels = new List<ConnectionPanel>();
            private string targetItemPrefabName;
            private string targetConnectionName;
            private string targetConnectionDisplayName;

            public override bool Start(Traitor traitor)
            {
                if (!base.Start(traitor))
                {
                    return false;
                }
                foreach (var item in Item.ItemList)
                {
                    if (item.Submarine == null || Traitors.All(t => item.Submarine.TeamID != t.Character.TeamID))
                    {
                        continue;
                    }
                    if (item.Prefab?.Identifier == tag || item.HasTag(tag))
                    {
                        var connectionPanel = item.GetComponent<ConnectionPanel>();
                        if (connectionPanel != null)
                        {
                            targetConnectionPanels.Add(connectionPanel);
                        }
                    }
                }
                if (targetConnectionPanels.Count > 0)
                {
                    var textId = targetConnectionPanels[0].Item.Prefab.GetItemNameTextId();
                    targetItemPrefabName = TextManager.FormatServerMessage(textId) ?? targetConnectionPanels[0].Item.Prefab.Name.Value;
                }

                return targetConnectionPanels.Count > 0;
            }

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                isCompleted = AreTargetsUnwired();
            }

            private bool AreTargetsUnwired()
            {
                for (int i = 0; i < targetConnectionPanels.Count; i++)
                {
                    for (int j = 0; j < targetConnectionPanels[i].Connections.Count; j++)
                    {
                        if (targetConnectionPanels[i].Connections[j] == null || targetConnectionPanels[i].Connections[j].Wires == null) continue;
                        if (targetConnectionName != string.Empty)
                        {
                            if (targetConnectionPanels[i].Connections[j].Name != targetConnectionName) continue;
                        }
                        if (!targetConnectionPanels[i].Connections[j].Wires.All(w => w == null)) return false;
                    }
                }

                return true;
            }

            public GoalUnwiring(string tag, string targetConnectionName, string targetConnectionDisplayTag) : base()
            {
                this.tag = tag;
                this.targetConnectionName = targetConnectionName;

                if (targetConnectionDisplayTag != string.Empty)
                {
                    targetConnectionDisplayName = TextManager.FormatServerMessage(targetConnectionDisplayTag);
                    InfoTextId = "TraitorGoalUnwireInfo";
                }
                else
                {
                    InfoTextId = "TraitorGoalUnwireAllInfo";
                }
            }
        }
    }
}
