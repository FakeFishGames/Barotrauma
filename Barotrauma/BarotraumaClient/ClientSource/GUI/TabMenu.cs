using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Linq;
using Barotrauma.Networking;

namespace Barotrauma
{
    class TabMenu
    {
        public static bool PendingChanges = false;

        private static bool initialized = false;

        private static UISprite spectateIcon, deadIcon, disconnectedIcon;
        private static Sprite ownerIcon, moderatorIcon;

        private enum InfoFrameTab { Crew, Mission, MyCharacter, Traitor };
        private static InfoFrameTab selectedTab;
        private GUIFrame infoFrame, contentFrame;

        private readonly List<GUIButton> tabButtons = new List<GUIButton>();
        private GUIFrame infoFrameHolder;
        private List<LinkedGUI> linkedGUIList;
        private GUIListBox logList;
        private GUIListBox[] crewListArray;
        private float sizeMultiplier = 1f;

        private IEnumerable<Character> crew;
        private List<Character.TeamType> teamIDs;
        private const string inLobbyString = "\u2022 \u2022 \u2022";

        private static Color ownCharacterBGColor = Color.Gold * 0.7f;

        private class LinkedGUI
        {
            private const ushort lowPingThreshold = 100;
            private const ushort mediumPingThreshold = 200;

            private ushort currentPing;
            private Client client;
            private Character character;
            private bool hasCharacter;
            private GUITextBlock textBlock;
            private GUIFrame frame;

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
            deadIcon = GUI.Style.GetComponentStyle("DeadIcon").Sprites[GUIComponent.ComponentState.None][0];
            disconnectedIcon = GUI.Style.GetComponentStyle("DisconnectedIcon").Sprites[GUIComponent.ComponentState.None][0];
            ownerIcon = GUI.Style.GetComponentStyle("OwnerIcon").Sprites[GUIComponent.ComponentState.None][0].Sprite;
            moderatorIcon = GUI.Style.GetComponentStyle("ModeratorIcon").Sprites[GUIComponent.ComponentState.None][0].Sprite;
            initialized = true;
        }

        public TabMenu()
        {
            if (!initialized) Initialize();

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

            infoFrame = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: "GUIBackgroundBlocker");

            switch (selectedTab)
            {
                case InfoFrameTab.Crew:
                case InfoFrameTab.Mission:
                case InfoFrameTab.Traitor:
                default:
                    contentFrame = new GUIFrame(new RectTransform(new Vector2(0.33f, 0.667f), infoFrame.RectTransform, Anchor.TopCenter, Pivot.TopCenter) { /*MinSize = new Point(width, height),*/ RelativeOffset = new Vector2(0.025f, 0.12f) });
                    break;
                case InfoFrameTab.MyCharacter:
                    contentFrame = new GUIFrame(new RectTransform(new Vector2(0.33f, 0.5f), infoFrame.RectTransform, Anchor.TopCenter, Pivot.TopCenter) { /*MinSize = new Point(width, height),*/ RelativeOffset = new Vector2(0.025f, 0.12f) });
                    break;
            }

            var innerFrame = new GUIFrame(new RectTransform(new Vector2(0.958f, 0.943f), contentFrame.RectTransform, Anchor.TopCenter, Pivot.TopCenter) { AbsoluteOffset = new Point(0, GUI.IntScale(17.5f)) }, style: null);
            var buttonArea = new GUILayoutGroup(new RectTransform(new Point(innerFrame.Rect.Width, GUI.IntScale(25f)), innerFrame.RectTransform) { AbsoluteOffset = new Point(2, 0) }, isHorizontal: true)
            {
                RelativeSpacing = 0.01f
            };

