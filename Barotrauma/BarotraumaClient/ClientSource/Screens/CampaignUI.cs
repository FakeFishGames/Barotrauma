using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Barotrauma
{
    class CampaignUI
    {
        private CampaignMode.InteractionType selectedTab;

        private GUIFrame[] tabs;

        public CampaignMode.InteractionType SelectedTab => selectedTab;

        private Point prevResolution;

        private GUIComponent locationInfoPanel;

        private GUIListBox missionList;
        private readonly List<GUITickBox> missionTickBoxes = new List<GUITickBox>();
        private readonly List<GUITextBlock> missionRewardTexts = new List<GUITextBlock>();

        private bool hasMaxMissions;

        private GUIButton repairHullsButton, replaceShuttlesButton, repairItemsButton;

        private SubmarineSelection submarineSelection;

        private Location selectedLocation;

        public Action StartRound;

        public LevelData SelectedLevel { get; private set; }
                
        private GUIButton StartButton { get; set; }

        public CampaignMode Campaign { get; }

        public CrewManagement CrewManagement { get; set; }
        private Store Store { get; set; }

        public UpgradeStore UpgradeStore { get; set; }

        public MedicalClinicUI MedicalClinic { get; set; }

        public CampaignUI(CampaignMode campaign, GUIComponent container)
        {
            Campaign = campaign;

            if (campaign.Map == null) { throw new InvalidOperationException("Failed to create campaign UI (campaign map was null)."); }
            if (campaign.Map.CurrentLocation == null) { throw new InvalidOperationException("Failed to create campaign UI (current location not set)."); }

            CreateUI(container);

            campaign.Map.OnLocationSelected += SelectLocation;
            campaign.Map.OnMissionsSelected += (connection, missions) => 
            {
                if (missionList?.Content != null)
                {
                    foreach (GUIComponent missionElement in missionList.Content.Children)
                    {
                        if (missionElement.FindChild(c => c is GUITickBox, recursive: true) is GUITickBox tickBox) 
                        { 
                            tickBox.Selected = missions.Contains(tickBox.UserData as Mission); 
                        }
                    }
                }
            };
        }

        private void CreateUI(GUIComponent container)
        {
            container.ClearChildren();

            tabs = new GUIFrame[Enum.GetValues(typeof(CampaignMode.InteractionType)).Length];

            // map tab -------------------------------------------------------------------------

            tabs[(int)CampaignMode.InteractionType.Map] = CreateDefaultTabContainer(container, new Vector2(0.9f));
            var mapFrame = new GUIFrame(new RectTransform(Vector2.One, GetTabContainer(CampaignMode.InteractionType.Map).RectTransform, Anchor.TopLeft), color: Color.Black * 0.9f);
            new GUICustomComponent(new RectTransform(Vector2.One, mapFrame.RectTransform), DrawMap, UpdateMap);
            new GUIFrame(new RectTransform(Vector2.One, mapFrame.RectTransform), style: "InnerGlow", color: Color.Black * 0.9f)
            {
                CanBeFocused = false
            };

            // crew tab -------------------------------------------------------------------------

            var crewTab = new GUIFrame(new RectTransform(Vector2.One, container.RectTransform), color: Color.Black * 0.9f);
            tabs[(int)CampaignMode.InteractionType.Crew] = crewTab;
            CrewManagement = new CrewManagement(this, crewTab);

            // store tab -------------------------------------------------------------------------
            
            var storeTab = new GUIFrame(new RectTransform(Vector2.One, container.RectTransform), color: Color.Black * 0.9f);
            tabs[(int)CampaignMode.InteractionType.Store] = storeTab;
            Store = new Store(this, storeTab);

            // repair tab -------------------------------------------------------------------------

            tabs[(int)CampaignMode.InteractionType.Repair] = CreateDefaultTabContainer(container, new Vector2(0.7f));
            var repairFrame = new GUIFrame(new RectTransform(Vector2.One, GetTabContainer(CampaignMode.InteractionType.Repair).RectTransform, Anchor.TopLeft), color: Color.Black * 0.9f);
            new GUIFrame(new RectTransform(new Vector2(1.25f, 1.25f), repairFrame.RectTransform, Anchor.Center), style: "OuterGlow", color: Color.Black * 0.7f)
            {
                UserData = "outerglow",
                CanBeFocused = false
            };

            var repairContent = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.85f), repairFrame.RectTransform, Anchor.Center))
            {
                RelativeSpacing = 0.05f,
                Stretch = true
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.2f), repairContent.RectTransform), "", font: GUI.LargeFont)
            {
                TextGetter = GetMoney
            };

            // repair hulls -----------------------------------------------

            var repairHullsHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), repairContent.RectTransform), childAnchor: Anchor.TopRight)
            {
                RelativeSpacing = 0.05f,
                Stretch = true
            };
            new GUIImage(new RectTransform(new Vector2(0.3f, 1.0f), repairHullsHolder.RectTransform, Anchor.CenterLeft), "RepairHullButton")
            {
                IgnoreLayoutGroups = true,
                CanBeFocused = false
            };
            var repairHullsLabel = new GUITextBlock(new RectTransform(new Vector2(0.7f, 0.3f), repairHullsHolder.RectTransform), TextManager.Get("RepairAllWalls"), textAlignment: Alignment.Right, font: GUI.SubHeadingFont)
            {
                ForceUpperCase = true
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), repairHullsHolder.RectTransform), CampaignMode.HullRepairCost.ToString(), textAlignment: Alignment.Right, font: GUI.SubHeadingFont);
            repairHullsButton = new GUIButton(new RectTransform(new Vector2(0.4f, 0.3f), repairHullsHolder.RectTransform) { MinSize = new Point(140, 0) }, TextManager.Get("Repair"))
            {
                OnClicked = (btn, userdata) =>
                {
                    if (Campaign.PurchasedHullRepairs)
                    {
                        Campaign.Money += CampaignMode.HullRepairCost;
                        Campaign.PurchasedHullRepairs = false;
                    }
                    else
                    {
                        if (Campaign.Money >= CampaignMode.HullRepairCost)
                        {
                            Campaign.Money -= CampaignMode.HullRepairCost;
                            GameAnalyticsManager.AddMoneySpentEvent(CampaignMode.HullRepairCost, GameAnalyticsManager.MoneySink.Service, "hullrepairs");
                            Campaign.PurchasedHullRepairs = true;
                        }
                    }
                    GameMain.Client?.SendCampaignState();
                    btn.GetChild<GUITickBox>().Selected = Campaign.PurchasedHullRepairs;

                    return true;
                }
            };
            new GUITickBox(new RectTransform(new Vector2(0.65f), repairHullsButton.RectTransform, Anchor.CenterLeft) { AbsoluteOffset = new Point(10, 0) }, "")
            {
                CanBeFocused = false
            };

            // repair items -------------------------------------------

            var repairItemsHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), repairContent.RectTransform), childAnchor: Anchor.TopRight)
            {
                RelativeSpacing = 0.05f,
                Stretch = true
            };
            new GUIImage(new RectTransform(new Vector2(0.3f, 1.0f), repairItemsHolder.RectTransform, Anchor.CenterLeft), "RepairItemsButton")
            {
                IgnoreLayoutGroups = true,
                CanBeFocused = false
            };
            var repairItemsLabel = new GUITextBlock(new RectTransform(new Vector2(0.7f, 0.3f), repairItemsHolder.RectTransform), TextManager.Get("RepairAllItems"), textAlignment: Alignment.Right, font: GUI.SubHeadingFont)
            {
                ForceUpperCase = true
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), repairItemsHolder.RectTransform), CampaignMode.ItemRepairCost.ToString(), textAlignment: Alignment.Right, font: GUI.SubHeadingFont);
            repairItemsButton = new GUIButton(new RectTransform(new Vector2(0.4f, 0.3f), repairItemsHolder.RectTransform) { MinSize = new Point(140, 0) }, TextManager.Get("Repair"))
            {
                OnClicked = (btn, userdata) =>
                {
                    if (Campaign.PurchasedItemRepairs)
                    {
                        Campaign.Money += CampaignMode.ItemRepairCost;
                        Campaign.PurchasedItemRepairs = false;
                    }
                    else
                    {
                        if (Campaign.Money >= CampaignMode.ItemRepairCost)
                        {
                            Campaign.Money -= CampaignMode.ItemRepairCost;
                            GameAnalyticsManager.AddMoneySpentEvent(CampaignMode.ItemRepairCost, GameAnalyticsManager.MoneySink.Service, "devicerepairs");
                            Campaign.PurchasedItemRepairs = true;
                        }
                    }
                    GameMain.Client?.SendCampaignState();
                    btn.GetChild<GUITickBox>().Selected = Campaign.PurchasedItemRepairs;

                    return true;
                }
            };
            new GUITickBox(new RectTransform(new Vector2(0.65f), repairItemsButton.RectTransform, Anchor.CenterLeft) { AbsoluteOffset = new Point(10, 0) }, "")
            {
                CanBeFocused = false
            };

            // replace lost shuttles -------------------------------------------

            var replaceShuttlesHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.5f), repairContent.RectTransform), childAnchor: Anchor.TopRight)
            {
                RelativeSpacing = 0.05f,
                Stretch = true
            };
            new GUIImage(new RectTransform(new Vector2(0.3f, 1.0f), replaceShuttlesHolder.RectTransform, Anchor.CenterLeft), "ReplaceShuttlesButton")
            {
                IgnoreLayoutGroups = true,
                CanBeFocused = false
            };
            var replaceShuttlesLabel = new GUITextBlock(new RectTransform(new Vector2(0.7f, 0.3f), replaceShuttlesHolder.RectTransform), TextManager.Get("ReplaceLostShuttles"), textAlignment: Alignment.Right, font: GUI.SubHeadingFont)
            {
                ForceUpperCase = true
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), replaceShuttlesHolder.RectTransform), CampaignMode.ShuttleReplaceCost.ToString(), textAlignment: Alignment.Right, font: GUI.SubHeadingFont);
            replaceShuttlesButton = new GUIButton(new RectTransform(new Vector2(0.4f, 0.3f), replaceShuttlesHolder.RectTransform) { MinSize = new Point(140, 0) }, TextManager.Get("ReplaceShuttles"))
            {
                OnClicked = (btn, userdata) =>
                {
                    if (GameMain.GameSession?.SubmarineInfo != null &&
                        GameMain.GameSession.SubmarineInfo.LeftBehindSubDockingPortOccupied)
                    {
                        new GUIMessageBox("", TextManager.Get("ReplaceShuttleDockingPortOccupied"));
                        return true;
                    }

                    if (Campaign.PurchasedLostShuttles)
                    {
                        Campaign.Money += CampaignMode.ShuttleReplaceCost;
                        Campaign.PurchasedLostShuttles = false;
                    }
                    else
                    {
                        if (Campaign.Money >= CampaignMode.ShuttleReplaceCost)
                        {
                            Campaign.Money -= CampaignMode.ShuttleReplaceCost;
                            GameAnalyticsManager.AddMoneySpentEvent(CampaignMode.ShuttleReplaceCost, GameAnalyticsManager.MoneySink.Service, "retrieveshuttle");
                            Campaign.PurchasedLostShuttles = true;
                        }
                    }
                    GameMain.Client?.SendCampaignState();
                    btn.GetChild<GUITickBox>().Selected = Campaign.PurchasedLostShuttles;

                    return true;
                }
            };
            new GUITickBox(new RectTransform(new Vector2(0.65f), replaceShuttlesButton.RectTransform, Anchor.CenterLeft) { AbsoluteOffset = new Point(10, 0) }, "")
            {
                CanBeFocused = false
            };
            GUITextBlock.AutoScaleAndNormalize(repairHullsLabel, repairItemsLabel, replaceShuttlesLabel);
            GUITextBlock.AutoScaleAndNormalize(repairHullsButton.GetChild<GUITickBox>().TextBlock, repairItemsButton.GetChild<GUITickBox>().TextBlock, replaceShuttlesButton.GetChild<GUITickBox>().TextBlock);

            // upgrade tab -------------------------------------------------------------------------

            tabs[(int)CampaignMode.InteractionType.Upgrade] = new GUIFrame(new RectTransform(Vector2.One, container.RectTransform), color: Color.Black * 0.9f);
            UpgradeStore = new UpgradeStore(this, GetTabContainer(CampaignMode.InteractionType.Upgrade));

            // Submarine buying tab
            tabs[(int)CampaignMode.InteractionType.PurchaseSub] = new GUIFrame(new RectTransform(Vector2.One, container.RectTransform, Anchor.TopLeft), color: Color.Black * 0.9f);

            tabs[(int)CampaignMode.InteractionType.MedicalClinic] = new GUIFrame(new RectTransform(Vector2.One, container.RectTransform), color: Color.Black * 0.9f);
            MedicalClinic = new MedicalClinicUI(Campaign.MedicalClinic, GetTabContainer(CampaignMode.InteractionType.MedicalClinic));

            // mission info -------------------------------------------------------------------------

            locationInfoPanel = new GUIFrame(new RectTransform(new Vector2(0.35f, 0.75f), GetTabContainer(CampaignMode.InteractionType.Map).RectTransform, Anchor.CenterRight)
            { RelativeOffset = new Vector2(0.02f, 0.0f) }, 
                color: Color.Black)
            {
                Visible = false
            };

            // -------------------------------------------------------------------------

            SelectTab(CampaignMode.InteractionType.Map);

            prevResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
        }

        private GUIFrame CreateDefaultTabContainer(GUIComponent container, Vector2 frameSize, bool visible = true)
        {
            var innerFrame = new GUIFrame(new RectTransform(frameSize, container.RectTransform, Anchor.Center))
            {
                Visible = visible
            };
            new GUIFrame(new RectTransform(innerFrame.Rect.Size - GUIStyle.ItemFrameMargin, innerFrame.RectTransform, Anchor.Center), style: null)
            {
                UserData = "container"
            };
            return innerFrame;
        }

        public GUIComponent GetTabContainer(CampaignMode.InteractionType tab)
        {
            var tabFrame = tabs[(int)tab];
            return tabFrame?.GetChildByUserData("container") ?? tabFrame;
        }

        private void DrawMap(SpriteBatch spriteBatch, GUICustomComponent mapContainer)
        {
            if (GameMain.GraphicsWidth != prevResolution.X || GameMain.GraphicsHeight != prevResolution.Y)
            {
                CreateUI(tabs[(int)CampaignMode.InteractionType.Map].Parent);
            }

            GameMain.GameSession?.Map?.Draw(spriteBatch, mapContainer);
        }

        private void UpdateMap(float deltaTime, GUICustomComponent mapContainer)
        {
            var map = GameMain.GameSession?.Map;
            if (map == null) { return; }
            if (selectedLocation != null && selectedLocation == GameMain.GameSession.Campaign.GetCurrentDisplayLocation())
            {
                map.SelectLocation(-1);
            }
            map.Update(deltaTime, mapContainer);
            foreach (GUITickBox tickBox in missionTickBoxes)
            {
                bool disable = hasMaxMissions && !tickBox.Selected;
                tickBox.Enabled = Campaign.AllowedToManageCampaign() && !disable;
                tickBox.Box.DisabledColor = disable ? tickBox.Box.Color * 0.5f : tickBox.Box.Color * 0.8f;
                foreach (GUIComponent child in tickBox.Parent.Parent.Children)
                {
                    if (child is GUITextBlock textBlock)
                    {
                        textBlock.SelectedTextColor = textBlock.HoverTextColor = textBlock.TextColor = 
                            disable ? new Color(textBlock.TextColor, 0.5f) : new Color(textBlock.TextColor, 1.0f);
                    }
                }
            }
        }

        public void Update(float deltaTime)
        {
            switch (SelectedTab)
            {
                case CampaignMode.InteractionType.PurchaseSub:
                    submarineSelection?.Update();
                    break;

                case CampaignMode.InteractionType.Crew:
                    CrewManagement?.Update();
                    break;

                case CampaignMode.InteractionType.Store:
                    Store?.Update(deltaTime);
                    break;
                
                case CampaignMode.InteractionType.MedicalClinic:
                    MedicalClinic?.Update(deltaTime);
                    break;
            }
        }

        public void RefreshLocationInfo()
        {
            if (selectedLocation != null && Campaign?.Map?.SelectedConnection != null)
            {
                SelectLocation(selectedLocation, Campaign.Map.SelectedConnection);
            }
        }

        public void SelectLocation(Location location, LocationConnection connection)
        {
            missionTickBoxes.Clear();
            missionRewardTexts.Clear();
            locationInfoPanel.ClearChildren();
            //don't select the map panel if we're looking at some other tab
            if (selectedTab == CampaignMode.InteractionType.Map)
            {
                SelectTab(CampaignMode.InteractionType.Map);
                locationInfoPanel.Visible = location != null;
            }

            Location prevSelectedLocation = selectedLocation;
            float prevMissionListScroll = missionList?.BarScroll ?? 0.0f;

            selectedLocation = location;
            if (location == null) { return; }

            int padding = GUI.IntScale(20);

            var content = new GUILayoutGroup(new RectTransform(locationInfoPanel.Rect.Size - new Point(padding * 2), locationInfoPanel.RectTransform, Anchor.Center), childAnchor: Anchor.TopRight)
            {
                Stretch = true,
                RelativeSpacing = 0.02f,
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), location.Name, font: GUI.LargeFont)
            {
                AutoScaleHorizontal = true
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), location.Type.Name, font: GUI.SubHeadingFont);

            Sprite portrait = location.Type.GetPortrait(location.PortraitId);
            portrait.EnsureLazyLoaded();

            var portraitContainer = new GUICustomComponent(new RectTransform(new Vector2(1.0f, 0.3f), content.RectTransform), onDraw: (sb, customComponent) =>
            {
                portrait.Draw(sb, customComponent.Rect.Center.ToVector2(), Color.Gray, portrait.size / 2, scale: Math.Max(customComponent.Rect.Width / portrait.size.X, customComponent.Rect.Height / portrait.size.Y));
            })
            {
                HideElementsOutsideFrame = true
            };

            var textContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), portraitContainer.RectTransform, Anchor.Center))
            {
                RelativeSpacing = 0.05f
            };

            if (connection?.LevelData != null)
            {
                var biomeLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), textContent.RectTransform),
                    TextManager.Get("Biome", fallBackTag: "location"), font: GUI.SubHeadingFont, textAlignment: Alignment.CenterLeft);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), biomeLabel.RectTransform), connection.Biome.DisplayName, textAlignment: Alignment.CenterRight);

                var difficultyLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), textContent.RectTransform),
                    TextManager.Get("LevelDifficulty"), font: GUI.SubHeadingFont, textAlignment: Alignment.CenterLeft);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), difficultyLabel.RectTransform), ((int)connection.LevelData.Difficulty) + " %", textAlignment: Alignment.CenterRight);
            
                if (connection.LevelData.HasBeaconStation)
                {
                    var beaconStationContent = new GUILayoutGroup(new RectTransform(biomeLabel.RectTransform.NonScaledSize, textContent.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);
                    string style = connection.LevelData.IsBeaconActive ? "BeaconStationActive" : "BeaconStationInactive";
                    var icon = new GUIImage(new RectTransform(new Point((int)(beaconStationContent.Rect.Height * 1.2f)), beaconStationContent.RectTransform),
                        style, scaleToFit: true)
                    {
                        Color = MapGenerationParams.Instance.IndicatorColor,
                        HoverColor = Color.Lerp(MapGenerationParams.Instance.IndicatorColor, Color.White, 0.5f),
                        ToolTip = TextManager.Get(connection.LevelData.IsBeaconActive ? "BeaconStationActiveTooltip" : "BeaconStationInactiveTooltip")
                    };
                    new GUITextBlock(new RectTransform(Vector2.One, beaconStationContent.RectTransform),
                        TextManager.Get("submarinetype.beaconstation", fallBackTag: "beaconstationsonarlabel"), font: GUI.SubHeadingFont, textAlignment: Alignment.CenterLeft)
                    {
                        Padding = Vector4.Zero,
                        ToolTip = icon.ToolTip
                    };
                }
                if (connection.LevelData.HasHuntingGrounds)
                {
                    var huntingGroundsContent = new GUILayoutGroup(new RectTransform(biomeLabel.RectTransform.NonScaledSize, textContent.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);
                    var icon = new GUIImage(new RectTransform(new Point((int)(huntingGroundsContent.Rect.Height * 1.5f)), huntingGroundsContent.RectTransform),
                        "HuntingGrounds", scaleToFit: true)
                    {
                        Color = MapGenerationParams.Instance.IndicatorColor,
                        HoverColor = Color.Lerp(MapGenerationParams.Instance.IndicatorColor, Color.White, 0.5f),
                        ToolTip = TextManager.Get("HuntingGroundsTooltip")
                    };
                    new GUITextBlock(new RectTransform(Vector2.One, huntingGroundsContent.RectTransform),
                        TextManager.Get("missionname.huntinggrounds"), font: GUI.SubHeadingFont, textAlignment: Alignment.CenterLeft)
                    {
                        Padding = Vector4.Zero,
                        ToolTip = icon.ToolTip
                    };
                }
            }

            missionList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.4f), content.RectTransform))
            {
                Spacing = (int)(5 * GUI.yScale)
            };
            missionList.OnSelected = (GUIComponent selected, object userdata) =>
            {
                var tickBox = selected.FindChild(c => c is GUITickBox, recursive: true) as GUITickBox;
                if (GUI.MouseOn == tickBox) { return false; }
                if (tickBox != null)
                {
                    if (Campaign.AllowedToManageCampaign() && tickBox.Enabled)
                    {
                        tickBox.Selected = !tickBox.Selected;
                    }
                }
                return true;
            };

            SelectedLevel = connection?.LevelData;
            Location currentDisplayLocation = Campaign.GetCurrentDisplayLocation();
            if (connection != null && connection.Locations.Contains(currentDisplayLocation))
            {
                List<Mission> availableMissions = currentDisplayLocation.GetMissionsInConnection(connection).ToList();
                if (!availableMissions.Contains(null)) { availableMissions.Insert(0, null); }

                missionList.Content.ClearChildren();

                foreach (Mission mission in availableMissions)
                {
                    var missionPanel = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), missionList.Content.RectTransform), style: null)
                    {
                        UserData = mission
                    };
                    var missionTextContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), missionPanel.RectTransform, Anchor.Center))
                    {
                        Stretch = true,
                        CanBeFocused = true,
                        AbsoluteSpacing = GUI.IntScale(5)
                    };

                    var missionName = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextContent.RectTransform), mission?.Name ?? TextManager.Get("NoMission"), font: GUI.SubHeadingFont, wrap: true);
                   // missionName.RectTransform.MinSize = new Point(0, (int)(missionName.Rect.Height * 1.5f));
                    if (mission != null)
                    {                    
                        var tickBox = new GUITickBox(new RectTransform(Vector2.One * 0.9f, missionName.RectTransform, anchor: Anchor.CenterLeft, scaleBasis: ScaleBasis.Smallest) { AbsoluteOffset = new Point((int)missionName.Padding.X, 0) }, label: string.Empty)
                        {
                            UserData = mission,
                            Selected = Campaign.Map.CurrentLocation?.SelectedMissions.Contains(mission) ?? false
                        };
                        tickBox.RectTransform.MinSize = new Point(tickBox.Rect.Height, 0);
                        tickBox.RectTransform.IsFixedSize = true;
                        tickBox.Enabled = Campaign.AllowedToManageCampaign();
                        tickBox.OnSelected += (GUITickBox tb) =>
                        {
                            if (!Campaign.AllowedToManageCampaign()) { return false; }
                            
                            if (tb.Selected)
                            {
                                Campaign.Map.CurrentLocation.SelectMission(mission);
                            }
                            else
                            {
                                Campaign.Map.CurrentLocation.DeselectMission(mission);
                            }

                            foreach (GUITextBlock rewardText in missionRewardTexts)
                            {
                                Mission otherMission = rewardText.UserData as Mission;
                                rewardText.SetRichText(otherMission.GetMissionRewardText(Submarine.MainSub));
                            }

                            UpdateMaxMissions(connection.OtherLocation(currentDisplayLocation));

                            if ((Campaign is MultiPlayerCampaign multiPlayerCampaign) && !multiPlayerCampaign.SuppressStateSending &&
                                Campaign.AllowedToManageCampaign())
                            {
                                GameMain.Client?.SendCampaignState();
                            }
                            return true;                            
                        };
                        missionTickBoxes.Add(tickBox);

                        GUILayoutGroup difficultyIndicatorGroup = null;
                        if (mission.Difficulty.HasValue)
                        {
                            difficultyIndicatorGroup = new GUILayoutGroup(new RectTransform(Vector2.One * 0.9f, missionName.RectTransform, anchor: Anchor.CenterRight, scaleBasis: ScaleBasis.Smallest) { AbsoluteOffset = new Point((int)missionName.Padding.Z, 0) },
                                isHorizontal: true, childAnchor: Anchor.CenterRight)
                            {
                                AbsoluteSpacing = 1,
                                UserData = "difficulty"
                            };
                            var difficultyColor = mission.GetDifficultyColor();
                            for (int i = 0; i < mission.Difficulty; i++)
                            {
                                new GUIImage(new RectTransform(Vector2.One, difficultyIndicatorGroup.RectTransform, scaleBasis: ScaleBasis.Smallest) { IsFixedSize = true }, "DifficultyIndicator", scaleToFit: true)
                                {
                                    Color = difficultyColor,
                                    SelectedColor = difficultyColor,
                                    HoverColor = difficultyColor
                                };
                            }
                        }

                        float extraPadding = 0;// 0.8f * tickBox.Rect.Width;
                        float extraZPadding = difficultyIndicatorGroup != null ? mission.Difficulty.Value * (difficultyIndicatorGroup.Children.First().Rect.Width + difficultyIndicatorGroup.AbsoluteSpacing) : 0;
                        missionName.Padding = new Vector4(missionName.Padding.X + tickBox.Rect.Width * 1.2f + extraPadding,
                            missionName.Padding.Y,
                            missionName.Padding.Z + extraZPadding + extraPadding,
                            missionName.Padding.W);
                        missionName.CalculateHeightFromText();

                        //spacing
                        new GUIFrame(new RectTransform(new Vector2(1.0f, 0.0f), missionTextContent.RectTransform) { MinSize = new Point(0, GUI.IntScale(10)) }, style: null);

                        var rewardText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextContent.RectTransform), mission.GetMissionRewardText(Submarine.MainSub), wrap: true, parseRichText: true)
                        {
                            UserData = mission
                        };
                        missionRewardTexts.Add(rewardText);

                        string reputationText = mission.GetReputationRewardText(mission.Locations[0]);
                        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextContent.RectTransform), reputationText, wrap: true, parseRichText: true);

                        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextContent.RectTransform), mission.Description, wrap: true, parseRichText: true);
                    }
                    missionPanel.RectTransform.MinSize = new Point(0, (int)(missionTextContent.Children.Sum(c => c.Rect.Height + missionTextContent.AbsoluteSpacing) / missionTextContent.RectTransform.RelativeSize.Y) + GUI.IntScale(0));
                    foreach (GUIComponent child in missionTextContent.Children)
                    {
                        if (child is GUITextBlock textBlock)
                        {
                            textBlock.Color = textBlock.SelectedColor = textBlock.HoverColor = Color.Transparent;
                            textBlock.SelectedTextColor = textBlock.HoverTextColor = textBlock.TextColor;
                        }
                    }
                    missionPanel.OnAddedToGUIUpdateList = (c) =>
                    {
                        missionTextContent.Children.ForEach(child => child.State = c.State);
                        if (missionTextContent.FindChild("difficulty", recursive: true) is GUILayoutGroup group)
                        {
                            group.State = c.State;
                        }
                    };

                    if (mission != availableMissions.Last())
                    {
                        new GUIFrame(new RectTransform(new Vector2(1.0f, 0.01f), missionList.Content.RectTransform), style: "HorizontalLine")
                        {
                            CanBeFocused = false
                        };
                    }
                }
                if (prevSelectedLocation == selectedLocation)
                {
                    missionList.BarScroll = prevMissionListScroll;
                    missionList.UpdateDimensions();
                    missionList.UpdateScrollBarSize();
                }
            }
            var destination = connection.OtherLocation(currentDisplayLocation);
            UpdateMaxMissions(destination);

            var buttonArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.05f), content.RectTransform), isHorizontal: true);

            new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f), buttonArea.RectTransform), "", font: GUI.Style.SubHeadingFont)
            {
                TextGetter = () =>
                {
                    return TextManager.AddPunctuation(':', TextManager.Get("Missions"), $"{Campaign.NumberOfMissionsAtLocation(destination)}/{Campaign.Settings.TotalMaxMissionCount}");
                }
            };

            StartButton = new GUIButton(new RectTransform(new Vector2(0.4f, 1.0f), buttonArea.RectTransform),
                TextManager.Get("StartCampaignButton"), style: "GUIButtonLarge")
            {
                OnClicked = (GUIButton btn, object obj) =>
                {
                    if (missionList.Content.FindChild(c => c is GUITickBox tickBox && tickBox.Selected, recursive: true) == null &&
                        missionList.Content.Children.Any(c => c.UserData is Mission))
                    {
                        var noMissionVerification = new GUIMessageBox(string.Empty, TextManager.Get("nomissionprompt"), new string[] { TextManager.Get("yes"), TextManager.Get("no") });
                        noMissionVerification.Buttons[0].OnClicked = (btn, userdata) =>
                        {
                            StartRound?.Invoke();
                            noMissionVerification.Close();
                            return true;
                        };
                        noMissionVerification.Buttons[1].OnClicked = noMissionVerification.Close;
                    }
                    else
                    {
                        StartRound?.Invoke();
                    }
                    return true;
                },
                Enabled = true,
                Visible = Campaign.AllowedToEndRound()
            };

            buttonArea.RectTransform.MinSize = new Point(0, StartButton.RectTransform.MinSize.Y);

            if (Level.Loaded != null &&
                connection?.LevelData == Level.Loaded.LevelData &&
                currentDisplayLocation == Campaign.Map?.CurrentLocation)
            {
                StartButton.Visible = false;
                missionList.Enabled = false;
            }
            //locationInfoPanel?.UpdateAuto(1.0f);
        }

        public void SelectTab(CampaignMode.InteractionType tab)
        {
            if (Campaign.ShowCampaignUI || (Campaign.ForceMapUI && tab == CampaignMode.InteractionType.Map))
            {
                HintManager.OnShowCampaignInterface(tab);
            }

            selectedTab = tab;
            for (int i = 0; i < tabs.Length; i++)
            {
                if (tabs[i] != null)
                {
                    tabs[i].Visible = (int)selectedTab == i;
                }
            }
            
            locationInfoPanel.Visible = tab == CampaignMode.InteractionType.Map && selectedLocation != null;

            switch (selectedTab)
            {
                case CampaignMode.InteractionType.Repair:
                    repairHullsButton.Enabled =
                        (Campaign.PurchasedHullRepairs || Campaign.Money >= CampaignMode.HullRepairCost) &&
                        Campaign.AllowedToManageCampaign();
                    repairHullsButton.GetChild<GUITickBox>().Selected = Campaign.PurchasedHullRepairs;
                    repairItemsButton.Enabled =
                        (Campaign.PurchasedItemRepairs || Campaign.Money >= CampaignMode.ItemRepairCost) &&
                        Campaign.AllowedToManageCampaign();
                    repairItemsButton.GetChild<GUITickBox>().Selected = Campaign.PurchasedItemRepairs;

                    if (GameMain.GameSession?.SubmarineInfo == null || !GameMain.GameSession.SubmarineInfo.SubsLeftBehind)
                    {
                        replaceShuttlesButton.Enabled = false;
                        replaceShuttlesButton.GetChild<GUITickBox>().Selected = false;
                    }
                    else
                    {
                        replaceShuttlesButton.Enabled =
                            (Campaign.PurchasedLostShuttles || Campaign.Money >= CampaignMode.ShuttleReplaceCost) &&
                            Campaign.AllowedToManageCampaign();
                        replaceShuttlesButton.GetChild<GUITickBox>().Selected = Campaign.PurchasedLostShuttles;
                    }
                    break;
                case CampaignMode.InteractionType.Store:
                    Store.RefreshItemsToSell();
                    Store.Refresh();
                    break;
                case CampaignMode.InteractionType.Crew:
                    CrewManagement.UpdateCrew();
                    break;
                case CampaignMode.InteractionType.PurchaseSub:
                    if (submarineSelection == null) submarineSelection = new SubmarineSelection(false, () => Campaign.ShowCampaignUI = false, tabs[(int)CampaignMode.InteractionType.PurchaseSub].RectTransform);
                    submarineSelection.RefreshSubmarineDisplay(true);
                    break;
            }
        }

        public static string GetMoney()
        {
            return TextManager.GetWithVariable("PlayerCredits", "[credits]", (GameMain.GameSession?.Campaign == null) ? "0" : string.Format(CultureInfo.InvariantCulture, "{0:N0}", GameMain.GameSession.Campaign.Money));
        }

        private void UpdateMaxMissions(Location location)
        {
            hasMaxMissions = Campaign.NumberOfMissionsAtLocation(location) >= Campaign.Settings.TotalMaxMissionCount;
        }
    }
}
