using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public class GoalDestroyItemsWithTag : Goal
        {
            private readonly string tag;
            private readonly bool matchIdentifier;
            private readonly bool matchTag;
            private readonly bool matchInventory;

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[percentage]", "[tag]" });
            public override IEnumerable<string> InfoTextValues => base.InfoTextValues.Concat(new string[] { string.Format("{0:0}", DestroyPercent * 100.0f), tag });

            private readonly float destroyPercent;
            protected float DestroyPercent => destroyPercent;

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            private int totalCount = 0;
            private int targetCount = 0;

            protected int CountMatchingItems(bool includeDestroyed)
            {
                int result = 0;
                foreach (var item in Item.ItemList)
                {
                    if (item == null || item.Prefab == null)
                    {
                        continue;
                    }
                    if (item.Submarine == null || item.Submarine.TeamID != Traitor.Character.TeamID)
                    {
                        continue;
                    }
                    if (!matchInventory && item.ParentInventory?.Owner is Character)
                    {
                        continue;
                    }
                    if (!includeDestroyed && (item.Condition <= 0.0f || /* item.CurrentHull == null || */!Traitor.Character.Submarine.IsEntityFoundOnThisSub(item, true)))
                    {
                        continue;
                    }
                    if ((matchIdentifier && item.prefab.Identifier == tag) || (matchTag && item.HasTag(tag)))
                    {
                        ++result;
                    }
                }
                return result;
            }

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                isCompleted = CountMatchingItems(false) <= targetCount;
            }

            public override bool Start(GameServer server, Traitor traitor)
            {
                if (!base.Start(server, traitor))
                {
                    return false;
                }
                totalCount = CountMatchingItems(true);
                if (totalCount <= 0)
                {
                    return false;
                }
                targetCount = (int)(destroyPercent * totalCount + 0.5f);
                return true;
            }

            public GoalDestroyItemsWithTag(string tag, float destroyPercent, bool matchTag, bool matchIdentifier, bool matchInventory) : base()
            {
                InfoTextId = "TraitorGoalDestroyItems";
                this.tag = tag;
                this.destroyPercent = destroyPercent;
                this.matchTag = matchTag;
                this.matchIdentifier = matchIdentifier;
                this.matchInventory = matchInventory;
            }
        }
    }
}
