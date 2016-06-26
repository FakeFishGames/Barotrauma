using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Barotrauma
{

    class LinkedSubmarinePrefab : MapEntityPrefab
    {
        public readonly Submarine mainSub;
        
        public LinkedSubmarinePrefab(Submarine submarine)
        {
            this.mainSub = submarine;
        }

        protected override void CreateInstance(Rectangle rect)
        {
            System.Diagnostics.Debug.Assert(Submarine.MainSub != null);

            LinkedSubmarine.Create(Submarine.MainSub, mainSub.FilePath, rect.Location.ToVector2());
        }
    }

    class LinkedSubmarine : MapEntity
    {
        private List<Vector2> wallVertices;

        private string filePath;

        private XElement saveElement;

        public LinkedSubmarine(Submarine submarine)
            : base(null, submarine) 
        {
            InsertToList();
        }
        
        public static LinkedSubmarine Create(Submarine mainSub, string filePath, Vector2 position)
        {
            LinkedSubmarine sl = new LinkedSubmarine(mainSub);
            sl.filePath = filePath;

            XDocument doc = Submarine.OpenFile(filePath);
            if (doc == null || doc.Root == null) return null;

            sl.GenerateWallVertices(doc.Root);

            //for (int i = 0; i < sl.wallVertices.Count; i++)
            //{
            //    sl.wallVertices[i] = sl.wallVertices[i] += position;
            //}

            sl.Rect = new Rectangle(
                (int)sl.wallVertices.Min(v => v.X + position.X),
                (int)sl.wallVertices.Max(v => v.Y + position.Y),
                (int)sl.wallVertices.Max(v => v.X + position.X),
                (int)sl.wallVertices.Min(v => v.Y + position.Y));

            sl.rect = new Rectangle(sl.rect.X, sl.rect.Y, sl.rect.Width - sl.rect.X, sl.rect.Y - sl.rect.Height);

            return sl;
        }

        public override bool IsMouseOn(Vector2 position)
        {
            return Vector2.Distance(position, WorldPosition) < 50.0f;
        }

        public override void Draw(SpriteBatch spriteBatch, bool editing, bool back = true)
        {
            if (!editing || wallVertices == null) return;

            Color color = (isHighlighted) ? Color.Orange : Color.Green;
            if (isSelected) color = Color.Red;
            
            Vector2 pos = new Vector2(rect.X + rect.Width/2, rect.Y - rect.Height/2);

            for (int i = 0; i < wallVertices.Count; i++)
            {
                Vector2 startPos = wallVertices[i] + pos;
                startPos.Y = -startPos.Y;

                Vector2 endPos = wallVertices[(i + 1) % wallVertices.Count] + pos;
                endPos.Y = -endPos.Y;

                GUI.DrawLine(spriteBatch, 
                    startPos, 
                    endPos, 
                    color, 0.0f, 5);
            }

            pos.Y = -pos.Y;
            GUI.DrawLine(spriteBatch, pos + Vector2.UnitY * 50.0f, pos - Vector2.UnitY * 50.0f, color, 0.0f, 5);
            GUI.DrawLine(spriteBatch, pos + Vector2.UnitX * 50.0f, pos - Vector2.UnitX * 50.0f, color, 0.0f, 5);

        }

        private void GenerateWallVertices(XElement rootElement)
        {
            List<Vector2> points = new List<Vector2>();

            var wallPrefabs =
                MapEntityPrefab.list.FindAll(mp => (mp is StructurePrefab) && ((StructurePrefab)mp).HasBody);

            foreach (XElement element in rootElement.Elements())
            {
                if (element.Name != "Structure") continue;

                string name = ToolBox.GetAttributeString(element, "name", "");
                if (!wallPrefabs.Any(wp => wp.Name == name)) continue;

                var rect = ToolBox.GetAttributeVector4(element, "rect", Vector4.Zero);
                
                points.Add(new Vector2(rect.X, rect.Y));
                points.Add(new Vector2(rect.X + rect.Z, rect.Y));
                points.Add(new Vector2(rect.X, rect.Y - rect.W));
                points.Add(new Vector2(rect.X + rect.Z, rect.Y - rect.W));
            }

            wallVertices = MathUtils.GiftWrap(points);
        }

        public override XElement Save(XElement parentElement)
        {
            var doc = Submarine.OpenFile(filePath);            

            doc.Root.Name = "LinkedSubmarine";

            doc.Root.Add(
                new XAttribute("filepath", filePath), 
                new XAttribute("pos", ToolBox.Vector2ToString(Position - Submarine.HiddenSubPosition)));

            parentElement.Add(doc.Root);

            return doc.Root;
        }

        public static void Load(XElement element, Submarine submarine)
        {
            Vector2 pos = ToolBox.GetAttributeVector2(element, "pos", Vector2.Zero);

            if (Screen.Selected == GameMain.EditMapScreen)
            {
                string filePath = ToolBox.GetAttributeString(element, "filepath", "");
                
                Create(submarine, filePath, pos);

                return;
            }

            var ls = new LinkedSubmarine(submarine);
            ls.saveElement = element;

            ls.rect.Location = pos.ToPoint();
        }

        public override void OnMapLoaded()
        {
            if (saveElement == null) return;
            var sub = Submarine.Load(saveElement, false);
            sub.SetPosition(WorldPosition - Submarine.WorldPosition);
            sub.Submarine = Submarine;
        }
    }
}
