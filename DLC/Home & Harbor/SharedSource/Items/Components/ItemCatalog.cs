using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Collections.Immutable;

namespace HomeAndHarbor
{
    public partial class ItemCatalog : ItemComponent, IClientSerializable, IServerSerializable
    {
        public partial class CatalogItem
        {
            public ItemPrefab? ItemToPlace => itemToPlace;
            public CatalogCategory Category { get; private set; }

            private readonly ItemCatalog catalog;
            private readonly ItemPrefab? itemToPlace;

            private readonly Sprite? overrideSprite;
            private readonly int cost;
            private readonly Identifier requiredResearch;
            private readonly List<UpgradeResourceCost> resourceCosts = [];
            private readonly int maxPerOutpost, maxPerSubmarine, maxPerModule, maxPerHull;
            private readonly bool attachesToFloor;
            private readonly bool allowAttachInsideDoors;
            private readonly bool allowInGameSpriteDepthEditing, allowInGameFlipping;
            private readonly float maxSpriteDepth, minSpriteDepth;
            private readonly float defaultSpriteDepth;
            public readonly ImmutableHashSet<Identifier> DisallowAttachingOverTags;

            public readonly Identifier OverrideBlueprintItem;

            public CatalogItem(ItemCatalog parent, CatalogCategory category, ContentXElement element)
            {
                catalog = parent;
                Category = category;
                cost = element.GetAttributeInt(nameof(cost), 0);
                maxPerOutpost = element.GetAttributeInt(nameof(maxPerOutpost), -1);
                maxPerModule = element.GetAttributeInt(nameof(maxPerModule), maxPerOutpost);
                maxPerSubmarine = element.GetAttributeInt(nameof(maxPerSubmarine), -1);
                maxPerHull = element.GetAttributeInt(nameof(maxPerHull), -1);
                attachesToFloor = element.GetAttributeBool(nameof(attachesToFloor), false);
                allowAttachInsideDoors = element.GetAttributeBool(nameof(allowAttachInsideDoors), false);
                allowInGameSpriteDepthEditing = element.GetAttributeBool(nameof(allowInGameSpriteDepthEditing), true);
                allowInGameFlipping = element.GetAttributeBool(nameof(allowInGameFlipping), true);
                minSpriteDepth = element.GetAttributeFloat(nameof(minSpriteDepth), Holdable.MinDefaultSpriteDepthEdit);
                maxSpriteDepth = element.GetAttributeFloat(nameof(maxSpriteDepth), Holdable.MaxDefaultSpriteDepthEdit);
                defaultSpriteDepth = element.GetAttributeFloat(nameof(defaultSpriteDepth), ItemCatalogBlueprint.UseItemSpriteDepth);
                itemToPlace = ItemPrefab.Find(name: "", element.GetAttributeIdentifier(nameof(Identifier), def: ""));
                DisallowAttachingOverTags = element.GetAttributeIdentifierImmutableHashSet(nameof(DisallowAttachingOverTags), ImmutableHashSet<Identifier>.Empty);
                OverrideBlueprintItem = element.GetAttributeIdentifier(nameof(OverrideBlueprintItem), def: "");

                if (element.ContentPackage != null)
                {
                    foreach (ContentXElement subElement in element.Elements())
                    {
                        switch (subElement.Name.ToString().ToLowerInvariant())
                        {
                            case "sprite":
                                overrideSprite = new Sprite(subElement);
                                break;
                            case "resourcecost" or "requireditem":
                                resourceCosts.Add(new UpgradeResourceCost(subElement));
                                break;
                            case "requiredresearch":
                                requiredResearch = subElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                                break;
                        }
                    }
                }
            }

