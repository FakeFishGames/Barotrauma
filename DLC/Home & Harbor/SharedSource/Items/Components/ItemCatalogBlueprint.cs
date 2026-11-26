using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Collections.Immutable;

namespace HomeAndHarbor
{
    public partial class ItemCatalogBlueprint : ItemComponent, IServerSerializable
    {
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier ItemToPlaceIdentifier { get; set; }
        [Serialize(-1, IsPropertySaveable.Yes)]
        public int MaxPerOutpost { get; set; }
        [Serialize(-1, IsPropertySaveable.Yes)]
        public int MaxPerSubmarine { get; set; }
        [Serialize(-1, IsPropertySaveable.Yes)]
        public int MaxPerModule { get; set; }
        [Serialize(-1, IsPropertySaveable.Yes)]
        public int MaxPerHull { get; set; }
        [Serialize(false, IsPropertySaveable.Yes)]
        public bool AttachesToFloor { get; set; }
        [Serialize(false, IsPropertySaveable.Yes)]
        public bool AllowAttachInsideDoors { get; set; }
        [Serialize(false, IsPropertySaveable.No, description: "Can the item be attached to subs/outposts belonging to some other team?")]
        public bool AllowAttachingInOtherTeamsSubs { get; set; }
        [Serialize(true, IsPropertySaveable.Yes)]
        public bool AllowInGameSpriteDepthEditing { get; set; }
        [Serialize(true, IsPropertySaveable.Yes)]
        public bool AllowInGameFlipping { get; set; }
        [Serialize(Holdable.MinDefaultSpriteDepthEdit, IsPropertySaveable.Yes)]
        public float MinSpriteDepth { get; set; }
        [Serialize(Holdable.MaxDefaultSpriteDepthEdit, IsPropertySaveable.Yes)]
        public float MaxSpriteDepth { get; set; }

        /// <summary>
        /// Value of <see cref="DefaultSpriteDepth"/> that makes the blueprint use the sprite depth of the item to place.
        /// </summary>
        public const float UseItemSpriteDepth = -1.0f;

