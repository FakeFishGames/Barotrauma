using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class Traitor
    {
        public class GoalFindItem : Goal
        {
            private string identifier;
            private Item target;

            // TODO(xxx): [target] and [targethullname] don't really make sense, also could be that the target item is not inside a container esp if someone has moved it?
            public override IEnumerable<string> InfoTextKeys => base.InfoTextKeys.Concat(new string[] { "[identifier]", "[target]", "[targethullname]" });
            public override IEnumerable<string> InfoTextValues => base.InfoTextValues.Concat(new string[] { target.prefab.Identifier, target.ContainerIdentifier ?? "(unknown)", target.Container?.CurrentHull?.DisplayName ?? "(unknown)" });

            public override bool IsCompleted => target.ParentInventory == Traitor.Character.Inventory;

            public override bool Start(GameServer server, Traitor traitor)
            {
                if (!base.Start(server, traitor))
                {
                    return false;
                }
                // TODO(xxx): Spawn item to find
                // ItemPrefab.Find();
                target = Item.ItemList.Find(item => item.prefab.Identifier == identifier);
                return target != null;
            }

            public GoalFindItem(string identifier)
            {
                this.identifier = identifier;
            }
        }
    }
}