            /// <summary>
            /// Modifies the instantiated blueprint item to make it place the item the user selected on the catalog
            /// </summary>
            /// <param name="blueprint"></param>
            public void SetBlueprintFields(Item blueprint, bool updateBlueprintComponentProperties = true)
            {
                if (itemToPlace == null) { return; }

                if (blueprint.GetComponent<ItemCatalogBlueprint>() is not ItemCatalogBlueprint blueprintComponent)
                {
                    DebugConsole.AddWarning($"No ItemCatalogBlueprint component found in the blueprint item {blueprint.Prefab.Name} when trying to place a {itemToPlace.Name}. Attaching the item will probably not work correctly.");
                    return;
                }

                blueprintComponent.ItemToPlaceIdentifier = itemToPlace.Identifier;
                blueprintComponent.MaxPerOutpost = maxPerOutpost;
                blueprintComponent.MaxPerModule = maxPerModule;
                blueprintComponent.MaxPerSubmarine = maxPerSubmarine;
                blueprintComponent.MaxPerHull = maxPerHull;
                blueprintComponent.AttachesToFloor = attachesToFloor;
                blueprintComponent.AllowAttachInsideDoors = allowAttachInsideDoors;
                blueprintComponent.AllowInGameSpriteDepthEditing = allowInGameSpriteDepthEditing;
                blueprintComponent.AllowInGameFlipping = allowInGameFlipping;
                if (catalog != null)
                {
                    blueprintComponent.DisallowAttachingOverTags = GetDisallowAttachingOverTags(this,
                        catalogTags: catalog.disallowAttachingOverTags,
                        catalogItemIdentifiers: catalog.catalogPrefabs.Select(x => x.Identifier),
                        disallowPlacingOverAnyCatalogItemByDefault: catalog.DisallowPlacingOverAnyCatalogItemByDefault);
                }
                blueprintComponent.MinSpriteDepth = minSpriteDepth;
                blueprintComponent.MaxSpriteDepth = maxSpriteDepth;
                blueprintComponent.DefaultSpriteDepth = defaultSpriteDepth;

                blueprint.RectWidth = (int)(itemToPlace.Size.X * itemToPlace.Scale);
                blueprint.RectHeight = (int)(itemToPlace.Size.Y * itemToPlace.Scale);

                if (updateBlueprintComponentProperties)
                {
                    blueprintComponent.SetBlueprintProperties();
                }
            }

            public bool IsResearched()
            {
                if (requiredResearch.IsEmpty) { return true; }

                if (GameMain.GameSession?.Campaign == null ||
                    !ResearchManager.TryGetResearchManagerForCampaign(GameMain.GameSession.Campaign, out var researchManager))
                {
                    return true;
                }

                return researchManager.IsFinished(requiredResearch);
            }

            public bool HasResources(Character character, Client? client = null, IEnumerable<Item>? availableResources = null)
            {
                if (!UpgradeResourceCost.HasResources(resourceCosts, character, availableResources: availableResources)) { return false; }
                if (GameMain.GameSession?.Campaign is not { } campaign) { return true; }

                return campaign.CanAfford(cost, client);
            }

            public bool CanBePurchased(Character character, Client? client = null, IEnumerable<Item>? availableResources = null)
            {
                return IsResearched() && HasResources(character, client, availableResources);
            }

            public void Purchase(Character character, Client? client = null)
            {
                if (!UpgradeResourceCost.TryTakeResources(resourceCosts, character)) { return; }

                if (GameMain.GameSession?.Campaign is { } campaign && !campaign.TryPurchase(client, cost))
                {
                    return;
                }

#if CLIENT
                //disable all buy buttons
                catalog.catalogItems.ForEach(catalogueItem => catalogueItem.SetBuyButtonEnabled(false));

                //force an update to refresh the state of the purchase buttons in case this purchase uses up materials/money and makes some item unavailable 
                //do this with a brief delay because removing the materials isn't processed until the next frame, and in MP there's always some delay
                CoroutineManager.Invoke(() => { catalog.forceRefreshAllCatalogItems = true; }, delay: 1.0f);               
#endif

                ItemPrefab.Prefabs.TryGet(OverrideBlueprintItem, out ItemPrefab? overrideBlueprintPrefab);

                Entity.Spawner.AddItemToSpawnQueue(overrideBlueprintPrefab ?? catalog.blueprintPrefab, character.Inventory, onSpawned: (Item instantiatedItem) =>
                {
                    SetBlueprintFields(instantiatedItem);
#if SERVER
                    catalog.Item.CreateServerEvent(catalog, new CatalogEventDataServer(EventType.Purchase, this, instantiatedItem));
#endif
                });
            }

