using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Immutable;
using System.Net;

namespace Barotrauma
{
    #warning TODO: MapEntityPrefab should be constrained further to not include item assemblies, as assemblies are effectively not entities at all
    partial class ItemAssemblyPrefab : MapEntityPrefab
    {
        public static readonly PrefabCollection<ItemAssemblyPrefab> Prefabs = new PrefabCollection<ItemAssemblyPrefab>();

        private readonly XElement configElement;

        public readonly ImmutableArray<(Identifier Identifier, Rectangle Rect)> DisplayEntities;

        public readonly Rectangle Bounds;

        public override LocalizedString Name { get; }

        public override Sprite Sprite => null;

        public override string OriginalName => Name.Value;

        public override ImmutableHashSet<Identifier> Tags { get; }

        public override ImmutableHashSet<Identifier> AllowedLinks => null;

        public override MapEntityCategory Category => MapEntityCategory.ItemAssembly;

        public override ImmutableHashSet<string> Aliases => null;

        protected override Identifier DetermineIdentifier(XElement element)
        {
            return element.GetAttributeIdentifier("identifier", element.GetAttributeIdentifier("name", ""));
        }

        public ItemAssemblyPrefab(ContentXElement element, ItemAssemblyFile file) : base(element, file)
        {
            configElement = element;

            SerializableProperty.DeserializeProperties(this, configElement);

            Name = TextManager.Get($"EntityName.{Identifier}").Fallback(element.GetAttributeString("name", ""));
            Description = TextManager.Get($"EntityDescription.{Identifier}");
            Tags = Enumerable.Empty<Identifier>().ToImmutableHashSet();

            string description = element.GetAttributeString("description", string.Empty);
            if (!description.IsNullOrEmpty())
            {
                Description = Description.Fallback(description);
            }

            List<ushort> containedItemIDs = new List<ushort>();
            foreach (XElement entityElement in element.Elements())
            {
                var containerElement = entityElement.GetChildElement("itemcontainer");
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
            var displayEntities = new List<(Identifier, Rectangle)>();
            foreach (XElement entityElement in element.Elements())
            {
                ushort id = (ushort)entityElement.GetAttributeInt("ID", 0);
                if (id > 0 && containedItemIDs.Contains(id)) { continue; }

                Identifier identifier = entityElement.GetAttributeIdentifier("identifier", entityElement.Name.ToString().ToLowerInvariant());

                Rectangle rect = entityElement.GetAttributeRect("rect", Rectangle.Empty);
                if (!entityElement.Elements().Any(e => e.Name.LocalName.Equals("wire", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!entityElement.GetAttributeBool("hideinassemblypreview", false)) { displayEntities.Add((identifier, rect)); }
                    minX = Math.Min(minX, rect.X);
                    minY = Math.Min(minY, rect.Y - rect.Height);
                    maxX = Math.Max(maxX, rect.Right);
                    maxY = Math.Max(maxY, rect.Y);
                }
            }
            DisplayEntities = displayEntities.ToImmutableArray();

            Bounds = minX == int.MaxValue ?
                new Rectangle(0, 0, 1, 1) :
                new Rectangle(minX, minY, maxX - minX, maxY - minY);
        }

        protected override void CreateInstance(Rectangle rect)
        {
#if CLIENT
            var loaded = CreateInstance(rect.Location.ToVector2(), Submarine.MainSub, selectInstance: Screen.Selected == GameMain.SubEditorScreen);
            if (Screen.Selected is SubEditorScreen && loaded.Any())
            {
                SubEditorScreen.StoreCommand(new AddOrDeleteCommand(loaded, false, handleInventoryBehavior: false));
            }
#else
            var loaded = CreateInstance(rect.Location.ToVector2(), Submarine.MainSub);
#endif
        }

        public List<MapEntity> CreateInstance(Vector2 position, Submarine sub, bool selectInstance = false)
        {
            return PasteEntities(position, sub, configElement, ContentFile.Path.Value, selectInstance);
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
                if (me is not Item item) { continue; }
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
            Prefabs.Remove(this);
            try
            {
                if (ContentPackage is { Files: { Length: 1 } }
                    && ContentPackageManager.LocalPackages.Contains(ContentPackage))
                {
                    Directory.Delete(ContentPackage.Dir, recursive: true);
                    ContentPackageManager.LocalPackages.Refresh();
                    ContentPackageManager.EnabledPackages.DisableRemovedMods();
                }
            }
            catch (Exception e)
            {
                DebugConsole.ThrowError("Deleting item assembly \"" + Name + "\" failed.", e);
            }
        }

        public override void Dispose() { }
    }
}
