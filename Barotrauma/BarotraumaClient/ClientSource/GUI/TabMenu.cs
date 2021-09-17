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

        public enum InfoFrameTab { Crew, Mission, Reputation, MyCharacter, Traitor, Submarine, Talents };
        public static InfoFrameTab selectedTab;
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

        public static Color OwnCharacterBGColor = Color.Gold * 0.7f;

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
                    return GUI.Style.Green;
                }
                else if (currentPing < mediumPingThreshold)
                {
                    return GUI.Style.Yellow;
                }
                else
                {
                    return GUI.Style.Red;
                }
            }

            public void Remove(GUIFrame parent)
            {
                parent.RemoveChild(frame);
            }
        }

        public void Initialize()
        {
            spectateIcon = GUI.Style.GetComponentStyle("SpectateIcon").Sprites[GUIComponent.ComponentState.None][0];
            disconnectedIcon = GUI.Style.GetComponentStyle("DisconnectedIcon").Sprites[GUIComponent.ComponentState.None][0];
            ownerIcon = GUI.Style.GetComponentStyle("OwnerIcon").GetDefaultSprite();
            moderatorIcon = GUI.Style.GetComponentStyle("ModeratorIcon").GetDefaultSprite();
            initialized = true;
        }

        public TabMenu()
        {
            if (!initialized) { Initialize(); }

            CreateInfoFrame(selectedTab);
            SelectInfoFrameTab(null, selectedTab);
        }

        public void Update()
        {
            if (selectedTab != InfoFrameTab.Crew) return;
            if (linkedGUIList == null) return;

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
            infoFrame?.AddToGUIUpdateList();
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
            Vector2 contentFrameSize;
            switch (selectedTab)
            {
                case InfoFrameTab.MyCharacter:
                    contentFrameSize = new Vector2(0.45f, 0.5f);
                    break;
                default:
                    contentFrameSize = new Vector2(0.45f, 0.667f);
                    break;
            }
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
                    OnClicked = SelectInfoFrameTab
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
                new GUITextBlock(new RectTransform(Vector2.One, balanceFrame.RectTransform), "", textAlignment: Alignment.Right, parseRichText: true)
                {
                    TextGetter = () => TextManager.GetWithVariable("campaignmoney", "[money]", string.Format(CultureInfo.InvariantCulture, "{0:N0}", campaignMode.Money))
                };
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

            if (GameMain.NetworkMember != null)
            {
                var myCharacterButton = createTabButton(InfoFrameTab.MyCharacter, "tabmenu.character");
            }

            var talentsButton = createTabButton(InfoFrameTab.Talents, "tabmenu.talents");
            talentsButton.OnAddedToGUIUpdateList += (GUIComponent component) =>
            {
                talentsButton.Enabled = Character.Controlled?.Info != null && (GameMain.GameSession?.Campaign != null || Screen.Selected == GameMain.TestScreen || GameMain.GameSession.GameMode is TestGameMode);
                if (!talentsButton.Enabled && selectedTab == InfoFrameTab.Talents)
                {
                    SelectInfoFrameTab(null, InfoFrameTab.Crew);
                }
            };
        }

        private bool SelectInfoFrameTab(GUIButton button, object userData)
        {
            selectedTab = (InfoFrameTab)userData;

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
                    if (GameMain.GameSession.RoundSummary != null && GameMain.GameSession.GameMode is CampaignMode campaignMode)
                    {
                        infoFrameHolder.ClearChildren();
                        GUIFrame reputationFrame = new GUIFrame(new RectTransform(Vector2.One, infoFrameHolder.RectTransform, Anchor.TopCenter), style: "GUIFrameListBox");
                        GameMain.GameSession.RoundSummary.CreateReputationInfoPanel(reputationFrame, campaignMode);
                    }
                    break;
                case InfoFrameTab.Traitor:
                    TraitorMissionPrefab traitorMission = GameMain.Client.TraitorMission;
                    Character traitor = GameMain.Client.Character;
                    if (traitor == null || traitorMission == null) return false;
                    CreateTraitorInfo(infoFrameHolder, traitorMission, traitor);
                    break;
                case InfoFrameTab.MyCharacter:
                    if (GameMain.NetworkMember == null) { return false; }
                    GameMain.NetLobbyScreen.CreatePlayerFrame(infoFrameHolder);
                    break;
                case InfoFrameTab.Submarine:
                    CreateSubmarineInfo(infoFrameHolder, Submarine.MainSub);
                    break;
                case InfoFrameTab.Talents:
                    CreateTalentInfo(infoFrameHolder);
                    break;
            }

            return true;
        }

        private const float jobColumnWidthPercentage = 0.138f;
        private const float characterColumnWidthPercentage = 0.656f;
        private const float pingColumnWidthPercentage = 0.206f;

        private int jobColumnWidth, characterColumnWidth, pingColumnWidth;

        private void CreateCrewListFrame(GUIFrame crewFrame)
        {
            crew = GameMain.GameSession.CrewManager.GetCharacters();
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
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, nameHeight), content.RectTransform), CombatMission.GetTeamName(teamIDs[i]), textColor: i == 0 ? GUI.Style.Green : GUI.Style.Orange) { ForceUpperCase = true };
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
                headerFrames[i].RectTransform.RelativeSize = new Vector2(1f - crewListArray[i].ScrollBar.Rect.Width / (float)crewListArray[i].Rect.Width, GUI.HotkeyFont.Size / (float)crewFrame.RectTransform.Rect.Height * 1.5f);

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

            jobButton.TextBlock.Font = characterButton.TextBlock.Font = GUI.HotkeyFont;
            jobButton.CanBeFocused = characterButton.CanBeFocused = false;
            jobButton.TextBlock.ForceUpperCase = characterButton.TextBlock.ForceUpperCase = true;

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
                ToolBox.LimitString(character.Info.Name, GUI.Font, characterColumnWidth), textAlignment: Alignment.Center, textColor: character.Info.Job.Prefab.UIColor);

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

            jobButton.TextBlock.Font = characterButton.TextBlock.Font = pingButton.TextBlock.Font = GUI.HotkeyFont;
            jobButton.CanBeFocused = characterButton.CanBeFocused = pingButton.CanBeFocused = false;
            jobButton.TextBlock.ForceUpperCase = characterButton.TextBlock.ForceUpperCase = pingButton.ForceUpperCase = true;

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
                GameMain.GameSession?.CrewManager?.CreateModerationContextMenu(PlayerInput.MousePosition.ToPoint(), client);
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
                    ToolBox.LimitString(character.Info.Name, GUI.Font, characterColumnWidth), textAlignment: Alignment.Center, textColor: character.Info.Job.Prefab.UIColor);

                if (character is AICharacter)
                {
                    linkedGUIList.Add(new LinkedGUI(character, frame, !character.IsDead, new GUITextBlock(new RectTransform(new Point(pingColumnWidth, paddedFrame.Rect.Height), paddedFrame.RectTransform), TextManager.Get("tabmenu.bot"), textAlignment: Alignment.Center) { ForceUpperCase = true }));
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
                    ToolBox.LimitString(client.Name, GUI.Font, (int)(characterColumnWidth - paddedFrame.Rect.Width * characterNameWidthAdjustment)), textAlignment: Alignment.Center, textColor: nameColor);

                float iconWidth = iconSize.X / (float)characterColumnWidth;
                int xOffset = (int)(jobColumnWidth + characterNameBlock.TextPos.X - GUI.Font.MeasureString(characterNameBlock.Text).X / 2f - paddedFrame.AbsoluteSpacing - iconWidth * paddedFrame.Rect.Width);
                new GUIImage(new RectTransform(new Vector2(iconWidth, 1f), paddedFrame.RectTransform) { AbsoluteOffset = new Point(xOffset + 2, 0) }, permissionIcon) { IgnoreLayoutGroups = true };
            }
            else
            {
                characterNameBlock = new GUITextBlock(new RectTransform(new Point(characterColumnWidth, paddedFrame.Rect.Height), paddedFrame.RectTransform),
                    ToolBox.LimitString(client.Name, GUI.Font, characterColumnWidth), textAlignment: Alignment.Center, textColor: nameColor);
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
                Vector2 stringOffset = GUI.GlobalFont.MeasureString(inLobbyString) / 2f;
                GUI.GlobalFont.DrawString(spriteBatch, inLobbyString, area.Center.ToVector2() - stringOffset, Color.White);
            }
        }

        private void DrawDisconnectedIcon(SpriteBatch spriteBatch, Rectangle area)
        {
            disconnectedIcon.Draw(spriteBatch, area, GUI.Style.Red);
        }

        /// <summary>
        /// Select an element from CrewListFrame
        /// </summary>
        private bool SelectElement(object userData, GUIComponent crewList)
        {
            Character character = userData as Character;
            Client client = userData as Client;

            GUIComponent existingPreview = infoFrameHolder.FindChild("SelectedCharacter");
            if (existingPreview != null) infoFrameHolder.RemoveChild(existingPreview);

            GUIFrame background = new GUIFrame(new RectTransform(new Vector2(0.543f, 0.717f), infoFrameHolder.RectTransform, Anchor.TopLeft, Pivot.TopRight) { RelativeOffset = new Vector2(-0.145f, 0) })
            {
                UserData = "SelectedCharacter"
            };

            if (character != null)
            {
                if (GameMain.NetworkMember == null)
                {
                    GUIComponent preview = character.Info.CreateInfoFrame(background, false, null);
                }
                else
                {
                    GUIComponent preview = character.Info.CreateInfoFrame(background, false, GetPermissionIcon(GameMain.Client.ConnectedClients.Find(c => c.Character == character)));
                    GameMain.Client.SelectCrewCharacter(character, preview);
                }
            }
            else if (client != null)
            {
                GUIComponent preview = CreateClientInfoFrame(background, client, GetPermissionIcon(client));
                if (GameMain.NetworkMember != null) GameMain.Client.SelectCrewClient(client, preview);
            }

            return true;
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

                ScalableFont font = paddedFrame.Rect.Width < 280 ? GUI.SmallFont : GUI.Font;

                var headerTextArea = new GUILayoutGroup(new RectTransform(new Vector2(0.575f, 1.0f), headerArea.RectTransform))
                {
                    RelativeSpacing = 0.02f,
                    Stretch = true
                };

                GUITextBlock clientNameBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), headerTextArea.RectTransform), ToolBox.LimitString(client.Name, GUI.Font, headerTextArea.Rect.Width), textColor: Color.White, font: GUI.Font)
                {
                    ForceUpperCase = true,
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

            if (GameSession.IsTabMenuOpen && selectedTab == InfoFrameTab.Crew)
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
                    textColor = GUI.Style.Green;
                    break;
                case PlayerConnectionChangeType.Kicked:
                    textColor = GUI.Style.Orange;
                    break;
                case PlayerConnectionChangeType.Disconnected:
                    textColor = GUI.Style.Yellow;
                    break;
                case PlayerConnectionChangeType.Banned:
                    textColor = GUI.Style.Red;
                    break;
            }

            if (logList != null)
            {
                var textBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), logList.Content.RectTransform), line, wrap: true, font: GUI.SmallFont, parseRichText: true)
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
                            OnClick = GameMain.NetLobbyScreen.SelectPlayer
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

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), locationInfoContainer.RectTransform), location.Name, font: GUI.LargeFont);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), locationInfoContainer.RectTransform), location.Type.Name, font: GUI.SubHeadingFont);

            var biomeLabel = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), locationInfoContainer.RectTransform),
                TextManager.Get("Biome", fallBackTag: "location"), font: GUI.SubHeadingFont, textAlignment: Alignment.CenterLeft);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), biomeLabel.RectTransform), Level.Loaded.LevelData.Biome.DisplayName, textAlignment: Alignment.CenterRight);
            var difficultyLabel = new GUITextBlock(new RectTransform(new Vector2(0.5f, 0.0f), locationInfoContainer.RectTransform),
                TextManager.Get("LevelDifficulty"), font: GUI.SubHeadingFont, textAlignment: Alignment.CenterLeft);
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
                int iconSize = (int)(GUI.LargeFont.MeasureChar('T').Y + GUI.Font.MeasureChar('T').Y * 4 + spacing * 4);

                foreach (Mission mission in GameMain.GameSession.Missions)
                {
                    GUIFrame missionDescriptionHolder = new GUIFrame(new RectTransform(Vector2.One, missionList.Content.RectTransform), style: null);
                    GUILayoutGroup missionTextGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.744f, 0f), missionDescriptionHolder.RectTransform, Anchor.CenterLeft) { AbsoluteOffset = new Point(iconSize + spacing, 0) }, false, childAnchor: Anchor.TopLeft)
                    {
                        AbsoluteSpacing = spacing
                    };
                    string descriptionText = mission.Description;
                    foreach (string missionMessage in mission.ShownMessages)
                    {
                        descriptionText += "\n\n" + missionMessage;
                    }
                    string rewardText = mission.GetMissionRewardText(Submarine.MainSub);
                    string reputationText = mission.GetReputationRewardText(mission.Locations[0]);

                    var missionNameRichTextData = RichTextData.GetRichTextData(mission.Name, out string missionNameString);
                    var missionRewardRichTextData = RichTextData.GetRichTextData(rewardText, out string missionRewardString);
                    var missionReputationRichTextData = RichTextData.GetRichTextData(reputationText, out string missionReputationString);
                    var missionDescriptionRichTextData = RichTextData.GetRichTextData(descriptionText, out string missionDescriptionString);

                    missionNameString = ToolBox.WrapText(missionNameString, missionTextGroup.Rect.Width, GUI.LargeFont);
                    missionRewardString = ToolBox.WrapText(missionRewardString, missionTextGroup.Rect.Width, GUI.Font);
                    missionReputationString = ToolBox.WrapText(missionReputationString, missionTextGroup.Rect.Width, GUI.Font);
                    missionDescriptionString = ToolBox.WrapText(missionDescriptionString, missionTextGroup.Rect.Width, GUI.Font);

                    Vector2 missionNameSize = GUI.LargeFont.MeasureString(missionNameString);
                    Vector2 missionDescriptionSize = GUI.Font.MeasureString(missionDescriptionString);
                    Vector2 missionRewardSize = GUI.Font.MeasureString(missionRewardString);
                    Vector2 missionReputationSize = GUI.Font.MeasureString(missionReputationString);

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

                        new GUIImage(new RectTransform(new Point(iconSize), missionDescriptionHolder.RectTransform), mission.Prefab.Icon, null, true)
                        {
                            Color = mission.Prefab.IconColor,
                            HoverColor = mission.Prefab.IconColor,
                            SelectedColor = mission.Prefab.IconColor,
                            CanBeFocused = false
                        };
                    }
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextGroup.RectTransform), missionNameRichTextData, missionNameString, font: GUI.LargeFont);
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
                    var rewardTextBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextGroup.RectTransform), missionRewardRichTextData, missionRewardString);
                    if (difficultyIndicatorGroup != null)
                    {
                        difficultyIndicatorGroup.RectTransform.Resize(new Point((int)(difficultyIndicatorGroup.Rect.Width - rewardTextBlock.Padding.X - rewardTextBlock.Padding.Z), difficultyIndicatorGroup.Rect.Height));
                        difficultyIndicatorGroup.RectTransform.AbsoluteOffset = new Point((int)rewardTextBlock.Padding.X, 0);
                    }
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextGroup.RectTransform), missionReputationRichTextData, missionReputationString);
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextGroup.RectTransform), missionDescriptionRichTextData, missionDescriptionString);
                }
            }
            else
            {
                GUILayoutGroup missionTextGroup = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0f), missionList.RectTransform, Anchor.CenterLeft), false, childAnchor: Anchor.TopLeft);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextGroup.RectTransform), TextManager.Get("NoMission"), font: GUI.LargeFont);
            }
        }

        private void CreateTraitorInfo(GUIFrame infoFrame, TraitorMissionPrefab traitorMission, Character traitor)
        {
            GUIFrame missionFrame = new GUIFrame(new RectTransform(Vector2.One, infoFrame.RectTransform, Anchor.TopCenter), style: "GUIFrameListBox");

            int padding = (int)(0.0245f * missionFrame.Rect.Height);

            GUIFrame missionDescriptionHolder = new GUIFrame(new RectTransform(new Point(missionFrame.Rect.Width - padding * 2, 0), missionFrame.RectTransform, Anchor.TopCenter) { AbsoluteOffset = new Point(0, padding) }, style: null);
            GUILayoutGroup missionTextGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.65f, 0f), missionDescriptionHolder.RectTransform, Anchor.CenterLeft) { RelativeOffset = new Vector2(0.319f, 0f) }, false, childAnchor: Anchor.TopLeft);

            string missionNameString = ToolBox.WrapText(TextManager.Get("tabmenu.traitor"), missionTextGroup.Rect.Width, GUI.LargeFont);
            string missionDescriptionString = ToolBox.WrapText(traitor.TraitorCurrentObjective, missionTextGroup.Rect.Width, GUI.Font);

            Vector2 missionNameSize = GUI.LargeFont.MeasureString(missionNameString);
            Vector2 missionDescriptionSize = GUI.Font.MeasureString(missionDescriptionString);

            missionDescriptionHolder.RectTransform.NonScaledSize = new Point(missionDescriptionHolder.RectTransform.NonScaledSize.X, (int)(missionNameSize.Y + missionDescriptionSize.Y));
            missionTextGroup.RectTransform.NonScaledSize = new Point(missionTextGroup.RectTransform.NonScaledSize.X, missionDescriptionHolder.RectTransform.NonScaledSize.Y);

            float aspectRatio = traitorMission.Icon.SourceRect.Width / traitorMission.Icon.SourceRect.Height;

            int iconWidth = (int)(0.319f * missionDescriptionHolder.RectTransform.NonScaledSize.X);
            int iconHeight = Math.Max(missionTextGroup.RectTransform.NonScaledSize.Y, (int)(iconWidth * aspectRatio));
            Point iconSize = new Point(iconWidth, iconHeight);

            new GUIImage(new RectTransform(iconSize, missionDescriptionHolder.RectTransform), traitorMission.Icon, null, true) { Color = traitorMission.IconColor };

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextGroup.RectTransform), missionNameString, font: GUI.LargeFont);
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

            string className = !sub.Info.HasTag(SubmarineTag.Shuttle) ? TextManager.Get($"submarineclass.{sub.Info.SubmarineClass}") : TextManager.Get("shuttle");

            int nameHeight = (int)GUI.LargeFont.MeasureString(sub.Info.DisplayName, true).Y;
            int classHeight = (int)GUI.SubHeadingFont.MeasureString(className).Y;

            var submarineNameText = new GUITextBlock(new RectTransform(new Point(subInfoTextLayout.Rect.Width, nameHeight + HUDLayoutSettings.Padding / 2), subInfoTextLayout.RectTransform), sub.Info.DisplayName, textAlignment: Alignment.CenterLeft, font: GUI.LargeFont) { CanBeFocused = false };
            submarineNameText.RectTransform.MinSize = new Point(0, (int)submarineNameText.TextSize.Y);
            var submarineClassText = new GUITextBlock(new RectTransform(new Point(subInfoTextLayout.Rect.Width, classHeight), subInfoTextLayout.RectTransform), className, textAlignment: Alignment.CenterLeft, font: GUI.SubHeadingFont) { CanBeFocused = false };
            submarineClassText.RectTransform.MinSize = new Point(0, (int)submarineClassText.TextSize.Y);

            if (GameMain.GameSession?.GameMode is CampaignMode campaign)
            {
                GUILayoutGroup headerLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.09f), paddedFrame.RectTransform) { RelativeOffset = new Vector2(0f, 0.43f) }, isHorizontal: true) { Stretch = true };
                GUIImage headerIcon = new GUIImage(new RectTransform(Vector2.One, headerLayout.RectTransform, scaleBasis: ScaleBasis.BothHeight), style: "SubmarineIcon");
                new GUITextBlock(new RectTransform(Vector2.One, headerLayout.RectTransform), TextManager.Get("uicategory.upgrades"), font: GUI.LargeFont);

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
                sub.Info.CreateSpecsWindow(specsListBox, GUI.Font, includeTitle: false, includeClass: false, includeDescription: true);
            }
        }
        private Color unselectedColor = new Color(240, 255, 255, 225);
        private Color selectedColor = new Color(220, 255, 220, 225);
        private Color ownedColor = new Color(140, 180, 140, 225);
        private Color unselectableColor = new Color(100, 100, 100, 225);
        private Color pressedColor = new Color(60, 60, 60, 225);

        private readonly List<(GUIButton button, GUIComponent icon, GUIImage glow)> talentButtons = new List<(GUIButton button, GUIComponent icon, GUIImage glow)>();
        private readonly List<(string talentTree, int index, GUIImage icon, GUIFrame background, GUIFrame backgroundGlow)> talentCornerIcons = new List<(string talentTree, int index, GUIImage icon, GUIFrame background, GUIFrame backgroundGlow)>();
        private List<string> selectedTalents = new List<string>();

        private GUITextBlock experienceText;
        private GUIProgressBar experienceBar;
        private GUITextBlock talentPointText;
        private GUIListBox skillListBox;

        private readonly ImmutableDictionary<TalentTree.TalentTreeStageState, GUIComponentStyle> talentStageStyles = new Dictionary<TalentTree.TalentTreeStageState, GUIComponentStyle>
        {
            { TalentTree.TalentTreeStageState.Invalid, GUI.Style.GetComponentStyle("TalentTreeLocked") },
            { TalentTree.TalentTreeStageState.Locked, GUI.Style.GetComponentStyle("TalentTreeLocked") },
            { TalentTree.TalentTreeStageState.Unlocked, GUI.Style.GetComponentStyle("TalentTreePurchased") },
            { TalentTree.TalentTreeStageState.Available, GUI.Style.GetComponentStyle("TalentTreeUnlocked") },
            { TalentTree.TalentTreeStageState.Highlighted, GUI.Style.GetComponentStyle("TalentTreeAvailable") },
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

            GUIFrame paddedTalentFrame = new GUIFrame(new RectTransform(new Vector2(0.9f), talentFrameContent.RectTransform, Anchor.Center), style: null);

            if (controlledCharacter.Info == null)
            {
                DebugConsole.ThrowError("No character info found for talent UI");
                return;
            }

            selectedTalents = controlledCharacter.Info.UnlockedTalents.ToList();

            GUILayoutGroup talentFrameLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 1.0f), paddedTalentFrame.RectTransform, anchor: Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                AbsoluteSpacing = GUI.IntScale(5)
            };

            GUILayoutGroup talentInfoLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.25f), talentFrameLayoutGroup.RectTransform, Anchor.Center), isHorizontal: true);

            CharacterInfo info = controlledCharacter.Info;
            Job job = info.Job;

            new GUICustomComponent(new RectTransform(new Vector2(0.25f, 1f), talentInfoLayoutGroup.RectTransform), onDraw: (batch, component) =>
            {
                float posY = component.Rect.Bottom - component.Rect.Width;
                info.DrawPortrait(batch, new Vector2(component.Rect.X, posY), Vector2.Zero, component.Rect.Width, false, false);
            });

            GUILayoutGroup nameLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.375f, 1f), talentInfoLayoutGroup.RectTransform));

            Vector2 nameSize = GUI.SubHeadingFont.MeasureString(info.Name);
            GUITextBlock nameBlock = new GUITextBlock(new RectTransform(Vector2.One, nameLayout.RectTransform), info.Name, font: GUI.SubHeadingFont) { TextColor = job.Prefab.UIColor };
            nameBlock.RectTransform.NonScaledSize = nameSize.Pad(nameBlock.Padding).ToPoint();

            Vector2 jobSize = GUI.SmallFont.MeasureString(job.Name);
            GUITextBlock jobBlock = new GUITextBlock(new RectTransform(Vector2.One, nameLayout.RectTransform), job.Name, font: GUI.SmallFont) { TextColor = job.Prefab.UIColor };
            jobBlock.RectTransform.NonScaledSize = jobSize.Pad(jobBlock.Padding).ToPoint();

            string traitString = TextManager.AddPunctuation(':', TextManager.Get("PersonalityTrait"), TextManager.Get("personalitytrait." + info.PersonalityTrait.Name.Replace(" ", "")));
            Vector2 traitSize = GUI.SmallFont.MeasureString(traitString);
            GUITextBlock traitBlock = new GUITextBlock(new RectTransform(Vector2.One, nameLayout.RectTransform), traitString, font: GUI.SmallFont);
            traitBlock.RectTransform.NonScaledSize = traitSize.Pad(traitBlock.Padding).ToPoint();

            GUILayoutGroup skillLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.375f, 1f), talentInfoLayoutGroup.RectTransform)) { Stretch = true };

            string skillString = TextManager.Get("skills");
            Vector2 skillSize = GUI.SubHeadingFont.MeasureString(skillString);
            GUITextBlock skillBlock = new GUITextBlock(new RectTransform(Vector2.One, skillLayout.RectTransform), skillString, font: GUI.SubHeadingFont);
            skillBlock.RectTransform.NonScaledSize = skillSize.Pad(skillBlock.Padding).ToPoint();

            skillListBox = new GUIListBox(new RectTransform(new Vector2(1f, 1f - skillBlock.RectTransform.RelativeSize.Y), skillLayout.RectTransform), style: null);
            CreateTalentSkillList(controlledCharacter, skillListBox);

            if (!TalentTree.JobTalentTrees.TryGetValue(controlledCharacter.Info.Job.Prefab.Identifier, out TalentTree talentTree)) { return; }

            new GUIFrame(new RectTransform(new Vector2(1f, 1f), talentFrameLayoutGroup.RectTransform), style: "HorizontalLine");

            GUIListBox talentTreeListBox = new GUIListBox(new RectTransform(new Vector2(1f, 0.7f), talentFrameLayoutGroup.RectTransform, Anchor.TopCenter), isHorizontal: true, style: null);

            foreach (var subTree in talentTree.TalentSubTrees)
            {
                GUIFrame subTreeFrame = new GUIFrame(new RectTransform(new Vector2(0.333f, 1f), talentTreeListBox.Content.RectTransform, anchor: Anchor.TopLeft), style: null);
                GUILayoutGroup subTreeLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(1f, 1f), subTreeFrame.RectTransform, Anchor.Center), false, childAnchor: Anchor.TopCenter);

                GUIFrame subtreeTitleFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.111f), subTreeLayoutGroup.RectTransform, anchor: Anchor.TopCenter), style: null);
                int elementPadding = GUI.IntScale(8);
                Point headerSize = subtreeTitleFrame.RectTransform.NonScaledSize;
                GUIFrame subTreeTitleBackground = new GUIFrame(new RectTransform(new Point(headerSize.X - elementPadding, headerSize.Y), subtreeTitleFrame.RectTransform, anchor: Anchor.Center), style: "SubtreeHeader");
                new GUITextBlock(new RectTransform(Vector2.One, subTreeTitleBackground.RectTransform, anchor: Anchor.TopCenter), subTree.DisplayName, font: GUI.LargeFont, textAlignment: Alignment.Center);

                for (int i = 0; i < 4; i++)
                {
                    GUIFrame talentOptionFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.222f), subTreeLayoutGroup.RectTransform, anchor: Anchor.TopCenter), style: null);

                    Point talentFrameSize = talentOptionFrame.RectTransform.NonScaledSize;

                    GUIFrame talentBackground = new GUIFrame(new RectTransform(new Point(talentFrameSize.X - elementPadding, talentFrameSize.Y - elementPadding), talentOptionFrame.RectTransform, anchor: Anchor.Center), style: "TalentBackground");
                    GUIFrame talentBackgroundHighlight = new GUIFrame(new RectTransform(Vector2.One, talentBackground.RectTransform, anchor: Anchor.Center), style: "TalentBackgroundGlow") { Visible = false };

                    GUIImage cornerIcon = new GUIImage(new RectTransform(new Vector2(0.25f), talentOptionFrame.RectTransform, anchor: Anchor.BottomRight, scaleBasis: ScaleBasis.BothHeight), style: null)
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

                        foreach (TalentPrefab talent in talentOption.Talents)
                        {
                            GUIFrame talentFrame = new GUIFrame(new RectTransform(Vector2.One, talentOptionLayoutGroup.RectTransform), style: null)
                            {
                                CanBeFocused = false,
                            };

                            GUIButton talentButton = new GUIButton(new RectTransform(Vector2.One, talentFrame.RectTransform, anchor: Anchor.Center), style: null)
                            {
                                ToolTip = $"{talent.DisplayName}\n\n{talent.Description}",
                                UserData = talent.Identifier,
                                PressedColor = pressedColor,
                                OnClicked = (button, userData) =>
                                {
                                    // deselect other buttons in tier by removing their selected talents from pool
                                    foreach (GUIButton guiButton in talentOptionLayoutGroup.GetAllChildren<GUIButton>())
                                    {
                                        if (guiButton.UserData is string otherTalentIdentifier && guiButton != button)
                                        {
                                            if (!controlledCharacter.HasTalent(otherTalentIdentifier))
                                            {
                                                selectedTalents.Remove(otherTalentIdentifier);
                                            }
                                        }
                                    }
                                    string talentIdentifier = userData as string;

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

                                    UpdateTalentButtons();
                                    return true;
                                },
                            };

                            talentButton.Color = talentButton.HoverColor = talentButton.PressedColor = talentButton.SelectedColor = Color.Transparent;

                            GUIComponent iconImage;
                            if (talent.Icon is null)
                            {
                                iconImage = new GUITextBlock(new RectTransform(Vector2.One, talentButton.RectTransform, anchor: Anchor.Center, scaleBasis: ScaleBasis.BothHeight), text: "???", font: GUI.LargeFont, textAlignment: Alignment.Center, style: null)
                                {
                                    OutlineColor = GUI.Style.Red,
                                    TextColor = GUI.Style.Red,
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

                            GUIImage iconGlow = new GUIImage(new RectTransform(Vector2.One, iconImage.RectTransform, anchor: Anchor.Center), sprite: GUI.Style.TalentGlow.Sprite, scaleToFit: true) { Visible = false };

                            talentButtons.Add((talentButton, iconImage, iconGlow));
                        }

                        talentCornerIcons.Add((subTree.Identifier, i, cornerIcon, talentBackground, talentBackgroundHighlight));
                    }
                }
            }

            GUILayoutGroup talentBottomFrame = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.07f), talentFrameLayoutGroup.RectTransform, Anchor.TopCenter), isHorizontal: true) { RelativeSpacing = 0.01f };

            GUILayoutGroup experienceLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.775f, 1f), talentBottomFrame.RectTransform));
            GUIFrame experienceBarFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.5f), experienceLayout.RectTransform), style: null);

            experienceBar = new GUIProgressBar(new RectTransform(new Vector2(1f, 1f), experienceBarFrame.RectTransform, Anchor.CenterLeft),
                barSize: controlledCharacter.Info.GetProgressTowardsNextLevel(), color: GUI.Style.Green)
            {
                IsHorizontal = true
            };

            experienceText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 1.0f), experienceBarFrame.RectTransform, anchor: Anchor.Center), "", font: GUI.Font, textAlignment: Alignment.CenterRight);

            talentPointText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.5f), experienceLayout.RectTransform, anchor: Anchor.Center), "", font: GUI.SubHeadingFont, parseRichText: true, textAlignment: Alignment.CenterRight);

            new GUIButton(new RectTransform(new Vector2(0.1f, 1f), talentBottomFrame.RectTransform), text: TextManager.Get("reset"), style: "GUICharacterInfoButton")
            {
                OnClicked = ResetTalentSelection,
            };

            new GUIButton(new RectTransform(new Vector2(0.1f, 1f), talentBottomFrame.RectTransform), text: TextManager.Get("applysettingsbutton"), style: "GUICharacterInfoButton")
            {
                OnClicked = ApplyTalentSelection,
            };

            UpdateTalentButtons();
        }

        private void CreateTalentSkillList(Character character, GUIListBox parent)
        {
            parent.Content.ClearChildren();
            foreach (Skill skill in character.Info.Job.Skills)
            {
                GUILayoutGroup skillContainer = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.2f), parent.Content.RectTransform), isHorizontal: true) { CanBeFocused = false };

                new GUITextBlock(new RectTransform(new Vector2(0.7f, 1f), skillContainer.RectTransform), TextManager.Get($"skillname.{skill.Identifier}", returnNull: true) ?? skill.Identifier);
                new GUITextBlock(new RectTransform(new Vector2(0.15f, 1.0f), skillContainer.RectTransform), Math.Floor(skill.Level).ToString("F0"), textAlignment: Alignment.CenterRight) { Padding = new Vector4(0, 0, 4, 0) };

                float modifiedSkillLevel = character.GetSkillLevel(skill.Identifier);
                if (!MathUtils.NearlyEqual(MathF.Floor(modifiedSkillLevel), MathF.Floor(skill.Level)))
                {
                    int skillChange = (int)MathF.Floor(modifiedSkillLevel - skill.Level);
                    string stringColor = true switch
                    {
                        true when skillChange > 0 => XMLExtensions.ColorToString(GUI.Style.Green),
                        true when skillChange < 0 => XMLExtensions.ColorToString(GUI.Style.Red),
                        _ => XMLExtensions.ColorToString(GUI.Style.TextColor)
                    };

                    string changeText = $"(‖color:{stringColor}‖{(skillChange > 0 ? "+" : string.Empty) + skillChange}‖color:end‖)";
                    new GUITextBlock(new RectTransform(new Vector2(0.15f, 1.0f), skillContainer.RectTransform), changeText, parseRichText: true) { Padding = Vector4.Zero };
                }
                skillContainer.Recalculate();
            }

            parent.RecalculateChildren();
        }

        private void UpdateTalentButtons()
        {
            Character controlledCharacter = Character.Controlled;

            experienceText.Text = $"{controlledCharacter.Info.ExperiencePoints - controlledCharacter.Info.GetExperienceRequiredForCurrentLevel()} / {controlledCharacter.Info.GetExperienceRequiredToLevelUp() - controlledCharacter.Info.GetExperienceRequiredForCurrentLevel()}";
            experienceBar.BarSize = controlledCharacter.Info.GetProgressTowardsNextLevel();
            //experienceBar.ToolTip = $"{controlledCharacter.Info.ExperiencePoints - controlledCharacter.Info.GetExperienceRequiredForCurrentLevel()} / {controlledCharacter.Info.GetExperienceRequiredToLevelUp() - controlledCharacter.Info.GetExperienceRequiredForCurrentLevel()}";

            selectedTalents = TalentTree.CheckTalentSelection(controlledCharacter, selectedTalents);

            string pointsLeft = controlledCharacter.Info.GetAvailableTalentPoints().ToString();

            int talentCount = selectedTalents.Count - controlledCharacter.Info.UnlockedTalents.Count;

            if (talentCount > 0)
            {
                string pointsUsed = $"‖color:{XMLExtensions.ColorToString(GUI.Style.Red)}‖{-talentCount}‖color:end‖";
                string localizedString = TextManager.GetWithVariables("talentmenu.points.spending", new []{ "[amount]", "[used]" }, new []{ pointsLeft, pointsUsed});
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
                string talentIdentifier = talentButton.button.UserData as string;
                bool unselectable = !TalentTree.IsViableTalentForCharacter(controlledCharacter, talentIdentifier, selectedTalents) || controlledCharacter.HasTalent(talentIdentifier);
                Color newTalentColor = unselectable ? unselectableColor : unselectedColor;

                talentButton.glow.Visible = false;

                if (controlledCharacter.HasTalent(talentIdentifier))
                {
                    newTalentColor = new Color(140,225,140,255);
                }
                else if (selectedTalents.Contains(talentIdentifier))
                {
                    newTalentColor = new Color(174,164,124,255);
                    talentButton.glow.Visible = true;
                }

                talentButton.icon.Color = newTalentColor;
            }

            CreateTalentSkillList(controlledCharacter, skillListBox);
        }

        private void ApplyTalents(Character controlledCharacter)
        {
            selectedTalents = TalentTree.CheckTalentSelection(controlledCharacter, selectedTalents);
            foreach (string talent in selectedTalents)
            {
                controlledCharacter.GiveTalent(talent);
                if (GameMain.Client != null)
                {
                    GameMain.Client.CreateEntityEvent(controlledCharacter, new object[] { NetEntityEvent.Type.UpdateTalents });
                }
            }
            UpdateTalentButtons();
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
            selectedTalents = controlledCharacter.Info.UnlockedTalents.ToList();
            UpdateTalentButtons();
            return true;
        }

        public void OnExperienceChanged(Character character)
        {
            if (character != Character.Controlled) { return; }
            UpdateTalentButtons();
        }

        private readonly StatTypes[] basicStats = new StatTypes[]
        {
            StatTypes.MaximumHealthMultiplier,
            StatTypes.MovementSpeed,
            StatTypes.SwimmingSpeed,
            StatTypes.RepairSpeed,
        };

        private readonly StatTypes[] combatStats = new StatTypes[]
        {
            StatTypes.MeleeAttackMultiplier,
            StatTypes.MeleeAttackSpeed,
            StatTypes.RangedAttackSpeed,
            StatTypes.TurretAttackSpeed,
        };

        private readonly StatTypes[] miscStats = new StatTypes[]
        {
            StatTypes.ReputationGainMultiplier,
            StatTypes.MissionMoneyGainMultiplier,
            StatTypes.ExperienceGainMultiplier,
            StatTypes.MissionExperienceGainMultiplier,
        };

        private void CreateCharacterSheet(GUILayoutGroup characterInfoColumn)
        {
            Character controlledCharacter = Character.Controlled;

            CreateRow(basicStats);
            CreateRow(combatStats);
            CreateRow(miscStats);

            void CreateRow(StatTypes[] statTypes)
            {
                GUILayoutGroup characterInfoRow = new GUILayoutGroup(new RectTransform(new Vector2(0.33f, 1.0f), characterInfoColumn.RectTransform, anchor: Anchor.TopLeft), childAnchor: Anchor.TopCenter);
                foreach (StatTypes statType in statTypes)
                {
                    ShowStat(statType, characterInfoRow);
                }
            }

            void ShowStat(StatTypes statType, GUILayoutGroup characterInfoRow)
            {
                GUIFrame textInfoFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.33f), characterInfoRow.RectTransform, Anchor.TopCenter), style: null);
                new GUITextBlock(new RectTransform(new Vector2(1f, 1f), textInfoFrame.RectTransform, Anchor.TopLeft), statType.ToString(), font: GUI.SmallFont, textAlignment: Alignment.TopLeft);
                new GUITextBlock(new RectTransform(new Vector2(1f, 1f), textInfoFrame.RectTransform, Anchor.TopLeft), (int)(100f * (1 + controlledCharacter.GetStatValue(statType))) + "%", font: GUI.Font, textAlignment: Alignment.TopRight);
            }
        }
    }
}