            public void Remove()
            {
                overrideSprite?.Remove();
                resourceCosts.Clear();
            }
        }

        public class CatalogCategory
        {
            public readonly Identifier Identifier;
            public ImmutableArray<CatalogItem> CatalogItems;

            public CatalogCategory(ItemCatalog itemCatalog, ContentXElement element)
            {
                List<CatalogItem> items = new List<CatalogItem>();

                Identifier = element.GetAttributeIdentifier("identifier", Identifier.Empty);

                foreach (ContentXElement subElement in element.Elements())
                {
                    switch (subElement.Name.ToString().ToLowerInvariant())
                    {
                        case "catalogitem":
                            CatalogItem catalogItem = new CatalogItem(itemCatalog, this, subElement);
                            items.Add(catalogItem);
                            break;
                    }
                }

                CatalogItems = items.ToImmutableArray();
            }
        }

        public static readonly Identifier ReclaimableTag = "detachable".ToIdentifier();

        private const float ReclaimTime = 5;

        [Serialize(true, IsPropertySaveable.No, description: "Should the catalog item be removed if it's not in an outpost level or if it ends up outside the player inventory?")]
        public bool RemoveIfNotInPlayerInventoryOrOutpostLevel { get; set; }

        [Serialize(true, IsPropertySaveable.No, description: "Should the catalog item be removed if it's not in the level it originally spawned in?")]
        public bool RemoveIfLevelChanged { get; set; }

        [Serialize("", IsPropertySaveable.Yes)]
        public string OriginalLevelSeed { get; set; } = string.Empty;

        [Serialize(false, IsPropertySaveable.No, description: $"Sets {nameof(CatalogItem.DisallowAttachingOverTags)} to the identifier of every catalog item by default.")]
        public bool DisallowPlacingOverAnyCatalogItemByDefault { get; set; }

        private ImmutableHashSet<Identifier> disallowAttachingOverTags = ImmutableHashSet<Identifier>.Empty;

        [Serialize("", IsPropertySaveable.No, description: $"Tags or identifiers of items none of the items in this catalog can be attached over. " +
            $"Combined with the item-specific {nameof(CatalogItem.DisallowAttachingOverTags)} (or the identifiers of every catalog item, if {nameof(DisallowPlacingOverAnyCatalogItemByDefault)} is enabled).")]
        public string DisallowAttachingOverTags
        {
            get => disallowAttachingOverTags.ConvertToString();
            set => disallowAttachingOverTags = value.ToIdentifiers().ToImmutableHashSet();
        }

        /// <summary>
        /// Combines the catalog-level tags with either the item-specific tags,
        /// or the identifiers of every catalog item if <paramref name="disallowPlacingOverAnyCatalogItemByDefault"/> is enabled and the item doesn't define its own tags.
        /// </summary>
        public static string GetDisallowAttachingOverTags(CatalogItem catalogItem, IEnumerable<Identifier> catalogTags, IEnumerable<Identifier> catalogItemIdentifiers, bool disallowPlacingOverAnyCatalogItemByDefault)
        {
            IEnumerable<Identifier> itemTags = disallowPlacingOverAnyCatalogItemByDefault && catalogItem.DisallowAttachingOverTags.IsEmpty ?
                catalogItemIdentifiers :
                catalogItem.DisallowAttachingOverTags;
            return catalogTags.Union(itemTags).ConvertToString();
        }

        private readonly ItemPrefab blueprintPrefab;
        private readonly List<CatalogCategory> categories = [];
        private readonly List<CatalogItem> catalogItems = [];
        private readonly HashSet<ItemPrefab> catalogPrefabs = []; // fast access to check if a prefab belongs to the catalog

