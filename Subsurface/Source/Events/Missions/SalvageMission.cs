using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class SalvageMission : Mission
    {
        private ItemPrefab itemPrefab;

        private Item item;

        private Level.PositionType spawnPositionType;

        private int state;

        public override Vector2 RadarPosition
        {
            get
            {
                return state>0 ? Vector2.Zero : ConvertUnits.ToDisplayUnits(item.SimPosition);
            }
        }

        public SalvageMission(XElement element)
            : base(element)
        {
            string itemName = ToolBox.GetAttributeString(element, "itemname", "");

            itemPrefab = ItemPrefab.list.Find(ip => ip.Name == itemName) as ItemPrefab;
            
            string spawnPositionTypeStr = ToolBox.GetAttributeString(element, "spawntype", "");

            if (string.IsNullOrWhiteSpace(spawnPositionTypeStr) ||
                !Enum.TryParse<Level.PositionType>(spawnPositionTypeStr, true, out spawnPositionType))
            {
                spawnPositionType = Level.PositionType.Cave | Level.PositionType.Ruin;
            }

            if (itemPrefab == null)
            {
                DebugConsole.ThrowError("Error in SalvageMission: couldn't find an item prefab with the name "+itemName);
            }
        }

        public override void Start(Level level)
        {
            Vector2 position = Level.Loaded.GetRandomItemPos(spawnPositionType, 100.0f, 30.0f);
            
            item = new Item(itemPrefab, position, null);
            item.MoveWithLevel = true;
            item.body.FarseerBody.IsKinematic = true;
        }

        public override void Update(float deltaTime)
        {
            switch (state)
            {
                case 0:
                    //item.body.LinearVelocity = Vector2.Zero;
                    if (item.ParentInventory!=null) item.body.FarseerBody.IsKinematic = false;
                    if (item.CurrentHull == null) return;
                    
                    ShowMessage(state);
                    state = 1;
                    break;
                case 1:
                    if (!Submarine.MainSub.AtEndPosition && !Submarine.MainSub.AtStartPosition) return;
                    ShowMessage(state);
                    state = 2;
                    break;
            }    
        }

        public override void End()
        {
            if (item.CurrentHull == null || !item.CurrentHull.Submarine.AtEndPosition || item.Removed) return;
            item.Remove();

            GiveReward();

            completed = true;
        }
    }
}
