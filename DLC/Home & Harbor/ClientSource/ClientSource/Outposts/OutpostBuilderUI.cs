using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using HomeAndHarbor.Resources;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Diagnostics.CodeAnalysis;

namespace HomeAndHarbor;

internal class OutpostBuilderUI : IDisposable
{
    private PlacedModule? selectedModule;
    private PurchasableOutpostModulePrefab? selectedModulePrefab;
    private OutpostBuilder.PendingOutpostModule? selectedPendingModule;
    private GUIComponent? highlightedGapForModulePlacement;

    private PlacedModule? movingModule;
    private OutpostBuilder.PendingOutpostModule? movingPendingModule;

    /// <summary>
    /// Cursor position when starting to drag a module.
    /// </summary>
    private Vector2 startDragCursorPosition;
    /// <summary>
    /// Used for storing the initial positions of the modules we're dragging.
    /// </summary>
    private readonly Dictionary<GUIComponent, Vector2> startDragElementPositions = [];

    private readonly Location location;
    private readonly PersistentOutpostData outpostData;
    private readonly Submarine outpost;

    private bool? hadPermissionsToManageOutpost;

    public OutpostItemSwapUI ItemSwapUI;

    private readonly Dictionary<PurchasableOutpostModulePrefab, double> lastCanAffordCheckTime = new Dictionary<PurchasableOutpostModulePrefab, double>();
    private readonly Dictionary<PurchasableOutpostModulePrefab, bool> canAffordModuleCached = new Dictionary<PurchasableOutpostModulePrefab, bool>();

    private static readonly Color AvailableGapColor = GUIStyle.Orange;
    private static readonly Color UnavailableGapColor = Color.Gray;
    private static readonly Color PendingHullColor = Color.Gray * 0.75f;
    private static readonly Color PendingRemovalHullColor = Color.Red * 0.75f;
    private static readonly Color HallwayColor = Color.Gray * 0.7f;

    #region UI elements
    private GUIFrame? UIFrame;
    private GUITextBlock? filterText;
    private GUIListBox? moduleList;

    private GUICustomComponent? miniMapContainer;
    private GUIFrame? miniMap;

    private GUIComponent? moduleInfoPanel;

    private readonly Dictionary<PlacedModule, GUIComponent> miniMapModuleElements = [];
    private readonly Dictionary<Item, GUIComponent> miniMapSwappableItemElements = [];
    private readonly Dictionary<OutpostBuilder.PendingOutpostModule, GUIComponent> miniMapPendingModuleElements = [];
    private readonly Dictionary<PlacedModule.GapInfo, GUIComponent> miniMapGapElements = [];

    /// <summary>
    /// Element that represents the module you're currently placing
    /// </summary>
    private GUIComponent? modulePlacementElement;

    private readonly Dictionary<SubmarineInfo, SubmarinePreview.SubmarineTexture> blueprintSprites = new Dictionary<SubmarineInfo, SubmarinePreview.SubmarineTexture>();

    private GUITextBox filterBox;
    private GUIComponent swapArea;

    private GUITickBox showResearchedOnly;

    private readonly List<GUIButton> catalogButtons = [];

    private bool hasBeenOpened;

    private readonly HashSet<Identifier> collapsedModuleCategories = [];
    private readonly Dictionary<Identifier, GUIButton> categoryCollapseButtons = [];
    #endregion

    #region Panning and zooming
    /// <summary>
    /// The scale of the minimap (= the ratio between in-world units and units on the minimap)
    /// </summary>
    private float minimapScale = 1.0f;

    private Vector2 miniMapOffset = Vector2.Zero;
    private bool draggingMiniMap;
    private Vector2? dragMiniMapStart;
    private const int DragTreshold = 8;

    private const float MinZoom = 0.4f, MaxZoom = 2.5f, DefaultZoom = 0.8f;

    private bool recreateMiniMap;
    private bool refreshMiniMapOffsetAndZoom;

    private float zoom = DefaultZoom;
    private float targetZoom = DefaultZoom;

    private float Zoom
    {
        get => zoom;
        set => zoom = Math.Clamp(value, MinZoom, MaxZoom);
    }

    #endregion

    #region Initialization

    private readonly GUIComponent originalParent;

    public OutpostBuilderUI(Location location, PersistentOutpostData outpostData, Submarine outpost, GUIComponent parent)
    {
        originalParent = parent;
        this.location = location;
        this.outpostData = outpostData;
        this.outpost = outpost;

        CreateBlueprintSprites(outpost);
        CreateUI(parent);

        ItemSwapUI = new OutpostItemSwapUI(this, swapArea);
    }

    public void OnResolutionChanged()
    {
        CreateUI(originalParent);
        ItemSwapUI = new OutpostItemSwapUI(this, swapArea);
    }

    public void UpdateCatalogLocks(bool hasOutpostManagementItem)
    {
        foreach (GUIButton button in catalogButtons)
        {
            OutpostCatalogPrefab? catalogPrefab = button.UserData as OutpostCatalogPrefab;
            if (catalogPrefab == null) { continue; }

            CantSpawnCatalogReason reason = OutpostBuilder.CanSpawnOrEquipCatalogForCharacter(outpost, Character.Controlled, catalogPrefab);

            if (reason == CantSpawnCatalogReason.MissingOutpostManagement)
            {
                button.ToolTip = TextManager.Get("outpostbuilderui.requiremoduleadmin");
                button.Enabled = false;
            }
            else if (reason == CantSpawnCatalogReason.MissingTechnology)
            {
                if (TechnologyPrefab.Prefabs.TryGet(catalogPrefab.RequiredTechnologyIdentifier, out TechnologyPrefab? technologyPrefab))
                {
                    button.ToolTip = RichString.Rich(TextManager.GetWithVariable("outpostbuilderui.requirestechnology", "[technology]", technologyPrefab.GetDisplayName()));
                    button.Enabled = false;
                }
                else
                {
                    DebugConsole.ThrowError($"Failed to find technology with identifier {catalogPrefab.RequiredTechnologyIdentifier}");
                }
            }
            else if (reason == CantSpawnCatalogReason.HandsFull)
            {
                button.ToolTip = TextManager.Get("outpostbuilderui.handsfull");
                button.Enabled = false;
            }
            else if (reason == CantSpawnCatalogReason.NoCharacter)
            {
                button.ToolTip = TextManager.Get("outpostbuilderui.nocharacter");
                button.Enabled = false;
            }
            else
            {
                button.ToolTip = catalogPrefab.ToolTip;
                button.Enabled = true;
            }

            UpdateCatalogButtonStyle(button, catalogPrefab);
        }
    }

    /// <summary>
    /// UI styles don't have a disabled sprite state, so swap to the separate disabled style when needed
    /// </summary>
    private static void UpdateCatalogButtonStyle(GUIButton button, OutpostCatalogPrefab catalogPrefab)
    {
        if (catalogPrefab.ButtonStyle.IsEmpty || catalogPrefab.DisabledButtonStyle.IsEmpty) { return; }

        Identifier styleIdentifier = button.Enabled ? catalogPrefab.ButtonStyle : catalogPrefab.DisabledButtonStyle;
        if (button.Style?.Identifier == styleIdentifier) { return; }
        if (GUIStyle.GetComponentStyle(styleIdentifier) is { } style)
        {
            button.ApplyStyle(style);
        }
    }

