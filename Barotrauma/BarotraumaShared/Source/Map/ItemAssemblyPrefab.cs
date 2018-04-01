using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class ItemAssemblyPrefab : MapEntityPrefab
    {
        private readonly XElement configElement;
        private readonly string configPath;

        public ItemAssemblyPrefab(string filePath)
        {
            configPath = filePath;
            XDocument doc = XMLExtensions.TryLoadXml(filePath);
            if (doc == null || doc.Root == null) return;

            name = doc.Root.GetAttributeString("name", "");
            configElement = doc.Root;

            Category = MapEntityCategory.ItemAssembly;

            SerializableProperty.DeserializeProperties(this, configElement);

            List.Add(this);
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
                minX = Math.Min(minX, entities[i].WorldRect.X);
                maxX = Math.Max(maxX, entities[i].WorldRect.Right);
                minY = Math.Min(minY, entities[i].WorldRect.Y - entities[i].WorldRect.Height);
                maxY = Math.Max(maxY, entities[i].WorldRect.Y);
            }
            Vector2 center = new Vector2((minX + maxX) / 2.0f, (minY + maxY) / 2.0f);
            foreach (MapEntity me in entities)
            {
                me.Move(new Vector2(rect.Center.X, rect.Y - rect.Height / 2) - center);
            }

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
