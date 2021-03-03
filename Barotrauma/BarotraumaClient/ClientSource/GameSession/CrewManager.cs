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
using Barotrauma.Steam;

namespace Barotrauma
{
    partial class CrewManager
    {
        private Point screenResolution;

        #region UI

        public GUIComponent ReportButtonFrame { get; set; }

        private GUIFrame guiFrame;
        private GUIComponent crewAreaWithButtons;
        private GUIFrame crewArea;
        private GUIListBox crewList;
        private GUIButton commandButton, toggleCrewButton;
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
                _isCrewMenuOpen = GameMain.Config.CrewMenuOpen = value;
            }
        }

        public bool AutoShowCrewList() => _isCrewMenuOpen = true;

        public void AutoHideCrewList() => _isCrewMenuOpen = false;

        public void ResetCrewList() => _isCrewMenuOpen = GameMain.Config.CrewMenuOpen;

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
            guiFrame = new GUIFrame(new RectTransform(Vector2.One, GUICanvas.Instance), null, Color.Transparent)
            {
                CanBeFocused = false
            };

            #region Crew Area

            crewAreaWithButtons = new GUIFrame(
                HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.CrewArea, guiFrame.RectTransform),
                style: null,
                color: Color.Transparent)
            {
                CanBeFocused = false
            };

            var commandButtonHeight = (int)(GUI.Scale * 40);
            var buttonSize = new Point((int)(182f / 99f * commandButtonHeight), commandButtonHeight);
            var crewListToggleButtonHeight = (int)(64f * buttonSize.X / 175f);

            crewArea = new GUIFrame(
                new RectTransform(
                    new Point(crewAreaWithButtons.Rect.Width, crewAreaWithButtons.Rect.Height - commandButtonHeight - crewListToggleButtonHeight - 2 * HUDLayoutSettings.Padding),
                    crewAreaWithButtons.RectTransform,
                    Anchor.BottomLeft),
                style: null,
                color: Color.Transparent)
            {
                CanBeFocused = false
            };

            commandButton = new GUIButton(
                new RectTransform(buttonSize, parent: crewAreaWithButtons.RectTransform),
                style: "CommandButton")
            {
                // TODO: Update keybind if it's changed
                ToolTip = TextManager.Get("inputtype.command") + " (" + GameMain.Config.KeyBindText(InputType.Command) + ")",
                OnClicked = (button, userData) =>
                {
                    ToggleCommandUI();
                    return true;
                }
            };

            // AbsoluteOffset is set in UpdateProjectSpecific based on crewListOpenState
            crewList = new GUIListBox(
                new RectTransform(
                    Vector2.One,
                    crewArea.RectTransform),
                style: null,
                isScrollBarOnDefaultSide: false)
            {
                AutoHideScrollBar = false,
                CanBeFocused = false,
                OnSelected = (component, userData) => false,
                SelectMultiple = false,
                Spacing = (int)(GUI.Scale * 10)
            };

            buttonSize.Y = crewListToggleButtonHeight;
            toggleCrewButton = new GUIButton(
                new RectTransform(buttonSize, parent: crewAreaWithButtons.RectTransform)
                {
                    AbsoluteOffset = new Point(0, commandButtonHeight + HUDLayoutSettings.Padding)
                },
                style: "CrewListToggleButton")
            {
                OnClicked = (GUIButton btn, object userdata) =>
                {
                    IsCrewMenuOpen = !IsCrewMenuOpen;
                    return true;
                }
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
                            AddSinglePlayerChatMessage(
                                Character.Controlled.Info.Name,
                                msg,
                                ((msgCommand == "r" || msgCommand == "radio") && ChatMessage.CanUseRadio(Character.Controlled)) ? ChatMessageType.Radio : ChatMessageType.Default,
                                Character.Controlled);
                            var headset = GetHeadset(Character.Controlled, true);
                            if (headset != null && headset.CanTransmit())
                            {
                                headset.TransmitSignal(stepsTaken: 0, signal: msg, source: headset.Item, sender: Character.Controlled, sentFromChat: true);
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
                    ClampMouseRectToParent = false
                };
                chatBox.ToggleButton.RectTransform.AbsoluteOffset = new Point(0, HUDLayoutSettings.ChatBoxArea.Height - chatBox.ToggleButton.Rect.Height);
                chatBox.ToggleButton.OnClicked += (GUIButton btn, object userdata) =>
                {
                    chatBox.ToggleOpen = !chatBox.ToggleOpen;
                    chatBox.CloseAfterMessageSent = false;
                    return true;
                };
            }

            var reports = Order.PrefabList.FindAll(o => o.IsReport && o.SymbolSprite != null && !o.Hidden);
            if (reports.None())
            {
                DebugConsole.ThrowError("No valid orders for report buttons found! Cannot create report buttons. The orders for the report buttons must have 'targetallcharacters' attribute enabled and a valid 'symbolsprite' defined.");
                return;
            }

            ReportButtonFrame = new GUILayoutGroup(new RectTransform(
                new Point((HUDLayoutSettings.ChatBoxArea.Height - chatBox.ToggleButton.Rect.Height - (int)((reports.Count - 1) * 5 * GUI.Scale)) / reports.Count, HUDLayoutSettings.ChatBoxArea.Height - chatBox.ToggleButton.Rect.Height), guiFrame.RectTransform))
            {
                AbsoluteSpacing = (int)(5 * GUI.Scale),
                UserData = "reportbuttons",
                CanBeFocused = false
            };

            ReportButtonFrame.RectTransform.AbsoluteOffset = new Point(0, -chatBox.ToggleButton.Rect.Height);

            //report buttons
            foreach (Order order in reports)
            {
                if (!order.IsReport || order.SymbolSprite == null || order.Hidden) { continue; }
                var btn = new GUIButton(new RectTransform(new Point(ReportButtonFrame.Rect.Width), ReportButtonFrame.RectTransform), style: null)
                {
                    OnClicked = (GUIButton button, object userData) =>
                    {
                        if (!CanIssueOrders) { return false; }
                        var sub = Character.Controlled.Submarine;
                        if (sub == null || sub.TeamID != Character.Controlled.TeamID || sub.Info.IsWreck) { return false; }
                        SetCharacterOrder(null, order, null, CharacterInfo.HighestManualOrderPriority, Character.Controlled);
                        if (IsSinglePlayer) { HumanAIController.ReportProblem(Character.Controlled, order); }
                        return true;
                    },
                    UserData = order,
                    ToolTip = order.Name,
                    ClampMouseRectToParent = false
                };

                new GUIFrame(new RectTransform(new Vector2(1.5f), btn.RectTransform, Anchor.Center), "OuterGlowCircular")
                {
                    Color = GUI.Style.Red * 0.8f,
                    HoverColor = GUI.Style.Red * 1.0f,
                    PressedColor = GUI.Style.Red * 0.6f,
                    UserData = "highlighted",
                    CanBeFocused = false,
                    Visible = false
                };

                var img = new GUIImage(new RectTransform(Vector2.One, btn.RectTransform), order.Prefab.SymbolSprite, scaleToFit: true)
                {
                    Color = order.Color,
                    HoverColor = Color.Lerp(order.Color, Color.White, 0.5f),
                    ToolTip = order.Name,
                    SpriteEffects = SpriteEffects.FlipHorizontally
                };
            }

            #endregion

            screenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            prevUIScale = GUI.Scale;
            _isCrewMenuOpen = GameMain.Config.CrewMenuOpen;
            dismissedOrderPrefab ??= Order.GetPrefab("dismissed");
        }

        #endregion

        #region Character list management

        public Rectangle GetActiveCrewArea()
        {
            return crewArea.Rect;
        }

        public IEnumerable<Character> GetCharacters()
        {
            return characters;
        }

        public IEnumerable<CharacterInfo> GetCharacterInfos()
        {
            return characterInfos;
        }

        /// <summary>
        /// Remove the character from the crew (and crew menus).
        /// </summary>
        /// <param name="character">The character to remove</param>
        /// <param name="removeInfo">If the character info is also removed, the character will not be visible in the round summary.</param>
        public void RemoveCharacter(Character character, bool removeInfo = false)
        {
            if (character == null)
            {
                DebugConsole.ThrowError("Tried to remove a null character from CrewManager.\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }
            characters.Remove(character);
            if (removeInfo) { characterInfos.Remove(character.Info); }
        }

        private void AddCharacterToCrewList(Character character)
        {
            if (character == null) { return; }

            var background = new GUIFrame(
                new RectTransform(crewListEntrySize, parent: crewList.Content.RectTransform, anchor: Anchor.TopRight),
                style: "CrewListBackground")
            {
                UserData = character,
                OnSecondaryClicked = (comp, data) =>
                {
                    if (data == null) { return false; }
                    
                    var client = GameMain.NetworkMember?.ConnectedClients?.Find(c => c.Character == data);
                    if (client != null)
                    {
                        CreateModerationContextMenu(PlayerInput.MousePosition.ToPoint(), client);
                        return true;
                    }
                    return false;
                }
            };

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
            new GUIFrame(new RectTransform(new Vector2(paddingRelativeWidth, 1.0f), layoutGroup.RectTransform), style: null);

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

            var nameRelativeWidth = 1.0f
                // Start padding
                - paddingRelativeWidth
                // 5 icons (job, 3 orders, sound)
                - (5 * 0.8f * iconRelativeWidth)
                // Vertical line
                - (0.1f * iconRelativeWidth)
                // Spacing
                - (7 * layoutGroup.RelativeSpacing);

            var font = layoutGroup.Rect.Width < 150 ? GUI.SmallFont : GUI.Font;
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
                CanBeFocused = false
            };
            nameBlock.Text = ToolBox.LimitString(character.Name, font, (int)nameBlock.Rect.Width);

            var nameActualRealtiveWidth = Math.Min(nameRelativeWidth * background.Rect.Width, 150) / background.Rect.Width;
            var characterButton = new GUIButton(
                new RectTransform(
                    new Vector2(paddingRelativeWidth + 0.8f * iconRelativeWidth + nameActualRealtiveWidth + 2 * layoutGroup.RelativeSpacing, 1.0f),
                    background.RectTransform),
                style: null)
            {
                UserData = character
            };

            // Only create a tooltip if the name doesn't fit the name block
            if (nameBlock.Text.EndsWith("..."))
            {
                var characterTooltip = character.Name;
                if (character.Info?.Job?.Name != null) { characterTooltip += " (" + character.Info.Job.Name + ")"; };
                characterButton.ToolTip = characterTooltip;
                if (character.Info?.Job?.Prefab != null)
                {
                    characterButton.TooltipRichTextData = new List<RichTextData>() { new RichTextData()
                    {
                        Color = character.Info.Job.Prefab.UIColor,
                        EndIndex = characterTooltip.Length - 1
                    }};
                }
            }

            if (IsSinglePlayer)
            {
                characterButton.OnClicked = CharacterClicked;
            }
            else
            {
                characterButton.CanBeFocused = false;
                characterButton.CanBeSelected = false;
            }

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
                CanDragElements = true,
                HideChildrenOutsideFrame = false,
                KeepSpaceForScrollBar = false,
                OnRearranged = OnOrdersRearranged,
                ScrollBarVisible = false,
                Spacing = 2,
                UserData = character
            };
            currentOrderList.RectTransform.IsFixedSize = true;

            // Previous orders
            new GUILayoutGroup(new RectTransform(Vector2.One, parent: orderGroup.RectTransform), isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                CanBeFocused = false,
                Stretch = false
            };

            var soundIcons = new GUIFrame(new RectTransform(new Vector2(0.8f * iconRelativeWidth, 0.8f), layoutGroup.RectTransform), style: null)
            {
                CanBeFocused = false,
                UserData = "soundicons"
            };
            new GUIImage(
                new RectTransform(Vector2.One, soundIcons.RectTransform),
                GUI.Style.GetComponentStyle("GUISoundIcon").GetDefaultSprite(),
                scaleToFit: true)
            {
                CanBeFocused = false,
                UserData = new Pair<string, float>("soundicon", 0.0f),
                Visible = true
            };
            new GUIImage(
                new RectTransform(Vector2.One, soundIcons.RectTransform),
                "GUISoundIconDisabled",
                scaleToFit: true)
            {
                CanBeFocused = true,
                UserData = "soundicondisabled",
                Visible = false
            };

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
        }

        /// <summary>
        /// Sets which character is selected in the crew UI (highlight effect etc)
        /// </summary>
        public bool CharacterClicked(GUIComponent component, object selection)
        {
            if (!AllowCharacterSwitch) { return false; }
            Character character = selection as Character;
            if (character == null || character.IsDead || character.IsUnconscious) { return false; }
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

        public void KillCharacter(Character killedCharacter)
        {
            if (crewList.Content.GetChildByUserData(killedCharacter) is GUIComponent characterComponent)
            {
                CoroutineManager.StartCoroutine(KillCharacterAnim(characterComponent));
            }
            RemoveCharacter(killedCharacter);
        }

        private IEnumerable<object> KillCharacterAnim(GUIComponent component)
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

        #endregion

        #region Dialog

        /// <summary>
        /// Adds the message to the single player chatbox.
        /// </summary>
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

        private WifiComponent GetHeadset(Character character, bool requireEquipped)
        {
            if (character?.Inventory == null) { return null; }

            var radioItem = character.Inventory.AllItems.FirstOrDefault(it => it.GetComponent<WifiComponent>() != null);
            if (radioItem == null) { return null; }
            if (requireEquipped && !character.HasEquippedItem(radioItem)) { return null; }

            return radioItem.GetComponent<WifiComponent>();
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
                c.SpeechImpediment <= 100.0f);
            pendingConversationLines.AddRange(NPCConversation.CreateRandom(availableSpeakers));
        }

        #endregion

        #region Voice chat

        public void SetPlayerVoiceIconState(Client client, bool muted, bool mutedLocally)
        {
            if (client?.Character == null) { return; }

            if (crewList.Content.GetChildByUserData(client.Character)?
                    .FindChild(c => c is GUILayoutGroup)?
                    .GetChildByUserData("soundicons") is GUIComponent soundIcons)
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
            if (client?.Character != null) { SetCharacterSpeaking(client.Character); }
        }

        public void SetCharacterSpeaking(Character character)
        {
            if (crewList.Content.GetChildByUserData(character)?
                    .FindChild(c => c is GUILayoutGroup)?
                    .GetChildByUserData("soundicons")?
                    .FindChild(c => c.UserData is Pair<string, float> pair && pair.First == "soundicon") is GUIComponent soundIcon)
            {
                soundIcon.Color = Color.White;
                Pair<string, float> userdata = soundIcon.UserData as Pair<string, float>;
                userdata.Second = 1.0f;
            }
        }

        #endregion

        #region Crew List Order Displayment

        /// <summary>
        /// Sets the character's current order (if it's close enough to receive messages from orderGiver) and
        /// displays the order in the crew UI
        /// </summary>
        public void SetCharacterOrder(Character character, Order order, string option, int priority, Character orderGiver)
        {
            if (order != null && order.TargetAllCharacters)
            {
                Hull hull = null;
                if (order.IsReport)
                {
                    if (orderGiver?.CurrentHull == null) { return; }
                    hull = orderGiver.CurrentHull;
                    AddOrder(new Order(order.Prefab ?? order, hull, null, orderGiver), order.FadeOutTime);
                }
                else if(order.IsIgnoreOrder)
                {
                    WallSection ws = null;
                    if (order.TargetType == Order.OrderTargetType.Entity && order.TargetEntity is IIgnorable ignorable)
                    {
                        ignorable.OrderedToBeIgnored = order.Identifier == "ignorethis";
                        AddOrder(new Order(order.Prefab ?? order, order.TargetEntity, order.TargetItemComponent, orderGiver), null);
                    }
                    else if (order.TargetType == Order.OrderTargetType.WallSection && order.TargetEntity is Structure s)
                    {
                        var wallSectionIndex = order.WallSectionIndex ?? s.Sections.IndexOf(wallContext);
                        ws = s.GetSection(wallSectionIndex);
                        if (ws != null)
                        {
                            ws.OrderedToBeIgnored = order.Identifier == "ignorethis";
                            AddOrder(new Order(order.Prefab ?? order, s, wallSectionIndex, orderGiver), null);
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
                    orderGiver.Speak(order.GetChatMessage("", hull?.DisplayName, givingOrderToSelf: character == orderGiver), ChatMessageType.Order);
                }
                else
                {
                    OrderChatMessage msg = new OrderChatMessage(order, "", priority, order.IsReport ? hull : order.TargetEntity, null, orderGiver);
                    GameMain.Client?.SendChatMessage(msg);
                }
            }
            else
            {
                //can't issue an order if no characters are available
                if (character == null) { return; }

                if (IsSinglePlayer)
                {
                    character.SetOrder(order, option, priority, orderGiver, speak: orderGiver != character);
                    orderGiver?.Speak(order?.GetChatMessage(character.Name, orderGiver.CurrentHull?.DisplayName, givingOrderToSelf: character == orderGiver, orderOption: option));
                }
                else if (orderGiver != null)
                {
                    OrderChatMessage msg = new OrderChatMessage(order, option, priority, order?.TargetSpatialEntity ?? order?.TargetItemComponent?.Item as ISpatialEntity, character, orderGiver);
                    GameMain.Client?.SendChatMessage(msg);
                }
            }
        }

        /// <summary>
        /// Displays the specified order in the crew UI next to the character.
        /// </summary>
        public void AddCurrentOrderIcon(Character character, Order order, string option, int priority)
        {
            if (character == null) { return; }

            var characterComponent = crewList.Content.GetChildByUserData(character);

            if (characterComponent == null) { return; }

            var currentOrderIconList = GetCurrentOrderIconList(characterComponent);
            var currentOrderIcons = currentOrderIconList.Content.Children;
            var iconsToRemove = new List<GUIComponent>();
            var newPreviousOrders = new List<OrderInfo>();
            bool updatedExistingIcon = false;

            foreach (var icon in currentOrderIcons)
            {
                var orderInfo = (OrderInfo)icon.UserData;
                var matchingOrder = character.GetCurrentOrder(orderInfo.Order, orderInfo.OrderOption);
                if (!matchingOrder.HasValue)
                {
                    iconsToRemove.Add(icon);
                    newPreviousOrders.Add(orderInfo);
                }
                else if (orderInfo.MatchesOrder(order, option))
                {
                    icon.UserData = new OrderInfo(order, option, priority);
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
                var orderInfo = (OrderInfo)icon.UserData;
                if (orderInfo.MatchesOrder(order, option))
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
                currentOrderIconList.CanDragElements = currentOrderIconList.Content.CountChildren > 1;
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
            var nodeIcon = CreateNodeIcon(size, currentOrderIconList.Content.RectTransform, GetOrderIconSprite(order, option), order.Color, tooltip: CreateOrderTooltip(order, option));
            nodeIcon.UserData = new OrderInfo(order, option, priority);
            nodeIcon.OnSecondaryClicked = (image, userData) =>
            {
                if (!CanIssueOrders) { return false; }
                var orderInfo = (OrderInfo)userData;
                SetCharacterOrder(character, dismissedOrderPrefab, Order.GetDismissOrderOption(orderInfo),
                    character.GetCurrentOrder(orderInfo.Order, orderInfo.OrderOption)?.ManualPriority ?? 0,
                    Character.Controlled);
                return true;
            };

            new GUIFrame(new RectTransform(new Point((int)(1.5f * nodeWidth)), parent: nodeIcon.RectTransform, Anchor.Center), "OuterGlowCircular")
            {
                CanBeFocused = false,
                Color = order.Color,
                UserData = "glow",
                Visible = false
            };

            int hierarchyIndex = GetOrderIconHierarchyIndex(priority);
            if (hierarchyIndex != currentOrderIconList.Content.GetChildIndex(nodeIcon))
            {
                nodeIcon.RectTransform.RepositionChildInHierarchy(hierarchyIndex);
            }

            currentOrderIconList.CanDragElements = currentOrderIconList.Content.CountChildren > 1;
            RearrangeIcons();

            void RearrangeIcons()
            {
                if (character.CurrentOrders != null)
                {
                    // Make sure priority values are up-to-date
                    foreach (var currentOrderInfo in character.CurrentOrders)
                    {
                        var component = currentOrderIconList.Content.FindChild(c => c?.UserData is OrderInfo componentOrderInfo &&
                            componentOrderInfo.MatchesOrder(currentOrderInfo));
                        if (component == null) { continue; }
                        var componentOrderInfo = (OrderInfo)component.UserData;
                        int newPriority = currentOrderInfo.ManualPriority;
                        if (componentOrderInfo.ManualPriority != newPriority)
                        {
                            component.UserData = new OrderInfo(componentOrderInfo, newPriority);
                        }
                    }

                    currentOrderIconList.Content.RectTransform.SortChildren((x, y) =>
                    {
                        var xOrder = (OrderInfo)x.GUIComponent.UserData;
                        var yOrder = (OrderInfo)y.GUIComponent.UserData;
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

            static int GetOrderIconHierarchyIndex(int priority)
            {
                return CharacterInfo.HighestManualOrderPriority - priority;
            }
        }

        public void AddCurrentOrderIcon(Character character, OrderInfo? orderInfo)
        {
            AddCurrentOrderIcon(character, orderInfo?.Order, orderInfo?.OrderOption, orderInfo?.ManualPriority ?? 0);
        }

        private void AddPreviousOrderIcon(Character character, GUIComponent characterComponent, OrderInfo orderInfo)
        {
            if (orderInfo.Order == null || orderInfo.Order.Identifier == dismissedOrderPrefab.Identifier) { return; }

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
            var previousOrderInfo = new OrderInfo(orderInfo, OrderInfo.OrderType.Previous);
            var prevOrderFrame = new GUIButton(new RectTransform(size, parent: previousOrderIconGroup.RectTransform), style: null)
            {
                UserData = previousOrderInfo,
                OnClicked = (button, userData) =>
                {
                    if (!CanIssueOrders) { return false; }
                    var orderInfo = (OrderInfo)userData;
                    SetCharacterOrder(character, orderInfo.Order, orderInfo.OrderOption, CharacterInfo.HighestManualOrderPriority, Character.Controlled);
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
                previousOrderInfo.Order.Color,
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
                    if (icon.UserData is OrderInfo orderInfo)
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
            var orderInfo = (OrderInfo)userData;
            var priority = Math.Max(CharacterInfo.HighestManualOrderPriority - orderList.Content.GetChildIndex(orderComponent), 1);
            if (orderInfo.ManualPriority == priority) { return; }
            var character = (Character)orderList.UserData;
            SetCharacterOrder(character, orderInfo.Order, orderInfo.OrderOption, priority, Character.Controlled);
        }

        private string CreateOrderTooltip(Order order, string option)
        {
            if (order == null) { return ""; }
            if (!string.IsNullOrEmpty(option))
            {
                return TextManager.GetWithVariables("crewlistordericontooltip",
                    new string[2] { "[ordername]", "[orderoption]" },
                    new string[2] { order.Name, order.GetOptionName(option) });
            }
            else if (order.TargetEntity is Item targetItem && order.MinimapIcons.ContainsKey(targetItem.Prefab.Identifier))
            {
                return TextManager.GetWithVariables("crewlistordericontooltip",
                    new string[2] { "[ordername]", "[orderoption]" },
                    new string[2] { order.Name, targetItem.Name });
            }
            else
            {
                return order.Name;
            }
        }

        private string CreateOrderTooltip(OrderInfo orderInfo) =>
            CreateOrderTooltip(orderInfo.Order, orderInfo.OrderOption);

        private Sprite GetOrderIconSprite(Order order, string option)
        {
            if (order == null) { return null; }
            Sprite sprite = null;
            if (option != null && order.Prefab.OptionSprites.Any())
            {
                order.Prefab.OptionSprites.TryGetValue(option, out sprite);
            }
            if (sprite == null && order.TargetEntity is Item targetItem && order.MinimapIcons.Any())
            {
                order.MinimapIcons.TryGetValue(targetItem.Prefab.Identifier, out sprite);
            }
            return sprite ?? order.SymbolSprite;
        }

        private Sprite GetOrderIconSprite(OrderInfo orderInfo) =>
            GetOrderIconSprite(orderInfo.Order, orderInfo.OrderOption);

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
        
        #region Context Menu

        public void CreateModerationContextMenu(Point mousePos, Client client)
        {
            if (GUIContextMenu.CurrentContextMenu != null) { return; }
            if (IsSinglePlayer || client == null || (!GameMain.Client?.PreviouslyConnectedClients?.Contains(client) ?? true)) { return; }

            bool hasSteam = client.SteamID > 0 && SteamManager.IsInitialized,
                 canKick  = GameMain.Client.HasPermission(ClientPermissions.Kick),
                 canBan   = GameMain.Client.HasPermission(ClientPermissions.Ban) && client.AllowKicking,
                 canPromo = GameMain.Client.HasPermission(ClientPermissions.ManagePermissions);

            // Disable options if we are targeting ourselves
            if (client.ID == GameMain.Client?.ID)
            {
                canKick = canBan = canPromo = false;
            }

            List<ContextMenuOption> options = new List<ContextMenuOption>();
            
            options.Add(new ContextMenuOption("ViewSteamProfile", isEnabled: hasSteam, onSelected: delegate
            { 
                Steamworks.SteamFriends.OpenWebOverlay($"https://steamcommunity.com/profiles/{client.SteamID}");
            }));

            options.Add(new ContextMenuOption("ModerationMenu.UserDetails", isEnabled: true, onSelected: delegate
            {
                GameMain.NetLobbyScreen?.SelectPlayer(client);
            }));


            // Creates sub context menu options for all the ranks
            List<ContextMenuOption> permissionOptions = new List<ContextMenuOption>();
            foreach (PermissionPreset rank in PermissionPreset.List)
            {
                permissionOptions.Add(new ContextMenuOption(rank.Name, isEnabled: true, onSelected: () =>
                {
                    string label = TextManager.GetWithVariables(rank.Permissions == ClientPermissions.None ?  "clearrankprompt" : "giverankprompt", new []{ "[user]", "[rank]" }, new []{ client.Name, rank.Name });
                    GUIMessageBox msgBox = new GUIMessageBox(string.Empty, label, new[] { TextManager.Get("Yes"), TextManager.Get("Cancel") });

                    msgBox.Buttons[0].OnClicked = delegate
                    {
                        client.SetPermissions(rank.Permissions, rank.PermittedCommands);
                        GameMain.Client.UpdateClientPermissions(client);
                        msgBox.Close();
                        return true;
                    };
                    msgBox.Buttons[1].OnClicked = delegate
                    {
                        msgBox.Close();
                        return true;
                    };
                }) { Tooltip = rank.Description });
            }

            options.Add(new ContextMenuOption("Permissions", isEnabled: canPromo, options: permissionOptions.ToArray()));

            Color clientColor = client.Character?.Info?.Job.Prefab.UIColor ?? Color.White;

            if (GameMain.Client.ConnectedClients.Contains(client))
            {
                options.Add(new ContextMenuOption(client.MutedLocally ? "Unmute" : "Mute", isEnabled: client.ID != GameMain.Client?.ID, onSelected: delegate
                {
                    client.MutedLocally = !client.MutedLocally;
                }));

                bool kickEnabled = client.ID != GameMain.Client?.ID && client.AllowKicking;

                // if the user can kick create a kick option else create the votekick option
                ContextMenuOption kickOption;
                if (canKick)
                {
                    kickOption = new ContextMenuOption("Kick", isEnabled: kickEnabled, onSelected: delegate
                    {
                        GameMain.Client?.CreateKickReasonPrompt(client.Name, false);
                    });
                }
                else
                {
                    kickOption = new ContextMenuOption("VoteToKick", isEnabled: kickEnabled, onSelected: delegate
                    {
                        GameMain.Client?.VoteForKick(client);
                    });
                }

                options.Add(kickOption);
            }

            options.Add(new ContextMenuOption("Ban", isEnabled: canBan, onSelected: delegate
            {
                GameMain.Client?.CreateKickReasonPrompt(client.Name, true);
            }));

            GUIContextMenu.CreateContextMenu(null, client.Name, headerColor: clientColor, options.ToArray());
        }
        
        #endregion

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

            crewAreaWithButtons.Visible = !(GameMain.GameSession?.GameMode is CampaignMode campaign) || (!campaign.ForceMapUI && !campaign.ShowCampaignUI);

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
            if (aiController != null)
            {
                aiController.Reset();
            }
            DisableCommandUI();
            Character.Controlled = character;
        }

        private int TryAdjustIndex(int amount)
        {
            int index = Character.Controlled == null ? 0 :
                crewList.Content.GetChildIndex(crewList.Content.GetChildByUserData(Character.Controlled)) + amount;
            int lastIndex = crewList.Content.CountChildren - 1;
            if (index > lastIndex)
            {
                index = 0;
            }
            if (index < 0)
            {
                index = lastIndex;
            }
            return index;
        }

        partial void UpdateProjectSpecific(float deltaTime)
        {
            // Quick selection
            if (!GameMain.IsMultiplayer && GUI.KeyboardDispatcher.Subscriber == null)
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

            #region Command UI

            WasCommandInterfaceDisabledThisUpdate = false;

            if (PlayerInput.KeyDown(InputType.Command) && (GUI.KeyboardDispatcher.Subscriber == null || GUI.KeyboardDispatcher.Subscriber == crewList) &&
                commandFrame == null && !clicklessSelectionActive && CanIssueOrders && !(GameMain.GameSession?.Campaign?.ShowCampaignUI ?? false))
            {
                if (PlayerInput.KeyDown(Keys.LeftShift) || PlayerInput.KeyDown(Keys.RightShift))
                {
                    CreateCommandUI(FindEntityContext(), true);
                }
                else
                {
                    CreateCommandUI(HUDLayoutSettings.BottomRightInfoArea.Contains(PlayerInput.MousePosition) ? Character.Controlled : GUI.MouseOn?.UserData as Character);
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
                var hitDeselect = PlayerInput.KeyHit(InputType.Deselect) && (!PlayerInput.SecondaryMouseButtonClicked() ||
                     (optionNodes.None(n => GUI.IsMouseOn(n.Item1)) && shortcutNodes.None(n => GUI.IsMouseOn(n))));
                // TODO: Consider using HUD.CloseHUD() instead of KeyHit(Escape), the former method is also used for health UI
                if (hitDeselect || PlayerInput.KeyHit(Keys.Escape) || !CanIssueOrders ||
                    (PlayerInput.KeyHit(InputType.Command) && selectedNode == null && !clicklessSelectionActive))
                {
                    DisableCommandUI();
                }
                else if (PlayerInput.KeyUp(InputType.Command))
                {
                    if (!isOpeningClick && clicklessSelectionActive && timeSelected < 0.15f)
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
                    if (!GUI.IsMouseOn(centerNode))
                    {
                        clicklessSelectionActive = true;

                        var mouseBearing = GetBearing(centerNode.Center, PlayerInput.MousePosition, flipY: true);

                        GUIComponent closestNode = null;
                        float closestBearing = 0;

                        optionNodes.ForEach(n => CheckIfClosest(n.Item1));
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

                        if (closestNode != null && closestNode == selectedNode)
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
                foreach (Tuple<GUIComponent, Keys> node in optionNodes)
                {
                    if (node.Item2 != Keys.None && PlayerInput.KeyHit(node.Item2))
                    {
                        var b = node.Item1 as GUIButton;
                        if (PlayerInput.IsShiftDown() && b?.OnSecondaryClicked != null)
                        {
                            b.OnSecondaryClicked.Invoke(node.Item1 as GUIButton, node.Item1.UserData);
                        }
                        else
                        {
                            b?.OnClicked?.Invoke(node.Item1 as GUIButton, node.Item1.UserData);
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

            // TODO: Expand crew list to use command button's space when it's not visible
            if (!IsSinglePlayer && commandButton != null)
            {
                if (!CanIssueOrders && commandButton.Visible)
                {
                    commandButton.Visible = false;
                }
                else if (CanIssueOrders && !commandButton.Visible)
                {
                    commandButton.Visible = true;
                }
            }

            #endregion

            if (ChatBox != null)
            {
                ChatBox.Update(deltaTime);
                ChatBox.InputBox.Visible = Character.Controlled != null;

                if (!DebugConsole.IsOpen && ChatBox.InputBox.Visible && GUI.KeyboardDispatcher.Subscriber == null)
                {
                    if (PlayerInput.KeyHit(InputType.Chat) && !ChatBox.InputBox.Selected)
                    {
                        ChatBox.InputBox.AddToGUIUpdateList();
                        ChatBox.GUIFrame.Flash(Color.DarkGreen, 0.5f);
                        if (!ChatBox.ToggleOpen)
                        {
                            ChatBox.CloseAfterMessageSent = !ChatBox.ToggleOpen;
                            ChatBox.ToggleOpen = true;
                        }
                        ChatBox.InputBox.Select(ChatBox.InputBox.Text.Length);
                    }

                    if (PlayerInput.KeyHit(InputType.RadioChat) && !ChatBox.InputBox.Selected)
                    {
                        if (Character.Controlled == null || Character.Controlled.SpeechImpediment < 100)
                        {
                            ChatBox.InputBox.AddToGUIUpdateList();
                            ChatBox.GUIFrame.Flash(Color.YellowGreen, 0.5f);
                            if (!ChatBox.ToggleOpen)
                            {
                                ChatBox.CloseAfterMessageSent = !ChatBox.ToggleOpen;
                                ChatBox.ToggleOpen = true;
                            }

                            if (!ChatBox.InputBox.Text.StartsWith(ChatBox.RadioChatString))
                            {
                                ChatBox.InputBox.Text = ChatBox.RadioChatString;
                            }
                            ChatBox.InputBox.Select(ChatBox.InputBox.Text.Length);
                        }
                    }
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
                        if (characterComponent.Visible)
                        {
                            if (character == Character.Controlled && characterComponent.State != GUIComponent.ComponentState.Selected)
                            {
                                crewList.Select(character, force: true);
                            }
                            if (character.AIController is HumanAIController controller)
                            {
                                OrderInfo? currentOrderInfo = controller.ObjectiveManager?.GetCurrentOrderInfo();
                                if (currentOrderInfo.HasValue)
                                {
                                    SetHighlightedOrderIcon(characterComponent, currentOrderInfo.Value.Order?.Identifier, currentOrderInfo.Value.OrderOption);
                                }
                            }
                            if (characterComponent.GetChild<GUILayoutGroup>().GetChildByUserData("soundicons") is GUIComponent soundIconParent)
                            {
                                if (soundIconParent.FindChild(c => c.UserData is Pair<string, float> pair && pair.First == "soundicon") is GUIImage soundIcon)
                                {
                                    VoipClient.UpdateVoiceIndicator(soundIcon, 0.0f, deltaTime);
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

        private void SetHighlightedOrderIcon(GUIComponent characterComponent, string orderIdentifier, string orderOption)
        {
            var currentOrderIconList = GetCurrentOrderIconList(characterComponent);
            if (currentOrderIconList == null) { return; }
            bool foundMatch = false;
            foreach (var orderIcon in currentOrderIconList.Content.Children)
            {
                var glowComponent = orderIcon.GetChildByUserData("glow");
                if (glowComponent == null) { continue; }
                if (foundMatch)
                {
                    glowComponent.Visible = false;
                    continue;
                }
                var orderInfo = (OrderInfo)orderIcon.UserData;
                foundMatch = orderInfo.MatchesOrder(orderIdentifier, orderOption);
                glowComponent.Visible = foundMatch;
            }
        }

        public void SetHighlightedOrderIcon(Character character, string orderIdentifier, string orderOption)
        {
            if (crewList == null) { return; }
            var characterComponent = crewList.Content.GetChildByUserData(character);
            SetHighlightedOrderIcon(characterComponent, orderIdentifier, orderOption);
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
        private GUIButton centerNode, returnNode, expandNode, shortcutCenterNode;
        private readonly List<Tuple<GUIComponent, Keys>> optionNodes = new List<Tuple<GUIComponent, Keys>>();
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
        private Order dismissedOrderPrefab;
        private Character characterContext;
        private Item itemContext;
        private Hull hullContext;
        private WallSection wallContext;
        private bool isContextual;
        private readonly List<Order> contextualOrders = new List<Order>();
        private Point shorcutCenterNodeOffset;
        private const int maxShortCutNodeCount = 4;

        private bool WasCommandInterfaceDisabledThisUpdate { get; set; }
        private bool CanIssueOrders
        {
            get
            {
#if DEBUG
                if (Character.Controlled == null) { return true; }
#endif
                return Character.Controlled?.Info != null && Character.Controlled.SpeechImpediment < 100.0f;

            }
        }

        private bool CanSomeoneHearCharacter()
        {
#if DEBUG
            if (Character.Controlled == null) { return true; }
#endif
            return Character.Controlled != null && characters.Any(c => c != Character.Controlled && c.CanHearCharacter(Character.Controlled));
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
                new RectTransform(Vector2.One, GUICanvas.Instance, anchor: Anchor.Center),
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
            dismissedOrderPrefab ??= Order.GetPrefab("dismissed");

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
        }

        private void ToggleCommandUI()
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
                if (Order.PrefabList.Any(o => o.Category == category && !o.IsReport))
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
                onDraw: DrawNodeConnectors);
            nodeConnectors.SetAsFirstChild();
            background.SetAsFirstChild();
        }

        private void DrawNodeConnectors(SpriteBatch spriteBatch, GUIComponent container)
        {
            if (centerNode == null || optionNodes == null) { return; }
            var startNodePos = centerNode.Rect.Center.ToVector2();
            // Don't draw connectors for mini map options or assignment nodes
            if ((targetFrame == null || !targetFrame.Visible) && !(optionNodes.FirstOrDefault()?.Item1.UserData is Character))
            {
                optionNodes.ForEach(n => DrawNodeConnector(startNodePos, centerNodeMargin, n.Item1, optionNodeMargin, spriteBatch));
            }
            DrawNodeConnector(startNodePos, centerNodeMargin, returnNode, returnNodeMargin, spriteBatch);
            if (shortcutCenterNode == null || !shortcutCenterNode.Visible) { return; }
            DrawNodeConnector(startNodePos, centerNodeMargin, shortcutCenterNode, shortcutCenterNodeMargin, spriteBatch);
            startNodePos = shortcutCenterNode.Rect.Center.ToVector2();
            shortcutNodes.ForEach(n => DrawNodeConnector(startNodePos, shortcutCenterNodeMargin, n, shortcutNodeMargin, spriteBatch));
        }

        private void DrawNodeConnector(Vector2 startNodePos, float startNodeMargin, GUIComponent endNode, float endNodeMargin, SpriteBatch spriteBatch)
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
                GUI.DrawLine(spriteBatch, start, end, colorSource != null ? colorSource.HoverColor : Color.White, width: 4);
            }
            else
            {
                GUI.DrawLine(spriteBatch, start, end, colorSource != null ? colorSource.Color : Color.White * nodeColorMultiplier, width: 2);
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
            if (!(optionNodes.Find(n => n.Item1 == node) is Tuple<GUIComponent, Keys> optionNode) || !optionNodes.Remove(optionNode))
            {
                shortcutNodes.Remove(node);
            };
            RemoveOptionNodes();

            if (returnNode != null)
            {
                returnNode.RemoveChild(returnNode.GetChildByUserData("hotkey"));
                returnNode.Children.ForEach(child => child.Visible = false);
                returnNode.Visible = false;
                historyNodes.Push(returnNode);
            }

            // When the mini map is shown, always position the return node on the bottom
            List<Item> matchingItems = null;
            if (node?.UserData is Order order)
            {
                matchingItems = order.GetMatchingItems(true, interactableFor: characterContext ?? Character.Controlled);
            }
            var offset =  matchingItems != null && matchingItems.Count > 1 ?
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
            RemoveOptionNodes();
            if (targetFrame != null) { targetFrame.Visible = false; }
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
            else if (userData is Order order)
            {
                CreateOrderOptions(order);
            }
            return true;
        }

        private void RemoveOptionNodes()
        {
            if (commandFrame != null)
            {
                optionNodes.ForEach(node => commandFrame.RemoveChild(node.Item1));
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
            if (Order.OrderCategoryIcons.TryGetValue(category, out Tuple<Sprite, Color> sprite))
            {
                var tooltip = TextManager.Get("ordercategorytitle." + category.ToString().ToLower());
                var categoryDescription = TextManager.Get("ordercategorydescription." + category.ToString(), true);
                if (!string.IsNullOrWhiteSpace(categoryDescription)) { tooltip += "\n" + categoryDescription; }
                CreateNodeIcon(Vector2.One, node.RectTransform, sprite.Item1, sprite.Item2, tooltip: tooltip);
            }
            CreateHotkeyIcon(node.RectTransform, hotkey % 10);
            optionNodes.Add(new Tuple<GUIComponent, Keys>(node, Keys.D0 + hotkey % 10));
        }

        private void CreateShortcutNodes()
        {
            Submarine sub = GetTargetSubmarine();

            if (sub == null) { return; }

            shortcutNodes.Clear();

            if (shortcutNodes.Count < maxShortCutNodeCount &&
                sub.GetItems(false).Find(i => i.HasTag("reactor") && i.IsPlayerTeamInteractable)?.GetComponent<Reactor>() is Reactor reactor)
            {
                var reactorOutput = -reactor.CurrPowerConsumption;
                // If player is not an engineer AND the reactor is not powered up AND nobody is using the reactor
                // ---> Create shortcut node for "Operate Reactor" order's "Power Up" option
                if ((Character.Controlled == null || Character.Controlled.Info?.Job?.Prefab != JobPrefab.Get("engineer")) &&
                    reactorOutput < float.Epsilon && characters.None(c => c.SelectedConstruction == reactor.Item))
                {
                    var order = new Order(Order.GetPrefab("operatereactor"), reactor.Item, reactor, Character.Controlled);
                    var option = order.Prefab.Options[0];
                    shortcutNodes.Add(
                        CreateOrderOptionNode(shortcutNodeSize, null, Point.Zero, order, option, order.Prefab.GetOptionName(option), -1));
                }
            }

            // TODO: Reconsider the conditions as bot captain can have the nav term selected without operating it
            // If player is not a captain AND nobody is using the nav terminal AND the nav terminal is powered up
            // --> Create shortcut node for Steer order
            if (shortcutNodes.Count < maxShortCutNodeCount && (Character.Controlled == null || Character.Controlled.Info?.Job?.Prefab != JobPrefab.Get("captain")) &&
                sub.GetItems(false).Find(i => i.HasTag("navterminal") && i.IsPlayerTeamInteractable) is Item nav && characters.None(c => c.SelectedConstruction == nav) &&
                nav.GetComponent<Steering>() is Steering steering && steering.Voltage > steering.MinVoltage)
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, Order.GetPrefab("steer"), -1));
            }

            // If player is not a security officer AND invaders are reported
            // --> Create shorcut node for Fight Intruders order
            if (shortcutNodes.Count < maxShortCutNodeCount && (Character.Controlled == null || Character.Controlled.Info?.Job?.Prefab != JobPrefab.Get("securityofficer")) &&
                (Order.GetPrefab("reportintruders") is Order reportIntruders && ActiveOrders.Any(o => o.First.Prefab == reportIntruders)))
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, Order.GetPrefab("fightintruders"), -1));
            }

            // If player is not a mechanic AND a breach has been reported
            // --> Create shorcut node for Fix Leaks order
            if (shortcutNodes.Count < maxShortCutNodeCount && (Character.Controlled == null || Character.Controlled.Info?.Job?.Prefab != JobPrefab.Get("mechanic")) &&
                (Order.GetPrefab("reportbreach") is Order reportBreach && ActiveOrders.Any(o => o.First.Prefab == reportBreach)))
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, Order.GetPrefab("fixleaks"), -1));
            }

            // If player is not an engineer AND broken devices have been reported
            // --> Create shortcut node for Repair Damaged Systems order
            if (shortcutNodes.Count < maxShortCutNodeCount && (Character.Controlled == null || Character.Controlled.Info?.Job?.Prefab != JobPrefab.Get("engineer")) &&
                (Order.GetPrefab("reportbrokendevices") is Order reportBrokenDevices && ActiveOrders.Any(o => o.First.Prefab == reportBrokenDevices)))
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, Order.GetPrefab("repairsystems"), -1));
            }

            // If fire is reported
            // --> Create shortcut node for Extinguish Fires order
            if (shortcutNodes.Count < maxShortCutNodeCount && ActiveOrders.Any(o=> o.First.Prefab == Order.GetPrefab("reportfire")))
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, Order.GetPrefab("extinguishfires"), -1));
            }

            if (shortcutNodes.Count < maxShortCutNodeCount && characterContext?.Info?.Job?.Prefab?.AppropriateOrders != null)
            {
                foreach (string orderIdentifier in characterContext.Info.Job.Prefab.AppropriateOrders)
                {
                    if (Order.GetPrefab(orderIdentifier) is Order orderPrefab &&
                        shortcutNodes.None(n => (n.UserData is Order order && order.Identifier == orderIdentifier) ||
                                                (n.UserData is Tuple<Order, string> orderWithOption && orderWithOption.Item1.Identifier == orderIdentifier)) &&
                        !orderPrefab.IsReport && orderPrefab.Category != null)
                    {
                        if (!orderPrefab.MustSetTarget || orderPrefab.GetMatchingItems(sub, true, interactableFor: characterContext ?? Character.Controlled).Any())
                        {
                            shortcutNodes.Add(CreateOrderNode(shortcutNodeSize, null, Point.Zero, orderPrefab, -1));
                        }
                        if (shortcutNodes.Count >= maxShortCutNodeCount) { break; }
                    }
                }
            }

            if (shortcutNodes.Count < maxShortCutNodeCount && characterContext != null && !characterContext.IsDismissed)
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, dismissedOrderPrefab, -1));
            }

            if (shortcutNodes.Count < 1) { return; }

            shortcutCenterNode = new GUIButton(
                new RectTransform(shortcutCenterNodeSize, parent: commandFrame.RectTransform, anchor: Anchor.Center),
                style: null);
            CreateNodeIcon(shortcutCenterNode.RectTransform, "CommandShortcutNode");
            foreach (GUIComponent c in shortcutCenterNode.Children)
            {
                c.HoverColor = c.Color;
                c.PressedColor = c.Color;
                c.SelectedColor = c.Color;
            }
            shortcutCenterNode.RectTransform.MoveOverTime(shorcutCenterNodeOffset, CommandNodeAnimDuration);

            var nodeCountForCalculations = shortcutNodes.Count * 2 + 2;
            var offsets = MathUtils.GetPointsOnCircumference(Vector2.Zero, 0.75f * nodeDistance, nodeCountForCalculations);
            var firstOffsetIndex = nodeCountForCalculations / 2 - 1;
            for (int i = 0; i < shortcutNodes.Count; i++)
            {
                shortcutNodes[i].RectTransform.Parent = commandFrame.RectTransform;
                shortcutNodes[i].RectTransform.MoveOverTime(shorcutCenterNodeOffset + offsets[firstOffsetIndex - i].ToPoint(), CommandNodeAnimDuration);
            }
        }

        private void CreateOrderNodes(OrderCategory orderCategory)
        {
            var orders = Order.PrefabList.FindAll(o => o.Category == orderCategory && !o.IsReport);
            Order order;
            bool disableNode;
            var offsets = MathUtils.GetPointsOnCircumference(Vector2.Zero, nodeDistance,
                GetCircumferencePointCount(orders.Count), GetFirstNodeAngle(orders.Count));
            for (int i = 0; i < orders.Count; i++)
            {
                order = orders[i];
                disableNode = !CanSomeoneHearCharacter() ||
                    (order.MustSetTarget && (order.ItemComponentType != null || order.TargetItems.Length > 0) &&
                     order.GetMatchingItems(true, interactableFor: characterContext ?? Character.Controlled).None());
                optionNodes.Add(new Tuple<GUIComponent, Keys>(
                    CreateOrderNode(nodeSize, commandFrame.RectTransform, offsets[i].ToPoint(), order, (i + 1) % 10, disableNode: disableNode, checkIfOrderCanBeHeard: false),
                    !disableNode ? Keys.D0 + (i + 1) % 10 : Keys.None));
            }
        }

        /// <summary>
        /// Create order nodes based on the item context
        /// </summary>
        private void CreateContextualOrderNodes()
        {
            if (contextualOrders.None())
            {
                string orderIdentifier;

                // Check if targeting an item or a hull
                if (itemContext != null && itemContext.IsPlayerTeamInteractable)
                {
                    ItemComponent targetComponent;
                    foreach (Order p in Order.PrefabList)
                    {
                        targetComponent = null;
                        if ((p.TargetItems.Length > 0 && (p.TargetItems.Contains(itemContext.Prefab.Identifier) || itemContext.HasTag(p.TargetItems))) ||
                            p.TryGetTargetItemComponent(itemContext, out targetComponent))
                        {
                            contextualOrders.Add(p.HasOptions ? p : new Order(p, itemContext, targetComponent, Character.Controlled));
                        }
                    }

                    // If targeting a periscope connected to a turret, show the 'operateweapons' order
                    orderIdentifier = "operateweapons";
                    var operateWeaponsPrefab = Order.GetPrefab(orderIdentifier);
                    if (contextualOrders.None(o => o.Identifier.Equals(orderIdentifier)) && itemContext.Components.Any(c => c is Controller))
                    {
                        var turret = itemContext.GetConnectedComponents<Turret>().FirstOrDefault(c => c.Item.HasTag(operateWeaponsPrefab.TargetItems)) ??
                            itemContext.GetConnectedComponents<Turret>(recursive: true).FirstOrDefault(c => c.Item.HasTag(operateWeaponsPrefab.TargetItems));
                        if (turret != null) { contextualOrders.Add(new Order(operateWeaponsPrefab, turret.Item, turret, Character.Controlled)); }
                    }

                    // If targeting a repairable item with condition below the repair threshold, show the 'repairsystems' order
                    orderIdentifier = "repairsystems";
                    if (contextualOrders.None(o => o.Identifier.Equals(orderIdentifier)) && itemContext.Repairables.Any(r => itemContext.ConditionPercentage < r.RepairThreshold))
                    {
                        if (itemContext.Repairables.Any(r => r != null && r.requiredSkills.Any(s => s != null && s.Identifier.Equals("electrical"))))
                        {
                            contextualOrders.Add(new Order(Order.GetPrefab("repairelectrical"), itemContext, targetItem: null, Character.Controlled));
                        }
                        else if (itemContext.Repairables.Any(r => r != null && r.requiredSkills.Any(s => s != null && s.Identifier.Equals("mechanical"))))
                        {
                            contextualOrders.Add(new Order(Order.GetPrefab("repairmechanical"), itemContext, targetItem: null, Character.Controlled));
                        }
                        else
                        {
                            contextualOrders.Add(new Order(Order.GetPrefab(orderIdentifier), itemContext, targetItem: null, Character.Controlled));
                        }
                    }

                    // Remove the 'pumpwater' order if the target pump is auto-controlled (as it will immediately overwrite the work done by the bot)
                    orderIdentifier = "pumpwater";
                    if (contextualOrders.FirstOrDefault(o => o.Identifier.Equals(orderIdentifier)) is Order o &&
                        itemContext.Components.FirstOrDefault(c => c.GetType() == o.ItemComponentType) is Pump pump)
                    {
                        if (pump.IsAutoControlled) { contextualOrders.Remove(o); }
                    }

                    if (contextualOrders.None())
                    {
                        orderIdentifier = "cleanupitems";
                        if (contextualOrders.None(o => o.Identifier.Equals(orderIdentifier)))
                        {
                            if (AIObjectiveCleanupItems.IsValidTarget(itemContext, Character.Controlled, checkInventory: false) || AIObjectiveCleanupItems.IsValidContainer(itemContext, Character.Controlled))
                            {
                                contextualOrders.Add(new Order(Order.GetPrefab(orderIdentifier), itemContext, targetItem: null, Character.Controlled));
                            }
                        }
                    }

                    AddIgnoreOrder(itemContext);
                }
                else if (hullContext != null)
                {
                    contextualOrders.Add(new Order(Order.GetPrefab("fixleaks"), hullContext, targetItem: null, Character.Controlled));

                    if (wallContext != null)
                    {
                        AddIgnoreOrder(wallContext);
                    }
                }

                void AddIgnoreOrder(IIgnorable target)
                {
                    var orderIdentifier = "ignorethis";
                    if (!target.OrderedToBeIgnored && contextualOrders.None(o => o.Identifier == orderIdentifier))
                    {
                        AddOrder();
                    }
                    else
                    {
                        orderIdentifier = "unignorethis";
                        if (target.OrderedToBeIgnored && contextualOrders.None(o => o.Identifier == orderIdentifier))
                        {
                            AddOrder();
                        }
                    }

                    void AddOrder()
                    {
                        if (target is WallSection ws)
                        {
                            contextualOrders.Add(new Order(Order.GetPrefab(orderIdentifier), ws.Wall, ws.Wall.Sections.IndexOf(ws), orderGiver: Character.Controlled));
                        }
                        else
                        {
                            contextualOrders.Add(new Order(Order.GetPrefab(orderIdentifier), target as Entity, null, Character.Controlled));
                        }
                    }
                }

                orderIdentifier = "wait";
                if (contextualOrders.None(o => o.Identifier.Equals(orderIdentifier)))
                {
                    Vector2 position = GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
                    Hull hull = Hull.FindHull(position, guess: Character.Controlled?.CurrentHull);
                    contextualOrders.Add(new Order(Order.GetPrefab(orderIdentifier), new OrderTarget(position, hull), Character.Controlled));
                }

                if (contextualOrders.None(o => o.Category != OrderCategory.Movement) && characters.Any(c => c != Character.Controlled))
                {
                    orderIdentifier = "follow";
                    if (contextualOrders.None(o => o.Identifier.Equals(orderIdentifier)))
                    {
                        contextualOrders.Add(Order.GetPrefab(orderIdentifier));
                    }
                }

                // Show 'dismiss' order only when there are crew members with active orders
                orderIdentifier = "dismissed";
                if (contextualOrders.None(o => o.Identifier.Equals(orderIdentifier)) && characters.Any(c => !c.IsDismissed))
                {
                    contextualOrders.Add(Order.GetPrefab(orderIdentifier));
                }
            }

            var offsets = MathUtils.GetPointsOnCircumference(Vector2.Zero, nodeDistance, contextualOrders.Count, MathHelper.ToRadians(90f + 180f / contextualOrders.Count));
            bool disableNode = !CanSomeoneHearCharacter();
            for (int i = 0; i < contextualOrders.Count; i++)
            {
                optionNodes.Add(new Tuple<GUIComponent, Keys>(
                    CreateOrderNode(nodeSize, commandFrame.RectTransform, offsets[i].ToPoint(), contextualOrders[i], (i + 1) % 10, disableNode: disableNode, checkIfOrderCanBeHeard: false),
                    !disableNode ? Keys.D0 + (i + 1) % 10 : Keys.None));
            }
        }

        // TODO: there's duplicate logic here and above -> would be better to refactor so that the conditions are only defined in one place
        public static bool DoesItemHaveContextualOrders(Item item)
        {
            if (Order.PrefabList.Any(o => o.TargetItems.Length > 0 && o.TargetItems.Contains(item.Prefab.Identifier))) { return true; }
            if (Order.PrefabList.Any(o => item.HasTag(o.TargetItems))) { return true; }
            if (Order.PrefabList.Any(o => o.TryGetTargetItemComponent(item, out _))) { return true; }
            if (AIObjectiveCleanupItems.IsValidTarget(item, Character.Controlled, checkInventory: false)) { return true; }
            if (AIObjectiveCleanupItems.IsValidContainer(item, Character.Controlled)) { return true; }

            if (item.Repairables.Any(r => item.ConditionPercentage < r.RepairThreshold)) { return true; }
            var operateWeaponsPrefab = Order.GetPrefab("operateweapons");
            return item.Components.Any(c => c is Controller) &&
                (item.GetConnectedComponents<Turret>().Any(c => c.Item.HasTag(operateWeaponsPrefab.TargetItems)) ||
                 item.GetConnectedComponents<Turret>(recursive: true).Any(c => c.Item.HasTag(operateWeaponsPrefab.TargetItems))); 
        }

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
                disableNode = !CanSomeoneHearCharacter();
            }

            var mustSetOptionOrTarget = order.HasOptions;
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
                if (o.MustManuallyAssign && characterContext == null)
                {
                    CreateAssignmentNodes(node);
                }
                else if (mustSetOptionOrTarget)
                {
                    NavigateForward(button, userData);
                }
                else
                {
                    if (orderTargetEntity != null)
                    {
                        o = new Order(o.Prefab, orderTargetEntity, orderTargetEntity.Components.FirstOrDefault(ic => ic.GetType() == order.ItemComponentType), orderGiver: order.OrderGiver);
                    }
                    var character = !o.TargetAllCharacters ? characterContext ?? GetCharacterForQuickAssignment(o) : null;
                    SetCharacterOrder(character, o, null, CharacterInfo.HighestManualOrderPriority, Character.Controlled);
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
                    "\n" + (!PlayerInput.MouseButtonsSwapped() ? TextManager.Get("input.leftmouse") : TextManager.Get("input.rightmouse")) + ": " + TextManager.Get("commandui.quickassigntooltip") +
                    "\n" + (!PlayerInput.MouseButtonsSwapped() ? TextManager.Get("input.rightmouse") : TextManager.Get("input.leftmouse")) + ": " + TextManager.Get("commandui.manualassigntooltip"));
            
            if (disableNode)
            {
                node.CanBeFocused = icon.CanBeFocused = false;
                CreateBlockIcon(node.RectTransform);
            }
            else if (hotkey >= 0)
            {
                CreateHotkeyIcon(node.RectTransform, hotkey);
            }
            return node;
        }

        private void CreateOrderOptions(Order order)
        {
            Submarine submarine = GetTargetSubmarine();
            var matchingItems = (itemContext == null && order.MustSetTarget) ? order.GetMatchingItems(submarine, true, interactableFor: characterContext ?? Character.Controlled) : new List<Item>();

            //more than one target item -> create a minimap-like selection with a pic of the sub
            if (itemContext == null && matchingItems.Count > 1)
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
                    if (itemTargetFrame.RectTransform.RelativeOffset.X < 0.5f && itemTargetFrame.RectTransform.RelativeOffset.Y < 0.5f)
                    {
                        anchor = Anchor.BottomRight;
                    }
                    else if (itemTargetFrame.RectTransform.RelativeOffset.X > 0.5f && itemTargetFrame.RectTransform.RelativeOffset.Y < 0.5f)
                    {
                        anchor = Anchor.BottomLeft;
                    }

                    else if (itemTargetFrame.RectTransform.RelativeOffset.X < 0.5f && itemTargetFrame.RectTransform.RelativeOffset.Y > 0.5f)
                    {
                        anchor = Anchor.TopRight;
                    }

                    GUIComponent optionElement;
                    if (order.Options.Length > 1)
                    {
                        optionElement = new GUIFrame(
                            new RectTransform(
                                new Point((int)(250 * GUI.Scale), (int)((40 + order.Options.Length * 40) * GUI.Scale)),
                                parent: itemTargetFrame.RectTransform,
                                anchor: anchor),
                            style: "InnerFrame");

                        new GUIFrame(
                            new RectTransform(Vector2.One, optionElement.RectTransform, anchor: Anchor.Center),
                            style: "OuterGlow",
                            color: Color.Black * 0.7f);

                        var optionContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.9f), optionElement.RectTransform, anchor: Anchor.Center))
                        {
                            RelativeSpacing = 0.05f,
                            Stretch = true
                        };

                        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), optionContainer.RectTransform),
                            item?.Name ?? GetOrderNameBasedOnContextuality(order));

                        for (int i = 0; i < order.Options.Length; i++)
                        {
                            var optionButton = new GUIButton(
                                new RectTransform(new Vector2(1.0f, 0.2f), optionContainer.RectTransform),
                                text: order.GetOptionName(i), style: "GUITextBox")
                            {
                                UserData = new Tuple<Order, string>(
                                    item == null ? order : new Order(order, item, order.GetTargetItemComponent(item)),
                                    order.Options[i]),
                                Font = GUI.SmallFont,
                                OnClicked = (_, userData) =>
                                {
                                    if (!CanIssueOrders) { return false; }
                                    var o = userData as Tuple<Order, string>;
                                    SetCharacterOrder(characterContext ?? GetCharacterForQuickAssignment(o.Item1), o.Item1, o.Item2, CharacterInfo.HighestManualOrderPriority, Character.Controlled);
                                    DisableCommandUI();
                                    return true;
                                }
                            };
                            if (CanOpenManualAssignment(optionButton))
                            {
                                optionButton.OnSecondaryClicked = (button, _) => CreateAssignmentNodes(button);
                            }
                            optionNodes.Add(new Tuple<GUIComponent, Keys>(optionButton, Keys.None));
                        }
                    }
                    else
                    {
                        var userData = new Tuple<Order, string>(item == null ? order : new Order(order, item, order.GetTargetItemComponent(item)), "");
                        optionElement = new GUIButton(
                            new RectTransform(
                                new Point((int)(50 * GUI.Scale)),
                                parent: itemTargetFrame.RectTransform,
                                anchor: anchor),
                            style: null)
                        {
                            UserData = userData,
                            Font = GUI.SmallFont,
                            ToolTip = item?.Name ?? GetOrderNameBasedOnContextuality(order),
                            OnClicked = (_, userData) =>
                            {
                                if (!CanIssueOrders) { return false; }
                                var o = userData as Tuple<Order, string>;
                                SetCharacterOrder(characterContext ?? GetCharacterForQuickAssignment(o.Item1), o.Item1, o.Item2, CharacterInfo.HighestManualOrderPriority, Character.Controlled);
                                DisableCommandUI();
                                return true;
                            }
                        };
                        if (CanOpenManualAssignment(optionElement))
                        {
                            optionElement.OnSecondaryClicked = (button, _) => CreateAssignmentNodes(button);
                        }
                        Sprite icon = null;
                        order.MinimapIcons?.TryGetValue(item.Prefab.Identifier, out icon);
                        if (item.Prefab.MinimapIcon != null)
                        {
                            icon = item.Prefab.MinimapIcon;
                        }
                        var colorMultiplier = characters.Any(c => c.CurrentOrders.Any(o => o.Order != null &&
                            o.Order.Identifier == userData.Item1.Identifier &&
                            o.Order.TargetEntity == userData.Item1.TargetEntity)) ? 0.5f : 1f;
                        CreateNodeIcon(Vector2.One, optionElement.RectTransform, icon ?? order.SymbolSprite, order.Color * colorMultiplier);
                        optionNodes.Add(new Tuple<GUIComponent, Keys>(optionElement, Keys.None));
                    }
                    optionElements.Add(optionElement);
                }
                GUI.PreventElementOverlap(optionElements, clampArea: new Rectangle(10, 10, GameMain.GraphicsWidth - 20, GameMain.GraphicsHeight - 20));

                var shadow = new GUIFrame(
                    new RectTransform(targetFrame.Rect.Size + new Point((int)(200 * GUI.Scale)), targetFrame.RectTransform, anchor: Anchor.Center),
                    style: "OuterGlow",
                    color: matchingItems.Count > 1 ? Color.Black * 0.9f : Color.Black * 0.7f);
                shadow.SetAsFirstChild();
            }
            //only one target (or an order with no particular targets), just show options
            else
            {
                var item = itemContext != null ?
                    (order.UseController ? itemContext.GetConnectedComponents<Turret>().FirstOrDefault()?.Item ?? itemContext.GetConnectedComponents<Turret>(recursive: true).FirstOrDefault()?.Item : itemContext) :
                    (matchingItems.Count > 0 ? matchingItems[0] : null);
                var o = item == null || !order.IsPrefab ? order : new Order(order, item, order.GetTargetItemComponent(item));
                var offsets = MathUtils.GetPointsOnCircumference(Vector2.Zero, nodeDistance,
                    GetCircumferencePointCount(order.Options.Length),
                    GetFirstNodeAngle(order.Options.Length));
                var offsetIndex = 0;
                for (int i = 0; i < order.Options.Length; i++)
                {
                    optionNodes.Add(new Tuple<GUIComponent, Keys>(
                        CreateOrderOptionNode(nodeSize, commandFrame.RectTransform, offsets[offsetIndex++].ToPoint(), o, order.Options[i], order.GetOptionName(i), (i + 1) % 10),
                        Keys.D0 + (i + 1) % 10));
                }
            }
        }

        private GUIButton CreateOrderOptionNode(Point size, RectTransform parent, Point offset, Order order, string option, string optionName, int hotkey)
        {
            var node = new GUIButton(new RectTransform(size, parent: parent, anchor: Anchor.Center), style: null)
            {
                UserData = new Tuple<Order, string>(order, option),
                OnClicked = (_, userData) =>
                {
                    if (!CanIssueOrders) { return false; }
                    var o = userData as Tuple<Order, string>;
                    SetCharacterOrder(characterContext ?? GetCharacterForQuickAssignment(o.Item1), o.Item1, o.Item2, CharacterInfo.HighestManualOrderPriority, Character.Controlled);
                    DisableCommandUI();
                    return true;
                }
            };
            if (CanOpenManualAssignment(node))
            {
                node.OnSecondaryClicked = (button, _) => CreateAssignmentNodes(button);
            }
            node.RectTransform.MoveOverTime(offset, CommandNodeAnimDuration);

            GUIImage icon = null;
            if (order.Prefab.OptionSprites.TryGetValue(option, out Sprite sprite))
            {
                var showAssignmentTooltip = characterContext == null && !order.MustManuallyAssign && !order.TargetAllCharacters;
                icon = CreateNodeIcon(Vector2.One, node.RectTransform, sprite, order.Color,
                    tooltip: characterContext != null ? optionName : optionName +
                        "\n" + (!PlayerInput.MouseButtonsSwapped() ? TextManager.Get("input.leftmouse") : TextManager.Get("input.rightmouse")) + ": " + TextManager.Get("commandui.quickassigntooltip") +
                        "\n" + (!PlayerInput.MouseButtonsSwapped() ? TextManager.Get("input.rightmouse") : TextManager.Get("input.leftmouse")) + ": " + TextManager.Get("commandui.manualassigntooltip"));
            }
            if (!CanSomeoneHearCharacter())
            {
                node.CanBeFocused = false;
                if (icon != null) { icon.CanBeFocused = false; }
                CreateBlockIcon(node.RectTransform);
            }
            else if (hotkey >= 0)
            {
                CreateHotkeyIcon(node.RectTransform, hotkey);
            }
            return node;
        }

        private bool CreateAssignmentNodes(GUIComponent node)
        {
            var order = (node.UserData is Order) ?
                new Tuple<Order, string>(node.UserData as Order, null) :
                node.UserData as Tuple<Order, string>;
            var characters = GetCharactersForManualAssignment(order.Item1);
            if (characters.None()) { return false; }

            if (!(optionNodes.Find(n => n.Item1 == node) is Tuple<GUIComponent, Keys> optionNode) || !optionNodes.Remove(optionNode))
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
                if (string.IsNullOrEmpty(order.Item2))
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
                    if (order.Item1.Prefab.OptionSprites.TryGetValue(order.Item2, out Sprite sprite))
                    {
                        CreateNodeIcon(Vector2.One, clickedOptionNode.RectTransform, sprite, order.Item1.Color, tooltip: order.Item2);
                    }
                    SetCenterNode(clickedOptionNode);
                    node = null;
                }
                targetFrame.Visible = false;
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
            CreateNodeIcon(expandNode.RectTransform, "CommandExpandNode", order.Item1.Color, tooltip: TextManager.Get("commandui.expand"));

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
                CreateAssignmentNode(userData as Tuple<Order, string>, extraOptionCharacters[i], offsets[i].ToPoint(), -1, nameLabelScale: 1.15f);
            }
            return true;
        }

        private void CreateAssignmentNode(Tuple<Order, string> order, Character character, Point offset, int hotkey, float nameLabelScale = 1f)
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
                    SetCharacterOrder(userData as Character, order.Item1, order.Item2, CharacterInfo.HighestManualOrderPriority, Character.Controlled);
                    DisableCommandUI();
                    return true;
                }
            };
            node.RectTransform.MoveOverTime(offset, CommandNodeAnimDuration);

            var jobColor = character.Info?.Job?.Prefab?.UIColor ?? Color.White;

            // Order icon
            var topOrderInfo = character.GetCurrentOrderWithTopPriority();
            GUIImage orderIcon;
            if (topOrderInfo.HasValue)
            {
                orderIcon = new GUIImage(new RectTransform(new Vector2(1.2f), node.RectTransform, anchor: Anchor.Center), topOrderInfo.Value.Order.SymbolSprite, scaleToFit: true);
                var tooltip = topOrderInfo.Value.Order.Name;
                if (!string.IsNullOrWhiteSpace(topOrderInfo.Value.OrderOption)) { tooltip += " (" + topOrderInfo.Value.Order.GetOptionName(topOrderInfo.Value.OrderOption) + ")"; };
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
            var font = GUI.SmallFont;
            var nameLabel = new GUITextBlock(
                new RectTransform(new Point(width, 0), parent: node.RectTransform, anchor: Anchor.TopCenter, pivot: Pivot.BottomCenter)
                {
                    RelativeOffset = new Vector2(0f, -0.25f)
                },
                ToolBox.LimitString(character.Info?.DisplayName, font, width), textColor: jobColor * nodeColorMultiplier, font: font, textAlignment: Alignment.Center, style: null)
            {
                CanBeFocused = false,
                ForceUpperCase = true,
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
                CreateBlockIcon(node.RectTransform);
            }
            if (hotkey >= 0)
            {
                if (canHear) { CreateHotkeyIcon(node.RectTransform, hotkey); }
                optionNodes.Add(new Tuple<GUIComponent, Keys>(node, canHear ? Keys.D0 + hotkey : Keys.None));
            }
            else
            {
                extraOptionNodes.Add(node);
            }
        }

        private GUIImage CreateNodeIcon(Vector2 relativeSize, RectTransform parent, Sprite sprite, Color color, string tooltip = null)
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
        private GUIImage CreateNodeIcon(Point absoluteSize, RectTransform parent, Sprite sprite, Color color, string tooltip = null)
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

        private void CreateNodeIcon(RectTransform parent, string style, Color? color = null, string tooltip = null)
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

        private void CreateBlockIcon(RectTransform parent)
        {
            new GUIImage(new RectTransform(new Vector2(0.9f), parent, anchor: Anchor.Center), cancelIcon, scaleToFit: true)
            {
                CanBeFocused = false,
                Color = GUI.Style.Red * nodeColorMultiplier,
                HoverColor = GUI.Style.Red
            };
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
            var tooltip = character?.Info != null ? characterContext.Info.DisplayName : null;
            if (string.IsNullOrWhiteSpace(tooltip)) { component.ToolTip = tooltip; return; }
            if (character.Info?.Job != null && !string.IsNullOrWhiteSpace(characterContext.Info.Job.Name)) { tooltip += " (" + characterContext.Info.Job.Name + ")"; }
            component.ToolTip = tooltip;
        }

        private string GetOrderNameBasedOnContextuality(Order order)
        {
            if (order == null) { return ""; }
            if (isContextual) { return order.ContextualName; }
            return order.Name;
        }

        #region Crew Member Assignment Logic
        private bool CanOpenManualAssignment(GUIComponent node)
        {
            if (node == null || characterContext != null) { return false; }
            if (node.UserData is Tuple<Order, string> orderInfo)
            {
                return !orderInfo.Item1.TargetAllCharacters;
            }
            if (node.UserData is Order order)
            {
                return !order.TargetAllCharacters && !order.HasOptions &&
                    (!order.MustSetTarget || itemContext != null ||
                     order.GetMatchingItems(GetTargetSubmarine(), true, interactableFor: Character.Controlled).Count < 2);
            }
            return false;
        }

        private Character GetCharacterForQuickAssignment(Order order)
        {
            var controllingCharacter = Character.Controlled != null;
#if !DEBUG
            if (!controllingCharacter) { return null; }
#endif
            if (order.Category == OrderCategory.Operate && HumanAIController.IsItemOperatedByAnother(null, order.TargetItemComponent, out Character operatingCharacter) &&
                (!controllingCharacter || operatingCharacter.CanHearCharacter(Character.Controlled)))
            {
                return operatingCharacter;
            }
            return GetCharactersSortedForOrder(order, false).FirstOrDefault(c => !controllingCharacter || c.CanHearCharacter(Character.Controlled)) ?? Character.Controlled;
        }

        private List<Character> GetCharactersForManualAssignment(Order order)
        {
#if !DEBUG
            if (Character.Controlled == null) { return new List<Character>(); }
#endif
            if (order.Identifier == dismissedOrderPrefab.Identifier)
            {
                return characters.FindAll(c => !c.IsDismissed).OrderBy(c => c.Info.DisplayName).ToList();
            }
            return GetCharactersSortedForOrder(order, order.Identifier != "follow").ToList();
        }

        private IEnumerable<Character> GetCharactersSortedForOrder(Order order, bool includeSelf)
        {
            return characters.FindAll(c => Character.Controlled == null || ((includeSelf || c != Character.Controlled) && c.TeamID == Character.Controlled.TeamID))
                    // 1. Prioritize those who are on the same submarine than the controlled character
                    .OrderByDescending(c => Character.Controlled == null || c.Submarine == Character.Controlled.Submarine)
                    // 2. Prioritize those who are already ordered to operate the item target of the new 'operate' order, or given the same maintenance order as now issued
                    .ThenByDescending(c => c.CurrentOrders.Any(o =>
                        o.Order != null && o.Order.Identifier == order.Identifier &&
                        (order.Category == OrderCategory.Maintenance || (order.Category == OrderCategory.Operate && o.Order.TargetSpatialEntity == order.TargetSpatialEntity))))
                    // 3. Prioritize those with the appropriate job for the order
                    .ThenByDescending(c => order.HasAppropriateJob(c))
                    // 4. Prioritize bots over player controlled characters
                    .ThenByDescending(c => c.IsBot)
                    // 5. Use the priority value of the current objective
                    .ThenBy(c => c.AIController is HumanAIController humanAI ? humanAI.ObjectiveManager.CurrentObjective?.Priority : 0)
                    // 6. Prioritize those with the best skill for the order
                    .ThenByDescending(c => c.GetSkillLevel(order.AppropriateSkill));
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
                WifiComponent radio = GetHeadset(Character.Controlled, true);
                canIssueOrders = 
                    radio != null && 
                    radio.CanTransmit() && 
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

                bool hasIntruders = Character.CharacterList.Any(c => c.CurrentHull == Character.Controlled.CurrentHull && AIObjectiveFightIntruders.IsValidTarget(c, Character.Controlled));
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
            Order order = Order.GetPrefab(orderIdentifier);
            var reportButton = ReportButtonFrame.GetChildByUserData(order);
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

        public void EndRound()
        {
            //remove characterinfos whose characters have been removed or killed
            characterInfos.RemoveAll(c => c.Character == null || c.Character.Removed || c.CauseOfDeath != null);

            characters.Clear();
            crewList.ClearChildren();
            GUIContextMenu.CurrentContextMenu = null;
        }

        public void Reset()
        {
            characters.Clear();
            characterInfos.Clear();
            crewList.ClearChildren();
        }

        public void Save(XElement parentElement)
        {
            XElement element = new XElement("crew");

            foreach (CharacterInfo ci in characterInfos)
            {
                var infoElement = ci.Save(element);
                if (ci.InventoryData != null) { infoElement.Add(ci.InventoryData); }
                if (ci.HealthData != null) { infoElement.Add(ci.HealthData); }
                if (ci.LastControlled) { infoElement.Add(new XAttribute("lastcontrolled", true)); }
            }
            parentElement.Add(element);
        }
    }
}