        private bool reclaiming = false;
        private Character? reclaimingUser;
        private Item? reclaimingTarget;
        private float reclaimingTime;

        public ItemCatalog(Item item, ContentXElement element) : base(item, element)
        {
            IsActive = true;

            blueprintPrefab = ItemPrefab.Find(null, element.GetAttributeIdentifier("blueprintitem", def: "blueprint"));
            foreach (ContentXElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "category":
                        CatalogCategory category = new CatalogCategory(this, subElement);
                        catalogItems.AddRange(category.CatalogItems);
                        category.CatalogItems.ForEach(c => { if (c.ItemToPlace != null) { catalogPrefabs.Add(c.ItemToPlace); } });
                        categories.Add(category);
                        break;
                }
            }

            InitProjectSpecific(element);
        }

        public override void OnItemLoaded()
        {
            if (OriginalLevelSeed.IsNullOrEmpty())
            {
                OriginalLevelSeed = Level.Loaded?.Seed ?? string.Empty;
            }
        }

        partial void InitProjectSpecific(ContentXElement element);

        protected override void RemoveComponentSpecific()
        {
            catalogItems.ForEach(static catalogItem => catalogItem.Remove());
            base.RemoveComponentSpecific();
        }

        private void StartReclaiming(Item item, Character character)
        {
            reclaimingTarget = item;
            reclaimingUser = character;
            reclaimingTime = ReclaimTime;

            reclaiming = true;

#if SERVER
            Item.CreateServerEvent(this, new CatalogEventDataServer(EventType.StartReclaim));
#endif
        }

        private void Reclaiming(float deltaTime)
        {
            if (!reclaiming || reclaimingUser == null || reclaimingTarget == null)
            {
                return;
            }

            if (reclaimingUser.IsAnySelectedItem(reclaimingTarget) ||
                reclaimingUser.IsKeyDown(InputType.Aim) ||
                !reclaimingUser.CanInteractWith(reclaimingTarget) ||
                reclaimingTarget.Removed)
            {
                StopReclaiming();
                return;
            }

            reclaimingTime -= deltaTime;

            reclaimingUser.AnimController.UpdateUseItem(!reclaimingUser.IsClimbing, reclaimingTarget.WorldPosition, handMoveAmount: Vector2.Zero);

#if CLIENT
            Character.Controlled?.UpdateHUDProgressBar(
                this,
                reclaimingTarget.WorldPosition,
                reclaimingTime / ReclaimTime,
                GUIStyle.Red, GUIStyle.Green,
                "progressbar.deattaching");
#endif

            if (reclaimingTime <= 0)
            {
                StopReclaiming();
            }
        }

        private void StopReclaiming()
        {
            if (!reclaiming || reclaimingUser == null || reclaimingTarget == null)
            {
                return;
            }

            if (reclaimingTime <= 0)
            {
                CatalogItem? matchingCatalog = catalogItems.FirstOrDefault(c => c.ItemToPlace == reclaimingTarget.Prefab);

                if (matchingCatalog == null)
                {
                    DebugConsole.ThrowError($"Failed to find the matching CatalogItem for {reclaimingTarget}.");
                    return;
                }

                ItemPrefab.Prefabs.TryGet(matchingCatalog.OverrideBlueprintItem, out ItemPrefab? overrideBlueprintPrefab);

                Entity.Spawner.AddItemToSpawnQueue(overrideBlueprintPrefab ?? blueprintPrefab, reclaimingUser.Inventory, onSpawned: (Item instantiatedItem) =>
                {

                    if (instantiatedItem.GetComponent<ItemCatalogBlueprint>() is { } blueprintComponent)
                    {
                        blueprintComponent.WasAttachedBefore = true;
                    }

                    matchingCatalog.SetBlueprintFields(instantiatedItem);
                    
#if SERVER
                    Item.CreateServerEvent(this, new CatalogEventDataServer(EventType.Purchase, matchingCatalog, instantiatedItem));
#endif
                });

                if (reclaimingTarget.OwnInventory != null)
                {
                    foreach (Item item in reclaimingTarget.OwnInventory.AllItemsMod)
                    {
                        item.Drop(null);
                    }
                }

                Entity.Spawner.AddEntityToRemoveQueue(reclaimingTarget, onRemoved: (_) =>
                {
                    PersistentOutpostData.RefreshFacilityResources();
#if SERVER
                    Item.CreateServerEvent(this, new CatalogEventDataServer(EventType.ReclaimFinished));
#endif
                });
            }

            reclaiming = false;
        }


