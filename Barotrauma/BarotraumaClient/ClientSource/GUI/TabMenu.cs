using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Linq;
using Barotrauma.Networking;
using System.Globalization;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class TabMenu
    {
        public static bool PendingChanges = false;

        private static bool initialized = false;

        private static UISprite spectateIcon, disconnectedIcon;
        private static Sprite ownerIcon, moderatorIcon;

        public enum InfoFrameTab { Crew, Mission, Reputation, Traitor, Submarine, Talents };
        public static InfoFrameTab SelectedTab { get; private set; }
        private GUIFrame infoFrame, contentFrame;

        private readonly List<GUIButton> tabButtons = new List<GUIButton>();
        private GUIFrame infoFrameHolder;
        private List<LinkedGUI> linkedGUIList;
        private GUIListBox logList;
        private GUIListBox[] crewListArray;
        private float sizeMultiplier = 1f;

        private IEnumerable<Character> crew;
        private List<CharacterTeamType> teamIDs;
        private const string inLobbyString = "\u2022 \u2022 \u2022";

        private GUIFrame pendingChangesFrame = null;

        public static Color OwnCharacterBGColor = Color.Gold * 0.7f;
        private bool isTransferMenuOpen;
        private bool isSending;
        private GUIComponent transferMenu;
        private GUIButton transferMenuButton;
        private float transferMenuOpenState;
        private bool transferMenuStateCompleted;

        private class LinkedGUI
        {
            private const ushort lowPingThreshold = 100;
            private const ushort mediumPingThreshold = 200;

            private ushort currentPing;
            private readonly Client client;
            private readonly Character character;
            private readonly bool hasCharacter;
            private readonly GUITextBlock textBlock;
            private readonly GUIFrame frame;

            public LinkedGUI(Client client, GUIFrame frame, bool hasCharacter, GUITextBlock textBlock)
            {
                this.client = client;
                this.textBlock = textBlock;
                this.frame = frame;
                this.hasCharacter = hasCharacter;
            }

            public LinkedGUI(Character character, GUIFrame frame, bool hasCharacter, GUITextBlock textBlock)
            {
                this.character = character;
                this.textBlock = textBlock;
                this.frame = frame;
                this.hasCharacter = hasCharacter;
            }

            public bool HasMultiplayerCharacterChanged()
            {
                if (client == null) return false;
                bool characterState = client.Character != null;
                if (characterState && client.Character.IsDead) characterState = false;
                return hasCharacter != characterState;
            }

            public bool HasMultiplayerCharacterDied()
            {
                if (client == null || !hasCharacter || client.Character == null) return false;
                return client.Character.IsDead;
            }

            public bool HasAICharacterDied()
            {
                if (character == null) return false;
                return character.IsDead;
            }

            public void TryPingRefresh()
            {
                if (client == null) return;
                if (currentPing == client.Ping) return;
                currentPing = client.Ping;
                textBlock.Text = currentPing.ToString();
                textBlock.TextColor = GetPingColor();
            }

            private Color GetPingColor()
            {
                if (currentPing < lowPingThreshold)
                {
                    return GUIStyle.Green;
                }
                else if (currentPing < mediumPingThreshold)
                {
                    return GUIStyle.Yellow;
                }
                else
                {
                    return GUIStyle.Red;
                }
            }

            public void Remove(GUIFrame parent)
            {
                parent.RemoveChild(frame);
            }
        }

        public void Initialize()
        {
            spectateIcon = GUIStyle.GetComponentStyle("SpectateIcon").Sprites[GUIComponent.ComponentState.None][0];
            disconnectedIcon = GUIStyle.GetComponentStyle("DisconnectedIcon").Sprites[GUIComponent.ComponentState.None][0];
            ownerIcon = GUIStyle.GetComponentStyle("OwnerIcon").GetDefaultSprite();
            moderatorIcon = GUIStyle.GetComponentStyle("ModeratorIcon").GetDefaultSprite();
            initialized = true;
        }

        public TabMenu()
        {
            if (!initialized) { Initialize(); }
            CreateInfoFrame(SelectedTab);
            SelectInfoFrameTab(SelectedTab);
        }

        public void Update(float deltaTime)
        {
            float menuOpenSpeed = deltaTime * 10f;
            if (isTransferMenuOpen)
            {
                if (transferMenuStateCompleted)
                {
                    transferMenuOpenState = transferMenuOpenState < 0.25f ? Math.Min(0.25f, transferMenuOpenState + (menuOpenSpeed / 2f)) : 0.25f;
                }
                else
                {
                    if (transferMenuOpenState > 0.15f)
                    {
                        transferMenuStateCompleted = false;
                        transferMenuOpenState = Math.Max(0.15f, transferMenuOpenState - menuOpenSpeed);
                    }
                    else
                    {
                        transferMenuStateCompleted = true;
                    }
                }
            }
            else
            {
                transferMenuStateCompleted = false;
                if (transferMenuOpenState < 1f)
                {
                    transferMenuOpenState = Math.Min(1f, transferMenuOpenState + menuOpenSpeed);
                }
            }

            if (transferMenu != null && transferMenuButton != null)
            {
                int pos = (int)(transferMenuOpenState * -transferMenu.Rect.Height);
                transferMenu.RectTransform.AbsoluteOffset = new Point(0, pos);
                transferMenuButton.RectTransform.AbsoluteOffset = new Point(0, -pos - transferMenu.Rect.Height);
            }
            GameSession.UpdateTalentNotificationIndicator(talentPointNotification);
            if (Character.Controlled is { } controlled && talentResetButton != null && talentApplyButton != null)
            {
                int talentCount = selectedTalents.Count - controlled.Info.GetUnlockedTalentsInTree().Count();
                talentResetButton.Enabled = talentApplyButton.Enabled = talentCount > 0;
                if (talentApplyButton.Enabled && talentApplyButton.FlashTimer <= 0.0f)
                {
                    talentApplyButton.Flash(GUIStyle.Orange);
                }
            }

            if (SelectedTab != InfoFrameTab.Crew) { return; }
            if (linkedGUIList == null) { return; }

            if (GameMain.IsMultiplayer)
            {
                for (int i = 0; i < linkedGUIList.Count; i++)
                {
                    linkedGUIList[i].TryPingRefresh();
                    if (linkedGUIList[i].HasMultiplayerCharacterChanged() || linkedGUIList[i].HasMultiplayerCharacterDied() || linkedGUIList[i].HasAICharacterDied())
                    {
                        RemoveCurrentElements();
                        CreateMultiPlayerList(true);
                        return;
                    }
                }
            }
            else
            {
                for (int i = 0; i < linkedGUIList.Count; i++)
                {
                    if (linkedGUIList[i].HasAICharacterDied())
                    {
                        RemoveCurrentElements();
                        CreateSinglePlayerList(true);
                    }
                }
            }
        }

        public void AddToGUIUpdateList()
        {
            infoFrame?.AddToGUIUpdateList(order: 1);
            NetLobbyScreen.JobInfoFrame?.AddToGUIUpdateList();
        }

        public static void OnRoundEnded()
        {
            storedMessages.Clear();
            PendingChanges = false;
        }

        private void CreateInfoFrame(InfoFrameTab selectedTab)
        {
            tabButtons.Clear();

            infoFrame = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: null);
            new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, infoFrame.RectTransform, Anchor.Center), style: "GUIBackgroundBlocker");

            //this used to be a switch expression but i changed it because it killed enc :(
            //now it's not even a switch statement anymore :(
            Vector2 contentFrameSize = new Vector2(0.45f, 0.667f);
            contentFrame = new GUIFrame(new RectTransform(contentFrameSize, infoFrame.RectTransform, Anchor.TopCenter, Pivot.TopCenter) { RelativeOffset = new Vector2(0.0f, 0.12f) });

            var horizontalLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.958f, 0.943f), contentFrame.RectTransform, Anchor.TopCenter, Pivot.TopCenter) { AbsoluteOffset = new Point(0, GUI.IntScale(25f)) }, isHorizontal: true)
            {
                RelativeSpacing = 0.01f
            };

            var buttonArea = new GUILayoutGroup(new RectTransform(new Vector2(0.07f, 1f), parent: horizontalLayoutGroup.RectTransform), isHorizontal: false)
            {
                AbsoluteSpacing = GUI.IntScale(5f)
            };
            var innerLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.92f, 1f), horizontalLayoutGroup.RectTransform))
            {
                RelativeSpacing = 0.01f,
                Stretch = true
            };

            float absoluteSpacing = innerLayoutGroup.RelativeSpacing * innerLayoutGroup.Rect.Height;
            int multiplier = GameMain.GameSession?.GameMode is CampaignMode ? 2 : 1;
            int infoFrameHolderHeight = Math.Min((int)(0.97f * innerLayoutGroup.Rect.Height), (int)(innerLayoutGroup.Rect.Height - multiplier * (GUI.IntScale(15f) + absoluteSpacing)));
            infoFrameHolder = new GUIFrame(new RectTransform(new Point(innerLayoutGroup.Rect.Width, infoFrameHolderHeight), parent: innerLayoutGroup.RectTransform), style: null);

            GUIButton createTabButton(InfoFrameTab tab, string textTag)
            {
                var newButton = new GUIButton(new RectTransform(Vector2.One, buttonArea.RectTransform, scaleBasis: ScaleBasis.BothWidth), style: $"InfoFrameTabButton.{tab}")
                {
                    UserData = tab,
                    ToolTip = TextManager.Get(textTag),
                    OnClicked = (btn, userData) => { SelectInfoFrameTab((InfoFrameTab)userData); return true; }
                };
                tabButtons.Add(newButton);
                return newButton;
            }

            var crewButton = createTabButton(InfoFrameTab.Crew, "crew");

            if (!(GameMain.GameSession?.GameMode is TestGameMode))
            {
                createTabButton(InfoFrameTab.Mission, "mission");
            }

            if (GameMain.GameSession?.GameMode is CampaignMode campaignMode)
            {
                var reputationButton = createTabButton(InfoFrameTab.Reputation, "reputation");

                var balanceFrame = new GUIFrame(new RectTransform(new Point(innerLayoutGroup.Rect.Width, innerLayoutGroup.Rect.Height - infoFrameHolderHeight), parent: innerLayoutGroup.RectTransform), style: "InnerFrame");
                GUITextBlock balanceText = new GUITextBlock(new RectTransform(Vector2.One, balanceFrame.RectTransform), string.Empty, textAlignment: Alignment.Right);
                GUIFrame bottomDisclaimerFrame = new GUIFrame(new RectTransform(new Vector2(contentFrameSize.X, 0.1f), infoFrame.RectTransform)
                {
                    AbsoluteOffset = new Point(contentFrame.Rect.X, contentFrame.Rect.Bottom + GUI.IntScale(8))
                }, style: null);

                pendingChangesFrame = new GUIFrame(new RectTransform(Vector2.One, bottomDisclaimerFrame.RectTransform, Anchor.Center), style: null);

                if (GameMain.NetLobbyScreen?.CampaignCharacterDiscarded ?? false)
                {
                    NetLobbyScreen.CreateChangesPendingFrame(pendingChangesFrame);
                }

                SetBalanceText(balanceText, campaignMode.Bank.Balance);
                campaignMode.OnMoneyChanged.RegisterOverwriteExisting(nameof(CreateInfoFrame).ToIdentifier(), e =>
                {
                    if (e.Wallet != campaignMode.Bank) { return; }
                    SetBalanceText(balanceText, e.Wallet.Balance);
                });

                static void SetBalanceText(GUITextBlock text, int balance)
                {
                    text.Text = TextManager.GetWithVariable("bankbalanceformat", "[money]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", balance));
                }
            }
            else
            {
                bool isTraitor = GameMain.Client?.Character?.IsTraitor ?? false;
                if (isTraitor && GameMain.Client.TraitorMission != null)
                {
                    var traitorButton = createTabButton(InfoFrameTab.Traitor, "tabmenu.traitor");
                }
            }

            var submarineButton = createTabButton(InfoFrameTab.Submarine, "submarine");

            var talentsButton = createTabButton(InfoFrameTab.Talents, "tabmenu.character");
            talentsButton.OnAddedToGUIUpdateList += (component) =>
            {
                talentsButton.Enabled = Character.Controlled?.Info != null;
                if (!talentsButton.Enabled && selectedTab == InfoFrameTab.Talents)
                {
                    SelectInfoFrameTab(InfoFrameTab.Crew);
                }
            };

            talentPointNotification = GameSession.CreateTalentIconNotification(talentsButton);
        }

        public void SelectInfoFrameTab(InfoFrameTab selectedTab)
        {
            SelectedTab = selectedTab;

            CreateInfoFrame(selectedTab);
            tabButtons.ForEach(tb => tb.Selected = (InfoFrameTab)tb.UserData == selectedTab);

            switch (selectedTab)
            {
                case InfoFrameTab.Crew:
                    CreateCrewListFrame(infoFrameHolder);
                    break;
                case InfoFrameTab.Mission:
                    CreateMissionInfo(infoFrameHolder);
                    break;
                case InfoFrameTab.Reputation:
                    if (GameMain.GameSession?.RoundSummary != null && GameMain.GameSession?.GameMode is CampaignMode campaignMode)
                    {
                        infoFrameHolder.ClearChildren();
                        GUIFrame reputationFrame = new GUIFrame(new RectTransform(Vector2.One, infoFrameHolder.RectTransform, Anchor.TopCenter), style: "GUIFrameListBox");
                        GameMain.GameSession.RoundSummary.CreateReputationInfoPanel(reputationFrame, campaignMode);
                    }
                    break;
                case InfoFrameTab.Traitor:
                    TraitorMissionPrefab traitorMission = GameMain.Client?.TraitorMission;
                    Character traitor = GameMain.Client?.Character;
                    if (traitor == null || traitorMission == null) { return; }
                    CreateTraitorInfo(infoFrameHolder, traitorMission, traitor);
                    break;
                case InfoFrameTab.Submarine:
                    CreateSubmarineInfo(infoFrameHolder, Submarine.MainSub);
                    break;
                case InfoFrameTab.Talents:
                    CreateTalentInfo(infoFrameHolder);
                    break;
            }
        }

        private const float jobColumnWidthPercentage = 0.138f;
        private const float characterColumnWidthPercentage = 0.656f;
        private const float pingColumnWidthPercentage = 0.206f;

        private int jobColumnWidth, characterColumnWidth, pingColumnWidth;

        private void CreateCrewListFrame(GUIFrame crewFrame)
        {
            crew = GameMain.GameSession?.CrewManager?.GetCharacters() ?? Array.Empty<Character>();
            teamIDs = crew.Select(c => c.TeamID).Distinct().ToList();

            // Show own team first when there's more than one team
            if (teamIDs.Count > 1 && GameMain.Client?.Character != null)
            {
                CharacterTeamType ownTeam = GameMain.Client.Character.TeamID;
                teamIDs = teamIDs.OrderBy(i => i != ownTeam).ThenBy(i => i).ToList();
            }

            if (!teamIDs.Any()) { teamIDs.Add(CharacterTeamType.None); }

            var content = new GUILayoutGroup(new RectTransform(Vector2.One, crewFrame.RectTransform));

            crewListArray = new GUIListBox[teamIDs.Count];
            GUILayoutGroup[] headerFrames = new GUILayoutGroup[teamIDs.Count];

            float nameHeight = 0.075f;

            Vector2 crewListSize = new Vector2(1f, 1f / teamIDs.Count - (teamIDs.Count > 1 ? nameHeight * 1.1f : 0f));
            for (int i = 0; i < teamIDs.Count; i++)
            {
                if (teamIDs.Count > 1)
                {
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, nameHeight), content.RectTransform), CombatMission.GetTeamName(teamIDs[i]), textColor: i == 0 ? GUIStyle.Green : GUIStyle.Orange) { ForceUpperCase = ForceUpperCase.Yes };
                }

                headerFrames[i] = new GUILayoutGroup(new RectTransform(Vector2.Zero, content.RectTransform, Anchor.TopLeft, Pivot.BottomLeft) { AbsoluteOffset = new Point(2, -1) }, isHorizontal: true)
                {
                    AbsoluteSpacing = 2,
                    UserData = i
                };

                GUIListBox crewList = new GUIListBox(new RectTransform(crewListSize, content.RectTransform))
                {
                    Padding = new Vector4(2, 5, 0, 0),
                    AutoHideScrollBar = false
                };
                crewList.UpdateDimensions();

                if (teamIDs.Count > 1)
                {
                    crewList.OnSelected = (component, obj) =>
                    {
                        for (int i = 0; i < crewListArray.Length; i++)
                        {
                            if (crewListArray[i] == crewList) continue;
                            crewListArray[i].Deselect();
                        }
                        SelectElement(component.UserData, crewList);
                        return true;
                    };
                }
                else
                {
                    crewList.OnSelected = (component, obj) =>
                    {
                        SelectElement(component.UserData, crewList);
                        return true;
                    };
                }

                crewListArray[i] = crewList;
            }

            for (int i = 0; i < teamIDs.Count; i++)
            {
                headerFrames[i].RectTransform.RelativeSize = new Vector2(1f - crewListArray[i].ScrollBar.Rect.Width / (float)crewListArray[i].Rect.Width, GUIStyle.HotkeyFont.Size / (float)crewFrame.RectTransform.Rect.Height * 1.5f);

                if (!GameMain.IsMultiplayer)
                {
                    CreateSinglePlayerListContentHolder(headerFrames[i]);
                }
                else
                {
                    CreateMultiPlayerListContentHolder(headerFrames[i]);
                }
            }

            crewFrame.RectTransform.AbsoluteOffset = new Point(0, (int)(headerFrames[0].Rect.Height * headerFrames.Length) - (teamIDs.Count > 1 ? GUI.IntScale(10f) : 0));

            float totalRelativeHeight = 0.0f;
            if (teamIDs.Count > 1) { totalRelativeHeight += teamIDs.Count * nameHeight; }
            headerFrames.ForEach(f => totalRelativeHeight += f.RectTransform.RelativeSize.Y);
            crewListArray.ForEach(f => totalRelativeHeight += f.RectTransform.RelativeSize.Y);
            if (totalRelativeHeight > 1.0f)
            {
                float heightOverflow = totalRelativeHeight - 1.0f;
                float heightToReduce = heightOverflow / crewListArray.Length;
                crewListArray.ForEach(l =>
                {
                    l.RectTransform.Resize(l.RectTransform.RelativeSize - new Vector2(0.0f, heightToReduce));
                    l.UpdateDimensions();
                });
            }

            if (GameMain.IsMultiplayer)
            {
                CreateMultiPlayerList(false);
                CreateMultiPlayerLogContent(crewFrame);
            }
            else
            {
                CreateSinglePlayerList(false);
            }
        }

        private void CreateSinglePlayerListContentHolder(GUILayoutGroup headerFrame)
        {
            GUIButton jobButton = new GUIButton(new RectTransform(new Vector2(0f, 1f), headerFrame.RectTransform), TextManager.Get("tabmenu.job"), style: "GUIButtonSmallFreeScale");
            GUIButton characterButton = new GUIButton(new RectTransform(new Vector2(0f, 1f), headerFrame.RectTransform), TextManager.Get("name"), style: "GUIButtonSmallFreeScale");

            sizeMultiplier = (headerFrame.Rect.Width - headerFrame.AbsoluteSpacing * (headerFrame.CountChildren - 1)) / (float)headerFrame.Rect.Width;

            jobButton.RectTransform.RelativeSize = new Vector2(jobColumnWidthPercentage * sizeMultiplier, 1f);
            characterButton.RectTransform.RelativeSize = new Vector2((1f - jobColumnWidthPercentage * sizeMultiplier) * sizeMultiplier, 1f);

            jobButton.TextBlock.Font = characterButton.TextBlock.Font = GUIStyle.HotkeyFont;
            jobButton.CanBeFocused = characterButton.CanBeFocused = false;
            jobButton.TextBlock.ForceUpperCase = characterButton.TextBlock.ForceUpperCase = ForceUpperCase.Yes;

            jobColumnWidth = jobButton.Rect.Width;
            characterColumnWidth = characterButton.Rect.Width;
        }

        private void CreateSinglePlayerList(bool refresh)
        {
            if (refresh)
            {
                crew = GameMain.GameSession.CrewManager.GetCharacters();
            }

            linkedGUIList = new List<LinkedGUI>();

            for (int i = 0; i < teamIDs.Count; i++)
            {
                foreach (Character character in crew.Where(c => c.TeamID == teamIDs[i]))
                {
                    CreateSinglePlayerCharacterElement(character, i);
                }
            }
        }

        private void CreateSinglePlayerCharacterElement(Character character, int i)
        {
            GUIFrame frame = new GUIFrame(new RectTransform(new Point(crewListArray[i].Content.Rect.Width, GUI.IntScale(33f)), crewListArray[i].Content.RectTransform), style: "ListBoxElement")
            {
                UserData = character,
                Color = (Character.Controlled == character) ? OwnCharacterBGColor : Color.Transparent
            };

            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.9f), frame.RectTransform, Anchor.Center), isHorizontal: true)
            {
                AbsoluteSpacing = 2
            };

            new GUICustomComponent(new RectTransform(new Point(jobColumnWidth, paddedFrame.Rect.Height), paddedFrame.RectTransform, Anchor.Center), onDraw: (sb, component) => character.Info.DrawJobIcon(sb, component.Rect))
            {
                CanBeFocused = false,
                HoverColor = Color.White,
                SelectedColor = Color.White
            };

            GUITextBlock characterNameBlock = new GUITextBlock(new RectTransform(new Point(characterColumnWidth, paddedFrame.Rect.Height), paddedFrame.RectTransform),
                ToolBox.LimitString(character.Info.Name, GUIStyle.Font, characterColumnWidth), textAlignment: Alignment.Center, textColor: character.Info.Job.Prefab.UIColor);

            linkedGUIList.Add(new LinkedGUI(character, frame, !character.IsDead, null));
        }

        private void CreateMultiPlayerListContentHolder(GUILayoutGroup headerFrame)
        {
            GUIButton jobButton = new GUIButton(new RectTransform(new Vector2(0f, 1f), headerFrame.RectTransform), TextManager.Get("tabmenu.job"), style: "GUIButtonSmallFreeScale");
            GUIButton characterButton = new GUIButton(new RectTransform(new Vector2(0f, 1f), headerFrame.RectTransform), TextManager.Get("name"), style: "GUIButtonSmallFreeScale");
            GUIButton pingButton = new GUIButton(new RectTransform(new Vector2(0f, 1f), headerFrame.RectTransform), TextManager.Get("serverlistping"), style: "GUIButtonSmallFreeScale");

            sizeMultiplier = (headerFrame.Rect.Width - headerFrame.AbsoluteSpacing * (headerFrame.CountChildren - 1)) / (float)headerFrame.Rect.Width;

            jobButton.RectTransform.RelativeSize = new Vector2(jobColumnWidthPercentage * sizeMultiplier, 1f);
            characterButton.RectTransform.RelativeSize = new Vector2(characterColumnWidthPercentage * sizeMultiplier, 1f);
            pingButton.RectTransform.RelativeSize = new Vector2(pingColumnWidthPercentage * sizeMultiplier, 1f);

            jobButton.TextBlock.Font = characterButton.TextBlock.Font = pingButton.TextBlock.Font = GUIStyle.HotkeyFont;
            jobButton.CanBeFocused = characterButton.CanBeFocused = pingButton.CanBeFocused = false;
            jobButton.TextBlock.ForceUpperCase = characterButton.TextBlock.ForceUpperCase = pingButton.ForceUpperCase = ForceUpperCase.Yes;

            jobColumnWidth = jobButton.Rect.Width;
            characterColumnWidth = characterButton.Rect.Width;
            pingColumnWidth = pingButton.Rect.Width;
        }

        private void CreateMultiPlayerList(bool refresh)
        {
            if (refresh)
            {
                crew = GameMain.GameSession.CrewManager.GetCharacters();
            }

            linkedGUIList = new List<LinkedGUI>();

            List<Client> connectedClients = GameMain.Client.ConnectedClients;

            for (int i = 0; i < teamIDs.Count; i++)
            {
                foreach (Character character in crew.Where(c => c.TeamID == teamIDs[i]))
                {
                    if (!(character is AICharacter) && connectedClients.Any(c => c.Character == null && c.Name == character.Name)) { continue; }
                    CreateMultiPlayerCharacterElement(character, GameMain.Client.PreviouslyConnectedClients.FirstOrDefault(c => c.Character == character), i);
                }
            }

            for (int j = 0; j < connectedClients.Count; j++)
            {
                Client client = connectedClients[j];
                if (!client.InGame || client.Character == null || client.Character.IsDead)
                {
                    CreateMultiPlayerClientElement(client);
                }
            }
        }

        private void CreateMultiPlayerCharacterElement(Character character, Client client, int i)
        {
            GUIFrame frame = new GUIFrame(new RectTransform(new Point(crewListArray[i].Content.Rect.Width, GUI.IntScale(33f)), crewListArray[i].Content.RectTransform), style: "ListBoxElement")
            {
                UserData = character,
                Color = (GameMain.NetworkMember != null && GameMain.Client.Character == character) ? OwnCharacterBGColor : Color.Transparent
            };

            frame.OnSecondaryClicked += (component, data) =>
            {
                NetLobbyScreen.CreateModerationContextMenu(client);
                return true;
            };

            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.9f), frame.RectTransform, Anchor.Center), isHorizontal: true)
            {
                AbsoluteSpacing = 2
            };

            new GUICustomComponent(new RectTransform(new Point(jobColumnWidth, paddedFrame.Rect.Height), paddedFrame.RectTransform, Anchor.Center), onDraw: (sb, component) => character.Info.DrawJobIcon(sb, component.Rect))
            {
                CanBeFocused = false,
                HoverColor = Color.White,
                SelectedColor = Color.White
            };

            if (client != null)
            {
                CreateNameWithPermissionIcon(client, paddedFrame);
                linkedGUIList.Add(new LinkedGUI(client, frame, true, new GUITextBlock(new RectTransform(new Point(pingColumnWidth, paddedFrame.Rect.Height), paddedFrame.RectTransform), client.Ping.ToString(), textAlignment: Alignment.Center)));
            }
            else
            {
                GUITextBlock characterNameBlock = new GUITextBlock(new RectTransform(new Point(characterColumnWidth, paddedFrame.Rect.Height), paddedFrame.RectTransform),
                    ToolBox.LimitString(character.Info.Name, GUIStyle.Font, characterColumnWidth), textAlignment: Alignment.Center, textColor: character.Info.Job.Prefab.UIColor);

                if (character is AICharacter)
                {
                    linkedGUIList.Add(new LinkedGUI(character, frame, !character.IsDead, new GUITextBlock(new RectTransform(new Point(pingColumnWidth, paddedFrame.Rect.Height), paddedFrame.RectTransform), TextManager.Get("tabmenu.bot"), textAlignment: Alignment.Center) { ForceUpperCase = ForceUpperCase.Yes }));
                }
                else
                {
                    linkedGUIList.Add(new LinkedGUI(client: null, frame, true, null));

                    new GUICustomComponent(new RectTransform(new Point(pingColumnWidth, paddedFrame.Rect.Height), paddedFrame.RectTransform, Anchor.Center), onDraw: (sb, component) => DrawDisconnectedIcon(sb, component.Rect))
                    {
                        CanBeFocused = false,
                        HoverColor = Color.White,
                        SelectedColor = Color.White
                    };
                }
            }
        }

        private void CreateMultiPlayerClientElement(Client client)
        {
            int teamIndex = GetTeamIndex(client);
            if (teamIndex == -1) teamIndex = 0;

            GUIFrame frame = new GUIFrame(new RectTransform(new Point(crewListArray[teamIndex].Content.Rect.Width, GUI.IntScale(33f)), crewListArray[teamIndex].Content.RectTransform), style: "ListBoxElement")
            {
                UserData = client,
                Color = Color.Transparent
            };

            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.9f), frame.RectTransform, Anchor.Center), isHorizontal: true)
            {
                AbsoluteSpacing = 2
            };

            new GUICustomComponent(new RectTransform(new Point(jobColumnWidth, paddedFrame.Rect.Height), paddedFrame.RectTransform, Anchor.Center),
                onDraw: (sb, component) => DrawNotInGameIcon(sb, component.Rect, client))
            {
                CanBeFocused = false,
                HoverColor = Color.White,
                SelectedColor = Color.White
            };

            CreateNameWithPermissionIcon(client, paddedFrame);
            linkedGUIList.Add(new LinkedGUI(client, frame, false, new GUITextBlock(new RectTransform(new Point(pingColumnWidth, paddedFrame.Rect.Height), paddedFrame.RectTransform), client.Ping.ToString(), textAlignment: Alignment.Center)));
        }

        private int GetTeamIndex(Client client)
        {
            if (teamIDs.Count <= 1) { return 0; }

            if (client.Character != null)
            {
                return teamIDs.IndexOf(client.Character.TeamID);
            }

            if (client.CharacterID != 0)
            {
                foreach (Character c in crew)
                {
                    if (client.CharacterID == c.ID)
                    {
                        return teamIDs.IndexOf(c.TeamID);
                    }
                }
            }
            else
            {
                foreach (Character c in crew)
                {
                    if (client.Name == c.Name)
                    {
                        return teamIDs.IndexOf(c.TeamID);
                    }
                }
            }

            return 0;
        }

        private void CreateNameWithPermissionIcon(Client client, GUILayoutGroup paddedFrame)
        {
            GUITextBlock characterNameBlock;
            Sprite permissionIcon = GetPermissionIcon(client);
            JobPrefab prefab = client.Character?.Info?.Job?.Prefab;
            Color nameColor = prefab != null ? prefab.UIColor : Color.White;

            if (permissionIcon != null)
            {
                Point iconSize = permissionIcon.SourceRect.Size;
                float characterNameWidthAdjustment = (iconSize.X + paddedFrame.AbsoluteSpacing) / characterColumnWidth;

                characterNameBlock = new GUITextBlock(new RectTransform(new Point(characterColumnWidth, paddedFrame.Rect.Height), paddedFrame.RectTransform),
                    ToolBox.LimitString(client.Name, GUIStyle.Font, (int)(characterColumnWidth - paddedFrame.Rect.Width * characterNameWidthAdjustment)), textAlignment: Alignment.Center, textColor: nameColor);

                float iconWidth = iconSize.X / (float)characterColumnWidth;
                int xOffset = (int)(jobColumnWidth + characterNameBlock.TextPos.X - GUIStyle.Font.MeasureString(characterNameBlock.Text).X / 2f - paddedFrame.AbsoluteSpacing - iconWidth * paddedFrame.Rect.Width);
                new GUIImage(new RectTransform(new Vector2(iconWidth, 1f), paddedFrame.RectTransform) { AbsoluteOffset = new Point(xOffset + 2, 0) }, permissionIcon) { IgnoreLayoutGroups = true };
            }
            else
            {
                characterNameBlock = new GUITextBlock(new RectTransform(new Point(characterColumnWidth, paddedFrame.Rect.Height), paddedFrame.RectTransform),
                    ToolBox.LimitString(client.Name, GUIStyle.Font, characterColumnWidth), textAlignment: Alignment.Center, textColor: nameColor);
            }

            if (client.Character != null && client.Character.IsDead)
            {
                characterNameBlock.Strikethrough = new GUITextBlock.StrikethroughSettings(null, GUI.IntScale(1f), GUI.IntScale(5f));
            }
        }

        private Sprite GetPermissionIcon(Client client)
        {
            if (GameMain.NetworkMember == null || client == null || !client.HasPermissions) return null;

            if (client.IsOwner) // Owner cannot be kicked
            {
                return ownerIcon;
            }
            else
            {
                return moderatorIcon;
            }
        }

        private void DrawNotInGameIcon(SpriteBatch spriteBatch, Rectangle area, Client client)
        {
            if (client.Spectating)
            {
                spectateIcon.Draw(spriteBatch, area, Color.White);
            }
            else if (client.Character != null && client.Character.IsDead)
            {
                if (client.Character.Info != null)
                {
                    client.Character.Info.DrawJobIcon(spriteBatch, area);
                }
            }
            else
            {
                Vector2 stringOffset = GUIStyle.GlobalFont.MeasureString(inLobbyString) / 2f;
                GUIStyle.GlobalFont.DrawString(spriteBatch, inLobbyString, area.Center.ToVector2() - stringOffset, Color.White);
            }
        }

        private void DrawDisconnectedIcon(SpriteBatch spriteBatch, Rectangle area)
        {
            disconnectedIcon.Draw(spriteBatch, area, GUIStyle.Red);
        }

        /// <summary>
        /// Select an element from CrewListFrame
        /// </summary>
        private bool SelectElement(object userData, GUIComponent crewList)
        {
            Character character = userData as Character;
            Client client = userData as Client;

            GUIComponent existingPreview = infoFrameHolder.FindChild("SelectedCharacter");
            if (existingPreview != null) { infoFrameHolder.RemoveChild(existingPreview); }

            GUIFrame background = new GUIFrame(new RectTransform(new Vector2(0.543f, 0.717f), infoFrameHolder.RectTransform, Anchor.TopLeft, Pivot.TopRight) { RelativeOffset = new Vector2(-0.145f, 0) })
            {
                UserData = "SelectedCharacter"
            };

            if (character != null)
            {
                if (GameMain.Client == null)
                {
                    GUIComponent preview = character.Info.CreateInfoFrame(background, false, null);
                }
                else
                {
                    GUIComponent preview = character.Info.CreateInfoFrame(background, false, GetPermissionIcon(GameMain.Client.ConnectedClients.Find(c => c.Character == character)));
                    GameMain.Client.SelectCrewCharacter(character, preview);
                    CreateWalletFrame(background, character);
                }
            }
            else if (client != null)
            {
                GUIComponent preview = CreateClientInfoFrame(background, client, GetPermissionIcon(client));
                GameMain.Client?.SelectCrewClient(client, preview);
                if (client.Character != null)
                {
                    CreateWalletFrame(background, client.Character);
                }
            }

            return true;
        }

        private void CreateWalletFrame(GUIComponent parent, Character character)
        {
            if (character is null) { throw new ArgumentNullException(nameof(character), "Tried to create a wallet frame for a null character");}
            isTransferMenuOpen = false;
            transferMenuOpenState = 1f;
            ImmutableArray<Character> salaryCrew = Mission.GetSalaryEligibleCrew().Where(c => c != character).ToImmutableArray();

            Wallet targetWallet = character.Wallet;

            GUIFrame walletFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.35f), parent.RectTransform, anchor: Anchor.TopLeft)
            {
                RelativeOffset = new Vector2(0, 1.02f)
            });

            GUILayoutGroup walletLayout = new GUILayoutGroup(new RectTransform(ToolBox.PaddingSizeParentRelative(walletFrame.RectTransform, 0.9f), walletFrame.RectTransform, anchor: Anchor.Center));

            GUILayoutGroup headerLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.33f), walletLayout.RectTransform), isHorizontal: true);
            GUIImage icon = new GUIImage(new RectTransform(Vector2.One, headerLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "StoreTradingIcon", scaleToFit: true);
            float relativeX =  icon.RectTransform.NonScaledSize.X / (float)icon.Parent.RectTransform.NonScaledSize.X;
            GUILayoutGroup headerTextLayout = new GUILayoutGroup(new RectTransform(new Vector2(1.0f - relativeX, 1f), headerLayout.RectTransform), isHorizontal: true) { Stretch = true };
            new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), headerTextLayout.RectTransform), TextManager.Get("crewwallet.wallet"), font: GUIStyle.LargeFont);
            GUITextBlock moneyBlock = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), headerTextLayout.RectTransform), TextManager.FormatCurrency(targetWallet.Balance), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Right);

            GUILayoutGroup middleLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.66f), walletLayout.RectTransform));
            GUILayoutGroup salaryTextLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.5f), middleLayout.RectTransform), isHorizontal: true);
            GUITextBlock salaryTitle = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), salaryTextLayout.RectTransform), TextManager.Get("crewwallet.salary"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.BottomLeft);
            GUITextBlock rewardBlock = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), salaryTextLayout.RectTransform), string.Empty, textAlignment: Alignment.BottomRight);
            GUILayoutGroup sliderLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.5f), middleLayout.RectTransform), isHorizontal: true, childAnchor: Anchor.Center);
            GUIScrollBar salarySlider = new GUIScrollBar(new RectTransform(new Vector2(0.9f, 1f), sliderLayout.RectTransform), style: "GUISlider", barSize: 0.03f)
            {
                ToolTip = TextManager.Get("crewwallet.salary.tooltip"),
                Range = new Vector2(0, 1),
                BarScrollValue = targetWallet.RewardDistribution / 100f,
                Step = 0.01f,
                BarSize = 0.1f,
                OnMoved = (bar, scroll) =>
                {
                    SetRewardText((int)(scroll * 100), rewardBlock);
                    return true;
                },
                OnReleased = (bar, scroll) =>
                {
                    int newRewardDistribution = (int)(scroll * 100);
                    if (newRewardDistribution == targetWallet.RewardDistribution) { return false; }
                    SetRewardDistribution(character, newRewardDistribution);
                    return true;
                }
            };

            SetRewardText(targetWallet.RewardDistribution, rewardBlock);

