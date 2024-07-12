using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class ItemAssemblyPrefab : MapEntityPrefab
    {
        public void DrawIcon(SpriteBatch spriteBatch, GUICustomComponent guiComponent)
        {
            Rectangle drawArea = guiComponent.Rect;

            float scale = Math.Min(drawArea.Width / (float)Bounds.Width, drawArea.Height / (float)Bounds.Height) * 0.9f;

            foreach (var displayEntity in DisplayEntities)
            {
                var entityPrefab = FindByIdentifier(displayEntity.Identifier);
                if (entityPrefab is CoreEntityPrefab || entityPrefab == null) { continue; }
                var drawRect = new Rectangle(
                    (int)(displayEntity.Rect.X * scale) + drawArea.Center.X, (int)((displayEntity.Rect.Y) * scale) - drawArea.Center.Y, 
                    (int)(displayEntity.Rect.Width * scale), (int)(displayEntity.Rect.Height * scale));
                entityPrefab.DrawPlacing(spriteBatch, drawRect, entityPrefab.Scale * scale, rotation: displayEntity.RotationRad);
            }
        }

        public override void DrawPlacing(SpriteBatch spriteBatch, Camera cam)
        {
            base.DrawPlacing(spriteBatch, cam);
            Draw(
                spriteBatch,
                placePosition != Vector2.Zero ? placePosition : Submarine.MouseToWorldGrid(cam, Submarine.MainSub));
        }

        public void Draw(SpriteBatch spriteBatch, Vector2 pos)
        {
            foreach (var displayEntity in DisplayEntities)
            {
                var entityPrefab = FindByIdentifier(displayEntity.Identifier);
                if (entityPrefab == null) { continue; }
                Rectangle drawRect = displayEntity.Rect;
                drawRect.Location += pos.ToPoint();
                entityPrefab.DrawPlacing(spriteBatch, drawRect, entityPrefab.Scale, rotation: displayEntity.RotationRad);
            }
        }

        public static XElement Save(List<MapEntity> entities, string name, string description, bool hideInMenus = false)
        {
            XElement element = new XElement("ItemAssembly",
                new XAttribute("name", name),
                new XAttribute("description", description),
                new XAttribute("hideinmenus", hideInMenus));

            //move the entities so that their "center of mass" is at {0,0}
            var assemblyEntities = MapEntity.CopyEntities(entities);
            for (int i = 0; i < assemblyEntities.Count && i < entities.Count; i++)
            {
                assemblyEntities[i].Layer = entities[i].Layer;
            }

            //find wires and items that are contained inside another item
            //place them at {0,0} to prevent them from messing up the origin of the prefab and to hide them in preview
            List<MapEntity> disabledEntities = new List<MapEntity>();
            foreach (MapEntity mapEntity in assemblyEntities)
            {
                if (mapEntity is Item item)
                {
                    var wire = item.GetComponent<Wire>();
                    if (item.ParentInventory != null ||
                        (wire != null && wire.Connections.Any(c => c != null)))
                    {
                        item.SetTransform(Vector2.Zero, 0.0f);
                        disabledEntities.Add(mapEntity);
                    }
                }
            }

            float minX = int.MaxValue, maxX = int.MinValue;
            float minY = int.MaxValue, maxY = int.MinValue;
            foreach (MapEntity mapEntity in assemblyEntities)
            {
                if (disabledEntities.Contains(mapEntity)) { continue; }
                minX = Math.Min(minX, mapEntity.WorldRect.X);
                maxX = Math.Max(maxX, mapEntity.WorldRect.Right);
                minY = Math.Min(minY, mapEntity.WorldRect.Y - mapEntity.WorldRect.Height);
                maxY = Math.Max(maxY, mapEntity.WorldRect.Y);
            }
            Vector2 center = new Vector2((minX + maxX) / 2.0f, (minY + maxY) / 2.0f);
            if (Submarine.MainSub != null) { center -= Submarine.MainSub.HiddenSubPosition; }

            Vector2 offsetFromGrid = new Vector2(
                MathUtils.RoundTowardsClosest(center.X, Submarine.GridSize.X) - center.X,
                MathUtils.RoundTowardsClosest(center.Y, Submarine.GridSize.Y) - center.Y - Submarine.GridSize.Y / 2);

            MapEntity.SelectedList.Clear();
            assemblyEntities.ForEach(e => MapEntity.AddSelection(e));

            foreach (MapEntity mapEntity in assemblyEntities)
            {
                mapEntity.Move(-center - offsetFromGrid);
                mapEntity.Submarine = Submarine.MainSub;
                var entityElement = mapEntity.Save(element);
                if (disabledEntities.Contains(mapEntity))
                {
                    entityElement.Add(new XAttribute("hideinassemblypreview", "true"));
                }
            }

            //restore the previous selection
            MapEntity.SelectedList.Clear();
            entities.ForEach(e => MapEntity.AddSelection(e));

            return element;
        }
    }
}
