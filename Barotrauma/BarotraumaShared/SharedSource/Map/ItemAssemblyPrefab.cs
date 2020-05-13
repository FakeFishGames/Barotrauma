using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class ItemAssemblyPrefab : MapEntityPrefab
    {
        private string name;
        public override string Name { get { return name; } }

        public static readonly PrefabCollection<ItemAssemblyPrefab> Prefabs = new PrefabCollection<ItemAssemblyPrefab>();

        private bool disposed = false;
        public override void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            Prefabs.Remove(this);
        }

        private readonly XElement configElement;
        
        public List<Pair<MapEntityPrefab, Rectangle>> DisplayEntities
        {
            get;
            private set;
        }

        public Rectangle Bounds;

        public ItemAssemblyPrefab(string filePath)
        {
            FilePath = filePath;
            XDocument doc = XMLExtensions.TryLoadXml(filePath);
            if (doc == null) { return; }

            originalName = doc.Root.GetAttributeString("name", "");
            identifier = doc.Root.GetAttributeString("identifier", null) ?? originalName.ToLowerInvariant().Replace(" ", "");
            configElement = doc.Root;

            Category = MapEntityCategory.ItemAssembly;

            SerializableProperty.DeserializeProperties(this, configElement);

            name = TextManager.Get("EntityName." + identifier, returnNull: true) ?? originalName;
            Description = TextManager.Get("EntityDescription." + identifier, returnNull: true) ?? Description;

            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            DisplayEntities = new List<Pair<MapEntityPrefab, Rectangle>>();
            foreach (XElement entityElement in doc.Root.Elements())
            {
                string identifier = entityElement.GetAttributeString("identifier", entityElement.Name.ToString().ToLowerInvariant());
                MapEntityPrefab mapEntity = List.FirstOrDefault(p => p.Identifier == identifier);
                if (mapEntity == null)
                {
                    string entityName = entityElement.GetAttributeString("name", "");
                    mapEntity = List.FirstOrDefault(p => p.Name == entityName);
                }

                Rectangle rect = entityElement.GetAttributeRect("rect", Rectangle.Empty);
                if (mapEntity != null && !entityElement.Elements().Any(e => e.Name.LocalName.Equals("wire", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!entityElement.GetAttributeBool("hideinassemblypreview", false)) { DisplayEntities.Add(new Pair<MapEntityPrefab, Rectangle>(mapEntity, rect)); }
                    minX = Math.Min(minX, rect.X);
                    minY = Math.Min(minY, rect.Y - rect.Height);
                    maxX = Math.Max(maxX, rect.Right);
                    maxY = Math.Max(maxY, rect.Y);
                }
            }

            Bounds = minX == int.MaxValue ?
                new Rectangle(0, 0, 1, 1) :
                new Rectangle(minX, minY, maxX - minX, maxY - minY);

            Prefabs.Add(this, false);
        }

        public static void Remove(string filePath)
        {
            Prefabs.RemoveByFile(filePath);
        }
        
        protected override void CreateInstance(Rectangle rect)
        {
            CreateInstance(rect.Location.ToVector2(), Submarine.MainSub);
        }

        public List<MapEntity> CreateInstance(Vector2 position, Submarine sub, bool selectPrefabs = false)
        {
            List<MapEntity> entities = MapEntity.LoadAll(sub, configElement, FilePath);
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
            if (Screen.Selected == GameMain.SubEditorScreen && selectPrefabs)
            {
                MapEntity.SelectedList.Clear();
                entities.ForEach(MapEntity.AddSelection);
            }
#endif   
            return entities;

        }
        
        public void Delete()
        {
            Dispose();
            if (File.Exists(FilePath))
            {
                try
                {
                    File.Delete(FilePath);
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

            List<string> itemAssemblyFiles = new List<string>();

            //find assembly files in the item assembly folder
            string directoryPath = Path.Combine("Content", "Items", "Assemblies");
            if (Directory.Exists(directoryPath))
            {
                itemAssemblyFiles.AddRange(Directory.GetFiles(directoryPath));
            }

            //find assembly files in selected content packages
            foreach (ContentPackage cp in GameMain.Config.SelectedContentPackages)
            {
                foreach (string filePath in cp.GetFilesOfType(ContentType.ItemAssembly))
                {
                    //ignore files that have already been added (= file saved to item assembly folder)
                    if (itemAssemblyFiles.Any(f => Path.GetFullPath(f) == Path.GetFullPath(filePath))) { continue; }
                    itemAssemblyFiles.Add(filePath);
                }
            }

            foreach (string file in itemAssemblyFiles)
            {
                new ItemAssemblyPrefab(file);
            }
        }
    }
}