// @formatter:off
            GUIScissorComponent scissorComponent = new GUIScissorComponent(new RectTransform(new Vector2(0.85f, 1.25f), walletFrame.RectTransform, Anchor.BottomCenter, Pivot.TopCenter))
            {
                CanBeFocused = false
            };
            transferMenu = new GUIFrame(new RectTransform(Vector2.One, scissorComponent.Content.RectTransform));

            GUILayoutGroup transferMenuLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.8f), transferMenu.RectTransform, Anchor.BottomLeft), childAnchor: Anchor.Center);
                GUILayoutGroup paddedTransferMenuLayout = new GUILayoutGroup(new RectTransform(ToolBox.PaddingSizeParentRelative(transferMenuLayout.RectTransform, 0.85f), transferMenuLayout.RectTransform));
                    GUILayoutGroup mainLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.5f), paddedTransferMenuLayout.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft);
                        GUILayoutGroup leftLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1f), mainLayout.RectTransform));
                            GUITextBlock leftName = new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), leftLayout.RectTransform), character.Name, textAlignment: Alignment.CenterLeft, font: GUIStyle.SubHeadingFont);
                            GUITextBlock leftBalance = new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), leftLayout.RectTransform), TextManager.FormatCurrency(targetWallet.Balance), textAlignment: Alignment.Left) { TextColor = GUIStyle.Blue };
                        GUILayoutGroup rightLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1f), mainLayout.RectTransform), childAnchor: Anchor.TopRight);
                            GUITextBlock rightName = new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), rightLayout.RectTransform), string.Empty, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterRight);
                            GUITextBlock rightBalance = new GUITextBlock(new RectTransform(new Vector2(1f, 0.5f), rightLayout.RectTransform), string.Empty, textAlignment: Alignment.Right) { TextColor = GUIStyle.Red };
                        GUILayoutGroup centerLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.9f), mainLayout.RectTransform, Anchor.Center), childAnchor: Anchor.Center) { IgnoreLayoutGroups = true };
                            new GUIFrame(new RectTransform(new Vector2(0f, 1f), centerLayout.RectTransform, Anchor.Center), style: "VerticalLine") { IgnoreLayoutGroups = true };
                            GUIButton centerButton = new GUIButton(new RectTransform(new Vector2(0.6f), centerLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight, anchor: Anchor.Center), style: "GUIButtonTransferArrow");

                    GUILayoutGroup inputLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.25f), paddedTransferMenuLayout.RectTransform), childAnchor: Anchor.Center);
                        GUINumberInput transferAmountInput = new GUINumberInput(new RectTransform(new Vector2(0.5f, 1f), inputLayout.RectTransform), GUINumberInput.NumberType.Int, hidePlusMinusButtons: true)
                        {
                            MinValueInt = 0
                        };

                    GUILayoutGroup buttonLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.25f), paddedTransferMenuLayout.RectTransform), childAnchor: Anchor.Center);
                        GUILayoutGroup centerButtonLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.75f, 1f), buttonLayout.RectTransform), isHorizontal: true);
                            GUIButton confirmButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1f), centerButtonLayout.RectTransform), TextManager.Get("confirm"), style: "GUIButtonFreeScale") { Enabled = false };
                            GUIButton resetButton = new GUIButton(new RectTransform(new Vector2(0.5f, 1f), centerButtonLayout.RectTransform), TextManager.Get("reset"), style: "GUIButtonFreeScale") { Enabled = false };
