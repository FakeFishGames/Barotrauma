using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class SalvageQuest : Quest
    {
        private ItemPrefab itemPrefab;

        private Item item;

        private int state;

        public override Vector2 RadarPosition
        {
            get
            {
                return ConvertUnits.ToDisplayUnits(item.SimPosition);
            }
        }

        public SalvageQuest(XElement element)
            : base(element)
        {
            string itemName = ToolBox.GetAttributeString(element, "itemname", "");

            itemPrefab = ItemPrefab.list.Find(ip => ip.Name == itemName) as ItemPrefab;

            if (itemPrefab == null)
            {
                DebugConsole.ThrowError("Error in salvagequest: couldn't find an item prefab with the name "+itemName);
            }
        }

        public override void Start(Level level)
        {
            Vector2 position = Vector2.Zero;

            int tries = 0;
            do
            {
                Vector2 tryPos = level.PositionsOfInterest[Rand.Int(level.PositionsOfInterest.Count, false)];
                
                if (Submarine.PickBody(
                    tryPos, 
                    tryPos - Vector2.UnitY*level.Size.Y, 
                    null, Physics.CollisionLevel) != null)
                {
                    position = tryPos;
                    break;
                }

                tries++;

                if (tries==10)
                {
                    position = level.EndPosition - Vector2.UnitY*300.0f;
                }

            } while (tries < 10);


            item = new Item(itemPrefab, position, null);
            item.MoveWithLevel = true;
            item.body.FarseerBody.GravityScale = 0.5f;
            //item.MoveWithLevel = true;
        }

        public override void Update(float deltaTime)
        {
            switch (state)
            {
                case 0:
                    if (item.CurrentHull == null) return;
                    ShowMessage(state);
                    state = 1;
                    break;
                case 1:
                    if (!Level.Loaded.AtEndPosition && !Level.Loaded.AtStartPosition) return;
                    ShowMessage(state);
                    state = 2;
                    break;
            }    
        }

        public override void End()
        {
            item.Remove();
            if (item.CurrentHull == null)
            {
                new GUIMessageBox("Quest failed", failureMessage);
                return;
            }            

            GiveReward();

            completed = true;
        }
    }
}