    [MemberNotNull(nameof(filterBox), nameof(swapArea), nameof(showResearchedOnly))]
    private void CreateUI(GUIComponent parent)
    {
        UIFrame?.Parent.RemoveChild(UIFrame);

        UIFrame = new GUIFrame(new RectTransform(Vector2.One, parent.RectTransform), style: null);

        // outpost preview behind other elements ------------------------------------------------------------------

        miniMapContainer = new GUICustomComponent(new RectTransform(new Vector2(1f - OutpostManagementUI.LeftPanelWidth, 1f), UIFrame.RectTransform, Anchor.TopRight), onDraw: DrawMiniMap, onUpdate: UpdateMiniMap)
        {
            HideElementsOutsideFrame = true
        };

        if (outpost != null)
        {
            if (Level.Loaded?.StartLocation is { } location)
            {
                CreateMiniMap(outpost, location, miniMapContainer);
            }
            if (outpost.OutpostModules.None())
            {
                new GUITextBlock(new RectTransform(new Vector2(0.4f, 0.2f), miniMapContainer.RectTransform, Anchor.Center), 
                   TextManager.Get("outpostbuilderui.cantaddmodules"), textAlignment: Alignment.Center, wrap: true, textColor: GUIStyle.Red);
            }
        }

        int spacing = GUI.IntScale(15);

        // catalog buttons, laid out in a row with the catalog name below each button
        int catalogLabelSpacing = GUI.IntScale(4);
        var catalogPrefabs = OutpostCatalogPrefab.Prefabs.ToList();
        var catalogButtonsLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.2f, 0.15f), UIFrame.RectTransform, Anchor.BottomRight),
            isHorizontal: true,
            childAnchor: Anchor.BottomRight)
        {
            AbsoluteSpacing = spacing,
            CanBeFocused = true
        };

        for (int i = 0; i < catalogPrefabs.Count; i++)
        {
            OutpostCatalogPrefab catalogPrefab = catalogPrefabs[i];

            var catalogColumn = new GUILayoutGroup(new RectTransform(new Vector2(0.6f, 1.0f), catalogButtonsLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight),
                childAnchor: Anchor.TopCenter)
            {
                AbsoluteSpacing = catalogLabelSpacing,
                CanBeFocused = true
            };

            var catalogButton = new GUIButton(new RectTransform(new Vector2(1.0f, 0.6f), catalogColumn.RectTransform),
                style: catalogPrefab.ButtonStyle.IsEmpty ? "GUIButtonSmallFreeScale" : catalogPrefab.ButtonStyle.Value)
            {
                UserData = catalogPrefab,
                ToolTip = catalogPrefab.ToolTip
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), catalogColumn.RectTransform),
                catalogPrefab.DisplayName.Replace(' ', '\n'),
                textAlignment: Alignment.TopCenter,
                font: GUIStyle.SmallFont)
            {
                CanBeFocused = false
            };

            catalogButton.OnClicked = (GUIButton button, object obj) =>
            {
                OutpostManagementUI.Instance?.ToggleOpen();

                if (GameMain.NetworkMember is { IsClient: true })
                {
                    OutpostBuilder.ClientRequestCatalog(catalogPrefab);
                }
                else
                {
                    OutpostBuilder.SpawnOrEquipCatalogForCharacter(Character.Controlled, catalogPrefab);
                }

                return true;
            };

            catalogButtons.Add(catalogButton);
        }

        // module list at the left ------------------------------------------------------------------

        var moduleListPanel = new GUIFrame(new RectTransform(new Vector2(OutpostManagementUI.LeftPanelWidth, 1.0f), UIFrame.RectTransform), style: "UpgradeUIFrame");

        var innerFrame = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.97f), moduleListPanel.RectTransform, Anchor.Center), style: "InnerFrameDark", color: Color.Black);
        var moduleListContents = new GUILayoutGroup(new RectTransform(new Vector2(0.97f, 0.98f), innerFrame.RectTransform, Anchor.Center))
        {
            Stretch = true,
            AbsoluteSpacing = spacing
        };

        //spacing
        //new GUIFrame(new RectTransform(new Point(moduleListContents.Rect.Width, spacing), moduleListContents.RectTransform), style: null, Color.Transparent);

        filterText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), moduleListContents.RectTransform), TextManager.Get("serverlog.filter"), font: GUIStyle.SubHeadingFont);
        filterBox = new GUITextBox(new RectTransform(new Vector2(0.65f, 1f), filterText.RectTransform, Anchor.CenterRight), font: GUIStyle.Font, createClearButton: true) { OverflowClip = true };
        filterBox.OnTextChanged += (textBox, text) =>
        {
            FilterModuleList();
            return true;
        };
        filterText.InheritChildrenMinHeight();

        showResearchedOnly = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.0f), moduleListContents.RectTransform), TextManager.Get("outpostbuilderui.showresearched"))
        {
            OnSelected = (tickBox) =>
            {
                FilterModuleList();
                return true;
            }
        };

        moduleList = new GUIListBox(new RectTransform(Vector2.One, moduleListContents.RectTransform), useMouseDownToSelect: true, style: "GUIListBoxNoBorder")
        {
            OnSelected = SelectModulePrefab,
            Spacing = GUI.IntScale(5),
            Visible = true,
            PlaySoundOnSelect = true,
        };
        UpdateModuleList(moduleList.Content);
        FilterModuleList();

        swapArea = new GUIFrame(new RectTransform(Vector2.One, miniMapContainer.RectTransform, Anchor.Center), style: null)
        {
            Color = Color.Black * 0.5f,
            Visible = false,
        };

        CheckErrors();
    }

    private static void CheckErrors()
    {
        foreach (var purchasableModule in PurchasableOutpostModulePrefab.Prefabs)
        {
            var matchingInfo = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == purchasableModule?.OutpostInfo?.Name && s.IsOutpostModule);
            if (matchingInfo == null)
            {
                DebugConsole.ThrowError($"Error in purchasable module \"{purchasableModule}\": cannot find a matching outpost module in saved modules. Did you forget to add the module to the filelist.xml?");
            }
        }
    }

    public void CreateBlueprintSprites(Submarine outpost)
    {
        foreach (var module in outpost.OutpostModules)
        {
            if (module.Info?.SubmarineElement != null)
            {
                GenerateTexture(module.Info);
            }
        }
        foreach (var purchasableModule in PurchasableOutpostModulePrefab.Prefabs.Where(m => m.Sold))
        {
            if (purchasableModule.OutpostInfo?.SubmarineElement != null)
            {
                GenerateTexture(purchasableModule.OutpostInfo);
            }
        }
        return;

        void GenerateTexture(SubmarineInfo moduleInfo)
        {
            if (blueprintSprites.ContainsKey(moduleInfo)) { return; }
            int textureSize = MathHelper.Clamp((int)Math.Max(moduleInfo.Dimensions.X, moduleInfo.Dimensions.Y) / 5, 256, 1024);

            SubmarinePreview.GenerateTexture(moduleInfo, doNotDrawPredicate: DoNotDrawPredicate, new Point(textureSize),onFinished: (SubmarinePreview.SubmarineTexture texture) =>
                                             {
                                                 blueprintSprites[moduleInfo] = texture;
                                             },
                                             hideFactionSpecificEntities: true);
            return;

            // don't draw items that can be swapped
            static bool DoNotDrawPredicate(Identifier identifier)
                => OutpostItemSwapUI.GetSwappableItems(identifier).Length > 0;
        }
    }

    public void OnOpened()
    {
        UpdateModuleList(moduleList!.Content);
        HideModuleInfoPanel();
        if (!hasBeenOpened)
        {
            ItemSwapUI.ClearSwapUI();
            collapsedModuleCategories.Clear();
            foreach (var category in availableCategories)
            {
                if (HasCategoryReachedLimit(category.Key, category.Value) || !HasCategoryBeenResearched(category.Key))
                {
                    collapsedModuleCategories.Add(category.Key);
                    if (categoryCollapseButtons.TryGetValue(category.Key, out GUIButton? categoryBtn) && categoryBtn != null)
                    {
                        GUIImage arrowImage = categoryBtn.GetChild<GUIImage>();
                        arrowImage.SpriteEffects = SpriteEffects.None;
                    }
                }
            }
            selectedModulePrefab = null;
            ResetMiniMapOffset();
            hasBeenOpened = true;
        }
        FilterModuleList();
    }
    #endregion

    #region Minimap
    private void CreateMiniMap(Submarine outpost, Location _, GUIComponent parent)
    {
        var previousPendingModules = new List<OutpostBuilder.PendingOutpostModule>(miniMapPendingModuleElements.Keys);

        //the elements are about to be recreated, so cancel any drag in progress (it refers to elements that no longer exist)
        //can happen in MP when the server sends module changes mid-drag
        movingModule = null;
        movingPendingModule = null;
        startDragElementPositions.Clear();
        modulesToMove.Clear();
        pendingModulesToMove.Clear();
        moduleElementsToMove.Clear();

        miniMap?.Parent.RemoveChild(miniMap);
        miniMapPendingModuleElements.Clear();
        miniMapModuleElements.Clear();
        miniMapGapElements.Clear();
        miniMapSwappableItemElements.Clear();

        if (outpost == null || outpostData == null) { return; }

        miniMap = new GUIFrame(new RectTransform(Vector2.One, parent.RectTransform), style: null);
        swapArea?.SetAsLastChild();

        var miniMapBackground = new GUIFrame(new RectTransform(Vector2.One * 10, miniMap.RectTransform, Anchor.Center), style: null, color: Color.Transparent)
        {
            CanBeFocused = false
        };

        minimapScale = Math.Min(
            parent.Rect.Width / (float)outpost.Borders.Width, 
            parent.Rect.Height / (float)outpost.Borders.Height);

        foreach (var module in outpost.OutpostModules)
        {
            Rectangle bounds = module.HullBounds;
            Vector2 modulePos = module.PositionRelativeToSub + bounds.Location.ToVector2() + module.TemporaryMoveOffset;
            modulePos.Y += module.HullBounds.Height;

            bool removalPending = outpostData.IsRemovalPending(module);
            Color hullColor = GetHullElementColor(module);

            var matchingPurchaseableModule = OutpostBuilder.FindModulePrefab(module);
            GUIButton hullFrame = new GUIButton(new RectTransform(bounds.Size.Multiply(minimapScale), miniMap.RectTransform) 
                { RelativeOffset = WorldPosToUI(modulePos) }, style: "ScanLines", color: hullColor)
            {
                OutlineColor = hullColor,
                HoverColor = Color.Lerp(hullColor, Color.White, 0.5f),
                SelectedColor = Color.White,
                OutlineThickness = 2,
                UserData = module,
                OnClicked = SelectPlacedModule,
                ToolTip = matchingPurchaseableModule?.DisplayName ?? PurchasableOutpostModulePrefab.GetDefaultDisplayName(module.Info),
                ClampMouseRectToGUIComponent = miniMapContainer
            };

            //element for rendering the module preview
            new GUICustomComponent(new RectTransform(Vector2.Divide(module.VisibleEntityBounds.Size.ToVector2(), bounds.Size.ToVector2()), hullFrame.RectTransform, Anchor.Center),
                onDraw: (SpriteBatch spriteBatch, GUICustomComponent customComponent) =>
            {
                DrawModule(spriteBatch, customComponent, module.Info);
            })
            {
                CanBeFocused = false
            };

            new GUIFrame(new RectTransform(new Point(GUI.IntScale(80)), hullFrame.RectTransform, Anchor.Center), style: "ModulePendingRemovalIcon")
            {
                Color = PendingRemovalHullColor,
                Visible = false,
                ToolTip = TextManager.Get("outpostbuilderui.removalpending"),
                OnAddedToGUIUpdateList = (component) =>
                {
                    bool removalPending = outpostData.IsRemovalPending(module);
                    component.Color = component.HoverColor = component.SelectedColor = component.PressedColor = component.DisabledColor =
                        removalPending && selectedModule != module ? 
                        PendingRemovalHullColor : 
                        Color.Transparent;
                    component.CanBeFocused = removalPending;
                }
            };

            CreateMoveModuleButton(hullFrame);

            miniMapModuleElements.Add(module, hullFrame);

            foreach (var gapInfo in module.AllGaps)
            {
                bool availableForModulePlacement = 
                    gapInfo.Gap is { IsDisabled: true } && 
                    OutpostBuilder.IsValidGapForModulePlacement(gapInfo.Gap);

                bool inUse = 
                    gapInfo.Gap == module.ThisGap || 
                    gapInfo.Gap == module.PreviousGap ||
                    outpost.OutpostModules.Any(m => m.PreviousGap == gapInfo.Gap) ||
                    outpostData.PendingOutpostModules.Any(m => m.Gap == gapInfo.Gap);

                if (!availableForModulePlacement && !inUse) { continue; }

                Color color = availableForModulePlacement && !removalPending && !inUse ? AvailableGapColor : UnavailableGapColor;

                Vector2 gapPos = gapInfo.Rect.Location.ToVector2().FlipY();
                //gap location is at the top left corner, but we're using the center as the pivot for the gaps to simplify positioning
                gapPos.X += gapInfo.Rect.Width / 2;
                gapPos.Y += gapInfo.Rect.Height / 2;
                GUIButton gapFrame = new GUIButton(new RectTransform(gapInfo.Gap.Rect.Size.Multiply(minimapScale), hullFrame.RectTransform, Anchor.Center)
                { RelativeOffset = Vector2.Divide(gapPos, bounds.Size.ToVector2()) }, style: "ScanLines", color: color)
                {
                    OutlineColor = color,
                    HoverColor = Color.Lerp(color, Color.White, 0.5f),
                    Enabled = availableForModulePlacement && !removalPending && !inUse,
                    DisabledColor = color,
                    SelectedColor = Color.White,
                    OutlineThickness = 2,
                    UserData = gapInfo,
                    ClampMouseRectToGUIComponent = miniMapContainer
                };

                new GUICustomComponent(new RectTransform(Vector2.One, gapFrame.RectTransform), onDraw: (sb, customComponent) => DrawHallway(sb, gapInfo, gapFrame))
                {
                    UserData = gapInfo
                };
                miniMapGapElements.Add(gapInfo, gapFrame);
            }

            foreach (MapEntity entity in module.Entities)
            {
                if (entity is not Item { AllowSwapping: true, Prefab.SwappableItem: not null } it)
                {
                    continue; //only show items that can be swapped
                }

                // filter out items that don't have any replacements
                if (OutpostItemSwapUI.GetSwappableItems(it.Prefab).Length == 0)  { continue; }

                Vector2 itemWorldPosition = it.WorldRect.Location.ToVector2();
                Vector2 submarinePosition = outpost.WorldPosition;
                Vector2 modulePosition = module.PositionRelativeToSub;
                Vector2 itemPos = itemWorldPosition - submarinePosition - modulePosition;
                itemPos.Y = -itemPos.Y;
                itemPos.X += it.Rect.Width / 2f;
                itemPos.Y += it.Rect.Height / 2f;

                Point size = new Point(Math.Max(it.Rect.Width, it.Rect.Height));

                GUIFrame poiFrame = new GUIFrame(new RectTransform(size.Multiply(minimapScale), hullFrame.RectTransform, Anchor.Center)
                                                     { RelativeOffset = Vector2.Divide(itemPos, bounds.Size.ToVector2()) }, style: null)
                {
                    Enabled = true,
                    UserData = entity,
                    ClampMouseRectToGUIComponent = miniMapContainer,
                };

                Sprite? upgradeSprite = it.Prefab.UpgradePreviewSprite;

                SpriteEffects spriteEffects = SpriteEffects.None;
                float rotation = MathHelper.ToDegrees(MathUtils.WrapAngleTwoPi(it.RotationRad));
                if (rotation > 90 && rotation < 270)
                {
                    spriteEffects |= SpriteEffects.FlipVertically;
                }
                if (rotation > 180)
                {
                    spriteEffects |= SpriteEffects.FlipHorizontally;
                }

                GUIButton button = new GUIButton(new RectTransform(Vector2.One, poiFrame.RectTransform, Anchor.Center), style: upgradeSprite != null ? null : "ScanLines", color: upgradeSprite != null ? Color.Transparent : GUIStyle.Green)
                {
                    ToolTip = entity.Name,
                    OutlineColor = upgradeSprite != null ? Color.Transparent : GUIStyle.Green,
                    OutlineThickness = 2,
                    OnClicked = (button, o) =>
                    {
                        FocusItemInModule(it);
                        return true;
                    }
                };
                if (upgradeSprite != null)
                {
                    //take the origin of the upgrade sprite into account in the positioning of the button
                    Vector2 relOrigin = new Vector2(0.5f, 0.5f) - upgradeSprite.RelativeOrigin;
                    button.RectTransform.AbsoluteOffset = new Point((int)(button.Rect.Width * relOrigin.X), (int)(button.Rect.Height * relOrigin.Y));
                }

                if (upgradeSprite != null)
                {
                    new GUIImage(new RectTransform(new Vector2(1.2f), button.RectTransform, Anchor.Center), upgradeSprite, scaleToFit: true)
                    {
                        ToolTip = entity.Name,
                        SelectedColor = GUIStyle.Orange,
                        HoverColor = Color.White * 0.8f,
                        Color = Color.White,
                        SpriteEffects = spriteEffects
                    };
                }

                if (miniMapSwappableItemElements.ContainsKey(it))
                {
                    DebugConsole.ThrowError(
                        $"Error when creating a preview image of the item {it.Name} on the outpost building UI."
                        +$" The item was marked as belonging to multiple modules: {module.Name} and {outpost.OutpostModules.FirstOrDefault(m => m != module && m.Entities.Contains(it))?.Name ?? "null"}.");
                }
                else
                {
                    miniMapSwappableItemElements.Add(it, poiFrame);
                }

            }
        }

        foreach (var pendingModule in outpostData.PendingOutpostModules)
        {
            if (pendingModule.ModulePrefab == null || pendingModule.Gap == null) { continue; }

            Point size = pendingModule.ModulePrefab.HullBounds.Size;

            GUIComponent? gapElement = miniMapGapElements.FirstOrDefault(g => g.Key.Gap == pendingModule.Gap).Value;
            if (gapElement == null)
            {
                DebugConsole.ThrowError("Error when creating outpost minimap: could not find the gap UI element for a pending module.");
                continue;
            }
            PlacedModule? prevModule = outpost.OutpostModules.FirstOrDefault(m => m.IsGapInThisModule(pendingModule.Gap));
            if (prevModule == null)
            {
                DebugConsole.ThrowError("Error when creating outpost minimap: could not find the previous module for a pending module.");
                continue;
            }
            var gapToAttachTo = prevModule.AllGaps.FirstOrDefault(g => g.Gap == pendingModule.Gap);
            if (gapToAttachTo == null)
            {
                DebugConsole.ThrowError("Error when creating outpost minimap: could not find the gap info for a pending module.");
                continue;
            }

            OutpostModuleInfo.GapPosition gapPosition = OutpostGenerator.GetOpposingGapPosition(OutpostGenerator.GetGapPosition(pendingModule.Gap));
            if (pendingModule.ThisGap is not { } thisGap)
            {
                DebugConsole.ThrowError($"Failed to find the gap that attaches to the previous module in the pending module {pendingModule.ModulePrefab.Identifier} (side {gapPosition}, index {pendingModule.ThisGapIndex}).");
                continue;
            }

            bool isHorizontal = gapPosition == OutpostModuleInfo.GapPosition.Right || gapPosition == OutpostModuleInfo.GapPosition.Left;

            //calculate the position of the pending module based on the gap it's attached to and the length of the hallway
            //(yes this is super hairy)
            Vector2 gapToAttachToPos = new Vector2(gapToAttachTo.Rect.X + gapToAttachTo.Rect.Width / 2, gapToAttachTo.Rect.Y - gapToAttachTo.Rect.Height / 2);
            Vector2 thisGapPos = new Vector2(thisGap.Rect.X + thisGap.Rect.Width / 2, thisGap.Rect.Y - thisGap.Rect.Height / 2);
            Vector2 newModulePos = 
                prevModule.PositionRelativeToSub + prevModule.TemporaryMoveOffset + gapToAttachToPos - thisGapPos +
                (isHorizontal ? Vector2.UnitX : Vector2.UnitY) * pendingModule.HallwayLength;

            GUIButton hullFrame = new GUIButton(new RectTransform(size.Multiply(minimapScale), miniMap.RectTransform, pivot: Pivot.Center)
                { RelativeOffset = WorldPosToUI(newModulePos) }, style: "ScanLines", color: PendingHullColor)
            {
                OutlineColor = PendingHullColor,
                HoverColor = Color.Lerp(PendingHullColor, Color.White, 0.5f),
                SelectedColor = Color.White,
                OutlineThickness = 2,
                UserData = pendingModule,
                OnClicked = SelectPendingModule,
                ClampMouseRectToGUIComponent = miniMapContainer
            };

            //flip y because module coordinates are in world space, and we're creating UI elements in screen space
            Point offset = (pendingModule.ModulePrefab.Bounds.Center - pendingModule.ModulePrefab.HullBounds.Center).FlipY();

            new GUICustomComponent(new RectTransform(Vector2.One, hullFrame.RectTransform, Anchor.Center)
            { RelativeOffset = -offset.ToVector2() },
                onDraw: (sb, customComponent) =>
                {
                    DrawModule(sb, customComponent, pendingModule.ModulePrefab.OutpostInfo, color: PendingHullColor * 0.5f);
                })
            {
                CanBeFocused = false
            };

            new GUIFrame(new RectTransform(new Point(GUI.IntScale(80)), hullFrame.RectTransform, Anchor.Center), style: "ModuleUnderConstructionIcon")
            {
                Color = PendingHullColor,
                ToolTip = GetTimeToBuildText(pendingModule),
                OnAddedToGUIUpdateList = (component) =>
                {
                    component.Color = component.HoverColor = component.SelectedColor = component.PressedColor =
                        selectedPendingModule != pendingModule ?
                        GUIStyle.TextColorBright :
                        Color.Transparent;
                }
            };

            foreach (var otherGapInfo in pendingModule.ModulePrefab.Gaps)
            {
                //slightly dimmer gaps on pending modules (can't attach other modules to these until the module has been built)
                Color color = UnavailableGapColor * 0.5f;
                Vector2 gapPos = otherGapInfo.Rect.Location.ToVector2();
                gapPos.X += otherGapInfo.Rect.Width / 2;
                gapPos.Y -= otherGapInfo.Rect.Height / 2;
                gapPos.Y = -gapPos.Y;
                GUIButton gapFrame = new GUIButton(new RectTransform(otherGapInfo.Rect.Size.Multiply(minimapScale), hullFrame.RectTransform, Anchor.Center)
                { RelativeOffset = Vector2.Divide(gapPos, pendingModule.ModulePrefab.HullBounds.Size.ToVector2()) }, style: "ScanLines", color: color)
                {
                    OutlineColor = color,
                    OutlineThickness = 2,
                    UserData = otherGapInfo
                };
                new GUICustomComponent(new RectTransform(Vector2.One, gapFrame.RectTransform), onDraw: (sb, customComponent) => DrawPendingModuleHallway(sb, gapFrame, gapToAttachTo, otherGapInfo))
                {
                    UserData = otherGapInfo
                };
            }

            CreateMoveModuleButton(hullFrame);
            miniMapPendingModuleElements.Add(pendingModule, hullFrame);

            //this didn't exist previously -> it's a newly added module, make it flash
            if (!previousPendingModules.Any(m => m.ModulePrefab == pendingModule.ModulePrefab && m.GapId == pendingModule.GapId))
            {
                hullFrame.Flash(GUIStyle.Green);
                hullFrame.Pulsate(Vector2.One, Vector2.One * 1.2f, duration: 0.5f);
            }
        }

        miniMap.RectTransform.LocalScale = new Vector2(Zoom);
        miniMap.RectTransform.RecalculateChildren(resize: true, scale: true);
        miniMap.RectTransform.AbsoluteOffset = miniMapOffset.ToPoint();
    }

    private Color GetHullElementColor(PlacedModule module)
    {
        bool removalPending = outpostData.IsRemovalPending(module);
        return removalPending ? Color.Gray : GUIStyle.Green;
    }

    private void DrawHallway(SpriteBatch spriteBatch, PlacedModule.GapInfo thisGapInfo, GUIComponent gapElementThis)
    {
        if (thisGapInfo == null) { return; }

        PlacedModule.GapInfo? previousGapInfo = null;
        foreach (var module in outpost.OutpostModules)
        {
            if (module.ThisGap == thisGapInfo.Gap)
            {
                previousGapInfo = outpost.OutpostModules.SelectMany(m => m.AllGaps).FirstOrDefault(g => g.Gap == module.PreviousGap);
            }
            else if (module.PreviousGap == thisGapInfo.Gap)
            {
                previousGapInfo = outpost.OutpostModules.SelectMany(m => m.AllGaps).FirstOrDefault(g => g.Gap == module.ThisGap);
            }
        }
        if (previousGapInfo == null) { return; }

        var gapElementPrevious = miniMapGapElements.GetValueOrDefault(previousGapInfo);
        if (gapElementThis == null || gapElementPrevious == null) { return; }

        Rectangle hallwayRect;
        if (thisGapInfo.Gap.IsHorizontal)
        {
            int minY = Math.Min(gapElementThis.Rect.Y, gapElementPrevious.Rect.Y);
            int maxY = Math.Max(gapElementThis.Rect.Bottom, gapElementPrevious.Rect.Bottom);
            hallwayRect = new Rectangle(
                Math.Min(gapElementThis.Rect.Center.X, gapElementPrevious.Rect.Center.X),
                minY,
                Math.Abs(gapElementThis.Rect.Center.X - gapElementPrevious.Rect.Center.X),
                maxY - minY);
        }
        else
        {
            int minX = Math.Min(gapElementThis.Rect.X, gapElementPrevious.Rect.X);
            int maxX = Math.Max(gapElementThis.Rect.Right, gapElementPrevious.Rect.Right);
            hallwayRect = new Rectangle(
                minX,
                Math.Min(gapElementThis.Rect.Center.Y, gapElementPrevious.Rect.Center.Y),
                maxX - minX,
                Math.Abs(gapElementThis.Rect.Center.Y - gapElementPrevious.Rect.Center.Y));
        }

        if (GUIStyle.GetComponentStyle("ScanLines") is { } style)
        {
            style.GetDefaultSprite()?.DrawTiled(spriteBatch, hallwayRect.Location.ToVector2(), hallwayRect.Size.ToVector2(), color: HallwayColor);
        }
    }

    private void DrawPendingModuleHallway(SpriteBatch spriteBatch, GUIComponent gapElementThis, PlacedModule.GapInfo? attachedToGap, PurchasableOutpostModulePrefab.GapPositionInfo thisGapInfo)
    {
        if (attachedToGap == null) { return; }
        PlacedModule.GapInfo? previousGapInfo = miniMapGapElements.FirstOrDefault(g => g.Key == attachedToGap).Key;        
        if (previousGapInfo == null) { return; }

        if (OutpostGenerator.GetOpposingGapPosition(thisGapInfo.Position) != OutpostGenerator.GetGapPosition(attachedToGap.Gap)) { return; }

        var gapElementPrevious = miniMapGapElements.GetValueOrDefault(previousGapInfo);
        if (gapElementThis == null || gapElementPrevious == null) { return; }

        Rectangle hallwayRect;
        if (thisGapInfo.IsHorizontal)
        {
            if (Math.Abs(gapElementPrevious.Center.Y - gapElementThis.Center.Y) > gapElementThis.Rect.Height) { return; }

            int minY = Math.Min(gapElementThis.Rect.Y, gapElementPrevious.Rect.Y);
            int maxY = Math.Max(gapElementThis.Rect.Bottom, gapElementPrevious.Rect.Bottom);
            hallwayRect = new Rectangle(
                Math.Min(gapElementThis.Rect.Center.X, gapElementPrevious.Rect.Center.X),
                minY,
                Math.Abs(gapElementThis.Rect.Center.X - gapElementPrevious.Rect.Center.X),
                maxY - minY);
        }
        else
        {
            if (Math.Abs(gapElementPrevious.Center.X - gapElementThis.Center.X) > gapElementThis.Rect.Width) { return; }

            int minX = Math.Min(gapElementThis.Rect.X, gapElementPrevious.Rect.X);
            int maxX = Math.Max(gapElementThis.Rect.Right, gapElementPrevious.Rect.Right);
            hallwayRect = new Rectangle(
                minX,
                Math.Min(gapElementThis.Rect.Center.Y, gapElementPrevious.Rect.Center.Y),
                maxX - minX,
                Math.Abs(gapElementThis.Rect.Center.Y - gapElementPrevious.Rect.Center.Y));
        }

        if (GUIStyle.GetComponentStyle("ScanLines") is { } style)
        {
            style.GetDefaultSprite()?.DrawTiled(spriteBatch, hallwayRect.Location.ToVector2(), hallwayRect.Size.ToVector2(), color: HallwayColor);
        }
    }

    private void CreateMoveModuleButton(GUIComponent parent)
    {
        var moveButton = new GUIButton(new RectTransform(new Point(GUI.IntScale(64)), parent.RectTransform, Anchor.Center) { IsFixedSize = true }, style: "MoveModuleButton")
        {
            OnButtonDown = () =>
            {
                if (parent.UserData is PlacedModule placedModule)
                {
                    movingModule = placedModule;
                    startDragCursorPosition = PlayerInput.MousePosition;
                    startDragElementPositions.Clear();
                    foreach (var moduleElement in miniMapModuleElements)
                    {
                        startDragElementPositions.Add(moduleElement.Value, moduleElement.Value.RectTransform.RelativeOffset);
                    }
                    foreach (var pendingModuleElement in miniMapPendingModuleElements)
                    {
                        startDragElementPositions.Add(pendingModuleElement.Value, pendingModuleElement.Value.RectTransform.RelativeOffset);
                    }
                }
                else if (parent.UserData is OutpostBuilder.PendingOutpostModule pendingModule)
                {
                    movingPendingModule = pendingModule;
                    startDragCursorPosition = PlayerInput.MousePosition;
                    startDragElementPositions.Clear();
                    startDragElementPositions.Add(parent, parent.RectTransform.RelativeOffset);
                }
                return true;
            },
            OnAddedToGUIUpdateList = (GUIComponent component) =>
            {
                //keep the size of the button the same regardless of zoom (= regardless of the scale of the minimap)
                component.RectTransform.LocalScale = new Vector2(
                    1.0f / component.RectTransform.Parent.Scale.X, 
                    1.0f / component.RectTransform.Parent.Scale.Y);
            },
            Visible = false
        };
    }

    private void DrawMiniMap(SpriteBatch spriteBatch, GUICustomComponent customComponent) => customComponent.DrawChildren(spriteBatch, recursive: true);

    public void ResetMiniMapOffset()
    {
        if (miniMap != null)
        {
            miniMapOffset = miniMap.Parent.Rect.DivideSize(2.0f).ToVector2();
            refreshMiniMapOffsetAndZoom = true;
        }
    }

    private void UpdateMiniMap(float deltaTime, GUICustomComponent customComponent)
    {
        if (GameMain.NetworkMember is { IsClient: true })
        {
            //deselect modules and recreate the minimap if the client gains or loses permissions to manage the outpost
            bool allowedToManageOutpost = OutpostBuilder.AllowedToManageOutpost();
            if (hadPermissionsToManageOutpost.HasValue &&
                hadPermissionsToManageOutpost.Value != allowedToManageOutpost)
            {
                DeselectAll();
            }
            hadPermissionsToManageOutpost = allowedToManageOutpost;
        }

        foreach ((OutpostBuilder.PendingOutpostModule pendingModule, GUIComponent frame) in miniMapPendingModuleElements)
        {
            float t = (MathF.Sin((float)Timing.TotalTime * 5.51f) + 1.0f) / 2.0f;
            frame.Color = frame.OutlineColor = Color.Lerp(Color.Transparent, PendingHullColor, t);
        }
        foreach ((PlacedModule placedModule, GUIComponent frame) in miniMapModuleElements)
        {
            if (!outpostData.IsRemovalPending(placedModule)) { continue; }
            float t = (MathF.Sin((float)Timing.TotalTime * 2.0f) + 1.0f) / 2.0f;
            frame.Color = frame.OutlineColor = Color.Lerp(Color.Gray, PendingRemovalHullColor, t);
        }

        bool mouseOnMiniMap = GUI.IsMouseOn(customComponent);
        if (mouseOnMiniMap)
        {
            if (PlayerInput.MidButtonDown())
            {
                ItemSwapUI.ClearSwapUI();
                dragMiniMapStart = PlayerInput.MousePosition;            
            }

            if (Math.Abs(PlayerInput.ScrollWheelSpeed) > 0)
            {
                ItemSwapUI.ClearSwapUI();
                targetZoom = Math.Clamp(targetZoom + PlayerInput.ScrollWheelSpeed / 1000.0f * Zoom, MinZoom, MaxZoom);
            }
        }

        if (!MathUtils.NearlyEqual(targetZoom, Zoom))
        {
            float newZoom = zoom + (targetZoom - zoom) * 0.5f;
            if (miniMap != null)
            {
                Vector2 size = miniMap.Rect.Size.ToVector2();
                miniMapOffset += -(newZoom - zoom) * 
                    Vector2.Divide(PlayerInput.MousePosition - miniMap.Parent.Rect.Center.ToVector2(), size) * size;
            }
            Zoom = newZoom;
            refreshMiniMapOffsetAndZoom |= true;
        }

        if (dragMiniMapStart is { } dragStart)
        {
            if (draggingMiniMap || Vector2.DistanceSquared(dragStart, PlayerInput.MousePosition) > GUI.IntScale(DragTreshold * DragTreshold))
            {
                miniMapOffset.X += PlayerInput.MouseSpeed.X;
                miniMapOffset.Y += PlayerInput.MouseSpeed.Y;

                refreshMiniMapOffsetAndZoom = true;
                draggingMiniMap = true;
            }
        }

        if (!PlayerInput.MidButtonHeld())
        {
            dragMiniMapStart = null;
            draggingMiniMap = false;
        }

        float moveSpeed = 20.0f;
        Vector2 moveAmount = Vector2.Zero;
        if (GUI.KeyboardDispatcher.Subscriber == null)
        {
            if (PlayerInput.KeyDown(InputType.Left)) { moveAmount += Vector2.UnitX; }
            if (PlayerInput.KeyDown(InputType.Right)) { moveAmount -= Vector2.UnitX; }
            if (PlayerInput.KeyDown(InputType.Up)) { moveAmount += Vector2.UnitY; }
            if (PlayerInput.KeyDown(InputType.Down)) { moveAmount -= Vector2.UnitY; }
        }
        if (moveAmount.LengthSquared() > 0.01f)
        {
            miniMapOffset += moveAmount * moveSpeed;
            refreshMiniMapOffsetAndZoom = true;
        }

        if (PlayerInput.PrimaryMouseButtonClicked() && GUI.MouseOn == customComponent)
        {
            DeselectAll();
        }

        if (selectedModulePrefab != null && modulePlacementElement != null)
        {
            UpdatePlacingNewModule(mouseOnMiniMap);
        }
        else if (modulePlacementElement != null)
        {
            modulePlacementElement.Parent?.RemoveChild(modulePlacementElement);
            modulePlacementElement = null;
        }

        if (recreateMiniMap && miniMapContainer != null)
        {
            CreateMiniMap(outpost, location, miniMapContainer);
            recreateMiniMap = false;
        }

        if (refreshMiniMapOffsetAndZoom)
        {
            if (miniMap != null && miniMapContainer != null)
            {
                miniMap.RectTransform.LocalScale = new Vector2(Zoom);
                miniMap.RectTransform.RecalculateChildren(resize: true, scale: true);

                Point min = new Point(int.MaxValue, int.MaxValue);
                Point max = new Point(0, 0);
                foreach (var child in miniMap.GetAllChildren<GUIButton>())
                {
                    min = new Point(Math.Min(child.Rect.X, min.X), Math.Min(child.Rect.Y, min.Y));
                    max = new Point(Math.Max(child.Rect.Right, max.X), Math.Max(child.Rect.Bottom, max.Y));
                }

                Rectangle viewArea = miniMap.Parent.Rect;
                //shrink the view area a bit (some margin at the sides, some point of the outpost must be visible within this area)
                viewArea.Inflate(-viewArea.Size.X / 10, -viewArea.Size.Y / 10);

                if (min.X > viewArea.Right)
                {
                    miniMapOffset.X += viewArea.Right - min.X;
                }
                else if (max.X < viewArea.X)
                {
                    miniMapOffset.X += viewArea.X - max.X;
                }

                if (min.Y > viewArea.Bottom)
                {
                    miniMapOffset.Y += viewArea.Bottom - min.Y;
                }
                else if (max.Y < viewArea.Y)
                {
                    miniMapOffset.Y += viewArea.Y - max.Y;
                }

                miniMap.RectTransform.AbsoluteOffset = miniMapOffset.ToPoint();
            }
            refreshMiniMapOffsetAndZoom = false;
        }

        if (PlayerInput.SecondaryMouseButtonClicked())
        {
            DeselectAll();
        }

        if (moduleInfoPanel != null)
        {
            moduleInfoPanel.Visible = !ItemSwapUI.IsSwapUIVisible &&
                                      (selectedModulePrefab != null
                                       || selectedModule != null
                                       || selectedPendingModule != null);
        }

        UpdateMoveExistingModule();
        UpdateMovePendingModule();

#if DEBUG
        if (PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.U) && miniMapContainer != null)
        {
            CreateMiniMap(outpost, location, miniMapContainer);
            UpdateModuleList(moduleList!.Content);
        }

        if (PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.I))
        {
            ResetMiniMapOffset();
        }
