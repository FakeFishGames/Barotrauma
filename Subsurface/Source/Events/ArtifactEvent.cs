using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class ArtifactEvent : ScriptedEvent
    {
        private ItemPrefab itemPrefab;

        private Item item;

        private int state;
        
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

        protected override void Start()
        {
            Vector2 position = Level.Loaded.GetRandomItemPos(30.0f);

            item = new Item(itemPrefab, position, null);
            item.MoveWithLevel = true;
            item.body.FarseerBody.IsKinematic = true;
        }

        public override void Update(float deltaTime)
        {
            switch (state)
            {
                case 0:
                    Start();
                    state = 1;
                    break;
                case 1:

                    //item.body.LinearVelocity = Vector2.Zero;
                    if (item.ParentInventory!=null) item.body.FarseerBody.IsKinematic = false;
                    if (item.CurrentHull == null) return;

                    state = 2;
                    break;
                case 2:
                    if (!Submarine.Loaded.AtEndPosition && !Submarine.Loaded.AtStartPosition) return;

                    Finished();
                    state = 3;
                    break;
            }    
        }
    }
}
