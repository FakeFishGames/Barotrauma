using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HomeAndHarbor
{
    public partial class ItemCatalog : ItemComponent
    {
        public partial class CatalogItem
        {
            private class ResourceElement
            {
                public readonly GUIImage Icon;
                public readonly GUITextBlock TextBlock;

                public ResourceElement(GUIImage icon, GUITextBlock text)
                {
                    Icon = icon;
                    TextBlock = text;
                }
            }


            private readonly Dictionary<ApplicableResourceCollection, ResourceElement> resourceImages = [];

            private GUIFrame? frame;
            private GUIFrame? unresearchedFrame;
            private GUITextBlock? costTextBlock;
            private GUIButton? buyButton;
            private GUITextBlock? restrictionsText;

            private bool updatedAtLeastOnce = false;

            public void CreateGUI(GUIListBox list)
            {
                if (itemToPlace == null) { return; }

                frame = new GUIFrame(new RectTransform(new Vector2(1f, 0.2f), list.Content.RectTransform), style: "InnerFrameDark", new Color(200, 200, 200)) { UserData = this };

                GUILayoutGroup verticalLayout = new(new RectTransform(new Vector2(0.95f, 0.9f), frame.RectTransform, Anchor.Center)) { Stretch = true };

                GUILayoutGroup horizontalLayout = new(new RectTransform(new Vector2(1f, 1f), verticalLayout.RectTransform, Anchor.Center), isHorizontal: true, Anchor.CenterLeft);

                GUIFrame previewFrame = new(new RectTransform(new Vector2(0.25f, 1f), horizontalLayout.RectTransform), style: null);
                Sprite previewSprite = overrideSprite ?? itemToPlace.InventoryIcon ?? itemToPlace.Sprite;
                GUIImage preview = new(new RectTransform(Vector2.One * 0.9f, previewFrame.RectTransform, Anchor.Center), previewSprite, scaleToFit: true);
                preview.RectTransform.MaxSize = previewSprite.SourceRect.Size;

                GUILayoutGroup rightSide = new(new RectTransform(new Vector2(0.75f, 1f), horizontalLayout.RectTransform)) 
                { 
                    Stretch = true
                };

                GUITextBlock itemName = new(new RectTransform(Vector2.UnitX, rightSide.RectTransform), itemToPlace.Name, font: GUIStyle.SubHeadingFont)
                {
                    OverflowClip = true,
                    Padding = Vector4.Zero
                };
                GUITextBlock description = new(new RectTransform(Vector2.UnitX, rightSide.RectTransform), itemToPlace.Description, textAlignment: Alignment.TopLeft, wrap: true)
                {
                    Padding = Vector4.Zero
                };

                GUIListBox materials = new(new RectTransform(new Vector2(1f, 0.4f), rightSide.RectTransform, Anchor.BottomLeft), isHorizontal: true, style: null)
                {
                    CurrentSelectMode = GUIListBox.SelectMode.None,
                    CanBeFocused = false,
                    Spacing = 15
                };

                // Clamp the mouse rect to the parent list box, since this list box is nested inside another one, using its own bounds would result in incorrect mouse clamping
                materials.Content.ClampMouseRectToGUIComponent = list; materials.Content.ClampMouseRectToGUIComponent = list;

                if (cost > 0)
                {
                    // Should "mk" be localised?
                    costTextBlock = new(new RectTransform(Vector2.UnitY, materials.Content.RectTransform), TextManager.GetWithVariable("currencyformat", "[credits]", cost.ToString()), textAlignment: Alignment.Center, textColor: Color.Green)
                    {
                        Padding = Vector4.Zero,
                        CanBeFocused = false
                    };

                    costTextBlock.RectTransform.MinSize = new Point((int)costTextBlock.TextSize.X, 0);
                }
                foreach (ApplicableResourceCollection resourceCollection in UpgradeResourceCost.GetApplicableResources(resourceCosts))
                {
                    if (resourceCollection.Count <= 0) { continue; }
                    ItemPrefab defaultItem = resourceCollection.MatchingItems.First();
                    int itemCount = resourceCollection.MatchingItems.Count();

                    GUILayoutGroup resourceCostGroup = new(new RectTransform(Vector2.UnitY, materials.Content.RectTransform))
                    {
                        ToolTip = defaultItem.Name,
                        CanBeFocused = true
                    };

                    GUIImage resourceCostSprite = new(new RectTransform(new Vector2(1f, 0.7f), resourceCostGroup.RectTransform), defaultItem.InventoryIcon ?? defaultItem.Sprite)
                    {
                        Color = defaultItem.InventoryIcon == null ? defaultItem.SpriteColor : defaultItem.InventoryIconColor,
                        CanBeFocused = false
                    };

                    GUITextBlock resourceCostText = new(new RectTransform(new Vector2(1f, 0.3f), resourceCostGroup.RectTransform), $"0/{resourceCollection.Count}", Color.Green, textAlignment: Alignment.Center)
                    {
                        CanBeFocused = false
                    };

                    float resourceWidth = materials.Rect.Width * 0.05f + MathF.Max(resourceCostText.TextSize.X, (float)resourceCostSprite.Rect.Width);
                    resourceCostGroup.RectTransform.MinSize = new Point((int)resourceWidth, resourceCostSprite.Rect.Height);
                    resourceImages.Add(resourceCollection, new ResourceElement(resourceCostSprite, resourceCostText));
                }

                materials.ForceUpdate();

                if (materials.ScrollBar.Enabled)
                {
                    materials.RectTransform.MinSize += new Point(0, materials.ScrollBar.Rect.Height);
                }

                new GUIFrame(new RectTransform(Vector2.UnitX, verticalLayout.RectTransform), "HorizontalLine", GUIStyle.Blue);

                GUILayoutGroup subLayout = new(new RectTransform(new Vector2(1f, 0.2f), verticalLayout.RectTransform, Anchor.Center), isHorizontal: true, Anchor.CenterLeft) 
                { 
                    Stretch = true
                };

                restrictionsText = new(new RectTransform(Vector2.One, subLayout.RectTransform), String.Empty, Color.White, textAlignment: Alignment.CenterLeft);

                buyButton = new GUIButton(new RectTransform(new Vector2(0.35f, 0f), subLayout.RectTransform), TextManager.Get("purchase"), style: "ItemCatalogPurchaseButton")
                {
                    OnClicked = (_, _) =>
                    {
                        EventEditorScreen.AskForConfirmation(TextManager.Get("upgrades.purchaseprompttitle"), TextManager.GetWithVariable("catalog.confirmdescription", "[itemname]", itemToPlace.Name), () =>
                        {
                            catalog.item.CreateClientEvent(catalog, new CatalogEventDataClient(this));
                            Purchase(Character.Controlled);
                            new GUIMessageBox("", TextManager.Get("catalog.purchasemessage"), Array.Empty<LocalizedString>(), type: GUIMessageBox.Type.InGame);

                            return true;
                        }, overrideConfirmButtonSound: GUISoundType.ConfirmTransaction);
                        return true;
                    }
                };

                frame.RectTransform.MinSize = new Point(0, 64 + itemName.Rect.Height + subLayout.Rect.Height + materials.Rect.Height + (int)description.TextSize.Y);
                if (!requiredResearch.IsEmpty)
                {
                    if (!TechnologyPrefab.Prefabs.TryGet(requiredResearch, out TechnologyPrefab? technologyPrefab))
                    {
                        DebugConsole.AddWarning($"Failed to find technology prefab {requiredResearch}");
                    }
                    else
                    {
                        unresearchedFrame = new GUIFrame(new RectTransform(Vector2.One, frame.RectTransform), color: Color.Black * 0.9f);
                        new GUITextBlock(new RectTransform(new Vector2(1f, 1f), unresearchedFrame.RectTransform),
                            TextManager.Get("catalog.requiredresearch") + $"\n{technologyPrefab.GetDisplayName()}",
                            GUIStyle.Red * 0.85f, textAlignment: Alignment.Center, font: GUIStyle.SubHeadingFont);

                        unresearchedFrame.Visible = !IsResearched();
                    }
                }
            }

            public void SetBuyButtonEnabled(bool enabled)
            {
                if (buyButton != null) 
                { 
                    buyButton.Enabled = enabled;                
                }
            }

            public void UpdateGUI(bool forceRefresh, IEnumerable<Item> allItems)
            {
                if (!updatedAtLeastOnce)
                {
                    forceRefresh = true;
                }

                //don't refresh if the frame of the item is currently outside view
                if (!forceRefresh && frame != null)
                {
                    if (frame.Rect.Y > frame.Parent.Rect.Bottom || frame.Rect.Bottom < frame.Parent.Rect.Y)
                    {
                        return;
                    }
                }

                updatedAtLeastOnce = true;

                buyButton!.Enabled = CanBePurchased(Character.Controlled, availableResources: allItems);

                if (unresearchedFrame != null)
                {
                    unresearchedFrame.Visible = !IsResearched();
                }

                if (costTextBlock != null)
                {
                    bool canAfford = GameMain.GameSession?.Campaign is not { } campaign || campaign.CanAfford(cost);
                    costTextBlock.TextColor = canAfford ? GUIStyle.Green : GUIStyle.Red;
                }

                foreach ((ApplicableResourceCollection resourceCollection, ResourceElement resourceElement) in resourceImages)
                {
                    ItemPrefab currentPrefab;
                    if (resourceCollection.Count > 1)
                    {
                        const float spriteUpdateInterval = 3f; // in seconds
                        int itemIndex = (int)(Timing.TotalTimeUnpaused / spriteUpdateInterval) % resourceCollection.MatchingItems.Length;
                        currentPrefab = resourceCollection.MatchingItems[itemIndex];

                        resourceElement.Icon.Sprite = currentPrefab.InventoryIcon ?? currentPrefab.Sprite;
                        resourceElement.Icon.Parent.ToolTip = currentPrefab.Name;
                    }
                    else
                    {
                        currentPrefab = resourceCollection.MatchingItems.First();
                    }

                    int currentAmountItems = allItems.Count(resourceCollection.Cost.MatchesItem);
                    resourceElement.Icon.Color = currentPrefab.InventoryIcon == null ? currentPrefab.SpriteColor : currentPrefab.InventoryIconColor;

                    resourceElement.TextBlock.Text = $"{currentAmountItems}/{resourceCollection.Count}";


                    resourceElement.TextBlock.TextColor = currentAmountItems >= resourceCollection.Count ? GUIStyle.Green : GUIStyle.Red;
                }

                RefreshRestrictionText();
            }

            private void RefreshRestrictionText()
            {
                if (restrictionsText == null) { return; }

                int submarineAttachedCount = 0;
                int hullAttachedCount = 0;
                if (catalog.item.Submarine != null)
                {
                    submarineAttachedCount = Item.ItemList.Count(i => i.Prefab == itemToPlace
                        && i.Submarine == catalog.item.Submarine && i.GetComponent<Holdable>() is not { Attached: false });
                }
                if (catalog.item.CurrentHull != null)
                {
                    List<Hull> linkedHulls = [catalog.item.CurrentHull];
                    catalog.item.CurrentHull.GetLinkedHulls(linkedHulls, includeHiddenHulls: true);
                    hullAttachedCount = Item.ItemList.Count(i => i.Prefab == itemToPlace
                        && linkedHulls.Contains(i.CurrentHull) && i.GetComponent<Holdable>() is not { Attached: false });
                }

                LocalizedString? submarineLimitText = null;
                LocalizedString? hullLimitText = null;
                if (catalog.item.Submarine?.Info is { IsOutpost: true } && maxPerOutpost >= 0)
                {
                    submarineLimitText = TextManager.GetWithVariables("catalog.outpostlimit", ("[current]", submarineAttachedCount.ToString()), ("[max]", maxPerOutpost.ToString()));
                }
                if (catalog.item.Submarine?.Info is { IsPlayer: true } && maxPerSubmarine >= 0)
                {
                    submarineLimitText = TextManager.GetWithVariables("catalog.submarinelimit", ("[current]", submarineAttachedCount.ToString()), ("[max]", maxPerSubmarine.ToString()));
                }
                if (catalog.item.CurrentHull != null && maxPerHull >= 0)
                {
                    hullLimitText = TextManager.GetWithVariables("catalog.hulllimit", ("[current]", hullAttachedCount.ToString()), ("[max]", maxPerHull.ToString()));
                }

                LocalizedString? moduleLimitText = null;
                if (maxPerModule >= 0 &&
                    //no need to show the module limit if the per-outpost limit is more restrictive  
                    (maxPerModule < maxPerOutpost || maxPerOutpost == -1))
                {
                    PlacedModule? targetModule = catalog.item.CurrentHull?.Submarine?.OutpostModules?.FirstOrDefault(m => m.Entities.Contains(catalog.item.CurrentHull));
                    if (targetModule != null)
                    {
                        var moduleItems = Item.ItemList.Where(it => targetModule.Entities.Contains(it.CurrentHull));
                        int moduleAttachedCount = moduleItems.Count(i => i.Prefab == itemToPlace
                            && i.GetComponent<Holdable>() is not { Attached: false });
                        moduleLimitText = TextManager.GetWithVariables("catalog.modulelimit", ("[current]", moduleAttachedCount.ToString()), ("[max]", maxPerModule.ToString()));
                    }
                }

                var nonNullTexts = new List<LocalizedString?> { submarineLimitText, moduleLimitText, hullLimitText }.Where(text => text != null);
                restrictionsText.Text = string.Join("\n", nonNullTexts.Select(t => t?.Value ?? string.Empty));
            }
        }

        public override bool RecreateGUIOnResolutionChange => true;

        private HashSet<CatalogCategory> collapsedCategories = new HashSet<CatalogCategory>();
        private CatalogCategory? showOnlyCategory = null;
        private double guiRefreshTime = 0;

        private GUITextBox? filterBox;
        private GUITickBox? researchedOnlyTickBox;
        private GUITickBox? materialsAvailableOnlyTickBox;
        private GUIListBox? itemList;

        /// <summary>
        /// When set, we force an update of all the catalog items, checking whether the player 
        /// has all the materials for them and enabling the "purchase" button accordingly
        /// </summary>
        private bool forceRefreshAllCatalogItems;

        partial void InitProjectSpecific(ContentXElement element) => CreateGUI();

        protected override void CreateGUI()
        {
            if (GuiFrame == null) { return; }

            GUIFrame contentFrame = new(new RectTransform(new Vector2(0.95f, 0.95f), GuiFrame.RectTransform, Anchor.Center), "InnerFrame");
            GUILayoutGroup content = new(new RectTransform(Vector2.One, contentFrame.RectTransform, Anchor.Center)) { Stretch = true, AbsoluteSpacing = 5 };

            GUIFrame header = new(new RectTransform(new Vector2(1f, 0f), content.RectTransform), "ItemCatalogHeader");

            GUILayoutGroup headerLayout = new(new RectTransform(Vector2.One, header.RectTransform));

            GUIFrame titleFrame = new(new RectTransform(Vector2.UnitX, headerLayout.RectTransform), style: null);

            GUITextBlock titleTextBlock = new(new RectTransform(Vector2.UnitX, titleFrame.RectTransform, Anchor.CenterLeft), item.Name, GUIStyle.Blue, GUIStyle.LargeFont);

            filterBox = GUI.CreateFilterBox(new RectTransform(new Vector2(0.5f, 1f), titleFrame.RectTransform, Anchor.CenterRight) 
            { 
                MaxSize = new Point(headerLayout.Rect.Size.X - (int)titleTextBlock.TextSize.X - 20, 0) 
            });

            titleFrame.RectTransform.MinSize = new Point(0, (int)titleTextBlock.TextSize.Y);

            GUILayoutGroup categoryButtonContainer = new(new RectTransform(Vector2.UnitX, headerLayout.RectTransform), true) { Stretch = true };
            const int maxButtonSize = 50;
            int buttonSize = Math.Min(titleFrame.Rect.Width / (categories.Count + 1), maxButtonSize);

            categoryButtonContainer.RectTransform.MinSize = new Point(0, Math.Min(buttonSize, maxButtonSize));

            GUIButton allCategoryButton = new(new RectTransform(new Point(buttonSize), categoryButtonContainer.RectTransform), style: "CategoryButton.All")
            {
                ToolTip = TextManager.Get("MapEntityCategory.All"),
                OnClicked = (GUIButton button, object obj) =>
                {
                    showOnlyCategory = null;
                    UpdateFilter();
                    return true;
                }
            };
            foreach (CatalogCategory category in categories)
            {
                GUIButton categoryButton = new(new RectTransform(new Point(buttonSize), categoryButtonContainer.RectTransform), style: $"ItemCatalogCategory.{category.Identifier}")
                {
                    ToolTip = TextManager.Get($"catalog.{category.Identifier}"),
                    OnClicked = (GUIButton button, object obj) =>
                    {
                        showOnlyCategory = category;
                        UpdateFilter();
                        return true;
                    }
                };
            }

            var tickBoxesLayout = new GUILayoutGroup(new RectTransform(Vector2.UnitX, headerLayout.RectTransform), isHorizontal: true);

            researchedOnlyTickBox = new GUITickBox(new RectTransform(new Vector2(0.5f, 0f), tickBoxesLayout.RectTransform), TextManager.Get("researchedonly"), GUIStyle.SubHeadingFont)
            {
                ToolTip = TextManager.Get("researchedonly.tooltip"),
                Selected = true
            };
            researchedOnlyTickBox.TextBlock.OverflowClip = true;
            researchedOnlyTickBox.OnSelected += _ =>
            {
                UpdateFilter();
                return true;
            };

            materialsAvailableOnlyTickBox = new GUITickBox(new RectTransform(new Vector2(0.5f, 0f), tickBoxesLayout.RectTransform), TextManager.Get("materialsavailableonly"), GUIStyle.SubHeadingFont)
            {
                ToolTip = TextManager.Get("materialsavailableonly.tooltip")
            };
            materialsAvailableOnlyTickBox.TextBlock.OverflowClip = true;
            materialsAvailableOnlyTickBox.OnSelected += _ =>
            {
                UpdateFilter();
                return true;
            };

            tickBoxesLayout.RectTransform.MinSize = new Point(0, researchedOnlyTickBox.Rect.Height);

            header.RectTransform.MinSize = new Point(header.Rect.Width, titleFrame.Rect.Height + categoryButtonContainer.Rect.Height + tickBoxesLayout.Rect.Height + GUI.IntScale(6f));
            headerLayout.Recalculate();

            itemList = new GUIListBox(new RectTransform(content.RectTransform.Rect.Size, content.RectTransform), style: null) 
            { 
                Spacing = 10,
                CurrentSelectMode = GUIListBox.SelectMode.None
            };

            foreach (CatalogCategory category in categories)
            {
                if (!category.CatalogItems.Where(c => c.Category == category).Any(static c => c.IsResearched()))
                {
                    collapsedCategories.Add(category);
                }

                var categoryText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), itemList.Content.RectTransform, Anchor.TopLeft),
                    TextManager.Get($"catalog.{category.Identifier}"), textColor: GUIStyle.Green, textAlignment: Alignment.CenterLeft, font: GUIStyle.SubHeadingFont, wrap: true)
                {
                    UserData = category,
                    Padding = new Vector4(GUI.IntScale(20)),
                    CanBeFocused = false
                };

                bool isCollapsed = collapsedCategories.Contains(category);

                GUIButton categoryButton = new(new RectTransform(Vector2.One, categoryText.RectTransform), style: null)
                {
                    UserData = category,
                    OnClicked = (btn, userdata) =>
                    {
                        GUIImage arrowImage = btn.GetChild<GUIImage>();
                        arrowImage.SpriteEffects ^= SpriteEffects.FlipVertically;
                        if (arrowImage.SpriteEffects == SpriteEffects.None)
                        {
                            collapsedCategories.Add(category);
                        }
                        else
                        {
                            collapsedCategories.Remove(category);
                        }
                        UpdateFilter();
                        return true;
                    }
                };

                new GUIImage(new RectTransform(new Vector2(0.5f), categoryButton.RectTransform, Anchor.CenterRight, scaleBasis: ScaleBasis.Smallest), style: "GUIButtonVerticalArrowFreeScale", scaleToFit: true)
                {
                    SpriteEffects = isCollapsed ? SpriteEffects.None : SpriteEffects.FlipVertically
                };

                new GUIFrame(new RectTransform(Vector2.UnitX, itemList.Content.RectTransform), "HorizontalLine", GUIStyle.Green)
                {
                    UserData = category
                };

                category.CatalogItems.OrderBy(static c => !c.IsResearched()).ForEach(catalogueItem => catalogueItem.CreateGUI(itemList));
            }

            filterBox.OnTextChanged += (_, filterText) =>
            {
                UpdateFilter();
                return true;
            };

            UpdateFilter();
        }

        public override void Equip(Character character)
        {
            base.Equip(character);
            forceRefreshAllCatalogItems = true;
        }

        public override void UpdateHUDComponentSpecific(Character character, float deltaTime, Camera cam)
        {
            base.UpdateHUDComponentSpecific(character, deltaTime, cam);

            if (Timing.TotalTimeUnpaused > guiRefreshTime + 1 || forceRefreshAllCatalogItems)
            {
                IEnumerable<Item> allItems = CargoManager.FindAllItemsOnPlayerAndAllowedSubs(Character.Controlled);
                catalogItems.ForEach(catalogueItem => catalogueItem.UpdateGUI(forceRefreshAllCatalogItems, allItems));
                UpdateFilter(allItems);
                guiRefreshTime = Timing.TotalTimeUnpaused;
                forceRefreshAllCatalogItems = false;
            }
        }

        private List<Item> cachedhighlightedItems = new List<Item>();
        private double lastItemHighlightRefresh = 0f;
        public override IEnumerable<Item> GetEquippedItemHighlights(Character character)
        {
            if (character.Submarine == null) { return Enumerable.Empty<Item>(); }

            if (Timing.TotalTime > lastItemHighlightRefresh)
            {
                cachedhighlightedItems.Clear();
                cachedhighlightedItems.AddRange(character.Submarine.GetItems(false).Where(CanBeReclaimed));
                lastItemHighlightRefresh = Timing.TotalTime + 0.1f;
            }

            return cachedhighlightedItems;
        }

        public override bool ShouldAllowEquippedInteraction(Character character)
        {
            if (character.FocusedItem == null) { return false; }
            // don't display anything in the action list if the item is unrelated to the catalog
            return catalogPrefabs.Contains(character.FocusedItem.Prefab);
        }

        private void UpdateFilter(IEnumerable<Item>? allItems = null)
        {
            if (itemList == null) { return; }

            allItems ??= CargoManager.FindAllItemsOnPlayerAndAllowedSubs(Character.Controlled);

            foreach (GUIComponent listElement in itemList.Content.Children)
            {
                if (listElement.UserData is CatalogItem catalogItem)
                {
                    string itemName = catalogItem.ItemToPlace?.Name.Value ?? "";

                    if (string.IsNullOrEmpty(filterBox?.Text))
                    {
                        listElement.Visible =
                            (showOnlyCategory == null ? !collapsedCategories.Contains(catalogItem.Category) : catalogItem.Category == showOnlyCategory) &&
                            (researchedOnlyTickBox == null || !researchedOnlyTickBox.Selected || catalogItem.IsResearched()) &&
                            (materialsAvailableOnlyTickBox == null || !materialsAvailableOnlyTickBox.Selected || catalogItem.HasResources(Character.Controlled, availableResources: allItems));
                    }
                    else
                    {
                        listElement.Visible =
                            itemName.Contains(filterBox.Text.Trim(), StringComparison.OrdinalIgnoreCase) &&
                            (researchedOnlyTickBox == null || !researchedOnlyTickBox.Selected || catalogItem.IsResearched()) &&
                            (materialsAvailableOnlyTickBox == null || !materialsAvailableOnlyTickBox.Selected || catalogItem.HasResources(Character.Controlled, availableResources: allItems));
                    }
                }
                else if (listElement.UserData is CatalogCategory category)
                {
                    listElement.Visible = showOnlyCategory == null || showOnlyCategory == category;
                }
            }
        }

        #region Networking
        public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData? extraData = null)
        {
            CatalogEventDataClient data = ExtractEventData<CatalogEventDataClient>(extraData);
            msg.WriteRangedInteger(catalogItems.IndexOf(data.CatalogItem), 0, catalogItems.Count - 1);
        }

        public void ClientEventRead(IReadMessage msg, float sendingTime)
        {
            EventType eventType = (EventType)msg.ReadByte();

            switch (eventType)
            {
                case EventType.Purchase:
                    int catalogItemIndex = msg.ReadRangedInteger(0, catalogItems.Count - 1);
                    UInt16 instantiatedItemID = msg.ReadUInt16();

                    if (instantiatedItemID != Entity.NullEntityID)
                    {
                        if (Entity.FindEntityByID(instantiatedItemID) is not Item instantiatedItem)
                        {
                            DebugConsole.ThrowError($"Failed to find the item with ID {instantiatedItemID} when reading catalog event data.");
                            return;
                        }
                        CatalogItem catalogItem = catalogItems[catalogItemIndex];
                        catalogItem.SetBlueprintFields(instantiatedItem);
                    }
                    break;
                case EventType.StartReclaim:
                    UInt16 targetID = msg.ReadUInt16();
                    UInt16 userID = msg.ReadUInt16();

                    if (targetID == 0 || userID == 0)
                    {
                        StopReclaiming();
                    }
                    else
                    {
                        if (Entity.FindEntityByID(targetID) is not Item target)
                        {
                            DebugConsole.ThrowError($"Failed to find the item with ID {targetID} when reading catalog event data.");
                            return;
                        }

                        if (Entity.FindEntityByID(userID) is not Character user)
                        {
                            DebugConsole.ThrowError($"Failed to find the item with ID {userID} when reading catalog event data.");
                            return;
                        }

                        StartReclaiming(target, user);
                    }

                    break;
                case EventType.ReclaimFinished:
                    StopReclaiming();
                    PersistentOutpostData.RefreshFacilityResources();
                    break;
            }
        }
        #endregion
    }
}
