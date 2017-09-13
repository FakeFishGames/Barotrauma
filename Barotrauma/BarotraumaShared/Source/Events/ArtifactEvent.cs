using Microsoft.Xna.Framework;
using System;
using System.Xml.Linq;

namespace Barotrauma
{
    class ArtifactEvent : ScriptedEvent
    {
        private ItemPrefab itemPrefab;

        private Item item;

        private int state;

        public override string ToString()
        {
            return "ScriptedEvent (" + (itemPrefab==null ? "null" : itemPrefab.Name) + ")";
        }
        
        public ArtifactEvent(XElement element)
            : base(element)
        {
            string itemName = ToolBox.GetAttributeString(element, "itemname", "");

            itemPrefab = ItemPrefab.list.Find(ip => ip.Name == itemName) as ItemPrefab;

            if (itemPrefab == null)
            {
                DebugConsole.ThrowError("Error in SalvageMission: couldn't find an item prefab with the name "+itemName);
            }
        }

        public override void Init()
        {
            base.Init();

            Vector2 position = Level.Loaded.GetRandomItemPos(
                Level.PositionType.Cave | Level.PositionType.MainPath | Level.PositionType.Ruin, 500.0f, 30.0f);

            item = new Item(itemPrefab, position, null);
            item.MoveWithLevel = true;
            item.body.FarseerBody.IsKinematic = true;

            //try to find a nearby artifact holder (or any alien itemcontainer) and place the artifact inside it
            foreach (Item it in Item.ItemList)
            {
                if (it.Submarine != null || !it.HasTag("alien")) continue;

                if (Math.Abs(item.WorldPosition.X - it.WorldPosition.X) > 2000.0f) continue;
                if (Math.Abs(item.WorldPosition.Y - it.WorldPosition.Y) > 2000.0f) continue;

                var itemContainer = it.GetComponent<Items.Components.ItemContainer>();
                if (itemContainer == null) continue;

                itemContainer.Combine(item);
                break;
            }

            if (GameSettings.VerboseLogging)
            {
                DebugConsole.NewMessage("Initialized ArtifactEvent (" + item.Name + ")", Color.White);
            }
        }

        public override void Update(float deltaTime)
        {
            switch (state)
            {
                case 0:
                    if (item.ParentInventory!=null) item.body.FarseerBody.IsKinematic = false;
                    if (item.CurrentHull == null) return;

                    state = 1;
                    break;
                case 1:
                    if (!Submarine.MainSub.AtEndPosition && !Submarine.MainSub.AtStartPosition) return;

                    Finished();
                    state = 2;
                    break;
            }    
        }
    }
}
