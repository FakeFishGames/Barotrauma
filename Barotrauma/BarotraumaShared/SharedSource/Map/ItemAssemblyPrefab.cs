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
        private readonly string name;
        public override string Name { get { return name; } }

        public static readonly PrefabCollection<ItemAssemblyPrefab> Prefabs = new PrefabCollection<ItemAssemblyPrefab>();

        public static readonly string VanillaSaveFolder = Path.Combine("Content", "Items", "Assemblies");
        public static readonly string SaveFolder = "ItemAssemblies";

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

        public ItemAssemblyPrefab(string filePath, bool allowOverwrite = false)
        {
            FilePath = filePath;
            XDocument doc = XMLExtensions.TryLoadXml(filePath);
            if (doc == null) { return; }

            XElement element = doc.Root;
            if (element.IsOverride())
            {
                element = element.Elements().First();
            }

            originalName = element.GetAttributeString("name", "");
            identifier = element.GetAttributeString("identifier", null) ?? originalName.ToLowerInvariant().Replace(" ", "");
            configElement = element;

            Category = MapEntityCategory.ItemAssembly;

            SerializableProperty.DeserializeProperties(this, configElement);

            name = TextManager.Get("EntityName." + identifier, returnNull: true) ?? originalName;
            Description = TextManager.Get("EntityDescription." + identifier, returnNull: true) ?? Description;

            List<ushort> containedItemIDs = new List<ushort>();
            foreach (XElement entityElement in element.Elements())
            {
                var containerElement = entityElement.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("itemcontainer", StringComparison.OrdinalIgnoreCase));
                if (containerElement == null) { continue; }

                string containedString = containerElement.GetAttributeString("contained", "");
                string[] itemIdStrings = containedString.Split(',');
                var itemIds = new List<ushort>[itemIdStrings.Length];
                for (int i = 0; i < itemIdStrings.Length; i++)
                {
                    itemIds[i] ??= new List<ushort>();
                    foreach (string idStr in itemIdStrings[i].Split(';'))
                    {
                        if (int.TryParse(idStr, out int id)) 
                        { 
                            itemIds[i].Add((ushort)id);
                            containedItemIDs.Add((ushort)id);
                        }                        
                    }
                }
            }

            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            DisplayEntities = new List<Pair<MapEntityPrefab, Rectangle>>();
            foreach (XElement entityElement in element.Elements())
            {
                ushort id = (ushort)entityElement.GetAttributeInt("ID", 0);
                if (id > 0 && containedItemIDs.Contains(id)) { continue; }

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

            if (allowOverwrite && Prefabs.ContainsKey(identifier))
            {
                Prefabs.Remove(Prefabs[identifier]);
            }
            Prefabs.Add(this, doc.Root.IsOverride());
        }

        public static void Remove(string filePath)
        {
            Prefabs.RemoveByFile(filePath);
        }
        
        protected override void CreateInstance(Rectangle rect)
        {
#if CLIENT
            var loaded = CreateInstance(rect.Location.ToVector2(), Submarine.MainSub, selectInstance: Screen.Selected == GameMain.SubEditorScreen);
            if (Screen.Selected is SubEditorScreen)
            {
                SubEditorScreen.StoreCommand(new AddOrDeleteCommand(loaded, false, handleInventoryBehavior: false));
            }
#else
            var loaded = CreateInstance(rect.Location.ToVector2(), Submarine.MainSub);
#endif
        }

        public List<MapEntity> CreateInstance(Vector2 position, Submarine sub, bool selectInstance = false)
        {
            return PasteEntities(position, sub, configElement, FilePath, selectInstance);
        }

        public static List<MapEntity> PasteEntities(Vector2 position, Submarine sub, XElement configElement, string filePath = null, bool selectInstance = false)
        {
            int idOffset = Entity.FindFreeIdBlock(configElement.Elements().Count());
            List<MapEntity> entities = MapEntity.LoadAll(sub, configElement, filePath, idOffset);
            if (entities.Count == 0) { return entities; }

            Vector2 offset = sub?.HiddenSubPosition ?? Vector2.Zero;

            foreach (MapEntity me in entities)
            {
                me.Move(position);
                me.Submarine = sub;
                if (!(me is Item item)) { continue; }
                Wire wire = item.GetComponent<Wire>();
                //Vector2 subPosition = Submarine == null ? Vector2.Zero : Submarine.HiddenSubPosition;
                if (wire != null) 
                { 
                    //fix wires that have been erroneously saved at the "hidden position"
                    if (sub != null && Vector2.Distance(me.Position, sub.HiddenSubPosition) > sub.HiddenSubPosition.Length() / 2)
                    {
                        me.Move(position);
                    }
                    wire.MoveNodes(position - offset); 
                }
            }

            MapEntity.MapLoaded(entities, true);
#if CLIENT
            if (Screen.Selected == GameMain.SubEditorScreen && selectInstance)
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

            //find assembly files in the item assembly folders
            if (Directory.Exists(VanillaSaveFolder))
            {
                itemAssemblyFiles.AddRange(Directory.GetFiles(VanillaSaveFolder));
            }
            if (Directory.Exists(SaveFolder))
            {
                itemAssemblyFiles.AddRange(Directory.GetFiles(SaveFolder));
            }

            //find assembly files in selected content packages
            foreach (ContentPackage cp in GameMain.Config.AllEnabledPackages)
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