        [Serialize(UseItemSpriteDepth, IsPropertySaveable.Yes, description: "The sprite depth the item is initially placed with (clamped to MinSpriteDepth and MaxSpriteDepth). A negative value uses the sprite depth of the item to place.")]
        public float DefaultSpriteDepth { get; set; }
        [Serialize("", IsPropertySaveable.Yes)]
        public string DisallowAttachingOverTags { get; set; } = string.Empty;
        [Serialize(false, IsPropertySaveable.Yes)]
        public bool WasAttachedBefore { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Makes this blueprint automatically inherit the usual properties that blueprints spawned from the specified item catalog would have. (e.g. MaxPerModule, DisallowAttachingOverTags)")]
        public Identifier InheritCatalogPropertiesFrom { get; set; }

        public ItemCatalogBlueprint(Item item, ContentXElement element) : base(item, element)
        {
        }

        public override void OnItemLoaded()
        {
            SetBlueprintProperties();
        }

#if SERVER
        public override void OnLoadCharacterInventory(Character character)
        {
            Item.CreateServerEvent(this);
        }
#endif

        public void SetBlueprintProperties()
        {
            if (!ItemPrefab.Prefabs.TryGet(ItemToPlaceIdentifier, out ItemPrefab? itemToPlace))
            {
                return;
            }

            Holdable? holdable = Item.GetComponent<Holdable>();

            if (holdable == null)
            {
                DebugConsole.AddWarning($"No holdable component found in the blueprint item {Item.Prefab.Name} when trying to place a {itemToPlace.Name}. Attaching the item will probably not work correctly.");
                return;
            }

            Item.OverrideName = TextManager.GetWithVariable("blueprint", "[itemname]", itemToPlace.Name);
            Item.OverrideDescription = itemToPlace.Description;

            InheritCatalogProperties();

            holdable.Aimable = holdable.Attachable = true;
            holdable.AttachAsIdentifier = itemToPlace.Identifier;
            holdable.AttachTags = ItemCatalog.ReclaimableTag.ToString();
            holdable.MaxAttachedPerOutpost = MaxPerOutpost;
            holdable.MaxAttachedPerOutpostModule = MaxPerModule;
            holdable.MaxAttachedPerSub = MaxPerSubmarine;
            holdable.MaxAttachedPerHull = MaxPerHull;
            holdable.AttachesToFloor = AttachesToFloor;
            holdable.AllowAttachInsideDoors = AllowAttachInsideDoors;
            holdable.AllowInGameSpriteDepthEditing = AllowInGameSpriteDepthEditing;
            holdable.AllowAttachingInOtherTeamsSubs = AllowAttachingInOtherTeamsSubs;
            holdable.AllowInGameFlipping = AllowInGameFlipping;
            holdable.DisallowAttachingOverTags = DisallowAttachingOverTags;
            holdable.MinSpriteDepth = MinSpriteDepth;
            holdable.MaxSpriteDepth = MaxSpriteDepth;
            holdable.Item.SpriteDepth = DefaultSpriteDepth >= 0.0f ? DefaultSpriteDepth : itemToPlace.Sprite.Depth;
            holdable.DisableContainerAlwaysSpawnedItems = WasAttachedBefore;

            holdable.OnAttached += PersistentOutpostData.RefreshFacilityResources;
            //detaching doesn't need to be handled here - we won't be detaching the blueprint item itself, but the item spawned by it.
            //refreshing the resources is handled in the ItemCatalog class, where we handle "reclaiming" the attached blueprint

            item.RectWidth = (int)(itemToPlace.Size.X * itemToPlace.Scale);
            item.RectHeight = (int)(itemToPlace.Size.Y * itemToPlace.Scale);
        }

        private void InheritCatalogProperties()
        {
            if (InheritCatalogPropertiesFrom.IsEmpty)
            {
                return;
            }

            if (!ItemPrefab.Prefabs.TryGet(InheritCatalogPropertiesFrom, out ItemPrefab? catalogItemPrefab))
            {
                DebugConsole.ThrowError($"Failed to find catalog prefab with identifier {InheritCatalogPropertiesFrom}");
                return;
            }

            // Very hacky solution for grabbing the item catalog properties from the catalog item

            var itemCatalogElement = catalogItemPrefab.ConfigElement.Element.Element(nameof(ItemCatalog));

            if (itemCatalogElement == null)
            {
                DebugConsole.ThrowError($"{itemCatalogElement} doesn't appear to be an item catalog: failed to find item component element {nameof(ItemCatalog)}");
                return;
            }

            bool disallowPlacingOverAnyCatalogItemByDefault = itemCatalogElement.GetAttributeBool(nameof(ItemCatalog.DisallowPlacingOverAnyCatalogItemByDefault), false);

            ItemCatalog.CatalogItem? catalogItem = null;

            List<Identifier> itemCatalogItemIdentifiers = [];

            foreach (var categoryElement in itemCatalogElement.Elements("Category"))
            {
                foreach (var catalogElement in categoryElement.Elements(nameof(ItemCatalog.CatalogItem)))
                {
                    Identifier itemIdentifier = catalogElement.GetAttributeIdentifier("identifier", "");
                    itemCatalogItemIdentifiers.Add(itemIdentifier);

                    if (itemIdentifier == ItemToPlaceIdentifier)
                    {
                        catalogItem = new ItemCatalog.CatalogItem(parent: null!, category: null!, new ContentXElement(null, catalogElement));
                    }
                }
            }

            if (catalogItem == null)
            {
                DebugConsole.ThrowError($"Failed to find a matching {nameof(ItemCatalog.CatalogItem)} with identifier {ItemToPlaceIdentifier} in {catalogItemPrefab.Name}");
                return;
            }

            catalogItem.SetBlueprintFields(Item, updateBlueprintComponentProperties: false);

            Identifier[] catalogTags = itemCatalogElement.GetAttributeIdentifierArray(nameof(ItemCatalog.DisallowAttachingOverTags), []);
            DisallowAttachingOverTags = ItemCatalog.GetDisallowAttachingOverTags(catalogItem,
                catalogTags: catalogTags,
                catalogItemIdentifiers: itemCatalogItemIdentifiers,
                disallowPlacingOverAnyCatalogItemByDefault: disallowPlacingOverAnyCatalogItemByDefault);
        }


#if SERVER
        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData)
        {
            msg.WriteIdentifier(ItemToPlaceIdentifier);
            msg.WriteInt32(MaxPerOutpost);
            msg.WriteInt32(MaxPerSubmarine);
            msg.WriteInt32(MaxPerModule);
            msg.WriteInt32(MaxPerHull);
            msg.WriteBoolean(AttachesToFloor);
            msg.WriteBoolean(AllowAttachInsideDoors);
            msg.WriteBoolean(AllowInGameSpriteDepthEditing);
            msg.WriteBoolean(AllowInGameFlipping);
            msg.WriteString(DisallowAttachingOverTags);
        }
#else
        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {   
            ItemToPlaceIdentifier = msg.ReadIdentifier();
            MaxPerOutpost = msg.ReadInt32();
            MaxPerSubmarine = msg.ReadInt32();
            MaxPerModule = msg.ReadInt32();
            MaxPerHull = msg.ReadInt32();
            AttachesToFloor = msg.ReadBoolean();
            AllowAttachInsideDoors = msg.ReadBoolean();
            AllowInGameSpriteDepthEditing = msg.ReadBoolean();
            AllowInGameFlipping = msg.ReadBoolean();
            DisallowAttachingOverTags = msg.ReadString();

            SetBlueprintProperties();
        }
#endif
    }
}