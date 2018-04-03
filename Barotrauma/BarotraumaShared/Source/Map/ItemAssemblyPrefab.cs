using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    class ItemAssemblyPrefab : MapEntityPrefab
    {
        private readonly XElement configElement;
        private readonly string configPath;

        private List<Pair<MapEntityPrefab,Rectangle>> entities;

        public ItemAssemblyPrefab(string filePath)
        {
            configPath = filePath;
            XDocument doc = XMLExtensions.TryLoadXml(filePath);
            if (doc == null || doc.Root == null) return;

            name = doc.Root.GetAttributeString("name", "");
            configElement = doc.Root;

            Category = MapEntityCategory.ItemAssembly;

            SerializableProperty.DeserializeProperties(this, configElement);

            entities = new List<Pair<MapEntityPrefab, Rectangle>>();
            foreach (XElement entityElement in doc.Root.Elements())
            {
                string entityName = entityElement.GetAttributeString("name", "");
                MapEntityPrefab mapEntity = List.Find(p => p.Name == entityName);
                if (mapEntity != null) entities.Add(new Pair<MapEntityPrefab,Rectangle>(mapEntity, entityElement.GetAttributeRect("rect", Rectangle.Empty)));
            }
            
            float minX = entities[0].Second.X, maxX = entities[0].Second.Right;
            float minY = entities[0].Second.Y - entities[0].Second.Height, maxY = entities[0].Second.Y;
            foreach (Pair<MapEntityPrefab, Rectangle> entity in entities)
            {
                for (int i = 0; i < entities.Count; i++)
                {
                    minX = Math.Min(minX, entities[i].Second.Center.X);
                    maxX = Math.Max(maxX, entities[i].Second.Center.X);
                    minY = Math.Min(minY, entities[i].Second.Y - entities[i].Second.Height / 2);
                    maxY = Math.Max(maxY, entities[i].Second.Y - entities[i].Second.Height / 2);
                }
            }

            Vector2 center = new Vector2((minX + maxX) / 2.0f, (minY + maxY) / 2.0f);
            for (int i = 0; i < entities.Count; i++)
            {
                entities[i].Second = new Rectangle(
                    (int)(entities[i].Second.X - center.X),
                    (int)(entities[i].Second.Y - center.Y),
                    entities[i].Second.Width,
                    entities[i].Second.Height);
            }

            List.Add(this);
        }

        public override void DrawPlacing(SpriteBatch spriteBatch, Camera cam, Rectangle? placeRect = null)
        {
            base.DrawPlacing(spriteBatch, cam);
            foreach (Pair<MapEntityPrefab, Rectangle> entity in entities)
            {
                Rectangle drawRect = entity.Second;
                drawRect.Location += Submarine.MouseToWorldGrid(cam, Submarine.MainSub).ToPoint();
                entity.First.DrawPlacing(spriteBatch, cam, drawRect);
            }
        }

        protected override void CreateInstance(Rectangle rect)
        {
            List<MapEntity> entities = MapEntity.LoadAll(Submarine.MainSub, configElement, configPath);
            if (entities.Count == 0) return;
            
            //move the created entities to the center of the ItemAssembly
            float minX = entities[0].WorldRect.X, maxX = entities[0].WorldRect.Right;
            float minY = entities[0].WorldRect.Y - entities[0].WorldRect.Height, maxY = entities[0].WorldRect.Y;
            for (int i = 0; i < entities.Count; i++)
            {
                minX = Math.Min(minX, entities[i].WorldRect.Center.X);
                maxX = Math.Max(maxX, entities[i].WorldRect.Center.X);
                minY = Math.Min(minY, entities[i].WorldRect.Y - entities[i].WorldRect.Height / 2);
                maxY = Math.Max(maxY, entities[i].WorldRect.Y - entities[i].WorldRect.Height / 2);
            }
            Vector2 center = new Vector2((minX + maxX) / 2.0f, (minY + maxY) / 2.0f);
            foreach (MapEntity me in entities)
            {
                me.Move(rect.Location.ToVector2() - center);
            }

            MapEntity.MapLoaded(entities);

#if CLIENT
            MapEntity.SelectedList.Clear();
            MapEntity.SelectedList.AddRange(entities);
#endif
        }

        public void Delete()
        {
            List.Remove(this);
            if (File.Exists(configPath))
            {
                try
                {
                    File.Delete(configPath);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Deleting item assembly \"" + name + "\" failed.", e);
                }
            }
        }

        public static void LoadAll()
        {
            if (GameSettings.VerboseLogging)
            {
                DebugConsole.Log("Loading item assembly prefabs: ");
            }

            string directoryPath = Path.Combine("Content", "Items", "Assemblies");
            if (!Directory.Exists(directoryPath)) return;

            var files = Directory.GetFiles(directoryPath);
            foreach (string file in files)
            {
                new ItemAssemblyPrefab(file);
            }
        }
    }
}
