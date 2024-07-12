using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;

namespace Barotrauma
{
    class RoundSummary
    {
        private float crewListAnimDelay = 0.25f;
        private float missionIconAnimDelay;

        private const float jobColumnWidthPercentage = 0.11f;
        private const float characterColumnWidthPercentage = 0.44f;
        private const float statusColumnWidthPercentage = 0.45f;

        private int jobColumnWidth, characterColumnWidth, statusColumnWidth;

        private readonly List<Mission> selectedMissions;
        private readonly Location startLocation, endLocation;

        private readonly GameMode gameMode;

        private readonly Dictionary<Identifier, float> initialFactionReputations = new Dictionary<Identifier, float>();

        public GUILayoutGroup ButtonArea { get; private set; }

        public GUIButton ContinueButton { get; private set; }

        public GUIComponent Frame { get; private set; }

        public RoundSummary(GameMode gameMode, IEnumerable<Mission> selectedMissions, Location startLocation, Location endLocation)
        {
            this.gameMode = gameMode;
            this.selectedMissions = selectedMissions.ToList();
            this.startLocation = startLocation;
            this.endLocation = endLocation;
            if (gameMode is CampaignMode campaignMode)
            {
                foreach (Faction faction in campaignMode.Factions)
                {
                    initialFactionReputations.Add(faction.Prefab.Identifier, faction.Reputation.Value);
                }
            }
        }

        public GUIFrame CreateSummaryFrame(GameSession gameSession, string endMessage, CampaignMode.TransitionType transitionType = CampaignMode.TransitionType.None, TraitorManager.TraitorResults? traitorResults = null)
        {
            bool singleplayer = GameMain.NetworkMember == null;
            bool gameOver =
                gameSession.GameMode.IsSinglePlayer ?
                gameSession.CrewManager.GetCharacters().All(c => c.IsDead || c.IsIncapacitated) :
                gameSession.CrewManager.GetCharacters().All(c => c.IsDead || c.IsIncapacitated || c.IsBot);

            if (!singleplayer)
            {
                SoundPlayer.OverrideMusicType = (gameOver ? "crewdead" : "endround").ToIdentifier();
                SoundPlayer.OverrideMusicDuration = 18.0f;
            }

            GUIFrame background = new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, GUI.Canvas, Anchor.Center), style: "GUIBackgroundBlocker")
            {
                UserData = this
            };

            List<GUIComponent> rightPanels = new List<GUIComponent>();

            int minWidth = 400, minHeight = 350;
            int padding = GUI.IntScale(25.0f);

            //crew panel -------------------------------------------------------------------------------

            GUIFrame crewFrame = new GUIFrame(new RectTransform(new Vector2(0.35f, 0.4f), background.RectTransform, Anchor.TopCenter, minSize: new Point(minWidth, minHeight)));
            GUIFrame crewFrameInner = new GUIFrame(new RectTransform(new Point(crewFrame.Rect.Width - padding * 2, crewFrame.Rect.Height - padding * 2), crewFrame.RectTransform, Anchor.Center), style: "InnerFrame");

