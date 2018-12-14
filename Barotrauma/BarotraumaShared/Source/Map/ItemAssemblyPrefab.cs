using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    class ItemAssemblyPrefab : MapEntityPrefab
    {
        private readonly XElement configElement;
        private readonly string configPath;
        
        public List<Pair<MapEntityPrefab, Rectangle>> Entities
        {
            get;
            private set;
        }

        public ItemAssemblyPrefab(string filePath)
        {
            configPath = filePath;
            XDocument doc = XMLExtensions.TryLoadXml(filePath);
            if (doc == null || doc.Root == null) return;
            
            name = doc.Root.GetAttributeString("name", "");
            if (doc.Root.GetAttributeString("identifier", null) == null)
            {
                DebugConsole.NewMessage(name.ToLowerInvariant().Replace(" ", ""), Color.Yellow);
            }
            identifier = doc.Root.GetAttributeString("identifier", null) ?? name.ToLowerInvariant().Replace(" ", "");
            configElement = doc.Root;

            Category = MapEntityCategory.ItemAssembly;

            SerializableProperty.DeserializeProperties(this, configElement);

            Entities = new List<Pair<MapEntityPrefab, Rectangle>>();
            foreach (XElement entityElement in doc.Root.Elements())
            {
                string entityName = entityElement.GetAttributeString("name", "");
                MapEntityPrefab mapEntity = List.Find(p => p.Name == entityName);
                if (mapEntity != null) Entities.Add(new Pair<MapEntityPrefab,Rectangle>(mapEntity, entityElement.GetAttributeRect("rect", Rectangle.Empty)));
            }
            
            List.Add(this);
        }
        
        protected override void CreateInstance(Rectangle rect)
        {
            CreateInstance(rect.Location.ToVector2(), Submarine.MainSub);
        }

        public List<MapEntity> CreateInstance(Vector2 position, Submarine sub)
        {
            List<MapEntity> entities = MapEntity.LoadAll(sub, configElement, configPath);
            if (entities.Count == 0) return entities;

            Vector2 offset = sub == null ? Vector2.Zero : sub.HiddenSubPosition;

            foreach (MapEntity me in entities)
            {
                me.Move(position);
                Item item = me as Item;
                if (item == null) continue;
                Wire wire = item.GetComponent<Wire>();
                if (wire != null) wire.MoveNodes(position - offset);
            }

            MapEntity.MapLoaded(entities, true);
#if CLIENT
            if (Screen.Selected == GameMain.SubEditorScreen)
            {
                MapEntity.SelectedList.Clear();
                MapEntity.SelectedList.AddRange(entities);
            }
#endif   
            return entities;

        }

#if CLIENT
        public override void DrawPlacing(SpriteBatch spriteBatch, Camera cam, Rectangle? placeRect = null)
        {
            base.DrawPlacing(spriteBatch, cam);
            foreach (Pair<MapEntityPrefab, Rectangle> entity in Entities)
            {
                Rectangle drawRect = entity.Second;
                drawRect.Location += Submarine.MouseToWorldGrid(cam, Submarine.MainSub).ToPoint();
                entity.First.DrawPlacing(spriteBatch, cam, drawRect);
            }
        }

        public static XElement Save(List<MapEntity> entities, string name, string description)
        {
            XElement element = new XElement("ItemAssembly",
                new XAttribute("name", name),
                new XAttribute("description", description));

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
#endif
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
