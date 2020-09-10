using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public sealed class GoalDestroyItemsWithTag : Goal
        {
            private readonly StringIdentifier GoalTag;
            private readonly bool matchIdentifier;
            private readonly bool matchTag;
            private readonly bool matchInventory;

            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[percentage]", "[tag]" });
            public override IEnumerable<string> InfoTextValues(Traitor traitor) => base.InfoTextValues(traitor).Concat(new string[] { string.Format("{0:0}", DestroyPercent * 100.0f), tagPrefabName ?? "" });

            private readonly float destroyPercent;
            private float DestroyPercent => destroyPercent;

            private bool isCompleted = false;
            public override bool IsCompleted => isCompleted;

            private int totalCount = 0;
            private int targetCount = 0;
            private string tagPrefabName = null;

            private int CountMatchingItems()
            {
                int result = 0;
                foreach (var item in Item.ItemList)
                {
                    if (!matchInventory && Traitors.All(traitor => item.FindParentInventory(inventory => inventory.Owner is Character && inventory.Owner != traitor.Character) != null))
                    {
                        continue;
                    }

                    if (item.Submarine == null)
                    {
                        //items outside the sub don't count as destroyed if they're still in the traitor's inventory
                        bool carriedByTraitor = Traitors.Any(traitor => item.IsOwnedBy(traitor.Character));
                        if (!carriedByTraitor) { continue; }
                    }
                    else
                    {
                        if (Traitors.All(traitor => item.Submarine.TeamID != traitor.Character.TeamID)) { continue; }
                    }

                    if (item.Condition <= 0.0f)
                    {
                        continue;
                    }
                    var identifierMatches = matchIdentifier && item.prefab.MapEntityIdentifier == GoalTag;
                    if (identifierMatches && tagPrefabName == null)
                    {
                        var textId = item.Prefab.GetItemNameTextId();
                        tagPrefabName = textId != null ? TextManager.FormatServerMessage(textId) : item.Prefab.Name;
                    }
                    if (identifierMatches || (matchTag && item.ItemTags.HasTag(GoalTag.IdentifierString)))
                    {
                        ++result;
                    }
                }

                // Quick fix
                if (tagPrefabName == null && matchIdentifier)
                {
                    tagPrefabName = TextManager.FormatServerMessage($"entityname.{GoalTag.IdentifierString}");
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
                GoalTag = new StringIdentifier(tag);
                this.destroyPercent = destroyPercent;
                this.matchTag = matchTag;
                this.matchIdentifier = matchIdentifier;
                this.matchInventory = matchInventory;
            }
        }
    }
}