        /// <summary>
        /// Can the item be reclaimed by the catalog? I.e. "uninstalled", turning the item back to a blueprint.
        /// </summary>
        public bool CanBeReclaimed(Item item)
        {
            return catalogPrefabs.Contains(item.Prefab) && item.HasTag(ReclaimableTag);
        }

        public override bool BypassNonInteractableItem(Character character, Item item)
        {
            return CanBeReclaimed(item);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            // Only do this in campaign so the catalog can still be spawned and used in other gamemodes
            if (GameMain.GameSession?.Campaign != null)
            {
                if (RemoveIfNotInPlayerInventoryOrOutpostLevel &&
                    // Remove item if it's outside the player inventory
                    ((item.ParentInventory == null || item.ParentInventory.Owner is not Character) ||
                    // Or if it's outside an outpost level
                    (GameMain.GameSession?.Level?.Type != LevelData.LevelType.Outpost)))
                {
                    Entity.Spawner.AddEntityToRemoveQueue(Item);
                    return;
                }
                if (RemoveIfLevelChanged && 
                    !OriginalLevelSeed.IsNullOrWhiteSpace() &&
                    Level.Loaded?.Seed != OriginalLevelSeed)
                {
                    Entity.Spawner.AddEntityToRemoveQueue(Item);
                    return;

                }
            }

            Reclaiming(deltaTime);

            base.Update(deltaTime, cam);
        }

        public override bool Use(float deltaTime, Character? character = null)
        {
            if (reclaiming) { return false; }
            if (character == null) { return false; }

            Item? selectedItem = character.FocusedItem;

            if (selectedItem == null || selectedItem.Removed) { return false; }

            if (selectedItem.GetComponent<Holdable>() is { } holdable)
            {
                if (holdable.CanBeDeattached())
                {
                    // This item is already detachable, let's just call Use so our character starts detaching it
                    holdable.Use(deltaTime, character);

                    return false;
                }
            }

            if (!CanBeReclaimed(selectedItem))
            {
#if CLIENT
                if (character == Character.Controlled)
                {
                    GUI.AddMessage(TextManager.Get("catalog.detachfailure"), Color.Red);
                }
#endif
                return false;
            }

            StartReclaiming(selectedItem, character);

            return true;
        }

        public override bool HasRequiredItems(Character character, bool addMessage, LocalizedString? msg = null)
        {
            Item? selectedItem = character.FocusedItem;
            if (selectedItem == null || !CanBeReclaimed(selectedItem))
            {
                return false;
            }

            return base.HasRequiredItems(character, addMessage, msg);
        }

        #region Networking
        private enum EventType
        {
            Purchase,
            StartReclaim,
            ReclaimFinished
        }

        private readonly struct CatalogEventDataServer : IEventData
        {
            public readonly EventType EventType;
            public readonly CatalogItem? CatalogItem;
            public readonly Item? InstantiatedItem;

            public CatalogEventDataServer(EventType eventType)
            {
                EventType = eventType;
            }

            public CatalogEventDataServer(EventType eventType, CatalogItem catalogItem, Item instantiatedItem)
            {
                EventType = eventType;
                CatalogItem = catalogItem;
                InstantiatedItem = instantiatedItem;
            }
        }

        private readonly record struct CatalogEventDataClient(CatalogItem CatalogItem) : IEventData;
        #endregion
    }
}
