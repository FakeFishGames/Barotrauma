using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.ObjectModel;

namespace Subsurface
{
    class WayPoint : MapEntity
    {
        public enum SpawnType { None, Human, Enemy };

        private SpawnType spawnType;

        public override Vector2 SimPosition
        {
            get { return ConvertUnits.ToSimUnits(new Vector2(rect.X, rect.Y)); }
        }

        public WayPoint(Rectangle newRect)
        {
            rect = newRect;
            linkedTo = new ObservableCollection<MapEntity>();

            mapEntityList.Add(this);
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing)
        {
            if (!editing) return;

            Color clr = (isSelected) ? Color.Red : Color.LightGreen;
            GUI.DrawRectangle(spriteBatch, new Rectangle(rect.X, -rect.Y, rect.Width, rect.Height), clr, true);

            foreach (MapEntity e in linkedTo)
            {
                GUI.DrawLine(spriteBatch,
                    new Vector2(rect.X + rect.Width / 2, -rect.Y + rect.Height / 2),
                    new Vector2(e.Rect.X + e.Rect.Width / 2, -e.Rect.Y + e.Rect.Height / 2),
                    Color.Green);
            }
        }

        public override void DrawEditing(SpriteBatch spriteBatch, Camera cam)
        {
            int x = 300, y = 10;

            spriteBatch.DrawString(GUI.font, "Editing waypoint", new Vector2(x, y), Color.Black);
            spriteBatch.DrawString(GUI.font, "Hold space to link to another entity", new Vector2(x, y + 20), Color.Black);
            spriteBatch.DrawString(GUI.font, "Spawnpoint: "+spawnType.ToString()+" +/-", new Vector2(x, y + 40), Color.Black);

            if (PlayerInput.KeyHit(Keys.Add)) spawnType += 1;
            if (PlayerInput.KeyHit(Keys.Subtract)) spawnType -= 1;

            if (spawnType > SpawnType.Enemy) spawnType = SpawnType.None;
            if (spawnType < SpawnType.None) spawnType = SpawnType.Enemy;

            if (!PlayerInput.LeftButtonClicked()) return;

            Vector2 position = cam.ScreenToWorld(PlayerInput.MousePosition);

            foreach (MapEntity e in mapEntityList)
            {                
                if (e.GetType()!=typeof(WayPoint)) continue;
                if (e == this) continue;

                if (!Map.RectContains(e.Rect, position)) continue;

                linkedTo.Add(e);
                e.linkedTo.Add(this);
            }
        }

        public static WayPoint GetRandom(SpawnType spawnType = SpawnType.None)
        {
            List<WayPoint> wayPoints = new List<WayPoint>();

            foreach (MapEntity e in mapEntityList)
            {
                WayPoint wayPoint = e as WayPoint;
                if (wayPoint==null) continue;
                    
                if (spawnType != SpawnType.None && wayPoint.spawnType != spawnType) continue;

                wayPoints.Add(wayPoint);
            }

            if (wayPoints.Count() == 0) return null;

            return wayPoints[Game1.random.Next(wayPoints.Count())];
        }

        public override XElement Save(XDocument doc)
        {
            XElement element = new XElement("WayPoint");

            element.Add(new XAttribute("ID", ID),
                new XAttribute("x", rect.X),
                new XAttribute("y", rect.Y),
                new XAttribute("spawn", spawnType));

            doc.Root.Add(element);

            if (linkedTo != null)
            {
                int i = 0;
                foreach (MapEntity e in linkedTo)
                {
                    element.Add(new XAttribute("linkedto" + i, e.ID));
                    i += 1;
                }
            }

            return element;
        }

        public static void Load(XElement element)
        {
            Rectangle rect = new Rectangle(
                int.Parse(element.Attribute("x").Value),
                int.Parse(element.Attribute("y").Value),
                (int)Map.gridSize.X, (int)Map.gridSize.Y);

            WayPoint w = new WayPoint(rect);

            w.ID = int.Parse(element.Attribute("ID").Value);
            w.spawnType = (SpawnType)Enum.Parse(typeof(SpawnType), 
                ToolBox.GetAttributeString(element, "spawn", "None"));

            w.linkedToID = new List<int>();
            int i = 0;
            while (element.Attribute("linkedto" + i) != null)
            {
                w.linkedToID.Add(int.Parse(element.Attribute("linkedto" + i).Value));
                i += 1;
            }
        }
    
    }
}
