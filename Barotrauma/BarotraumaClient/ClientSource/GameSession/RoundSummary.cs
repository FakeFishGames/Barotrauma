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
        private readonly Mission selectedMission;
        private readonly Location startLocation, endLocation;

        private readonly GameMode gameMode;

        private readonly float initialLocationReputation;
        private readonly Dictionary<Faction, float> initialFactionReputations = new Dictionary<Faction, float>();

        public GUILayoutGroup ButtonArea { get; private set; }

        public GUIButton ContinueButton { get; private set; }

        public GUIComponent Frame { get; private set; }



        public RoundSummary(SubmarineInfo sub, GameMode gameMode, Mission selectedMission, Location startLocation, Location endLocation)
        {
            this.sub = sub;
            this.gameMode = gameMode;
            this.selectedMission = selectedMission;
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

            GUIFrame crewFrame = new GUIFrame(new RectTransform(new Vector2(0.35f, 0.55f), background.RectTransform, Anchor.TopCenter, minSize: new Point(minWidth, minHeight)));
            GUIFrame crewFrameInner = new GUIFrame(new RectTransform(new Point(crewFrame.Rect.Width - padding * 2, crewFrame.Rect.Height - padding * 2), crewFrame.RectTransform, Anchor.Center), style: "InnerFrame");

            var crewContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), crewFrameInner.RectTransform, Anchor.Center))
            {
                Stretch = true
            };

            var crewHeader = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), crewContent.RectTransform),
                TextManager.Get("crew"), textAlignment: Alignment.TopLeft, font: GUI.SubHeadingFont);
            crewHeader.RectTransform.MinSize = new Point(0, GUI.IntScale(crewHeader.Rect.Height * 2.0f));

            CreateCrewList(crewContent, gameSession.CrewManager.GetCharacterInfos().Where(c => c.TeamID != Character.TeamType.Team2));

            //another crew frame for the 2nd team in combat missions
            if (gameSession.Mission is CombatMission)
            {
                crewHeader.Text = CombatMission.GetTeamName(Character.TeamType.Team1);
                GUIFrame crewFrame2 = new GUIFrame(new RectTransform(new Vector2(0.35f, 0.55f), background.RectTransform, Anchor.TopCenter, minSize: new Point(minWidth, minHeight)));
                rightPanels.Add(crewFrame2);
                GUIFrame crewFrameInner2 = new GUIFrame(new RectTransform(new Point(crewFrame2.Rect.Width - padding * 2, crewFrame2.Rect.Height - padding * 2), crewFrame2.RectTransform, Anchor.Center), style: "InnerFrame");
                var crewContent2 = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), crewFrameInner2.RectTransform, Anchor.Center))
                {
                    Stretch = true
                };
                var crewHeader2 = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), crewContent2.RectTransform),
                    CombatMission.GetTeamName(Character.TeamType.Team2), textAlignment: Alignment.TopLeft, font: GUI.SubHeadingFont);
                crewHeader2.RectTransform.MinSize = new Point(0, GUI.IntScale(crewHeader2.Rect.Height * 2.0f));
                CreateCrewList(crewContent2, gameSession.CrewManager.GetCharacterInfos().Where(c => c.TeamID == Character.TeamType.Team2));
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

                GUIListBox reputationList = new GUIListBox(new RectTransform(Vector2.One, reputationContent.RectTransform))
                {
                    Padding = new Vector4(2, 5, 0, 0)
                };
                reputationList.ContentBackground.Color = Color.Transparent;

                if (startLocation.Type.HasOutpost && startLocation.Reputation != null)
                {
                    var iconStyle = GUI.Style.GetComponentStyle("LocationReputationIcon");
                    CreateReputationElement(
                        reputationList.Content,
                        startLocation.Name,
                        startLocation.Reputation.Value, startLocation.Reputation.NormalizedValue, initialLocationReputation,
                        startLocation.Type.Name, "",
                        iconStyle?.GetDefaultSprite(), startLocation.Type.GetPortrait(0), iconStyle?.Color ?? Color.White);
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
                    CreateReputationElement(
                        reputationList.Content,
                        faction.Prefab.Name,
                        faction.Reputation.Value, faction.Reputation.NormalizedValue, initialReputation,
                        faction.Prefab.ShortDescription, faction.Prefab.Description,
                        faction.Prefab.Icon, faction.Prefab.BackgroundPortrait, faction.Prefab.IconColor);
                }

                float otherElementHeight = 0.0f;
                float maxDescriptionHeight = 0.0f;
                foreach (GUIComponent child in reputationList.Content.Children)
                {
                    var descriptionElement = child.FindChild("description", recursive: true) as GUITextBlock;
                    maxDescriptionHeight = Math.Max(maxDescriptionHeight, descriptionElement.TextSize.Y * 1.1f);
                    otherElementHeight = Math.Max(otherElementHeight, descriptionElement.Parent.Rect.Height - descriptionElement.TextSize.Y);
                }
                foreach (GUIComponent child in reputationList.Content.Children)
                {
                    var descriptionElement = child.FindChild("description", recursive: true) as GUITextBlock;
                    descriptionElement.RectTransform.MaxSize = new Point(int.MaxValue, (int)(maxDescriptionHeight));
                    child.RectTransform.MaxSize = new Point(int.MaxValue, (int)((maxDescriptionHeight + otherElementHeight) * 1.2f));
                    (descriptionElement?.Parent as GUILayoutGroup).Recalculate();
                }
            }

            //mission panel -------------------------------------------------------------------------------

            GUIFrame missionframe = new GUIFrame(new RectTransform(new Vector2(0.39f, 0.22f), background.RectTransform, Anchor.TopCenter, minSize: new Point(minWidth, minHeight / 4)));
            GUIFrame missionframeInner = new GUIFrame(new RectTransform(new Point(missionframe.Rect.Width - padding * 2, missionframe.Rect.Height - padding * 2), missionframe.RectTransform, Anchor.Center), style: "InnerFrame");

            var missionContent = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), missionframeInner.RectTransform, Anchor.Center))
            {
                RelativeSpacing = 0.05f,
                Stretch = true
            };

            if (!string.IsNullOrWhiteSpace(endMessage))
            {
                var endText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionContent.RectTransform),
                    TextManager.GetServerMessage(endMessage), wrap: true);
                endText.RectTransform.MinSize = new Point(0, endText.Rect.Height);
                var line = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.1f), missionContent.RectTransform), style: "HorizontalLine");
                line.RectTransform.NonScaledSize = new Point(line.Rect.Width, GUI.IntScale(5.0f));
            }

            var missionContentHorizontal = new GUILayoutGroup(new RectTransform(Vector2.One, missionContent.RectTransform), childAnchor: Anchor.TopLeft, isHorizontal: true)
            {
                RelativeSpacing = 0.025f,
                Stretch = true
            };

            Mission displayedMission = selectedMission ?? startLocation.SelectedMission;
            string missionMessage = "";
            GUIImage missionIcon;
            if (displayedMission != null)
            {
                missionMessage =
                    displayedMission == selectedMission ?
                    displayedMission.Completed ? displayedMission.SuccessMessage : displayedMission.FailureMessage : 
                    displayedMission.Description;
                missionIcon = new GUIImage(new RectTransform(new Point(missionContentHorizontal.Rect.Height), missionContentHorizontal.RectTransform), displayedMission.Prefab.Icon, scaleToFit: true)
                {
                    Color = displayedMission.Prefab.IconColor
                };
                if (displayedMission == selectedMission)
                {
                    new GUIImage(new RectTransform(Vector2.One, missionIcon.RectTransform), displayedMission.Completed ? "MissionCompletedIcon" : "MissionFailedIcon", scaleToFit: true);                    
                }
            }
            else
            {
                missionIcon = new GUIImage(new RectTransform(new Point(missionContentHorizontal.Rect.Height), missionContentHorizontal.RectTransform), style: "NoMissionIcon", scaleToFit: true);
            }
            var missionTextContent = new GUILayoutGroup(new RectTransform(Vector2.One, missionContentHorizontal.RectTransform))
            {
                RelativeSpacing = 0.05f
            };
            missionContentHorizontal.Recalculate();
            missionContent.Recalculate();
            missionIcon.RectTransform.MinSize = new Point(0, missionContentHorizontal.Rect.Height);
            missionTextContent.RectTransform.MaxSize = new Point(int.MaxValue, missionIcon.Rect.Width);

            GUITextBlock missionDescription = null;
            if (displayedMission == null)
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextContent.RectTransform),
                    TextManager.Get("nomission"), font: GUI.LargeFont);
            }
            else
            {
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextContent.RectTransform),
                    TextManager.AddPunctuation(':', TextManager.Get("Mission"), displayedMission.Name), font: GUI.SubHeadingFont);
                missionDescription = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextContent.RectTransform),
                    missionMessage, wrap: true);
                if (displayedMission == selectedMission && displayedMission.Completed)
                {
                    string rewardText = TextManager.GetWithVariable("currencyformat", "[credits]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", displayedMission.Reward));
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextContent.RectTransform),
                        TextManager.GetWithVariable("MissionReward", "[reward]", rewardText));
                }
            }

            ButtonArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), missionContent.RectTransform, Anchor.BottomCenter), isHorizontal: true, childAnchor: Anchor.BottomRight)
            {
                IgnoreLayoutGroups = true,
                RelativeSpacing = 0.025f
            };

            ContinueButton = new GUIButton(new RectTransform(new Vector2(0.25f, 1.0f), ButtonArea.RectTransform), TextManager.Get("Close"));
            ButtonArea.RectTransform.NonScaledSize = new Point(ButtonArea.Rect.Width, ContinueButton.Rect.Height);
            ButtonArea.RectTransform.IsFixedSize = true;

            missionContent.Recalculate();
            //description overlapping with the buttons -> switch to small font
            if (missionDescription != null && missionDescription.Rect.Y + missionDescription.TextSize.Y > ButtonArea.Rect.Y)
            {
                missionDescription.Font = GUI.Style.SmallFont;
                //still overlapping -> shorten the text
                if (missionDescription.Rect.Y + missionDescription.TextSize.Y > ButtonArea.Rect.Y && missionDescription.WrappedText.Contains('\n'))
                {
                    missionDescription.ToolTip = missionDescription.Text;
                    missionDescription.Text = missionDescription.WrappedText.Split('\n').First() + "...";
                }
            }

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

        private string GetHeaderText(bool gameOver, CampaignMode.TransitionType transitionType)
        {
            string locationName = Submarine.MainSub.AtEndPosition ? endLocation?.Name : startLocation?.Name;

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
                    case CampaignMode.TransitionType.ProgressToNextEmptyLocation:
                        locationName = endLocation?.Name;
                        textTag = "RoundSummaryProgress";
                        break;
                    case CampaignMode.TransitionType.ReturnToPreviousLocation:
                    case CampaignMode.TransitionType.ReturnToPreviousEmptyLocation:
                        locationName = startLocation?.Name;
                        textTag = "RoundSummaryReturn";
                        break;
                    default:
                        textTag = Submarine.MainSub.AtEndPosition ? "RoundSummaryProgress" : "RoundSummaryReturn";
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
                Padding = new Vector4(2, 5, 0, 0),
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

        private void CreateReputationElement(GUIComponent parent, 
            string name, float reputation, float normalizedReputation, float initialReputation,
            string shortDescription, string fullDescription, Sprite icon, Sprite backgroundPortrait, Color iconColor)
        {
            var factionFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.3f), parent.RectTransform), style: null);

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
                RelativeSpacing = 0.02f,
                Stretch = true
            };

            var factionTextContent = new GUILayoutGroup(new RectTransform(Vector2.One, factionInfoHorizontal.RectTransform))
            {
                RelativeSpacing = 0.05f,
                Stretch = true
            };
            var factionIcon = new GUIImage(new RectTransform(new Point((int)(factionInfoHorizontal.Rect.Height * 0.7f)), factionInfoHorizontal.RectTransform, scaleBasis: ScaleBasis.Smallest), icon, scaleToFit: true)
            {
                Color = iconColor
            };
            factionInfoHorizontal.Recalculate();

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), factionTextContent.RectTransform),
                name, font: GUI.SubHeadingFont)
            {
                Padding = Vector4.Zero
            };
            var factionDescription = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.6f), factionTextContent.RectTransform),
                shortDescription, font: GUI.SmallFont, wrap: true)
            {
                UserData = "description",
                Padding = Vector4.Zero
            };
            if (shortDescription != fullDescription && !string.IsNullOrEmpty(fullDescription))
            {
                factionDescription.ToolTip = fullDescription;
            }

            var sliderHolder = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.1f), factionTextContent.RectTransform),
                childAnchor: Anchor.CenterLeft, isHorizontal: true)
            {
                RelativeSpacing = 0.05f,
                Stretch = true
            };
            sliderHolder.RectTransform.MaxSize = new Point(int.MaxValue, GUI.IntScale(25.0f));
            factionTextContent.Recalculate();

            new GUICustomComponent(new RectTransform(new Vector2(0.8f, 1.0f), sliderHolder.RectTransform),
                onDraw: (sb, customComponent) => DrawReputationBar(sb, customComponent.Rect, normalizedReputation));

            string reputationText = ((int)Math.Round(reputation)).ToString();
            int reputationChange = (int)Math.Round( reputation - initialReputation);
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
                    textAlignment: Alignment.CenterLeft, font: GUI.SubHeadingFont);
            }
        }

        public static void DrawReputationBar(SpriteBatch sb, Rectangle rect, float normalizedReputation)
        {
            GUI.DrawRectangle(sb, rect, GUI.Style.ColorInventoryBackground, isFilled: true);
            if (normalizedReputation < 0.5f)
            {
                int barWidth = (int)((0.5f - normalizedReputation) * rect.Width);
                GUI.DrawRectangle(sb, new Rectangle(rect.Center.X - barWidth, rect.Y, barWidth, rect.Height), GUI.Style.Red, isFilled: true);
            }
            else if (normalizedReputation > 0.5f)
            {
                int barWidth = (int)((normalizedReputation - 0.5f) * rect.Width);
                GUI.DrawRectangle(sb, new Rectangle(rect.Center.X, rect.Y, barWidth, rect.Height), GUI.Style.Green, isFilled: true);
            }
            GUI.DrawLine(sb, new Vector2(rect.Center.X, rect.Y - 2), new Vector2(rect.Center.X, rect.Bottom + 2), GUI.Style.TextColor);
        }
    }
}
