using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Barotrauma
{
    class RoundSummary
    {
        private const float jobColumnWidthPercentage = 0.11f;
        private const float characterColumnWidthPercentage = 0.44f;
        private const float statusColumnWidthPercentage = 0.45f;

        private int jobColumnWidth, characterColumnWidth, statusColumnWidth;

        private readonly SubmarineInfo sub;
        private readonly List<Mission> selectedMissions;
        private readonly Location startLocation, endLocation;

        private readonly GameMode gameMode;

        private readonly float initialLocationReputation;
        private readonly Dictionary<Faction, float> initialFactionReputations = new Dictionary<Faction, float>();

        public GUILayoutGroup ButtonArea { get; private set; }

        public GUIButton ContinueButton { get; private set; }

        public GUIComponent Frame { get; private set; }



        public RoundSummary(SubmarineInfo sub, GameMode gameMode, IEnumerable<Mission> selectedMissions, Location startLocation, Location endLocation)
        {
            this.sub = sub;
            this.gameMode = gameMode;
            this.selectedMissions = selectedMissions.ToList();
            this.startLocation = startLocation;
            this.endLocation = endLocation;
            initialLocationReputation = startLocation?.Reputation?.Value ?? 0.0f;
            if (gameMode is CampaignMode campaignMode)
            {
                foreach (Faction faction in campaignMode.Factions)
                {
                    initialFactionReputations.Add(faction, faction.Reputation.Value);
                }
            }
        }

        public GUIFrame CreateSummaryFrame(GameSession gameSession, string endMessage, List<TraitorMissionResult> traitorResults, CampaignMode.TransitionType transitionType = CampaignMode.TransitionType.None)
        {
            bool singleplayer = GameMain.NetworkMember == null;
            bool gameOver =
                gameSession.GameMode.IsSinglePlayer ?
                gameSession.CrewManager.GetCharacters().All(c => c.IsDead || c.IsIncapacitated) :
                gameSession.CrewManager.GetCharacters().All(c => c.IsDead || c.IsIncapacitated || c.IsBot);

            if (!singleplayer)
            {
                SoundPlayer.OverrideMusicType = gameOver ? "crewdead" : "endround";
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

            GUIFrame crewFrame = new GUIFrame(new RectTransform(new Vector2(0.35f, 0.45f), background.RectTransform, Anchor.TopCenter, minSize: new Point(minWidth, minHeight)));
            GUIFrame crewFrameInner = new GUIFrame(new RectTransform(new Point(crewFrame.Rect.Width - padding * 2, crewFrame.Rect.Height - padding * 2), crewFrame.RectTransform, Anchor.Center), style: "InnerFrame");

            var crewContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), crewFrameInner.RectTransform, Anchor.Center))
            {
                Stretch = true
            };

            var crewHeader = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), crewContent.RectTransform),
                TextManager.Get("crew"), textAlignment: Alignment.TopLeft, font: GUI.SubHeadingFont);
            crewHeader.RectTransform.MinSize = new Point(0, GUI.IntScale(crewHeader.Rect.Height * 2.0f));

            CreateCrewList(crewContent, gameSession.CrewManager.GetCharacterInfos().Where(c => c.TeamID != CharacterTeamType.Team2));

            //another crew frame for the 2nd team in combat missions
            if (gameSession.Missions.Any(m => m is CombatMission))
            {
                crewHeader.Text = CombatMission.GetTeamName(CharacterTeamType.Team1);
                GUIFrame crewFrame2 = new GUIFrame(new RectTransform(new Vector2(0.35f, 0.45f), background.RectTransform, Anchor.TopCenter, minSize: new Point(minWidth, minHeight)));
                rightPanels.Add(crewFrame2);
                GUIFrame crewFrameInner2 = new GUIFrame(new RectTransform(new Point(crewFrame2.Rect.Width - padding * 2, crewFrame2.Rect.Height - padding * 2), crewFrame2.RectTransform, Anchor.Center), style: "InnerFrame");
                var crewContent2 = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), crewFrameInner2.RectTransform, Anchor.Center))
                {
                    Stretch = true
                };
                var crewHeader2 = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), crewContent2.RectTransform),
                    CombatMission.GetTeamName(CharacterTeamType.Team2), textAlignment: Alignment.TopLeft, font: GUI.SubHeadingFont);
                crewHeader2.RectTransform.MinSize = new Point(0, GUI.IntScale(crewHeader2.Rect.Height * 2.0f));
                CreateCrewList(crewContent2, gameSession.CrewManager.GetCharacterInfos().Where(c => c.TeamID == CharacterTeamType.Team2));
            }

            //header -------------------------------------------------------------------------------

            string headerText = GetHeaderText(gameOver, transitionType);
            GUITextBlock headerTextBlock = null;
            if (!string.IsNullOrEmpty(headerText))
            {
                headerTextBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), crewFrame.RectTransform, Anchor.TopLeft, Pivot.BottomLeft), 
                    headerText, textAlignment: Alignment.BottomLeft, font: GUI.LargeFont, wrap: true);
            }

            //traitor panel -------------------------------------------------------------------------------

            if (traitorResults != null && traitorResults.Any())
            {
                GUIFrame traitorframe = new GUIFrame(new RectTransform(crewFrame.RectTransform.RelativeSize, background.RectTransform, Anchor.TopCenter, minSize: crewFrame.RectTransform.MinSize));
                rightPanels.Add(traitorframe);
                GUIFrame traitorframeInner = new GUIFrame(new RectTransform(new Point(traitorframe.Rect.Width - padding * 2, traitorframe.Rect.Height - padding * 2), traitorframe.RectTransform, Anchor.Center), style: "InnerFrame");

                var traitorContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), traitorframeInner.RectTransform, Anchor.Center))
                {
                    Stretch = true
                };

                var traitorHeader = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), traitorContent.RectTransform),
                    TextManager.Get("traitors"), font: GUI.SubHeadingFont);
                traitorHeader.RectTransform.MinSize = new Point(0, GUI.IntScale(traitorHeader.Rect.Height * 2.0f));

                GUIListBox listBox = CreateCrewList(traitorContent, traitorResults.SelectMany(tr => tr.Characters.Select(c => c.Info)));

                foreach (var traitorResult in traitorResults)
                {
                    var traitorMission = TraitorMissionPrefab.List.Find(t => t.Identifier == traitorResult.MissionIdentifier);
                    if (traitorMission == null) { continue; }

                    //spacing
                    new GUIFrame(new RectTransform(new Point(listBox.Content.Rect.Width, GUI.IntScale(25)), listBox.Content.RectTransform), style: null);

                    var traitorResultHorizontal = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.3f), listBox.Content.RectTransform), childAnchor: Anchor.CenterLeft, isHorizontal: true)
                    {
                        RelativeSpacing = 0.05f,
                        Stretch = true
                    };

                    new GUIImage(new RectTransform(new Point(traitorResultHorizontal.Rect.Height), traitorResultHorizontal.RectTransform), traitorMission.Icon, scaleToFit: true)
                    {
                        Color = traitorMission.IconColor
                    };

                    string traitorMessage = TextManager.GetServerMessage(traitorResult.EndMessage);
                    if (!string.IsNullOrEmpty(traitorMessage))
                    {
                        var textContent = new GUILayoutGroup(new RectTransform(Vector2.One, traitorResultHorizontal.RectTransform))
                        {
                            RelativeSpacing = 0.025f
                        };

                        var traitorStatusText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), textContent.RectTransform),
                            TextManager.Get(traitorResult.Success ? "missioncompleted" : "missionfailed"), 
                            textColor: traitorResult.Success ? GUI.Style.Green : GUI.Style.Red, font: GUI.SubHeadingFont);

                        var traitorMissionInfo = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), textContent.RectTransform),
                            traitorMessage, font: GUI.SmallFont, wrap: true);

                        traitorResultHorizontal.Recalculate();

                        traitorStatusText.CalculateHeightFromText();
                        traitorMissionInfo.CalculateHeightFromText();
                        traitorStatusText.RectTransform.MinSize = new Point(0, traitorStatusText.Rect.Height);
                        traitorMissionInfo.RectTransform.MinSize = new Point(0, traitorMissionInfo.Rect.Height);
                        textContent.RectTransform.MaxSize = new Point(int.MaxValue, (int)((traitorStatusText.Rect.Height + traitorMissionInfo.Rect.Height) * 1.2f));
                        traitorResultHorizontal.RectTransform.MinSize = new Point(0, traitorStatusText.RectTransform.MinSize.Y + traitorMissionInfo.RectTransform.MinSize.Y);
                    }
                }
            }

            //reputation panel -------------------------------------------------------------------------------

            if (gameMode is CampaignMode campaignMode)
            {
                GUIFrame reputationframe = new GUIFrame(new RectTransform(crewFrame.RectTransform.RelativeSize, background.RectTransform, Anchor.TopCenter, minSize: crewFrame.RectTransform.MinSize));
                rightPanels.Add(reputationframe);
                GUIFrame reputationframeInner = new GUIFrame(new RectTransform(new Point(reputationframe.Rect.Width - padding * 2, reputationframe.Rect.Height - padding * 2), reputationframe.RectTransform, Anchor.Center), style: "InnerFrame");

                var reputationContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), reputationframeInner.RectTransform, Anchor.Center))
                {
                    Stretch = true
                };

                var reputationHeader = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), reputationContent.RectTransform),
                    TextManager.Get("reputation"), textAlignment: Alignment.TopLeft, font: GUI.SubHeadingFont);
                reputationHeader.RectTransform.MinSize = new Point(0, GUI.IntScale(reputationHeader.Rect.Height * 2.0f));

                CreateReputationInfoPanel(reputationContent, campaignMode);
            }

            //mission panel -------------------------------------------------------------------------------

            GUIFrame missionframe = new GUIFrame(new RectTransform(new Vector2(0.39f, 0.3f), background.RectTransform, Anchor.TopCenter, minSize: new Point(minWidth, minHeight / 4)));
            GUILayoutGroup missionFrameContent = new GUILayoutGroup(new RectTransform(new Point(missionframe.Rect.Width - padding * 2, missionframe.Rect.Height - padding * 2), missionframe.RectTransform, Anchor.Center))
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };
            GUIFrame missionframeInner = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.9f), missionFrameContent.RectTransform, Anchor.Center), style: "InnerFrame");

            var missionContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.93f), missionframeInner.RectTransform, Anchor.Center))
            {
                Stretch = true
            };

            List<Mission> missionsToDisplay = new List<Mission>(selectedMissions);
            if (!selectedMissions.Any() && startLocation?.SelectedMission != null) { missionsToDisplay.Add(startLocation.SelectedMission); }

            if (missionsToDisplay.Any())
            {
                var missionHeader = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionContent.RectTransform),
                    TextManager.Get(missionsToDisplay.Count > 1 ? "Missions" : "Mission"), textAlignment: Alignment.TopLeft, font: GUI.SubHeadingFont);
                missionHeader.RectTransform.MinSize = new Point(0, (int)(missionHeader.Rect.Height * 1.2f));
            }

            GUIListBox missionList = new GUIListBox(new RectTransform(Vector2.One, missionContent.RectTransform, Anchor.Center))
            {
                Padding = new Vector4(4, 10, 0, 0) * GUI.Scale
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
                endText.RectTransform.MinSize = new Point(0, endText.Rect.Height);
                var line = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.1f), missionList.Content.RectTransform), style: "HorizontalLine");
                line.RectTransform.NonScaledSize = new Point(line.Rect.Width, GUI.IntScale(5.0f));
            }

            foreach (Mission displayedMission in missionsToDisplay)
            {
                var missionContentHorizontal = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.8f), missionList.Content.RectTransform), childAnchor: Anchor.CenterLeft, isHorizontal: true)
                {
                    RelativeSpacing = 0.025f,
                    Stretch = true
                };

                string missionMessage =
                    selectedMissions.Contains(displayedMission) ?
                    displayedMission.Completed ? displayedMission.SuccessMessage : displayedMission.FailureMessage :
                    displayedMission.Description;
                GUIImage missionIcon = new GUIImage(new RectTransform(new Point((int)(missionContentHorizontal.Rect.Height)), missionContentHorizontal.RectTransform), displayedMission.Prefab.Icon, scaleToFit: true)
                {
                    Color = displayedMission.Prefab.IconColor
                }; 
                missionIcon.RectTransform.MinSize = new Point((int)(missionContentHorizontal.Rect.Height * 0.9f));
                if (selectedMissions.Contains(displayedMission))
                {
                    new GUIImage(new RectTransform(Vector2.One, missionIcon.RectTransform), displayedMission.Completed ? "MissionCompletedIcon" : "MissionFailedIcon", scaleToFit: true);
                }

                var missionTextContent = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.8f), missionContentHorizontal.RectTransform))
                {
                    RelativeSpacing = 0.05f
                };
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextContent.RectTransform),
                    displayedMission.Name, font: GUI.SubHeadingFont);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextContent.RectTransform),
                    missionMessage, wrap: true, parseRichText: true);
                if (selectedMissions.Contains(displayedMission) && displayedMission.Completed && displayedMission.Reward > 0)
                {
                    string rewardText = TextManager.GetWithVariable("currencyformat", "[credits]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", displayedMission.Reward));
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextContent.RectTransform), displayedMission.GetMissionRewardText(), parseRichText: true);
                }

                if (displayedMission != missionsToDisplay.Last())
                {
                    var spacing = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.0f), missionList.Content.RectTransform) { MaxSize = new Point(int.MaxValue, GUI.IntScale(15)) }, style: null);
                    new GUIFrame(new RectTransform(new Vector2(0.8f, 1.0f), spacing.RectTransform, Anchor.Center) { RelativeOffset = new Vector2(0.1f, 0.0f) }, "HorizontalLine");
                }
            }

            if (!missionsToDisplay.Any())
            {
                var missionContentHorizontal = new GUILayoutGroup(new RectTransform(Vector2.One, missionList.Content.RectTransform), childAnchor: Anchor.TopLeft, isHorizontal: true)
                {
                    RelativeSpacing = 0.025f,
                    Stretch = true
                };
                GUIImage missionIcon = new GUIImage(new RectTransform(new Point((int)(missionContentHorizontal.Rect.Height * 0.7f)), missionContentHorizontal.RectTransform), style: "NoMissionIcon", scaleToFit: true);
                missionIcon.RectTransform.MinSize = new Point((int)(missionContentHorizontal.Rect.Height * 0.7f));
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionContentHorizontal.RectTransform),
                    TextManager.Get("nomission"), font: GUI.LargeFont);
            }

            /*missionContentHorizontal.Recalculate();
            missionContent.Recalculate();
            missionIcon.RectTransform.MinSize = new Point(0, missionContentHorizontal.Rect.Height);
            missionTextContent.RectTransform.MaxSize = new Point(int.MaxValue, missionIcon.Rect.Width);*/

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
            GUIListBox reputationList = new GUIListBox(new RectTransform(Vector2.One, parent.RectTransform))
            {
                Padding = new Vector4(4, 10, 0, 0) * GUI.Scale
            };
            reputationList.ContentBackground.Color = Color.Transparent;

            if (startLocation.Type.HasOutpost && startLocation.Reputation != null)
            {
                var iconStyle = GUI.Style.GetComponentStyle("LocationReputationIcon");
                var locationFrame = CreateReputationElement(
                    reputationList.Content,
                    startLocation.Name,
                    startLocation.Reputation.Value, startLocation.Reputation.NormalizedValue, initialLocationReputation,
                    startLocation.Type.Name, "",
                    iconStyle?.GetDefaultSprite(), startLocation.Type.GetPortrait(0), iconStyle?.Color ?? Color.White);
                CreatePathUnlockElement(locationFrame, null, startLocation);
            }

            foreach (Faction faction in campaignMode.Factions)
            {
                float initialReputation = faction.Reputation.Value;
                if (initialFactionReputations.ContainsKey(faction))
                {
                    initialReputation = initialFactionReputations[faction];
                }
                else
                {
                    DebugConsole.AddWarning($"Could not determine reputation change for faction \"{faction.Prefab.Name}\" (faction was not present at the start of the round).");
                }
                var factionFrame = CreateReputationElement(
                    reputationList.Content,
                    faction.Prefab.Name,
                    faction.Reputation.Value, faction.Reputation.NormalizedValue, initialReputation,
                    faction.Prefab.ShortDescription, faction.Prefab.Description,
                    faction.Prefab.Icon, faction.Prefab.BackgroundPortrait, faction.Prefab.IconColor);
                CreatePathUnlockElement(factionFrame, faction, null);
            }

            float maxDescriptionHeight = 0.0f;
            foreach (GUIComponent child in reputationList.Content.Children)
            {
                var descriptionElement = child.FindChild("description", recursive: true) as GUITextBlock;
                maxDescriptionHeight = Math.Max(maxDescriptionHeight, descriptionElement.TextSize.Y * 1.1f);
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
                if (GameMain.GameSession?.Campaign?.Map != null)
                {
                    foreach (LocationConnection connection in GameMain.GameSession.Campaign.Map.Connections)
                    {
                        if (!connection.Locked || (!connection.Locations[0].Discovered && !connection.Locations[1].Discovered)) { continue; }

                        var gateLocation = connection.Locations[0].IsGateBetweenBiomes ? connection.Locations[0] : connection.Locations[1];
                        var unlockEvent =
                            EventSet.PrefabList.Find(ep => ep.UnlockPathEvent && ep.BiomeIdentifier == gateLocation.LevelData.Biome.Identifier) ??
                            EventSet.PrefabList.Find(ep => ep.UnlockPathEvent && string.IsNullOrEmpty(ep.BiomeIdentifier));

                        if (unlockEvent == null) { continue; }
                        if (string.IsNullOrEmpty(unlockEvent.UnlockPathFaction) || unlockEvent.UnlockPathFaction.Equals("location", StringComparison.OrdinalIgnoreCase))
                        {
                            if (location == null || gateLocation != location) { continue; }
                        }
                        else
                        {
                            if (faction == null || !faction.Prefab.Identifier.Equals(unlockEvent.UnlockPathFaction, StringComparison.OrdinalIgnoreCase)) { continue; }
                        }

                        if (unlockEvent != null)
                        {
                            Reputation unlockReputation = gateLocation.Reputation;
                            Faction unlockFaction = null;
                            if (!string.IsNullOrEmpty(unlockEvent.UnlockPathFaction))
                            {
                                unlockFaction = GameMain.GameSession.Campaign.Factions.Find(f => f.Prefab.Identifier == unlockEvent.UnlockPathFaction);
                                unlockReputation = unlockFaction?.Reputation;
                            }
                            float normalizedUnlockReputation = MathUtils.InverseLerp(unlockReputation.MinReputation, unlockReputation.MaxReputation, unlockEvent.UnlockPathReputation);
                            string unlockText = TextManager.GetWithVariables(
                                "lockedpathreputationrequirement",
                                new string[] { "[reputation]", "[biomename]" },
                                new string[] { Reputation.GetFormattedReputationText(normalizedUnlockReputation, unlockEvent.UnlockPathReputation, addColorTags: true), $"‖color:gui.orange‖{connection.LevelData.Biome.DisplayName}‖end‖" });
                            var unlockInfoPanel = new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.0f), reputationFrame.RectTransform, Anchor.BottomCenter) { MinSize = new Point(0, GUI.IntScale(30)), AbsoluteOffset = new Point(0, GUI.IntScale(3)) },
                                unlockText, style: "GUIButtonRound", textAlignment: Alignment.Center, textColor: GUI.Style.TextColor, parseRichText: true);
                            if (unlockInfoPanel.TextSize.X > unlockInfoPanel.Rect.Width * 0.7f)
                            {
                                unlockInfoPanel.Font = GUI.SmallFont;
                            }
                        }
                    }
                }
            }
        }

        private string GetHeaderText(bool gameOver, CampaignMode.TransitionType transitionType)
        {
            string locationName = Submarine.MainSub.AtEndExit ? endLocation?.Name : startLocation?.Name;

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
                        locationName = startLocation?.Name;
                        textTag = "RoundSummaryLeaving";
                        break;
                    case CampaignMode.TransitionType.ProgressToNextLocation:
                        locationName = endLocation?.Name;
                        textTag = "RoundSummaryProgress";
                        break;
                    case CampaignMode.TransitionType.ProgressToNextEmptyLocation:
                        locationName = endLocation?.Name;
                        textTag = "RoundSummaryProgressToEmptyLocation";
                        break;
                    case CampaignMode.TransitionType.ReturnToPreviousLocation:
                        locationName = startLocation?.Name;
                        textTag = "RoundSummaryReturn";
                        break;
                    case CampaignMode.TransitionType.ReturnToPreviousEmptyLocation:
                        locationName = startLocation?.Name;
                        textTag = "RoundSummaryReturnToEmptyLocation";
                        break;
                    default:
                        textTag = Submarine.MainSub.AtEndExit ? "RoundSummaryProgress" : "RoundSummaryReturn";
                        break;
                }
            }

            if (textTag == null) { return ""; }

            if (locationName == null)
            {
                DebugConsole.ThrowError($"Error while creating round summary: could not determine destination location. Start location: {startLocation?.Name ?? "null"}, end location: {endLocation?.Name ?? "null"}");
                locationName = "[UNKNOWN]";
            }

            string subName = string.Empty;
            SubmarineInfo currentOrPending = SubmarineSelection.CurrentOrPendingSubmarine();
            if (currentOrPending != null)
            {
                subName = currentOrPending.DisplayName;
            }

            return TextManager.GetWithVariables(textTag, new string[2] { "[sub]", "[location]" }, new string[2] { subName, locationName });            
        }

        private GUIListBox CreateCrewList(GUIComponent parent, IEnumerable<CharacterInfo> characterInfos)
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

            jobButton.TextBlock.Font = characterButton.TextBlock.Font = statusButton.TextBlock.Font = GUI.HotkeyFont;
            jobButton.CanBeFocused = characterButton.CanBeFocused = statusButton.CanBeFocused = false;
            jobButton.TextBlock.ForceUpperCase = characterButton.TextBlock.ForceUpperCase = statusButton.ForceUpperCase = true;

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

            foreach (CharacterInfo characterInfo in characterInfos)
            {
                if (characterInfo == null) { continue; }
                CreateCharacterElement(characterInfo, crewList);
            }

            return crewList;
        }

        private void CreateCharacterElement(CharacterInfo characterInfo, GUIListBox listBox)
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
                ToolBox.LimitString(characterInfo.Name, GUI.Font, characterColumnWidth), textAlignment: Alignment.Center, textColor: characterInfo.Job.Prefab.UIColor);

            string statusText = TextManager.Get("StatusOK");
            Color statusColor = GUI.Style.Green;

            Character character = characterInfo.Character;
            if (character == null || character.IsDead)
            {
                if (character == null && characterInfo.IsNewHire && characterInfo.CauseOfDeath == null)
                {
                    statusText = TextManager.Get("CampaignCrew.NewHire");
                    statusColor = GUI.Style.Blue;
                }
                else if (characterInfo.CauseOfDeath == null)
                {
                    statusText = TextManager.Get("CauseOfDeathDescription.Unknown");
                    statusColor = Color.DarkRed;
                }
                else if (characterInfo.CauseOfDeath.Type == CauseOfDeathType.Affliction && characterInfo.CauseOfDeath.Affliction == null)
                {
                    string errorMsg = "Character \"" + characterInfo.Name + "\" had an invalid cause of death (the type of the cause of death was Affliction, but affliction was not specified).";
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("RoundSummary:InvalidCauseOfDeath", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    statusText = TextManager.Get("CauseOfDeathDescription.Unknown");
                    statusColor = GUI.Style.Red;
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
                ToolBox.LimitString(statusText, GUI.Font, characterColumnWidth), textAlignment: Alignment.Center, textColor: statusColor);
        }

        private GUIFrame CreateReputationElement(GUIComponent parent, 
            string name, float reputation, float normalizedReputation, float initialReputation,
            string shortDescription, string fullDescription, Sprite icon, Sprite backgroundPortrait, Color iconColor)
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

            var factionInfoHorizontal = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), factionFrame.RectTransform, Anchor.Center), childAnchor: Anchor.CenterLeft, isHorizontal: true)
            {
                AbsoluteSpacing = GUI.IntScale(5),
                Stretch = true
            };

            var factionTextContent = new GUILayoutGroup(new RectTransform(Vector2.One, factionInfoHorizontal.RectTransform))
            {
                AbsoluteSpacing = GUI.IntScale(10),
                Stretch = true
            };
            var factionIcon = new GUIImage(new RectTransform(new Point((int)(factionInfoHorizontal.Rect.Height * 0.7f)), factionInfoHorizontal.RectTransform, scaleBasis: ScaleBasis.Smallest), icon, scaleToFit: true)
            {
                Color = iconColor
            };
            factionInfoHorizontal.Recalculate();

            var header = new GUITextBlock(new RectTransform(new Point(factionTextContent.Rect.Width, GUI.IntScale(40)), factionTextContent.RectTransform),
                name, font: GUI.SubHeadingFont)
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
                onDraw: (sb, customComponent) => DrawReputationBar(sb, customComponent.Rect, normalizedReputation));

            string reputationText = Reputation.GetFormattedReputationText(normalizedReputation, reputation, addColorTags: true);
            int reputationChange = (int)Math.Round(reputation - initialReputation);
            if (Math.Abs(reputationChange) > 0)
            {
                string changeText = $"{(reputationChange > 0 ? "+" : "") + reputationChange}";
                string colorStr = XMLExtensions.ColorToString(reputationChange > 0 ? GUI.Style.Green : GUI.Style.Red);
                var rtData = RichTextData.GetRichTextData($"{reputationText} (‖color:{colorStr}‖{changeText}‖color:end‖)", out string sanitizedText);
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), sliderHolder.RectTransform),
                    rtData, sanitizedText,
                    textAlignment: Alignment.CenterLeft, font: GUI.SubHeadingFont);
            }
            else
            {
                new GUITextBlock(new RectTransform(new Vector2(0.5f, 1.0f), sliderHolder.RectTransform),
                    reputationText,
                    textAlignment: Alignment.CenterLeft, font: GUI.SubHeadingFont, parseRichText: true);
            }

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.0f), factionTextContent.RectTransform) { MinSize = new Point(0, GUI.IntScale(5)) }, style: null);

            var factionDescription = new GUITextBlock(new RectTransform(new Vector2(0.8f, 0.6f), factionTextContent.RectTransform),
                shortDescription, font: GUI.SmallFont, wrap: true)
            {
                UserData = "description",
                Padding = Vector4.Zero
            };
            if (shortDescription != fullDescription && !string.IsNullOrEmpty(fullDescription))
            {
                factionDescription.ToolTip = fullDescription;
            }

            //spacing
            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.0f), factionTextContent.RectTransform) { MinSize = new Point(0, GUI.IntScale(5)) }, style: null);

            factionInfoHorizontal.Recalculate();
            factionTextContent.Recalculate();

            return factionFrame;
        }

        public static void DrawReputationBar(SpriteBatch sb, Rectangle rect, float normalizedReputation)
        {
            int segmentWidth = rect.Width / 5;
            rect.Width = segmentWidth * 5;
            for (int i = 0; i < 5; i++)
            {
                GUI.DrawRectangle(sb, new Rectangle(rect.X + (segmentWidth * i), rect.Y, segmentWidth, rect.Height), Reputation.GetReputationColor(i / 5.0f), isFilled: true);
                GUI.DrawRectangle(sb, new Rectangle(rect.X + (segmentWidth * i), rect.Y, segmentWidth, rect.Height), GUI.Style.ColorInventoryBackground, isFilled: false);
            }
            GUI.DrawRectangle(sb, rect, GUI.Style.ColorInventoryBackground, isFilled: false);

            GUI.Arrow.Draw(sb, new Vector2(rect.X + rect.Width * normalizedReputation, rect.Y), GUI.Style.ColorInventoryBackground, scale: GUI.Scale, spriteEffect: SpriteEffects.FlipVertically);
            GUI.Arrow.Draw(sb, new Vector2(rect.X + rect.Width * normalizedReputation, rect.Y), GUI.Style.TextColor, scale: GUI.Scale * 0.8f, spriteEffect: SpriteEffects.FlipVertically);

            GUI.DrawString(sb, new Vector2(rect.X, rect.Bottom), "-100", GUI.Style.TextColor, font: GUI.SmallFont);
            Vector2 textSize = GUI.SmallFont.MeasureString("100");
            GUI.DrawString(sb, new Vector2(rect.Right - textSize.X, rect.Bottom), "100", GUI.Style.TextColor, font: GUI.SmallFont);
        }
    }
}