            infoFrameHolder = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.926f), innerFrame.RectTransform, Anchor.BottomCenter, Pivot.BottomCenter), style: null);

            var crewButton = new GUIButton(new RectTransform(new Vector2(0.245f, 1.0f), buttonArea.RectTransform), TextManager.Get("Crew"), style: "GUITabButton")
            {
                UserData = InfoFrameTab.Crew,
                OnClicked = SelectInfoFrameTab
            };
            tabButtons.Add(crewButton);

            var missionButton = new GUIButton(new RectTransform(new Vector2(0.245f, 1.0f), buttonArea.RectTransform), TextManager.Get("Mission"), style: "GUITabButton")
            {
                UserData = InfoFrameTab.Mission,
                OnClicked = SelectInfoFrameTab
            };
            tabButtons.Add(missionButton);

            bool isTraitor = GameMain.Client?.Character?.IsTraitor ?? false;
            if (isTraitor && GameMain.Client.TraitorMission != null)
            {
                var traitorButton = new GUIButton(new RectTransform(new Vector2(0.245f, 1.0f), buttonArea.RectTransform), TextManager.Get("tabmenu.traitor"), style: "GUITabButton")
                {
                    UserData = InfoFrameTab.Traitor,
                    OnClicked = SelectInfoFrameTab
                };
                tabButtons.Add(traitorButton);
            }

            if (GameMain.NetworkMember != null)
            {
                var myCharacterButton = new GUIButton(new RectTransform(new Vector2(0.245f, 1.0f), buttonArea.RectTransform), TextManager.Get("tabmenu.character"), style: "GUITabButton")
                {
                    UserData = InfoFrameTab.MyCharacter,
                    OnClicked = SelectInfoFrameTab
                };
                tabButtons.Add(myCharacterButton);
            }
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
            if (teamIDs.Count > 1 && GameMain.Client.Character != null)
            {
                Character.TeamType ownTeam = GameMain.Client.Character.TeamID;
                teamIDs = teamIDs.OrderBy(i => i != ownTeam).ThenBy(i => i).ToList();
            }

            if (!teamIDs.Any()) teamIDs.Add(Character.TeamType.None);

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
                Color = (Character.Controlled == character) ? ownCharacterBGColor : Color.Transparent
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
                    if (!(character is AICharacter) && connectedClients.Find(c => c.Character == null && c.Name == character.Name) != null) continue;
                    CreateMultiPlayerCharacterElement(character, GameMain.Client.ConnectedClients.Find(c => c.Character == character), i);
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
                Color = (GameMain.NetworkMember != null && GameMain.Client.Character == character) ? ownCharacterBGColor : Color.Transparent
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
            if (teamIDs.Count <= 1) return 0;

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

            if (permissionIcon != null)
            {
                Point iconSize = permissionIcon.SourceRect.Size;
                float characterNameWidthAdjustment = (iconSize.X + paddedFrame.AbsoluteSpacing) / characterColumnWidth;

                characterNameBlock = new GUITextBlock(new RectTransform(new Point(characterColumnWidth, paddedFrame.Rect.Height), paddedFrame.RectTransform),
                    ToolBox.LimitString(client.Name, GUI.Font, (int)(characterColumnWidth - paddedFrame.Rect.Width * characterNameWidthAdjustment)), textAlignment: Alignment.Center, textColor: client.Character != null ? client.Character.Info.Job.Prefab.UIColor : Color.White);

                float iconWidth = iconSize.X / (float)characterColumnWidth;
                int xOffset = (int)(jobColumnWidth + characterNameBlock.TextPos.X - GUI.Font.MeasureString(characterNameBlock.Text).X / 2f - paddedFrame.AbsoluteSpacing - iconWidth * paddedFrame.Rect.Width);
                new GUIImage(new RectTransform(new Vector2(iconWidth, 1f), paddedFrame.RectTransform) { AbsoluteOffset = new Point(xOffset + 2, 0) }, permissionIcon) { IgnoreLayoutGroups = true };
            }
            else
            {
                characterNameBlock = new GUITextBlock(new RectTransform(new Point(characterColumnWidth, paddedFrame.Rect.Height), paddedFrame.RectTransform),
                    ToolBox.LimitString(client.Name, GUI.Font, characterColumnWidth), textAlignment: Alignment.Center, textColor: client.Character != null ? client.Character.Info.Job.Prefab.UIColor : Color.White);
            }

            if (client.Character != null && client.Character.IsDead)
            {
                characterNameBlock.Strikethrough = new GUITextBlock.StrikethroughSettings(null, GUI.IntScale(1f), GUI.IntScale(5f));
            }
        }

        private Sprite GetPermissionIcon(Client client)
        {
            if (GameMain.NetworkMember == null || client == null || !client.HasPermissions) return null;

            if (!client.AllowKicking) // Owner cannot be kicked
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
                client.Character.Info.DrawJobIcon(spriteBatch, area);
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

            GUIFrame background = new GUIFrame(new RectTransform(new Vector2(0.543f, 0.717f), infoFrameHolder.RectTransform, Anchor.TopLeft, Pivot.TopRight) { RelativeOffset = new Vector2(-0.061f, 0) })
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

            if (client.Character == null)
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
            if (!GameMain.GameSession?.GameMode?.IsRunning ?? true) { return; }

            string msg = ChatMessage.GetTimeStamp() + message.TextWithSender;
            storedMessages.Add(new Pair<string, PlayerConnectionChangeType>(msg, message.ChangeType));

            if (GameSession.IsTabMenuOpen)
            {
                TabMenu instance = GameSession.TabMenuInstance;
                instance.AddLineToLog(msg, message.ChangeType);

                // Update crew
                if (selectedTab == InfoFrameTab.Crew)
                {
                    instance.RemoveCurrentElements();
                    instance.CreateMultiPlayerList(true);
                }
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

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), logList.Content.RectTransform), line, wrap: true, font: GUI.SmallFont)
            {
                TextColor = textColor,
                CanBeFocused = false,
                UserData = line
            }.CalculateHeightFromText();

            //if ((prevSize == 1.0f && listBox.BarScroll == 0.0f) || (prevSize < 1.0f && listBox.BarScroll == 1.0f)) listBox.BarScroll = 1.0f;
        }

        private void CreateMissionInfo(GUIFrame infoFrame)
        {
            infoFrame.ClearChildren();
            GUIFrame missionFrame = new GUIFrame(new RectTransform(Vector2.One, infoFrame.RectTransform, Anchor.TopCenter), style: "GUIFrameListBox");
            int padding = (int)(0.0245f * missionFrame.Rect.Height);
            Location endLocation = GameMain.GameSession.EndLocation;
            Sprite portrait = endLocation.Type.GetPortrait(endLocation.PortraitId);
            bool hasPortrait = portrait != null && portrait.SourceRect.Width > 0 && portrait.SourceRect.Height > 0;
            int contentWidth = hasPortrait ? (int)(missionFrame.Rect.Width * 0.951f) : missionFrame.Rect.Width - padding * 2;

            Vector2 locationNameSize = GUI.LargeFont.MeasureString(endLocation.Name);
            Vector2 locationTypeSize = GUI.SubHeadingFont.MeasureString(endLocation.Name);
            GUITextBlock locationNameText = new GUITextBlock(new RectTransform(new Point(contentWidth, (int)locationNameSize.Y), missionFrame.RectTransform, Anchor.TopCenter) { AbsoluteOffset = new Point(0, padding) }, endLocation.Name, font: GUI.LargeFont);
            GUITextBlock locationTypeText = new GUITextBlock(new RectTransform(new Point(contentWidth, (int)locationTypeSize.Y), missionFrame.RectTransform, Anchor.TopCenter) { AbsoluteOffset = new Point(0, locationNameText.Rect.Height + padding) }, endLocation.Type.Name, font: GUI.SubHeadingFont);

            int locationInfoYOffset = locationNameText.Rect.Height + locationTypeText.Rect.Height + padding * 2;

            GUIFrame missionDescriptionHolder;

            if (hasPortrait)
            {
                GUIFrame missionImageHolder = new GUIFrame(new RectTransform(new Point(contentWidth, (int)(missionFrame.Rect.Height * 0.588f)), missionFrame.RectTransform, Anchor.TopCenter) { AbsoluteOffset = new Point(0, locationInfoYOffset) });
                float portraitAspectRatio = portrait.SourceRect.Width / portrait.SourceRect.Height;
                GUIImage portraitImage = new GUIImage(new RectTransform(new Vector2(1.0f, 1f), missionImageHolder.RectTransform), portrait, scaleToFit: true);
                missionImageHolder.RectTransform.NonScaledSize = new Point(portraitImage.Rect.Size.X, (int)(portraitImage.Rect.Size.X / portraitAspectRatio));
                missionDescriptionHolder = new GUIFrame(new RectTransform(new Point(contentWidth, 0), missionFrame.RectTransform, Anchor.TopCenter) { AbsoluteOffset = new Point(0, missionImageHolder.RectTransform.AbsoluteOffset.Y + missionImageHolder.Rect.Height + padding) }, style: null);
            }
            else
            {
                missionDescriptionHolder = new GUIFrame(new RectTransform(new Point(contentWidth, 0), missionFrame.RectTransform, Anchor.TopCenter) { AbsoluteOffset = new Point(0, locationInfoYOffset) }, style: null);
            }               

            Mission mission = GameMain.GameSession?.Mission;
            if (mission != null)
            {
                GUILayoutGroup missionTextGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.744f, 0f), missionDescriptionHolder.RectTransform, Anchor.CenterLeft) { RelativeOffset = new Vector2(0.225f, 0f) }, false, childAnchor: Anchor.TopLeft);

                string missionNameString = ToolBox.WrapText(mission.Name, missionTextGroup.Rect.Width, GUI.LargeFont);
                string missionDescriptionString = ToolBox.WrapText(mission.Description, missionTextGroup.Rect.Width, GUI.Font);
                string missionRewardString = ToolBox.WrapText(TextManager.GetWithVariable("MissionReward", "[reward]", mission.Reward.ToString()), missionTextGroup.Rect.Width, GUI.Font);

                Vector2 missionNameSize = GUI.LargeFont.MeasureString(missionNameString);
                Vector2 missionDescriptionSize = GUI.Font.MeasureString(missionDescriptionString);
                Vector2 missionRewardSize = GUI.Font.MeasureString(missionRewardString);

                missionDescriptionHolder.RectTransform.NonScaledSize = new Point(missionDescriptionHolder.RectTransform.NonScaledSize.X, (int)(missionNameSize.Y + missionDescriptionSize.Y + missionRewardSize.Y));
                missionTextGroup.RectTransform.NonScaledSize = new Point(missionTextGroup.RectTransform.NonScaledSize.X, missionDescriptionHolder.RectTransform.NonScaledSize.Y);

                float iconAspectRatio = mission.Prefab.Icon.SourceRect.Width / mission.Prefab.Icon.SourceRect.Height;
                int iconWidth = (int)(0.225f * missionDescriptionHolder.RectTransform.NonScaledSize.X);
                int iconHeight = Math.Max(missionTextGroup.RectTransform.NonScaledSize.Y, (int)(iconWidth * iconAspectRatio));
                Point iconSize = new Point(iconWidth, iconHeight);

                new GUIImage(new RectTransform(iconSize, missionDescriptionHolder.RectTransform), mission.Prefab.Icon, null, true) { Color = mission.Prefab.IconColor };

                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextGroup.RectTransform), missionNameString, font: GUI.LargeFont);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextGroup.RectTransform), missionRewardString);
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), missionTextGroup.RectTransform), missionDescriptionString);
            }
            else
            {
                GUILayoutGroup missionTextGroup = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0f), missionDescriptionHolder.RectTransform, Anchor.CenterLeft), false, childAnchor: Anchor.TopLeft);
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
    }
}
