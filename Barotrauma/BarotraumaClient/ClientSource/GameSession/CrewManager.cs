using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class CrewManager
    {
        private Point screenResolution;

        public OrderPrefab DraggedOrderPrefab;
        public bool DragOrder;
        private bool dropOrder;
        private int framesToSkip = 2;
        private float dragOrderTreshold;
        private Vector2 dragPoint = Vector2.Zero;

        #region UI

        public GUIComponent ReportButtonFrame { get; set; }

        private GUIFrame guiFrame;
        private GUIFrame crewArea;
        private GUIListBox crewList;
        private float crewListOpenState;
        private bool _isCrewMenuOpen = true;
        private Point crewListEntrySize;

        /// <summary>
        /// Present only in single player games. In multiplayer. The chatbox is found from GameSession.Client.
        /// </summary>
        public ChatBox ChatBox { get; private set; }

        private float prevUIScale;

        public bool AllowCharacterSwitch = true;

        /// <summary>
        /// This property stores the preference in settings. Don't use for automatic logic.
        /// Use AutoShowCrewList(), AutoHideCrewList(), and ResetCrewList().
        /// </summary>
        public bool IsCrewMenuOpen
        {
            get { return _isCrewMenuOpen; }
            set
            {
                if (_isCrewMenuOpen == value) { return; }
                _isCrewMenuOpen = value;
                PreferCrewMenuOpen = value;
            }
        }

        public static bool PreferCrewMenuOpen = true;

        public bool AutoShowCrewList() => _isCrewMenuOpen = true;

        public void AutoHideCrewList() => _isCrewMenuOpen = false;

        public void ResetCrewList() => _isCrewMenuOpen = PreferCrewMenuOpen;

        const float CommandNodeAnimDuration = 0.2f;

        public List<GUIButton> OrderOptionButtons = new List<GUIButton>();

        private Sprite jobIndicatorBackground, previousOrderArrow, cancelIcon;

        #endregion

        #region Constructors

        public CrewManager(XElement element, bool isSinglePlayer)
            : this(isSinglePlayer)
        {
            AddCharacterElements(element);
        }

        partial void InitProjectSpecific()
        {
            guiFrame = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), null, Color.Transparent)
            {
                CanBeFocused = false
            };

            #region Crew Area

            crewArea = new GUIFrame(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.CrewArea, guiFrame.RectTransform), style: null, color: Color.Transparent)
            {
                CanBeFocused = false
            };

            // AbsoluteOffset is set in UpdateProjectSpecific based on crewListOpenState
            crewList = new GUIListBox(new RectTransform(Vector2.One, crewArea.RectTransform), style: null, isScrollBarOnDefaultSide: false)
            {
                AutoHideScrollBar = false,
                CanBeFocused = false,
                CurrentDragMode = GUIListBox.DragMode.DragWithinBox,
                CanInteractWhenUnfocusable = true,
                OnSelected = (component, userData) => false,
                SelectMultiple = false,
                Spacing = (int)(GUI.Scale * 10),
                OnRearranged = OnCrewListRearranged
            };

            jobIndicatorBackground = new Sprite("Content/UI/CommandUIAtlas.png", new Rectangle(0, 512, 128, 128));
            previousOrderArrow = new Sprite("Content/UI/CommandUIAtlas.png", new Rectangle(128, 512, 128, 128));
            cancelIcon = new Sprite("Content/UI/CommandUIAtlas.png", new Rectangle(512, 384, 128, 128));

            // Calculate and store crew list entry size so it doesn't have to be calculated for every entry
            crewListEntrySize = new Point(crewList.Content.Rect.Width - HUDLayoutSettings.Padding, 0);
            int crewListEntryMinHeight = 32;
            crewListEntrySize.Y = Math.Max(crewListEntryMinHeight, (int)(crewListEntrySize.X / 8f));
            float charactersPerView = crewList.Content.Rect.Height / (float)(crewListEntrySize.Y + crewList.Spacing);
            int adjustedHeight = (int)Math.Ceiling(crewList.Content.Rect.Height / Math.Round(charactersPerView)) - crewList.Spacing;
            if (adjustedHeight < crewListEntryMinHeight) { adjustedHeight = (int)Math.Ceiling(crewList.Content.Rect.Height / Math.Floor(charactersPerView)) - crewList.Spacing; }
            crewListEntrySize.Y = adjustedHeight;

            #endregion

            #region Chatbox

            if (IsSinglePlayer)
            {
                ChatBox = new ChatBox(guiFrame, isSinglePlayer: true)
                {
                    OnEnterMessage = (textbox, text) =>
                    {
                        if (Character.Controlled?.Info == null)
                        {
                            textbox.Deselect();
                            textbox.Text = "";
                            return true;
                        }

                        textbox.TextColor = ChatMessage.MessageColor[(int)ChatMessageType.Default];

                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            string msgCommand = ChatMessage.GetChatMessageCommand(text, out string msg);
                            // add to local history
                            ChatBox.ChatManager.Store(text);
                            bool isUsingRadioMode = GameMain.ActiveChatMode == ChatMode.Radio;
                            bool containsRadioCommand = msgCommand == "r" || msgCommand == "radio";
                            bool canUseRadio = ChatMessage.CanUseRadio(Character.Controlled, out WifiComponent headset);
                            ChatMessageType messageType = ((isUsingRadioMode && msgCommand == "") || containsRadioCommand) && canUseRadio ? ChatMessageType.Radio : ChatMessageType.Default;
                            AddSinglePlayerChatMessage(
                                Character.Controlled.Info.Name,
                                msg, messageType,
                                Character.Controlled);
                            if (messageType == ChatMessageType.Radio && headset != null)
                            {
                                Signal s = new Signal(msg, sender: Character.Controlled, source: headset.Item);
                                headset.TransmitSignal(s, sentFromChat: true);
                            }
                        }
                        textbox.Deselect();
                        textbox.Text = "";
                        if (ChatBox.CloseAfterMessageSent) 
                        {
                            ChatBox.ToggleOpen = false;
                            ChatBox.CloseAfterMessageSent = false;
                        }
                        return true;
                    }
                };

                ChatBox.InputBox.OnTextChanged += ChatBox.TypingChatMessage;
            }

            #endregion

            #region Reports
            var chatBox = ChatBox ?? GameMain.Client?.ChatBox;
            if (chatBox != null)
            {
                chatBox.ToggleButton = new GUIButton(new RectTransform(new Point((int)(182f * GUI.Scale * 0.4f), (int)(99f * GUI.Scale * 0.4f)), chatBox.GUIFrame.Parent.RectTransform), style: "ChatToggleButton")
                {
                    ToolTip = TextManager.GetWithVariable("hudbutton.chatbox", "[key]", GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.ChatBox)),
                    ClampMouseRectToParent = false
                };
                chatBox.ToggleButton.RectTransform.AbsoluteOffset = new Point(0, HUDLayoutSettings.ChatBoxArea.Height - chatBox.ToggleButton.Rect.Height);
                chatBox.ToggleButton.OnClicked += (GUIButton btn, object userdata) =>
                {
                    chatBox.Toggle();
                    return true;
                };
            }

            var reports = OrderPrefab.Prefabs.Where(o => o.IsReport && o.SymbolSprite != null && !o.Hidden).OrderBy(o => o.Identifier).ToArray();
            if (reports.None())
            {
                DebugConsole.ThrowError("No valid orders for report buttons found! Cannot create report buttons. The orders for the report buttons must have 'targetallcharacters' attribute enabled and a valid 'symbolsprite' defined.");
                return;
            }

            ReportButtonFrame = new GUILayoutGroup(new RectTransform(
                new Point((HUDLayoutSettings.ChatBoxArea.Height - chatBox.ToggleButton.Rect.Height - (int)((reports.Length - 1) * 5 * GUI.Scale)) / reports.Length, HUDLayoutSettings.ChatBoxArea.Height - chatBox.ToggleButton.Rect.Height), guiFrame.RectTransform))
            {
                AbsoluteSpacing = (int)(5 * GUI.Scale),
                UserData = "reportbuttons",
                CanBeFocused = false,
                Visible = false
            };

            ReportButtonFrame.RectTransform.AbsoluteOffset = new Point(0, -chatBox.ToggleButton.Rect.Height);

            CreateReportButtons(this, ReportButtonFrame, reports, false);

            #endregion

            screenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            prevUIScale = GUI.Scale;
            _isCrewMenuOpen = PreferCrewMenuOpen = GameSettings.CurrentConfig.CrewMenuOpen;
        }

        public static void CreateReportButtons(CrewManager crewManager, GUIComponent parent, IReadOnlyList<OrderPrefab> reports, bool isHorizontal)
        {
            //report buttons
            foreach (OrderPrefab orderPrefab in reports)
            {
                if (!orderPrefab.IsReport || orderPrefab.SymbolSprite == null || orderPrefab.Hidden) { continue; }
                var btn = new GUIButton(new RectTransform(Vector2.One, parent.RectTransform, scaleBasis: isHorizontal ? ScaleBasis.BothHeight : ScaleBasis.BothWidth), style: null)
                {
                    OnClicked = (button, userData) =>
                    {
                        if (!CanIssueOrders || crewManager?.DraggedOrderPrefab != null) { return false; }
                        var sub = Character.Controlled.Submarine;
                        if (sub == null || sub.TeamID != Character.Controlled.TeamID || sub.Info.IsWreck) { return false; }

                        if (crewManager != null)
                        {
                            Order order = orderPrefab.CreateInstance(OrderPrefab.OrderTargetType.Entity, orderGiver: Character.Controlled);
                            crewManager.SetCharacterOrder(null, order);
                            if (crewManager.IsSinglePlayer) { HumanAIController.ReportProblem(Character.Controlled, order); }
                        }
                        return true;
                    },
                    UserData = orderPrefab,
                    ClampMouseRectToParent = false
                };
                btn.ToolTip = RichString.Rich($"‖color:{XMLExtensions.ColorToString(orderPrefab.Color)}‖{orderPrefab.Name}‖color:end‖\n{TextManager.Get("draganddropreports")}");

                if (crewManager != null)
                {
                    btn.OnButtonDown = () =>
                    {
                        crewManager.dragOrderTreshold = Math.Max(btn.Rect.Width, btn.Rect.Height) / 2f;
                        crewManager.DraggedOrderPrefab = orderPrefab;
                        crewManager.dropOrder = false;
                        crewManager.framesToSkip = 2;
                        crewManager.dragPoint = btn.Rect.Center.ToVector2();
                        return true;
                    };
                }

                new GUIFrame(new RectTransform(new Vector2(1.5f), btn.RectTransform, Anchor.Center), "OuterGlowCircular")
                {
                    Color = GUIStyle.Red * 0.8f,
                    HoverColor = GUIStyle.Red * 1.0f,
                    PressedColor = GUIStyle.Red * 0.6f,
                    UserData = "highlighted",
                    CanBeFocused = false,
                    Visible = false
                };

                var img = new GUIImage(new RectTransform(Vector2.One, btn.RectTransform), orderPrefab.SymbolSprite, scaleToFit: true)
                {
                    Color = orderPrefab.Color,
                    HoverColor = Color.Lerp(orderPrefab.Color, Color.White, 0.5f),
                    ToolTip = btn.ToolTip,
                    SpriteEffects = SpriteEffects.FlipHorizontally,
                    UserData = orderPrefab
                };
            }
        }

        #endregion

        #region Character list management

        public Rectangle GetActiveCrewArea()
        {
            return crewArea.Rect;
        }

        /// <summary>
        /// Add character to the list without actually adding it to the crew
        /// </summary>
        public GUIComponent AddCharacterToCrewList(Character character)
        {
            if (character == null) { return null; }
            if (crewList.Content.Children.Any(c => c.UserData as Character == character)) { return null; }

            var background = new GUIFrame(
                new RectTransform(crewListEntrySize, parent: crewList.Content.RectTransform, anchor: Anchor.TopRight),
                style: "CrewListBackground")
            {
                UserData = character,
                OnSecondaryClicked = (comp, data) =>
                {
                    if (data == null) { return false; }
                    if (GameMain.NetworkMember?.ConnectedClients?.Find(c => c.Character == data) is Client client)
                    {
                        NetLobbyScreen.CreateModerationContextMenu(client);
                        return true;
                    }
                    return false;
                }
            };
            SetCharacterComponentTooltip(background);

            var iconRelativeWidth = (float)crewListEntrySize.Y / background.Rect.Width;

            var layoutGroup = new GUILayoutGroup(
                new RectTransform(Vector2.One, parent: background.RectTransform),
                isHorizontal: true,
                childAnchor: Anchor.CenterLeft)
            {
                CanBeFocused = false,
                RelativeSpacing = 0.1f * iconRelativeWidth,
                UserData = character
            };

            var commandButtonAbsoluteHeight = Math.Min(40.0f, 0.67f * background.Rect.Height);
            var paddingRelativeWidth = 0.35f * commandButtonAbsoluteHeight / background.Rect.Width;

            // "Padding" to prevent member-specific command button from overlapping job indicator
            new GUIFrame(new RectTransform(new Vector2(paddingRelativeWidth, 1.0f), layoutGroup.RectTransform), style: null)
            {
                CanBeFocused = false
            };

            // Hide the icon to make more space for the name if the crew list's width is small enough
            bool isJobIconVisible = crewListEntrySize.X >= 220;

            if (isJobIconVisible)
            {
                var jobIconBackground = new GUIImage(
                    new RectTransform(new Vector2(0.8f * iconRelativeWidth, 0.8f), layoutGroup.RectTransform),
                    jobIndicatorBackground,
                    scaleToFit: true)
                {
                    CanBeFocused = false,
                    UserData = "job"
                };
                if (character?.Info?.Job.Prefab?.Icon != null)
                {
                    new GUIImage(
                        new RectTransform(Vector2.One, jobIconBackground.RectTransform),
                        character.Info.Job.Prefab.Icon,
                        scaleToFit: true)
                    {
                        CanBeFocused = false,
                        Color = character.Info.Job.Prefab.UIColor,
                        HoverColor = character.Info.Job.Prefab.UIColor,
                        PressedColor = character.Info.Job.Prefab.UIColor,
                        SelectedColor = character.Info.Job.Prefab.UIColor
                    };
                }
            }

            int iconsVisible = isJobIconVisible ? 5 : 4;
            var nameRelativeWidth = 1.0f
                // Start padding
                - paddingRelativeWidth
                // icons (job, active orders, current task / voip)
                - (iconsVisible * 0.8f * iconRelativeWidth)
                // Vertical line
                - (0.1f * iconRelativeWidth)
                // Spacing
                - (7 * layoutGroup.RelativeSpacing);
            nameRelativeWidth = Math.Max(nameRelativeWidth, 0.25f);

            var font = layoutGroup.Rect.Width < 150 ? GUIStyle.SmallFont : GUIStyle.Font;
            var nameBlock = new GUITextBlock(
                new RectTransform(
                    new Vector2(nameRelativeWidth, 1.0f),
                    layoutGroup.RectTransform)
                {
                    MaxSize = new Point(150, background.Rect.Height)
                }, "",
                font: font,
                textColor: character.Info?.Job?.Prefab?.UIColor)
            {
                CanBeFocused = false,
                UserData = "name"
            };
            nameBlock.Text = ToolBox.LimitString(character.Name, font, (int)nameBlock.Rect.Width);

            new GUIImage(
                new RectTransform(new Vector2(0.1f * iconRelativeWidth, 0.5f), layoutGroup.RectTransform),
                style: "VerticalLine")
            {
                CanBeFocused = false
            };

            var orderGroup = new GUILayoutGroup(new RectTransform(new Vector2(3 * 0.8f * iconRelativeWidth, 0.8f), parent: layoutGroup.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                CanBeFocused = false,
                Stretch = true
            }; 

            // Current orders
            var currentOrderList = new GUIListBox(new RectTransform(new Vector2(0.0f, 1.0f), parent: orderGroup.RectTransform), isHorizontal: true, style: null)
            {
                AllowMouseWheelScroll = false,
                CurrentDragMode = GUIListBox.DragMode.DragWithinBox,
                HideChildrenOutsideFrame = false,
                KeepSpaceForScrollBar = false,
                OnRearranged = OnOrdersRearranged,
                ScrollBarVisible = false,
                Spacing = 2,
                UserData = character
            };
            currentOrderList.RectTransform.IsFixedSize = true;
            currentOrderList.OnAddedToGUIUpdateList += (component) =>
            {
                if (component is GUIListBox list)
                {
                    list.CanBeFocused = CanIssueOrders;
                    list.CurrentDragMode = CanIssueOrders && list.Content.CountChildren > 1
                        ? GUIListBox.DragMode.DragWithinBox
                        : GUIListBox.DragMode.NoDragging;
                }
            };

            // Previous orders
            new GUILayoutGroup(new RectTransform(Vector2.One, parent: orderGroup.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                CanBeFocused = false,
                Stretch = false
            };

            var extraIconFrame = new GUIFrame(new RectTransform(new Vector2(0.8f * iconRelativeWidth, 0.8f), layoutGroup.RectTransform), style: null)
            {
                CanBeFocused = false,
                UserData = "extraicons"
            };

            var soundIconParent = new GUIFrame(new RectTransform(Vector2.One, extraIconFrame.RectTransform), style: null)
            {
                CanBeFocused = false,
                UserData = "soundicons",
                Visible = character.IsPlayer
            };
            new GUIImage(
                new RectTransform(Vector2.One, soundIconParent.RectTransform),
                GUIStyle.GetComponentStyle("GUISoundIcon").GetDefaultSprite(),
                scaleToFit: true)
            {
                CanBeFocused = false,
                UserData = new Pair<string, float>("soundicon", 0.0f),
                Visible = true
            };
            new GUIImage(
                new RectTransform(Vector2.One, soundIconParent.RectTransform),
                "GUISoundIconDisabled",
                scaleToFit: true)
            {
                CanBeFocused = true,
                UserData = "soundicondisabled",
                Visible = false
            };

            if (character.IsBot)
            {
                new GUIFrame(new RectTransform(Vector2.One, extraIconFrame.RectTransform), style: null)
                {
                    CanBeFocused = false,
                    UserData = "objectiveicon",
                    Visible = false
                };
            }

            new GUIButton(new RectTransform(new Point((int)commandButtonAbsoluteHeight), background.RectTransform), style: "CrewListCommandButton")
            {
                ToolTip = TextManager.Get("inputtype.command"),
                OnClicked = (component, userData) =>
                {
                    if (!CanIssueOrders) { return false; }
                    CreateCommandUI(character);
                    return true;
                }
            };

            return background;
        }

        public void RemoveCharacterFromCrewList(Character character)
        {
            if (crewList?.Content.GetChildByUserData(character) is { } component)
            {
                crewList.RemoveChild(component);
            }
        }

        private static void SetCharacterComponentTooltip(GUIComponent characterComponent)
        {
            if (!(characterComponent?.UserData is Character character)) { return; }
            if (character.Info?.Job?.Prefab == null) { return; }

            LocalizedString tooltip = TextManager.GetWithVariables("crewlistelementtooltip",
                ("[name]", character.Name),
                ("[job]", character.Info.Job.Name));
            string color = XMLExtensions.ColorToString(character.Info.Job.Prefab.UIColor);
            RichString richToolTip = RichString.Rich($"‖color:{color}‖"+tooltip+"‖color:end‖");
            characterComponent.ToolTip = richToolTip;
        }

        /// <summary>
        /// Sets which character is selected in the crew UI (highlight effect etc)
        /// </summary>
        public bool CharacterClicked(GUIComponent component, object selection)
        {
            if (!AllowCharacterSwitch) { return false; }
            if (!(selection is Character character) || character.IsDead || character.IsUnconscious) { return false; }
            if (!character.IsOnPlayerTeam) { return false; }

            SelectCharacter(character);
            if (GUI.KeyboardDispatcher.Subscriber == crewList) { GUI.KeyboardDispatcher.Subscriber = null; }
            return true;
        }

        public void ReviveCharacter(Character revivedCharacter)
        {
            if (crewList.Content.GetChildByUserData(revivedCharacter) is GUIComponent characterComponent)
            {
                crewList.Content.RemoveChild(characterComponent);
            }
            if (characterInfos.Contains(revivedCharacter.Info)) { AddCharacter(revivedCharacter); }
        }

        public void KillCharacter(Character killedCharacter, bool resetCrewListIndex = true)
        {
            if (crewList.Content.GetChildByUserData(killedCharacter) is GUIComponent characterComponent)
            {
                CoroutineManager.StartCoroutine(KillCharacterAnim(characterComponent));
            }
            RemoveCharacter(killedCharacter, resetCrewListIndex: resetCrewListIndex);
        }

        private IEnumerable<CoroutineStatus> KillCharacterAnim(GUIComponent component)
        {
            List<GUIComponent> components = component.GetAllChildren().ToList();
            components.Add(component);
            components.RemoveAll(c => 
                c.UserData is Pair<string, float> pair && pair.First == "soundicon" || 
                c.UserData as string == "soundicondisabled");
            components.ForEach(c => c.Color = Color.DarkRed);

            yield return new WaitForSeconds(1.0f);

            float timer = 0.0f;
            float hideDuration = 1.0f;
            while (timer < hideDuration)
            {
                foreach (GUIComponent comp in components)
                {
                    comp.Color = Color.Lerp(Color.DarkRed, Color.Transparent, timer / hideDuration);
                    comp.RectTransform.LocalScale = new Vector2(comp.RectTransform.LocalScale.X, 1.0f - (timer / hideDuration));
                }
                timer += CoroutineManager.DeltaTime;
                yield return CoroutineStatus.Running;
            }

            crewList.Content.RemoveChild(component);
            // GUITextBlock.AutoScaleAndNormalize(list.Content.GetAllChildren<GUITextBlock>(), defaultScale: 1.0f);
            crewList.UpdateScrollBarSize();

            yield return CoroutineStatus.Success;
        }

        partial void RenameCharacterProjSpecific(CharacterInfo characterInfo)
        {
            if (!(crewList.Content.GetChildByUserData(characterInfo?.Character) is GUIComponent characterComponent)) { return; }
            SetCharacterComponentTooltip(characterComponent);
            if (!(characterComponent.FindChild("name", recursive: true) is GUITextBlock nameBlock)) { return; }
            nameBlock.Text = ToolBox.LimitString(characterInfo.Name, nameBlock.Font, nameBlock.Rect.Width);
        }

        private void OnCrewListRearranged(GUIListBox crewList, object draggedElementData)
        {
            if (crewList != this.crewList) { return; }
            if (!(draggedElementData is Character)) { return; }
            if (!IsSinglePlayer) { return; }
            if (crewList.HasDraggedElementIndexChanged)
            {
                UpdateCrewListIndices();
            }
            else
            {
                CharacterClicked(crewList.DraggedElement, draggedElementData);
            }
        }

        private void ResetCrewListIndex(Character c)
        {
            if (c?.Info == null) { return; }
            c.Info.CrewListIndex = -1;
            UpdateCrewListIndices();
        }

        private void UpdateCrewListIndices()
        {
            if (crewList == null) { return; }
            for (int i = 0; i < crewList.Content.CountChildren; i++)
            {
                var characterComponent = crewList.Content.GetChild(i);
                if (!(characterComponent?.UserData is Character c)) { continue; }
                if (c.Info == null) { continue; }
                c.Info.CrewListIndex = i;
            }
        }

        private void SortCrewList()
        {
            if (crewList == null) { return; }
            crewList.Content.RectTransform.SortChildren((x, y) =>
            {
                var infoX = (x.GUIComponent.UserData as Character)?.Info?.CrewListIndex;
                var infoY = (y.GUIComponent.UserData as Character)?.Info?.CrewListIndex;
                if (infoX.HasValue)
                {
                    return infoY.HasValue ? infoX.Value.CompareTo(infoY.Value) : -1;
                }
                else
                {
                    return infoY.HasValue ? 1 : 0;
                }
            });
            UpdateCrewListIndices();
        }

        #endregion

        #region Dialog

        /// <summary>
        /// Adds the message to the single player chatbox.
        /// </summary>
        public void AddSinglePlayerChatMessage(LocalizedString senderName, LocalizedString text, ChatMessageType messageType, Character sender)
        {
            AddSinglePlayerChatMessage(senderName.Value, text.Value, messageType, sender);
        }
        public void AddSinglePlayerChatMessage(string senderName, string text, ChatMessageType messageType, Character sender)
        {
            if (!IsSinglePlayer)
            {
                DebugConsole.ThrowError("Cannot add messages to single player chat box in multiplayer mode!\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }
            if (string.IsNullOrEmpty(text)) { return; }

            if (sender != null)
            {
                GameMain.GameSession.CrewManager.SetCharacterSpeaking(sender);
            }
            ChatBox.AddMessage(ChatMessage.Create(senderName, text, messageType, sender));
        }

        public void AddSinglePlayerChatMessage(ChatMessage message)
        {
            if (!IsSinglePlayer)
            {
                DebugConsole.ThrowError("Cannot add messages to single player chat box in multiplayer mode!\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }
            if (string.IsNullOrEmpty(message.Text)) { return; }

            if (message.Sender != null)
            {
                GameMain.GameSession.CrewManager.SetCharacterSpeaking(message.Sender);
            }
            ChatBox.AddMessage(message);
        }

        partial void CreateRandomConversation()
        {
            if (GameMain.Client != null)
            {
                //let the server create random conversations in MP
                return;
            }
            List<Character> availableSpeakers = Character.CharacterList.FindAll(c =>
                c.AIController is HumanAIController &&
                !c.IsDead &&
                c.SpeechImpediment <= 100.0f &&
                c.CharacterHealth.GetAllAfflictions(a => a is AfflictionHusk huskInfection && huskInfection.Prefab is AfflictionPrefabHusk { CauseSpeechImpediment: true }).None());
            pendingConversationLines.AddRange(NPCConversation.CreateRandom(availableSpeakers));
        }

        #endregion

        #region Voice chat

        public void SetPlayerVoiceIconState(Client client, bool muted, bool mutedLocally)
        {
            if (client?.Character == null) { return; }

            if (GetSoundIconParent(client.Character) is GUIComponent soundIcons)
            {
                var soundIcon = soundIcons.FindChild(c => c.UserData is Pair<string, float> pair && pair.First == "soundicon");
                var soundIconDisabled = soundIcons.FindChild("soundicondisabled");
                soundIcon.Visible = !muted && !mutedLocally;
                soundIconDisabled.Visible = muted || mutedLocally;
                soundIconDisabled.ToolTip = TextManager.Get(mutedLocally ? "MutedLocally" : "MutedGlobally");
            }
        }

        public void SetClientSpeaking(Client client)
        {
            if (client?.Character != null)
            {
                SetCharacterSpeaking(client.Character);
            }
        }

        public void SetCharacterSpeaking(Character character)
        {
            if (character == null || character.IsBot) { return; }

            if (GetSoundIconParent(character)?.FindChild(c => c.UserData is Pair<string, float> pair && pair.First == "soundicon") is GUIComponent soundIcon)
            {
                soundIcon.Color = Color.White;
                Pair<string, float> userdata = soundIcon.UserData as Pair<string, float>;
                userdata.Second = 1.0f;
            }
        }

        private GUIComponent GetSoundIconParent(GUIComponent characterComponent)
        {
            return characterComponent?
                .FindChild(c => c is GUILayoutGroup)?
                .GetChildByUserData("extraicons")?
                .GetChildByUserData("soundicons");
        }

        private GUIComponent GetSoundIconParent(Character character)
        {
            return GetSoundIconParent(crewList?.Content.GetChildByUserData(character));
        }

        #endregion

        #region Crew List Order Displayment

        /// <summary>
        /// Sets the character's current order (if it's close enough to receive messages from orderGiver) and
        /// displays the order in the crew UI
        /// </summary>
        public void SetCharacterOrder(Character character, Order order, bool isNewOrder = true)
        {
            if (order != null && order.TargetAllCharacters)
            {
                Hull hull = order.TargetHull;
                if (order.IsReport)
                {
                    if (order.OrderGiver?.CurrentHull == null && hull == null) { return; }
                    hull ??= order.OrderGiver.CurrentHull;
                    AddOrder(order.WithTargetEntity(hull), order.FadeOutTime);
                }
                else if (order.IsIgnoreOrder)
                {
                    WallSection ws = null;
                    if (order.TargetType == Order.OrderTargetType.Entity && order.TargetEntity is IIgnorable ignorable)
                    {
                        ignorable.OrderedToBeIgnored = order.Identifier == "ignorethis";
                        AddOrder(order.Clone(), null);
                    }
                    else if (order.TargetType == Order.OrderTargetType.WallSection && order.TargetEntity is Structure s)
                    {
                        var wallSectionIndex = order.WallSectionIndex ?? s.Sections.IndexOf(wallContext);
                        ws = s.GetSection(wallSectionIndex);
                        if (ws != null)
                        {
                            ws.OrderedToBeIgnored = order.Identifier == "ignorethis";
                            AddOrder(order.WithWallSection(s, wallSectionIndex), null);
                        }
                    }
                    else
                    {
                        return;
                    }
                    if (ws != null)
                    {
                        hull = Hull.FindHull(ws.WorldPosition);
                    }
                    else if (order.TargetEntity is Item i)
                    {
                        hull = i.CurrentHull;
                    }
                    else if (order.TargetEntity is ISpatialEntity se)
                    {
                        hull = Hull.FindHull(se.WorldPosition);
                    }
                }
                if (IsSinglePlayer)
                {
                    order.OrderGiver?.Speak(order.GetChatMessage("", hull?.DisplayName?.Value, givingOrderToSelf: character == order.OrderGiver, isNewOrder: isNewOrder), ChatMessageType.Order);
                }
                else
                {
                    OrderChatMessage msg = new OrderChatMessage(order.WithTargetEntity(order.IsReport ? hull : order.TargetEntity), null, order.OrderGiver, isNewOrder: isNewOrder);
                    GameMain.Client?.SendChatMessage(msg);
                }
            }
            else
            {
                //can't issue an order if no characters are available
                if (character == null) { return; }
                var orderGiver = order?.OrderGiver;
                if (IsSinglePlayer)
                {
                    bool isGivingOrderToSelf = orderGiver == character;
                    character.SetOrder(order, isNewOrder, speak: !isGivingOrderToSelf);
                        string message = order?.GetChatMessage(character.Name, orderGiver?.CurrentHull?.DisplayName?.Value, isGivingOrderToSelf, orderOption: order?.Option ?? Identifier.Empty, isNewOrder: isNewOrder);
                        orderGiver?.Speak(message);
                }
                else if (orderGiver != null)
                {
                    OrderChatMessage msg = new OrderChatMessage(order, character, orderGiver, isNewOrder: isNewOrder);
                    GameMain.Client?.SendChatMessage(msg);
                }
            }
        }

        /// <summary>
        /// Displays the specified order in the crew UI next to the character.
        /// </summary>
        public void AddCurrentOrderIcon(Character character, Order order)
        {
            if (character == null) { return; }

            var characterComponent = crewList.Content.GetChildByUserData(character);

            if (characterComponent == null) { return; }

            var currentOrderIconList = GetCurrentOrderIconList(characterComponent);
            var currentOrderIcons = currentOrderIconList.Content.Children;
            var iconsToRemove = new List<GUIComponent>();
            var newPreviousOrders = new List<Order>();
            bool updatedExistingIcon = false;

            foreach (var icon in currentOrderIcons)
            {
                var orderInfo = (Order)icon.UserData;
                var matchingOrder = character.GetCurrentOrder(orderInfo);
                if (matchingOrder is null)
                {
                    iconsToRemove.Add(icon);
                    newPreviousOrders.Add(orderInfo);
                }
                else if (orderInfo.MatchesOrder(order))
                {
                    icon.UserData = order.Clone();
                    if (icon is GUIImage image)
                    {
                        image.Sprite = GetOrderIconSprite(order);
                        image.ToolTip = CreateOrderTooltip(order);
                    }
                    updatedExistingIcon = true;
                }
            }
            iconsToRemove.ForEach(c => currentOrderIconList.RemoveChild(c));

            // Remove a previous order icon if it matches the new order
            // We don't want the same order as both a current order and a previous order
            var previousOrderIconGroup = GetPreviousOrderIconGroup(characterComponent);
            var previousOrderIcons = previousOrderIconGroup.Children;
            foreach (var icon in previousOrderIcons)
            {
                var orderInfo = (Order)icon.UserData;
                if (orderInfo.MatchesOrder(order))
                {
                    previousOrderIconGroup.RemoveChild(icon);
                    break;
                }
            }

            // Rearrange the icons before adding anything
            if (updatedExistingIcon)
            {
                RearrangeIcons();
            }

            for (int i = newPreviousOrders.Count - 1; i >= 0; i--)
            {
                AddPreviousOrderIcon(character, characterComponent, newPreviousOrders[i]);
            }

            if (order == null || order.Identifier == dismissedOrderPrefab.Identifier || updatedExistingIcon)
            {
                RearrangeIcons();
                return;
            }

            int orderIconCount = currentOrderIconList.Content.CountChildren + previousOrderIconGroup.CountChildren;
            if (orderIconCount >= CharacterInfo.MaxCurrentOrders)
            {
                RemoveLastOrderIcon(characterComponent);
            }

            float nodeWidth = ((1.0f / CharacterInfo.MaxCurrentOrders) * currentOrderIconList.Parent.Rect.Width) - ((CharacterInfo.MaxCurrentOrders - 1) * currentOrderIconList.Spacing);
            Point size = new Point((int)nodeWidth, currentOrderIconList.RectTransform.NonScaledSize.Y);
            var nodeIcon = CreateNodeIcon(size, currentOrderIconList.Content.RectTransform, GetOrderIconSprite(order), order.Color, tooltip: CreateOrderTooltip(order));
            nodeIcon.UserData = order.Clone();
            nodeIcon.OnSecondaryClicked = (image, userData) =>
            {
                if (!CanIssueOrders) { return false; }
                var orderInfo = (Order)userData;
                var order = orderInfo.GetDismissal().WithManualPriority(character.GetCurrentOrder(orderInfo)?.ManualPriority ?? 0).WithOrderGiver(Character.Controlled);
                SetCharacterOrder(character, order);
                return true;
            };

            new GUIFrame(new RectTransform(new Point((int)(1.5f * nodeWidth)), parent: nodeIcon.RectTransform, Anchor.Center), "OuterGlowCircular")
            {
                CanBeFocused = false,
                Color = order.Color,
                UserData = "glow",
                Visible = false
            };

            int hierarchyIndex = Math.Clamp(CharacterInfo.HighestManualOrderPriority - order.ManualPriority, 0, Math.Max(currentOrderIconList.Content.CountChildren - 1, 0));
            if (hierarchyIndex != currentOrderIconList.Content.GetChildIndex(nodeIcon))
            {
                nodeIcon.RectTransform.RepositionChildInHierarchy(hierarchyIndex);
            }

            RearrangeIcons();

            void RearrangeIcons()
            {
                if (character.CurrentOrders != null)
                {
                    // Make sure priority values are up-to-date
                    foreach (var currentOrderInfo in character.CurrentOrders)
                    {
                        var component = currentOrderIconList.Content.FindChild(c => c?.UserData is Order componentOrderInfo &&
                            componentOrderInfo.MatchesOrder(currentOrderInfo));
                        if (component == null) { continue; }
                        var componentOrderInfo = (Order)component.UserData;
                        int newPriority = currentOrderInfo.ManualPriority;
                        if (componentOrderInfo.ManualPriority != newPriority)
                        {
                            component.UserData = componentOrderInfo.WithManualPriority(newPriority);
                        }
                    }

                    currentOrderIconList.Content.RectTransform.SortChildren((x, y) =>
                    {
                        var xOrder = (Order)x.GUIComponent.UserData;
                        var yOrder = (Order)y.GUIComponent.UserData;
                        return yOrder.ManualPriority.CompareTo(xOrder.ManualPriority);
                    });

                    if (currentOrderIconList.Parent is GUILayoutGroup parentGroup)
                    {
                        int iconCount = currentOrderIconList.Content.CountChildren;
                        float nonScaledWidth = ((float)iconCount / CharacterInfo.MaxCurrentOrders) * parentGroup.Rect.Width + (iconCount * currentOrderIconList.Spacing);
                        currentOrderIconList.RectTransform.NonScaledSize = new Point((int)nonScaledWidth, currentOrderIconList.RectTransform.NonScaledSize.Y);
                        parentGroup.Recalculate();
                        previousOrderIconGroup.Recalculate();
                    }
                }
            }
        }

        private void AddPreviousOrderIcon(Character character, GUIComponent characterComponent, Order orderInfo)
        {
            if (orderInfo == null || orderInfo.Identifier == dismissedOrderPrefab.Identifier) { return; }

            var currentOrderIconList = GetCurrentOrderIconList(characterComponent);
            int maxPreviousOrderIcons = CharacterInfo.MaxCurrentOrders - currentOrderIconList.Content.CountChildren;

            if (maxPreviousOrderIcons < 1) { return; }

            var previousOrderIconGroup = GetPreviousOrderIconGroup(characterComponent);
            if (previousOrderIconGroup.CountChildren >= maxPreviousOrderIcons)
            {
                RemoveLastPreviousOrderIcon(previousOrderIconGroup);
            }

            float nodeWidth = ((1.0f / CharacterInfo.MaxCurrentOrders) * previousOrderIconGroup.Parent.Rect.Width) - ((CharacterInfo.MaxCurrentOrders - 1) * currentOrderIconList.Spacing);
            Point size = new Point((int)nodeWidth, previousOrderIconGroup.Rect.Height);
            var previousOrderInfo = orderInfo.WithType(Order.OrderType.Previous);
            var prevOrderFrame = new GUIButton(new RectTransform(size, parent: previousOrderIconGroup.RectTransform), style: null)
            {
                UserData = previousOrderInfo,
                OnClicked = (button, userData) =>
                {
                    if (!CanIssueOrders) { return false; }
                    var orderInfo = (Order)userData;
                    int priority = GetManualOrderPriority(character, orderInfo);
                    SetCharacterOrder(character, orderInfo.WithManualPriority(priority).WithOrderGiver(Character.Controlled));
                    return true;
                },
                OnSecondaryClicked = (button, userData) =>
                {
                    if (previousOrderIconGroup == null) { return false; }
                    previousOrderIconGroup.RemoveChild(button);
                    previousOrderIconGroup.Recalculate();
                    return true;
                }
            };
            prevOrderFrame.RectTransform.IsFixedSize = true;

            var prevOrderIconFrame = new GUIFrame(
                new RectTransform(new Vector2(0.8f), prevOrderFrame.RectTransform, anchor: Anchor.BottomLeft),
                style: null);

            CreateNodeIcon(Vector2.One,
                prevOrderIconFrame.RectTransform,
                GetOrderIconSprite(previousOrderInfo),
                previousOrderInfo.Color,
                tooltip: CreateOrderTooltip(previousOrderInfo));

            foreach (GUIComponent c in prevOrderIconFrame.Children)
            {
                c.HoverColor = c.Color;
                c.PressedColor = c.Color;
                c.SelectedColor = c.Color;
            }

            new GUIImage(
                new RectTransform(new Vector2(0.8f), prevOrderFrame.RectTransform, anchor: Anchor.TopRight),
                previousOrderArrow,
                scaleToFit: true)
            {
                CanBeFocused = false
            };

            prevOrderFrame.SetAsFirstChild();
        }

        private void AddOldPreviousOrderIcons(Character character, GUIComponent oldCharacterComponent)
        {
            var oldPrevOrderIcons = GetPreviousOrderIconGroup(oldCharacterComponent).Children;
            if (oldPrevOrderIcons.None()) { return; }
            if (oldPrevOrderIcons.Count() > 1)
            {
                oldPrevOrderIcons = oldPrevOrderIcons.Reverse();
            }
            if (crewList.Content.Children.FirstOrDefault(c => c.UserData == character) is GUIComponent newCharacterComponent)
            {
                foreach (GUIComponent icon in oldPrevOrderIcons)
                {
                    if (icon.UserData is Order orderInfo)
                    {
                        AddPreviousOrderIcon(character, newCharacterComponent, orderInfo);
                    }
                }
            }
        }

        private void RemoveLastOrderIcon(GUIComponent characterComponent)
        {
            var previousOrderIconGroup = GetPreviousOrderIconGroup(characterComponent);
            if (RemoveLastPreviousOrderIcon(previousOrderIconGroup))
            {
                return;
            }
            var currentOrderIconList = GetCurrentOrderIconList(characterComponent);
            if (currentOrderIconList.Content.CountChildren > 0)
            {
                var iconToRemove = currentOrderIconList.Content.Children.Last();
                currentOrderIconList.RemoveChild(iconToRemove);
                return;
            }
        }

        private bool RemoveLastPreviousOrderIcon(GUILayoutGroup iconGroup)
        {
            if (iconGroup.CountChildren > 0)
            {
                var iconToRemove = iconGroup.Children.Last();
                iconGroup.RemoveChild(iconToRemove);
                return true;
            }
            return false;
        }

        private GUIListBox GetCurrentOrderIconList(GUIComponent characterComponent) =>
            characterComponent?.GetChild<GUILayoutGroup>().GetChild<GUILayoutGroup>().GetChild<GUIListBox>();

        private GUILayoutGroup GetPreviousOrderIconGroup(GUIComponent characterComponent) =>
            characterComponent?.GetChild<GUILayoutGroup>().GetChild<GUILayoutGroup>().GetChild<GUILayoutGroup>();

        private void OnOrdersRearranged(GUIListBox orderList, object userData)
        {
            var orderComponent = orderList.Content.GetChildByUserData(userData);
            if (orderComponent == null) { return; }
            var orderInfo = (Order)userData;
            var priority = Math.Max(CharacterInfo.HighestManualOrderPriority - orderList.Content.GetChildIndex(orderComponent), 1);
            if (orderInfo.ManualPriority == priority) { return; }
            var character = (Character)orderList.UserData;
            SetCharacterOrder(character, orderInfo.WithManualPriority(priority), isNewOrder: false);
        }

        private LocalizedString CreateOrderTooltip(OrderPrefab orderPrefab, Identifier option, Entity targetEntity)
        {
            if (orderPrefab == null) { return ""; }
            if (option != Identifier.Empty)
            {
                return TextManager.GetWithVariables("crewlistordericontooltip".ToIdentifier(),
                    ("[ordername]".ToIdentifier(), orderPrefab.Name),
                    ("[orderoption]".ToIdentifier(), orderPrefab.GetOptionName(option)));
            }
            else if (targetEntity is Item targetItem && targetItem.Prefab.MinimapIcon != null)
            {
                return TextManager.GetWithVariables("crewlistordericontooltip".ToIdentifier(),
                    ("[ordername]".ToIdentifier(), orderPrefab.Name),
                    ("[orderoption]".ToIdentifier(), targetItem.Name));
            }
            else
            {
                return orderPrefab.Name;
            }
        }

        private LocalizedString CreateOrderTooltip(Order order)
        {
            if (order.DisplayGiverInTooltip && order.OrderGiver != null)
            {
                return TextManager.GetWithVariables("crewlistordericontooltip",
                    ("[ordername]", order.Name),
                    ("[orderoption]", order.OrderGiver.DisplayName));
            }
            return CreateOrderTooltip(order.Prefab, order.Option, order?.TargetEntity);
        }

        private Sprite GetOrderIconSprite(Order order)
        {
            if (order == null) { return null; }
            Sprite sprite = null;
            if (order.Option != Identifier.Empty && order.Prefab.OptionSprites.Any())
            {
                order.Prefab.OptionSprites.TryGetValue(order.Option, out sprite);
            }
            if (sprite == null && order.TargetEntity is Item targetItem && targetItem.Prefab.MinimapIcon != null)
            {
                sprite = targetItem.Prefab.MinimapIcon;
            }
            return sprite ?? order.SymbolSprite;
        }

        #endregion

        #region Updating and drawing the UI

        private void DrawMiniMapOverlay(SpriteBatch spriteBatch, GUICustomComponent container)
        {
            Submarine sub = container.UserData as Submarine;

            if (sub?.HullVertices == null) { return; }

            var dockedBorders = sub.GetDockedBorders();
            dockedBorders.Location += sub.WorldPosition.ToPoint();

            float scale = Math.Min(
                container.Rect.Width / (float)dockedBorders.Width,
                container.Rect.Height / (float)dockedBorders.Height) * 0.9f;

            float displayScale = ConvertUnits.ToDisplayUnits(scale);
            Vector2 offset = (sub.WorldPosition - new Vector2(dockedBorders.Center.X, dockedBorders.Y - dockedBorders.Height / 2)) * scale;
            Vector2 center = container.Rect.Center.ToVector2();

            for (int i = 0; i < sub.HullVertices.Count; i++)
            {
                Vector2 start = (sub.HullVertices[i] * displayScale + offset);
                start.Y = -start.Y;
                Vector2 end = (sub.HullVertices[(i + 1) % sub.HullVertices.Count] * displayScale + offset);
                end.Y = -end.Y;
                GUI.DrawLine(spriteBatch, center + start, center + end, Color.DarkCyan * Rand.Range(0.3f, 0.35f), width: 10);
            }
        }

        public void AddToGUIUpdateList()
        {
            if (GUI.DisableHUD) { return; }
            if (CoroutineManager.IsCoroutineRunning("LevelTransition") || CoroutineManager.IsCoroutineRunning("SubmarineTransition")) { return; }

            commandFrame?.AddToGUIUpdateList();

            if (GUI.DisableUpperHUD) { return; }

            if (GameMain.GraphicsWidth != screenResolution.X || GameMain.GraphicsHeight != screenResolution.Y || prevUIScale != GUI.Scale)
            {
                var oldCrewList = crewList;
                InitProjectSpecific();

                foreach (GUIComponent oldCharacterComponent in oldCrewList.Content.Children)
                {
                    if (!(oldCharacterComponent.UserData is Character character) || character.IsDead || character.Removed) { continue; }
                    AddCharacter(character);
                    AddOldPreviousOrderIcons(character, oldCharacterComponent);
                }
            }

            crewArea.Visible = !(GameMain.GameSession?.GameMode is CampaignMode campaign) || (!campaign.ForceMapUI && !campaign.ShowCampaignUI);

            guiFrame.AddToGUIUpdateList();
        }

        public void SelectNextCharacter()
        {
            if (!AllowCharacterSwitch || GameMain.IsMultiplayer || characters.None()) { return; }
            if (crewList.Content.GetChild(TryAdjustIndex(1))?.UserData is Character character)
            {
                SelectCharacter(character);
            }
        }

        public void SelectPreviousCharacter()
        {
            if (!AllowCharacterSwitch || GameMain.IsMultiplayer || characters.None()) { return; }
            if (crewList.Content.GetChild(TryAdjustIndex(-1))?.UserData is Character character)
            {
                SelectCharacter(character);
            }
        }

        private void SelectCharacter(Character character)
        {
            if (ConversationAction.IsDialogOpen) { return; }
            if (!AllowCharacterSwitch) { return; }
            //make the previously selected character wait in place for some time
            //(so they don't immediately start idling and walking away from their station)
            var aiController = Character.Controlled?.AIController;
            aiController?.Reset();
            DisableCommandUI();
            Character.Controlled = character;
            HintManager.OnChangeCharacter();
            if (GameSession.TabMenuInstance != null && TabMenu.SelectedTab == TabMenu.InfoFrameTab.Talents)
            {
                GameSession.TabMenuInstance.SelectInfoFrameTab(TabMenu.SelectedTab);
            }
        }

        private int TryAdjustIndex(int amount)
        {
            if (Character.Controlled == null) { return 0; }

            int currentIndex = crewList.Content.GetChildIndex(crewList.Content.GetChildByUserData(Character.Controlled));
            if (currentIndex == -1) { return 0; }

            int lastIndex = crewList.Content.CountChildren - 1;

            int index = currentIndex + amount;
            for (int i = 0; i < crewList.Content.CountChildren; i++)
            {
                if (index > lastIndex) { index = 0; }
                if (index < 0) { index = lastIndex; }

                if ((crewList.Content.GetChild(index)?.UserData as Character)?.IsOnPlayerTeam ?? false)
                {
                    return index;
                }

                index += amount;
            }

            return 0;
        }

        private bool CreateOrder(OrderPrefab orderPrefab, Hull targetHull = null)
        {
            var sub = Character.Controlled?.Submarine;

            if (sub == null || sub.TeamID != Character.Controlled.TeamID || sub.Info.IsWreck) { return false; }

            var order = new Order(orderPrefab, targetHull, null, Character.Controlled)
                .WithManualPriority(CharacterInfo.HighestManualOrderPriority);
            SetCharacterOrder(null, order);

            if (IsSinglePlayer)
            {
                HumanAIController.ReportProblem(Character.Controlled, order);
            }

            return true;
        }

        private void UpdateOrderDrag()
        {
            if (DraggedOrderPrefab is { } orderPrefab)
            {
                if (dropOrder)
                {
                    // stinky workaround
                    if (framesToSkip > 0)
                    {
                        framesToSkip--;
                    }
                    else
                    {
                        Hull hull = null;

                        if (GUI.MouseOn is GUIFrame frame)
                        {
                            if (frame.UserData is Hull data)
                            {
                                hull = data;
                            } 
                            else if (frame.Parent?.UserData is Hull parentData)
                            {
                                hull = parentData;
                            }
                        }

                        framesToSkip = 2;
                        dropOrder = false;
                        DraggedOrderPrefab = null;

                        if (hull is null && GUI.MouseOn is { Visible: true, CanBeFocused: true }) { return; }

                        hull ??= Hull.HullList.FirstOrDefault(h => h.WorldRect.ContainsWorld(Screen.Selected.Cam.ScreenToWorld(PlayerInput.MousePosition)));
                        CreateOrder(orderPrefab, hull);
                    }
                }
                else
                {
                    DragOrder = DragOrder || Vector2.DistanceSquared(dragPoint, PlayerInput.MousePosition) > dragOrderTreshold * dragOrderTreshold;

                    if (!PlayerInput.PrimaryMouseButtonHeld())
                    {
                        if (DragOrder)
                        {
                            dropOrder = true;
                        }
                        else
                        {
                            DraggedOrderPrefab = null;
                        }
                        dragPoint = Vector2.Zero;
                        DragOrder = false;
                    }
                }
            }
        }

        partial void UpdateProjectSpecific(float deltaTime)
        {
            // Quick selection
            if (GameMain.IsSingleplayer && GUI.KeyboardDispatcher.Subscriber == null)
            {
                if (PlayerInput.KeyHit(InputType.SelectNextCharacter))
                {
                    SelectNextCharacter();
                }
                if (PlayerInput.KeyHit(InputType.SelectPreviousCharacter))
                {
                    SelectPreviousCharacter();
                }
            }

            if (GUI.DisableHUD) { return; }

            UpdateOrderDrag();

            #region Command UI

            WasCommandInterfaceDisabledThisUpdate = false;

            if (PlayerInput.KeyDown(InputType.Command) &&
                (GUI.KeyboardDispatcher.Subscriber == null || (GUI.KeyboardDispatcher.Subscriber is GUIComponent component && (component == crewList || component.IsChildOf(crewList)))) &&
                commandFrame == null && !clicklessSelectionActive && CanIssueOrders && !(GameMain.GameSession?.Campaign?.ShowCampaignUI ?? false))
            {
                if (PlayerInput.IsShiftDown())
                {
                    CreateCommandUI(FindEntityContext(), true);
                }
                else
                {
                    CreateCommandUI(CharacterHUD.MouseOnCharacterPortrait() ? Character.Controlled : GUI.MouseOn?.UserData as Character);
                }
                SoundPlayer.PlayUISound(GUISoundType.PopupMenu);
                clicklessSelectionActive = isOpeningClick = true;
            }

            if (commandFrame != null)
            {
                void ResetNodeSelection(GUIButton newSelectedNode = null)
                {
                    if (commandFrame == null) { return; }
                    selectedNode?.Children.ForEach(c => c.Color = c.HoverColor * nodeColorMultiplier);
                    selectedNode = newSelectedNode;
                    timeSelected = 0;
                    isSelectionHighlighted = false;
                }

                // When using Deselect to close the interface, make sure it's not a seconday mouse button click on a node
                // That should be reserved for opening manual assignment
                bool isMouseOnOptionNode = optionNodes.Any(n => GUI.IsMouseOn(n.Button));
                bool isMouseOnShortcutNode = !isMouseOnOptionNode && shortcutNodes.Any(n => GUI.IsMouseOn(n));
                bool hitDeselect = PlayerInput.KeyHit(InputType.Deselect) &&
                    (!PlayerInput.SecondaryMouseButtonClicked() || (!isMouseOnOptionNode && !isMouseOnShortcutNode));

                bool isBoundToPrimaryMouse = GameSettings.CurrentConfig.KeyMap.Bindings[InputType.Command].MouseButton == MouseButton.PrimaryMouse;
                bool canToggleInterface = !isBoundToPrimaryMouse ||
                    (!isMouseOnOptionNode && !isMouseOnShortcutNode && extraOptionNodes.None(n => GUI.IsMouseOn(n)) && !GUI.IsMouseOn(returnNode));

                // TODO: Consider using HUD.CloseHUD() instead of KeyHit(Escape), the former method is also used for health UI
                if (hitDeselect || PlayerInput.KeyHit(Keys.Escape) || !CanIssueOrders ||
                    (canToggleInterface && PlayerInput.KeyHit(InputType.Command) && selectedNode == null && !clicklessSelectionActive))
                {
                    DisableCommandUI();
                }
                else if (PlayerInput.KeyUp(InputType.Command))
                {
                    // Clickless selection behavior
                    if (canToggleInterface && !isOpeningClick && clicklessSelectionActive && timeSelected < 0.15f)
                    {
                        DisableCommandUI();
                    }
                    else
                    {
                        clicklessSelectionActive = isOpeningClick = false;
                        if (selectedNode != null)
                        {
                            ResetNodeSelection();
                        }
                    }
                }
                else if (PlayerInput.KeyDown(InputType.Command) && (targetFrame == null || !targetFrame.Visible))
                {
                    // Clickless selection behavior
                    if (!GUI.IsMouseOn(centerNode))
                    {
                        clicklessSelectionActive = true;

                        var mouseBearing = GetBearing(centerNode.Center, PlayerInput.MousePosition, flipY: true);

                        GUIComponent closestNode = null;
                        float closestBearing = 0;

                        optionNodes.ForEach(n => CheckIfClosest(n.Button));
                        CheckIfClosest(returnNode);

                        void CheckIfClosest(GUIComponent comp)
                        {
                            if (comp == null) { return; }
                            var offset = comp.RectTransform.AbsoluteOffset;
                            var nodeBearing = GetBearing(centerNode.RectTransform.AbsoluteOffset.ToVector2(), offset.ToVector2(), flipY: true);
                            if (closestNode == null)
                            {
                                closestNode = comp;
                                closestBearing = Math.Abs(nodeBearing - mouseBearing);
                            }
                            else
                            {
                                var difference = Math.Abs(nodeBearing - mouseBearing);
                                if (difference < closestBearing)
                                {
                                    closestNode = comp;
                                    closestBearing = difference;
                                }
                            }
                        }

                        if (closestNode != null && closestNode.CanBeFocused && closestNode == selectedNode)
                        {
                            timeSelected += deltaTime;
                            if (timeSelected >= selectionTime)
                            {
                                if (PlayerInput.IsShiftDown() && selectedNode.OnSecondaryClicked != null)
                                {
                                    selectedNode.OnSecondaryClicked.Invoke(selectedNode, selectedNode.UserData);
                                }
                                else
                                {
                                    selectedNode.OnClicked?.Invoke(selectedNode, selectedNode.UserData);
                                }
                                ResetNodeSelection();
                            }
                            else if (timeSelected >= 0.15f && !isSelectionHighlighted)
                            {
                                selectedNode.Children.ForEach(c => c.Color = c.HoverColor);
                                isSelectionHighlighted = true;
                            }
                        }
                        else
                        {
                            ResetNodeSelection(closestNode as GUIButton);
                        }
                    }
                    else if (selectedNode != null)
                    {
                        ResetNodeSelection();
                    }
                }

                var hotkeyHit = false;
                foreach (OptionNode node in optionNodes)
                {
                    if (node.Keys != Keys.None && PlayerInput.KeyHit(node.Keys))
                    {
                        var b = node.Button as GUIButton;
                        if (PlayerInput.IsShiftDown() && b?.OnSecondaryClicked != null)
                        {
                            b.OnSecondaryClicked.Invoke(node.Button as GUIButton, node.Button.UserData);
                        }
                        else
                        {
                            b?.OnClicked?.Invoke(node.Button as GUIButton, node.Button.UserData);
                        }
                        ResetNodeSelection();
                        hotkeyHit = true;
                        break;
                    }
                }

                if (!hotkeyHit)
                {
                    if (returnNodeHotkey != Keys.None && PlayerInput.KeyHit(returnNodeHotkey))
                    {
                        returnNode?.OnClicked?.Invoke(returnNode, returnNode.UserData);
                        ResetNodeSelection();
                    }
                    else if (expandNodeHotkey != Keys.None && PlayerInput.KeyHit(expandNodeHotkey))
                    {
                        expandNode?.OnClicked?.Invoke(expandNode, expandNode.UserData);
                        ResetNodeSelection();
                    }
                }
            }
            else if (!PlayerInput.KeyDown(InputType.Command))
            {
                clicklessSelectionActive = false;
            }

            #endregion

            if (ChatBox != null)
            {
                ChatBox.Update(deltaTime);
                ChatBox.InputBox.Visible = Character.Controlled != null;
                if (!DebugConsole.IsOpen && ChatBox.InputBox.Visible && GUI.KeyboardDispatcher.Subscriber == null && !ChatBox.InputBox.Selected)
                {
                    ChatBox.ApplySelectionInputs();
                }
            }

            if (!GUI.DisableUpperHUD)
            {
                crewArea.Visible = characters.Count > 0 && CharacterHealth.OpenHealthWindow == null;

                foreach (GUIComponent characterComponent in crewList.Content.Children)
                {
                    if (characterComponent.UserData is Character character)
                    {
                        characterComponent.Visible = Character.Controlled == null || Character.Controlled.TeamID == character.TeamID;
                        if (character.TeamID == CharacterTeamType.FriendlyNPC && Character.Controlled != null && 
                            (character.CurrentHull == Character.Controlled.CurrentHull || Vector2.DistanceSquared(Character.Controlled.WorldPosition, character.WorldPosition) < 500.0f * 500.0f))
                        {
                            characterComponent.Visible = true;
                        }
                        if (characterComponent.Visible)
                        {
                            if (character == Character.Controlled && crewList.SelectedComponent != characterComponent)
                            {
                                crewList.Select(character, GUIListBox.Force.Yes);
                            }
                            // Icon colors might change based on the target so we check if they need to be updated
                            if (GetCurrentOrderIconList(characterComponent) is GUIListBox currentOrderIconList)
                            {
                                foreach (var orderIcon in currentOrderIconList.Content.Children)
                                {
                                    if (!(orderIcon.UserData is Order order)) { continue; }
                                    if (order.ColoredWhenControllingGiver && order.OrderGiver != Character.Controlled)
                                    {
                                        orderIcon.Color = AIObjective.ObjectiveIconColor;
                                    }
                                    else
                                    {
                                        orderIcon.Color = order.Color;
                                    }
                                }
                            }
                            // Only update the order highlights and objective icons here in singleplayer
                            // The server will let the clients know when they need to update in multiplayer
                            if (GameMain.IsSingleplayer && character.IsBot && character.AIController is HumanAIController controller &&
                                controller.ObjectiveManager is AIObjectiveManager objectiveManager)
                            {
                                if (objectiveManager.CurrentObjective is AIObjective currentObjective)
                                {
                                    if (objectiveManager.IsOrder(currentObjective))
                                    {
                                        var orderInfo = objectiveManager.CurrentOrders.FirstOrDefault(o => o.Objective == currentObjective);
                                        if (orderInfo != null)
                                        {
                                            SetOrderHighlight(characterComponent, orderInfo.Identifier, orderInfo.Option);
                                        }
                                    }
                                    else
                                    {
                                        CreateObjectiveIcon(characterComponent, currentObjective);
                                    }
                                }
                            }
                            // Order highlighting and objective icons are intended to communicate bot behavior so they should be disabled for player characters
                            if (character.IsPlayer)
                            {
                                DisableOrderHighlight(characterComponent);
                                RemoveObjectiveIcon(characterComponent);
                            }
                            if (GetSoundIconParent(characterComponent) is GUIComponent soundIconParent)
                            {
                                if (soundIconParent.FindChild(c => c.UserData is Pair<string, float> pair && pair.First == "soundicon") is GUIImage soundIcon)
                                {
                                    if (character.IsPlayer)
                                    {
                                        soundIconParent.Visible = true;
                                        VoipClient.UpdateVoiceIndicator(soundIcon, 0.0f, deltaTime);
                                    }
                                    else if(soundIcon.Visible)
                                    {
                                        var userdata = soundIcon.UserData as Pair<string, float>;
                                        userdata.Second = 0.0f;
                                        soundIconParent.Visible = soundIcon.Visible = false;
                                    }
                                }
                            }
                        }
                    }
                }

                crewArea.RectTransform.AbsoluteOffset = Vector2.SmoothStep(
                    new Vector2(-crewArea.Rect.Width - HUDLayoutSettings.Padding, 0.0f),
                    Vector2.Zero,
                    crewListOpenState).ToPoint();

                crewListOpenState = IsCrewMenuOpen ?
                    Math.Min(crewListOpenState + deltaTime * 2.0f, 1.0f) :
                    Math.Max(crewListOpenState - deltaTime * 2.0f, 0.0f);

                if (GUI.KeyboardDispatcher.Subscriber == null && PlayerInput.KeyHit(InputType.CrewOrders))
                {
                    SoundPlayer.PlayUISound(GUISoundType.PopupMenu);
                    IsCrewMenuOpen = !IsCrewMenuOpen;
                }
            }

            UpdateReports();
        }

        private void SetOrderHighlight(GUIComponent characterComponent, Identifier orderIdentifier, Identifier orderOption)
        {
            if (characterComponent == null) { return; }
            RemoveObjectiveIcon(characterComponent);
            if (GetCurrentOrderIconList(characterComponent) is GUIListBox currentOrderIconList)
            {
                bool foundMatch = false;
                foreach (var orderIcon in currentOrderIconList.Content.Children)
                {
                    if (!(orderIcon.GetChildByUserData("glow") is GUIComponent glowComponent)) { continue; }
                    glowComponent.Color = orderIcon.Color;
                    if (foundMatch)
                    {
                        glowComponent.Visible = false;
                        continue;
                    }
                    var orderInfo = (Order)orderIcon.UserData;
                    foundMatch = orderInfo.MatchesOrder(orderIdentifier, orderOption);
                    glowComponent.Visible = foundMatch;
                }
            }
        }

        public void SetOrderHighlight(Character character, Identifier orderIdentifier, Identifier orderOption)
        {
            if (crewList == null) { return; }
            var characterComponent = crewList.Content.GetChildByUserData(character);
            SetOrderHighlight(characterComponent, orderIdentifier, orderOption);
        }

        private void DisableOrderHighlight(GUIComponent characterComponent)
        {
            if (GetCurrentOrderIconList(characterComponent) is GUIListBox currentOrderIconList)
            {
                foreach (var orderIcon in currentOrderIconList.Content.Children)
                {
                    var glowComponent = orderIcon.GetChildByUserData("glow");
                    if (glowComponent == null) { continue; }
                    glowComponent.Visible = false;
                }
            }
        }

        private void CreateObjectiveIcon(GUIComponent characterComponent, Sprite sprite, LocalizedString tooltip)
        {
            if (characterComponent == null || !(characterComponent.UserData is Character character) || character.IsPlayer) { return; }
            DisableOrderHighlight(characterComponent);
            if (GetObjectiveIconParent(characterComponent) is GUIFrame objectiveIconFrame)
            {
                var existingObjectiveIcon = objectiveIconFrame.GetChild<GUIImage>();
                if (existingObjectiveIcon == null || existingObjectiveIcon.Sprite != sprite || existingObjectiveIcon.ToolTip != tooltip)
                {
                    objectiveIconFrame.ClearChildren();
                    if (sprite != null)
                    {
                        var objectiveIcon = CreateNodeIcon(Vector2.One, objectiveIconFrame.RectTransform, sprite, AIObjective.ObjectiveIconColor, tooltip: tooltip);
                        new GUIFrame(new RectTransform(new Vector2(1.5f), objectiveIcon.RectTransform, anchor: Anchor.Center), style: "OuterGlowCircular")
                        {
                            CanBeFocused = false,
                            Color = AIObjective.ObjectiveIconColor
                        };
                        objectiveIconFrame.Visible = true;
                    }
                    else
                    {
                        objectiveIconFrame.Visible = false;
                    }
                }
            }
        }

        public void CreateObjectiveIcon(Character character, Identifier identifier, Identifier option, Entity targetEntity)
        {
            CreateObjectiveIcon(crewList?.Content.GetChildByUserData(character),
                AIObjective.GetSprite(identifier, option, targetEntity),
                GetObjectiveIconTooltip(identifier, option, targetEntity));
        }

        private void CreateObjectiveIcon(GUIComponent characterComponent, AIObjective objective)
        {
            CreateObjectiveIcon(characterComponent,
                objective?.GetSprite(),
                GetObjectiveIconTooltip(objective));
        }

        private LocalizedString GetObjectiveIconTooltip(Identifier identifier, Identifier option, Entity targetEntity)
        {
            LocalizedString variableValue;
            if (OrderPrefab.Prefabs.ContainsKey(identifier))
            {
                var orderPrefab = OrderPrefab.Prefabs[identifier];
                variableValue = CreateOrderTooltip(orderPrefab, option, targetEntity);
            }
            else
            {
                variableValue = TextManager.Get($"objective.{identifier}");
            }
            return variableValue.IsNullOrEmpty() ? variableValue : TextManager.GetWithVariable("crewlistobjectivetooltip", "[objective]", variableValue);
        }

        private LocalizedString GetObjectiveIconTooltip(AIObjective objective)
        {
            return objective == null ? "" :
                GetObjectiveIconTooltip(objective.Identifier, objective.Option, (objective as AIObjectiveOperateItem)?.OperateTarget);
        }

        private GUIComponent GetObjectiveIconParent(GUIComponent characterComponent)
        {
            return characterComponent?
                .GetChild<GUILayoutGroup>()?
                .GetChildByUserData("extraicons")?
                .GetChildByUserData("objectiveicon");
        }

        private void RemoveObjectiveIcon(GUIComponent characterComponent)
        {
            if (GetObjectiveIconParent(characterComponent) is GUIFrame objectiveIconFrame)
            {
                objectiveIconFrame.ClearChildren();
                objectiveIconFrame.Visible = false;
            }
        }

        #endregion

        #region Command UI

        public static bool IsCommandInterfaceOpen
        {
            get
            {
                if (GameMain.GameSession?.CrewManager == null)
                {
                    return false;
                }
                else
                {
                    return GameMain.GameSession.CrewManager.commandFrame != null || GameMain.GameSession.CrewManager.WasCommandInterfaceDisabledThisUpdate;
                }
            }
        }
        private GUIFrame commandFrame, targetFrame;
        private GUIButton centerNode, returnNode, expandNode;
        private GUIFrame shortcutCenterNode;
        private class OptionNode
        {
            public readonly GUIButton Button;
            public readonly Keys Keys;
            public OptionNode(GUIButton guiComponent, Keys keys)
            {
                Button = guiComponent;
                Keys = keys;
            }
        }
        private readonly List<OptionNode> optionNodes = new List<OptionNode>();
        private Keys returnNodeHotkey = Keys.None, expandNodeHotkey = Keys.None;
        private readonly List<GUIComponent> shortcutNodes = new List<GUIComponent>();
        private readonly List<GUIComponent> extraOptionNodes = new List<GUIComponent>();
        private GUICustomComponent nodeConnectors;
        private GUIImage background;

        private GUIButton selectedNode;
        private readonly float selectionTime = 0.75f;
        private float timeSelected = 0.0f;
        private bool clicklessSelectionActive, isOpeningClick, isSelectionHighlighted;

        private Point centerNodeSize, nodeSize, shortcutCenterNodeSize, shortcutNodeSize, returnNodeSize, assignmentNodeSize;
        private float centerNodeMargin, optionNodeMargin, shortcutCenterNodeMargin, shortcutNodeMargin, returnNodeMargin;

        private List<OrderCategory> availableCategories;
        private Stack<GUIButton> historyNodes = new Stack<GUIButton>();
        private readonly List<Character> extraOptionCharacters = new List<Character>();

        /// <summary>
        /// node.Color = node.HighlightColor * nodeColorMultiplier
        /// </summary>
        private const float nodeColorMultiplier = 0.75f;
        private int nodeDistance = (int)(GUI.Scale * 250);
        private const float returnNodeDistanceModifier = 0.65f;
        private OrderPrefab dismissedOrderPrefab => OrderPrefab.Dismissal;
        private Character characterContext;
        private Item itemContext;
        private Hull hullContext;
        private WallSection wallContext;
        private bool isContextual;
        private readonly List<Order> contextualOrders = new List<Order>();
        private Point shorcutCenterNodeOffset;
        private const int maxShortcutNodeCount = 4;

        private bool WasCommandInterfaceDisabledThisUpdate { get; set; }
        public static bool CanIssueOrders
        {
            get
            {
#if DEBUG
                if (Character.Controlled == null) { return true; }
#endif
                return Character.Controlled?.Info != null && Character.Controlled.SpeechImpediment < 100.0f;

            }
        }

        private bool CanCharacterBeHeard()
        {
#if DEBUG
            if (Character.Controlled == null) { return true; }
#endif
            if (Character.Controlled != null)
            {
                if (characterContext == null)
                {
                    return characters.Any(c => c != Character.Controlled && c.CanHearCharacter(Character.Controlled)) || GetOrderableFriendlyNPCs().Any(c => c != Character.Controlled && c.CanHearCharacter(Character.Controlled));
                }
                else
                {
                    return characterContext.CanHearCharacter(Character.Controlled);
                }
            }
            return false;
        }

        private Entity FindEntityContext()
        {
            if (Character.Controlled?.FocusedCharacter is Character focusedCharacter && !focusedCharacter.IsDead &&
                HumanAIController.IsFriendly(Character.Controlled, focusedCharacter) && Character.Controlled.TeamID == focusedCharacter.TeamID)
            {
                if (Character.Controlled?.FocusedItem != null)
                {
                    Vector2 mousePos = GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
                    if (Vector2.Distance(mousePos, focusedCharacter.WorldPosition) < Vector2.Distance(mousePos, Character.Controlled.FocusedItem.WorldPosition))
                    {
                        return focusedCharacter;
                    }
                    else
                    {
                        return Character.Controlled.FocusedItem;
                    }
                }
                else
                {
                    return focusedCharacter;
                }

            }
            else if (TryGetBreachedHullAtHoveredWall(out Hull breachedHull, out wallContext))
            {
                return breachedHull;
            }
            else
            {
                return Character.Controlled?.FocusedItem;
            }
        }

        private void CreateCommandUI(Entity entityContext = null, bool forceContextual = false)
        {
            if (commandFrame != null) { DisableCommandUI(); }

            CharacterHealth.OpenHealthWindow = null;

            // Character context works differently to others as we still use the "basic" command interface,
            // but the order will be automatically assigned to this character
            isContextual = forceContextual;
            if (entityContext is Character character)
            {
                characterContext = character;
                itemContext = null;
                hullContext = null;
                wallContext = null;
                isContextual = false;
            }
            else if (entityContext is Item item)
            {
                itemContext = item;
                characterContext = null;
                hullContext = null;
                wallContext = null;
                isContextual = true;
            }
            else if (entityContext is Hull hull)
            {
                hullContext = hull;
                characterContext = null;
                itemContext = null;
                isContextual = true;
            }

            ScaleCommandUI();

            commandFrame = new GUIFrame(
                new RectTransform(Vector2.One, GUI.Canvas, anchor: Anchor.Center),
                style: null,
                color: Color.Transparent);
            background = new GUIImage(
                new RectTransform(Vector2.One, commandFrame.RectTransform, anchor: Anchor.Center),
                "CommandBackground");
            background.Color = background.Color * 0.8f;
            GUIButton startNode = null;
            if (characterContext == null)
            {
                startNode = new GUIButton(
                    new RectTransform(centerNodeSize, parent: commandFrame.RectTransform, anchor: Anchor.Center),
                    style: null);
                CreateNodeIcon(startNode.RectTransform, "CommandStartNode");
            }
            else
            {
                // Button
                startNode = new GUIButton(
                    new RectTransform(centerNodeSize, parent: commandFrame.RectTransform, anchor: Anchor.Center),
                    style: null);
                // Container
                new GUIImage(
                    new RectTransform(Vector2.One, startNode.RectTransform, anchor: Anchor.Center),
                   "CommandNodeContainer",
                    scaleToFit: true)
                {
                    Color = characterContext.Info?.Job?.Prefab != null ? characterContext.Info.Job.Prefab.UIColor * nodeColorMultiplier : Color.White,
                    HoverColor = characterContext.Info?.Job?.Prefab != null ? characterContext.Info.Job.Prefab.UIColor : Color.White,
                    UserData = "colorsource"
                };
                // Character icon
                var characterIcon = new GUICustomComponent(
                    new RectTransform(Vector2.One, startNode.RectTransform, anchor: Anchor.Center),
                    (spriteBatch, _) =>
                    {
                        if (!(entityContext is Character character) || character?.Info == null) { return; }
                        var node = startNode;
                        character.Info.DrawJobIcon(spriteBatch,
                            new Rectangle((int)(node.Rect.X + node.Rect.Width * 0.5f), (int)(node.Rect.Y + node.Rect.Height * 0.1f), (int)(node.Rect.Width * 0.6f), (int)(node.Rect.Height * 0.8f)));
                        character.Info.DrawIcon(spriteBatch, new Vector2(node.Rect.X + node.Rect.Width * 0.35f, node.Center.Y), node.Rect.Size.ToVector2() * 0.7f);
                    });
                SetCharacterTooltip(characterIcon, entityContext as Character);
            }
            SetCenterNode(startNode);

            availableCategories ??= GetAvailableCategories();

            if (isContextual)
            {
                CreateContextualOrderNodes();
            }
            else
            {
                CreateShortcutNodes();
                CreateOrderCategoryNodes();
            }
            
            CreateNodeConnectors();
            if (Character.Controlled != null)
            {
                Character.Controlled.dontFollowCursor = true;
            }

            HintManager.OnShowCommandInterface();
        }

        public void ToggleCommandUI()
        {
            if (commandFrame == null)
            {
                if (CanIssueOrders)
                {
                    CreateCommandUI();
                }
            }
            else
            {
                DisableCommandUI();
            }
        }

        private void ScaleCommandUI()
        {
            // Node sizes
            nodeSize = new Point((int)(100 * GUI.Scale));
            centerNodeSize = nodeSize;
            returnNodeSize = new Point((int)(48 * GUI.Scale));
            assignmentNodeSize = new Point((int)(64 * GUI.Scale));
            shortcutCenterNodeSize = returnNodeSize;
            shortcutNodeSize = assignmentNodeSize;
            
            // Node margins (used in drawing the connecting lines)
            centerNodeMargin = centerNodeSize.X * 0.5f;
            optionNodeMargin = nodeSize.X * 0.5f;
            shortcutCenterNodeMargin = shortcutCenterNodeSize.X * 0.45f;
            shortcutNodeMargin = shortcutNodeSize.X * 0.5f;
            returnNodeMargin = returnNodeSize.X * 0.5f;

            nodeDistance = (int)(150 * GUI.Scale);
            shorcutCenterNodeOffset = new Point(0, (int)(1.25f * nodeDistance));
        }

        private List<OrderCategory> GetAvailableCategories()
        {
            availableCategories = new List<OrderCategory>();
            foreach (OrderCategory category in Enum.GetValues(typeof(OrderCategory)))
            {
                if (OrderPrefab.Prefabs.Any(o => o.Category == category && !o.IsReport))
                {
                    availableCategories.Add(category);
                }
            }
            return availableCategories;
        }

        private void CreateNodeConnectors()
        {
            nodeConnectors = new GUICustomComponent(
                new RectTransform(Vector2.One, commandFrame.RectTransform),
                onDraw: DrawNodeConnectors)
            {
                CanBeFocused = false
            };
            nodeConnectors.SetAsFirstChild();
            background.SetAsFirstChild();
        }

        private void DrawNodeConnectors(SpriteBatch spriteBatch, GUIComponent container)
        {
            if (centerNode == null || optionNodes == null) { return; }
            var startNodePos = centerNode.Rect.Center.ToVector2();
            // Don't draw connectors for assignment nodes
            if (!(optionNodes.FirstOrDefault()?.Button.UserData is Character))
            {
                // Regular option nodes
                if (targetFrame == null || !targetFrame.Visible)
                {
                    optionNodes.ForEach(n => DrawNodeConnector(startNodePos, centerNodeMargin, n.Button, optionNodeMargin, spriteBatch));
                }
                // Minimap item nodes
                else
                {
                    foreach (var node in optionNodes)
                    {
                        float iconRadius = 0.5f * optionNodeMargin;
                        Vector2 itemPosition = node.Button.Parent.Rect.Center.ToVector2();
                        if (Vector2.Distance(node.Button.Center, itemPosition) <= iconRadius) { continue; }
                        DrawNodeConnector(itemPosition, 0.0f, node.Button, iconRadius, spriteBatch, widthMultiplier: 0.5f);
                        GUI.DrawFilledRectangle(spriteBatch, itemPosition - Vector2.One, new Vector2(3),
                            node.Button.GetChildByUserData("colorsource")?.Color ?? Color.White);
                    }
                }
            }
            DrawNodeConnector(startNodePos, centerNodeMargin, returnNode, returnNodeMargin, spriteBatch);
            if (shortcutCenterNode == null || !shortcutCenterNode.Visible) { return; }
            DrawNodeConnector(startNodePos, centerNodeMargin, shortcutCenterNode, shortcutCenterNodeMargin, spriteBatch);
            startNodePos = shortcutCenterNode.Rect.Center.ToVector2();
            shortcutNodes.ForEach(n => DrawNodeConnector(startNodePos, shortcutCenterNodeMargin, n, shortcutNodeMargin, spriteBatch));
        }

        private void DrawNodeConnector(Vector2 startNodePos, float startNodeMargin, GUIComponent endNode, float endNodeMargin, SpriteBatch spriteBatch, float widthMultiplier = 1.0f)
        {
            if (endNode == null || !endNode.Visible) { return; }
            var endNodePos = endNode.Rect.Center.ToVector2();
            var direction = (endNodePos - startNodePos) / Vector2.Distance(startNodePos, endNodePos);
            var start = startNodePos + direction * startNodeMargin;
            var end = endNodePos - direction * endNodeMargin;
            var colorSource = endNode.GetChildByUserData("colorsource");
            if ((selectedNode == null && endNode != shortcutCenterNode && GUI.IsMouseOn(endNode)) ||
                (isSelectionHighlighted && (endNode == selectedNode || (endNode == shortcutCenterNode && shortcutNodes.Any(n => GUI.IsMouseOn(n))))))
            {
                GUI.DrawLine(spriteBatch, start, end, colorSource?.HoverColor ?? Color.White, width: Math.Max(widthMultiplier * 4.0f, 1.0f));
            }
            else
            {
                GUI.DrawLine(spriteBatch, start, end, colorSource?.Color ?? Color.White * nodeColorMultiplier, width: Math.Max(widthMultiplier * 2.0f, 1.0f));
            }
        }

        public void DisableCommandUI()
        {
            if (commandFrame == null) { return; }
            WasCommandInterfaceDisabledThisUpdate = true;
            RemoveOptionNodes();
            historyNodes.Clear();
            nodeConnectors = null;
            centerNode = null;
            returnNode = null;
            expandNode = null;
            shortcutCenterNode = null;
            targetFrame = null;
            selectedNode = null;
            timeSelected = 0;
            background = null;
            commandFrame = null;
            extraOptionCharacters.Clear();
            isOpeningClick = isSelectionHighlighted = false;
            characterContext = null;
            itemContext = null;
            isContextual = false;
            contextualOrders.Clear();
            returnNodeHotkey = expandNodeHotkey = Keys.None;
            if (Character.Controlled != null)
            {
                Character.Controlled.dontFollowCursor = false;
            }
        }

        private bool NavigateForward(GUIButton node, object userData)
        {
            if (commandFrame == null) { return false; }
            if (!(optionNodes.Find(n => n.Button == node) is OptionNode optionNode) || !optionNodes.Remove(optionNode))
            {
                shortcutNodes.Remove(node);
            };
            RemoveOptionNodes();
            bool wasMinimapVisible = targetFrame != null && targetFrame.Visible;
            HideMinimap();

            if (returnNode != null)
            {
                returnNode.RemoveChild(returnNode.GetChildByUserData("hotkey"));
                returnNode.Children.ForEach(child => child.Visible = false);
                returnNode.Visible = false;
                historyNodes.Push(returnNode);
            }

            // When the mini map is shown, always position the return node on the bottom
            bool placeReturnNodeOnTheBottom = wasMinimapVisible ||
                (node?.UserData is Order order && order.GetMatchingItems(true, interactableFor: characterContext ?? Character.Controlled).Count > 1);
            var offset = placeReturnNodeOnTheBottom ?
                new Point(0, (int)(returnNodeDistanceModifier * nodeDistance)) :
                node.RectTransform.AbsoluteOffset.Multiply(-returnNodeDistanceModifier);
            SetReturnNode(centerNode, offset);

            SetCenterNode(node);
            if (shortcutCenterNode != null)
            {
                commandFrame.RemoveChild(shortcutCenterNode);
                shortcutCenterNode = null;
            }

            CreateNodes(userData);
            CreateReturnNodeHotkey();
            return true;
        }

        private bool NavigateBackward(GUIButton node, object userData)
        {
            if (commandFrame == null) { return false; }
            RemoveOptionNodes();
            HideMinimap();
            // TODO: Center node could move to option node instead of being removed
            commandFrame.RemoveChild(centerNode);
            SetCenterNode(node);
            if (historyNodes.Count > 0)
            {
                var historyNode = historyNodes.Pop();
                SetReturnNode(historyNode, historyNode.RectTransform.AbsoluteOffset);
                historyNode.Visible = true;
                historyNode.RemoveChild(historyNode.GetChildByUserData("hotkey"));
                historyNode.Children.ForEach(child => child.Visible = true);
            }
            else
            {
                returnNode = null;
            }
            CreateNodes(userData);
            CreateReturnNodeHotkey();
            return true;
        }

        private void HideMinimap()
        {
            if (targetFrame == null || !targetFrame.Visible) { return; }
            targetFrame.Visible = false;
            // Reset the node connectors to their original parent
            nodeConnectors.RectTransform.Parent = commandFrame.RectTransform;
            nodeConnectors.RectTransform.RepositionChildInHierarchy(1);
        }

        private void CreateReturnNodeHotkey()
        {
            if (returnNode != null && returnNode.Visible)
            {
                var hotkey = 1;
                if (targetFrame == null || !targetFrame.Visible)
                {
                    hotkey = optionNodes.Count + 1;
                    if (expandNode != null && expandNode.Visible) { hotkey += 1; }
                }
                CreateHotkeyIcon(returnNode.RectTransform, hotkey % 10, true);
                returnNodeHotkey = Keys.D0 + hotkey % 10;
            }
            else
            {
                returnNodeHotkey = Keys.None;
            }
        }

        private void SetCenterNode(GUIButton node, bool resetAnchor = false)
        {
            node.RectTransform.Parent = commandFrame.RectTransform;
            if (resetAnchor)
            {
                node.RectTransform.SetPosition(Anchor.Center);
            }
            node.RectTransform.SetPosition(Anchor.Center);
            node.RectTransform.MoveOverTime(Point.Zero, CommandNodeAnimDuration);
            node.RectTransform.ScaleOverTime(centerNodeSize, CommandNodeAnimDuration);
            node.RemoveChild(node.GetChildByUserData("hotkey"));
            foreach (GUIComponent c in node.Children)
            {
                c.Color = c.HoverColor * nodeColorMultiplier;
                c.HoverColor = c.Color;
                c.PressedColor = c.Color;
                c.SelectedColor = c.Color;
                SetCharacterTooltip(c, characterContext);
            }
            node.OnClicked = null;
            node.OnSecondaryClicked = null;
            node.CanBeFocused = false;
            centerNode = node;
        }

        private void SetReturnNode(GUIButton node, Point offset)
        {
            node.RectTransform.MoveOverTime(offset, CommandNodeAnimDuration);
            node.RectTransform.ScaleOverTime(returnNodeSize, CommandNodeAnimDuration);
            foreach (GUIComponent c in node.Children)
            {
                c.HoverColor = c.Color * (1 / nodeColorMultiplier);
                c.PressedColor = c.HoverColor;
                c.SelectedColor = c.HoverColor;
                c.ToolTip = TextManager.Get("commandui.return");
            }
            node.OnClicked = NavigateBackward;
            node.OnSecondaryClicked = null;
            node.CanBeFocused = true;
            returnNode = node;
        }

        private bool CreateNodes(object userData)
        {
            if (userData == null)
            {
                if (isContextual)
                {
                    CreateContextualOrderNodes();
                }
                else
                {
                    CreateShortcutNodes();
                    CreateOrderCategoryNodes();
                }
            }
            else if (userData is OrderCategory category)
            {
                CreateOrderNodes(category);
            }
            else if (userData is Order nodeOrder)
            {
                Submarine submarine = GetTargetSubmarine();
                List<Item> matchingItems = null;
                if (itemContext == null && nodeOrder.MustSetTarget)
                {
                    matchingItems = nodeOrder.GetMatchingItems(submarine, true, interactableFor: characterContext ?? Character.Controlled);
                }
                //more than one target item -> create a minimap-like selection with a pic of the sub
                if (itemContext == null && !(nodeOrder.TargetEntity is Item) && matchingItems != null && matchingItems.Count > 1)
                {
                    CreateMinimapNodes(nodeOrder, submarine, matchingItems);
                }
                //only one target (or an order with no particular targets), just show options
                else
                {
                    CreateOrderOptionNodes(nodeOrder, itemContext ?? nodeOrder.TargetEntity as Item ?? matchingItems?.FirstOrDefault());
                }
            }
            else if (userData is MinimapNodeData {Order: { } minimapOrder} && minimapOrder.Prefab.HasOptions)
            {
                CreateOrderOptionNodes(minimapOrder, minimapOrder.TargetEntity as Item);
            }
            else
            {
                DebugConsole.ThrowError($"Unexpected node user data of type {userData.GetType()} when creating command interface nodes");
                return false;
            }
            return true;
        }

        private void RemoveOptionNodes()
        {
            if (commandFrame != null)
            {
                optionNodes.ForEach(node => commandFrame.RemoveChild(node.Button));
                shortcutNodes.ForEach(node => commandFrame.RemoveChild(node));
                commandFrame.RemoveChild(expandNode);
            }
            optionNodes.Clear();
            shortcutNodes.Clear();
            expandNode = null;
            expandNodeHotkey = Keys.None;
            RemoveExtraOptionNodes();
        }

        private void RemoveExtraOptionNodes()
        {
            if (commandFrame != null)
            {
                extraOptionNodes.ForEach(node => commandFrame.RemoveChild(node));
            }
            extraOptionNodes.Clear();
        }

        private void CreateOrderCategoryNodes()
        {
            // TODO: Calculate firstAngle parameter based on category count
            var offsets = MathUtils.GetPointsOnCircumference(Vector2.Zero, nodeDistance, availableCategories.Count, MathHelper.ToRadians(225));
            var offsetIndex = 0;
            availableCategories.ForEach(oc => CreateOrderCategoryNode(oc, offsets[offsetIndex++].ToPoint(), offsetIndex));
        }

        private void CreateOrderCategoryNode(OrderCategory category, Point offset, int hotkey)
        {
            var node = new GUIButton(
                new RectTransform(nodeSize, parent: commandFrame.RectTransform, anchor: Anchor.Center), style: null)
            {
                UserData = category,
                OnClicked = NavigateForward
            };

            node.RectTransform.MoveOverTime(offset, CommandNodeAnimDuration);
            var icon = OrderCategoryIcon.OrderCategoryIcons.FirstOrDefault(ic => ic.Category == category);
            if (!(icon is null))
            {
                var tooltip = TextManager.Get($"ordercategorytitle.{category}");
                var categoryDescription = TextManager.Get($"ordercategorydescription.{category}");
                if (!categoryDescription.IsNullOrWhiteSpace()) { tooltip += "\n" + categoryDescription; }
                CreateNodeIcon(Vector2.One, node.RectTransform, icon.Sprite, icon.Color, tooltip: tooltip);
            }
            CreateHotkeyIcon(node.RectTransform, hotkey % 10);
            optionNodes.Add(new OptionNode(node, Keys.D0 + hotkey % 10));
        }

        /// <remarks>
        /// The order giver doesn't need to be set for the Order instances as it will be set when the node button is clicked.
        /// </remarks>
        private void CreateShortcutNodes()
        {
            if (!(GetTargetSubmarine() is { } sub)) { return; }
            shortcutNodes.Clear();
            var subItems = sub.GetItems(false);
            if (CanFitMoreNodes() && subItems.Find(i => i.HasTag("reactor") && i.IsPlayerTeamInteractable)?.GetComponent<Reactor>() is Reactor reactor)
            {
                float reactorOutput = -reactor.CurrPowerConsumption;
                // If player is not an engineer AND the reactor is not powered up AND nobody is using the reactor
                // --> Create shortcut node for "Operate Reactor" order's "Power Up" option
                if (ShouldDelegateOrder("operatereactor") && reactorOutput < float.Epsilon && characters.None(c => c.SelectedItem == reactor.Item))
                {
                    var orderPrefab = OrderPrefab.Prefabs["operatereactor"];
                    var order = new Order(orderPrefab, orderPrefab.Options[0], reactor.Item, reactor);
                    if (IsNonDuplicateOrder(order))
                    {
                        AddOrderNode(order);
                    }
                }
            }
            // TODO: Reconsider the conditions as bot captain can have the nav term selected without operating it
            // If player is not a captain AND nobody is using the nav terminal AND the nav terminal is powered up
            // --> Create shortcut node for Steer order
            if (CanFitMoreNodes() && ShouldDelegateOrder("steer") && IsNonDuplicateOrderPrefab(OrderPrefab.Prefabs["steer"]) &&
                subItems.Find(i => i.HasTag("navterminal") && i.IsPlayerTeamInteractable) is Item nav && characters.None(c => c.SelectedItem == nav) &&
                nav.GetComponent<Steering>() is Steering steering && steering.Voltage > steering.MinVoltage)
            {
                var order = new Order(OrderPrefab.Prefabs["steer"], steering.Item, steering);
                AddOrderNode(order);
            }
            // If player is not a security officer AND invaders are reported
            // --> Create shorcut node for Fight Intruders order
            if (CanFitMoreNodes() && ShouldDelegateOrder("fightintruders") &&
                ActiveOrders.Any(o => o.Order.Identifier == "reportintruders") &&
                IsNonDuplicateOrderPrefab(OrderPrefab.Prefabs["fightintruders"]))
            {
                AddOrderNodeWithIdentifier("fightintruders");
            }
            // If player is not a mechanic AND a breach has been reported
            // --> Create shorcut node for Fix Leaks order
            if (CanFitMoreNodes() && ShouldDelegateOrder("fixleaks") &&
                IsNonDuplicateOrderPrefab(OrderPrefab.Prefabs["fixleaks"]) &&
                ActiveOrders.Any(o => o.Order.Identifier == "reportbreach"))
            {
                AddOrderNodeWithIdentifier("fixleaks");
            }
            // --> Create shortcut nodes for the Repair orders
            if (CanFitMoreNodes() && ActiveOrders.Any(o => o.Order.Identifier == "reportbrokendevices"))
            {
                var reportBrokenDevices = OrderPrefab.Prefabs["reportbrokendevices"];
                // TODO: Doesn't work for player issued reports, because they don't have a target.
                bool useSpecificRepairOrder = false;
                if (CanFitMoreNodes() && ShouldDelegateOrder("repairelectrical") &&
                    ActiveOrders.Any(o => o.Order.Prefab == reportBrokenDevices && o.Order.TargetItemComponent is Repairable r && r.requiredSkills.Any(s => s.Identifier == "electrical")))
                {
                    if (IsNonDuplicateOrderPrefab(OrderPrefab.Prefabs["repairelectrical"]))
                    {
                        AddOrderNodeWithIdentifier("repairelectrical");
                    }
                    useSpecificRepairOrder = true;
                }
                if (CanFitMoreNodes() && ShouldDelegateOrder("repairmechanical") &&
                    ActiveOrders.Any(o => o.Order.Prefab == reportBrokenDevices && o.Order.TargetItemComponent is Repairable r && r.requiredSkills.Any(s => s.Identifier == "mechanical")))
                {
                    if (IsNonDuplicateOrderPrefab(OrderPrefab.Prefabs["repairmechanical"]))
                    {
                        AddOrderNodeWithIdentifier("repairmechanical");
                    }
                    useSpecificRepairOrder = true;
                }
                if (!useSpecificRepairOrder && CanFitMoreNodes() && ShouldDelegateOrder("repairsystems") && OrderPrefab.Prefabs["repairsystems"] is OrderPrefab repairOrder && IsNonDuplicateOrderPrefab(repairOrder))
                {
                    AddOrderNodeWithIdentifier("repairsystems");
                }
            }
            // If fire is reported
            // --> Create shortcut node for Extinguish Fires order
            if (CanFitMoreNodes() && IsNonDuplicateOrderPrefab(OrderPrefab.Prefabs["extinguishfires"]) &&
                ActiveOrders.Any(o => o.Order.Identifier == "reportfire"))
            {
                AddOrderNodeWithIdentifier("extinguishfires");
            }
            if (CanFitMoreNodes() && characterContext?.Info?.Job?.Prefab?.AppropriateOrders != null)
            {
                foreach (Identifier orderIdentifier in characterContext.Info.Job.Prefab.AppropriateOrders)
                {
                    if (OrderPrefab.Prefabs[orderIdentifier] is OrderPrefab orderPrefab && IsNonDuplicateOrderPrefab(orderPrefab) &&
                        shortcutNodes.None(n => n.UserData is Order order && order.Identifier == orderIdentifier) &&
                        !orderPrefab.IsReport && orderPrefab.Category != null)
                    {
                        if (!orderPrefab.MustSetTarget || orderPrefab.GetMatchingItems(sub, true, interactableFor: characterContext ?? Character.Controlled).Any())
                        {
                            var order = orderPrefab.CreateInstance(OrderPrefab.OrderTargetType.Entity);
                            AddOrderNode(order);
                        }
                        if (!CanFitMoreNodes()) { break; }
                    }
                }
            }
            if (CanFitMoreNodes() && characterContext != null && !characterContext.IsDismissed)
            {
                var order = OrderPrefab.Dismissal.CreateInstance(OrderPrefab.OrderTargetType.Entity);
                AddOrderNode(order);
            }
            shortcutNodes.RemoveAll(n => n.UserData is Order o && !IsOrderAvailable(o));
            if (shortcutNodes.Count < 1) { return; }
            shortcutCenterNode = new GUIFrame(new RectTransform(shortcutCenterNodeSize, parent: commandFrame.RectTransform, anchor: Anchor.Center), style: null)
            {
                CanBeFocused = false
            };
            CreateNodeIcon(shortcutCenterNode.RectTransform, "CommandShortcutNode");
            foreach (GUIComponent c in shortcutCenterNode.Children)
            {
                c.HoverColor = c.Color;
                c.PressedColor = c.Color;
                c.SelectedColor = c.Color;
            }
            shortcutCenterNode.RectTransform.MoveOverTime(shorcutCenterNodeOffset, CommandNodeAnimDuration);
            int nodeCountForCalculations = shortcutNodes.Count * 2 + 2;
            Vector2[] offsets = MathUtils.GetPointsOnCircumference(Vector2.Zero, 0.75f * nodeDistance, nodeCountForCalculations);
            int firstOffsetIndex = nodeCountForCalculations / 2 - 1;
            for (int i = 0; i < shortcutNodes.Count; i++)
            {
                shortcutNodes[i].RectTransform.Parent = commandFrame.RectTransform;
                shortcutNodes[i].RectTransform.MoveOverTime(shorcutCenterNodeOffset + offsets[firstOffsetIndex - i].ToPoint(), CommandNodeAnimDuration);
            }

            bool CanFitMoreNodes()
            {
                return shortcutNodes.Count < maxShortcutNodeCount;
            }
            static bool ShouldDelegateOrder(string orderIdentifier) => ShouldDelegateOrderId(orderIdentifier.ToIdentifier());
            static bool ShouldDelegateOrderId(Identifier orderIdentifier)
            {
                return !(Character.Controlled is Character c) || !(c?.Info?.Job != null && c.Info.Job.Prefab.AppropriateOrders.Contains(orderIdentifier));
            }
            bool IsNonDuplicateOrder(Order order) => IsNonDuplicateOrderPrefab(order.Prefab, order.Option);
            bool IsNonDuplicateOrderPrefab(OrderPrefab orderPrefab, Identifier option = default)
            {
                return characterContext == null || (option.IsEmpty ?
                    characterContext.CurrentOrders.None(oi => oi?.Identifier == orderPrefab?.Identifier) :
                    characterContext.CurrentOrders.None(oi => oi?.Identifier == orderPrefab?.Identifier && oi.Option == option));
            }
            void AddOrderNodeWithIdentifier(string identifier)
            {
                var order = OrderPrefab.Prefabs[identifier].CreateInstance(OrderPrefab.OrderTargetType.Entity);
                AddOrderNode(order);
            }
            void AddOrderNode(Order order)
            {
                var node = order.Option.IsEmpty ?
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, order, -1) :
                    CreateOrderOptionNode(shortcutNodeSize, null, Point.Zero, order, -1);
                shortcutNodes.Add(node);
            } 
        }

        private void CreateOrderNodes(OrderCategory orderCategory)
        {
            var orderPrefabs = OrderPrefab.Prefabs.Where(o => o.Category == orderCategory && !o.IsReport && IsOrderAvailable(o)).OrderBy(o => o.Identifier).ToArray();
            Order order;
            bool disableNode;
            var offsets = MathUtils.GetPointsOnCircumference(Vector2.Zero, nodeDistance,
                GetCircumferencePointCount(orderPrefabs.Length), GetFirstNodeAngle(orderPrefabs.Length));
            for (int i = 0; i < orderPrefabs.Length; i++)
            {
                order = orderPrefabs[i].CreateInstance(OrderPrefab.OrderTargetType.Entity);
                disableNode = !CanCharacterBeHeard() ||
                              (order.MustSetTarget && (order.ItemComponentType != null || order.GetTargetItems().Any() || order.RequireItems.Any()) &&
                               order.GetMatchingItems(true, interactableFor: characterContext ?? Character.Controlled).None());
                optionNodes.Add(new OptionNode(
                    CreateOrderNode(nodeSize, commandFrame.RectTransform, offsets[i].ToPoint(), order, (i + 1) % 10, disableNode: disableNode, checkIfOrderCanBeHeard: false),
                    !disableNode ? Keys.D0 + (i + 1) % 10 : Keys.None));
            }
        }

        /// <summary>
        /// Create order nodes based on the item context
        /// </summary>
        /// <remarks>
        /// The order giver doesn't need to be set for the Order instances as it will be set when the node button is clicked.
        /// </remarks>
        private void CreateContextualOrderNodes()
        {
            if (contextualOrders.None())
            {
                // Check if targeting an item or a hull
                if (itemContext != null && itemContext.IsPlayerTeamInteractable)
                {
                    ItemComponent targetComponent;
                    foreach (OrderPrefab p in OrderPrefab.Prefabs)
                    {
                        targetComponent = null;
                        if (p.UseController && itemContext.Components.None(c => c is Controller)) { continue; }
                        if (p.HasOptionSpecificTargetItems)
                        {
                            foreach (Identifier option in p.Options)
                            {
                                if (p.TargetItemsMatchItem(itemContext, option))
                                {
                                    contextualOrders.Add(new Order(p, option, itemContext, targetComponent));
                                }
                            }
                        }
                        else if (p.TargetItemsMatchItem(itemContext) || p.TryGetTargetItemComponent(itemContext, out targetComponent))
                        {
                            contextualOrders.Add(p.HasOptions ?
                                p.CreateInstance(OrderPrefab.OrderTargetType.Entity) :
                                new Order(p, itemContext, targetComponent));
                        }
                    }
                    // If targeting a periscope connected to a turret, show the 'operateweapons' order
                    var operateWeaponsPrefab = OrderPrefab.Prefabs["operateweapons"];
                    if (contextualOrders.None(o => o.Identifier == "operateweapons") && itemContext.Components.Any(c => c is Controller))
                    {
                        var turret = itemContext.GetConnectedComponents<Turret>().FirstOrDefault(c => operateWeaponsPrefab.TargetItemsMatchItem(c.Item)) ??
                            itemContext.GetConnectedComponents<Turret>(recursive: true).FirstOrDefault(c => operateWeaponsPrefab.TargetItemsMatchItem(c.Item));
                        if (turret != null)
                        {
                            contextualOrders.Add(new Order(operateWeaponsPrefab, turret.Item, turret));
                        }
                    }
                    // If targeting a repairable item with condition below the repair threshold, show the 'repairsystems' order
                    if (contextualOrders.None(order => order.Identifier == "repairsystems") && itemContext.Repairables.Any(r => r.IsBelowRepairThreshold))
                    {
                        if (itemContext.Repairables.Any(r => r != null && r.requiredSkills.Any(s => s != null && s.Identifier.Equals("electrical"))))
                        {
                            contextualOrders.Add(new Order(OrderPrefab.Prefabs["repairelectrical"], itemContext, targetItem: null));
                        }
                        else if (itemContext.Repairables.Any(r => r != null && r.requiredSkills.Any(s => s != null && s.Identifier.Equals("mechanical"))))
                        {
                            contextualOrders.Add(new Order(OrderPrefab.Prefabs["repairmechanical"], itemContext, targetItem: null));
                        }
                        else
                        {
                            contextualOrders.Add(new Order(OrderPrefab.Prefabs["repairsystems"], itemContext, targetItem: null));
                        }
                    }
                    // Remove the 'pumpwater' order if the target pump is auto-controlled (as it will immediately overwrite the work done by the bot)
                    if (contextualOrders.FirstOrDefault(order => order.Identifier.Equals("pumpwater")) is Order pumpOrder &&
                        itemContext.Components.FirstOrDefault(c => c.GetType() == pumpOrder.ItemComponentType) is Pump pump && pump.IsAutoControlled)
                    {
                        contextualOrders.Remove(pumpOrder);
                    }
                    if (contextualOrders.None(info => info.Identifier.Equals("cleanupitems")))
                    {
                        if (AIObjectiveCleanupItems.IsValidTarget(itemContext, Character.Controlled, checkInventory: false) || AIObjectiveCleanupItems.IsValidContainer(itemContext, Character.Controlled))
                        {
                            contextualOrders.Add(new Order(OrderPrefab.Prefabs["cleanupitems"], itemContext, targetItem: null));
                        }
                    }
                    AddIgnoreOrder(itemContext);
                }
                else if (hullContext != null)
                {
                    contextualOrders.Add(new Order(OrderPrefab.Prefabs["fixleaks"], hullContext, targetItem: null));
                    if (wallContext != null)
                    {
                        AddIgnoreOrder(wallContext);
                    }
                }
                void AddIgnoreOrder(IIgnorable target)
                {
                    var orderIdentifier = "ignorethis";
                    if (!target.OrderedToBeIgnored && contextualOrders.None(order => order.Identifier == orderIdentifier))
                    {
                        AddOrder();
                    }
                    else
                    {
                        orderIdentifier = "unignorethis";
                        if (target.OrderedToBeIgnored && contextualOrders.None(order => order.Identifier == orderIdentifier))
                        {
                            AddOrder();
                        }
                    }

                    void AddOrder()
                    {
                        if (target is WallSection ws)
                        {
                            contextualOrders.Add(new Order(OrderPrefab.Prefabs[orderIdentifier], ws.Wall, ws.Wall.Sections.IndexOf(ws), orderGiver: Character.Controlled));
                        }
                        else
                        {
                            contextualOrders.Add(new Order(OrderPrefab.Prefabs[orderIdentifier], target as Entity, null, Character.Controlled));
                        }
                    }
                }
                if (contextualOrders.None(order => order.Identifier.Equals("wait")))
                {
                    Vector2 position = GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
                    Hull hull = Hull.FindHull(position, guess: Character.Controlled?.CurrentHull);
                    contextualOrders.Add(new Order(OrderPrefab.Prefabs["wait"], new OrderTarget(position, hull)));
                }
                if (contextualOrders.None(order => order.Category != OrderCategory.Movement) && characters.Any(c => c != Character.Controlled))
                {
                    if (contextualOrders.None(order => order.Identifier.Equals("follow")))
                    {
                        contextualOrders.Add(OrderPrefab.Prefabs["follow"].CreateInstance(OrderPrefab.OrderTargetType.Entity));
                    }
                }
                // Show 'dismiss' order only when there are crew members with active orders
                if (contextualOrders.None(order => order.IsDismissal) && characters.Any(c => !c.IsDismissed))
                {
                    contextualOrders.Add(OrderPrefab.Dismissal.CreateInstance(OrderPrefab.OrderTargetType.Entity));
                }
            }
            contextualOrders.RemoveAll(o => !IsOrderAvailable(o));
            var offsets = MathUtils.GetPointsOnCircumference(Vector2.Zero, nodeDistance, contextualOrders.Count, MathHelper.ToRadians(90f + 180f / contextualOrders.Count));
            bool disableNode = !CanCharacterBeHeard();
            for (int i = 0; i < contextualOrders.Count; i++)
            {
                var order = contextualOrders[i];
                int hotkey = (i + 1) % 10;
                var component = order.Option.IsEmpty ?
                    CreateOrderNode(nodeSize, commandFrame.RectTransform, offsets[i].ToPoint(), order, hotkey, disableNode: disableNode, checkIfOrderCanBeHeard: false) :
                    CreateOrderOptionNode(nodeSize, commandFrame.RectTransform, offsets[i].ToPoint(), order, hotkey);
                optionNodes.Add(new OptionNode(component, !disableNode ? Keys.D0 + (i + 1) % 10 : Keys.None));
            }
        }

        // TODO: there's duplicate logic here and above -> would be better to refactor so that the conditions are only defined in one place
        public static bool DoesItemHaveContextualOrders(Item item)
        {
            if (OrderPrefab.Prefabs.Any(o => o.TargetItemsMatchItem(item))) { return true; }
            if (OrderPrefab.Prefabs.Any(o => o.TryGetTargetItemComponent(item, out _))) { return true; }
            if (AIObjectiveCleanupItems.IsValidTarget(item, Character.Controlled, checkInventory: false)) { return true; }
            if (AIObjectiveCleanupItems.IsValidContainer(item, Character.Controlled)) { return true; }
            if (OrderPrefab.Prefabs.TryGet("loaditems", out OrderPrefab loadItemsPrefab) && AIObjectiveLoadItems.IsValidTarget(item, Character.Controlled, targetContainerTags: loadItemsPrefab.GetTargetItems())) { return true; }
            if (item.Repairables.Any(r => r.IsBelowRepairThreshold)) { return true; }
            return OrderPrefab.Prefabs.TryGet("operateweapons", out OrderPrefab operateWeaponsPrefab) && item.Components.Any(c => c is Controller) &&
                   (item.GetConnectedComponents<Turret>().Any(c => operateWeaponsPrefab.TargetItemsMatchItem(c.Item)) ||
                    item.GetConnectedComponents<Turret>(recursive: true).Any(c => operateWeaponsPrefab.TargetItemsMatchItem(c.Item)));
        }

        /// <param name="hotkey">Use a negative value (e.g. -1) if there should be no hotkey associated with the node</param>
        private GUIButton CreateOrderNode(Point size, RectTransform parent, Point offset, Order order, int hotkey, bool disableNode = false, bool checkIfOrderCanBeHeard = true)
        {
            var node = new GUIButton(
                new RectTransform(size, parent: parent, anchor: Anchor.Center), style: null)
            {
                UserData = order
            };

            node.RectTransform.MoveOverTime(offset, CommandNodeAnimDuration);

            if (checkIfOrderCanBeHeard && !disableNode)
            {
                disableNode = !CanCharacterBeHeard();
            }

            bool mustSetOptionOrTarget = order.Prefab.HasOptions;
            Item orderTargetEntity = null;
            
            // If the order doesn't have options, but must set a target,
            // we have to check if there's only one possible target available
            // so we know to directly target that with the order
            if (!mustSetOptionOrTarget && order.MustSetTarget && itemContext == null)
            {
                var matchingItems = order.GetMatchingItems(GetTargetSubmarine(), true, interactableFor: characterContext ?? Character.Controlled);
                if (matchingItems.Count > 1)
                {
                    mustSetOptionOrTarget = true;
                }
                else
                {
                    orderTargetEntity = matchingItems.FirstOrDefault();
                }
            }

            node.OnClicked = (button, userData) =>
            {
                if (disableNode || !CanIssueOrders) { return false; }
                var o = userData as Order;
                if (mustSetOptionOrTarget)
                {
                    NavigateForward(button, userData);
                }
                else if (o.MustManuallyAssign && characterContext == null)
                {
                    CreateAssignmentNodes(node);
                }
                else
                {
                    if (orderTargetEntity != null)
                    {
                        o = new Order(o.Prefab, orderTargetEntity, orderTargetEntity.Components.FirstOrDefault(ic => ic.GetType() == order.ItemComponentType), orderGiver: order.OrderGiver);
                    }
                    var character = !o.TargetAllCharacters ? characterContext ?? GetCharacterForQuickAssignment(o) : null;
                    int priority = GetManualOrderPriority(character, o);
                    SetCharacterOrder(character, o.WithManualPriority(priority).WithOrderGiver(Character.Controlled));
                    DisableCommandUI();
                }
                return true;
            };

            if (CanOpenManualAssignment(node))
            {
                node.OnSecondaryClicked = (button, _) => CreateAssignmentNodes(button);
            }
            var showAssignmentTooltip = !mustSetOptionOrTarget && characterContext == null && !order.MustManuallyAssign && !order.TargetAllCharacters;
            var orderName = GetOrderNameBasedOnContextuality(order);
            var icon = CreateNodeIcon(Vector2.One, node.RectTransform, order.SymbolSprite, order.Color,
                tooltip: !showAssignmentTooltip ? orderName : orderName +
                    "\n" + PlayerInput.PrimaryMouseLabel + ": " + TextManager.Get("commandui.quickassigntooltip") +
                    "\n" + PlayerInput.SecondaryMouseLabel + ": " + TextManager.Get("commandui.manualassigntooltip"));
            
            if (disableNode)
            {
                node.CanBeFocused = icon.CanBeFocused = false;
                CreateBlockIcon(node.RectTransform, tooltip: TextManager.Get(characterContext == null ? "nocharactercanhear" : "thischaractercanthear"));
            }
            else if (hotkey >= 0)
            {
                CreateHotkeyIcon(node.RectTransform, hotkey);
            }
            return node;
        }

        public struct MinimapNodeData
        {
            public Order Order;
        }
        
        private void CreateMinimapNodes(Order order, Submarine submarine, List<Item> matchingItems)
        {
            // TODO: Further adjustments to frameSize calculations
            // I just divided the existing sizes by 2 to get it working quickly without it overlapping too much
            Point frameSize;
            Rectangle subBorders = submarine.GetDockedBorders();
            if (subBorders.Width > subBorders.Height)
            {
                frameSize.X = Math.Min(GameMain.GraphicsWidth / 2, GameMain.GraphicsWidth - 50) / 2;
                //height depends on the dimensions of the sub
                frameSize.Y = (int)(frameSize.X * (subBorders.Height / (float)subBorders.Width));
            }
            else
            {
                frameSize.Y = Math.Min((int)(GameMain.GraphicsHeight * 0.6f), GameMain.GraphicsHeight - 50) / 2;
                //width depends on the dimensions of the sub
                frameSize.X = (int)(frameSize.Y * (subBorders.Width / (float)subBorders.Height));
            }

            // TODO: Use the old targetFrame if possible
            targetFrame = new GUIFrame(
                new RectTransform(frameSize, parent: commandFrame.RectTransform, anchor: Anchor.Center)
                {
                    AbsoluteOffset = new Point(0, -150),
                    Pivot = Pivot.BottomCenter
                },
                style: "InnerFrame");

            submarine.CreateMiniMap(targetFrame, pointsOfInterest: matchingItems);

            new GUICustomComponent(new RectTransform(Vector2.One, targetFrame.RectTransform), onDraw: DrawMiniMapOverlay)
            {
                CanBeFocused = false,
                UserData = submarine
            };

            List<GUIComponent> optionElements = new List<GUIComponent>();
            foreach (Item item in matchingItems)
            {
                var itemTargetFrame = targetFrame.Children.First().FindChild(item);
                if (itemTargetFrame == null) { continue; }

                var anchor = Anchor.TopLeft;
                if (itemTargetFrame.RectTransform.RelativeOffset.X < 0.5f)
                {
                    if (itemTargetFrame.RectTransform.RelativeOffset.Y < 0.5f)
                    {
                        anchor = Anchor.BottomRight;
                    }
                    else
                    {
                        anchor = Anchor.TopRight;
                    }
                }
                else if (itemTargetFrame.RectTransform.RelativeOffset.Y < 0.5f)
                {
                    anchor = Anchor.BottomLeft;
                }

                var userData = new MinimapNodeData
                {
                    Order = item == null ? order : order.WithItemComponent(item, order.GetTargetItemComponent(item))
                };
                var optionElement = new GUIButton(
                    new RectTransform(
                        new Point((int)(50 * GUI.Scale)),
                        parent: itemTargetFrame.RectTransform,
                        anchor: anchor),
                    style: null)
                {
                    UserData = userData,
                    Font = GUIStyle.SmallFont,
                    OnClicked = (button, obj) =>
                    {
                        if (!CanIssueOrders) { return false; }
                        var o = (MinimapNodeData)obj;
                        if (o.Order.Prefab.HasOptions)
                        {
                            NavigateForward(button, o);
                        }
                        else if (o.Order.MustManuallyAssign && characterContext == null)
                        {
                            CreateAssignmentNodes(button);
                        }
                        else
                        {
                            var character = characterContext ?? GetCharacterForQuickAssignment(o.Order);
                            int priority = GetManualOrderPriority(character, o.Order);
                            SetCharacterOrder(
                                    character,
                                    o.Order
                                        .WithManualPriority(priority)
                                        .WithOrderGiver(Character.Controlled));
                            DisableCommandUI();
                        }
                        return true;
                    }
                };
                if (CanOpenManualAssignmentMinimapOrder(optionElement))
                {
                    optionElement.OnSecondaryClicked = (button, _) => CreateAssignmentNodes(button);
                }
                var colorMultiplier = characters.Any(c => c.CurrentOrders.Any(o => o != null &&
                    o.Identifier == userData.Order.Identifier &&
                    o.TargetEntity == userData.Order.TargetEntity)) ? 0.5f : 1f;
                CreateNodeIcon(Vector2.One, optionElement.RectTransform, item.Prefab.MinimapIcon ?? order.SymbolSprite, order.Color * colorMultiplier, tooltip: item.Name);
                optionNodes.Add(new OptionNode(optionElement, Keys.None));
                optionElements.Add(optionElement);
            }

            Rectangle clampArea = new Rectangle(10, 10, GameMain.GraphicsWidth - 20, GameMain.GraphicsHeight - 20);
            Rectangle disallowedArea = targetFrame.GetChild<GUIFrame>().Rect;
            Point originalSize = disallowedArea.Size;
            disallowedArea.Size = disallowedArea.MultiplySize(0.9f);
            disallowedArea.X += (originalSize.X - disallowedArea.Size.X) / 2;
            disallowedArea.Y += (originalSize.Y - disallowedArea.Size.Y) / 2;
            GUI.PreventElementOverlap(optionElements, new List<Rectangle>() { disallowedArea }, clampArea);
            nodeConnectors.RectTransform.Parent = targetFrame.RectTransform;
            nodeConnectors.RectTransform.SetAsFirstChild();

            var shadow = new GUIFrame(
                new RectTransform(targetFrame.Rect.Size + new Point((int)(200 * GUI.Scale)), targetFrame.RectTransform, anchor: Anchor.Center),
                style: "OuterGlow",
                color: matchingItems.Count > 1 ? Color.Black * 0.9f : Color.Black * 0.7f);
            shadow.SetAsFirstChild();
        }

        private void CreateOrderOptionNodes(Order order, Item targetItem)
        {
            if (itemContext != null)
            {
                targetItem = !order.UseController ? itemContext :
                    itemContext.GetConnectedComponents<Turret>().FirstOrDefault()?.Item ?? itemContext.GetConnectedComponents<Turret>(recursive: true).FirstOrDefault()?.Item;
            }
            var o = targetItem == null ? order : order.WithItemComponent(targetItem, order.GetTargetItemComponent(targetItem));
            var offsets = MathUtils.GetPointsOnCircumference(Vector2.Zero, nodeDistance,
                GetCircumferencePointCount(order.Options.Length),
                GetFirstNodeAngle(order.Options.Length));
            var offsetIndex = 0;
            for (int i = 0; i < order.Options.Length; i++)
            {
                optionNodes.Add(new OptionNode(
                    CreateOrderOptionNode(nodeSize, commandFrame.RectTransform, offsets[offsetIndex++].ToPoint(), o.WithOption(order.Options[i]), (i + 1) % 10),
                    Keys.D0 + (i + 1) % 10));
            }
        }

        private GUIButton CreateOrderOptionNode(Point size, RectTransform parent, Point offset, Order order, int hotkey)
        {
            var node = new GUIButton(new RectTransform(size, parent: parent, anchor: Anchor.Center), style: null)
            {
                UserData = order,
                OnClicked = (button, userData) =>
                {
                    if (!CanIssueOrders) { return false; }
                    var o = userData as Order;
                    if (o.MustManuallyAssign && characterContext == null)
                    {
                        CreateAssignmentNodes(button);
                    }
                    else
                    {
                        var character = characterContext ?? GetCharacterForQuickAssignment(o);
                        int priority = GetManualOrderPriority(character, o);
                        SetCharacterOrder(character, o.WithManualPriority(priority).WithOrderGiver(Character.Controlled));
                        DisableCommandUI();
                    }
                    return true;
                }
            };
            if (CanOpenManualAssignment(node))
            {
                node.OnSecondaryClicked = (button, _) => CreateAssignmentNodes(button);
            }
            node.RectTransform.MoveOverTime(offset, CommandNodeAnimDuration);

            GUIImage icon = null;
            if (order.Prefab.OptionSprites.TryGetValue(order.Option, out Sprite sprite))
            {
                var optionName = order.Prefab.GetOptionName(order.Option);
                var showAssignmentTooltip = characterContext == null && !order.MustManuallyAssign && !order.TargetAllCharacters;
                icon = CreateNodeIcon(Vector2.One, node.RectTransform, sprite, order.Color,
                    tooltip: characterContext != null ? optionName : optionName +
                        "\n" + PlayerInput.PrimaryMouseLabel + ": " + TextManager.Get("commandui.quickassigntooltip") +
                        "\n" + PlayerInput.SecondaryMouseLabel + ": " + TextManager.Get("commandui.manualassigntooltip"));
            }
            if (!CanCharacterBeHeard())
            {
                node.CanBeFocused = false;
                if (icon != null) { icon.CanBeFocused = false; }
                CreateBlockIcon(node.RectTransform, tooltip: TextManager.Get(characterContext == null ? "nocharactercanhear" : "thischaractercanthear"));
            }
            else if (hotkey >= 0)
            {
                CreateHotkeyIcon(node.RectTransform, hotkey);
            }
            return node;
        }

        private bool CreateAssignmentNodes(GUIComponent node)
        {
            if (centerNode == null)
            {
                DisableCommandUI();
                return false;
            }

            var order = node.UserData is MinimapNodeData minimapNodeData ? minimapNodeData.Order : node.UserData as Order;
            var characters = GetCharactersForManualAssignment(order);
            if (characters.None()) { return false; }

            if (!(optionNodes.Find(n => n.Button == node) is OptionNode optionNode) || !optionNodes.Remove(optionNode))
            {
                shortcutNodes.Remove(node);
            };
            RemoveOptionNodes();

            if (returnNode != null)
            {
                returnNode.Children.ForEach(child => child.Visible = false);
                returnNode.Visible = false;
                historyNodes.Push(returnNode);
            }
            SetReturnNode(centerNode, new Point(0, (int)(returnNodeDistanceModifier * nodeDistance)));

            if (targetFrame == null || !targetFrame.Visible)
            {
                SetCenterNode(node as GUIButton);
            }
            else
            {
                if (order.Option.IsEmpty)
                {
                    SetCenterNode(node as GUIButton, resetAnchor: true);
                }
                else
                {
                    var clickedOptionNode = new GUIButton(
                    new RectTransform(centerNodeSize, parent: commandFrame.RectTransform, anchor: Anchor.Center),
                    style: null)
                    {
                        UserData = node.UserData
                    };
                    if (order.Prefab.OptionSprites.TryGetValue(order.Option, out Sprite sprite))
                    {
                        CreateNodeIcon(Vector2.One, clickedOptionNode.RectTransform, sprite, order.Color, tooltip: order.GetOptionName(order.Option)); //TODO: revise tooltip
                    }
                    SetCenterNode(clickedOptionNode);
                    node = null;
                }
                HideMinimap();
            }
            if (shortcutCenterNode != null)
            {
                commandFrame.RemoveChild(shortcutCenterNode);
                shortcutCenterNode = null;
            }

            var characterCount = characters.Count;
            int hotkey = 1;
            Vector2[] offsets;
            var needToExpand = characterCount > 10;
            if (characterCount > 5)
            {
                // First ring
                var charactersOnFirstRing = needToExpand ? 5 : (int)Math.Floor(characterCount / 2f);
                offsets = GetAssignmentNodeOffsets(charactersOnFirstRing);
                for (int i = 0; i < charactersOnFirstRing; i++)
                {
                    CreateAssignmentNode(order, characters[i], offsets[i].ToPoint(), hotkey++ % 10);
                }
                // Second ring
                var charactersOnSecondRing = needToExpand ? 4 : characterCount - charactersOnFirstRing;
                offsets = GetAssignmentNodeOffsets(needToExpand ? 5 : charactersOnSecondRing, false);
                for (int i = 0; i < charactersOnSecondRing; i++)
                {
                    CreateAssignmentNode(order, characters[charactersOnFirstRing + i], offsets[i].ToPoint(), hotkey++ % 10);
                }
            }
            else
            {
                offsets = GetAssignmentNodeOffsets(characterCount);
                for (int i = 0; i < characterCount; i++)
                {
                    CreateAssignmentNode(order, characters[i], offsets[i].ToPoint(), hotkey++ % 10);
                }
            }

            if (!needToExpand)
            {
                hotkey = optionNodes.Count + 1;
                CreateHotkeyIcon(returnNode.RectTransform, hotkey % 10, true);
                returnNodeHotkey = Keys.D0 + hotkey % 10;
                expandNodeHotkey = Keys.None;
                return true;
            }

            extraOptionCharacters.Clear();
            // Sort expanded assignment nodes by characters' jobs and then by their names
            extraOptionCharacters.AddRange(characters.GetRange(hotkey - 1, characterCount - (hotkey - 1))
                .OrderBy(c => c?.Info?.Job?.Name).ThenBy(c => c?.Info?.DisplayName));

            expandNode = new GUIButton(
                new RectTransform(assignmentNodeSize, parent: commandFrame.RectTransform, anchor: Anchor.Center)
                {
                    AbsoluteOffset = offsets.Last().ToPoint()
                },
                style: null)
            {
                UserData = order,
                OnClicked = ExpandAssignmentNodes
            };
            CreateNodeIcon(expandNode.RectTransform, "CommandExpandNode", order.Color, tooltip: TextManager.Get("commandui.expand"));

            hotkey = optionNodes.Count + 1;
            CreateHotkeyIcon(expandNode.RectTransform, hotkey % 10);
            expandNodeHotkey = Keys.D0 + hotkey % 10;
            CreateHotkeyIcon(returnNode.RectTransform, ++hotkey % 10, true);
            returnNodeHotkey = Keys.D0 + hotkey % 10;
            return true;
        }

        private Vector2[] GetAssignmentNodeOffsets(int characters, bool firstRing = true)
        {
            var nodeDistance = 1.8f * this.nodeDistance;
            var nodePositionsOnEachSide = characters % 2 > 0 ? 7 : 6;
            var nodeCountForCalculation = 2 * nodePositionsOnEachSide + 2;
            var offsets = MathUtils.GetPointsOnCircumference(firstRing ? new Vector2(0f, 0.5f * nodeDistance) : Vector2.Zero,
                nodeDistance, nodeCountForCalculation, MathHelper.ToRadians(180f + 360f / nodeCountForCalculation));
            var emptySpacesPerSide = (nodePositionsOnEachSide - characters) / 2;
            var offsetsInUse = new Vector2[nodePositionsOnEachSide - 2 * emptySpacesPerSide];
            for (int i = 0; i < offsetsInUse.Length; i++)
            {
                offsetsInUse[i] = offsets[i + emptySpacesPerSide];
            }
            return offsetsInUse;
        }

        private bool ExpandAssignmentNodes(GUIButton node, object userData)
        {
            node.OnClicked = (button, _) =>
            {
                RemoveExtraOptionNodes();
                button.OnClicked = ExpandAssignmentNodes;
                return true;
            };

            var availableNodePositions = 20;
            var offsets = MathUtils.GetPointsOnCircumference(Vector2.Zero, 2.7f * this.nodeDistance, availableNodePositions,
                firstAngle: MathHelper.ToRadians(-90f - ((extraOptionCharacters.Count - 1) * 0.5f * (360f / availableNodePositions))));
            for (int i = 0; i < extraOptionCharacters.Count && i < availableNodePositions; i++)
            {
                CreateAssignmentNode(userData as Order, extraOptionCharacters[i], offsets[i].ToPoint(), -1, nameLabelScale: 1.15f);
            }
            return true;
        }

        private void CreateAssignmentNode(Order order, Character character, Point offset, int hotkey, float nameLabelScale = 1f)
        {
            // Button
            var node = new GUIButton(
                new RectTransform(assignmentNodeSize, parent: commandFrame.RectTransform, anchor: Anchor.Center),
                style: null)
            {
                UserData = character,
                OnClicked = (_, userData) =>
                {
                    if (!CanIssueOrders) { return false; }
                    var character = userData as Character;
                    int priority = GetManualOrderPriority(character, order);
                    SetCharacterOrder(character, order.WithManualPriority(priority).WithOrderGiver(Character.Controlled));
                    DisableCommandUI();
                    return true;
                }
            };
            node.RectTransform.MoveOverTime(offset, CommandNodeAnimDuration);

            var jobColor = character.Info?.Job?.Prefab?.UIColor ?? Color.White;

            // Order icon
            var topOrderInfo = character.GetCurrentOrderWithTopPriority();
            GUIImage orderIcon;
            if (topOrderInfo != null)
            {
                orderIcon = new GUIImage(new RectTransform(new Vector2(1.2f), node.RectTransform, anchor: Anchor.Center), topOrderInfo.SymbolSprite, scaleToFit: true);
                var tooltip = topOrderInfo.Name;
                if (topOrderInfo.Option != Identifier.Empty) { tooltip += " (" + topOrderInfo.GetOptionName(topOrderInfo.Option) + ")"; };
                orderIcon.ToolTip = tooltip;
            }
            else
            {
                orderIcon = new GUIImage(new RectTransform(new Vector2(1.2f), node.RectTransform, anchor: Anchor.Center), "CommandIdleNode", scaleToFit: true);
            }
            orderIcon.Color = jobColor * nodeColorMultiplier;
            orderIcon.HoverColor = jobColor;
            orderIcon.PressedColor = jobColor;
            orderIcon.SelectedColor = jobColor;
            orderIcon.UserData = "colorsource";

            // Name label
            var width = (int)(nameLabelScale * nodeSize.X);
            var font = GUIStyle.SmallFont;
            var nameLabel = new GUITextBlock(
                new RectTransform(new Point(width, 0), parent: node.RectTransform, anchor: Anchor.TopCenter, pivot: Pivot.BottomCenter)
                {
                    RelativeOffset = new Vector2(0f, -0.25f)
                },
                ToolBox.LimitString(character.Info?.DisplayName, font, width), textColor: jobColor * nodeColorMultiplier, font: font, textAlignment: Alignment.Center, style: null)
            {
                CanBeFocused = false,
                ForceUpperCase = ForceUpperCase.Yes,
                HoverTextColor = jobColor
            };

            if (character.Info?.Job?.Prefab?.IconSmall is Sprite smallJobIcon)
            {
                // Job icon
                new GUIImage(
                    new RectTransform(new Vector2(0.4f), node.RectTransform, anchor: Anchor.TopCenter, pivot: Pivot.Center)
                    {
                        RelativeOffset = new Vector2(0.0f, -((orderIcon.RectTransform.RelativeSize.Y - 1) / 2))
                    },
                    smallJobIcon, scaleToFit: true)
                {
                    CanBeFocused = false,
                    Color = jobColor,
                    HoverColor = jobColor
                };
            }

            bool canHear = character.CanHearCharacter(Character.Controlled);
#if DEBUG
            if (Character.Controlled == null) { canHear = true; }
#endif

            if (!canHear)
            {
                node.CanBeFocused = orderIcon.CanBeFocused = false;
                CreateBlockIcon(node.RectTransform, tooltip: TextManager.Get("thischaractercanthear"));
            }
            if (hotkey >= 0)
            {
                if (canHear) { CreateHotkeyIcon(node.RectTransform, hotkey); }
                optionNodes.Add(new OptionNode(node, canHear ? Keys.D0 + hotkey : Keys.None));
            }
            else
            {
                extraOptionNodes.Add(node);
            }
        }

        private GUIImage CreateNodeIcon(Vector2 relativeSize, RectTransform parent, Sprite sprite, Color color, LocalizedString tooltip = null)
        {
            // Icon
            return new GUIImage(
                new RectTransform(relativeSize, parent),
                sprite,
                scaleToFit: true)
            {
                Color = color * nodeColorMultiplier,
                HoverColor = color,
                PressedColor = color,
                SelectedColor = color,
                ToolTip = tooltip,
                UserData = "colorsource"
            };
        }

        /// <summary>
        /// Create node icon with a fixed absolute size
        /// </summary>
        private GUIImage CreateNodeIcon(Point absoluteSize, RectTransform parent, Sprite sprite, Color color, LocalizedString tooltip = null)
        {
            // Icon
            return new GUIImage(
                new RectTransform(absoluteSize, parent: parent) { IsFixedSize = true },
                sprite,
                scaleToFit: true)
            {
                Color = color * nodeColorMultiplier,
                HoverColor = color,
                PressedColor = color,
                SelectedColor = color,
                ToolTip = tooltip,
                UserData = "colorsource"
            };
        }

        private void CreateNodeIcon(RectTransform parent, string style, Color? color = null, LocalizedString tooltip = null)
        {
            // Icon
            var icon = new GUIImage(
                new RectTransform(Vector2.One, parent),
                style,
                scaleToFit: true)
            {
                ToolTip = tooltip,
                UserData = "colorsource"
            };
            if (color.HasValue)
            {
                icon.Color = color.Value * nodeColorMultiplier;
                icon.HoverColor = color.Value;
            }
            else
            {
                icon.Color = icon.HoverColor * nodeColorMultiplier;
            }
        }

        private void CreateHotkeyIcon(RectTransform parent, int hotkey, bool enlargeIcon = false)
        {
            var bg = new GUIImage(
                new RectTransform(new Vector2(enlargeIcon ? 0.4f : 0.25f), parent, anchor: Anchor.BottomCenter, pivot: Pivot.Center),
                "CommandHotkeyContainer",
                scaleToFit: true)
            {
                CanBeFocused = false,
                UserData = "hotkey"
            };
            new GUITextBlock(
                new RectTransform(Vector2.One, bg.RectTransform, anchor: Anchor.Center),
                hotkey.ToString(),
                textColor: Color.Black,
                textAlignment: Alignment.Center)
            {
                CanBeFocused = false
            };
        }

        private void CreateBlockIcon(RectTransform parent, LocalizedString tooltip = null)
        {
            var icon = new GUIImage(new RectTransform(new Vector2(0.9f), parent, anchor: Anchor.Center), cancelIcon, scaleToFit: true)
            {
                CanBeFocused = false,
                Color = GUIStyle.Red * nodeColorMultiplier,
                HoverColor = GUIStyle.Red
            };
            if (!tooltip.IsNullOrEmpty())
            {
                string color = XMLExtensions.ColorToString(GUIStyle.Red);
                tooltip = $"‖color:{color}‖{tooltip}‖color:end‖";
                icon.ToolTip = RichString.Rich(tooltip);
                icon.CanBeFocused = true;
            }
        }

        private int GetCircumferencePointCount(int nodes)
        {
            return nodes % 2 > 0 ? nodes : nodes + 1;
        }

        private float GetFirstNodeAngle(int nodeCount)
        {
            var bearing = 90.0f;
            if (returnNode != null)
            {
                bearing = GetBearing(
                    centerNode.RectTransform.AnimTargetPos.ToVector2(),
                    returnNode.RectTransform.AnimTargetPos.ToVector2());
            }
            else if (shortcutCenterNode != null)
            {
                bearing = GetBearing(
                    centerNode.RectTransform.AnimTargetPos.ToVector2(),
                    shorcutCenterNodeOffset.ToVector2());
            }
            return nodeCount % 2 > 0 ?
                MathHelper.ToRadians(bearing + 360.0f / nodeCount / 2) :
                MathHelper.ToRadians(bearing + 360.0f / (nodeCount + 1));
        }

        private float GetBearing(Vector2 startPoint, Vector2 endPoint, bool flipY = false, bool flipX = false)
        {
            var radians = Math.Atan2(
                !flipY ? endPoint.Y - startPoint.Y : startPoint.Y - endPoint.Y,
                !flipX ? endPoint.X - startPoint.X : startPoint.X - endPoint.X);
            var degrees = MathHelper.ToDegrees((float)radians);
            return (degrees < 0) ? (degrees + 360) : degrees;
        }

        private bool TryGetBreachedHullAtHoveredWall(out Hull breachedHull, out WallSection hoveredWall)
        {
            breachedHull = null;
            hoveredWall = null;
            // Based on the IsValidTarget() method of AIObjectiveFixLeaks class
            List<Gap> leaks = Gap.GapList.FindAll(g =>
                g != null && g.ConnectedWall != null && g.ConnectedDoor == null && g.Open > 0 && g.linkedTo.Any(l => l != null) &&
                g.Submarine != null && (Character.Controlled != null && g.Submarine.TeamID == Character.Controlled.TeamID && g.Submarine.Info.IsPlayer));
            if (leaks.None()) { return false; }
            Vector2 mouseWorldPosition = GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
            foreach (Gap leak in leaks)
            {
                if (Submarine.RectContains(leak.ConnectedWall.WorldRect, mouseWorldPosition))
                {
                    breachedHull = leak.FlowTargetHull;
                    foreach (var section in leak.ConnectedWall.Sections)
                    {
                        if (Submarine.RectContains(section.WorldRect, mouseWorldPosition))
                        {
                            hoveredWall = section;
                            break;
                        }
                        
                    }
                    return true;
                }
            }
            return false;
        }

        private Submarine GetTargetSubmarine()
        {
            var sub = Submarine.MainSub;
            if (Character.Controlled != null)
            {
                // Pick the second main sub when we have two teams (in combat mission)
                if (Character.Controlled.TeamID == CharacterTeamType.Team2 && Submarine.MainSubs.Length > 1)
                {
                    sub = Submarine.MainSubs[1];
                }
                // Target current submarine (likely a shuttle) when undocked from the main sub
                if (Character.Controlled.Submarine is Submarine currentSub && currentSub != sub && currentSub.TeamID == Character.Controlled.TeamID && !currentSub.IsConnectedTo(sub))
                {
                    sub = currentSub;
                }
            }
            return sub;
        }

        private void SetCharacterTooltip(GUIComponent component, Character character)
        {
            if (component == null) { return; }
            LocalizedString tooltip = character?.Info != null ? characterContext.Info.DisplayName : null;
            if (tooltip.IsNullOrWhiteSpace()) { component.ToolTip = tooltip; return; }
            if (character.Info?.Job != null && !characterContext.Info.Job.Name.IsNullOrWhiteSpace()) { tooltip += " (" + characterContext.Info.Job.Name + ")"; }
            component.ToolTip = tooltip;
        }

        private LocalizedString GetOrderNameBasedOnContextuality(Order order)
        {
            if (order == null) { return ""; }
            if (isContextual) { return order.ContextualName; }
            return order.Name;
        }

        private int GetManualOrderPriority(Character character, Order order)
        {
            return character?.Info?.GetManualOrderPriority(order) ?? CharacterInfo.HighestManualOrderPriority;
        }

        private bool IsOrderAvailable(Order order)
            => IsOrderAvailable(order.Prefab);
        
        private bool IsOrderAvailable(OrderPrefab order)
        {
            if (order == null) { return false; }
            switch (order.Identifier.Value.ToLowerInvariant())
            {
                case "assaultenemy":
                    Character character = characterContext ?? Character.Controlled;
                    if (character?.Submarine == null) { return false; }
                    return character.Submarine.GetConnectedSubs().Any(s => s.TeamID != character.TeamID);
                default:
                    return true;
            }
        }

        #region Crew Member Assignment Logic
        private bool CanOpenManualAssignmentMinimapOrder(GUIComponent node)
        {
            if (node == null || characterContext != null) { return false; }
            if (node.UserData is MinimapNodeData {Order: { } minimapOrder})
            {
                return !minimapOrder.TargetAllCharacters && (!minimapOrder.Prefab.HasOptions || !minimapOrder.Option.IsEmpty);
            }
            if (node.UserData is Order nodeOrder)
            {
                return !nodeOrder.TargetAllCharacters && !nodeOrder.Prefab.HasOptions &&
                       (!nodeOrder.MustSetTarget || itemContext != null ||
                        nodeOrder.GetMatchingItems(GetTargetSubmarine(), true, interactableFor: Character.Controlled).Count < 2);
            }
            return false;
        }

        private bool CanOpenManualAssignment(GUIComponent node)
        {
            if (node == null || characterContext != null) { return false; }
            if (node.UserData is Order nodeOrder)
            {
                return !nodeOrder.TargetAllCharacters &&
                    (!nodeOrder.Prefab.HasOptions || !nodeOrder.Option.IsEmpty) &&
                    (!nodeOrder.MustSetTarget || itemContext != null || nodeOrder.GetMatchingItems(GetTargetSubmarine(), true, interactableFor: Character.Controlled).Count < 2);
            }
            return false;
        }

        private Character GetCharacterForQuickAssignment(Order order)
        {
            return GetCharacterForQuickAssignment(order, Character.Controlled, characters);
        }

        private List<Character> GetCharactersForManualAssignment(Order order)
        {
#if !DEBUG
            if (Character.Controlled == null) { return new List<Character>(); }
#endif
            if (order.Identifier == dismissedOrderPrefab.Identifier)
            {
                return characters.Union(GetOrderableFriendlyNPCs()).Where(c => !c.IsDismissed).OrderBy(c => c.Info.DisplayName).ToList();
            }
            return GetCharactersSortedForOrder(order, characters, Character.Controlled, order.Identifier != "follow", extraCharacters: GetOrderableFriendlyNPCs()).ToList();
        }

        private IEnumerable<Character> GetOrderableFriendlyNPCs()
        {
            // TODO: change this so that we can get the data without having to rely on ui elements.
            return crewList.Content.Children.Where(c => c.UserData is Character character && character.TeamID == CharacterTeamType.FriendlyNPC).Select(c => (Character)c.UserData);
        }

        #endregion

        #endregion

        #region Reports

        /// <summary>
        /// Enables/disables report buttons when needed
        /// </summary>
        public void UpdateReports()
        {
            bool canIssueOrders = false;
            if (Character.Controlled?.CurrentHull?.Submarine != null && Character.Controlled.SpeechImpediment < 100.0f)
            {
                canIssueOrders = 
                    ChatMessage.CanUseRadio(Character.Controlled) &&
                    Character.Controlled?.CurrentHull?.Submarine?.TeamID == Character.Controlled.TeamID &&
                    !Character.Controlled.CurrentHull.Submarine.Info.IsWreck;
            }

            if (canIssueOrders)
            {
                ReportButtonFrame.Visible = !Character.Controlled.ShouldLockHud();
                if (!ReportButtonFrame.Visible) { return; }

                var reportButtonParent = ChatBox ?? GameMain.Client?.ChatBox;
                if (reportButtonParent == null) { return; }

                ReportButtonFrame.RectTransform.AbsoluteOffset = new Point(reportButtonParent.GUIFrame.Rect.Right + (int)(10 * GUI.Scale), reportButtonParent.GUIFrame.Rect.Y);

                bool hasFires = Character.Controlled.CurrentHull.FireSources.Count > 0;
                ToggleReportButton("reportfire", hasFires);

                bool hasLeaks = Character.Controlled.CurrentHull.ConnectedGaps.Any(g => !g.IsRoomToRoom && g.Open > 0.0f);
                ToggleReportButton("reportbreach", hasLeaks);

                bool hasIntruders = Character.CharacterList.Any(c => c.CurrentHull == Character.Controlled.CurrentHull && AIObjectiveFightIntruders.IsValidTarget(c, Character.Controlled, false));
                ToggleReportButton("reportintruders", hasIntruders);

                foreach (GUIComponent reportButton in ReportButtonFrame.Children)
                {
                    var highlight = reportButton.GetChildByUserData("highlighted");
                    if (highlight.Visible)
                    {
                        highlight.RectTransform.LocalScale = new Vector2(1.25f + (float)Math.Sin(Timing.TotalTime * 5.0f) * 0.25f);
                    }
                }
            }
            else
            {
                ReportButtonFrame.Visible = false;
            }
        }

        private void ToggleReportButton(string orderIdentifier, bool enabled)
        {
            ToggleReportButton(orderIdentifier.ToIdentifier(), enabled);
        }

        private void ToggleReportButton(Identifier orderIdentifier, bool enabled)
        {
            var reportButton = ReportButtonFrame.FindChild(c => c.UserData is OrderPrefab orderPrefab && orderPrefab.Identifier == orderIdentifier);
            if (reportButton != null)
            {
                reportButton.GetChildByUserData("highlighted").Visible = enabled;
            }
        }

        #endregion

        public void InitSinglePlayerRound()
        {
            crewList.ClearChildren();
            InitRound();
        }

        public void Reset()
        {
            characters.Clear();
            characterInfos.Clear();
            crewList.ClearChildren();
        }

        public XElement Save(XElement parentElement)
        {
            var element = new XElement("crew");
            for (int i = 0; i < characterInfos.Count; i++)
            {
                var ci = characterInfos[i];
                var infoElement = ci.Save(element);
                if (ci.InventoryData != null) { infoElement.Add(ci.InventoryData); }
                if (ci.HealthData != null) { infoElement.Add(ci.HealthData); }
                if (ci.OrderData != null) { infoElement.Add(ci.OrderData); }
                infoElement.Add(new XAttribute("crewlistindex", ci.CrewListIndex));
                if (ci.LastControlled) { infoElement.Add(new XAttribute("lastcontrolled", true)); }
            }
            parentElement?.Add(element);
            return element;
        }

        public static void ClientReadActiveOrders(IReadMessage inc)
        {
            ushort count = inc.ReadUInt16();
            if (count < 1) { return; }
            var activeOrders = new List<(Order, float?)>();
            for (ushort i = 0; i < count; i++)
            {
                var orderMessageInfo = OrderChatMessage.ReadOrder(inc);
                Character orderGiver = null;
                if (inc.ReadBoolean())
                {
                    ushort orderGiverId = inc.ReadUInt16();
                    orderGiver = orderGiverId != Entity.NullEntityID ? Entity.FindEntityByID(orderGiverId) as Character : null;
                }
                if (orderMessageInfo.OrderIdentifier == Identifier.Empty)
                {
                    DebugConsole.ThrowError("Invalid active order - order identifier empty.");
                    continue;
                }
                OrderPrefab orderPrefab = orderMessageInfo.OrderPrefab ?? OrderPrefab.Prefabs[orderMessageInfo.OrderIdentifier];
                Order order = orderMessageInfo.TargetType switch
                {   
                    Order.OrderTargetType.Entity => 
                        new Order(orderPrefab, orderMessageInfo.TargetEntity, orderPrefab.GetTargetItemComponent(orderMessageInfo.TargetEntity as Item), orderGiver: orderGiver),
                    Order.OrderTargetType.Position =>
                        new Order(orderPrefab, orderMessageInfo.TargetPosition, orderGiver: orderGiver),
                    Order.OrderTargetType.WallSection =>
                        new Order(orderPrefab, orderMessageInfo.TargetEntity as Structure, orderMessageInfo.WallSectionIndex, orderGiver: orderGiver),
                    _ => throw new NotImplementedException()
                };
                if (order != null && order.TargetAllCharacters)
                {
                    var fadeOutTime = !orderPrefab.IsIgnoreOrder ? (float?)orderPrefab.FadeOutTime : null;
                    activeOrders.Add((order, fadeOutTime));
                }
            }
            foreach (var (order, fadeOutTime) in activeOrders)
            {
                if (order.IsIgnoreOrder)
                {
                    switch (order.TargetType)
                    {
                        case Order.OrderTargetType.Entity:
                            if (!(order.TargetEntity is IIgnorable ignorableEntity)) { break; }
                            ignorableEntity.OrderedToBeIgnored = order.Identifier == "ignorethis";
                            break;
                        case Order.OrderTargetType.Position:
                            throw new NotImplementedException();
                        case Order.OrderTargetType.WallSection:
                            if (!order.WallSectionIndex.HasValue) { break; }
                            if (!(order.TargetEntity is Structure s)) { break; }
                            if (!(s.GetSection(order.WallSectionIndex.Value) is IIgnorable ignorableWall)) { break; }
                            ignorableWall.OrderedToBeIgnored = order.Identifier == "ignorethis";
                            break;
                    }
                }
                GameMain.GameSession?.CrewManager?.AddOrder(order, fadeOutTime);
            }
        }
    }
}