#endif
    }

    private Vector2 ScreenPosToUI(Vector2 pos)
    {
        if (miniMap == null) { return pos; }
        return Vector2.Divide(pos, miniMap.Rect.Size.ToVector2());
    }

    private Vector2 ScreenPosToWorld(Vector2 pos)
    {
        return (pos / minimapScale / zoom).FlipY();
    }

    public Vector2 WorldPosToUI(Vector2 pos)
    {
        return ScreenPosToUI((pos * minimapScale).FlipY());
    }

    public Vector2 UIToWorldPos(Vector2 pos)
    {
        if (miniMap == null) { return pos; }
        return (pos * miniMap.Rect.Size.ToVector2() / minimapScale);
    }

    public void DrawModule(SpriteBatch spriteBatch, GUICustomComponent customComponent, SubmarineInfo moduleInfo, Color? color = null)
    {
        Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
        spriteBatch.End();

        if (blueprintSprites.TryGetValue(moduleInfo, out SubmarinePreview.SubmarineTexture? submarineTexture) && submarineTexture != null)
        {
            Texture2D texture = submarineTexture.Texture2D;

            spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, blendState: BlendState.NonPremultiplied, rasterizerState: GameMain.ScissorTestEnable);
            GameMain.GameScreen.BlueprintEffect.Parameters["width"].SetValue((float)texture.Width);
            GameMain.GameScreen.BlueprintEffect.Parameters["height"].SetValue((float)texture.Height);
            Color blueprintColor = color ?? Color.White * 0.5f;

            Vector2 desiredSizeOnUI = (submarineTexture.Bounds.Max - submarineTexture.Bounds.Min) * minimapScale * zoom;
            float scale = Math.Max(desiredSizeOnUI.X / texture.Width, desiredSizeOnUI.Y / texture.Height);

            Vector2 boundsCenter = (submarineTexture.Bounds.Max + submarineTexture.Bounds.Min) / 2;
            Vector2 offset = -boundsCenter;
            
            spriteBatch.Draw(texture, customComponent.Parent.Rect.Center.ToVector2() - offset * minimapScale * zoom, sourceRectangle: null, blueprintColor,
                rotation: 0.0f, origin: new Vector2(texture.Width, texture.Height) / 2.0f,
                scale: new Vector2(scale),
                effects: SpriteEffects.None,
                layerDepth: 0.0f);

            spriteBatch.End();
        }
        spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
        spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
    }
    #endregion

    #region Placing and moving modules
    private void UpdatePlacingNewModule(bool mouseOnMiniMap)
    {
        if (selectedModulePrefab == null || modulePlacementElement == null) { return; }

        if (PlayerInput.SecondaryMouseButtonClicked())
        {
            selectedModulePrefab = null;
            return;
        }

        modulePlacementElement.Visible = mouseOnMiniMap;
        modulePlacementElement.ToolTip = string.Empty;
        if (!mouseOnMiniMap) { return; }

        modulePlacementElement.Selected = false;
        int snapDistance = GUI.IntScale(15);
        modulePlacementElement.RectTransform.ScreenSpaceOffset =
            PlayerInput.MousePosition.ToPoint() - modulePlacementElement.Parent.RectTransform.TopLeft;

        //gap highlighted -> check if the cursor is still close enough to keep placing the module and stretching the hallway
        if (highlightedGapForModulePlacement != null)
        {
            PlacedModule.GapInfo highlightedGapInfo = miniMapGapElements.FirstOrDefault(g => g.Value == highlightedGapForModulePlacement).Key;
            if (highlightedGapInfo != null)
            {
                OutpostModuleInfo.GapPosition gapPosition = OutpostGenerator.GetGapPosition(highlightedGapInfo.Gap);
                //deselect highlighted gap if we move the cursor too far
                if (highlightedGapInfo.Gap.IsHorizontal)
                {
                    //deselect if at the "wrong side" of the gap, or vertically too far (not aligned with the door)
                    if (gapPosition == OutpostModuleInfo.GapPosition.Right && PlayerInput.MousePosition.X < highlightedGapForModulePlacement.Center.X - snapDistance ||
                        gapPosition == OutpostModuleInfo.GapPosition.Left && PlayerInput.MousePosition.X > highlightedGapForModulePlacement.Center.X + snapDistance ||
                        Math.Abs(highlightedGapForModulePlacement.Center.Y - PlayerInput.MousePosition.Y) > modulePlacementElement.Rect.Height / 2)
                    {
                        highlightedGapForModulePlacement = null;
                    }
                }
                else
                {
                    //deselect if at the "wrong side" of the gap, or vertically too far (not aligned with the door)
                    if (gapPosition == OutpostModuleInfo.GapPosition.Top && PlayerInput.MousePosition.Y > highlightedGapForModulePlacement.Center.Y + snapDistance ||
                        gapPosition == OutpostModuleInfo.GapPosition.Bottom && PlayerInput.MousePosition.Y < highlightedGapForModulePlacement.Center.Y - snapDistance ||
                        Math.Abs(highlightedGapForModulePlacement.Center.X - PlayerInput.MousePosition.X) > modulePlacementElement.Rect.Width / 2)
                    {
                        highlightedGapForModulePlacement = null;
                    }
                }
            }
        }

        //no gap highlighted -> check if the cursor is close enough to any available gaps
        if (highlightedGapForModulePlacement == null)
        {
            foreach ((PlacedModule.GapInfo gapInfo, GUIComponent gapElement) in miniMapGapElements)
            {
                if (!gapElement.Enabled) { continue; }
                //gap must be disabled (currently unused) for module placement
                if (gapInfo.Gap is not { IsDisabled: true } gap) { continue; }

                //module gap belongs to marked for removal -> can't attach here
                var gapModule = outpost.OutpostModules.FirstOrDefault(m => m.AllGaps.Contains(gapInfo));
                if (gapModule != null && outpostData.IsRemovalPending(gapModule)) { continue; }

                //another module waiting to be attached here, can't attach
                if (outpostData.PendingOutpostModules.Any(m => m.Gap == gap)) { continue; }

                var inflatedRect = modulePlacementElement.Rect;
                inflatedRect.Inflate(snapDistance, snapDistance);
                if (inflatedRect.Intersects(gapElement.Rect) &&
                    (highlightedGapForModulePlacement == null || Vector2.DistanceSquared(gapElement.Center, PlayerInput.MousePosition) < Vector2.DistanceSquared(highlightedGapForModulePlacement.Center, PlayerInput.MousePosition)))
                {
                    highlightedGapForModulePlacement = gapElement;
                }
            }
        }
        else
        {
            //gap still highlighted -> position the module and extend the hallway to it
            PlacedModule.GapInfo highlightedGapInfo = miniMapGapElements.FirstOrDefault(g => g.Value == highlightedGapForModulePlacement).Key;
            if (highlightedGapInfo != null)
            {
                modulePlacementElement.Selected = true;
                OutpostModuleInfo.GapPosition gapPosition = OutpostGenerator.GetGapPosition(highlightedGapInfo.Gap);
                OutpostModuleInfo.GapPosition opposingGapPosition = OutpostGenerator.GetOpposingGapPosition(gapPosition);

                //find which gap in the module we're placing should attach to the highlighted gap
                var opposingGaps = selectedModulePrefab.Gaps.Where(g => g.Position == OutpostGenerator.GetOpposingGapPosition(gapPosition));
                PurchasableOutpostModulePrefab.GapPositionInfo? closestThisGap = null;
                float closestDist = float.PositiveInfinity;
                foreach (var gapElement in modulePlacementElement.Children)
                {
                    if (gapElement.UserData is PurchasableOutpostModulePrefab.GapPositionInfo { } gapInfo)
                    {
                        if (gapInfo.Position != opposingGapPosition) { continue; }
                        float dist = gapInfo.IsHorizontal ?
                            Math.Abs(gapElement.Center.Y - highlightedGapForModulePlacement.Center.Y) :
                            Math.Abs(gapElement.Center.X - highlightedGapForModulePlacement.Center.X);
                        if (dist < closestDist)
                        {
                            closestThisGap = gapInfo;
                            closestDist = dist;
                        }
                    }
                }

                if (closestThisGap is { } thisGap)
                {
                    Vector2 gapDirection = OutpostGenerator.GetGapDirection(gapPosition);
                    bool isHorizontal = Math.Abs(gapDirection.X) > Math.Abs(gapDirection.Y);

                    Vector2 thisGapPos = new Vector2(thisGap.Rect.X + thisGap.Rect.Width / 2, thisGap.Rect.Y - thisGap.Rect.Height / 2);
                    //position to the gap we're attaching to
                    modulePlacementElement.RectTransform.ScreenSpaceOffset =
                        highlightedGapForModulePlacement.Rect.Center
                        - modulePlacementElement.Parent.RectTransform.TopLeft;
                    //move so the gap in this module aligns with the one we're attaching to
                    modulePlacementElement.RectTransform.ScreenSpaceOffset
                        -= (thisGapPos * zoom * minimapScale).FlipY().ToPoint();

                    Vector2 mouseDiffFromGap = PlayerInput.MousePosition - highlightedGapForModulePlacement.Center;
                    //take the distance from the gap to the center of the module we're placing: that shouldn't count for moving the module (module should only move when the cursor is brought further than that)
                    Vector2 moduleDiffFromGap = highlightedGapForModulePlacement.Center - modulePlacementElement.Rect.Center.ToVector2();
                    if (thisGap.IsHorizontal)
                    {
                        mouseDiffFromGap.Y = 0;
                        moduleDiffFromGap.Y = 0;
                    }
                    else
                    {
                        mouseDiffFromGap.X = 0;
                        moduleDiffFromGap.X = 0;
                    }
                    Vector2 hallwayLength =
                        mouseDiffFromGap.Length() * gapDirection - moduleDiffFromGap.Length() * gapDirection;

                    hallwayLength = Vector2.Clamp(
                        hallwayLength,
                        -Vector2.One * OutpostBuilder.PendingOutpostModule.MaxHallwayLength,
                        Vector2.One * OutpostBuilder.PendingOutpostModule.MaxHallwayLength);

                    //hallway length must be in the same direction as the "direction of the module" from the gap we're attaching to
                    if (Vector2.Dot(hallwayLength, gapDirection) > 0)
                    {
                        modulePlacementElement.RectTransform.ScreenSpaceOffset
                            -= hallwayLength.FlipY().ToPoint();
                    }
                    else
                    {
                        //if not, set hallway length to zero
                        hallwayLength = Vector2.Zero;
                    }

                    //flash the target gap in green to indicate it's ok to attach to
                    if (highlightedGapForModulePlacement.FlashTimer <= 0.0f)
                    {
                        highlightedGapForModulePlacement.Flash(GUIStyle.Green, useRectangleFlash: true, flashRectInflate: Vector2.One * 2);
                    }

                    bool overlapFound =
                        ModuleElementOverlapsWithOtherModules(modulePlacementElement, attachedToGap: highlightedGapInfo.Gap);
                    if (ModuleElementBelowAirlock(modulePlacementElement))
                    {
                        overlapFound = true;
                        modulePlacementElement.ToolTip =
                           RichString.Rich($"‖color:gui.red‖{TextManager.Get("outpostbuilderui.cantplacebelowdocking")}‖end‖");
                    }
                    if (ModuleElementAboveRestrictedModule(modulePlacementElement, out GUIComponent? restrictingModuleElement))
                    {
                        overlapFound = true;
                        //if the module we're placing is the restricted one, the problem is that it'd be below another module
                        string errorTextTag = restrictingModuleElement == modulePlacementElement ?
                            "outpostbuilderui.cantplacebelowanother" :
                            "outpostbuilderui.cantplaceaboveanother";
                        modulePlacementElement.ToolTip =
                           RichString.Rich($"‖color:gui.red‖{TextManager.Get(errorTextTag)}‖end‖");
                        if (restrictingModuleElement is { FlashTimer: <= 0.0f})
                        {
                            restrictingModuleElement.Flash(GUIStyle.Red);
                        }
                    }

                    modulePlacementElement.Color = modulePlacementElement.SelectedColor = modulePlacementElement.HoverColor =
                        modulePlacementElement.OutlineColor = overlapFound ? GUIStyle.Red : Color.White;
                    if (PlayerInput.PrimaryMouseButtonClicked())
                    {
                        if (!overlapFound)
                        {
                            if (OutpostBuilder.TryPlaceModule(outpostData, selectedModulePrefab,
                                thisGap,
                                highlightedGapInfo.Gap,
                                hallwayLength: highlightedGapInfo.Gap.IsHorizontal ? -ScreenPosToWorld(hallwayLength).X : ScreenPosToWorld(hallwayLength).Y,
                                createNetworkEvent: true))
                            {
                                UpdateModuleLimitText(selectedModulePrefab.Category);
                            }
                        }
                        moduleList?.Deselect();
                        highlightedGapForModulePlacement = null;
                        selectedModulePrefab = null;
                        recreateMiniMap = true;
                        OutpostManagementUI.Instance?.OutpostOverviewUI?.Refresh();
                    }
                }
            }
        }
    }

    private List<PlacedModule> modulesToMove = [];
    private List<OutpostBuilder.PendingOutpostModule> pendingModulesToMove = [];
    private readonly List<GUIComponent> moduleElementsToMove = [];

    private void UpdateMoveExistingModule()
    {
        if (movingModule?.PreviousGap == null || miniMap == null) { return; }

        //cannot move the module if there's no door between the modules (which is the case when modules connect by staircases instead of ladders)
        if (movingModule.PreviousGap.ConnectedDoor == null) { return; }
        
        GUIComponent? movingElement = miniMapModuleElements.GetValueOrDefault(movingModule);
        if (movingElement == null) { return; } 
            
        Vector2 moveAmountScreen = PlayerInput.MousePosition - startDragCursorPosition;
        Vector2 moveAmountUI = ScreenPosToUI(moveAmountScreen);
        Vector2 moveAmountWorld = ScreenPosToWorld(moveAmountScreen);

        Vector2 thisGapPos = movingModule.ThisGap.WorldPosition + movingModule.TemporaryMoveOffset;
        Vector2 prevGapPos = movingModule.PreviousGap.WorldPosition + movingModule.PreviousModule.TemporaryMoveOffset;

        //make sure we don't resize the hallway in the "wrong direction" (making the module go inside the module we're attaching to)
        float hallwayLength = 0.0f;
        if (movingModule.PreviousGap.IsHorizontal)
        {
            moveAmountUI.Y = 0.0f;
            moveAmountWorld.Y = 0.0f;
            hallwayLength = (thisGapPos.X - prevGapPos.X) + moveAmountWorld.X;
            if (movingModule.ThisGapPosition == OutpostModuleInfo.GapPosition.Left)
            {
                if (hallwayLength < 0.0f)
                {
                    moveAmountWorld.X = Math.Max(moveAmountWorld.X, prevGapPos.X - thisGapPos.X);
                }
                else if (hallwayLength > OutpostBuilder.PendingOutpostModule.MaxHallwayLength)
                {
                    moveAmountWorld.X += OutpostBuilder.PendingOutpostModule.MaxHallwayLength - hallwayLength;
                }
                moveAmountUI.X = WorldPosToUI(moveAmountWorld).X * zoom;
            }
            else if (movingModule.ThisGapPosition == OutpostModuleInfo.GapPosition.Right)
            {
                if (hallwayLength > 0.0f)
                {
                    moveAmountWorld.X = Math.Min(moveAmountWorld.X, prevGapPos.X - thisGapPos.X);
                }
                else if (hallwayLength < -OutpostBuilder.PendingOutpostModule.MaxHallwayLength)
                {
                    moveAmountWorld.X += -OutpostBuilder.PendingOutpostModule.MaxHallwayLength - hallwayLength;
                }
                moveAmountUI.X = WorldPosToUI(moveAmountWorld).X * zoom;
            }
        }
        else
        {
            moveAmountUI.X = 0.0f;
            moveAmountWorld.X = 0.0f;
            hallwayLength = (thisGapPos.Y - prevGapPos.Y) + moveAmountWorld.Y;
            if (movingModule.ThisGapPosition == OutpostModuleInfo.GapPosition.Top)
            {
                if (hallwayLength > 0.0f)
                {
                    moveAmountWorld.Y = Math.Min(moveAmountWorld.Y, thisGapPos.Y - prevGapPos.Y);
                }
                else if (hallwayLength < -OutpostBuilder.PendingOutpostModule.MaxHallwayLength)
                {
                    moveAmountWorld.Y += -OutpostBuilder.PendingOutpostModule.MaxHallwayLength - hallwayLength;
                }
                moveAmountUI.Y = WorldPosToUI(moveAmountWorld).Y * zoom;
            }
            else if (movingModule.ThisGapPosition == OutpostModuleInfo.GapPosition.Bottom)
            {
                if (hallwayLength < 0.0f)
                {
                    moveAmountWorld.Y = Math.Max(moveAmountWorld.Y, prevGapPos.Y - thisGapPos.Y);
                }
                else if (hallwayLength > OutpostBuilder.PendingOutpostModule.MaxHallwayLength)
                {
                    moveAmountWorld.Y += OutpostBuilder.PendingOutpostModule.MaxHallwayLength - hallwayLength;
                }
                moveAmountUI.Y = WorldPosToUI(moveAmountWorld).Y * zoom;
            }
        }

        moduleElementsToMove.Clear();
        OutpostBuilder.GetModulesToMove(outpost, outpostData, movingModule, ref modulesToMove, ref pendingModulesToMove);
        foreach (var module in modulesToMove)
        {
            if (miniMapModuleElements.TryGetValue(module, out GUIComponent? moduleElement))
            {
                moduleElementsToMove.Add(moduleElement);
            }
        }
        foreach (var pendingModule in pendingModulesToMove)
        {
            if (miniMapPendingModuleElements.TryGetValue(pendingModule, out GUIComponent? moduleElement))
            {
                moduleElementsToMove.Add(moduleElement);
            }
        }

        bool overlapFound =
            !OutpostBuilder.CanMoveModulesWithoutOverlaps(outpost, modulesToMove, pendingModulesToMove, moveAmountWorld) ||
            pendingModulesToMove.Any(PendingModuleOverlapsWithOtherModules) ||
            modulesToMove.Any(PlacedModuleOverlapsWithPendingModules) ||
            ModuleElementOverlapsWithOtherModules(movingElement, movingModule.PreviousGap)  ||
            modulesToMove.Any(MovedModuleOverlapsWithStationaryHallways) ||
            (moveAmountWorld.Y < 0 && moduleElementsToMove.Any(ModuleElementBelowAirlock)) ||
            moduleElementsToMove.Any(m => ModuleElementAboveRestrictedModule(m, out _));

        //color modules red if there were overlaps
        foreach (var module in modulesToMove)
        {
            var moduleElement = miniMapModuleElements.GetValueOrDefault(module);
            MoveElement(moduleElement, defaultColor: GetHullElementColor(module));
        }
        foreach (var pendingModule in pendingModulesToMove)
        {
            var pendingModuleElement = miniMapPendingModuleElements.GetValueOrDefault(pendingModule);
            MoveElement(pendingModuleElement, defaultColor: PendingHullColor);
        }

        void MoveElement(GUIComponent? element, Color defaultColor)
        {
            if (element != null && startDragElementPositions.TryGetValue(element, out Vector2 startPos))
            {
                element.RectTransform.RelativeOffset = startPos + moveAmountUI;
                element.Color = element.OutlineColor = overlapFound ? GUIStyle.Red : PendingHullColor;
            }
        }

        if (!PlayerInput.PrimaryMouseButtonHeld())
        {
            //reset colors
            foreach (var module in outpost.OutpostModules)
            {
                var moduleElement = miniMapModuleElements.GetValueOrDefault(module);
                if (moduleElement != null)
                {
                    moduleElement.Color = moduleElement.OutlineColor = GetHullElementColor(module);
                }
            }
            foreach (var module in modulesToMove)
            {
                if (!overlapFound)
                {
                    module.TemporaryMoveOffset += moveAmountWorld;
                    moduleElementsToMove.ForEach(e => e.Flash(GUIStyle.Green));
                }
                else
                {
                    moduleElementsToMove.ForEach(e => e.Flash(GUIStyle.Red));
                    var moduleElement = miniMapModuleElements.GetValueOrDefault(module);
                    if (moduleElement != null && startDragElementPositions.TryGetValue(moduleElement, out Vector2 startPos))
                    {
                        moduleElement.RectTransform.RelativeOffset = startPos;
                    }
                }
            }
            bool moved = moveAmountWorld.LengthSquared() > 0.001f;
            if (moved && !overlapFound)
            {
                OutpostBuilder.ClientWriteMoveOutpostModule(movingModule);
            }
            movingModule = null;
            recreateMiniMap = moved;
        }
    }

    private void UpdateMovePendingModule()
    {
        if (movingPendingModule == null || miniMap == null) { return; }

        GUIComponent? movingElement = miniMapPendingModuleElements.GetValueOrDefault(movingPendingModule);
        if (movingElement == null || movingPendingModule.Gap == null || movingPendingModule.ModulePrefab == null) { return; }

        Vector2 moveAmountScreen = PlayerInput.MousePosition - startDragCursorPosition;
        Vector2 moveAmountUI = ScreenPosToUI(moveAmountScreen);
        Vector2 moveAmountWorld = ScreenPosToWorld(moveAmountScreen);

        //make sure we don't resize the hallway in the "wrong direction" (making the module go inside the module we're attaching to)
        //or past the max hallway length (the length gets clamped when set, so the UI element must be clamped to match)
        float hallwayLength;
        if (movingPendingModule.Gap.IsHorizontal)
        {
            moveAmountUI.Y = 0.0f;
            moveAmountWorld.Y = 0.0f;
            hallwayLength = movingPendingModule.HallwayLength + moveAmountWorld.X;
            if (movingPendingModule.SideToAttachTo == OutpostModuleInfo.GapPosition.Right)
            {
                if (hallwayLength < 0.0f)
                {
                    moveAmountWorld.X = Math.Max(moveAmountWorld.X, -movingPendingModule.HallwayLength);
                }
                else if (hallwayLength > OutpostBuilder.PendingOutpostModule.MaxHallwayLength)
                {
                    moveAmountWorld.X = OutpostBuilder.PendingOutpostModule.MaxHallwayLength - movingPendingModule.HallwayLength;
                }
                moveAmountUI.X = WorldPosToUI(moveAmountWorld).X * zoom;
            }
            else if (movingPendingModule.SideToAttachTo == OutpostModuleInfo.GapPosition.Left)
            {
                if (hallwayLength > 0.0f)
                {
                    moveAmountWorld.X = Math.Min(moveAmountWorld.X, -movingPendingModule.HallwayLength);
                }
                else if (hallwayLength < -OutpostBuilder.PendingOutpostModule.MaxHallwayLength)
                {
                    moveAmountWorld.X = -OutpostBuilder.PendingOutpostModule.MaxHallwayLength - movingPendingModule.HallwayLength;
                }
                moveAmountUI.X = WorldPosToUI(moveAmountWorld).X * zoom;
            }
        }
        else
        {
            moveAmountUI.X = 0.0f;
            moveAmountWorld.X = 0.0f;
            hallwayLength = movingPendingModule.HallwayLength + moveAmountWorld.Y;
            if (movingPendingModule.SideToAttachTo == OutpostModuleInfo.GapPosition.Top)
            {
                if (hallwayLength < 0.0f)
                {
                    moveAmountWorld.Y = Math.Max(moveAmountWorld.Y, -movingPendingModule.HallwayLength);
                }
                else if (hallwayLength > OutpostBuilder.PendingOutpostModule.MaxHallwayLength)
                {
                    moveAmountWorld.Y = OutpostBuilder.PendingOutpostModule.MaxHallwayLength - movingPendingModule.HallwayLength;
                }
                moveAmountUI.Y = WorldPosToUI(moveAmountWorld).Y * zoom;
            }
            else if (movingPendingModule.SideToAttachTo == OutpostModuleInfo.GapPosition.Bottom)
            {
                if (hallwayLength > 0.0f)
                {
                    moveAmountWorld.Y = Math.Min(moveAmountWorld.Y, -movingPendingModule.HallwayLength);
                }
                else if (hallwayLength < -OutpostBuilder.PendingOutpostModule.MaxHallwayLength)
                {
                    moveAmountWorld.Y = -OutpostBuilder.PendingOutpostModule.MaxHallwayLength - movingPendingModule.HallwayLength;
                }
                moveAmountUI.Y = WorldPosToUI(moveAmountWorld).Y * zoom;
            }
        }

        var pendingModuleElement = miniMapPendingModuleElements.GetValueOrDefault(movingPendingModule);
        bool overlapFound = false;
        if (pendingModuleElement != null)
        {
            overlapFound =
                ModuleElementOverlapsWithOtherModules(pendingModuleElement, attachedToGap: movingPendingModule.Gap) ||
                ModuleElementBelowAirlock(pendingModuleElement) ||
                ModuleElementAboveRestrictedModule(pendingModuleElement, out _);
            //color modules red if there were overlaps    
            if (startDragElementPositions.TryGetValue(pendingModuleElement, out Vector2 startPos))
            {
                pendingModuleElement.RectTransform.RelativeOffset = startPos + moveAmountUI;
            }
            pendingModuleElement.Color = overlapFound ? GUIStyle.Red : PendingHullColor;
            pendingModuleElement.OutlineColor = pendingModuleElement.Color;
        }

        if (!PlayerInput.PrimaryMouseButtonHeld())
        {
            //reset colors
            if (!overlapFound)
            {
                movingPendingModule.HallwayLength += movingPendingModule.Gap.IsHorizontal ? moveAmountWorld.X : moveAmountWorld.Y;
                if (pendingModuleElement != null)
                {
                    moduleElementsToMove.ForEach(e => e.Flash(GUIStyle.Green));
                }
                OutpostBuilder.ClientWriteMovePendingOutpostModule(outpostData, movingPendingModule);
            }
            else
            {
                if (pendingModuleElement != null)
                {
                    moduleElementsToMove.ForEach(e => e.Flash(GUIStyle.Red));
                    if (startDragElementPositions.TryGetValue(pendingModuleElement, out Vector2 startPos))
                    {
                        pendingModuleElement.RectTransform.RelativeOffset = startPos;
                    }
                }
            }
            if (pendingModuleElement != null)
            {
                pendingModuleElement.Color = pendingModuleElement.OutlineColor = PendingHullColor;
            }
            movingPendingModule = null;
        }
    }

    private bool PendingModuleOverlapsWithOtherModules(OutpostBuilder.PendingOutpostModule pendingModule)
    {
        if (miniMapPendingModuleElements.GetValueOrDefault(pendingModule) is GUIComponent element && pendingModule.Gap != null)
        {
            return ModuleElementOverlapsWithOtherModules(element, pendingModule.Gap);
        }
        return false;
    }

    private bool ModuleElementOverlapsWithOtherModules(GUIComponent moduleElement, Gap attachedToGap)
    {
        if (HallwayOverlapsWithPlacedModules(moduleElement, attachedToGap) ||
            HallwayOverlapsWithOtherHallways(moduleElement, attachedToGap) ||
            UIElementOverlapsWithHallways(moduleElement))
        {
            return true;
        }

        return
            ModuleElementOverlapsWithPlacedModules(moduleElement, attachedToGap) ||
            ModuleElementOverlapsWithPendingModules(moduleElement);
    }

    /// <summary>
    /// <see cref="OutpostGenerator.OverlapToleranceBetweenAdjacentModules"/> converted from world space to screen space.
    /// </summary>
    private int OverlapToleranceOnScreen => (int)Math.Ceiling(OutpostGenerator.OverlapToleranceBetweenAdjacentModules * minimapScale * zoom);

    private bool ModuleElementOverlapsWithPlacedModules(GUIComponent moduleElement, Gap attachedToGap)
    {
        foreach ((PlacedModule outpostModule, GUIComponent element) in miniMapModuleElements)
        {
            if (element == moduleElement) { continue; }

            Rectangle thisModuleRect = moduleElement.Rect;
            Rectangle otherModuleRect = element.Rect;

            bool attachedToOtherModule = outpostModule.IsGapInThisModule(attachedToGap);
            bool otherModuleAttachedToThis =
                outpostModule.PreviousModule != null &&
                miniMapModuleElements.TryGetValue(outpostModule.PreviousModule, out GUIComponent? prevModuleElement) &&
                prevModuleElement == moduleElement;
            if (attachedToOtherModule || otherModuleAttachedToThis) 
            {
                //more tolerance for overlaps between adjacent modules, the same way as during outpost generation
                Vector2 inflateAmount = new Vector2(-OverlapToleranceOnScreen);
                thisModuleRect.Inflate(inflateAmount);
                otherModuleRect.Inflate(inflateAmount);

            }
            if (thisModuleRect.Intersects(otherModuleRect))
            {
                return true;
            }
        }
        return false;
    }

    private bool PlacedModuleOverlapsWithPendingModules(PlacedModule placedModule)
    {
        if (miniMapModuleElements.GetValueOrDefault(placedModule) is not GUIComponent element)
        {
            return false;
        }
        foreach ((OutpostBuilder.PendingOutpostModule pendingModule, GUIComponent pendingModuleElement) in miniMapPendingModuleElements)
        {
            if (placedModule.IsGapInThisModule(pendingModule.Gap)) { continue; }
            if (pendingModuleElement.Rect.Intersects(element.Rect))
            {
                return true;
            }
        }
        return false;
    }

    private bool ModuleElementOverlapsWithPendingModules(GUIComponent moduleElement)
    {
        foreach ((_, GUIComponent element) in miniMapPendingModuleElements)
        {
            if (element == moduleElement) { continue; }
            if (moduleElement.Rect.Intersects(element.Rect))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks whether the hallway of a module that's being moved along with <see cref="movingModule"/> overlaps with the modules or hallways that stay in place,
    /// or whether the module overlaps with the hallways of the modules that stay in place.
    /// </summary>
    private bool MovedModuleOverlapsWithStationaryHallways(PlacedModule module)
    {
        //the hallway of the module we're moving is checked separately
        //(its length changes, so it needs to be checked against the other moved modules too)
        if (module == movingModule || module.PreviousGap == null) { return false; }
        if (miniMapModuleElements.GetValueOrDefault(module) is not GUIComponent element) { return false; }

        //the modules (and hallways) moving along with this one can be ignored, they all move by the same amount
        return
            HallwayOverlapsWithPlacedModules(element, module.PreviousGap, ignoredElements: moduleElementsToMove) ||
            HallwayOverlapsWithOtherHallways(element, module.PreviousGap, ignoredElements: moduleElementsToMove) ||
            UIElementOverlapsWithHallways(element, ignoredElements: moduleElementsToMove);
    }

    /// <param name="ignoredElements">Module elements that shouldn't be checked against</param>
    private bool HallwayOverlapsWithPlacedModules(GUIComponent moduleElement, Gap attachedToGap, ICollection<GUIComponent>? ignoredElements = null)
    {
        GUIComponent? gapElement = miniMapGapElements.FirstOrDefault(kvp => kvp.Key.Gap == attachedToGap).Value;
        if (gapElement == null) { return false; }
        foreach ((PlacedModule otherModule, GUIComponent otherModuleElement) in miniMapModuleElements)
        {
            if (moduleElement == otherModuleElement) { continue; }
            if (ignoredElements != null && ignoredElements.Contains(otherModuleElement)) { continue; }
            if (otherModule.AllGaps.Any(gapInfo => gapInfo.Gap == attachedToGap)) { continue; }
            if (HallwayOverlapsWithUIElement(moduleElement, attachedToGap, otherModuleElement))
            {
                return true;
            }
        }
        foreach ((OutpostBuilder.PendingOutpostModule pendingModule, GUIComponent pendingModuleElement) in miniMapPendingModuleElements)
        {
            if (pendingModule == movingPendingModule || moduleElement == pendingModuleElement) { continue; }
            if (ignoredElements != null && ignoredElements.Contains(pendingModuleElement)) { continue; }
            if (HallwayOverlapsWithUIElement(moduleElement, attachedToGap, pendingModuleElement))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks whether uiElementToCheck overlaps with the hallway between moduleElement and attachedGap. 
    /// </summary>
    private bool HallwayOverlapsWithUIElement(GUIComponent moduleElement, Gap attachedToGap, GUIComponent uiElementToCheck)
    {
        GUIComponent? gapElement = miniMapGapElements.FirstOrDefault(kvp => kvp.Key.Gap == attachedToGap).Value;
        if (gapElement == null) { return false; }

        Rectangle hallwayRect = gapElement.Rect;
        //stretch the gap rectangle all the way to the module to get the bounds of the hallway
        if (attachedToGap.IsHorizontal)
        {
            int min = (int)Math.Min(gapElement.Rect.X, moduleElement.Center.X);
            int max = (int)Math.Max(gapElement.Rect.X, moduleElement.Center.X);
            hallwayRect = new Rectangle(min, hallwayRect.Y, max - min, hallwayRect.Height);
        }
        else
        {
            int min = (int)Math.Min(gapElement.Rect.Y, moduleElement.Center.Y);
            int max = (int)Math.Max(gapElement.Rect.Y, moduleElement.Center.Y);
            hallwayRect = new Rectangle(hallwayRect.X, min, hallwayRect.Width, max - min);            
        }

        //inflate a bit: the hallway rect here is calculated based on the gaps, but the hallway is actually a bit wider than that
        //inflate by 10 pixels in world space
        int inflateAmount = (int)(10 * zoom);
        hallwayRect.Inflate(inflateAmount, inflateAmount);

        return hallwayRect.Intersects(uiElementToCheck.Rect);
    }

    /// <param name="ignoredElements">Module elements whose hallways shouldn't be checked against</param>
    private bool UIElementOverlapsWithHallways(GUIComponent uiElementToCheck, ICollection<GUIComponent>? ignoredElements = null)
    {
        foreach ((PlacedModule otherModule, GUIComponent otherModuleElement) in miniMapModuleElements)
        {
            //don't check for overlaps with the element itself
            if (uiElementToCheck == otherModuleElement) { continue; }
            if (ignoredElements != null && ignoredElements.Contains(otherModuleElement)) { continue; }
            //no previous gap = no hallway to check
            if (otherModule.PreviousGap == null) { continue; }

            //if the other module is connected to the element we're checking, there can be no overlaps
            //(the hallway between 2 modules doesn't overlap with those 2 modules)
            if (miniMapModuleElements.TryGetValue(otherModule.PreviousModule, out GUIComponent? previousModuleElement) &&
                previousModuleElement == uiElementToCheck)
            {
                continue;
            }

            if (HallwayOverlapsWithUIElement(otherModuleElement, otherModule.PreviousGap, uiElementToCheck))
            {
                return true;
            }
        }
        foreach ((OutpostBuilder.PendingOutpostModule pendingModule, GUIComponent pendingModuleElement) in miniMapPendingModuleElements)
        {
            if (pendingModule.Gap == null) { continue; }
            if (pendingModule == movingPendingModule || uiElementToCheck == pendingModuleElement) { continue; }
            if (ignoredElements != null && ignoredElements.Contains(pendingModuleElement)) { continue; }
            if (HallwayOverlapsWithUIElement(pendingModuleElement, pendingModule.Gap, uiElementToCheck))
            {
                return true;
            }
        }
        return false;
    }

    private bool HallwayOverlapsWithOtherHallway(GUIComponent moduleElement, Gap attachedToGap, GUIComponent otherModuleElement, Gap? otherModuleAttachedToGap)
    {
        if (otherModuleAttachedToGap == null) { return false; }

        GUIComponent? gapElement = miniMapGapElements.FirstOrDefault(kvp => kvp.Key.Gap == attachedToGap).Value;
        if (gapElement == null) { return false; }

        GUIComponent? otherModuleGapElement = miniMapGapElements.FirstOrDefault(kvp => kvp.Key.Gap == otherModuleAttachedToGap).Value;
        if (otherModuleGapElement == null) { return false; }

        return MathUtils.LineSegmentsIntersect(
                gapElement.Center,
                attachedToGap.IsHorizontal ? new Vector2(moduleElement.Center.X, gapElement.Center.Y) : new Vector2(gapElement.Center.X, moduleElement.Center.Y),
                otherModuleGapElement.Center,
                otherModuleAttachedToGap.IsHorizontal ? new Vector2(otherModuleElement.Center.X, otherModuleGapElement.Center.Y) : new Vector2(otherModuleGapElement.Center.X, otherModuleElement.Center.Y));
    }

    /// <param name="ignoredElements">Module elements whose hallways shouldn't be checked against</param>
    private bool HallwayOverlapsWithOtherHallways(GUIComponent moduleElement, Gap attachedToGap, ICollection<GUIComponent>? ignoredElements = null)
    {
        GUIComponent? gapElement = miniMapGapElements.FirstOrDefault(kvp => kvp.Key.Gap == attachedToGap).Value;
        if (gapElement == null) { return false; }

        var attachedToModule = outpost.OutpostModules.FirstOrDefault(m => m.AllGaps.Any(g => g.Gap == attachedToGap));

        foreach ((PlacedModule otherModule, GUIComponent otherModuleElement) in miniMapModuleElements)
        {
            if (otherModule.AllGaps.Any(gapInfo => gapInfo.Gap == attachedToGap) || otherModule.PreviousModule == null) { continue; }
            if (ignoredElements != null && ignoredElements.Contains(otherModuleElement)) { continue; }
            //the other module is attached to the same module as the one we're checking, hallways can't overlap
            if (otherModule.PreviousModule == attachedToModule) { continue; }
            if (HallwayOverlapsWithOtherHallway(moduleElement, attachedToGap, otherModuleElement, otherModule.PreviousGap))
            {
                return true;
            }
        }
        foreach ((OutpostBuilder.PendingOutpostModule pendingModule, GUIComponent pendingModuleElement) in miniMapPendingModuleElements)
        {
            if (pendingModule == movingPendingModule || pendingModuleElement == moduleElement) { continue; }
            if (ignoredElements != null && ignoredElements.Contains(pendingModuleElement)) { continue; }
            if (HallwayOverlapsWithOtherHallway(moduleElement, attachedToGap, pendingModuleElement, pendingModule.Gap))
            {
                return true;
            }
        }
        return false;
    }
    private bool ModuleElementBelowAirlock(GUIComponent moduleElement)
    {
        if (!miniMapModuleElements.TryGetValue(outpost.OutpostModules.First(), out GUIComponent? initialModuleElement)) { return false; }
        return moduleElement.Rect.Bottom > initialModuleElement.Rect.Bottom;
    }

    /// <summary>
    /// Is the module element above a module that doesn't allow modules above it (<see cref="PurchasableOutpostModulePrefab.DisallowModulesAbove"/>)?
    /// Or if the element itself is such a module, are there other modules above it?
    /// </summary>
    private bool ModuleElementAboveRestrictedModule(GUIComponent moduleElement, out GUIComponent? restrictingModuleElement)
    {
        restrictingModuleElement = null;
        bool disallowModulesAboveThis = GetModulePrefab(moduleElement) is { DisallowModulesAbove: true };
        foreach (GUIComponent otherElement in miniMapModuleElements.Values.Concat(miniMapPendingModuleElements.Values))
        {
            if (otherElement == moduleElement) { continue; }
            if (GetModulePrefab(otherElement) is { DisallowModulesAbove: true } &&
                IsAbove(moduleElement.Rect, otherElement.Rect))
            {
                restrictingModuleElement = otherElement;
                return true;
            }
            if (disallowModulesAboveThis && IsAbove(otherElement.Rect, moduleElement.Rect))
            {
                restrictingModuleElement = moduleElement;
                return true;
            }
        }
        return false;

        bool IsAbove(Rectangle rect, Rectangle restrictedRect)
        {
            //same tolerance as with overlaps, so modules attached right next to the restricted module don't count as being above it
            restrictedRect.Inflate(-OverlapToleranceOnScreen, 0);
            return rect.Top < restrictedRect.Top && rect.Right > restrictedRect.Left && rect.Left < restrictedRect.Right;
        }
    }

    private PurchasableOutpostModulePrefab? GetModulePrefab(GUIComponent moduleElement)
    {
        if (moduleElement == modulePlacementElement) { return selectedModulePrefab; }
        foreach ((PlacedModule module, GUIComponent element) in miniMapModuleElements)
        {
            if (element == moduleElement) { return OutpostBuilder.FindModulePrefab(module); }
        }
        foreach ((OutpostBuilder.PendingOutpostModule pendingModule, GUIComponent element) in miniMapPendingModuleElements)
        {
            if (element == moduleElement) { return pendingModule.ModulePrefab; }
        }
        return null;
    }
    #endregion

    #region Module list
    /// <summary>
    /// Key = identifier, Value = max count per category.
    /// </summary>
    private readonly Dictionary<Identifier, int> availableCategories = new();

    private bool HasCategoryReachedLimit(Identifier category, int categoryLimit)
    {
        return OutpostBuilder.CountModules(category, outpost, outpostData) >= categoryLimit;
    }

    private static bool HasCategoryBeenResearched(Identifier category)
    {
        return PurchasableOutpostModulePrefab.Prefabs
            .Where(m => m.Sold && m.Category == category)
            .Any(OutpostBuilder.HasRequiredResearch);
    }

    /// <summary>
    /// Is the technology required by the module hidden (see <see cref="TechnologyPrefab.HiddenUntilMetadataRequirementMet"/>)? If so, the module's details are hidden too, to avoid spoilers.
    /// </summary>
    private static bool IsModuleHidden(PurchasableOutpostModulePrefab module)
    {
        return TechnologyPrefab.Prefabs.TryGet(module.RequiredResearch, out TechnologyPrefab? technologyPrefab) && technologyPrefab.IsHidden();
    }

    /// <summary>
    /// Are all the modules in the category hidden? See <see cref="IsModuleHidden"/>.
    /// </summary>
    private static bool IsCategoryHidden(Identifier category)
    {
        return PurchasableOutpostModulePrefab.Prefabs
            .Where(m => m.Sold && m.Category == category)
            .All(IsModuleHidden);
    }

    private void UpdateModuleList(GUIComponent content)
    {
        availableCategories.Clear();
        foreach (var module in PurchasableOutpostModulePrefab.Prefabs.Where(m => m.Sold))
        {
            availableCategories.TryAdd(module.Category, module.MaxCountPerCategory);
        }

        content.ClearChildren();

        moduleLimitTexts.Clear();
        categoryCollapseButtons.Clear();

        foreach ((Identifier category, int maxCount) in availableCategories
            // Order alphabetically by identifier
            .OrderBy(c => c.Key))
        {
            GUIButton categoryButton = new(new RectTransform(new Vector2(1.0f, 0.01f), content.RectTransform), style: null)
            {
                UserData = category,
                OnClicked = (btn, userdata) =>
                {
                    Identifier toggledCategory = (Identifier)userdata;
                    GUIImage arrowImage = btn.GetChild<GUIImage>();
                    arrowImage.SpriteEffects ^= SpriteEffects.FlipVertically;
                    if (arrowImage.SpriteEffects == SpriteEffects.None)
                    {
                        collapsedModuleCategories.Add(toggledCategory);
                    }
                    else
                    {
                        collapsedModuleCategories.Remove(toggledCategory);
                    }
                    // Preserve scroll position, so the clicked category heading stays at the same visual position.
                    float scrollRange = moduleList != null ? Math.Max(moduleList.TotalSize - moduleList.Content.Rect.Height, 0f) : 0f;
                    float pixelOffset = (moduleList?.BarScroll ?? 0f) * scrollRange;
                    FilterModuleList();
                    if (moduleList != null)
                    {
                        moduleList.UpdateScrollBarSize();
                        // The scroll range may have changed now that a category was expanded/collapsed, let's keep the
                        // pixel offset constant so that only items below the category move.
                        float newScrollRange = Math.Max(moduleList.TotalSize - moduleList.Content.Rect.Height, 0f);
                        if (newScrollRange > 0f)
                        {
                            moduleList.BarScroll = pixelOffset / newScrollRange;
                        }
                    }
                    return true;
                }
            };

            LocalizedString categoryName = category.IsEmpty ?
                TextManager.Get("modulecategory.misc") :
                TextManager.Get($"modulecategory.{category}").Fallback(category.Value);
            if (IsCategoryHidden(category)) { categoryName = TechnologyPrefab.HiddenText; }
            var categoryText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), categoryButton.RectTransform, Anchor.TopLeft),
                categoryName, textAlignment: Alignment.CenterLeft, font: GUIStyle.SubHeadingFont, wrap: true)
            {
                Padding = new Vector4(GUI.IntScale(10)),
                CanBeFocused = true
            };
            categoryButton.RectTransform.InheritTotalChildrenHeight();
            Vector4 padding = new Vector4(0, 0, GUI.IntScale(35), 0);
            if (maxCount < OutpostBuilder.MaxModulesPerOutpost)
            {
                moduleLimitTexts[category] = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), categoryText.RectTransform, Anchor.TopRight), string.Empty, textAlignment: Alignment.CenterRight, font: GUIStyle.SubHeadingFont, wrap: true)
                {
                    Padding = padding,
                    CanBeFocused = true
                };
                UpdateModuleLimitText(category);
            }
            categoryText.CalculateHeightFromText(padding: GUI.IntScale(10));

            bool hasBeenResearched = HasCategoryBeenResearched(category);
            if (!hasBeenResearched)
            {
                categoryText.TextColor = GUIStyle.Red;
            }

            bool isCollapsed = collapsedModuleCategories.Contains(category);
            if (!hasBeenResearched)
            {
                categoryText.ToolTip = TextManager.Get("outpostbuilderui.categoryrequiresresearch");
            }
            if (maxCount < OutpostBuilder.MaxModulesPerOutpost)
            {
                moduleLimitTexts[category].ToolTip = TextManager.GetWithVariable("outpostbuilderui.maxmodulecount.tooltip", "[count]", maxCount.ToString());
            }
            categoryCollapseButtons.Add(category, categoryButton);

            new GUIImage(new RectTransform(new Vector2(0.5f), categoryButton.RectTransform, Anchor.CenterRight, scaleBasis: ScaleBasis.Smallest), style: "GUIButtonVerticalArrowFreeScale", scaleToFit: true)
            {
                SpriteEffects = isCollapsed ? SpriteEffects.None : SpriteEffects.FlipVertically
            };

            var divider = new GUIFrame(new RectTransform(Vector2.One, content.RectTransform), style: "HorizontalLine")
            {
                UserData = category
            };

            foreach (var module in PurchasableOutpostModulePrefab.Prefabs
                .Where(p => p.Category == category && p.Sold)
                // put unresearched modules at the bottom of each category
                .OrderBy(static p => !OutpostBuilder.HasRequiredResearch(p)))
            {
                CreateModuleElement(module, content);
            }
        }
        if (moduleList != null)
        {
            moduleList.UseGridLayout = false;
        }

        moduleList?.RecalculateChildren();
    }

    private void FilterModuleList()
    {
        if (moduleList == null || filterText == null) { return; }

        string filter = filterBox.Text;

        foreach (var element in moduleList.Content.Children)
        {
            //hide the category labels if the category not been researched, and we've opted to hide non-researched categories
            if (element.UserData is Identifier categoryIdentifier)
            {
                element.Visible = HasCategoryBeenResearched(categoryIdentifier) || showResearchedOnly is { Selected: false };
                continue;
            }

            var modulePrefab = element.UserData as PurchasableOutpostModulePrefab;
            if (modulePrefab?.OutpostInfo is not { } outpostInfo) 
            { 
                continue; 
            }

            if (showResearchedOnly is { Selected: true } && !OutpostBuilder.HasRequiredResearch(modulePrefab))
            {
                element.Visible = false;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(filter))
            {
                // Ignore category collapsing if we are filtering by text
                // Don't let hidden modules be found by their (hidden) name
                element.Visible = !IsModuleHidden(modulePrefab) && outpostInfo.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                element.Visible = !collapsedModuleCategories.Contains(modulePrefab.Category);
            }
        }
    }

    private void CreateModuleElement(PurchasableOutpostModulePrefab module, GUIComponent parent)
    {
        // Hidden modules only show "???" in place of the name, and no preview
        bool isHidden = IsModuleHidden(module);
        LocalizedString displayName = isHidden ? TechnologyPrefab.HiddenText : module.DisplayName;

        GUIFrame frame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.01f), parent.RectTransform),
            style: "UpgradeUIFrame")
        {
            UserData = module,
            CanBeFocused = OutpostBuilder.HasRequiredResearch(module) && outpost.OutpostModules.Any(),
            GlowOnSelect = true,
            Color = new Color(255, 255, 255, 150),
            HoverColor = new Color(255, 255, 255, 220),
            SelectedColor = new Color(101, 135, 121, 150),
            ToolTip = displayName,
            OnAddedToGUIUpdateList = (component) =>
            {
                //recreate the contents if we can no longer afford the module (or vice versa)
                //to get the colors & missing materials displayed correctly
                CanAffordModule(module, out bool hasChanged);
                if (hasChanged)
                {
                    component.ClearChildren();
                    CreateContents(component);
                    //refresh module info panel too
                    if (moduleInfoPanel != null && selectedModulePrefab != null)
                    {
                        ShowModuleInfo(selectedModulePrefab);
                    }
                }
            }
        };
        frame.RectTransform.MinSize = new Point(0, GUI.IntScale(200));
        frame.RectTransform.MaxSize = new Point(int.MaxValue, GUI.IntScale(200));

        CreateContents(frame);

        void CreateContents(GUIComponent frame)
        {
            GUILayoutGroup paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.8f), frame.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                Stretch = true,
                RelativeSpacing = 0.1f,
                CanBeFocused = false
            };

            var outpostPreview = new GUICustomComponent(new RectTransform(new Vector2(0.4f, 1.0f), paddedFrame.RectTransform, Anchor.TopCenter),
                onDraw: (SpriteBatch spriteBatch, GUICustomComponent customComponent) =>
                {
                    if (!isHidden && blueprintSprites.TryGetValue(module.OutpostInfo, out SubmarinePreview.SubmarineTexture? texture) && texture != null)
                    {
                        spriteBatch.Draw(texture.Texture2D, customComponent.Rect, Color.White);
                    }
                });

            var previewButtonFrame = new GUIButton(new RectTransform(new Vector2(1.0f, 1.0f), outpostPreview.RectTransform, Anchor.Center), style: null)
            {
                Visible = !isHidden,
                IgnoreLayoutGroups = true,
                ToolTip = TextManager.Get("previewsubmarine"),
                OnClicked = (btn, obj) =>
                {
                    SubmarinePreview.Create(module.OutpostInfo, showSubmarineSpecs: false);
                    return false;
                }
            };

            var previewButton = new GUIFrame(new RectTransform(Vector2.One * 0.2f, previewButtonFrame.RectTransform, anchor: Anchor.BottomRight, 
                scaleBasis: ScaleBasis.BothHeight) { AbsoluteOffset = new Point((int)(0.03f * outpostPreview.Rect.Height)) }, style: "ExpandButton")
            {
                Color = Color.White,
                HoverColor = Color.White,
                PressedColor = Color.White,
                ToolTip = TextManager.Get("previewsubmarine"),
                IgnoreLayoutGroups = true
            };

            GUILayoutGroup textContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.6f, 1.0f), paddedFrame.RectTransform), childAnchor: Anchor.TopLeft)
            {
                RelativeSpacing = 0.05f,
                CanBeFocused = false
            };

            GUITextBlock textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), textContainer.RectTransform),
                text: displayName, font: GUIStyle.SubHeadingFont, textColor: GUIStyle.TextColorNormal)
            {
                Padding = Vector4.Zero,
                CanBeFocused = true
            };
            textBlock.Text = ToolBox.LimitString(textBlock.Text, textBlock.Font, textBlock.Rect.Width);
            if (textBlock.Text != displayName)
            {
                textBlock.ToolTip = displayName;
            }

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), textContainer.RectTransform), text: TextManager.Get("outpostbuilderui.modulerequirements"), font: GUIStyle.SmallFont, textColor: GUIStyle.Orange)
            {
                Padding = Vector4.Zero,
                CanBeFocused = false
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), textContainer.RectTransform),
                text: TextManager.GetWithVariable("OutpostBuilderUI.WorldStepFormat", "[steps]", module.TimeStepsToBuild.ToString()), font: GUIStyle.SmallFont, textColor: GUIStyle.TextColorBright)
            {
                CanBeFocused = false
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), textContainer.RectTransform),
                text: TextManager.FormatCurrency(module.Price), font: GUIStyle.SmallFont, textColor: GUIStyle.Orange)
            {
                CanBeFocused = false,
                OnAddedToGUIUpdateList = (textBlock) => { ((GUITextBlock)textBlock).TextColor = OutpostBuilder.CanAfford(module, checkRequiredResources: false) ? GUIStyle.TextColorBright : GUIStyle.Red; },
            };

            if (module.ResourceCosts.Any())
            {
                var requiredList = new GUIListBox(new RectTransform(new Point(textContainer.Rect.Width, GUI.IntScale(50)), textContainer.RectTransform)
                {
                    MinSize = new Point(0, GUI.IntScale(50))
                }, isHorizontal: true, style: "GUIListBoxNoBorder");
                requiredList.ClampMouseRectToParent = requiredList.Content.ClampMouseRectToParent = true;
                UpgradeStore.CreateMaterialCostsElement(requiredList, module.ResourceCosts);
                requiredList.RectTransform.NonScaledSize += new Point(0, module.ResourceCosts.Count() > 2 ? requiredList.ScrollBar.Rect.Height : 0);
            }

            if (module.RequiredResearch != Identifier.Empty)
            {
                if (!TechnologyPrefab.Prefabs.TryGet(module.RequiredResearch, out TechnologyPrefab? technologyPrefab))
                {
                    DebugConsole.AddWarning($"Failed to find technology prefab {module.RequiredResearch}");
                }
                else if (!OutpostBuilder.HasRequiredResearch(module))
                {
                    //position the "hide frame" to the right to still allow previewing the module
                    var hideFrame = new GUIFrame(new RectTransform(new Vector2(0.6f, 1.0f), frame.RectTransform, Anchor.CenterRight), color: Color.Black * 0.9f)
                    {
                        IgnoreLayoutGroups = true
                    };
                    var btn = new GUIButton(new RectTransform(new Vector2(1f, 0.25f), hideFrame.RectTransform, Anchor.Center),
                        TextManager.Get("outpostbuilderui.requiredresearch") + $"\n{technologyPrefab.GetDisplayName()}", style: null)
                    {
                        Font = GUIStyle.SubHeadingFont,
                        TextColor = GUIStyle.Red * 0.85f,
                        HoverTextColor = Color.Lerp(GUIStyle.Red, Color.White, 0.5f),
                        ToolTip = TextManager.Get("outpostbuilderui.openresearchtab"),
                        OnAddedToGUIUpdateList = (GUIComponent component) =>
                        {
                            if (GUI.MouseOn == component)
                            {
                                if (OutpostManagementUI.Instance?.GetTabButton(OutpostManagementUI.Tab.Research) is GUIButton researchTabButton)
                                {
                                    if (researchTabButton.FlashTimer <= 0.0f) { researchTabButton.Flash(GUIStyle.Green); }
                                }
                            }
                        },
                        OnClicked = (btn, userdata) =>
                        {
                            if (OutpostManagementUI.Instance is { } outpostUI)
                            {
                                outpostUI.Open(OutpostManagementUI.Tab.Research);
                                foreach (var technologyUI in outpostUI.TechnologyUIs)
                                {
                                    if (technologyUI.HasTechnology(module.RequiredResearch))
                                    {
                                        technologyUI.FocusOnTechnology(module.RequiredResearch);
                                    }
                                }
                            }
                            return true;
                        }
                    };
                    btn.TextBlock.Wrap = true;
                }
            }

            bool canAfford = CanAffordModule(module, out _);
            bool maxModulesReached = OutpostBuilder.MaximumNumberOfModulesReached(outpostData);
            bool maxModulesInCategoryReached = OutpostBuilder.MaximumNumberOfModulesInCategoryReached(module, outpostData);
            bool researchRequired = OutpostBuilder.HasRequiredResearch(module);
            Color baseColor = researchRequired && canAfford && !maxModulesReached ? Color.White : GUIStyle.Red;
            //maximum number of modules already built is a positive thing, display in green instead of red
            if (maxModulesInCategoryReached) { baseColor = GUIStyle.Green; }
            frame.Color = new Color(baseColor, 200);
            frame.HoverColor = new Color(baseColor, 220);
            //dimmer color because the frame is set to glow on select, and we want the glow to be a bit more subtle
            frame.SelectedColor = canAfford ? new Color(101, 135, 121, 150) : new Color(135, 104, 102, 150);
            frame.ToolTip = displayName;
            if (maxModulesReached)
            {
                frame.ToolTip = TextManager.Get("outpostbuilderui.maxmodulesreached");
            }
            else if (maxModulesInCategoryReached)
            {
                frame.ToolTip = TextManager.Get("outpostbuilderui.maxmodulesincategoryreached");
            }
        }        
    }

    /// <summary>
    /// Checks and updates whether the player can afford the module, caching the value and only re-checking if it's been more than a second since it was last checked.
    /// </summary>
    private bool CanAffordModule(PurchasableOutpostModulePrefab module, out bool hasChanged)
    {
        hasChanged = false;
        if (!lastCanAffordCheckTime.TryGetValue(module, out double lastCheckTime) ||
            !canAffordModuleCached.ContainsKey(module) ||
            Timing.TotalTime > lastCheckTime + 1.0)
        {
            bool newValue = OutpostBuilder.CanAfford(module, checkRequiredResources: true);
            if (!canAffordModuleCached.TryGetValue(module, out bool cachedValue) || cachedValue != newValue)
            {
                hasChanged = true;
                canAffordModuleCached[module] = newValue;
            }
            //add some randomness so every module isn't updated on the same frame
            lastCanAffordCheckTime[module] = Timing.TotalTime + Rand.Range(0.0, 0.1);
        }
        return canAffordModuleCached[module];
    }

    /// <summary>
    /// Key = module category, value = textblock for displaying the module limit for that category
    /// </summary>
    private readonly Dictionary<Identifier, GUITextBlock> moduleLimitTexts = [];

    private void UpdateModuleLimitText(Identifier category)
    {
        if (moduleLimitTexts.TryGetValue(category, out GUITextBlock? textBlock) && textBlock != null && availableCategories.TryGetValue(category, out int maxCount))
        {
            int modulesPlacedInCategory = OutpostBuilder.CountModules(category, outpost, outpostData);
            textBlock.Text = $"{modulesPlacedInCategory}/{maxCount}";
            Color textColor;
            if (modulesPlacedInCategory >= maxCount)
            {
                textColor = GUIStyle.TextColorDim;
            }
            else
            {
                textColor = HasCategoryBeenResearched(category) ? GUIStyle.TextColorNormal : GUIStyle.TextColorDim;
            }
            textBlock.TextColor = textColor;
            textBlock.HoverTextColor = Color.Lerp(textColor, Color.White, 0.5f);
            if (textBlock.Parent is GUITextBlock parentText)
            {
                parentText.TextColor = textBlock.TextColor;
                parentText.TextColor = textBlock.HoverTextColor;
            }
        }
    }
    #endregion

    #region Module info
    private bool SelectPlacedModule(GUIComponent component, object obj)
    {
        SelectMiniMapModuleElement(component);
        if (obj is PlacedModule selectedModule)
        {
            SelectPlacedModule(selectedModule);
            return true;
        }
        return false;
    }

    private bool SelectPlacedModule(PlacedModule? selectedModule)
    {
        this.selectedModule = selectedModule;
        selectedPendingModule = null;
        selectedModulePrefab = null;
        if (selectedModule != null)
        {
            ShowModuleInfo(selectedModule);
            return true;
        }
        return false;
    }

    private bool SelectPendingModule(GUIComponent component, object obj)
    {
        SelectMiniMapModuleElement(component);
        if (obj is OutpostBuilder.PendingOutpostModule selectedPendingModule)
        {
            SelectPendingModule(selectedPendingModule);
            return true;
        }
        return false;
    }

    private void SelectPendingModule(OutpostBuilder.PendingOutpostModule? selectedPendingModule)
    {
        this.selectedPendingModule = selectedPendingModule;
        selectedModule = null;
        selectedModulePrefab = null;
        if (selectedPendingModule != null)
        {
            ShowModuleInfo(selectedPendingModule);
        }
    }

    private void SelectMiniMapModuleElement(GUIComponent? element)
    {
        foreach (var minimapElement in miniMapPendingModuleElements.Values.Union(miniMapModuleElements.Values))
        {
            if (minimapElement is GUIButton button)
            {
                button.Selected = minimapElement == element;
            }
            //enable move buttons
            foreach (var childButton in minimapElement.GetAllChildren<GUIButton>())
            {
                if (childButton.Style is not { } style) { continue; }
                if (style.Identifier != "MoveModuleButton") { continue; }
                childButton.Visible = minimapElement == element;
                //not connected to a gap with a door = staircase or some other special connection between modules, don't allow moving
                if (minimapElement.UserData is PlacedModule placedModule && placedModule.PreviousGap?.ConnectedDoor == null)
                {
                    childButton.Visible = false;
                }
                else if (minimapElement.UserData is OutpostBuilder.PendingOutpostModule pendingModule && pendingModule.Gap?.ConnectedDoor == null)
                {
                    childButton.Visible = false;
                }
                else
                {
                    childButton.Visible = minimapElement == element;
                }
            }
        }
    }

    private bool SelectModulePrefab(GUIComponent component, object obj)
    {
        if (obj is PurchasableOutpostModulePrefab modulePrefab)
        {
            selectedModule = null;
            selectedPendingModule = null;
            highlightedGapForModulePlacement = null;
            ShowModuleInfo(modulePrefab);
            if (OutpostBuilder.CanPurchase(modulePrefab, outpostData) &&
                !OutpostBuilder.MaximumNumberOfModulesReached(outpostData))
            {
                //remove existing element (if there's one)
                modulePlacementElement?.Parent?.RemoveChild(modulePlacementElement);

                Point moduleSize = modulePrefab.HullBounds.Size.Multiply(minimapScale);
                if (miniMap != null)
                {
                    modulePlacementElement =
                        new GUIButton(new RectTransform(moduleSize, miniMap.RectTransform, anchor: Anchor.TopLeft, pivot: Pivot.Center), style: "ScanLines", color: GUIStyle.Red)
                        {
                            Visible = false,
                            OutlineColor = Color.White,
                            SelectedColor = GUIStyle.Green,
                            OutlineThickness = 2,
                            UserData = modulePrefab
                        };

                    //flip y because module coordinates are in world space, and we're creating UI elements in screen space
                    Point offset = (modulePrefab.Bounds.Center - modulePrefab.HullBounds.Center).FlipY();

                    new GUICustomComponent(new RectTransform(Vector2.One, modulePlacementElement.RectTransform, Anchor.Center) 
                        { RelativeOffset = -offset.ToVector2() }, 
                        onDraw: (sb, customComponent) =>
                        {
                            DrawModule(sb, customComponent, modulePrefab.OutpostInfo);
                        })
                    {
                        CanBeFocused = false
                    };

                    foreach (var gapInfo in modulePrefab.Gaps)
                    {
                        Color color = AvailableGapColor;
                        Vector2 gapPos = gapInfo.Rect.Location.ToVector2();
                        gapPos.X += gapInfo.Rect.Width / 2;
                        gapPos.Y -= gapInfo.Rect.Height / 2;
                        gapPos.Y = -gapPos.Y;
                        GUIButton gapFrame = new GUIButton(new RectTransform(gapInfo.Rect.Size.Multiply(minimapScale), modulePlacementElement.RectTransform, Anchor.Center)
                            { RelativeOffset = Vector2.Divide(gapPos, modulePrefab.HullBounds.Size.ToVector2()) }, style: "ScanLines", color: color)
                        {
                            OutlineColor = color,
                            OutlineThickness = 2,
                            UserData = gapInfo
                        };
                        new GUICustomComponent(new RectTransform(Vector2.One, gapFrame.RectTransform), onDraw: (sb, customComponent) => DrawPendingModuleHallway(sb, gapFrame, miniMapGapElements.FirstOrDefault(g => g.Value == highlightedGapForModulePlacement).Key, gapInfo))
                        {
                            UserData = gapInfo
                        };
                    }
                }
            }
            SelectMiniMapModuleElement(null);
            selectedModulePrefab = modulePrefab;
            return true;
        }
        return false;
    }

    private void ShowModuleInfo(PurchasableOutpostModulePrefab modulePrefab)
    {
        ShowModuleInfo(modulePrefab, placedModule: null, pendingModule: null);
    }

    private void ShowModuleInfo(PlacedModule placedModule)
    {
        ShowModuleInfo(OutpostBuilder.FindModulePrefab(placedModule), placedModule, pendingModule: null);
    }

    private void ShowModuleInfo(OutpostBuilder.PendingOutpostModule pendingModule)
    {
        ShowModuleInfo(pendingModule.ModulePrefab, placedModule: null, pendingModule: pendingModule);
    }

    /// <summary>
    /// Displays information about the selected module in the module info panel. Can either be a module prefab, an already placed module, or a pending module.
    /// </summary>
    private void ShowModuleInfo(PurchasableOutpostModulePrefab? modulePrefab, PlacedModule? placedModule, OutpostBuilder.PendingOutpostModule? pendingModule)
    {
        HideModuleInfoPanel();
        moduleInfoPanel = new GUIFrame(new RectTransform(new Vector2(0.2f, 0), UIFrame!.RectTransform, Anchor.TopRight, minSize: new Point(GUI.IntScale(350), 0), maxSize: new Point(GUI.IntScale(600), UIFrame.Rect.Height)), style: "GUIFrameListBox");
        GUILayoutGroup moduleInfoContents = new(new RectTransform(new Vector2(0.92f), moduleInfoPanel.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter);

        if (modulePrefab == null && placedModule == null) { throw new ArgumentException($"Both {nameof(modulePrefab)} and {nameof(placedModule)} were null."); }
        modulePrefab ??= PurchasableOutpostModulePrefab.Prefabs.FirstOrDefault(p => p.OutpostInfo.FilePath == placedModule!.Info.FilePath);

        Vector4 textPadding = new Vector4(10, 0, 10, 0) * GUI.Scale;
        Color sectionHeaderBackgroundColor = new Color(26, 39, 34);
        Color sectionContentBackgroundColor = new Color(13, 20, 17);
        int sectionContentSpacing = GUI.IntScale(5);

        SubmarineInfo outpostInfo = modulePrefab?.OutpostInfo ?? placedModule!.Info;

        var headerContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.0f), moduleInfoContents.RectTransform), isHorizontal: true)
        {
            Stretch = true,
            CanBeFocused = true
        };

        GUITextBlock header = new(new RectTransform(new Vector2(0.6f, 1.0f), headerContainer.RectTransform), modulePrefab?.DisplayName ?? PurchasableOutpostModulePrefab.GetDefaultDisplayName(outpostInfo), font: GUIStyle.SubHeadingFont)
        {
            AutoScaleHorizontal = true
        };
        header.Padding = new Vector4(header.Padding.X, 0, 0, 0);
        header.RectTransform.MaxSize = new Point((int)(header.TextSize.X + header.Padding.X + header.Padding.Z), int.MaxValue);
        header.RectTransform.MinSize = headerContainer.RectTransform.MinSize = new Point(0, (int)(header.TextSize.Y * 1.5f));

        Identifier categoryID = modulePrefab?.Category ?? placedModule!.Category;
        LocalizedString categoryText = TextManager.Get($"ModuleCategory.{categoryID}").Fallback(categoryID.Value);
        var category = new GUITextBlock(new RectTransform(new Point(headerContainer.Rect.Width - header.Rect.Width, header.Rect.Height), headerContainer.RectTransform), categoryText, textAlignment: Alignment.CenterRight)
        {
            AutoScaleHorizontal = true
        };
        category.Padding = new Vector4(0, 0, category.Padding.Z, 0);

        if (modulePrefab != null)
        {
            // features
            Dictionary<FacilityResource, int> facilityResources = [];
            if (modulePrefab.OutpostFacilities.TryUnwrap(out OutpostFacilityManager? outpostFacilityManager) && outpostData.ResourceManager != null)
            {
                foreach (Resource resource in outpostData.ResourceManager.Resources)
                {
                    if (resource is not FacilityResource facilityResource) { continue; }
                    if (resource.Hidden) { continue; }
                    int amount = (int)facilityResource.GetAmount(outpostFacilityManager);
                    if (amount == 0) { continue; }
                    facilityResources.Add(facilityResource, amount);                    
                }
            }
            if (facilityResources.Count != 0 || modulePrefab.SwappableItems.Any() || modulePrefab.Workstations.Any() ||
                placedModule != null && placedModule.Entities.Any(e => e is Item item && (item.IsSwappable || item.GetComponent<WorkstationComponent>() != null)))
            {
                InsertSpacing();
                GUITextBlock featuresHeader = new(new RectTransform(Vector2.UnitX, moduleInfoContents.RectTransform), TextManager.Get("OutpostBuilderUI.ModuleFeatures"), GUIStyle.TextColorNormal,
                    style: null, color: sectionHeaderBackgroundColor)
                    {
                        Padding = textPadding
                    };
                featuresHeader.RectTransform.Resize(new Point(featuresHeader.Rect.Width, featuresHeader.Rect.Height + sectionContentSpacing * 2));

                GUIFrame featuresFrame = new(new RectTransform(Vector2.UnitX, moduleInfoContents.RectTransform), style: null, color: sectionContentBackgroundColor);
                GUILayoutGroup featuresContents = new(new RectTransform(Vector2.One, featuresFrame.RectTransform, Anchor.Center)) { AbsoluteSpacing = sectionContentSpacing };
                foreach ((FacilityResource facilityResource, int amount) in facilityResources.OrderBy(static kvp => kvp.Key.Prefab.Name))
                {
                    CreateTextBlock(featuresContents, facilityResource.Prefab.Name, amount.ToString());
                }
                IEnumerable<ItemPrefab> itemPrefabs;
                if (placedModule != null)
                {
                    //if this is a placed module, check the actual items in the module instead of just the prefab's swappable items
                    itemPrefabs = placedModule.Entities
                        .Select(e => e as Item)
                        .NotNull()
                        .Where(it => it.IsSwappable || it.GetComponent<WorkstationComponent>() != null)
                        .Select(it => it.Prefab);
                }
                else
                {
                    itemPrefabs = modulePrefab.SwappableItems.Concat(modulePrefab.Workstations);
                }

                foreach (var itemPrefab in itemPrefabs.Distinct().OrderBy(ip => ip.Name))
                {
                    //if this swappable device is also one of the "facility resources" (devices that contribute to resources), such as a turret, don't list again here
                    if (facilityResources.Keys.Any(f => f.MatchesItemPrefab(itemPrefab))) { continue; }
                    CreateTextBlock(featuresContents, itemPrefab.Name, itemPrefabs.Count(ip => ip == itemPrefab).ToString());
                }

                int featuresHeight = featuresContents.Children.Sum(c => c.Rect.Height) + (featuresContents.Children.Count() - 1) * featuresContents.AbsoluteSpacing;
                featuresContents.RectTransform.Resize(new Point(featuresContents.Rect.Width, featuresHeight), resizeChildren: false);
                featuresFrame.RectTransform.Resize(new Point(featuresFrame.Rect.Width, featuresHeight + sectionContentSpacing * 2), resizeChildren: false);
            }

            // cost / requirements
            bool showPrice = modulePrefab.Price != 0;
            bool showResearch = modulePrefab.RequiredResearch != Identifier.Empty;
            bool showItems = modulePrefab.ResourceCosts.Any();
            if (modulePrefab.Sold &&
                (showPrice || showResearch || showItems))
            {
                InsertSpacing();
                GUITextBlock requirementsHeader = new(new RectTransform(Vector2.UnitX, moduleInfoContents.RectTransform), TextManager.Get("OutpostBuilderUI.ModuleRequirements"), GUIStyle.TextColorNormal,
                    style: null, color: sectionHeaderBackgroundColor)
                    {
                        Padding = textPadding
                    };
                requirementsHeader.RectTransform.Resize(new Point(requirementsHeader.Rect.Width, requirementsHeader.Rect.Height + sectionContentSpacing * 2));

                GUIFrame requirementsFrame = new(new RectTransform(Vector2.UnitX, moduleInfoContents.RectTransform), style: null, sectionContentBackgroundColor);
                GUILayoutGroup requirementsContents = new(new RectTransform(Vector2.One, requirementsFrame.RectTransform, showItems ? Anchor.BottomCenter : Anchor.Center)) { AbsoluteSpacing = sectionContentSpacing };

                GUITextBlock timeBuildText = CreateTextBlock(requirementsContents, TextManager.Get("OutpostBuilderUI.TimeToBuild"), TextManager.GetWithVariable("OutpostBuilderUI.WorldStepFormat", "[steps]", modulePrefab.TimeStepsToBuild.ToString()));
                timeBuildText.TextColor = GUIStyle.TextColorBright;

                if (showPrice)
                {
                    GUITextBlock priceText = CreateTextBlock(requirementsContents, TextManager.Get("SubEditor.Price"), TextManager.FormatCurrency(modulePrefab.Price));
                    priceText.OnAddedToGUIUpdateList += component => ((GUITextBlock)component).TextColor = OutpostBuilder.CanAfford(modulePrefab, checkRequiredResources: false) ? GUIStyle.TextColorBright : GUIStyle.Red;
                }

                if (showResearch)
                {
                    if (!TechnologyPrefab.Prefabs.TryGet(modulePrefab.RequiredResearch, out TechnologyPrefab? technologyPrefab))
                    {
                        DebugConsole.AddWarning($"Failed to find technology prefab {modulePrefab.RequiredResearch}");
                    }
                    else
                    {
                        GUITextBlock technologyText = CreateTextBlock(requirementsContents, TextManager.Get("Research"), technologyPrefab.GetDisplayName());
                        technologyText.OnAddedToGUIUpdateList += component => ((GUITextBlock)component).TextColor = OutpostBuilder.HasRequiredResearch(modulePrefab) ? GUIStyle.TextColorBright : GUIStyle.Red;
                        technologyText.ToolTip = technologyPrefab.Description;
                    }
                }
                if (showItems)
                {
                    GUIListBox requiredResourceList = new(new RectTransform(Vector2.UnitX, requirementsContents.RectTransform, minSize: new Point(0, GUI.IntScale(50))), isHorizontal: true);
                    UpgradeStore.CreateMaterialCostsElement(requiredResourceList, modulePrefab.ResourceCosts);
                }

                int requirementsHeight = requirementsContents.Children.Sum(c => c.Rect.Height) + (requirementsContents.Children.Count() - 1) * requirementsContents.AbsoluteSpacing;
                requirementsContents.RectTransform.Resize(new Point(requirementsContents.Rect.Width, requirementsHeight), resizeChildren: false);
                requirementsFrame.RectTransform.Resize(new Point(requirementsFrame.Rect.Width, requirementsHeight + sectionContentSpacing * (showItems ? 1 : 2)), resizeChildren: false);
            }
        }

        LocalizedString description = modulePrefab?.DisplayDescription ?? outpostInfo.Description;
        if (!description.Value.IsNullOrEmpty())
        {
            InsertSpacing();
            GUITextBlock descriptionBlock = new(new RectTransform(new Vector2(0.9f, 0), moduleInfoContents.RectTransform), description, font: GUIStyle.SmallFont, textAlignment: Alignment.Center, wrap: true);
            descriptionBlock.RectTransform.MinSize = new Point(0, (int)descriptionBlock.TextSize.Y + sectionContentSpacing * 2);
        }

        if (pendingModule != null)
        {
            InsertSpacing();
            
            int totalTimeToBuild = 
                pendingModule.ModulePrefab?.TimeStepsToBuild ??
                //if the prefab is somehow not found, let's assume the current remaining time is the total time to build
                pendingModule.TimeStepsRemainingToBuild;
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), moduleInfoContents.RectTransform), GetTimeToBuildText(pendingModule), 
                textAlignment: Alignment.Center, textColor: GUIStyle.Red);
            var buttonText = TextManager.Get("OutpostBuilderUI.CancelPendingModule");
            var cancelPendingButton = CreateButton("OutpostBuilderUI.CancelPendingModule", moduleInfoContents);
            cancelPendingButton.OnClicked = (btn, userdata) =>
            {
                OutpostBuilder.TryRemovePendingModule(outpost, outpostData, pendingModule, client: null, createNetworkEvent: true);
                selectedPendingModule = null;
                recreateMiniMap = true;
                if (modulePrefab != null)
                {
                    UpdateModuleLimitText(modulePrefab.Category);   
                }
                OutpostManagementUI.Instance?.OutpostOverviewUI?.Refresh();
                return true;
            };
            if (!OutpostBuilder.AllowedToManageOutpost())
            {
                cancelPendingButton.Enabled = false;
                cancelPendingButton.ToolTip = TextManager.Get("OutpostBuilderUI.NoPermissions");
            }
        }

        if (placedModule != null)
        {
            InsertSpacing();
            GUIButton deleteButton = CreateButton(outpostData.IsRemovalPending(placedModule) ? "OutpostBuilderUI.CancelModuleDeletion" : "OutpostBuilderUI.DeleteModule", moduleInfoContents);
            deleteButton.OnClicked = (btn, userdata) =>
            {
                if (outpostData.IsRemovalPending(placedModule))
                {
                    OutpostBuilder.CancelModuleRemoval(outpostData, placedModule, createNetworkEvent: true);
                    btn.Text = TextManager.Get("OutpostBuilderUI.DeleteModule");
                }
                else
                {
                    OutpostBuilder.RemoveModule(outpostData, placedModule, createNetworkEvent: true);
                    btn.Text = TextManager.Get("OutpostBuilderUI.CancelModuleDeletion");
                }
                recreateMiniMap = true;
                OutpostManagementUI.Instance?.OutpostOverviewUI?.Refresh();
                return true;
            };
            InsertSpacing();

            if (!OutpostBuilder.AllowedToManageOutpost())
            {
                deleteButton.Enabled = false;
                deleteButton.ToolTip = TextManager.Get("OutpostBuilderUI.NoPermissions");
            }
            else if (placedModule == outpost.OutpostModules.First())
            {
                deleteButton.Enabled = false;
                deleteButton.ToolTip = TextManager.Get("OutpostBuilderUI.CantRemoveInitialModule");
            }
            // modules that attach to a staircase or other special connection that doesn't have a door shouldn't be removable if there's no door to reattach to after removing
            else if (placedModule.PreviousGap != null && placedModule.PreviousGap.ConnectedDoor == null)
            {
                deleteButton.Enabled = false;
                deleteButton.ToolTip = TextManager.Get("OutpostBuilderUI.NoDoorBetweenModules");
            }
            else if (
                //can't remove if other modules are attached (and not pending removal too)
                outpost.OutpostModules.Any(existingModule => existingModule.PreviousModule == placedModule && !outpostData.IsRemovalPending(existingModule)) ||
                //can't cancel removal if attached to a module that's pending removal
                outpost.OutpostModules.Any(existingModule => existingModule == placedModule.PreviousModule && outpostData.IsRemovalPending(existingModule)) ||
                //can't remove if there's modules pending to be attached to this module
                outpostData.PendingOutpostModules.Any(pendingModule => placedModule.AllGaps.Any(gap => gap.Gap == pendingModule.Gap)))
            {
                deleteButton.Enabled = false;
                deleteButton.ToolTip = TextManager.Get("OutpostBuilderUI.CantRemoveWhenOtherModulesAttached");
            }
        }

        int height = moduleInfoContents.Children.Sum(c => c.Rect.Height) + (moduleInfoContents.Children.Count() - 1) * moduleInfoContents.AbsoluteSpacing;
        moduleInfoContents.RectTransform.Resize(new Point(moduleInfoContents.Rect.Width, height), resizeChildren: false);
        moduleInfoPanel.RectTransform.Resize(new Point(moduleInfoPanel.Rect.Width, (int)(height / 0.92f)), resizeChildren: false);

        void InsertSpacing() => new GUIFrame(new RectTransform(new Point(moduleInfoContents!.Rect.Width, GUI.IntScale(10)), moduleInfoContents.RectTransform), style: null);
        GUITextBlock CreateTextBlock(GUIComponent parent, LocalizedString headerText, LocalizedString text)
        {
            GUITextBlock header = new(new RectTransform(Vector2.UnitX, parent.RectTransform) { IsFixedSize = true }, headerText) { Padding = textPadding };
            header.Text = ToolBox.LimitString(header.Text, header.Font, header.Rect.Width - GUI.IntScale(30));
            if (header.Text != headerText)
            {
                header.ToolTip = headerText;
            }
            return new GUITextBlock(new RectTransform(Vector2.One, header.RectTransform), text, font: GUIStyle.SmallFont, textAlignment: Alignment.Right) { Padding = textPadding, CanBeFocused = false };
        }
        static GUIButton CreateButton(string buttonText, GUIComponent parent)
        {
            GUIFrame buttonContainer = new(new RectTransform(Vector2.UnitX, parent.RectTransform), style: null);
            GUIButton btn = new(new RectTransform(new Vector2(0.75f, 1), buttonContainer.RectTransform, Anchor.Center), TextManager.Get(buttonText), style: "GUIButtonSmall");
            buttonContainer.InheritChildrenMinHeight();
            return btn;
        }
    }

    private LocalizedString GetTimeToBuildText(OutpostBuilder.PendingOutpostModule pendingModule)
    {
        int totalTimeToBuild =
            pendingModule.ModulePrefab?.TimeStepsToBuild ??
            //if the prefab is somehow not found, let's assume the current remaining time is the total time to build
            pendingModule.TimeStepsRemainingToBuild;
        return TextManager.GetWithVariables("OutpostBuilderUI.ConstructionInProgress",
                ("[current]", (totalTimeToBuild - pendingModule.TimeStepsRemainingToBuild).ToString()),
                ("[max]", totalTimeToBuild.ToString() ?? "unknown"));
    }

    public void HideModuleInfoPanel()
    {
        if (moduleInfoPanel != null) { moduleInfoPanel.RectTransform.Parent = null; }
    }

    private void FocusItemInModule(Item item)
    {
        ItemSwapUI.ClearSwapUI();
        if (!miniMapSwappableItemElements.TryGetValue(item, out GUIComponent? element)) { return; }
        if (miniMap is null) { return; }

        FocusElement(element);
        ItemSwapUI.CreateSwapUIFor(item);
    }

    private void FocusModule(PlacedModule? module)
    {
        ItemSwapUI.ClearSwapUI();
        if (module is null || miniMap is null) { return; }

        if (!miniMapModuleElements.TryGetValue(module, out GUIComponent? element)) { return; }
        FocusElement(element);
    }

    private void FocusElement(GUIComponent element, float duration = 0.2f)
    {
        if (miniMap is null) { return; }
        CoroutineManager.StartCoroutine(FocusToElementOverTime(element, duration));

    }

    private IEnumerable<CoroutineStatus> FocusToElementOverTime(GUIComponent element, float duration)
    {
        if (miniMap != null)
        {
            Vector2 elementPos = element.RectTransform.Rect.Center.ToVector2();
            Vector2 halfSize = miniMap.Rect.Size.ToVector2() / 2;
            Vector2 parentPos = miniMap.Rect.Location.ToVector2();
            Vector2 startOffset = miniMapOffset;
            Vector2 endOffset = parentPos - elementPos + (halfSize / Zoom);
            float t = 0;

            while (t < duration)
            {
                t += CoroutineManager.DeltaTime;
                miniMapOffset = Vector2.SmoothStep(startOffset, endOffset, t / duration);
                refreshMiniMapOffsetAndZoom = true;
                yield return CoroutineStatus.Running;
            }
            miniMapOffset = endOffset;
        }
        yield return CoroutineStatus.Success;
    }

    private void DeselectAll()
    {
        moduleList?.Deselect();
        selectedModulePrefab = null;
        SelectPendingModule(null);
        SelectPlacedModule(null);
        recreateMiniMap = true;
        modulesToMove.Clear();
        pendingModulesToMove.Clear();
        moduleElementsToMove.Clear();
        movingModule = null;
        movingPendingModule = null;
    }

    public void RecreateMiniMap()
    {
        recreateMiniMap = true;
    }
    #endregion

    ~OutpostBuilderUI()
    {
        Dispose();
    }

    public void Dispose()
    {
        foreach (var submarineTexture in blueprintSprites.Values)
        {
            CrossThread.RequestExecutionOnMainThread(submarineTexture.Texture2D.Dispose);
        }
        blueprintSprites.Clear();
    }
}