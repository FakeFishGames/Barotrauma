using Barotrauma;
using Barotrauma.Plugins;
using Microsoft.Xna.Framework;
using System.Diagnostics.CodeAnalysis;
using HomeAndHarbor.Resources;
using Barotrauma.Extensions;

namespace HomeAndHarbor;

internal class OutpostManagementUI : IDisposable
{
    public const float LeftPanelWidth = 0.22f;

    private static bool HasOwnedOutpost
        => GameMain.GameSession?.Campaign?.GetExtraField<bool>(CampaignSaveLoadHooks.PlayerHasOwnedOutpost) ?? false;

    public enum Tab
    {
        Overview,
        Building,
        Resources,
        Dwellers,
        Research
    }

    public static OutpostManagementUI? Instance;

    private bool wasChatBoxOpen;
    private bool isOpen = false;

    private readonly Location location;
    private readonly PersistentOutpostData? outpostData;
    private readonly bool IsPlayerOwned;
    private readonly Submarine? outpost;

    private OutpostOverviewUI? outpostOverviewUI;
    private OutpostBuilderUI? outpostBuilderUI;
    private OutpostResourcesUI? outpostResourcesUI;
    private OutpostDwellerManagementUI? outpostDwellerManagementUI;

    private readonly CampaignMode? campaign;

    public readonly Identifier OutpostUIOpenedHintTag = "outpostuiopened".ToIdentifier();

    public OutpostBuilderUI? OutpostBuilderUI
        => outpostBuilderUI;

    public OutpostResourcesUI? OutpostResourcesUI
        => outpostResourcesUI;

    public OutpostDwellerManagementUI? OutpostDwellerManagementUI
        => outpostDwellerManagementUI;

    public OutpostOverviewUI? OutpostOverviewUI
        => outpostOverviewUI;

    private GUIButton? saveAndReloadLevelButton;

    public IEnumerable<TechnologyUI> TechnologyUIs { get; private set; }

    #region UI elements

    private GUIFrame? UIFrame;
    private GUIButton? toggleUIButton;
    private GUIComponent? toggleUIButtonNotification;

    private Tab selectedTab;

    private record class TabUI(GUIFrame Frame, GUIButton Button, GUIButton NotificationButton, GUIImage LockIcon);

    private readonly Dictionary<Tab, TabUI> tabs = new Dictionary<Tab, TabUI>();

    private GUIFrame? populationFrame;
    private GUIFrame? researchPointsFrame;
    private GUIFrame? nuclearFuelFrame;
    private GUIFrame? ethanolFuelFrame;
    private GUIFrame? foodFrame;

    private GUILayoutGroup topFrameContents;
    #endregion

    private bool needsReconstruction;

    private static readonly Identifier ReactorFuelResourceIdentifier = "reactorfuel".ToIdentifier(),
                                       EthanolFuelResourceIdentifier = "ethanol".ToIdentifier(),
                                       ResearchPointResourceIdentifier = "researchpoints".ToIdentifier(),
                                       PopulationResourceIdentifier = "basepopulation".ToIdentifier(),
                                       FoodResourceIdentifier = "food".ToIdentifier();

    private readonly Resource? reactorFuelResource,
                               ethanolResource,
                               researchPointsResource,
                               populationResource,
                               foodResource;

    private static readonly Identifier ReactorFuelIndicatorIdentifier = new Identifier("ReactorFuel"),
                                       EthanolFuelIndicatorIdentifier = new Identifier("EthanolFuel");

    #region Initialization

    public OutpostManagementUI(Location location, PersistentOutpostData? outpostData, Submarine? outpost)
    {
        IsPlayerOwned = outpostData is { IsPlayerOwned: true };
        this.location = location;
        this.outpostData = outpostData;
        this.outpost = outpost;

        if (outpostData?.ResourceManager is not null)
        {
            reactorFuelResource = outpostData.ResourceManager.GetResource(ReactorFuelResourceIdentifier);
            ethanolResource = outpostData.ResourceManager.GetResource(EthanolFuelResourceIdentifier);
            researchPointsResource = outpostData.ResourceManager.GetResource(ResearchPointResourceIdentifier);
            populationResource = outpostData.ResourceManager.GetResource(PopulationResourceIdentifier);
            foodResource = outpostData.ResourceManager.GetResource(FoodResourceIdentifier);
        }

        campaign = GameMain.GameSession?.Campaign;

        CreateUI(location);
        GCHelper.RegisterForWatch(this, notes: $"OutpostManagementUI {outpost?.Info?.Name}");
    }

