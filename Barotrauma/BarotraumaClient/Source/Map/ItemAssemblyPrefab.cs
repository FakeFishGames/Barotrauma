using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class ItemAssemblyPrefab
    {
        public void DrawIcon(SpriteBatch spriteBatch, GUICustomComponent guiComponent)
        {
            Rectangle drawArea = guiComponent.Rect;

            float scale = Math.Min(drawArea.Width / (float)Bounds.Width, drawArea.Height / (float)Bounds.Height) * 0.9f;

            foreach (Pair<MapEntityPrefab, Rectangle> entity in Entities)
            {
                Rectangle drawRect = entity.Second;
                drawRect = new Rectangle(
                    (int)(drawRect.X * scale) + drawArea.Center.X, -((int)((drawRect.Y - drawRect.Height) * scale) + drawArea.Center.Y), 
                    (int)(drawRect.Width * scale), (int)(drawRect.Height * scale));
                entity.First.DrawPlacing(spriteBatch, drawRect, scale);
            }
        }


        public override void DrawPlacing(SpriteBatch spriteBatch, Camera cam)
        {
            base.DrawPlacing(spriteBatch, cam);
            foreach (Pair<MapEntityPrefab, Rectangle> entity in Entities)
            {
                Rectangle drawRect = entity.Second;
                drawRect.Location += Submarine.MouseToWorldGrid(cam, Submarine.MainSub).ToPoint();
                entity.First.DrawPlacing(spriteBatch, drawRect);
            }
        }

        public static XElement Save(List<MapEntity> entities, string name, string description, bool hideInMenus = false)
        {
            XElement element = new XElement("ItemAssembly",
                new XAttribute("name", name),
                new XAttribute("description", description),
                new XAttribute("hideinmenus", hideInMenus));

            //move the entities so that their "center of mass" is at {0,0}
            var assemblyEntities = MapEntity.CopyEntities(MapEntity.SelectedList);
            float minX = assemblyEntities[0].WorldRect.X, maxX = assemblyEntities[0].WorldRect.Right;
            float minY = assemblyEntities[0].WorldRect.Y - assemblyEntities[0].WorldRect.Height, maxY = assemblyEntities[0].WorldRect.Y;
            for (int i = 1; i < assemblyEntities.Count; i++)
            {
                minX = Math.Min(minX, assemblyEntities[i].WorldRect.X);
                maxX = Math.Max(maxX, assemblyEntities[i].WorldRect.Right);
                minY = Math.Min(minY, assemblyEntities[i].WorldRect.Y - assemblyEntities[i].WorldRect.Height);
                maxY = Math.Max(maxY, assemblyEntities[i].WorldRect.Y);
            }
            Vector2 center = new Vector2((minX + maxX) / 2.0f, (minY + maxY) / 2.0f);
            if (Submarine.MainSub != null) center -= Submarine.MainSub.HiddenSubPosition;
            center.X -= center.X % Submarine.GridSize.X;
            center.Y -= center.Y % Submarine.GridSize.Y;

            MapEntity.SelectedList.Clear();
            MapEntity.SelectedList.AddRange(assemblyEntities);

            foreach (MapEntity mapEntity in assemblyEntities)
            {
                mapEntity.Move(-center);
                mapEntity.Submarine = Submarine.MainSub;
                mapEntity.Save(element);
            }

            return element;
        }
    }
}
