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
            Vector2 position = level.PositionsOfInterest[Rand.Int(level.PositionsOfInterest.Count)];

            item = new Item(itemPrefab, position + level.Position);
            item.MoveWithLevel = true;
            //item.MoveWithLevel = true;
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