// @formatter:on
            ImmutableArray<GUILayoutGroup> layoutGroups = ImmutableArray.Create(transferMenuLayout, paddedTransferMenuLayout, mainLayout, leftLayout, rightLayout);
            MedicalClinicUI.EnsureTextDoesntOverflow(character.Name, leftName, leftLayout.Rect, layoutGroups);
            transferMenuButton = new GUIButton(new RectTransform(new Vector2(0.5f, 0.2f), walletFrame.RectTransform, Anchor.BottomCenter, Pivot.TopCenter), style: "UIToggleButtonVertical")
            {
                ToolTip = TextManager.Get("crewwallet.transfer.tooltip"),
                OnClicked = (button, o) =>
                {
                    isTransferMenuOpen = !isTransferMenuOpen;
                    if (!isTransferMenuOpen)
                    {
                        transferAmountInput.IntValue = 0;
                    }
                    ToggleTransferMenuIcon(button, open: isTransferMenuOpen);
                    return true;
                }
            };
            ToggleTransferMenuIcon(transferMenuButton, open: isTransferMenuOpen);
            ToggleCenterButton(centerButton, isSending);

            if (GameMain.GameSession?.Campaign is MultiPlayerCampaign campaign)
            {
                if (!(Character.Controlled is { } myCharacter))
                {
                    salarySlider.Enabled = false;
                    transferAmountInput.Enabled = false;
                    centerButton.Enabled = false;
                    confirmButton.Enabled = false;
                    return;
                }

                bool hasPermissions = campaign.AllowedToManageCampaign();
                salarySlider.Enabled = hasPermissions;
                Wallet otherWallet;

                switch (hasPermissions)
                {
                    case true:
                        rightName.Text = TextManager.Get("crewwallet.bank");
                        otherWallet = campaign.Bank;
                        break;
                    case false when character == myCharacter:
                        rightName.Text =  TextManager.Get("crewwallet.bank");
                        otherWallet = campaign.Bank;
                        isSending = true;
                        ToggleCenterButton(centerButton, isSending);
                        break;
                    default:
                        rightName.Text = myCharacter.Name;
                        otherWallet = campaign.PersonalWallet;
                        break;
                }

                MedicalClinicUI.EnsureTextDoesntOverflow(rightName.Text.ToString(), rightName, rightLayout.Rect, layoutGroups);

                if (!hasPermissions)
                {
                    centerButton.Enabled = centerButton.CanBeFocused = false;
                    salarySlider.Enabled = salarySlider.CanBeFocused = false;
                }

                leftBalance.Text = TextManager.FormatCurrency(otherWallet.Balance);

                UpdateAllInputs();

                centerButton.OnClicked = (btn, o) =>
                {
                    isSending = !isSending;
                    ToggleCenterButton(btn, isSending);
                    UpdateAllInputs();
                    return true;
                };

                transferAmountInput.OnValueChanged = input =>
                {
                    UpdateInputs();
                };

                transferAmountInput.OnValueEntered = input =>
                {
                    UpdateAllInputs();
                };

                campaign.OnMoneyChanged.RegisterOverwriteExisting(nameof(CreateWalletFrame).ToIdentifier(), e =>
                {
                    if (e.Wallet == targetWallet)
                    {
                        moneyBlock.Text = TextManager.FormatCurrency(e.Info.Balance);
                        salarySlider.BarScrollValue = e.Info.RewardDistribution / 100f;
                    }
                    UpdateAllInputs();
                });

                resetButton.OnClicked = (button, o) =>
                {
                    transferAmountInput.IntValue = 0;
                    UpdateAllInputs();
                    return true;
                };

                confirmButton.OnClicked = (button, o) =>
                {
                    int amount = transferAmountInput.IntValue;
                    if (amount == 0) { return false; }

                    Option<Character> target1 = Option<Character>.Some(character),
                                      target2 = otherWallet == campaign.Bank ? Option<Character>.None() : Option<Character>.Some(myCharacter);
                    if (isSending) { (target1, target2) = (target2, target1); }

                    SendTransaction(target1, target2, amount);
                    isTransferMenuOpen = false;
                    ToggleTransferMenuIcon(transferMenuButton, isTransferMenuOpen);
                    return true;
                };

                void UpdateAllInputs()
                {
                    UpdateInputs();
                    UpdateMaxInput();
                }

                void UpdateInputs()
                {
                    confirmButton.Enabled = resetButton.Enabled = transferAmountInput.IntValue > 0;
                    if (transferAmountInput.IntValue == 0)
                    {
                        rightBalance.Text = TextManager.FormatCurrency(otherWallet.Balance);
                        rightBalance.TextColor = GUIStyle.TextColorNormal;
                        leftBalance.Text = TextManager.FormatCurrency(targetWallet.Balance);
                        leftBalance.TextColor = GUIStyle.TextColorNormal;
                    }
                    else if (isSending)
                    {
                        rightBalance.Text = TextManager.FormatCurrency(otherWallet.Balance + transferAmountInput.IntValue);
                        rightBalance.TextColor = GUIStyle.Blue;
                        leftBalance.Text = TextManager.FormatCurrency(targetWallet.Balance - transferAmountInput.IntValue);
                        leftBalance.TextColor = GUIStyle.Red;
                    }
                    else
                    {
                        rightBalance.Text = TextManager.FormatCurrency(otherWallet.Balance - transferAmountInput.IntValue);
                        rightBalance.TextColor = GUIStyle.Red;
                        leftBalance.Text = TextManager.FormatCurrency(targetWallet.Balance + transferAmountInput.IntValue);
                        leftBalance.TextColor = GUIStyle.Blue;
                    }
                }

                void UpdateMaxInput()
                {
                    transferAmountInput.MaxValueInt = isSending ? targetWallet.Balance : otherWallet.Balance;
                }
            }

            static void ToggleTransferMenuIcon(GUIButton btn, bool open)
            {
                foreach (GUIComponent child in btn.Children)
                {
                    child.SpriteEffects = open ? SpriteEffects.None : SpriteEffects.FlipVertically;
                }
            }

            static void ToggleCenterButton(GUIButton btn, bool isSending)
            {
                foreach (GUIComponent child in btn.Children)
                {
                    child.SpriteEffects = isSending ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
                }
            }

            static void SendTransaction(Option<Character> to, Option<Character> from, int amount)
            {
                INetSerializableStruct transfer = new NetWalletTransfer
                {
                    Sender = from.Select(option => option.ID),
                    Receiver = to.Select(option => option.ID),
                    Amount = amount
                };
                IWriteMessage msg = new WriteOnlyMessage().WithHeader(ClientPacketHeader.TRANSFER_MONEY);
                transfer.Write(msg);
                GameMain.Client?.ClientPeer?.Send(msg, DeliveryMethod.Reliable);
            }

            static void SetRewardDistribution(Character character, int newValue)
            {
                INetSerializableStruct transfer = new NetWalletSetSalaryUpdate
                {
                    Target = character.ID,
                    NewRewardDistribution = newValue
                };
                IWriteMessage msg = new WriteOnlyMessage().WithHeader(ClientPacketHeader.REWARD_DISTRIBUTION);
                transfer.Write(msg);
                GameMain.Client?.ClientPeer?.Send(msg, DeliveryMethod.Reliable);
            }

            void SetRewardText(int value, GUITextBlock block)
            {
                var (_, percentage, sum) = Mission.GetRewardShare(value, salaryCrew, Option<int>.None());
                LocalizedString tooltip = string.Empty;
                block.TextColor = GUIStyle.TextColorNormal;

                if (sum > 100)
                {
                    tooltip = TextManager.GetWithVariables("crewwallet.salary.over100toolitp", ("[sum]", $"{(int)sum}"), ("[newvalue]", $"{percentage}"));
                    block.TextColor = GUIStyle.Orange;
                }

                LocalizedString text = TextManager.GetWithVariable("percentageformat", "[value]", $"{value}");

                block.Text = text;
                block.ToolTip = RichString.Rich(tooltip);
            }
        }

        private GUIComponent CreateClientInfoFrame(GUIFrame frame, Client client, Sprite permissionIcon = null)
        {
            GUIComponent paddedFrame;

            if (client.Character?.Info == null)
            {
                paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.874f, 0.58f), frame.RectTransform, Anchor.TopCenter) { RelativeOffset = new Vector2(0.0f, 0.05f) })
                {
                    RelativeSpacing = 0.05f
                    //Stretch = true
                };

                var headerArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.322f), paddedFrame.RectTransform), isHorizontal: true);

                new GUICustomComponent(new RectTransform(new Vector2(0.425f, 1.0f), headerArea.RectTransform),
                    onDraw: (sb, component) => DrawNotInGameIcon(sb, component.Rect, client));

                GUIFont font = paddedFrame.Rect.Width < 280 ? GUIStyle.SmallFont : GUIStyle.Font;

                var headerTextArea = new GUILayoutGroup(new RectTransform(new Vector2(0.575f, 1.0f), headerArea.RectTransform))
                {
                    RelativeSpacing = 0.02f,
                    Stretch = true
                };

                GUITextBlock clientNameBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), headerTextArea.RectTransform), ToolBox.LimitString(client.Name, GUIStyle.Font, headerTextArea.Rect.Width), textColor: Color.White, font: GUIStyle.Font)
                {
                    ForceUpperCase = ForceUpperCase.Yes,
                    Padding = Vector4.Zero
                };

                if (permissionIcon != null)
                {
                    Point iconSize = permissionIcon.SourceRect.Size;
                    int iconWidth = (int)((float)clientNameBlock.Rect.Height / iconSize.Y * iconSize.X);
                    new GUIImage(new RectTransform(new Point(iconWidth, clientNameBlock.Rect.Height), clientNameBlock.RectTransform) { AbsoluteOffset = new Point(-iconWidth - 2, 0) }, permissionIcon) { IgnoreLayoutGroups = true };
                }

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), headerTextArea.RectTransform), client.Spectating ? TextManager.Get("playingasspectator") : TextManager.Get("tabmenu.inlobby"), textColor: Color.White, font: font, wrap: true)
                {
                    Padding = Vector4.Zero
                };
            }
            else
            {
                paddedFrame = client.Character.Info.CreateInfoFrame(frame, false, permissionIcon);
            }

            return paddedFrame;
        }

        private void CreateMultiPlayerLogContent(GUIFrame crewFrame)
        {
            var logContainer = new GUIFrame(new RectTransform(new Vector2(0.543f, 0.717f), crewFrame.RectTransform, Anchor.TopRight, Pivot.TopLeft) { RelativeOffset = new Vector2(-0.061f, 0) });
            var innerFrame = new GUIFrame(new RectTransform(new Vector2(0.900f, 0.900f), logContainer.RectTransform, Anchor.TopCenter, Pivot.TopCenter) { RelativeOffset = new Vector2(0f, 0.0475f) }, style: null);
            var content = new GUILayoutGroup(new RectTransform(Vector2.One, innerFrame.RectTransform))
            {
                Stretch = true
            };

            logList = new GUIListBox(new RectTransform(Vector2.One, content.RectTransform))
            {
                Padding = new Vector4(0, 10 * GUI.Scale, 0, 10 * GUI.Scale),
                UserData = crewFrame,
                AutoHideScrollBar = false,
                Spacing = (int)(5 * GUI.Scale)
            };

            foreach (Pair<string, PlayerConnectionChangeType> pair in storedMessages)
            {
                AddLineToLog(pair.First, pair.Second);
            }

            logList.BarScroll = 1f;
        }

        private static readonly List<Pair<string, PlayerConnectionChangeType>> storedMessages = new List<Pair<string, PlayerConnectionChangeType>>();

        public static void StorePlayerConnectionChangeMessage(ChatMessage message)
        {
            if (!GameMain.GameSession?.IsRunning ?? true) { return; }

            string msg = ChatMessage.GetTimeStamp() + message.TextWithSender;
            storedMessages.Add(new Pair<string, PlayerConnectionChangeType>(msg, message.ChangeType));

            if (GameSession.IsTabMenuOpen && SelectedTab == InfoFrameTab.Crew)
            {
                TabMenu instance = GameSession.TabMenuInstance;
                instance.AddLineToLog(msg, message.ChangeType);
                instance.RemoveCurrentElements();
                instance.CreateMultiPlayerList(true);
            }
        }

        private void RemoveCurrentElements()
        {
            for (int i = 0; i < crewListArray.Length; i++)
            {
                for (int j = 0; j < linkedGUIList.Count; j++)
                {
                    linkedGUIList[j].Remove(crewListArray[i].Content);
                }
            }

            linkedGUIList.Clear();
        }

        private void AddLineToLog(string line, PlayerConnectionChangeType type)
        {
            Color textColor = Color.White;

            switch (type)
            {
                case PlayerConnectionChangeType.Joined:
                    textColor = GUIStyle.Green;
                    break;
                case PlayerConnectionChangeType.Kicked:
                    textColor = GUIStyle.Orange;
                    break;
                case PlayerConnectionChangeType.Disconnected:
                    textColor = GUIStyle.Yellow;
                    break;
                case PlayerConnectionChangeType.Banned:
                    textColor = GUIStyle.Red;
                    break;
            }

            if (logList != null)
            {
                var textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), logList.Content.RectTransform), RichString.Rich(line), wrap: true, font: GUIStyle.SmallFont)
                {
                    TextColor = textColor,
                    CanBeFocused = false,
                    UserData = line
                };
                textBlock.CalculateHeightFromText();
                if (textBlock.HasColorHighlight)
                {
                    foreach (var data in textBlock.RichTextData)
                    {
                        textBlock.ClickableAreas.Add(new GUITextBlock.ClickableArea()
                        {
                            Data = data,
                            OnClick = GameMain.NetLobbyScreen.SelectPlayer,
                            OnSecondaryClick = GameMain.NetLobbyScreen.ShowPlayerContextMenu
                        });
                    }
                }
            }
        }

        private void CreateMissionInfo(GUIFrame infoFrame)
        {
            infoFrame.ClearChildren();
            GUIFrame missionFrame = new GUIFrame(new RectTransform(Vector2.One, infoFrame.RectTransform, Anchor.TopCenter), style: "GUIFrameListBox");
            int padding = (int)(0.0245f * missionFrame.Rect.Height);
            GUIFrame missionFrameContent = new GUIFrame(new RectTransform(new Point(missionFrame.Rect.Width - padding * 2, missionFrame.Rect.Height - padding * 2), infoFrame.RectTransform, Anchor.Center), style: null);
            Location location = GameMain.GameSession.EndLocation ?? GameMain.GameSession.StartLocation;

            GUILayoutGroup locationInfoContainer = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.3f), missionFrameContent.RectTransform))
            {
                AbsoluteSpacing = GUI.IntScale(10)
            };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), locationInfoContainer.RectTransform), location.Name, font: GUIStyle.LargeFont);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), locationInfoContainer.RectTransform), location.Type.Name, font: GUIStyle.SubHeadingFont);

            var biomeLabel = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), locationInfoContainer.RectTransform),
                TextManager.Get("Biome", "location"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), biomeLabel.RectTransform), Level.Loaded.LevelData.Biome.DisplayName, textAlignment: Alignment.CenterRight);
            var difficultyLabel = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), locationInfoContainer.RectTransform),
                TextManager.Get("LevelDifficulty"), font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), difficultyLabel.RectTransform), ((int)Level.Loaded.LevelData.Difficulty) + " %", textAlignment: Alignment.CenterRight);

            new GUIFrame(new RectTransform(new Vector2(1.0f, 0.01f), missionFrameContent.RectTransform) { AbsoluteOffset = new Point(0, locationInfoContainer.Rect.Height + padding) }, style: "HorizontalLine")
            {
                CanBeFocused = false
            };

            int locationInfoYOffset = locationInfoContainer.Rect.Height + padding * 2;

            Sprite portrait = location.Type.GetPortrait(location.PortraitId);
            bool hasPortrait = portrait != null && portrait.SourceRect.Width > 0 && portrait.SourceRect.Height > 0;
            int contentWidth = missionFrameContent.Rect.Width;

            if (hasPortrait)
            {
                float portraitAspectRatio = portrait.SourceRect.Width / portrait.SourceRect.Height;
                GUIImage portraitImage = new GUIImage(new RectTransform(new Vector2(0.5f, 1f), locationInfoContainer.RectTransform, Anchor.CenterRight), portrait, scaleToFit: true)
                {
                    IgnoreLayoutGroups = true
                };
                locationInfoContainer.Recalculate();
                portraitImage.RectTransform.NonScaledSize = new Point(Math.Min((int)(portraitImage.Rect.Size.Y * portraitAspectRatio), portraitImage.Rect.Width), portraitImage.Rect.Size.Y);
            }

            GUIListBox missionList = new GUIListBox(new RectTransform(new Point(contentWidth, missionFrameContent.Rect.Height - locationInfoYOffset), missionFrameContent.RectTransform, Anchor.TopCenter) { AbsoluteOffset = new Point(0, locationInfoYOffset) });
            missionList.ContentBackground.Color = Color.Transparent;
            missionList.Spacing = GUI.IntScale(15);

            if (GameMain.GameSession?.Missions != null)
            {
                int spacing = GUI.IntScale(5);
                int iconSize = (int)(GUIStyle.LargeFont.MeasureChar('T').Y + GUIStyle.Font.MeasureChar('T').Y * 4 + spacing * 4);

                foreach (Mission mission in GameMain.GameSession.Missions)
                {
                    GUIFrame missionDescriptionHolder = new GUIFrame(new RectTransform(Vector2.One, missionList.Content.RectTransform), style: null);
                    GUILayoutGroup missionTextGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.744f, 0f), missionDescriptionHolder.RectTransform, Anchor.CenterLeft) { AbsoluteOffset = new Point(iconSize + spacing, 0) }, false, childAnchor: Anchor.TopLeft)
                    {
                        AbsoluteSpacing = spacing
                    };
                    LocalizedString descriptionText = mission.Description;
                    foreach (LocalizedString missionMessage in mission.ShownMessages)
                    {
                        descriptionText += "\n\n" + missionMessage;
                    }
                    RichString rewardText = mission.GetMissionRewardText(Submarine.MainSub);
                    RichString reputationText = mission.GetReputationRewardText(mission.Locations[0]);

                    Func<string, string> wrapMissionText(GUIFont font)
                    {
                        return (str) => ToolBox.WrapText(str, missionTextGroup.Rect.Width, font.Value);
                    }
                    RichString missionNameString = RichString.Rich(mission.Name, wrapMissionText(GUIStyle.LargeFont));
                    RichString missionRewardString = RichString.Rich(rewardText, wrapMissionText(GUIStyle.Font));
                    RichString missionReputationString = RichString.Rich(reputationText, wrapMissionText(GUIStyle.Font));
                    RichString missionDescriptionString = RichString.Rich(descriptionText, wrapMissionText(GUIStyle.Font));

                    Vector2 missionNameSize = GUIStyle.LargeFont.MeasureString(missionNameString);
                    Vector2 missionDescriptionSize = GUIStyle.Font.MeasureString(missionDescriptionString);
                    Vector2 missionRewardSize = GUIStyle.Font.MeasureString(missionRewardString);
                    Vector2 missionReputationSize = GUIStyle.Font.MeasureString(missionReputationString);

                    float ySize = missionNameSize.Y + missionDescriptionSize.Y + missionRewardSize.Y + missionReputationSize.Y + missionTextGroup.AbsoluteSpacing * 4;
                    bool displayDifficulty = mission.Difficulty.HasValue;
                    if (displayDifficulty) { ySize += missionRewardSize.Y; }

                    missionDescriptionHolder.RectTransform.NonScaledSize = new Point(missionDescriptionHolder.RectTransform.NonScaledSize.X, (int)ySize);
                    missionTextGroup.RectTransform.NonScaledSize = new Point(missionTextGroup.RectTransform.NonScaledSize.X, missionDescriptionHolder.RectTransform.NonScaledSize.Y);

                    if (mission.Prefab.Icon != null)
                    {
                        /*float iconAspectRatio = mission.Prefab.Icon.SourceRect.Width / mission.Prefab.Icon.SourceRect.Height;
                        int iconWidth = (int)(0.225f * missionDescriptionHolder.RectTransform.NonScaledSize.X);
                        int iconHeight = Math.Max(missionTextGroup.RectTransform.NonScaledSize.Y, (int)(iconWidth * iconAspectRatio));
                        Point iconSize = new Point(iconWidth, iconHeight);*/

                        var icon = new GUIImage(new RectTransform(new Point(iconSize), missionDescriptionHolder.RectTransform), mission.Prefab.Icon, null, true)
                        {
                            Color = mission.Prefab.IconColor,
                            HoverColor = mission.Prefab.IconColor,
                            SelectedColor = mission.Prefab.IconColor,
                            CanBeFocused = false
                        };
                        UpdateMissionStateIcon(mission, icon);
                        mission.OnMissionStateChanged += (mission) => UpdateMissionStateIcon(mission, icon);
                    }
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextGroup.RectTransform), missionNameString, font: GUIStyle.LargeFont);
                    GUILayoutGroup difficultyIndicatorGroup = null;
                    if (displayDifficulty)
                    {
                        difficultyIndicatorGroup = new GUILayoutGroup(new RectTransform(new Point(missionTextGroup.Rect.Width, (int)missionRewardSize.Y), parent: missionTextGroup.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
                        {
                            AbsoluteSpacing = 1
                        };
                        var difficultyColor = mission.GetDifficultyColor();
                        for (int i = 0; i < mission.Difficulty.Value; i++)
                        {
                            new GUIImage(new RectTransform(Vector2.One, difficultyIndicatorGroup.RectTransform, scaleBasis: ScaleBasis.Smallest), "DifficultyIndicator", scaleToFit: true)
                            {
                                CanBeFocused = false,
                                Color = difficultyColor
                            };
                        }
                    }
                    var rewardTextBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextGroup.RectTransform), missionRewardString);
                    if (difficultyIndicatorGroup != null)
                    {
                        difficultyIndicatorGroup.RectTransform.Resize(new Point((int)(difficultyIndicatorGroup.Rect.Width - rewardTextBlock.Padding.X - rewardTextBlock.Padding.Z), difficultyIndicatorGroup.Rect.Height));
                        difficultyIndicatorGroup.RectTransform.AbsoluteOffset = new Point((int)rewardTextBlock.Padding.X, 0);
                    }
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextGroup.RectTransform), missionReputationString);
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextGroup.RectTransform), missionDescriptionString);
                }
            }
            else
            {
                GUILayoutGroup missionTextGroup = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0f), missionList.RectTransform, Anchor.CenterLeft), false, childAnchor: Anchor.TopLeft);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextGroup.RectTransform), TextManager.Get("NoMission"), font: GUIStyle.LargeFont);
            }
        }

        private void UpdateMissionStateIcon(Mission mission, GUIImage missionIcon)
        {
            if (mission == null || missionIcon == null) { return; }
            string style = string.Empty;
            if (mission.DisplayAsFailed)
            {
                style = "MissionFailedIcon";
            }
            else if (mission.DisplayAsCompleted)
            {
                style = "MissionCompletedIcon";
            }
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
                stateIcon ??= new GUIImage(new RectTransform(Vector2.One, missionIcon.RectTransform), style, scaleToFit: true);
                stateIcon.Visible = true;
            }
        }

        private void CreateTraitorInfo(GUIFrame infoFrame, TraitorMissionPrefab traitorMission, Character traitor)
        {
            GUIFrame missionFrame = new GUIFrame(new RectTransform(Vector2.One, infoFrame.RectTransform, Anchor.TopCenter), style: "GUIFrameListBox");

            int padding = (int)(0.0245f * missionFrame.Rect.Height);

            GUIFrame missionDescriptionHolder = new GUIFrame(new RectTransform(new Point(missionFrame.Rect.Width - padding * 2, 0), missionFrame.RectTransform, Anchor.TopCenter) { AbsoluteOffset = new Point(0, padding) }, style: null);
            GUILayoutGroup missionTextGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.65f, 0f), missionDescriptionHolder.RectTransform, Anchor.CenterLeft) { RelativeOffset = new Vector2(0.319f, 0f) }, false, childAnchor: Anchor.TopLeft);

            LocalizedString missionNameString = ToolBox.WrapText(TextManager.Get("tabmenu.traitor"), missionTextGroup.Rect.Width, GUIStyle.LargeFont);
            LocalizedString missionDescriptionString = ToolBox.WrapText(traitor.TraitorCurrentObjective, missionTextGroup.Rect.Width, GUIStyle.Font);

            Vector2 missionNameSize = GUIStyle.LargeFont.MeasureString(missionNameString);
            Vector2 missionDescriptionSize = GUIStyle.Font.MeasureString(missionDescriptionString);

            missionDescriptionHolder.RectTransform.NonScaledSize = new Point(missionDescriptionHolder.RectTransform.NonScaledSize.X, (int)(missionNameSize.Y + missionDescriptionSize.Y));
            missionTextGroup.RectTransform.NonScaledSize = new Point(missionTextGroup.RectTransform.NonScaledSize.X, missionDescriptionHolder.RectTransform.NonScaledSize.Y);

            float aspectRatio = traitorMission.Icon.SourceRect.Width / traitorMission.Icon.SourceRect.Height;

            int iconWidth = (int)(0.319f * missionDescriptionHolder.RectTransform.NonScaledSize.X);
            int iconHeight = Math.Max(missionTextGroup.RectTransform.NonScaledSize.Y, (int)(iconWidth * aspectRatio));
            Point iconSize = new Point(iconWidth, iconHeight);

            new GUIImage(new RectTransform(iconSize, missionDescriptionHolder.RectTransform), traitorMission.Icon, null, true) { Color = traitorMission.IconColor };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextGroup.RectTransform), missionNameString, font: GUIStyle.LargeFont);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextGroup.RectTransform), missionDescriptionString);
        }

        private void CreateSubmarineInfo(GUIFrame infoFrame, Submarine sub)
        {
            GUIFrame subInfoFrame = new GUIFrame(new RectTransform(Vector2.One, infoFrame.RectTransform, Anchor.TopCenter), style: "GUIFrameListBox");
            GUIFrame paddedFrame = new GUIFrame(new RectTransform(Vector2.One * 0.97f, subInfoFrame.RectTransform, Anchor.Center), style: null);

            var previewButton = new GUIButton(new RectTransform(new Vector2(1.0f, 0.43f), paddedFrame.RectTransform), style: null)
            {
                OnClicked = (btn, obj) => { SubmarinePreview.Create(sub.Info); return false; },
            };

            var previewImage = sub.Info.PreviewImage ?? SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name.Equals(sub.Info.Name, StringComparison.OrdinalIgnoreCase))?.PreviewImage;
            if (previewImage == null)
            {
                new GUITextBlock(new RectTransform(Vector2.One, previewButton.RectTransform), TextManager.Get("SubPreviewImageNotFound"));
            }
            else
            {
                var submarinePreviewBackground = new GUIFrame(new RectTransform(Vector2.One, previewButton.RectTransform), style: null)
                {
                    Color = Color.Black,
                    HoverColor = Color.Black,
                    SelectedColor = Color.Black,
                    PressedColor = Color.Black,
                    CanBeFocused = false,
                };
                new GUIImage(new RectTransform(new Vector2(0.98f), submarinePreviewBackground.RectTransform, Anchor.Center), previewImage, scaleToFit: true) { CanBeFocused = false };
                new GUIFrame(new RectTransform(Vector2.One, submarinePreviewBackground.RectTransform), "InnerGlow", color: Color.Black) { CanBeFocused = false };
            }

            new GUIFrame(new RectTransform(Vector2.One * 0.12f, previewButton.RectTransform, anchor: Anchor.BottomRight, pivot: Pivot.BottomRight, scaleBasis: ScaleBasis.BothHeight)
            {
                AbsoluteOffset = new Point((int)(0.03f * previewButton.Rect.Height))
            },
                "ExpandButton", Color.White)
            {
                Color = Color.White,
                HoverColor = Color.White,
                PressedColor = Color.White
            };

            var subInfoTextLayout = new GUILayoutGroup(new RectTransform(Vector2.One, paddedFrame.RectTransform));

            LocalizedString className = !sub.Info.HasTag(SubmarineTag.Shuttle) ? TextManager.Get($"submarineclass.{sub.Info.SubmarineClass}") : TextManager.Get("shuttle");

            int nameHeight = (int)GUIStyle.LargeFont.MeasureString(sub.Info.DisplayName, true).Y;
            int classHeight = (int)GUIStyle.SubHeadingFont.MeasureString(className).Y;

            var submarineNameText = new GUITextBlock(new RectTransform(new Point(subInfoTextLayout.Rect.Width, nameHeight + HUDLayoutSettings.Padding / 2), subInfoTextLayout.RectTransform), sub.Info.DisplayName, textAlignment: Alignment.CenterLeft, font: GUIStyle.LargeFont) { CanBeFocused = false };
            submarineNameText.RectTransform.MinSize = new Point(0, (int)submarineNameText.TextSize.Y);
            var submarineClassText = new GUITextBlock(new RectTransform(new Point(subInfoTextLayout.Rect.Width, classHeight), subInfoTextLayout.RectTransform), className, textAlignment: Alignment.CenterLeft, font: GUIStyle.SubHeadingFont) { CanBeFocused = false };
            submarineClassText.RectTransform.MinSize = new Point(0, (int)submarineClassText.TextSize.Y);

            if (GameMain.GameSession?.GameMode is CampaignMode campaign)
            {
                GUILayoutGroup headerLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.09f), paddedFrame.RectTransform) { RelativeOffset = new Vector2(0f, 0.43f) }, isHorizontal: true) { Stretch = true };
                GUIImage headerIcon = new GUIImage(new RectTransform(Vector2.One, headerLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "SubmarineIcon");
                new GUITextBlock(new RectTransform(Vector2.One, headerLayout.RectTransform), TextManager.Get("uicategory.upgrades"), font: GUIStyle.LargeFont);

                var upgradeRootLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.48f), paddedFrame.RectTransform, Anchor.BottomLeft, Pivot.BottomLeft), isHorizontal: true);

                var upgradeCategoryPanel = UpgradeStore.CreateUpgradeCategoryList(new RectTransform(new Vector2(0.4f, 1f), upgradeRootLayout.RectTransform));
                upgradeCategoryPanel.HideChildrenOutsideFrame = true;
                UpgradeStore.UpdateCategoryList(upgradeCategoryPanel, campaign, sub, UpgradeStore.GetApplicableCategories(sub).ToArray());
                GUIComponent[] toRemove = upgradeCategoryPanel.Content.FindChildren(c => !c.Enabled).ToArray();
                toRemove.ForEach(c => upgradeCategoryPanel.RemoveChild(c));

                var upgradePanel = new GUIListBox(new RectTransform(new Vector2(0.6f, 1f), upgradeRootLayout.RectTransform));
                upgradeCategoryPanel.OnSelected = (component, userData) =>
                {
                    upgradePanel.ClearChildren();
                    if (userData is UpgradeStore.CategoryData categoryData && Submarine.MainSub != null)
                    {
                        foreach (UpgradePrefab prefab in categoryData.Prefabs)
                        {
                            var frame = UpgradeStore.CreateUpgradeFrame(prefab, categoryData.Category, campaign, new RectTransform(new Vector2(1f, 0.3f), upgradePanel.Content.RectTransform), addBuyButton: false);
                            UpgradeStore.UpdateUpgradeEntry(frame, prefab, categoryData.Category, campaign);
                        }
                    }
                    return true;
                };
            }
            else
            {
                var specsListBox = new GUIListBox(new RectTransform(new Vector2(1f, 0.57f), paddedFrame.RectTransform, Anchor.BottomLeft, Pivot.BottomLeft));
                sub.Info.CreateSpecsWindow(specsListBox, GUIStyle.Font, includeTitle: false, includeClass: false, includeDescription: true);
            }
        }
        private Color unselectedColor = new Color(240, 255, 255, 225);
        private Color unselectableColor = new Color(100, 100, 100, 225);
        private Color pressedColor = new Color(60, 60, 60, 225);

        private readonly List<(GUIButton button, GUIComponent icon)> talentButtons = new List<(GUIButton button, GUIComponent icon)>();
        private readonly List<(Identifier talentTree, int index, GUIImage icon, GUIFrame background, GUIFrame backgroundGlow)> talentCornerIcons = new List<(Identifier talentTree, int index, GUIImage icon, GUIFrame background, GUIFrame backgroundGlow)>();
        private List<Identifier> selectedTalents = new List<Identifier>();

        private GUITextBlock experienceText;
        private GUIProgressBar experienceBar;
        private GUITextBlock talentPointText;
        private GUIListBox skillListBox;

        private GUIButton talentApplyButton,
                          talentResetButton;

        private GUIImage talentPointNotification;

        private readonly ImmutableDictionary<TalentTree.TalentTreeStageState, GUIComponentStyle> talentStageStyles = new Dictionary<TalentTree.TalentTreeStageState, GUIComponentStyle>
        {
            { TalentTree.TalentTreeStageState.Invalid, GUIStyle.GetComponentStyle("TalentTreeLocked") },
            { TalentTree.TalentTreeStageState.Locked, GUIStyle.GetComponentStyle("TalentTreeLocked") },
            { TalentTree.TalentTreeStageState.Unlocked, GUIStyle.GetComponentStyle("TalentTreePurchased") },
            { TalentTree.TalentTreeStageState.Available, GUIStyle.GetComponentStyle("TalentTreeUnlocked") },
            { TalentTree.TalentTreeStageState.Highlighted, GUIStyle.GetComponentStyle("TalentTreeAvailable") },
        }.ToImmutableDictionary();

        private readonly ImmutableDictionary<TalentTree.TalentTreeStageState, Color> talentStageBackgroundColors = new Dictionary<TalentTree.TalentTreeStageState, Color>
        {
            { TalentTree.TalentTreeStageState.Invalid, new Color(48,48,48,255) },
            { TalentTree.TalentTreeStageState.Locked, new Color(48,48,48,255) },
            { TalentTree.TalentTreeStageState.Unlocked, new Color(24,37,31,255) },
            { TalentTree.TalentTreeStageState.Available, new Color(50,47,33,255) },
            { TalentTree.TalentTreeStageState.Highlighted, new Color(50,47,33,255) },
        }.ToImmutableDictionary();

        private void CreateTalentInfo(GUIFrame infoFrame)
        {
            infoFrame.ClearChildren();
            talentButtons.Clear();
            talentCornerIcons.Clear();

            Character controlledCharacter = Character.Controlled;
            if (controlledCharacter == null) { return; }

            GUIFrame talentFrameBackground = new GUIFrame(new RectTransform(Vector2.One, infoFrame.RectTransform, Anchor.TopCenter), style: "GUIFrameListBox");
            int padding = GUI.IntScale(15);
            GUIFrame talentFrameContent = new GUIFrame(new RectTransform(new Point(talentFrameBackground.Rect.Width - padding, talentFrameBackground.Rect.Height - padding), infoFrame.RectTransform, Anchor.Center), style: null);

            GUIFrame paddedTalentFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.9f), talentFrameContent.RectTransform, Anchor.Center), style: null);

            GUIFrame talentFrameMain = new GUIFrame(new RectTransform(Vector2.One, paddedTalentFrame.RectTransform), style: null);

            GUIFrame characterSettingsFrame = null;
            GUILayoutGroup characterLayout = null;
            if (!(GameMain.NetworkMember is null))
            {
                characterSettingsFrame = new GUIFrame(new RectTransform(Vector2.One, talentFrameContent.RectTransform), style: null) { Visible = false };
                characterLayout = new GUILayoutGroup(new RectTransform(Vector2.One, characterSettingsFrame.RectTransform));
                GUIFrame containerFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.9f), characterLayout.RectTransform), style: null);
                GUIFrame playerFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.7f), containerFrame.RectTransform, Anchor.Center), style: null);
                GameMain.NetLobbyScreen.CreatePlayerFrame(playerFrame, alwaysAllowEditing: true, createPendingText: false);
            }

            if (controlledCharacter.Info is null)
            {
                DebugConsole.ThrowError("No character info found for talent UI");
                return;
            }

            selectedTalents = controlledCharacter.Info.GetUnlockedTalentsInTree().ToList();

            GUILayoutGroup talentFrameLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), talentFrameMain.RectTransform, anchor: Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                AbsoluteSpacing = GUI.IntScale(5)
            };
            
            GUILayoutGroup talentInfoLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.25f), talentFrameLayoutGroup.RectTransform, Anchor.Center), isHorizontal: true);

            CharacterInfo info = controlledCharacter.Info;
            Job job = info.Job;

            new GUICustomComponent(new RectTransform(new Vector2(0.25f, 1f), talentInfoLayoutGroup.RectTransform), onDraw: (batch, component) =>
            {
                float posY = component.Rect.Center.Y - component.Rect.Width / 2;
                info.DrawPortrait(batch, new Vector2(component.Rect.X, posY), Vector2.Zero, component.Rect.Width, false, false);
            });

            GUILayoutGroup nameLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 1f), talentInfoLayoutGroup.RectTransform)) { RelativeSpacing = 0.05f };
            
            Vector2 nameSize = GUIStyle.SubHeadingFont.MeasureString(info.Name);
            GUITextBlock nameBlock = new GUITextBlock(new RectTransform(Vector2.One, nameLayout.RectTransform), info.Name, font: GUIStyle.SubHeadingFont) { TextColor = job.Prefab.UIColor };
            nameBlock.RectTransform.NonScaledSize = nameSize.Pad(nameBlock.Padding).ToPoint();

            Vector2 jobSize = GUIStyle.SmallFont.MeasureString(job.Name);
            GUITextBlock jobBlock = new GUITextBlock(new RectTransform(Vector2.One, nameLayout.RectTransform), job.Name, font: GUIStyle.SmallFont) { TextColor = job.Prefab.UIColor };
            jobBlock.RectTransform.NonScaledSize = jobSize.Pad(jobBlock.Padding).ToPoint();

            LocalizedString traitString = TextManager.AddPunctuation(':', TextManager.Get("PersonalityTrait"), TextManager.Get("personalitytrait." + info.PersonalityTrait.Name.Replace(" ", "")));
            Vector2 traitSize = GUIStyle.SmallFont.MeasureString(traitString);
            GUITextBlock traitBlock = new GUITextBlock(new RectTransform(Vector2.One, nameLayout.RectTransform), traitString, font: GUIStyle.SmallFont);
            traitBlock.RectTransform.NonScaledSize = traitSize.Pad(traitBlock.Padding).ToPoint();

            GUIFrame endocrineFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.35f), nameLayout.RectTransform, Anchor.BottomCenter), style: null);

            if (!(GameMain.NetworkMember is null))
            {
                GUIButton newCharacterBox = new GUIButton(new RectTransform(new Vector2(0.675f, 1f), endocrineFrame.RectTransform, Anchor.TopLeft), text: GameMain.NetLobbyScreen.CampaignCharacterDiscarded ? TextManager.Get("settings") : TextManager.Get("createnew"))
                {
                    IgnoreLayoutGroups = true
                };

                newCharacterBox.OnClicked = (button, o) =>
                {
                    if (!GameMain.NetLobbyScreen.CampaignCharacterDiscarded)
                    {
                        GameMain.NetLobbyScreen.TryDiscardCampaignCharacter(() =>
                        {
                            newCharacterBox.Text = TextManager.Get("settings");

                            if (pendingChangesFrame != null)
                            {
                                NetLobbyScreen.CreateChangesPendingFrame(pendingChangesFrame);
                            }
                            OpenMenu();
                        });
                        return true;
                    }

                    OpenMenu();
                    return true;

                    void OpenMenu()
                    {
                        characterSettingsFrame!.Visible = true;
                        talentFrameMain.Visible = false;
                    }
                };

                if (!(characterLayout is null))
                {
                    GUILayoutGroup characterCloseButtonLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.1f), characterLayout.RectTransform), childAnchor: Anchor.BottomRight);
                    new GUIButton(new RectTransform(new Vector2(0.4f, 1f), characterCloseButtonLayout.RectTransform), TextManager.Get("ApplySettingsButton")) //TODO: Is this text appropriate for this circumstance for all languages?
                    {
                        OnClicked = (button, o) =>
                        {
                            characterSettingsFrame!.Visible = false;
                            talentFrameMain.Visible = true;
                            return true;
                        }
                    };
                }
            }

            IEnumerable<TalentPrefab> endocrineTalents = info.GetEndocrineTalents().Select(e => TalentPrefab.TalentPrefabs.Find(c => c.Identifier == e));

            if (endocrineTalents.Count() > 0)
            {
                GUIImage endocrineIcon = new GUIImage(new RectTransform(new Vector2(0.275f, 1f), endocrineFrame.RectTransform, anchor: Anchor.TopRight, scaleBasis: ScaleBasis.Normal), style: "EndocrineReminderIcon")
                {
                    ToolTip = $"{TextManager.Get("afflictionname.endocrineboost")}\n\n{string.Join(", ", endocrineTalents.Select(e => e.DisplayName))}"
                };
            }

            GUILayoutGroup skillLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.45f, 1f), talentInfoLayoutGroup.RectTransform)) { Stretch = true };

            LocalizedString skillString = TextManager.Get("skills");
            Vector2 skillSize = GUIStyle.SubHeadingFont.MeasureString(skillString);
            GUITextBlock skillBlock = new GUITextBlock(new RectTransform(Vector2.One, skillLayout.RectTransform), skillString, font: GUIStyle.SubHeadingFont);
            skillBlock.RectTransform.NonScaledSize = skillSize.Pad(skillBlock.Padding).ToPoint();

            skillListBox = new GUIListBox(new RectTransform(new Vector2(1f, 1f - skillBlock.RectTransform.RelativeSize.Y), skillLayout.RectTransform), style: null);
            CreateTalentSkillList(controlledCharacter, skillListBox);

            if (!TalentTree.JobTalentTrees.TryGet(controlledCharacter.Info.Job.Prefab.Identifier, out TalentTree talentTree)) { return; }

            new GUIFrame(new RectTransform(new Vector2(1f, 1f), talentFrameLayoutGroup.RectTransform), style: "HorizontalLine");

            GUIListBox talentTreeListBox = new GUIListBox(new RectTransform(new Vector2(1f, 0.7f), talentFrameLayoutGroup.RectTransform, Anchor.TopCenter), isHorizontal: true, style: null);

            List<GUITextBlock> subTreeNames = new List<GUITextBlock>();
            foreach (var subTree in talentTree.TalentSubTrees)
            {
                GUIFrame subTreeFrame = new GUIFrame(new RectTransform(new Vector2(0.333f, 1f), talentTreeListBox.Content.RectTransform, anchor: Anchor.TopLeft), style: null);
                GUILayoutGroup subTreeLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(1f, 1f), subTreeFrame.RectTransform, Anchor.Center), false, childAnchor: Anchor.TopCenter);

                GUIFrame subtreeTitleFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.111f), subTreeLayoutGroup.RectTransform, anchor: Anchor.TopCenter), style: null);
                int elementPadding = GUI.IntScale(8);
                Point headerSize = subtreeTitleFrame.RectTransform.NonScaledSize;
                GUIFrame subTreeTitleBackground = new GUIFrame(new RectTransform(new Point(headerSize.X - elementPadding, headerSize.Y), subtreeTitleFrame.RectTransform, anchor: Anchor.Center), style: "SubtreeHeader");
                subTreeNames.Add(new GUITextBlock(new RectTransform(Vector2.One, subTreeTitleBackground.RectTransform, anchor: Anchor.TopCenter), subTree.DisplayName, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.Center));

                for (int i = 0; i < 4; i++)
                {
                    GUIFrame talentOptionFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.222f), subTreeLayoutGroup.RectTransform, anchor: Anchor.TopCenter), style: null);

                    Point talentFrameSize = talentOptionFrame.RectTransform.NonScaledSize;

                    GUIFrame talentBackground = new GUIFrame(new RectTransform(new Point(talentFrameSize.X - elementPadding, talentFrameSize.Y - elementPadding), talentOptionFrame.RectTransform, anchor: Anchor.Center), style: "TalentBackground");
                    GUIFrame talentBackgroundHighlight = new GUIFrame(new RectTransform(Vector2.One, talentBackground.RectTransform, anchor: Anchor.Center), style: "TalentBackgroundGlow") { Visible = false };

                    GUIImage cornerIcon = new GUIImage(new RectTransform(new Vector2(0.2f), talentOptionFrame.RectTransform, anchor: Anchor.BottomRight, scaleBasis: ScaleBasis.BothHeight) { MaxSize = new Point(16) }, style: null)
                    {
                        CanBeFocused = false
                    };

                    Point iconSize = cornerIcon.RectTransform.NonScaledSize;
                    cornerIcon.RectTransform.AbsoluteOffset = new Point(iconSize.X / 2, iconSize.Y / 2);

                    if (subTree.TalentOptionStages.Count > i)
                    {
                        TalentOption talentOption = subTree.TalentOptionStages[i];

                        GUILayoutGroup talentOptionCenterGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.75f, 0.7f), talentOptionFrame.RectTransform, Anchor.Center), childAnchor: Anchor.CenterLeft);

                        GUILayoutGroup talentOptionLayoutGroup = new GUILayoutGroup(new RectTransform(Vector2.One, talentOptionCenterGroup.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft) { Stretch = true };

                        foreach (TalentPrefab talent in talentOption.Talents.OrderBy(t => t.Identifier))
                        {
                            GUIFrame talentFrame = new GUIFrame(new RectTransform(Vector2.One, talentOptionLayoutGroup.RectTransform), style: null)
                            {
                                CanBeFocused = false
                            };

                            GUIFrame croppedTalentFrame = new GUIFrame(new RectTransform(Vector2.One, talentFrame.RectTransform, anchor: Anchor.Center, scaleBasis: ScaleBasis.BothHeight), style: null);

                            GUIButton talentButton = new GUIButton(new RectTransform(Vector2.One, croppedTalentFrame.RectTransform, anchor: Anchor.Center), style: null)
                            {
                                ToolTip = RichString.Rich(talent.DisplayName + "\n\n" + talent.Description),
                                UserData = talent.Identifier,
                                PressedColor = pressedColor,
                                OnClicked = (button, userData) =>
                                {
                                    // deselect other buttons in tier by removing their selected talents from pool
                                    foreach (GUIButton guiButton in talentOptionLayoutGroup.GetAllChildren<GUIButton>())
                                    {
                                        if (guiButton.UserData is Identifier otherTalentIdentifier && guiButton != button)
                                        {
                                            if (!controlledCharacter.HasTalent(otherTalentIdentifier))
                                            {
                                                selectedTalents.Remove(otherTalentIdentifier);
                                            }
                                        }
                                    }
                                    Identifier talentIdentifier = (Identifier)userData;

                                    if (TalentTree.IsViableTalentForCharacter(controlledCharacter, talentIdentifier, selectedTalents))
                                    {
                                        if (!selectedTalents.Contains(talentIdentifier))
                                        {
                                            selectedTalents.Add(talentIdentifier);
                                        }
                                    }
                                    else if (!controlledCharacter.HasTalent(talentIdentifier))
                                    {
                                        selectedTalents.Remove(talentIdentifier);
                                    }

                                    UpdateTalentInfo();
                                    return true;
                                },
                            };

                            talentButton.Color = talentButton.HoverColor = talentButton.PressedColor = talentButton.SelectedColor = Color.Transparent;

                            GUIComponent iconImage;
                            if (talent.Icon is null)
                            {
                                iconImage = new GUITextBlock(new RectTransform(Vector2.One, talentButton.RectTransform, anchor: Anchor.Center), text: "???", font: GUIStyle.LargeFont, textAlignment: Alignment.Center, style: null)
                                {
                                    OutlineColor = GUIStyle.Red,
                                    TextColor = GUIStyle.Red,
                                    PressedColor = unselectableColor,
                                    CanBeFocused = false,
                                };
                            }
                            else
                            {
                                iconImage = new GUIImage(new RectTransform(Vector2.One, talentButton.RectTransform, anchor: Anchor.Center), sprite: talent.Icon, scaleToFit: true)
                                {
                                    PressedColor = unselectableColor,
                                    CanBeFocused = false,
                                };
                            }

                            talentButtons.Add((talentButton, iconImage));
                        }

                        talentCornerIcons.Add((subTree.Identifier, i, cornerIcon, talentBackground, talentBackgroundHighlight));
                    }
                }
            }
            GUITextBlock.AutoScaleAndNormalize(subTreeNames);

            GUILayoutGroup talentBottomFrame = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.07f), talentFrameLayoutGroup.RectTransform, Anchor.TopCenter), isHorizontal: true) { RelativeSpacing = 0.01f };

            GUILayoutGroup experienceLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.59f, 1f), talentBottomFrame.RectTransform));
            GUIFrame experienceBarFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.5f), experienceLayout.RectTransform), style: null);

            experienceBar = new GUIProgressBar(new RectTransform(new Vector2(1f, 1f), experienceBarFrame.RectTransform, Anchor.CenterLeft),
                barSize: controlledCharacter.Info.GetProgressTowardsNextLevel(), color: GUIStyle.Green)
            {
                IsHorizontal = true,
            };
            
            experienceText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), experienceBarFrame.RectTransform, anchor: Anchor.Center), "", font: GUIStyle.Font, textAlignment: Alignment.CenterRight)
            {
                Shadow = true,
                ToolTip = TextManager.Get("experiencetooltip")
            };

            talentPointText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), experienceLayout.RectTransform, anchor: Anchor.Center), "", font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterRight) { AutoScaleVertical = true };

            talentResetButton = new GUIButton(new RectTransform(new Vector2(0.19f, 1f), talentBottomFrame.RectTransform), text: TextManager.Get("reset"), style: "GUIButtonFreeScale")
            {
                OnClicked = ResetTalentSelection
            };
            talentApplyButton = new GUIButton(new RectTransform(new Vector2(0.19f, 1f), talentBottomFrame.RectTransform), text: TextManager.Get("applysettingsbutton"), style: "GUIButtonFreeScale")
            {
                OnClicked = ApplyTalentSelection,
            };
            GUITextBlock.AutoScaleAndNormalize(talentResetButton.TextBlock, talentApplyButton.TextBlock);

            UpdateTalentInfo();
        }

        private void CreateTalentSkillList(Character character, GUIListBox parent)
        {
            parent.Content.ClearChildren();
            List<GUITextBlock> skillNames = new List<GUITextBlock>();
            foreach (Skill skill in character.Info.Job.Skills)
            {
                GUILayoutGroup skillContainer = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.2f), parent.Content.RectTransform), isHorizontal: true) { CanBeFocused = false };

                skillNames.Add(new GUITextBlock(new RectTransform(new Vector2(0.7f, 1f), skillContainer.RectTransform), TextManager.Get($"skillname.{skill.Identifier}").Fallback(skill.Identifier.Value)));
                new GUITextBlock(new RectTransform(new Vector2(0.15f, 1.0f), skillContainer.RectTransform), Math.Floor(skill.Level).ToString("F0"), textAlignment: Alignment.CenterRight) { Padding = new Vector4(0, 0, 4, 0) };

                float modifiedSkillLevel = character.GetSkillLevel(skill.Identifier);
                if (!MathUtils.NearlyEqual(MathF.Floor(modifiedSkillLevel), MathF.Floor(skill.Level)))
                {
                    int skillChange = (int)MathF.Floor(modifiedSkillLevel - skill.Level);
                    //TODO: if/when we upgrade to C# 9, do neater pattern matching here
                    string stringColor = true switch
                    {
                        true when skillChange > 0 => XMLExtensions.ColorToString(GUIStyle.Green),
                        true when skillChange < 0 => XMLExtensions.ColorToString(GUIStyle.Red),
                        _ => XMLExtensions.ColorToString(GUIStyle.TextColorNormal)
                    };

                    RichString changeText = RichString.Rich($"(‖color:{stringColor}‖{(skillChange > 0 ? "+" : string.Empty) + skillChange}‖color:end‖)");
                    new GUITextBlock(new RectTransform(new Vector2(0.15f, 1.0f), skillContainer.RectTransform), changeText) { Padding = Vector4.Zero };
                }
                skillContainer.Recalculate();
            }

            parent.RecalculateChildren();
            GUITextBlock.AutoScaleAndNormalize(skillNames);
        }

        private void UpdateTalentInfo()
        {
            Character controlledCharacter = Character.Controlled;
            if (controlledCharacter?.Info == null) { return; }

            if (SelectedTab != InfoFrameTab.Talents) { return; }

            bool unlockedAllTalents = controlledCharacter.HasUnlockedAllTalents();

            if (unlockedAllTalents)
            {
                experienceText.Text = string.Empty;
                experienceBar.BarSize = 1f;
            }
            else
            {
                experienceText.Text = $"{controlledCharacter.Info.ExperiencePoints - controlledCharacter.Info.GetExperienceRequiredForCurrentLevel()} / {controlledCharacter.Info.GetExperienceRequiredToLevelUp() - controlledCharacter.Info.GetExperienceRequiredForCurrentLevel()}";
                experienceBar.BarSize = controlledCharacter.Info.GetProgressTowardsNextLevel();
            }

            selectedTalents = TalentTree.CheckTalentSelection(controlledCharacter, selectedTalents);

            string pointsLeft = controlledCharacter.Info.GetAvailableTalentPoints().ToString();

            int talentCount = selectedTalents.Count - controlledCharacter.Info.GetUnlockedTalentsInTree().Count();

            if (unlockedAllTalents)
            {
                talentPointText.SetRichText($"‖color:{XMLExtensions.ToStringHex(Color.Gray)}‖{TextManager.Get("talentmenu.alltalentsunlocked")}‖color:end‖");
            }
            else if (talentCount > 0)
            {
                string pointsUsed = $"‖color:{XMLExtensions.ColorToString(GUIStyle.Red)}‖{-talentCount}‖color:end‖";
                LocalizedString localizedString = TextManager.GetWithVariables("talentmenu.points.spending", ("[amount]", pointsLeft), ("[used]", pointsUsed));
                talentPointText.SetRichText(localizedString);
            }
            else
            {
                talentPointText.SetRichText(TextManager.GetWithVariable("talentmenu.points", "[amount]", pointsLeft));
            }

            foreach (var (talentTree, index, icon, frame, glow) in talentCornerIcons)
            {
                TalentTree.TalentTreeStageState state = TalentTree.GetTalentOptionStageState(controlledCharacter, talentTree, index, selectedTalents);
                GUIComponentStyle newStyle = talentStageStyles[state];
                icon.ApplyStyle(newStyle);
                icon.Color = newStyle.Color;
                frame.Color = talentStageBackgroundColors[state];
                glow.Visible = state == TalentTree.TalentTreeStageState.Highlighted;
            }

            foreach (var talentButton in talentButtons)
            {
                Identifier talentIdentifier = (Identifier)talentButton.button.UserData;
                bool unselectable = !TalentTree.IsViableTalentForCharacter(controlledCharacter, talentIdentifier, selectedTalents) || controlledCharacter.HasTalent(talentIdentifier);
                Color newTalentColor = unselectable ? unselectableColor : unselectedColor;
                Color hoverColor = Color.White;

                if (controlledCharacter.HasTalent(talentIdentifier))
                {
                    newTalentColor = GUIStyle.Green;
                }
                else if (selectedTalents.Contains(talentIdentifier))
                {
                    newTalentColor = GUIStyle.Orange;
                    hoverColor = Color.Lerp(GUIStyle.Orange, Color.White, 0.7f);
                }

                talentButton.icon.Color = newTalentColor;
                talentButton.icon.HoverColor = hoverColor;
            }

            CreateTalentSkillList(controlledCharacter, skillListBox);
        }

        private void ApplyTalents(Character controlledCharacter)
        {
            selectedTalents = TalentTree.CheckTalentSelection(controlledCharacter, selectedTalents);
            foreach (Identifier talent in selectedTalents)
            {
                controlledCharacter.GiveTalent(talent);
                if (GameMain.Client != null)
                {
                    GameMain.Client.CreateEntityEvent(controlledCharacter, new Character.UpdateTalentsEventData());
                }
            }
            selectedTalents = controlledCharacter.Info.GetUnlockedTalentsInTree().ToList();
            UpdateTalentInfo();
        }

        private bool ApplyTalentSelection(GUIButton guiButton, object userData)
        {
            Character controlledCharacter = Character.Controlled;
            ApplyTalents(controlledCharacter);
            return true;
        }

        private bool ResetTalentSelection(GUIButton guiButton, object userData)
        {
            Character controlledCharacter = Character.Controlled;
            selectedTalents = controlledCharacter.Info.GetUnlockedTalentsInTree().ToList();
            UpdateTalentInfo();
            return true;
        }

        public void OnExperienceChanged(Character character)
        {
            if (character != Character.Controlled) { return; }
            UpdateTalentInfo();
        }
    }
}
