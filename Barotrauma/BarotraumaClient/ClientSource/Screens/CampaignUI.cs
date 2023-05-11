﻿using Barotrauma.Extensions;
using Barotrauma.Networking;
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

            campaign.Map.OnLocationSelected = SelectLocation;
            campaign.Map.OnMissionsSelected = (connection, missions) => 
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
            var mapContainer = new GUICustomComponent(new RectTransform(Vector2.One, mapFrame.RectTransform), DrawMap, UpdateMap);
            var notificationFrame = new GUIFrame(new RectTransform(new Point(mapContainer.Rect.Width, GUI.IntScale(40)), mapContainer.RectTransform, Anchor.BottomCenter), style: "ChatBox");
            
            new GUIFrame(new RectTransform(Vector2.One, mapFrame.RectTransform), style: "InnerGlow", color: Color.Black * 0.9f)
            {
                CanBeFocused = false
            };            
            
            var notificationContainer = new GUICustomComponent(new RectTransform(new Vector2(0.98f, 1.0f), notificationFrame.RectTransform, Anchor.Center), DrawMapNotifications, null)
            {
                HideElementsOutsideFrame = true
            };
            var notificationHeader = new GUIImage(new RectTransform(new Vector2(0.1f, 1.0f), notificationFrame.RectTransform, Anchor.CenterLeft), style: "GUISlopedHeaderRight");
            var text = new GUITextBlock(new RectTransform(Vector2.One, notificationHeader.RectTransform, Anchor.Center), TextManager.Get("breakingnews"), font: GUIStyle.LargeFont);
            notificationHeader.RectTransform.MinSize = new Point((int)(text.TextSize.X * 1.3f), 0);

            // crew tab -------------------------------------------------------------------------

            var crewTab = new GUIFrame(new RectTransform(Vector2.One, container.RectTransform), color: Color.Black * 0.9f);
            tabs[(int)CampaignMode.InteractionType.Crew] = crewTab;
            CrewManagement = new CrewManagement(this, crewTab);

            // store tab -------------------------------------------------------------------------
            
            var storeTab = new GUIFrame(new RectTransform(Vector2.One, container.RectTransform), color: Color.Black * 0.9f);
            tabs[(int)CampaignMode.InteractionType.Store] = storeTab;
            Store = new Store(this, storeTab);

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

            Campaign?.Map?.Draw(Campaign, spriteBatch, mapContainer);
        }

        private void DrawMapNotifications(SpriteBatch spriteBatch, GUICustomComponent notificationContainer)
        {
            Campaign?.Map?.DrawNotifications(spriteBatch, notificationContainer);
        }

        private void UpdateMap(float deltaTime, GUICustomComponent mapContainer)
        {
            var map = Campaign?.Map;
            if (map == null) { return; }
            if (selectedLocation != null && selectedLocation == Campaign.GetCurrentDisplayLocation())
            {
                map.SelectLocation(-1);
            }
            map.Update(Campaign, deltaTime, mapContainer);
            foreach (GUITickBox tickBox in missionTickBoxes)
            {
                bool disable = hasMaxMissions && !tickBox.Selected;
                tickBox.Enabled = CampaignMode.AllowedToManageCampaign(ClientPermissions.ManageMap) && !disable;
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
                case CampaignMode.InteractionType.Map:
                    if (StartButton != null)
                    {
                        StartButton.Enabled = CampaignMode.AllowedToManageCampaign(ClientPermissions.ManageMap) && Character.Controlled is { IsIncapacitated: false };
                    }
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

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), location.Name, font: GUIStyle.LargeFont)
            {
                AutoScaleHorizontal = true
            };
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform), location.Type.Name, font: GUIStyle.SubHeadingFont);

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
                if (location.Faction?.Prefab != null)
                {
                    var factionLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), textContent.RectTransform),
                        TextManager.Get("Faction"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft);
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), factionLabel.RectTransform), location.Faction.Prefab.Name, textAlignment: Alignment.CenterRight, textColor: location.Faction.Prefab.IconColor);
                }
                var biomeLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), textContent.RectTransform),
                    TextManager.Get("Biome", "location"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), biomeLabel.RectTransform), connection.Biome.DisplayName, textAlignment: Alignment.CenterRight);

                var difficultyLabel = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), textContent.RectTransform),
                    TextManager.Get("LevelDifficulty"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), difficultyLabel.RectTransform), TextManager.GetWithVariable("percentageformat", "[value]", ((int)connection.LevelData.Difficulty).ToString()), textAlignment: Alignment.CenterRight);

                if (connection.LevelData.HasBeaconStation)
                {
                    var beaconStationContent = new GUILayoutGroup(new RectTransform(biomeLabel.RectTransform.NonScaledSize, textContent.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);
                    string style = connection.LevelData.IsBeaconActive ? "BeaconStationActive" : "BeaconStationInactive";
                    var icon = new GUIImage(new RectTransform(new Point((int)(beaconStationContent.Rect.Height * 1.2f)), beaconStationContent.RectTransform),
                        style, scaleToFit: true)
                    {
                        Color = MapGenerationParams.Instance.IndicatorColor,
                        HoverColor = Color.Lerp(MapGenerationParams.Instance.IndicatorColor, Color.White, 0.5f),
                        ToolTip = RichString.Rich(TextManager.Get(connection.LevelData.IsBeaconActive ? "BeaconStationActiveTooltip" : "BeaconStationInactiveTooltip"))
                    };
                    new GUITextBlock(new RectTransform(Vector2.One, beaconStationContent.RectTransform),
                        TextManager.Get("submarinetype.beaconstation", "beaconstationsonarlabel"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft)
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
                        ToolTip = RichString.Rich(TextManager.Get("HuntingGroundsTooltip"))
                    };
                    new GUITextBlock(new RectTransform(Vector2.One, huntingGroundsContent.RectTransform),
                        TextManager.Get("missionname.huntinggrounds"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft)
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
                    if (CampaignMode.AllowedToManageCampaign(ClientPermissions.ManageMap) && tickBox.Enabled)
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

                if (!availableMissions.Any()) { availableMissions.Insert(0, null); }

                availableMissions.AddRange(location.AvailableMissions.Where(m => m.Locations[0] == m.Locations[1]));

                missionList.Content.ClearChildren();

                bool isPrevMissionInNextLocation = false;
                foreach (Mission mission in availableMissions)
                {
                    bool isMissionInNextLocation = mission != null && location.AvailableMissions.Contains(mission);
                    if (isMissionInNextLocation && !isPrevMissionInNextLocation)
                    {
                        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionList.Content.RectTransform), TextManager.Get("outpostmissions"),
                            textAlignment: Alignment.Center, font: GUIStyle.SubHeadingFont, wrap: true)
                        {
                            CanBeFocused = false
                        };
                        new GUIFrame(new RectTransform(new Vector2(1.0f, 0.01f), missionList.Content.RectTransform), style: "HorizontalLine")
                        {
                            CanBeFocused = false
                        };
                    }
                    isPrevMissionInNextLocation = isMissionInNextLocation;

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

                    var missionName = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextContent.RectTransform), mission?.Name ?? TextManager.Get("NoMission"), font: GUIStyle.SubHeadingFont, wrap: true);
                    missionName.RectTransform.MinSize = new Point(0, GUI.IntScale(15));
                    if (mission == null)
                    {
                        missionTextContent.RectTransform.MinSize = missionName.RectTransform.MinSize = new Point(0, GUI.IntScale(35));
                        missionTextContent.ChildAnchor = Anchor.CenterLeft;
                    }
                    else
                    {
                        GUITickBox tickBox = null;
                        if (!isMissionInNextLocation)
                        {
                            tickBox = new GUITickBox(new RectTransform(Vector2.One * 0.9f, missionName.RectTransform, anchor: Anchor.CenterLeft, scaleBasis: ScaleBasis.Smallest) { AbsoluteOffset = new Point((int)missionName.Padding.X, 0) }, label: string.Empty)
                            {
                                UserData = mission,
                                Selected = Campaign.Map.CurrentLocation?.SelectedMissions.Contains(mission) ?? false
                            };
                            tickBox.RectTransform.MinSize = new Point(tickBox.Rect.Height, 0);
                            tickBox.RectTransform.IsFixedSize = true;
                            tickBox.Enabled = CampaignMode.AllowedToManageCampaign(ClientPermissions.ManageMap);
                            tickBox.OnSelected += (GUITickBox tb) =>
                            {
                                if (!CampaignMode.AllowedToManageCampaign(Networking.ClientPermissions.ManageMap)) { return false; }

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
                                    rewardText.Text = otherMission.GetMissionRewardText(Submarine.MainSub);
                                }

                                UpdateMaxMissions(connection.OtherLocation(currentDisplayLocation));

                                if ((Campaign is MultiPlayerCampaign multiPlayerCampaign) && !multiPlayerCampaign.SuppressStateSending &&
                                    CampaignMode.AllowedToManageCampaign(Networking.ClientPermissions.ManageMap))
                                {
                                    GameMain.Client?.SendCampaignState();
                                }
                                return true;
                            };
                            missionTickBoxes.Add(tickBox);
                        }

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
                        missionName.Padding = new Vector4(missionName.Padding.X + (tickBox?.Rect.Width ?? 0) * 1.2f + extraPadding,
                            missionName.Padding.Y,
                            missionName.Padding.Z + extraZPadding + extraPadding,
                            missionName.Padding.W);
                        missionName.CalculateHeightFromText();

                        //spacing
                        new GUIFrame(new RectTransform(new Vector2(1.0f, 0.0f), missionTextContent.RectTransform) { MinSize = new Point(0, GUI.IntScale(10)) }, style: null);
                        
                        var rewardText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextContent.RectTransform), RichString.Rich(mission.GetMissionRewardText(Submarine.MainSub)), wrap: true)
                        {
                            UserData = mission
                        };
                        missionRewardTexts.Add(rewardText);

                        LocalizedString reputationText = mission.GetReputationRewardText();
                        if (!reputationText.IsNullOrEmpty())
                        {
                            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextContent.RectTransform), RichString.Rich(reputationText), wrap: true);
                        }
                        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextContent.RectTransform), RichString.Rich(mission.Description), wrap: true);
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

            new GUITextBlock(new RectTransform(new Vector2(0.6f, 1.0f), buttonArea.RectTransform), "", font: GUIStyle.SubHeadingFont)
            {
                TextGetter = () =>
                {
                    int missionCount = 0;
                    if (GameMain.GameSession != null && Campaign.Map?.CurrentLocation?.SelectedMissions != null)
                    {
                        missionCount = Campaign.Map.CurrentLocation.SelectedMissions.Count(m => m.Locations.Contains(location) && !GameMain.GameSession.Missions.Contains(m));
                    }
                    return TextManager.AddPunctuation(':', TextManager.Get("Missions"), $"{missionCount}/{Campaign.Settings.TotalMaxMissionCount}");
                }
            };

            StartButton = new GUIButton(new RectTransform(new Vector2(0.4f, 1.0f), buttonArea.RectTransform),
                TextManager.Get("StartCampaignButton"), style: "GUIButtonLarge")
            {
                OnClicked = (GUIButton btn, object obj) =>
                {
                    if (missionList.Content.FindChild(c => c is GUITickBox tickBox && tickBox.Selected, recursive: true) == null &&
                        missionList.Content.Children.Any(c => c.UserData is Mission mission && mission.Locations.Contains(Campaign?.Map?.CurrentLocation)))
                    {
                        var noMissionVerification = new GUIMessageBox(string.Empty, TextManager.Get("nomissionprompt"), new LocalizedString[] { TextManager.Get("yes"), TextManager.Get("no") });
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
                Visible = CampaignMode.AllowedToManageCampaign(ClientPermissions.ManageMap)
            };

            buttonArea.RectTransform.MinSize = new Point(0, StartButton.RectTransform.MinSize.Y);

            if (Level.Loaded != null &&
                connection?.LevelData == Level.Loaded.LevelData &&
                currentDisplayLocation == Campaign.Map?.CurrentLocation)
            {
                StartButton.Visible = false;
                missionList.Enabled = false;
            }
        }

        public void SelectTab(CampaignMode.InteractionType tab, Character npc = null)
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
                case CampaignMode.InteractionType.Store:
                    Store.SelectStore(npc);
                    break;
                case CampaignMode.InteractionType.Crew:
                    CrewManagement.UpdateCrew();
                    break;
                case CampaignMode.InteractionType.PurchaseSub:
                    if (submarineSelection == null) submarineSelection = new SubmarineSelection(false, () => Campaign.ShowCampaignUI = false, tabs[(int)CampaignMode.InteractionType.PurchaseSub].RectTransform);
                    submarineSelection.RefreshSubmarineDisplay(true, setTransferOptionToTrue: true);
                    break;
                case CampaignMode.InteractionType.Map:
                    GameMain.GameSession?.Map?.ResetPendingSub();
                    //refresh mission rewards (may have been changed by e.g. a pending submarine switch)
                    foreach (GUITextBlock rewardText in missionRewardTexts)
                    {
                        Mission mission = (Mission)rewardText.UserData;
                        rewardText.Text = mission.GetMissionRewardText(Submarine.MainSub);
                    }
                    break;
            }
        }

        public static LocalizedString GetMoney()
        {
            return TextManager.GetWithVariable("PlayerCredits", "[credits]", (GameMain.GameSession?.Campaign == null) ? "0" : string.Format(CultureInfo.InvariantCulture, "{0:N0}", GameMain.GameSession.Campaign.GetBalance()));
        }

        public static LocalizedString GetTotalBalance()
        {
            return TextManager.FormatCurrency(GameMain.GameSession?.Campaign is { } campaign ? campaign.GetBalance() : 0);
        }

        public static LocalizedString GetBankBalance()
        {
            return TextManager.FormatCurrency(GameMain.GameSession?.Campaign is { } campaign ? campaign.Bank.Balance : 0);
        }

        public static LocalizedString GetWalletBalance()
        {
            return TextManager.FormatCurrency(GameMain.GameSession?.Campaign is { } campaign ? campaign.Wallet.Balance : 0);
        }

        private void UpdateMaxMissions(Location location)
        {
            hasMaxMissions = Campaign.NumberOfMissionsAtLocation(location) >= Campaign.Settings.TotalMaxMissionCount;
        }

        public readonly struct PlayerBalanceElement
        {
            public readonly bool DisplaySeparateBalances;
            public readonly GUILayoutGroup ParentComponent;
            public readonly GUILayoutGroup TotalBalanceContainer;
            public readonly GUILayoutGroup BankBalanceContainer;

            public PlayerBalanceElement(bool displaySeparateBalances, GUILayoutGroup parentComponent, GUILayoutGroup totalBalanceContainer, GUILayoutGroup bankBalanceContainer)
            {
                DisplaySeparateBalances = displaySeparateBalances;
                ParentComponent = parentComponent;
                TotalBalanceContainer = totalBalanceContainer;
                BankBalanceContainer = bankBalanceContainer;
            }

            public PlayerBalanceElement(PlayerBalanceElement element, bool displaySeparateBalances)
            {
                DisplaySeparateBalances = displaySeparateBalances;
                ParentComponent = element.ParentComponent;
                TotalBalanceContainer = element.TotalBalanceContainer;
                BankBalanceContainer = element.BankBalanceContainer;
            }
        }

        public static PlayerBalanceElement? AddBalanceElement(GUIComponent elementParent, Vector2 relativeSize)
        {
            var parent = new GUILayoutGroup(new RectTransform(relativeSize, elementParent.RectTransform), isHorizontal: true, childAnchor: Anchor.TopRight);
            if (GameMain.IsSingleplayer)
            {
                AddBalance(parent, true, TextManager.Get("campaignstore.balance"), GetTotalBalance);
                return null;
            }
            else
            {
                bool displaySeparateBalances = CampaignMode.AllowedToManageWallets();
                var totalBalanceContainer = AddBalance(parent, displaySeparateBalances, TextManager.Get("campaignstore.total"), GetTotalBalance);
                var bankBalanceContainer = AddBalance(parent, displaySeparateBalances, TextManager.Get("crewwallet.bank"), GetBankBalance);
                AddBalance(parent, true, TextManager.Get("crewwallet.wallet"), GetWalletBalance);
                var playerBalanceElement = new PlayerBalanceElement(displaySeparateBalances, parent, totalBalanceContainer, bankBalanceContainer);
                parent.Recalculate();
                return playerBalanceElement;
            }

            static GUILayoutGroup AddBalance(GUIComponent parent, bool visible, LocalizedString text, GUITextBlock.TextGetterHandler textGetter)
            {
                float balanceContainerWidth = GameMain.IsSingleplayer ? 1 : 1 / 3f;
                var rt = new RectTransform(new Vector2(balanceContainerWidth, 1.0f), parent.RectTransform)
                {
                    MaxSize = new Point(GUI.IntScale(GUI.AdjustForTextScale(120)), int.MaxValue)
                };
                var balanceContainer = new GUILayoutGroup(rt, childAnchor: Anchor.TopRight)
                {
                    RelativeSpacing = 0.005f,
                    Visible = visible
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
        }

        public static PlayerBalanceElement? UpdateBalanceElement(PlayerBalanceElement? playerBalanceElement)
        {
            if (playerBalanceElement is { } balanceElement)
            {
                bool displaySeparateBalances = CampaignMode.AllowedToManageWallets();
                if (displaySeparateBalances != balanceElement.DisplaySeparateBalances)
                {
                    balanceElement.TotalBalanceContainer.Visible = displaySeparateBalances;
                    balanceElement.BankBalanceContainer.Visible = displaySeparateBalances;
                    playerBalanceElement = new PlayerBalanceElement(balanceElement, displaySeparateBalances);
                    balanceElement.ParentComponent.Recalculate();
                }
            }
            return playerBalanceElement;
        }
    }
}
