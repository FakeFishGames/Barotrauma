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
            public override IEnumerable<string> InfoTextValues => base.InfoTextValues.Concat(new string[] { string.Format("{0:0}", DestroyPercent * 100.0f), tagPrefabName });

            private readonly float destroyPercent;
            protected float DestroyPercent => destroyPercent;

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            private int totalCount = 0;
            private int targetCount = 0;
            private string tagPrefabName = null;

            protected int CountMatchingItems()
            {
                int result = 0;
                foreach (var item in Item.ItemList)
                {
                    if (!matchInventory && item.ParentInventory?.Owner is Character && item.ParentInventory?.Owner != Traitor.Character)
                    {
                        continue;
                    }

                    if (item.Submarine == null)
                    {
                        if (!(item.ParentInventory?.Owner is Character)) { continue; }
                    }
                    else
                    {
                        if (item.Submarine.TeamID != Traitor.Character.TeamID) { continue; }
                    }

                    if (item.Condition <= 0.0f)
                    {
                        continue;
                    }
                    var identifierMatches = matchIdentifier && item.prefab.Identifier == tag;
                    if (identifierMatches && tagPrefabName == null)
                    {
                        var textId = item.Prefab.GetNameTextId();
                        tagPrefabName = textId != null ? TextManager.FormatServerMessage(textId) : item.Prefab.Name;
                    }
                    if (identifierMatches || (matchTag && item.HasTag(tag)))
                    {
                        ++result;
                    }
                }
                return result;
            }

            public override void Update(float deltaTime)
            {
                base.Update(deltaTime);
                isCompleted = CountMatchingItems() <= targetCount;
            }

            public override bool Start(Traitor traitor)
            {
                if (!base.Start(traitor))
                {
                    return false;
                }
                totalCount = CountMatchingItems();
                if (totalCount <= 0)
                {
                    return false;
                }
                targetCount = (int)((1.0f - destroyPercent) * totalCount - 0.5f);
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