    [MemberNotNull(nameof(TechnologyUIs),
                   nameof(topFrameContents),
                   nameof(saveAndReloadLevelButton))]
    private void CreateUI(Location location)
    {
        tabs.Clear();
        UIFrame?.Parent?.RemoveChild(UIFrame);

        UIFrame = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), color: Color.Black * 0.9f);

        var innerFrame = new GUIFrame(new RectTransform(new Vector2(0.98f, 0.96f), UIFrame.RectTransform, Anchor.Center), style: null);

        var verticalLayout = new GUILayoutGroup(new RectTransform(Vector2.One, innerFrame.RectTransform))
        {
            Stretch = true,
            AbsoluteSpacing = 1
        };

        new GUIFrame(new RectTransform(new Point(verticalLayout.Rect.Width, HUDLayoutSettings.ButtonAreaTop.Height), verticalLayout.RectTransform) 
            { IsFixedSize = true}, 
            style: null);

        var topFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.15f), verticalLayout.RectTransform, Anchor.Center) 
            { MaxSize = new Point(int.MaxValue, GUI.IntScale(65)) }, style: null);
        topFrameContents = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.9f), topFrame.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterRight)
        {
            AbsoluteSpacing = GUI.IntScale(10)
        };

        string locationNameLabel = 
            //show the name of the location if we're currently at a player-owned outpost or if there's only one player-owned outpost we can be showing in the UI
            IsPlayerOwned || PersistentOutpostData.GetOwnedLocations().Count() == 1 ? 
                location.DisplayName.Value : 
                TextManager.Get("outpostbuilderui.title").Value;

        var locationNameBox = new GUITextBox(new RectTransform(new Vector2(0.3f, 1.0f), topFrameContents.RectTransform), 
                                             locationNameLabel, font: GUIStyle.LargeFont, textAlignment: Alignment.CenterLeft, style: "GUITextBoxNoBorder", createPenIcon: true)
        {
            Color = Color.Transparent,
            OutlineColor = Color.Transparent,
            TextColor = GUIStyle.TextColorNormal,
            MaxTextLength = Location.MaxNameLength,
            IgnoreLayoutGroups = true,
            Enabled = false
        };
        //use same color for disabled, this is used as just a readonly header for clients with no permissions
        locationNameBox.TextBlock.DisabledTextColor = locationNameBox.TextColor;

        var penIcon = new GUIImage(new RectTransform(new Vector2(0.5f), locationNameBox.RectTransform, Anchor.CenterRight, scaleBasis: ScaleBasis.Smallest), style: "TextBoxIcon")
        {
            ToolTip = TextManager.Get("outpostui.renameoutpost")
        };
        ResizeNameBox(locationNameLabel);

        locationNameBox.TextGetter = () =>
        {
            //resize the box to move the pen icon next to the end of the text
            if (IsPlayerOwned && location.DisplayName.Value != locationNameBox.Text)
            {
                ResizeNameBox(location.DisplayName.Value);
                locationNameLabel = location.DisplayName.Value;
            }
            return locationNameLabel;
        };
        void ResizeNameBox(string name) => locationNameBox.RectTransform.Resize(new Point((int)(locationNameBox.Font.MeasureString(name).X + GUI.IntScale(50)), locationNameBox.Rect.Height), resizeChildren: true);

        locationNameBox.OnAddedToGUIUpdateList += (GUIComponent component) =>
        {
            component.Enabled = penIcon.Visible = IsPlayerOwned && OutpostBuilder.AllowedToManageOutpost();
        };

        locationNameBox.OnSelected += (_, _) =>
        {
            CreateRenamePopup();
            locationNameBox.Deselect();
        };

        if (IsPlayerOwned)
        {
            CreateBalanceElement(new Vector2(GameMain.IsMultiplayer ? 0.25f : 0.1f, 1.0f), topFrameContents);

            populationFrame = CreateResourceIndicator(topFrameContents, "PopulationIcon", populationResource);
            researchPointsFrame = CreateResourceIndicator(topFrameContents, "ResearchPointIcon", researchPointsResource, onlyShowGeneration: true);
            nuclearFuelFrame = CreateResourceIndicator(topFrameContents, "NuclearFuelIcon", reactorFuelResource);
            ethanolFuelFrame = CreateResourceIndicator(topFrameContents, "EthanolFuelIcon", ethanolResource);
            foodFrame = CreateResourceIndicator(topFrameContents, "FoodIcon", foodResource);
        }
        else
        {
            researchPointsFrame = CreateResearchPointFrameWorkaround(topFrameContents);
        }

        var tabButtonContainer = new GUILayoutGroup(new RectTransform(new Vector2(LeftPanelWidth * 1.25f, 0.03f), verticalLayout.RectTransform) { MaxSize = new Point(int.MaxValue, GUI.IntScale(50)) }, isHorizontal: true);

        var tabContents = new GUIFrame(new RectTransform(Vector2.One, verticalLayout.RectTransform), style: null);

        CreateTab(Tab.Overview, "outpostbuilderui.overview");
        outpostOverviewUI?.Dispose();
        outpostOverviewUI = new OutpostOverviewUI(this, campaign, location, outpostData, parent: tabs[Tab.Overview].Frame);

        if (outpost != null && outpostData != null)
        {
            CreateTab(Tab.Building, "outpostbuilderui.outpostmodules");
            CreateTab(Tab.Resources, "outpostbuilderui.resources");
            CreateTab(Tab.Dwellers, "outpostui.dwellers");
            //these tabs are only available when the player is in a player-owned outpost, if not we don't need to create the UIs
            if (IsPlayerOwned)
            {
                outpostBuilderUI = new OutpostBuilderUI(location, outpostData, outpost, parent: tabs[Tab.Building].Frame);
                outpostResourcesUI = new OutpostResourcesUI(outpostData, outpost, parent: tabs[Tab.Resources].Frame);
                outpostDwellerManagementUI = new OutpostDwellerManagementUI(outpostData, outpost, parent: tabs[Tab.Dwellers].Frame, popupParent: innerFrame);
            }

            if (Level.Loaded is { LocationSetByDebugCommands: true })
            {
                var blocker = new GUIFrame(new RectTransform(Vector2.One, UIFrame.RectTransform), style: null, color: Color.Black * 0.75f);
                new GUITextBlock(new RectTransform(new Vector2(0.25f), blocker.RectTransform, Anchor.Center),
                    text:
                        "The current location has been changed with console commands\n" +
                        "and the currently loaded location might not match the location on the campaign map.\n" +
                        "Cannot access the outpost management UI until you enter a new level.",
                    textColor: Color.Magenta);
            }
        }

        CreateTab(Tab.Research, "research");

        GUITextBlock.AutoScaleAndNormalize(tabs.Values.Select(static b => b.Button.TextBlock));
        tabButtonContainer.InheritChildrenMinHeight();
        verticalLayout.Recalculate();

        outpostBuilderUI?.RecreateMiniMap();

        var bottomFrame = new GUIFrame(new RectTransform(new Point(verticalLayout.Rect.Width, HUDLayoutSettings.ButtonAreaTop.Height), verticalLayout.RectTransform, Anchor.BottomCenter)
            { IsFixedSize = true },
            style: null);

        verticalLayout.Recalculate();
        TechnologyUIs = TechnologyUI.CreateTechTreesForPrefabs(tabs[Tab.Research].Frame, outpostData).Values;

        var bottomFrameContents = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.9f), bottomFrame.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterRight)
        {
            AbsoluteSpacing = GUI.IntScale(10)
        };

        saveAndReloadLevelButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), bottomFrameContents.RectTransform), TextManager.Get("outpostbuilderui.SaveAndReloadLevel"), 
            style: "OutpostManagementBlankButton")
        {
            ToolTip = TextManager.Get("outpostbuilderui.SaveAndReloadLevel.tooltip"),
            OnClicked = (btn, userdata) =>
            {
                SetOpen(false);
                if (GameMain.Client != null)
                {
                    GameNetwork.Send(Plugin.NetworkHeaders.SaveAndReloadLevel, new OutpostBuilder.SaveAndReloadCurrentLocation());
                }
                else
                {
                    GameMain.GameSession?.Campaign?.SaveAndReloadLevel();
                }
                return true;
            }
        };
        saveAndReloadLevelButton.TextBlock.AutoScaleHorizontal = true;

        var worldStepCounter = TabMenu.CreateWorldStepCounter(bottomFrameContents, relativeSize: new Vector2(0.4f, 1.0f), frameStyle: "GUIToolTip");
        worldStepCounter.RectTransform.SetPosition(Anchor.Center);
        worldStepCounter.IgnoreLayoutGroups = true;

        SelectTab(IsPlayerOwned ? Tab.Overview : Tab.Research);

        PGameScreen.RegisterResolutionChangeEvent(nameof(OutpostManagementUI).ToIdentifier(), OnResolutionChanged);
        return;

        void CreateTab(Tab tab, string buttonText)
        {
            var button = new GUIButton(new RectTransform(new Vector2(1.0f / 4f, 1.0f), tabButtonContainer.RectTransform),
                                       TextManager.Get(buttonText), style: "GUITabButton")
            {
                OnClicked = (btn, userdata) => { SelectTab(tab); return true; }
            };

            var lockIcon = new GUIImage(new RectTransform(new Vector2(0.6f), button.RectTransform, Anchor.Center, scaleBasis: ScaleBasis.Smallest), style: "RedLockedIcon")
            {
                CanBeFocused = false,
                Visible = false
            };

            var notification = new GUIButton(new RectTransform(Vector2.One * 0.7f, button.RectTransform, scaleBasis: ScaleBasis.BothHeight, anchor: Anchor.BottomRight), style: "TalentPointNotification")
            {
                Visible = false
            };

            var frame = new GUIFrame(new RectTransform(Vector2.One, tabContents.RectTransform), style: null);

            tabs[tab] = new TabUI(frame, button, notification, lockIcon);
        }

        void CreateBalanceElement(Vector2 size, GUIComponent parent)
        {
            var balanceFrame = new GUIFrame(new RectTransform(size, parent.RectTransform), style: "UpgradeUIFrame");
            var balanceContents = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.8f), balanceFrame.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterRight)
            {
                CanBeFocused = true
            };
            var balanceElement = CampaignUI.AddBalanceElement(balanceContents, Vector2.One);
            if (balanceElement is { } balElement)
            {
                balElement.ParentComponent.OnAddedToGUIUpdateList += (_) =>
                {
                    balanceElement = CampaignUI.UpdateBalanceElement(balanceElement);
                };
            }

            // Show an orange "i" symbol in the empty top left side of the box if there's uncollected tax revenue
            var uncollectedTaxesIcon = new GUIImage(new RectTransform(new Vector2(0.4f), balanceFrame.RectTransform, Anchor.TopLeft,
                scaleBasis: ScaleBasis.BothHeight),
                style: "GUINotificationButton")
            {
                Color = GUIStyle.Orange,
                Visible = false
            };

            balanceContents.OnAddedToGUIUpdateList += (_) =>
            {
                if (outpostData == null)
                {
                    uncollectedTaxesIcon.Visible = false;
                }
                else
                {
                    uncollectedTaxesIcon.ToolTip = RichString.Rich(GetUncollectedTaxesTooltip(outpostData));
                    uncollectedTaxesIcon.Visible = outpostData.OutpostWalletBalance > 0;
                }
            };

            balanceFrame.RectTransform.MinSize = new Point(balanceContents.Children.Sum(static c => c.Rect.Width), 0);
        }
    }

    private const string IndicatorTextUserData = "text";

    private GUIFrame? CreateResourceIndicator(GUIComponent parent, string iconTag, Resource? resource, bool onlyShowGeneration = false)
    {
        if (resource is null) { return null; }

        RichString labelText = GetResourceIndicatorLabel(resource, out LocalizedString tooltip, onlyShowGeneration);
        GUIFrame resourceFrame = new GUIFrame(new RectTransform(new Vector2(0.06f, 1.0f), parent.RectTransform), style: "UpgradeUIFrame")
        {
            ToolTip = tooltip,
        };

        GUILayoutGroup layout = new GUILayoutGroup(new RectTransform(new Vector2(0.85f, 0.7f), resourceFrame.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterLeft)
        {
            Stretch = true
        };

        GUIImage icon = new GUIImage(new RectTransform(Vector2.One, layout.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: iconTag)
        {
            CanBeFocused = false
        };

        GUITextBlock label = new GUITextBlock(new RectTransform(Vector2.One, layout.RectTransform),
                         text: labelText, font: GUIStyle.IconFont, textAlignment: Alignment.Center)
        {
            UserData = IndicatorTextUserData,
            CanBeFocused = false
        };

        return resourceFrame;
    }

    /// <summary>
    /// Since research menu is available globally, and the research point resource is also hardcoded to
    /// be global resource, just find any outpost with a research manager to fetch a research point resource.
    /// This could be done without having to hunt the resource since the info is all global,
    /// but this avoids a lot of duplicate code to figure out the generation which may become outdated
    /// if we add another way of generating research points.
    /// </summary>
    private GUIFrame? CreateResearchPointFrameWorkaround(GUIComponent parent)
    {
        if (campaign?.Map is not { } map) { return null; }
        foreach (Location l in map.Locations)
        {
            if (PersistentOutpostData.GetPersistentOutpostData(l).ResourceManager is not { } rm) { continue; }
            if (rm.GetResource(ResearchPointResourceIdentifier) is not { } researchPointResource) { continue; }

            return CreateResourceIndicator(parent, "ResearchPointIcon", researchPointResource, onlyShowGeneration: true);
        }

        return null;
    }

    private void OnResolutionChanged()
    {
        if (GameMain.GameSession?.Campaign != campaign) { return; }

        needsReconstruction = true;
    }

    private static void SetElementVisibility(GUIComponent? component, bool isVisible)
    {
        if (component is null) { return; }
        component.Visible = isVisible;
        component.IgnoreLayoutGroups = !isVisible;
    }

    public void SelectTab(Tab selectedTab)
    {
        switch (selectedTab)
        {
            case Tab.Building:
                outpostBuilderUI?.OnOpened();
                break;
            case Tab.Dwellers:
                outpostDwellerManagementUI?.OnOpened();
                break;
            case Tab.Resources:
                OutpostResourcesUI?.OnOpened();
                break;
        }

        foreach ((Tab tab, TabUI element) in tabs)
        {
            SetElementVisibility(element.Frame, isVisible: tab == selectedTab);
            element.Button.Selected = tab == selectedTab;
        }
        this.selectedTab = selectedTab;
        UpdateResourceIndicatorVisibilities();
    }

    static GUILayoutGroup AddLabeledValue(GUIComponent parent, Vector2 relativeSize, LocalizedString text, GUITextBlock.TextGetterHandler textGetter)
    {
        var rt = new RectTransform(relativeSize, parent.RectTransform)
        {
            MaxSize = new Point(GUI.IntScale(GUI.AdjustForTextScale(120)), int.MaxValue)
        };
        var balanceContainer = new GUILayoutGroup(rt, childAnchor: Anchor.TopRight)
        {
            RelativeSpacing = 0.005f
        };
        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), balanceContainer.RectTransform), text,
            font: GUIStyle.Font, textAlignment: Alignment.BottomRight)
        {
            AutoScaleVertical = true,
            ForceUpperCase = ForceUpperCase.Yes
        };
        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), balanceContainer.RectTransform), "",
            textColor: Color.White, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.TopRight)
        {
            AutoScaleVertical = true,
            TextScale = 1.1f,
            TextGetter = textGetter
        };
        return balanceContainer;
    }

    #endregion

    public static void UpdateUI(GameMode _, float deltaTime)
    {
        if (HasOwnedOutpost && Level.Loaded?.StartLocation is { } startLocation)
        {
            PersistentOutpostData? outpostData = Level.Loaded.Type is LevelData.LevelType.Outpost
                                                    ? PersistentOutpostData.GetPersistentOutpostData(startLocation)
                                                    : null;
            if (Instance == null || Instance.outpostData != outpostData || Instance.IsPlayerOwned != outpostData is { IsPlayerOwned: true } || Instance.outpost is { Removed: true })
            {
                Instance?.Dispose();
                Instance = new OutpostManagementUI(startLocation, outpostData, Level.Loaded.StartOutpost);
            }
            Instance.Update(deltaTime);
            Instance.OutpostResourcesUI?.UpdateUI(deltaTime);
        }
        else
        {
            Instance?.Dispose();
            Instance = null;
        }
    }

    public static void RoundEnded(GameSession gameSession)
    {
        Instance?.Dispose();
        Instance = null;
    }

    private float resourceGeneratorCheckTimer;
    private const float ResourceGeneratorCheckInterval = 5f;

    private void Update(float deltaTime)
    {
        if (GameMain.GameSession?.TopLeftButtonGroup is { } topLeftButtonGroup)
        {
            var existingButton = topLeftButtonGroup.GetChildByUserData(nameof(OutpostManagementUI));
            if (existingButton == null)
            {
                int buttonHeight = GUI.IntScale(40);
                Vector2 buttonSpriteSize = GUIStyle.GetComponentStyle("OutpostManagementButton").GetDefaultSprite().size;
                int buttonWidth = (int)((buttonHeight / buttonSpriteSize.Y) * buttonSpriteSize.X);
                Point buttonSize = new Point(buttonWidth, buttonHeight);
                toggleUIButton = new GUIButton(new RectTransform(buttonSize, parent: GameMain.GameSession.TopLeftButtonGroup.RectTransform), style: "OutpostManagementButton")
                {
                    Color = GUIStyle.Orange,
                    UserData = nameof(OutpostManagementUI),
                    ToolTip = TextManager.Get("outpostbuilderui.title"),
                    OnClicked = (GUIButton btn, object userdata) =>
                    {
                        SetOpen(!isOpen);
                        return true;
                    }                    
                };

                toggleUIButtonNotification = GameSession.CreateNotificationIcon(toggleUIButton);
                toggleUIButtonNotification.Visible = false;
                toggleUIButtonNotification.CanBeFocused = true;

                if (!HintManager.IsHintIgnored(OutpostUIOpenedHintTag))
                {
                    toggleUIButton.OnAddedToGUIUpdateList += (GUIComponent btn) =>
                    {
                        if (HintManager.IsHintIgnored(OutpostUIOpenedHintTag)) { return; }
                        if (btn.FlashTimer <= 0.0f)
                        {
                            btn.Flash(GUIStyle.Green, flashDuration: 1.0f);
                            btn.Pulsate(Vector2.One, new Vector2(1.5f), duration: 1.0f);
                        }
                    };
                }
            }
        }

        if (toggleUIButtonNotification != null)
        {
            toggleUIButtonNotification.Visible = false;
            tabs[Tab.Research].NotificationButton.Visible = false;
        }

        if (saveAndReloadLevelButton is { } reloadBtn)
        {
            if (Level.IsLoadedOutpost &&
                Level.Loaded.StartLocation is { } startLocation && 
                PersistentOutpostData.GetPersistentOutpostData(startLocation) is { IsPlayerOwned: true } outpostData)
            {
                reloadBtn.Visible = true;
                bool wasEnabled = reloadBtn.Enabled;
                reloadBtn.Enabled = OutpostBuilder.AllowSaveAndReloadLevel();
                if (wasEnabled != reloadBtn.Enabled)
                {
                    reloadBtn.ToolTip = TextManager.Get(
                        reloadBtn.Enabled ?
                            (outpostData.PendingModuleRemovals.None() ? "outpostbuilderui.SaveAndReloadLevel.tooltip" : "outpostbuilderui.SaveAndReloadLevel.removemodule.tooltip") :
                            (OutpostBuilder.AllowedToManageOutpost() ? "outpostbuilderui.SaveAndReloadLevel.tooltip.disabled" : "outpostbuilderui.nopermissions"));
                }
                if (reloadBtn.Enabled && toggleUIButtonNotification != null)
                {
                    toggleUIButtonNotification.Visible = true;
                    toggleUIButtonNotification.ToolTip = reloadBtn.ToolTip;
                }
            }
            else
            {
                reloadBtn.Visible = false;
            }
        }
        if (toggleUIButtonNotification != null && 
            ResearchManager.TryGetResearchManagerForCampaign(GameMain.GameSession?.Campaign, out ResearchManager? researchManager) &&
            researchPointsResource != null && 
            GetResourceGenerationForResource(researchPointsResource).Increase > 0)
        {
            if (researchManager.ActiveResearch == null || researchManager.ActiveResearch.IsFinished)
            {
                toggleUIButtonNotification.Visible = tabs[Tab.Research].NotificationButton.Visible = true;
                toggleUIButtonNotification.ToolTip = tabs[Tab.Research].NotificationButton.ToolTip = TextManager.Get("outpostbuilderui.researchnotselected");
            }
        }

        if (isOpen)
        {
            GameMain.GameSession?.CrewManager?.DisableCommandUI();
        }

        if (PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.Y))
        {
            CreateUI(location);
        }

        if (resourceGeneratorCheckTimer < ResourceGeneratorCheckInterval)
        {
            resourceGeneratorCheckTimer += deltaTime;
        }
        else
        {
            UpdateResourceIndicatorVisibilities();
            resourceGeneratorCheckTimer = 0f;
        }

        if (needsReconstruction)
        {
            CreateUI(location);
            needsReconstruction = false;
        }
    }

    private void UpdateResourceIndicatorVisibilities()
    {
        if (outpost is null || outpostData is not { IsPlayerOwned: true }) { return; }

        SetElementVisibility(nuclearFuelFrame, isVisible: HasResourceGeneratorOfIdentifier(ReactorFuelIndicatorIdentifier));
        SetElementVisibility(ethanolFuelFrame, isVisible: HasResourceGeneratorOfIdentifier(EthanolFuelIndicatorIdentifier));

        SetText(nuclearFuelFrame, reactorFuelResource);
        SetText(ethanolFuelFrame, ethanolResource);
        SetText(researchPointsFrame, researchPointsResource, onlyShowGeneration: true);
        SetText(populationFrame, populationResource);
        SetText(foodFrame, foodResource);
        return;

        bool HasResourceGeneratorOfIdentifier(Identifier identifier)
            => outpostData.ResourceGenerators.Any(rg => rg.ShowOnInterfaceIdentifier == identifier);

        void SetText(GUIComponent? component, Resource? resource, bool onlyShowGeneration = false)
        {
            if (resource is null) { return; }
            if (component?.FindChild(IndicatorTextUserData, recursive: true) is not GUITextBlock block) { return; }

            RichString label = GetResourceIndicatorLabel(resource, out LocalizedString tooltip, onlyShowGeneration);
            component.ToolTip = tooltip;
            block.Text = label;
        }
    }

    /// <summary>
    /// Checks if the current outpost has an outpost management workstation and based on that enables/disables the tab buttons
    /// </summary>
    public void UpdateTabLocks()
    {
        if (outpostData is not { IsPlayerOwned: true } || outpost is null || outpostOverviewUI?.SelectedLocation != Level.Loaded?.StartLocation)
        {
            foreach ((Tab tab, TabUI tabUI) in tabs)
            {
                //only overview and research tabs are available "remotely" (when not in a player-owned outpost)
                SetLock(tabUI.Button, tabEnabled: tab is Tab.Overview or Tab.Research, 
                    lockTooltip: "outpostbuilderui.unavailablewhennotinoutpost".ToIdentifier());
            }
            return;
        }

        bool hasOutpostManagementItem = OutpostBuilder.DoesHaveOutpostManagementItem(outpost);
        OutpostBuilderUI?.UpdateCatalogLocks(hasOutpostManagementItem);

        foreach ((Tab tab, TabUI tabUI) in tabs)
        {
            tabUI.Button.Visible = true;
            SetLock(tabUI.Button, tab is Tab.Building or Tab.Research or Tab.Overview || hasOutpostManagementItem, lockTooltip: "outpostbuilderui.requiremoduleadmin".ToIdentifier());
        }
        return;

        static void SetLock(GUIButton button, bool tabEnabled, Identifier lockTooltip)
        {
            button.Enabled = tabEnabled;

            // lock icon
            button.GetChild<GUIImage>().Visible = !tabEnabled;

            button.ToolTip = tabEnabled ? "" : TextManager.Get(lockTooltip);
        }
    }

    public GUIButton? GetTabButton(Tab tab)
    {
        return tabs.GetValueOrDefault(tab)?.Button;
    }

    private void SetOpen(bool isOpen)
    {
        GameMain.GameSession.TopLeftButtonGroup?.Recalculate();

        if (isOpen)
        {
            HintManager.IgnoreCustomHint(OutpostUIOpenedHintTag, ignore: true);
            TechnologyUIs.ForEach(tech => tech.OnOpened());
        }

        this.isOpen = isOpen;
        SelectTab(selectedTab);

        var chatBox = GameMain.GameSession.CrewManager?.ChatBox ?? GameMain.Client?.ChatBox;
        if (chatBox != null)
        {
            if (isOpen)
            {
                wasChatBoxOpen = chatBox.ToggleOpen;
                chatBox.ToggleOpen = false;
            }
            else
            {
                chatBox.ToggleOpen = wasChatBoxOpen;
            }
        }

        if (toggleUIButton != null)
        {
            toggleUIButton.Selected = isOpen;
        }

        UpdateTabLocks();
    }

    /// <summary>
    /// Toggles the Outpost Management UI open or closed.
    /// </summary>
    public void ToggleOpen()
    {
        SetOpen(!isOpen);
    }

    /// <summary>
    /// Opens the Outpost Management UI to a specific tab.
    /// </summary>
    /// <param name="tabToOpen">The tab to open. Defaults to Overview.</param>
    public void Open(Tab tabToOpen = Tab.Overview)
    {
        if (!Enum.IsDefined(typeof(Tab), tabToOpen))
        {
            throw new ArgumentOutOfRangeException(nameof(tabToOpen));
        }

        selectedTab = tabToOpen;
        SetOpen(true);
    }

    public static void RecreateMiniMap()
    {
        Instance?.outpostBuilderUI?.RecreateMiniMap();

        // also refresh overview UI, we might be building something new
        Instance?.OutpostOverviewUI?.Refresh();
    }

    /// <summary>
    /// Checks if the outpost management UI should consume the escape key or not.
    /// </summary>
    /// <returns>True if escape has been consumed, false if not.</returns>
    internal static bool HandleEscapeKey()
    {
        if (OutpostDwellerManagementUI.workstationAssignmentPopup is { Closed: false })
        {
            OutpostDwellerManagementUI.workstationAssignmentPopup.Close();
            return true;
        }

        if (Instance is { isOpen: true })
        {
            Instance.SetOpen(false);
            return true;
        }

        return false;
    }

    public static void AddToGUIUpdateList(GameMode _)
    {
        if (Instance is { isOpen: true })
        {
            CharacterHUD.HideHUD = true;
            Character.DisableControls = true;
            Instance.UIFrame?.AddToGUIUpdateList();
        }
    }

    public (float Decrease, float Increase) GetResourceGenerationForResource(Resource mainResource)
    {
        float decrease = 0f,
              increase = 0f;

        Location resourceLocation = mainResource.Location;

        foreach (Resource resource in OutpostResourcesUI.FindAllResources(mainResource))
        {
            var activeGenerators = mainResource.Manager.OutpostData.ResourceGenerators
                                                                    .Where(static rg => rg.IsActive);

            float[] applicableGeneratedAmounts = activeGenerators
                             .SelectMany(rg => rg.ProducedResourcesList.Select(producedResource => (Generator: rg, ProducedResource: producedResource)))
                             .Where(g => g.ProducedResource.Identifier == resource.Identifier && g.ProducedResource.MatchesLocation(resourceLocation))
                             .Select(gp => gp.ProducedResource.GetScaledAmount(gp.Generator.AssignedSkillLevel, mainResource.Manager)
                                           * gp.Generator.GetDiminishingReturnMultiplier(mainResource.Manager.OutpostData))
                             .ToArray();

            decrease += applicableGeneratedAmounts.Where(static a => a < 0f).Sum(static a => MathF.Abs(a));
            increase += applicableGeneratedAmounts.Where(static a => a > 0f).Sum(static a => MathF.Abs(a));

            var conversions = activeGenerators
                                .SelectMany(static rg => 
                                    rg.ConvertedItemsList.Where(ci => rg.IsConversionEnabled(ci))
                                .Select(ci => (rg, Conversion: ci)))
                                .Where(g => 
                                    g.Conversion.HasRequiredConsumedItems(outpostData?.ResourceManager, g.rg.AssignedSkillLevel) &&
                                    g.Conversion.MeetsRequirement(outpostData?.ResourceManager));

            decrease += conversions
                        .Where(g => g.Conversion.ResourceIdentifier == resource.Identifier)
                        .SelectMany(g => g.Conversion.ConsumedItems.Select(item => item.GetScaledAmount(g.rg.AssignedSkillLevel)))
                        .Sum();

            increase += conversions
                        .Where(g => g.Conversion.OutputResource == resource.Identifier)
                        .SelectMany(g => g.Conversion.ProducedItems.Select(item => item.GetScaledAmount(g.rg.AssignedSkillLevel)))
                        .Sum();

            // Calculate every outpost since research points are global
            if (resource is ResearchPointResource && GameMain.GameSession?.Map is { } map)
            {
                foreach (Location otherLocation in map.Locations.Where(l => l != mainResource.Location))
                {
                    PersistentOutpostData data = PersistentOutpostData.GetPersistentOutpostData(otherLocation);
                    if (!data.IsPlayerOwned) { continue; }

                    // There is no reason to reduce research points, so we aren't accounting for the possibility of it being decreased here
                    increase +=
                        data.ResourceGenerators
                            .Where(static rgc => rgc.IsActive)
                            .SelectMany(static g => g.ProducedResourcesList)
                            .Where(pr => pr.Identifier == resource.Identifier)
                            .Sum(static pr => pr.Amount);
                }
            }

            float baseAmount = resource.Amount;
            foreach (PassiveGenerationParams passiveParams in resource.passiveGenerations)
            {
                // ignore generations probabilities, too difficult to calculate
                if (passiveParams.Probability < 1) { continue; }

                if (passiveParams.DisabledOnLocationTypes.Contains(resourceLocation.Type.Identifier)) { continue; }

                if (Requirement.RequirementsMatch(resource.Manager, passiveParams.Requirements, passiveParams.ConditionalComparison, debugLogName: $"GetResourceGenerationForResource {resource.Identifier}"))
                {
                    float generatedAmount = passiveParams.CalculateGeneration(baseAmount);
                    float diff = generatedAmount - baseAmount;

                    switch (diff)
                    {
                        case > 0:
                            increase += MathF.Abs(diff);
                            break;
                        case < 0:
                            decrease += MathF.Abs(diff);
                            break;
                    }

                    if (passiveParams.Interval > 1)
                    {
                        generatedAmount /= passiveParams.Interval;
                    }
                    baseAmount = generatedAmount;
                }
            }
        }

        return (decrease, increase);
    }

    private RichString GetResourceIndicatorLabel(Resource resource, out LocalizedString tooltip, bool onlyShowGeneration = false)
    {
        // Show one decimal for fractional values, e.g. reactor fuel is consumed 0.2 (1/5 of a fuel rod) per work shift
        string value = OutpostResourcesUI.FormatAmount(resource.Amount);
        tooltip = TextManager.Get($"outpostbuilderuitooltip.{resource.Identifier}");
        var decreaseAndIncrease = GetResourceGenerationForResource(resource);
        float generation = -decreaseAndIncrease.Decrease + decreaseAndIncrease.Increase;

        RichString labelText = value;

        if (!MathUtils.NearlyEqual(generation, 0f))
        {
            tooltip += $"\n\n{TextManager.GetWithVariable($"outpostbuilderui.generationtooltip.{(generation > 0 ? "increasing" : "decreasing")}", "[generation]", OutpostResourcesUI.FormatAmount(MathF.Abs(generation)))}";

            if (onlyShowGeneration)
            {
                labelText = RichString.Rich(TextManager.GetWithVariables("outpostbuilderui.pertickformat",
                                                                    ("[generation]", $"{(generation > 0 ? "+" : string.Empty)}{OutpostResourcesUI.FormatAmount(generation)}")));
            }
            else
            {
                labelText = RichString.Rich(TextManager.GetWithVariables("outpostbuilderui.pertickformatwithvalue",
                                                    ("[value]", value),
                                                    ("[generation]", $"{(generation > 0 ? "+" : string.Empty)}{OutpostResourcesUI.FormatAmount(generation)}")));
            }
        }

        string description = TextManager.Get($"resource.{resource.Prefab.Identifier}.description").Value;
        if (!description.IsNullOrEmpty())
        {
            tooltip += $"\n\n{description}";
        }

        return labelText;
    }

    public static LocalizedString GetUncollectedTaxesTooltip(PersistentOutpostData outpostData)
        => TextManager.GetWithVariable("outpostui.uncollectedtaxes", "[amount]", TextManager.FormatCurrency(outpostData.OutpostWalletBalance).Value);

    private void CreateRenamePopup()
    {
        GUIMessageBox renamePopup = new(
            TextManager.Get("outpostbuilderui.renameoutpost"), string.Empty,
            [TextManager.Get("Confirm"), TextManager.Get("Cancel")], minSize: new Point(0, GUI.IntScale(230)));

        GUITextBox newNameBox = new(new(Vector2.One, renamePopup.Content.RectTransform), text: location.DisplayName.Value)
        {
            MaxTextLength = Location.MaxNameLength,
            OnEnterPressed = static (textBox, text) =>
            {
                textBox.Text = Location.SanitizeName(text.Trim());
                return true;
            }
        };
        renamePopup.Buttons[0].OnClicked += (_, _) =>
        {
            if (Location.SanitizeName(newNameBox.Text) is string newName && !newName.IsNullOrWhiteSpace())
            {
                if (GameMain.NetworkMember is { IsClient: true })
                {
                    OutpostBuilder.ClientWriteRenameOutpost(newName);
                }
                else
                {
                    location.TryForceRawName(newName);
                }
                renamePopup.Close();
                return true;
            }
            else
            {
                newNameBox.Flash();
                return false;
            }
        };
        renamePopup.Buttons[1].OnClicked += renamePopup.Close;
    }

    public bool IsTabLocked(Tab tab)
    {
        return !tabs.TryGetValue(tab, out TabUI? value) || !value.Button.Enabled;
    }

    public void Dispose()
    {
        foreach (TechnologyUI techUi in TechnologyUIs)
        {
            techUi.Dispose();
        }

        outpostBuilderUI?.Dispose();
        outpostOverviewUI?.Dispose();
        PGameScreen.DeregisterResolutionChangeEvent(nameof(OutpostManagementUI).ToIdentifier());

        // Remove the button so the next instance recreates it with the correct closure
        toggleUIButton?.Parent?.RemoveChild(toggleUIButton);

        UIFrame?.Parent?.RemoveChild(UIFrame);
    }
}

