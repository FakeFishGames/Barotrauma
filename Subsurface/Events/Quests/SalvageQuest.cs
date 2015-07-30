using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Subsurface
{
    class SalvageQuest : Quest
    {
        ItemPrefab itemPrefab;

        Item item;

        public override string RadarLabel
        {
            get
            {
                return "Infrasonic signal";
            }
        }

        public override Vector2 RadarPosition
        {
            get
            {
                return item.Position;
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
            //item.MoveWithLevel = true;
        }

        public override void End()
        {
            if (item.CurrentHull == null) return;

            item.Remove();

            GiveReward();

            completed = true;
        }
    }
}