            var crewContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), crewFrameInner.RectTransform, Anchor.Center))
            {
                Stretch = true
            };

            var crewHeader = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), crewContent.RectTransform),
                TextManager.Get("crew"), textAlignment: Alignment.TopLeft, font: GUIStyle.SubHeadingFont);
            crewHeader.RectTransform.MinSize = new Point(0, GUI.IntScale(crewHeader.Rect.Height * 2.0f));

            var crewList = CreateCrewList(crewContent, gameSession.CrewManager.GetCharacterInfos().Where(c => c.TeamID != CharacterTeamType.Team2), traitorResults);
            if (traitorResults != null && traitorResults.Value.VotedAsTraitorClientSessionId > 0)
            {
                var traitorInfoPanel = CreateTraitorInfoPanel(crewList.Content, traitorResults.Value, crewListAnimDelay);
                traitorInfoPanel.RectTransform.SetAsFirstChild();
                var spacing = new GUIFrame(new RectTransform(new Point(0, GUI.IntScale(20)), crewList.Content.RectTransform), style: null);
                spacing.RectTransform.RepositionChildInHierarchy(1);
            }

            //another crew frame for the 2nd team in combat missions
            if (gameSession.Missions.Any(m => m is CombatMission))
            {
                crewHeader.Text = CombatMission.GetTeamName(CharacterTeamType.Team1);
                GUIFrame crewFrame2 = new GUIFrame(new RectTransform(crewFrame.RectTransform.RelativeSize, background.RectTransform, Anchor.TopCenter, minSize: new Point(minWidth, minHeight)));
                rightPanels.Add(crewFrame2);
                GUIFrame crewFrameInner2 = new GUIFrame(new RectTransform(new Point(crewFrame2.Rect.Width - padding * 2, crewFrame2.Rect.Height - padding * 2), crewFrame2.RectTransform, Anchor.Center), style: "InnerFrame");
                var crewContent2 = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), crewFrameInner2.RectTransform, Anchor.Center))
                {
                    Stretch = true
                };
                var crewHeader2 = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), crewContent2.RectTransform),
                    CombatMission.GetTeamName(CharacterTeamType.Team2), textAlignment: Alignment.TopLeft, font: GUIStyle.SubHeadingFont);
                crewHeader2.RectTransform.MinSize = new Point(0, GUI.IntScale(crewHeader2.Rect.Height * 2.0f));
                CreateCrewList(crewContent2, gameSession.CrewManager.GetCharacterInfos().Where(c => c.TeamID == CharacterTeamType.Team2), traitorResults);
            }

            //header -------------------------------------------------------------------------------

            LocalizedString headerText = GetHeaderText(gameOver, transitionType);
            GUITextBlock headerTextBlock = null;
            if (!headerText.IsNullOrEmpty())
            {
                headerTextBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), crewFrame.RectTransform, Anchor.TopLeft, Pivot.BottomLeft), 
                    headerText, textAlignment: Alignment.BottomLeft, font: GUIStyle.LargeFont, wrap: true);
            }

            //reputation panel -------------------------------------------------------------------------------

            var campaignMode = gameMode as CampaignMode;
            if (campaignMode != null)
            {
                GUIFrame reputationframe = new GUIFrame(new RectTransform(crewFrame.RectTransform.RelativeSize, background.RectTransform, Anchor.TopCenter, minSize: crewFrame.RectTransform.MinSize));
                rightPanels.Add(reputationframe);
                GUIFrame reputationframeInner = new GUIFrame(new RectTransform(new Point(reputationframe.Rect.Width - padding * 2, reputationframe.Rect.Height - padding * 2), reputationframe.RectTransform, Anchor.Center), style: "InnerFrame");

                var reputationContent = new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 0.95f), reputationframeInner.RectTransform, Anchor.Center))
                {
                    Stretch = true
                };

                var reputationHeader = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), reputationContent.RectTransform),
                    TextManager.Get("reputation"), textAlignment: Alignment.TopLeft, font: GUIStyle.SubHeadingFont);
                reputationHeader.RectTransform.MinSize = new Point(0, GUI.IntScale(reputationHeader.Rect.Height * 2.0f));

                CreateReputationInfoPanel(reputationContent, campaignMode);
            }

            //mission panel -------------------------------------------------------------------------------

            GUIFrame missionframe = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.4f), background.RectTransform, Anchor.TopCenter, minSize: new Point(minWidth, minHeight / 4)));
            GUILayoutGroup missionFrameContent = new GUILayoutGroup(new RectTransform(new Point(missionframe.Rect.Width - padding * 2, missionframe.Rect.Height - padding * 2), missionframe.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.03f
            };
            GUIFrame missionframeInner = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.9f), missionFrameContent.RectTransform, Anchor.Center), style: "InnerFrame");

            var missionContent = new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 0.93f), missionframeInner.RectTransform, Anchor.Center))
            {
                Stretch = true
            };

            List<Mission> missionsToDisplay = new List<Mission>(selectedMissions.Where(m => m.Prefab.ShowInMenus));
            if (startLocation != null)
            {
                foreach (Mission mission in startLocation.SelectedMissions)
                {
                    if (missionsToDisplay.Contains(mission)) { continue; }
                    if (!mission.Prefab.ShowInMenus) { continue; }
                    if (mission.Locations[0] == mission.Locations[1] ||
                        mission.Locations.Contains(campaignMode?.Map.SelectedLocation))
                    {
                        missionsToDisplay.Add(mission);
                    }
                }
            }

            GUIListBox missionList = new GUIListBox(new RectTransform(Vector2.One, missionContent.RectTransform, Anchor.Center))
            {
                Spacing = GUI.IntScale(15)
            };
            missionList.ContentBackground.Color = Color.Transparent;

            ButtonArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), missionFrameContent.RectTransform, Anchor.BottomCenter), isHorizontal: true, childAnchor: Anchor.BottomRight)
            {
                RelativeSpacing = 0.025f
            };

            missionFrameContent.Recalculate();
            missionContent.Recalculate();

            if (!string.IsNullOrWhiteSpace(endMessage))
            {
                var endText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionList.Content.RectTransform),
                    TextManager.GetServerMessage(endMessage), wrap: true)
                {
                    CanBeFocused = false
                };
            }

            float animDelay = missionIconAnimDelay;
            foreach (Mission mission in missionsToDisplay)
            {
                var textContent = new List<LocalizedString>();

                if (selectedMissions.Contains(mission))
                {
                    textContent.Add(mission.Completed ? mission.SuccessMessage : mission.FailureMessage);

                    var repText = mission.GetReputationRewardText();
                    if (!repText.IsNullOrEmpty()) { textContent.Add(repText); }

                    int totalReward = mission.GetFinalReward(Submarine.MainSub);
                    if (totalReward > 0)
                    {
                        textContent.Add(mission.GetMissionRewardText(Submarine.MainSub));
                        if (GameMain.IsMultiplayer && Character.Controlled is { } controlled && mission.Completed)
                        {
                            var (share, percentage, _) = Mission.GetRewardShare(controlled.Wallet.RewardDistribution, GameSession.GetSessionCrewCharacters(CharacterType.Player).Where(c => c != controlled), Option<int>.Some(totalReward));
                            if (share > 0)
                            {
                                string shareFormatted = string.Format(CultureInfo.InvariantCulture, "{0:N0}", share);
                                RichString yourShareString = TextManager.GetWithVariables("crewwallet.missionreward.get", ("[money]", $"{shareFormatted}"), ("[share]", $"{percentage}"));
                                textContent.Add(yourShareString);
                            }
                        }
                    }
                }
                else
                {
                    var repText = mission.GetReputationRewardText();
                    if (!repText.IsNullOrEmpty()) { textContent.Add(repText); }
                    textContent.Add(mission.GetMissionRewardText(Submarine.MainSub));
                    textContent.Add(mission.Description);
                    textContent.AddRange(mission.ShownMessages);
                }

                CreateMissionEntry(
                    missionList.Content,
                    mission.Name,
                    textContent,
                    mission.Difficulty ?? 0,
                    mission.Prefab.Icon, mission.Prefab.IconColor,
                    out GUIImage missionIcon);

                if (selectedMissions.Contains(mission))
                {
                    UpdateMissionStateIcon(mission.Completed, missionIcon, animDelay);
                    animDelay += 0.25f;
                }
            }

            if (!missionsToDisplay.Any())
            {
                var missionContentHorizontal = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.4f), missionList.Content.RectTransform), childAnchor: Anchor.TopLeft, isHorizontal: true)
                {
                    RelativeSpacing = 0.025f,
                    Stretch = true,
                    CanBeFocused = true
                };
                GUIImage missionIcon = new GUIImage(new RectTransform(new Point(missionContentHorizontal.Rect.Height), missionContentHorizontal.RectTransform), style: "NoMissionIcon", scaleToFit: true);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionContentHorizontal.RectTransform),
                    TextManager.Get("nomission"), font: GUIStyle.LargeFont);
            }

            gameSession?.EventManager?.EventLog?.CreateEventLogUI(missionList.Content, traitorResults);

            AddSeparators(missionList.Content);

            ContinueButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), ButtonArea.RectTransform), TextManager.Get("Close"));
            ButtonArea.RectTransform.NonScaledSize = new Point(ButtonArea.Rect.Width, ContinueButton.Rect.Height);
            ButtonArea.RectTransform.IsFixedSize = true;

            missionFrameContent.Recalculate();

            // set layout -------------------------------------------------------------------

            int panelSpacing = GUI.IntScale(20);
            int totalHeight = crewFrame.Rect.Height + panelSpacing + missionframe.Rect.Height;
            int totalWidth = crewFrame.Rect.Width;

            crewFrame.RectTransform.AbsoluteOffset = new Point(0, (GameMain.GraphicsHeight - totalHeight) / 2);
            missionframe.RectTransform.AbsoluteOffset = new Point(0, crewFrame.Rect.Bottom + panelSpacing);

            if (rightPanels.Any())
            {
                totalWidth = crewFrame.Rect.Width * 2 + panelSpacing;
                if (headerTextBlock != null)
                {
                    headerTextBlock.RectTransform.MinSize = new Point(totalWidth, 0);
                }
                crewFrame.RectTransform.AbsoluteOffset = new Point(-(crewFrame.Rect.Width + panelSpacing) / 2, crewFrame.RectTransform.AbsoluteOffset.Y);
                foreach (var rightPanel in rightPanels)
                {
                    rightPanel.RectTransform.AbsoluteOffset = new Point((rightPanel.Rect.Width + panelSpacing) / 2, crewFrame.RectTransform.AbsoluteOffset.Y);
                }
            }

            Frame = background;
            return background;
        }

        public void CreateReputationInfoPanel(GUIComponent parent, CampaignMode campaignMode)
        {
            GUIListBox reputationList = new GUIListBox(new RectTransform(Vector2.One, parent.RectTransform));
            reputationList.ContentBackground.Color = Color.Transparent;

            foreach (Faction faction in campaignMode.Factions.OrderBy(f => f.Prefab.MenuOrder).ThenBy(f => f.Prefab.Name))
            {
                float initialReputation = faction.Reputation.Value;
                if (!initialFactionReputations.TryGetValue(faction.Prefab.Identifier, out initialReputation))
                {
                    DebugConsole.AddWarning($"Could not determine reputation change for faction \"{faction.Prefab.Name}\" (faction was not present at the start of the round).");
                }
                var factionFrame = CreateReputationElement(
                    reputationList.Content,
                    faction.Prefab.Name,
                    faction.Reputation, initialReputation,
                    faction.Prefab.ShortDescription, faction.Prefab.Description,
                    faction.Prefab.Icon, faction.Prefab.BackgroundPortrait, faction.Prefab.IconColor);
                CreatePathUnlockElement(factionFrame, faction, null);
            }

            float maxDescriptionHeight = 0.0f;
            foreach (GUIComponent child in reputationList.Content.Children)
            {
                var descriptionElement = child.FindChild("description", recursive: true) as GUITextBlock;
                float descriptionHeight = descriptionElement.TextSize.Y * 1.1f;
                if (child.FindChild("unlockinfo") is GUIComponent unlockInfoComponent)
                {
                    descriptionHeight += 1.25f * unlockInfoComponent.Rect.Height;
                }
                maxDescriptionHeight = Math.Max(maxDescriptionHeight, descriptionHeight);
            }
            foreach (GUIComponent child in reputationList.Content.Children)
            {
                var headerElement = child.FindChild("header", recursive: true) as GUITextBlock;
                var descriptionElement = child.FindChild("description", recursive: true) as GUITextBlock;
                descriptionElement.RectTransform.NonScaledSize = new Point(descriptionElement.Rect.Width, (int)maxDescriptionHeight);
                descriptionElement.RectTransform.IsFixedSize = true;
                child.RectTransform.NonScaledSize = new Point(child.Rect.Width, headerElement.Rect.Height + descriptionElement.RectTransform.Parent.Children.Sum(c => c.Rect.Height + ((GUILayoutGroup)descriptionElement.Parent).AbsoluteSpacing));
            }

            void CreatePathUnlockElement(GUIComponent reputationFrame, Faction faction, Location location)
            {
                if (GameMain.GameSession?.Campaign?.Map == null) { return; }

                IEnumerable<LocationConnection> connectionsBetweenBiomes = 
                    GameMain.GameSession.Campaign.Map.Connections.Where(c => c.Locations[0].Biome != c.Locations[1].Biome);
                
                foreach (LocationConnection connection in connectionsBetweenBiomes)
                {
                    if (!connection.Locked || (!connection.Locations[0].Discovered && !connection.Locations[1].Discovered)) { continue; }

                    //don't show the "reputation required to unlock" text if another connection between the biomes has already been unlocked
                    if (connectionsBetweenBiomes.Where(c => !c.Locked).Any(c => 
                        (c.Locations[0].Biome == connection.Locations[0].Biome && c.Locations[1].Biome == connection.Locations[1].Biome) ||
                        (c.Locations[1].Biome == connection.Locations[0].Biome && c.Locations[0].Biome == connection.Locations[1].Biome)))
                    {
                        continue;
                    }

                    var gateLocation = connection.Locations[0].IsGateBetweenBiomes ? connection.Locations[0] : connection.Locations[1];
                    var unlockEvent = EventPrefab.GetUnlockPathEvent(gateLocation.LevelData.Biome.Identifier, gateLocation.Faction);

                    if (unlockEvent == null) { continue; }
                    if (unlockEvent.Faction.IsEmpty)
                    {
                        if (location == null || gateLocation != location) { continue; }
                    }
                    else
                    {
                        if (faction == null || faction.Prefab.Identifier != unlockEvent.Faction) { continue; }
                    }

                    if (unlockEvent != null)
                    {
                        Reputation unlockReputation = gateLocation.Reputation;
                        Faction unlockFaction = null;
                        if (!unlockEvent.Faction.IsEmpty)
                        {
                            unlockFaction = GameMain.GameSession.Campaign.Factions.Find(f => f.Prefab.Identifier == unlockEvent.Faction);
                            unlockReputation = unlockFaction?.Reputation;
                        }
                        float normalizedUnlockReputation = MathUtils.InverseLerp(unlockReputation.MinReputation, unlockReputation.MaxReputation, unlockEvent.UnlockPathReputation);
                        RichString unlockText = RichString.Rich(TextManager.GetWithVariables(
                            "lockedpathreputationrequirement",
                            ("[reputation]", Reputation.GetFormattedReputationText(normalizedUnlockReputation, unlockEvent.UnlockPathReputation, addColorTags: true)),
                            ("[biomename]", $"‖color:gui.orange‖{connection.LevelData.Biome.DisplayName}‖end‖")));
                        var unlockInfoPanel = new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.0f), reputationFrame.RectTransform, Anchor.BottomCenter) { MinSize = new Point(0, GUI.IntScale(30)), AbsoluteOffset = new Point(0, GUI.IntScale(3)) },
                            unlockText, style: "GUIButtonRound", textAlignment: Alignment.Center, textColor: GUIStyle.TextColorNormal);
                        unlockInfoPanel.Color = Color.Lerp(unlockInfoPanel.Color, Color.Black, 0.8f);
                        unlockInfoPanel.UserData = "unlockinfo";
                        if (unlockInfoPanel.TextSize.X > unlockInfoPanel.Rect.Width * 0.7f)
                        {
                            unlockInfoPanel.Font = GUIStyle.SmallFont;
                        }
                    }
                }                
            }
        }

        private static GUIComponent CreateTraitorInfoPanel(GUIComponent parent, TraitorManager.TraitorResults traitorResults, float iconAnimDelay)
        {
            var traitorCharacter = traitorResults.GetTraitorClient()?.Character;

            string resultTag =
                traitorResults.VotedCorrectTraitor ?
                traitorResults.ObjectiveSuccessful ? "traitor.blameresult.correct.objectivesuccessful" : "traitor.blameresult.correct.objectivefailed" :
                "traitor.blameresult.failure";

            var textContent = new List<LocalizedString>()
            {
                TextManager.GetWithVariable("traitor.blameresult", "[name]", traitorCharacter?.Name ?? "unknown"),
                TextManager.Get(resultTag)
            };

            if (traitorResults.MoneyPenalty > 0)
            {
                textContent.Add(
                    TextManager.GetWithVariable(
                        "traitor.blameresult.failure.penalty", 
                        "[money]", 
                        TextManager.FormatCurrency(traitorResults.MoneyPenalty, includeCurrencySymbol: false)));
            }

            var icon = GUIStyle.GetComponentStyle("TraitorMissionIcon")?.GetDefaultSprite();

            var content = CreateMissionEntry(
                parent,
                string.Empty,
                textContent,
                difficultyIconCount: 0,
                icon, GUIStyle.Red,
                out GUIImage missionIcon);
            UpdateMissionStateIcon(traitorResults.VotedCorrectTraitor, missionIcon, iconAnimDelay);
            return content;
        }

        public static GUIComponent CreateMissionEntry(GUIComponent parent, LocalizedString header, List<LocalizedString> textContent, int difficultyIconCount, 
            Sprite icon, Color iconColor, out GUIImage missionIcon)
        {
            int spacing = GUI.IntScale(5);

            int defaultLineHeight = (int)GUIStyle.Font.MeasureChar('T').Y;

            //make the icon big enough for header + some lines of text
            int iconSize = (int)(GUIStyle.SubHeadingFont.MeasureChar('T').Y + defaultLineHeight * 6);

            GUILayoutGroup content = new GUILayoutGroup(new RectTransform(new Point(parent.Rect.Width, iconSize), parent.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                AbsoluteSpacing = spacing,
                CanBeFocused = true
            };
            if (icon != null)
            {
                missionIcon = new GUIImage(new RectTransform(new Point(iconSize), content.RectTransform), icon, null, true)
                {
                    Color = iconColor,
                    HoverColor = iconColor,
                    SelectedColor = iconColor,
                    CanBeFocused = false
                };
                missionIcon.RectTransform.IsFixedSize = true;
            }
            else
            {
                missionIcon = null;
            }
            GUILayoutGroup missionTextGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.744f, 0f), content.RectTransform, Anchor.CenterLeft), childAnchor: Anchor.TopLeft)
            {
                AbsoluteSpacing = spacing
            };
            content.Recalculate();

            RichString missionNameString = RichString.Rich(header);
            List<RichString> contentStrings = new List<RichString>(textContent.Select(t => RichString.Rich(t)));

            if (!header.IsNullOrEmpty())
            {
                var nameText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextGroup.RectTransform), 
                    missionNameString, font: GUIStyle.SubHeadingFont, wrap: true);
                nameText.RectTransform.MinSize = new Point(0, (int)nameText.TextSize.Y);
            }

            GUILayoutGroup difficultyIndicatorGroup = null;
            if (difficultyIconCount > 0)
            {
                difficultyIndicatorGroup = new GUILayoutGroup(new RectTransform(new Point(missionTextGroup.Rect.Width, defaultLineHeight), parent: missionTextGroup.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                {
                    AbsoluteSpacing = 1
                };
                difficultyIndicatorGroup.RectTransform.MinSize = new Point(0, defaultLineHeight);
                var difficultyColor = Mission.GetDifficultyColor(difficultyIconCount);
                for (int i = 0; i < difficultyIconCount; i++)
                {
                    new GUIImage(new RectTransform(Vector2.One, difficultyIndicatorGroup.RectTransform, scaleBasis: ScaleBasis.Smallest), "DifficultyIndicator", scaleToFit: true)
                    {
                        CanBeFocused = false,
                        Color = difficultyColor
                    };
                }
            }

            GUITextBlock firstContentText = null;
            foreach (var contentString in contentStrings)
            {
                var text = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextGroup.RectTransform), contentString, wrap: true);
                text.RectTransform.MinSize = new Point(0, (int)text.TextSize.Y);
                firstContentText ??= text;
            }
            if (difficultyIndicatorGroup != null && firstContentText != null)
            {
                //make the icons align with the text content
                difficultyIndicatorGroup.RectTransform.AbsoluteOffset = new Point((int)firstContentText.Padding.X, 0);
            }
            missionTextGroup.RectTransform.MinSize = 
                new Point(0, missionTextGroup.Children.Sum(c => c.Rect.Height + missionTextGroup.AbsoluteSpacing) - missionTextGroup.AbsoluteSpacing);
            missionTextGroup.Recalculate();
            content.RectTransform.MinSize = new Point(0, Math.Max(missionTextGroup.Rect.Height, iconSize));

            return content;
        }

        public static void AddSeparators(GUIComponent container)
        {
            var children = container.Children.ToList();
            if (children.Count < 2) { return; }

            var lastChild = children.Last();
            foreach (var child in children)
            {
                if (child != lastChild)
                {
                    var separator = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.1f), container.RectTransform), style: "HorizontalLine");
                    separator.RectTransform.RepositionChildInHierarchy(container.GetChildIndex(child) + 1);
                }            
            }
        }

        public static void UpdateMissionStateIcon(bool success, GUIImage missionIcon, float delay = 0.5f)
        {
            if (missionIcon == null) { return; }
            string style = success ? "MissionCompletedIcon" : "MissionFailedIcon";
            GUIImage stateIcon = missionIcon.GetChild<GUIImage>();
            if (string.IsNullOrEmpty(style))
            {
                if (stateIcon != null)
                {
                    stateIcon.Visible = false;
                }
            }
            else
            {
                bool wasVisible = stateIcon is { Visible: true };
                stateIcon ??= new GUIImage(new RectTransform(Vector2.One, missionIcon.RectTransform, Anchor.Center), style, scaleToFit: true);
                stateIcon.Visible = true;
                if (!wasVisible)
                {
                    stateIcon.FadeIn(delay, 0.15f);
                    stateIcon.Pulsate(Vector2.One, Vector2.One * 1.5f, 1.0f + delay);
                }
            }
        }


        private LocalizedString GetHeaderText(bool gameOver, CampaignMode.TransitionType transitionType)
        {
            LocalizedString locationName = Submarine.MainSub is { AtEndExit: true } ? endLocation?.DisplayName : startLocation?.DisplayName;

            string textTag;
            if (gameOver)
            {
                textTag = "RoundSummaryGameOver";
            }
            else
            {
                switch (transitionType)
                {
                    case CampaignMode.TransitionType.LeaveLocation:
                        locationName = startLocation?.DisplayName;
                        textTag = "RoundSummaryLeaving";
                        break;
                    case CampaignMode.TransitionType.ProgressToNextLocation:
                        locationName = endLocation?.DisplayName;
                        textTag = "RoundSummaryProgress";
                        break;
                    case CampaignMode.TransitionType.ProgressToNextEmptyLocation:
                        locationName = endLocation?.DisplayName;
                        textTag = "RoundSummaryProgressToEmptyLocation";
                        break;
                    case CampaignMode.TransitionType.ReturnToPreviousLocation:
                        locationName = startLocation?.DisplayName;
                        textTag = "RoundSummaryReturn";
                        break;
                    case CampaignMode.TransitionType.ReturnToPreviousEmptyLocation:
                        locationName = startLocation?.DisplayName;
                        textTag = "RoundSummaryReturnToEmptyLocation";
                        break;
                    default:
                        textTag = Submarine.MainSub.AtEndExit ? "RoundSummaryProgress" : "RoundSummaryReturn";
                        break;
                }
            }

            if (startLocation?.Biome != null && startLocation.Biome.IsEndBiome)
            {
                locationName ??= startLocation.DisplayName;
            }

            if (textTag == null) { return ""; }

            if (locationName == null)
            {
                DebugConsole.ThrowError($"Error while creating round summary: could not determine destination location. Start location: {startLocation?.DisplayName ?? "null"}, end location: {endLocation?.DisplayName ?? "null"}");
                locationName = "[UNKNOWN]";
            }

            LocalizedString subName = string.Empty;
            SubmarineInfo currentOrPending = SubmarineSelection.CurrentOrPendingSubmarine();
            if (currentOrPending != null)
            {
                subName = currentOrPending.DisplayName;
            }

            return TextManager.GetWithVariables(textTag, ("[sub]", subName), ("[location]", locationName));            
        }

        private GUIListBox CreateCrewList(GUIComponent parent, IEnumerable<CharacterInfo> characterInfos, TraitorManager.TraitorResults? traitorResults)
        {
            var headerFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.0f), parent.RectTransform, Anchor.TopCenter, minSize: new Point(0, (int)(30 * GUI.Scale))) { }, isHorizontal: true)
            {
                AbsoluteSpacing = 2
            };
            GUIButton jobButton = new GUIButton(new RectTransform(new Vector2(0f, 1f), headerFrame.RectTransform), TextManager.Get("tabmenu.job"), style: "GUIButtonSmallFreeScale");
            GUIButton characterButton = new GUIButton(new RectTransform(new Vector2(0f, 1f), headerFrame.RectTransform), TextManager.Get("name"), style: "GUIButtonSmallFreeScale");
            GUIButton statusButton = new GUIButton(new RectTransform(new Vector2(0f, 1f), headerFrame.RectTransform), TextManager.Get("label.statuslabel"), style: "GUIButtonSmallFreeScale");

            float sizeMultiplier = 1.0f;
            //sizeMultiplier = (headerFrame.Rect.Width - headerFrame.AbsoluteSpacing * (headerFrame.CountChildren - 1)) / (float)headerFrame.Rect.Width;

            jobButton.RectTransform.RelativeSize = new Vector2(jobColumnWidthPercentage * sizeMultiplier, 1f);
            characterButton.RectTransform.RelativeSize = new Vector2(characterColumnWidthPercentage * sizeMultiplier, 1f);
            statusButton.RectTransform.RelativeSize = new Vector2(statusColumnWidthPercentage * sizeMultiplier, 1f);

            jobButton.TextBlock.Font = characterButton.TextBlock.Font = statusButton.TextBlock.Font = GUIStyle.HotkeyFont;
            jobButton.CanBeFocused = characterButton.CanBeFocused = statusButton.CanBeFocused = false;
            jobButton.TextBlock.ForceUpperCase = characterButton.TextBlock.ForceUpperCase = statusButton.ForceUpperCase = ForceUpperCase.Yes;

            jobColumnWidth = jobButton.Rect.Width;
            characterColumnWidth = characterButton.Rect.Width;
            statusColumnWidth = statusButton.Rect.Width;

            GUIListBox crewList = new GUIListBox(new RectTransform(Vector2.One, parent.RectTransform))
            {
                Padding = new Vector4(4, 10, 0, 0) * GUI.Scale,
                AutoHideScrollBar = false
            };
            crewList.ContentBackground.Color = Color.Transparent;

            headerFrame.RectTransform.RelativeSize -= new Vector2(crewList.ScrollBar.RectTransform.RelativeSize.X, 0.0f);

            float delay = crewListAnimDelay;
            foreach (CharacterInfo characterInfo in characterInfos)
            {
                if (characterInfo == null) { continue; }
                CreateCharacterElement(characterInfo, crewList, traitorResults, delay);
                delay += crewListAnimDelay;
            }
            missionIconAnimDelay = delay;

            return crewList;
        }

        private void CreateCharacterElement(CharacterInfo characterInfo, GUIListBox listBox, TraitorManager.TraitorResults? traitorResults, float animDelay)
        {
            GUIFrame frame = new GUIFrame(new RectTransform(new Point(listBox.Content.Rect.Width, GUI.IntScale(45)), listBox.Content.RectTransform), style: "ListBoxElement")
            {
                CanBeFocused = false,
                UserData = characterInfo,
                Color = (Character.Controlled?.Info == characterInfo) ? TabMenu.OwnCharacterBGColor : Color.Transparent
            };

            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.9f), frame.RectTransform, Anchor.Center), isHorizontal: true)
            {
                AbsoluteSpacing = 2,
                Stretch = true
            };

            new GUICustomComponent(new RectTransform(new Point(jobColumnWidth, paddedFrame.Rect.Height), paddedFrame.RectTransform, Anchor.Center), onDraw: (sb, component) => characterInfo.DrawJobIcon(sb, component.Rect))
            {
                ToolTip = characterInfo.Job.Name ?? "",
                HoverColor = Color.White,
                SelectedColor = Color.White
            };

            GUITextBlock characterNameBlock = new GUITextBlock(new RectTransform(new Point(characterColumnWidth, paddedFrame.Rect.Height), paddedFrame.RectTransform),
                ToolBox.LimitString(characterInfo.Name, GUIStyle.Font, characterColumnWidth), textAlignment: Alignment.Center, textColor: characterInfo.Job.Prefab.UIColor);

            LocalizedString statusText = TextManager.Get("StatusOK");
            Color statusColor = GUIStyle.Green;

            Character character = characterInfo.Character;
            if (character == null || character.IsDead)
            {
                if (character == null && characterInfo.IsNewHire && characterInfo.CauseOfDeath == null)
                {
                    statusText = TextManager.Get("CampaignCrew.NewHire");
                    statusColor = GUIStyle.Blue;
                }
                else if (characterInfo.CauseOfDeath == null)
                {
                    statusText = TextManager.Get("CauseOfDeathDescription.Unknown");
                    statusColor = Color.DarkRed;
                }
                else if (characterInfo.CauseOfDeath.Type == CauseOfDeathType.Affliction && characterInfo.CauseOfDeath.Affliction == null)
                {
                    string errorMsg = "Character \"[name]\" had an invalid cause of death (the type of the cause of death was Affliction, but affliction was not specified).";
                    DebugConsole.ThrowError(errorMsg.Replace("[name]", characterInfo.Name));
                    GameAnalyticsManager.AddErrorEventOnce("RoundSummary:InvalidCauseOfDeath", GameAnalyticsManager.ErrorSeverity.Error, errorMsg.Replace("[name]", characterInfo.SpeciesName.Value));
                    statusText = TextManager.Get("CauseOfDeathDescription.Unknown");
                    statusColor = GUIStyle.Red;
                }
                else
                {
                    statusText = characterInfo.CauseOfDeath.Type == CauseOfDeathType.Affliction ?
                        characterInfo.CauseOfDeath.Affliction.CauseOfDeathDescription :
                        TextManager.Get("CauseOfDeathDescription." + characterInfo.CauseOfDeath.Type.ToString());
                    statusColor = Color.DarkRed;
                }
            }
            else
            {
                if (character.IsUnconscious)
                {
                    statusText = TextManager.Get("Unconscious");
                    statusColor = Color.DarkOrange;
                }
                else if (character.Vitality / character.MaxVitality < 0.8f)
                {
                    statusText = TextManager.Get("Injured");
                    statusColor = Color.DarkOrange;
                }
            }

            GUITextBlock statusBlock = new GUITextBlock(new RectTransform(new Point(statusColumnWidth, paddedFrame.Rect.Height), paddedFrame.RectTransform),
                ToolBox.LimitString(statusText.Value, GUIStyle.Font, characterColumnWidth), textAlignment: Alignment.Center, textColor: statusColor);

            frame.FadeIn(animDelay, 0.15f);
            foreach (var child in frame.GetAllChildren())
            {
                child.FadeIn(animDelay, 0.15f);
            }

            if (traitorResults.HasValue && GameMain.NetworkMember != null)
            {
                var clientVotedAsTraitor = traitorResults.Value.GetTraitorClient();
                bool isTraitor = clientVotedAsTraitor != null && clientVotedAsTraitor.Character == character;
                if (isTraitor)
                {
                    var img = new GUIImage(new RectTransform(new Point(paddedFrame.Rect.Height), paddedFrame.RectTransform, Anchor.CenterRight), style: "TraitorVoteButton")
                    {
                        IgnoreLayoutGroups = true,
                        ToolTip = TextManager.GetWithVariable("traitor.blameresult", "[name]", characterInfo.Name)
                    };
                    img.FadeIn(1.0f + animDelay, 0.15f);
                    img.Pulsate(Vector2.One, Vector2.One * 1.5f, 1.5f + animDelay);
                }
            }
        }

        private static GUIFrame CreateReputationElement(GUIComponent parent, 
            LocalizedString name, Reputation reputation, float initialReputation,
            LocalizedString shortDescription, LocalizedString fullDescription, Sprite icon, Sprite backgroundPortrait, Color iconColor)
        {
            var factionFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.1f), parent.RectTransform), style: null);

            if (backgroundPortrait != null)
            {
                new GUICustomComponent(new RectTransform(Vector2.One, factionFrame.RectTransform), onDraw: (sb, customComponent) =>
                {
                    backgroundPortrait.Draw(sb, customComponent.Rect.Center.ToVector2(), customComponent.Color, backgroundPortrait.size / 2, scale: customComponent.Rect.Width / backgroundPortrait.size.X);
                })
                {
                    HideElementsOutsideFrame = true,
                    IgnoreLayoutGroups = true,
                    Color = iconColor * 0.2f
                };
            }

            var factionInfoHorizontal = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), factionFrame.RectTransform, Anchor.Center), childAnchor: Anchor.CenterRight, isHorizontal: true)
            {
                AbsoluteSpacing = GUI.IntScale(5),
                Stretch = true
            };

            var factionIcon = new GUIImage(new RectTransform(Vector2.One * 0.7f, factionInfoHorizontal.RectTransform, scaleBasis: ScaleBasis.Smallest), icon, scaleToFit: true)
            {
                Color = iconColor
            };
            var factionTextContent = new GUILayoutGroup(new RectTransform(Vector2.One, factionInfoHorizontal.RectTransform))
            {
                AbsoluteSpacing = GUI.IntScale(10),
                Stretch = true
            };

            factionInfoHorizontal.Recalculate();

            var header = new GUITextBlock(new RectTransform(new Point(factionTextContent.Rect.Width, GUI.IntScale(40)), factionTextContent.RectTransform),
                name, font: GUIStyle.SubHeadingFont)
            {
                Padding = Vector4.Zero,
                UserData = "header"
            };
            header.RectTransform.IsFixedSize = true;

            var sliderHolder = new GUILayoutGroup(new RectTransform(new Point((int)(factionTextContent.Rect.Width * 0.8f), GUI.IntScale(20.0f)), factionTextContent.RectTransform),
                childAnchor: Anchor.CenterLeft, isHorizontal: true)
            {
                RelativeSpacing = 0.05f,
                Stretch = true
            };
            sliderHolder.RectTransform.IsFixedSize = true;
            factionTextContent.Recalculate();
            
            new GUICustomComponent(new RectTransform(new Vector2(0.8f, 1.0f), sliderHolder.RectTransform),
                onDraw: (sb, customComponent) => DrawReputationBar(sb, customComponent.Rect, reputation.NormalizedValue, reputation.MinReputation, reputation.MaxReputation));
                
            var reputationText = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), sliderHolder.RectTransform),
                string.Empty, textAlignment: Alignment.CenterLeft, font: GUIStyle.SubHeadingFont);
            SetReputationText(reputationText);
            reputation?.OnReputationValueChanged.RegisterOverwriteExisting("RefreshRoundSummary".ToIdentifier(), _ => 
            {
                SetReputationText(reputationText);
            });

            void SetReputationText(GUITextBlock textBlock)
            {
                LocalizedString reputationText = Reputation.GetFormattedReputationText(reputation.NormalizedValue, reputation.Value, addColorTags: true);
                int reputationChange = (int)Math.Round(reputation.Value - initialReputation);
                if (Math.Abs(reputationChange) > 0)
                {
                    string changeText = $"{(reputationChange > 0 ? "+" : "") + reputationChange}";
                    string colorStr = XMLExtensions.ToStringHex(reputationChange > 0 ? GUIStyle.Green : GUIStyle.Red);
                    textBlock.Text = RichString.Rich($"{reputationText} (‖color:{colorStr}‖{changeText}‖color:end‖)");
                }
                else
                {
                    textBlock.Text = RichString.Rich(reputationText);
                }
            }

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.0f), factionTextContent.RectTransform) { MinSize = new Point(0, GUI.IntScale(5)) }, style: null);

            var factionDescription = new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.6f), factionTextContent.RectTransform),
                shortDescription, font: GUIStyle.SmallFont, wrap: true)
            {
                UserData = "description",
                Padding = Vector4.Zero
            };
            if (shortDescription != fullDescription && !fullDescription.IsNullOrEmpty())
            {
                factionDescription.ToolTip = fullDescription;
            }

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.0f), factionTextContent.RectTransform) { MinSize = new Point(0, GUI.IntScale(5)) }, style: null);

            factionInfoHorizontal.Recalculate();
            factionTextContent.Recalculate();

            return factionFrame;
        }

        public static void DrawReputationBar(SpriteBatch sb, Rectangle rect, float normalizedReputation, float minReputation, float maxReputation)
        {
            int segmentWidth = rect.Width / 5;
            rect.Width = segmentWidth * 5;
            for (int i = 0; i < 5; i++)
            {
                GUI.DrawRectangle(sb, new Rectangle(rect.X + (segmentWidth * i), rect.Y, segmentWidth, rect.Height), Reputation.GetReputationColor(i / 5.0f), isFilled: true);
                GUI.DrawRectangle(sb, new Rectangle(rect.X + (segmentWidth * i), rect.Y, segmentWidth, rect.Height), GUIStyle.ColorInventoryBackground, isFilled: false);
            }
            GUI.DrawRectangle(sb, rect, GUIStyle.ColorInventoryBackground, isFilled: false);

            GUI.Arrow.Draw(sb, new Vector2(rect.X + rect.Width * normalizedReputation, rect.Y), GUIStyle.ColorInventoryBackground, scale: GUI.Scale, spriteEffect: SpriteEffects.FlipVertically);
            GUI.Arrow.Draw(sb, new Vector2(rect.X + rect.Width * normalizedReputation, rect.Y), GUIStyle.TextColorNormal, scale: GUI.Scale * 0.8f, spriteEffect: SpriteEffects.FlipVertically);

            GUI.DrawString(sb, new Vector2(rect.X, rect.Bottom), ((int)minReputation).ToString(), GUIStyle.TextColorNormal, font: GUIStyle.SmallFont);
            string maxRepText = ((int)maxReputation).ToString();
            Vector2 textSize = GUIStyle.SmallFont.MeasureString(maxRepText);
            GUI.DrawString(sb, new Vector2(rect.Right - textSize.X, rect.Bottom), maxRepText, GUIStyle.TextColorNormal, font: GUIStyle.SmallFont);
        }
    }
}
