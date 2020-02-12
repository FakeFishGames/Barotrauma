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
        /// <summary>
        /// How long the previously selected character waits doing nothing when switching to another character. Only affects idling.
        /// </summary>
        const float CharacterWaitOnSwitch = 10.0f;

        private readonly List<CharacterInfo> characterInfos = new List<CharacterInfo>();
        private readonly List<Character> characters = new List<Character>();

        private Point screenResolution;

        #region UI

        public GUIComponent ReportButtonFrame { get; set; }

        private GUIFrame guiFrame;
        private GUIFrame crewArea;
        private GUIListBox crewList;
        private GUIButton commandButton, toggleCrewButton;
        private float crewListOpenState;
        private bool toggleCrewListOpen = true;

        /// <summary>
        /// Present only in single player games. In multiplayer. The chatbox is found from GameSession.Client.
        /// </summary>
        public ChatBox ChatBox { get; private set; }

        private float prevUIScale;

        public bool AllowCharacterSwitch = true;

        public bool ToggleCrewListOpen
        {
            get { return toggleCrewListOpen; }
            set
            {
                if (toggleCrewListOpen == value) { return; }
                toggleCrewListOpen = GameMain.Config.CrewMenuOpen = value;
                toggleCrewButton.Children.ForEach(c => c.SpriteEffects = toggleCrewListOpen ? SpriteEffects.None : SpriteEffects.FlipHorizontally);
            }
        }

        const float CommandNodeAnimDuration = 0.2f;

        public List<GUIButton> OrderOptionButtons = new List<GUIButton>();

        private Sprite jobIndicatorBackground, previousOrderArrow, cancelIcon;

        #endregion

        #region Constructors

        public CrewManager(XElement element, bool isSinglePlayer)
            : this(isSinglePlayer)
        {
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "character") continue;

                var characterInfo = new CharacterInfo(subElement);
                characterInfos.Add(characterInfo);
                foreach (XElement invElement in subElement.Elements())
                {
                    if (invElement.Name.ToString().ToLowerInvariant() != "inventory") continue;
                    characterInfo.InventoryData = invElement;
                    break;
                }
            }
        }

        partial void InitProjectSpecific()
        {
            guiFrame = new GUIFrame(new RectTransform(Vector2.One, GUICanvas.Instance), null, Color.Transparent)
            {
                CanBeFocused = false
            };

            var crewAreaWithButtons = new GUIFrame(
                HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.CrewArea, guiFrame.RectTransform),
                style: null,
                color: Color.Transparent)
            {
                CanBeFocused = false
            };

            var buttonHeight = (int)(GUI.Scale * 40);

            crewArea = new GUIFrame(
                new RectTransform(
                    new Point(crewAreaWithButtons.Rect.Width, crewAreaWithButtons.Rect.Height - (int)(1.5f * buttonHeight) - 2 * HUDLayoutSettings.Padding),
                    crewAreaWithButtons.RectTransform,
                    Anchor.BottomLeft),
                style: null,
                color: Color.Transparent)
            {
                CanBeFocused = false
            };

            var buttonSize = new Point((int)(182.0f / 99.0f * buttonHeight), buttonHeight);
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
                OnSelected = (component, userData) => false,
                SelectMultiple = false,
                Spacing = (int)(GUI.Scale * 10)
            };

            toggleCrewButton = new GUIButton(
                new RectTransform(
                    new Point(buttonSize.X, (int)(0.5f * buttonHeight)),
                    parent: crewAreaWithButtons.RectTransform)
                {
                    AbsoluteOffset = new Point(0, buttonHeight + HUDLayoutSettings.Padding)
                },
                style: "UIToggleButton")
            {
                OnClicked = (GUIButton btn, object userdata) =>
                {
                    ToggleCrewListOpen = !ToggleCrewListOpen;
                    return true;
                }
            };

            jobIndicatorBackground = new Sprite("Content/UI/CommandUIAtlas.png", new Rectangle(0, 512, 128, 128));
            previousOrderArrow = new Sprite("Content/UI/CommandUIAtlas.png", new Rectangle(128, 512, 128, 128));
            cancelIcon = new Sprite("Content/UI/CommandUIAtlas.png", new Rectangle(512, 384, 128, 128));

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
                                headset.TransmitSignal(stepsTaken: 0, signal: msg, source: headset.Item, sender: Character.Controlled, sendToChat: false);
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
            chatBox.ToggleButton = new GUIButton(new RectTransform(new Point((int)(182f * GUI.Scale * 0.4f), (int)(99f * GUI.Scale * 0.4f)), guiFrame.RectTransform), style: "ChatToggleButton");
            chatBox.ToggleButton.RectTransform.AbsoluteOffset = new Point(0, HUDLayoutSettings.ChatBoxArea.Height - chatBox.ToggleButton.Rect.Height);
            chatBox.ToggleButton.OnClicked += (GUIButton btn, object userdata) =>
            {
                chatBox.ToggleOpen = !chatBox.ToggleOpen;
                return true;
            };

            var reports = Order.PrefabList.FindAll(o => o.TargetAllCharacters && o.SymbolSprite != null);
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
                if (!order.TargetAllCharacters || order.SymbolSprite == null) { continue; }
                var btn = new GUIButton(new RectTransform(new Point(ReportButtonFrame.Rect.Width), ReportButtonFrame.RectTransform), style: null)
                {
                    OnClicked = (GUIButton button, object userData) =>
                    {
                        if (!CanIssueOrders) { return false; }
                        SetCharacterOrder(null, order, null, Character.Controlled);
                        var visibleHulls = new List<Hull>(Character.Controlled.GetVisibleHulls());
                        foreach (var hull in visibleHulls)
                        {
                            HumanAIController.PropagateHullSafety(Character.Controlled, hull);
                            HumanAIController.RefreshTargets(Character.Controlled, order, hull);
                        }
                        return true;
                    },
                    UserData = order,
                    ToolTip = order.Name
                };

                new GUIFrame(new RectTransform(new Vector2(1.5f), btn.RectTransform, Anchor.Center), "InnerGlowCircular")
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
            ToggleCrewListOpen = GameMain.Config.CrewMenuOpen;
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

        public void AddCharacter(Character character)
        {
            if (character.Removed)
            {
                DebugConsole.ThrowError("Tried to add a removed character to CrewManager!\n" + Environment.StackTrace);
                return;
            }
            if (character.IsDead)
            {
                DebugConsole.ThrowError("Tried to add a dead character to CrewManager!\n" + Environment.StackTrace);
                return;
            }

            if (!characters.Contains(character))
            {
                characters.Add(character);
            }
            if (!characterInfos.Contains(character.Info))
            {
                characterInfos.Add(character.Info);
            }

            AddCharacterToCrewList(character);

            if (character is AICharacter)
            {
                var ai = character.AIController as HumanAIController;
                if (ai == null)
                {
                    DebugConsole.ThrowError("Error in crewmanager - attempted to give orders to a character with no HumanAIController");
                    return;
                }
                character.SetOrder(ai.CurrentOrder, "", null, false);
            }
        }

        public void AddCharacterInfo(CharacterInfo characterInfo)
        {
            if (characterInfos.Contains(characterInfo))
            {
                DebugConsole.ThrowError("Tried to add the same character info to CrewManager twice.\n" + Environment.StackTrace);
                return;
            }

            characterInfos.Add(characterInfo);
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
                DebugConsole.ThrowError("Tried to remove a null character from CrewManager.\n" + Environment.StackTrace);
                return;
            }
            characters.Remove(character);
            if (removeInfo) { characterInfos.Remove(character.Info); }
        }

        /// <summary>
        /// Remove info of a selected character. The character will not be visible in any menus or the round summary.
        /// </summary>
        /// <param name="characterInfo"></param>
        public void RemoveCharacterInfo(CharacterInfo characterInfo)
        {
            characterInfos.Remove(characterInfo);
        }

        private void AddCharacterToCrewList(Character character)
        {
            if (character == null) { return; }

            int width = crewList.Content.Rect.Width - HUDLayoutSettings.Padding;
            int height = Math.Max(32, (int)((1.0f / 8.0f) * width));
            var background = new GUIFrame(
                new RectTransform(new Point(width, height), parent: crewList.Content.RectTransform, anchor: Anchor.TopRight),
                style: "CrewListBackground")
            {
                UserData = character
            };

            var iconRelativeWidth = (float)height / background.Rect.Width;

            var layoutGroup = new GUILayoutGroup(
                new RectTransform(Vector2.One, parent: background.RectTransform),
                isHorizontal: true,
                childAnchor: Anchor.CenterLeft)
            {
                CanBeFocused = false,
                RelativeSpacing = 0.1f * iconRelativeWidth,
                UserData = character
            };

            // "Padding" to prevent member-specific command button from overlapping job indicator
            var commandButtonAbsoluteHeight = Math.Min(40.0f, 0.67f * background.Rect.Height);
            var paddingRelativeWidth = 0.35f * commandButtonAbsoluteHeight / background.Rect.Width;
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

            var nameRelativeWidth = 1.0f - paddingRelativeWidth - 3.7f * iconRelativeWidth;
            var font = layoutGroup.Rect.Width < 150 ? GUI.SmallFont : GUI.Font;
            var nameBlock = new GUITextBlock(
                new RectTransform(
                    new Vector2(nameRelativeWidth, 1.0f),
                    layoutGroup.RectTransform)
                {
                    MaxSize = new Point(150, background.Rect.Height)
                },
                ToolBox.LimitString(character.Name, font, (int)(nameRelativeWidth * layoutGroup.Rect.Width)),
                font: font,
                textColor: character.Info?.Job?.Prefab?.UIColor)
            {
                CanBeFocused = false
            };

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
                    characterButton.TooltipColorData = new List<ColorData>() { new ColorData()
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
                new RectTransform(new Vector2(0.5f * iconRelativeWidth, 0.5f), layoutGroup.RectTransform),
                style: "VerticalLine")
            {
                CanBeFocused = false
            };

            var soundIcons = new GUIFrame(new RectTransform(new Vector2(0.8f * iconRelativeWidth, 0.8f), layoutGroup.RectTransform), style: null)
            {
                CanBeFocused = false,
                UserData = "soundicons"
            };
            new GUIImage(
                new RectTransform(Vector2.One, soundIcons.RectTransform),
                GUI.Style.GetComponentStyle("GUISoundIcon").Sprites[GUIComponent.ComponentState.None].FirstOrDefault().Sprite,
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
                DebugConsole.ThrowError("Cannot add messages to single player chat box in multiplayer mode!\n" + Environment.StackTrace);
                return;
            }
            if (string.IsNullOrEmpty(text)) { return; }

            if (sender != null)
            {
                GameMain.GameSession.CrewManager.SetCharacterSpeaking(sender);
            }
            ChatBox.AddMessage(ChatMessage.Create(senderName, text, messageType, sender));
        }

        private WifiComponent GetHeadset(Character character, bool requireEquipped)
        {
            if (character?.Inventory == null) return null;

            var radioItem = character.Inventory.Items.FirstOrDefault(it => it != null && it.GetComponent<WifiComponent>() != null);
            if (radioItem == null) return null;
            if (requireEquipped && !character.HasEquippedItem(radioItem)) return null;

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

        /// <summary>
        /// Sets the character's current order (if it's close enough to receive messages from orderGiver) and
        /// displays the order in the crew UI
        /// </summary>
        public void SetCharacterOrder(Character character, Order order, string option, Character orderGiver)
        {
            if (order != null && order.TargetAllCharacters)
            {
                if (orderGiver == null || orderGiver.CurrentHull == null) { return; }
                var hull = orderGiver.CurrentHull;
                AddOrder(new Order(order.Prefab, hull, null, orderGiver), order.Prefab.FadeOutTime);
                if (IsSinglePlayer)
                {
                    orderGiver.Speak(
                        order.GetChatMessage("", hull.DisplayName, givingOrderToSelf: character == orderGiver), ChatMessageType.Order);
                }
                else
                {
                    OrderChatMessage msg = new OrderChatMessage(order, "", hull, null, orderGiver);
                    GameMain.Client?.SendChatMessage(msg);
                }
            }
            else
            {
                if (character == null)
                {
                    //can't issue an order if no characters are available
                    return;
                }

                if (IsSinglePlayer)
                {
                    character.SetOrder(order, option, orderGiver, speak: orderGiver != character);
                    orderGiver?.Speak(
                        order.GetChatMessage(character.Name, orderGiver.CurrentHull?.DisplayName, givingOrderToSelf: character == orderGiver, orderOption: option), null);
                }
                else if (orderGiver != null)
                {
                    OrderChatMessage msg = new OrderChatMessage(order, option, order?.TargetItemComponent?.Item, character, orderGiver);
                    GameMain.Client?.SendChatMessage(msg);
                }
            }
        }

        /// <summary>
        /// Displays the specified order in the crew UI next to the character.
        /// </summary>
        public void DisplayCharacterOrder(Character character, Order order, string option)
        {
            if (character == null) { return; }

            var characterFrame = crewList.Content.GetChildByUserData(character);

            if (characterFrame == null) { return; }

            GUILayoutGroup layoutGroup = (GUILayoutGroup)characterFrame.FindChild(c => c is GUILayoutGroup);
            layoutGroup.RemoveChild(GetPreviousOrderComponent(layoutGroup));
            var currentOrderComponent = GetCurrentOrderComponent(layoutGroup);

            if (order != null && currentOrderComponent != null)
            {
                var currentOrderInfo = (OrderInfo)currentOrderComponent.UserData;

                if (order.Identifier == currentOrderInfo.Order.Identifier &&
                    option == currentOrderInfo.OrderOption &&
                    order.TargetEntity == currentOrderInfo.Order.TargetEntity) { return; }

                DisplayPreviousCharacterOrder(character, layoutGroup, currentOrderInfo);
            }

            layoutGroup.RemoveChild(currentOrderComponent);

            if (order == null || order.Identifier == dismissedOrderPrefab.Identifier) { return; }

            var orderFrame = new GUIButton(
                new RectTransform(
                    layoutGroup.GetChildByUserData("job").RectTransform.RelativeSize,
                    layoutGroup.RectTransform),
                style: null)
            {
                UserData = new OrderInfo(order, option),
                OnClicked = (button, userData) =>
                {
                    if (!CanIssueOrders) { return false; }
                    SetCharacterOrder(character, dismissedOrderPrefab, null, Character.Controlled);
                    return true;
                }
            };
            CreateNodeIcon(orderFrame.RectTransform, order.SymbolSprite, order.Color, tooltip: order.Name);
            new GUIImage(
                new RectTransform(Vector2.One, orderFrame.RectTransform),
                cancelIcon,
                scaleToFit: true)
            {
                CanBeFocused = false,
                UserData = "cancel",
                Visible = false
            };
            orderFrame.RectTransform.RepositionChildInHierarchy(4);
            characterFrame.SetAsFirstChild();
        }

        private void DisplayPreviousCharacterOrder(Character character, GUILayoutGroup characterComponent, OrderInfo currentOrderInfo)
        {
            if (currentOrderInfo.Order == null || currentOrderInfo.Order.Identifier == dismissedOrderPrefab.Identifier) { return; }

            var previousOrderInfo = new OrderInfo(currentOrderInfo);
            var prevOrderFrame = new GUIButton(
                new RectTransform(
                    characterComponent.GetChildByUserData("job").RectTransform.RelativeSize,
                    characterComponent.RectTransform),
                style: null)
            {
                UserData = previousOrderInfo, 
                OnClicked = (button, userData) =>
                {
                    if (!CanIssueOrders) { return false; }
                    var orderInfo = (OrderInfo)userData;
                    SetCharacterOrder(character, orderInfo.Order, orderInfo.OrderOption, Character.Controlled);
                    return true;
                }
            };

            var prevOrderIconFrame = new GUIFrame(
                new RectTransform(new Vector2(0.8f), prevOrderFrame.RectTransform, anchor: Anchor.BottomLeft),
                style: null);
            CreateNodeIcon(
                prevOrderIconFrame.RectTransform,
                previousOrderInfo.Order.SymbolSprite,
                previousOrderInfo.Order.Color,
                tooltip: previousOrderInfo.Order.Name);
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
            prevOrderFrame.RectTransform.RepositionChildInHierarchy(GetCurrentOrderComponent(characterComponent) != null ? 5 : 4);
        }

        private GUIComponent GetCurrentOrderComponent(GUILayoutGroup characterComponent)
        {
            return characterComponent.FindChild(c => c.UserData is OrderInfo orderInfo && orderInfo.ComponentIdentifier == "currentorder");
        }

        private GUIComponent GetPreviousOrderComponent(GUILayoutGroup characterComponent)
        {
            return characterComponent.FindChild(c => c.UserData is OrderInfo orderInfo && orderInfo.ComponentIdentifier == "previousorder");
        }

        private struct OrderInfo
        {
            public string ComponentIdentifier { get; set; }
            public Order Order { get; private set; }
            public string OrderOption { get; private set; }

            public OrderInfo(Order order, string orderOption)
            {
                ComponentIdentifier = "currentorder";
                Order = order;
                OrderOption = orderOption;
            }

            public OrderInfo(OrderInfo orderInfo)
            {
                ComponentIdentifier = "previousorder";
                Order = orderInfo.Order;
                OrderOption = orderInfo.OrderOption;
            }
        }

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

            commandFrame?.AddToGUIUpdateList();

            if (GUI.DisableUpperHUD) { return; }

            if (GameMain.GraphicsWidth != screenResolution.X || GameMain.GraphicsHeight != screenResolution.Y || prevUIScale != GUI.Scale)
            {
                var previousCrewList = crewList;
                InitProjectSpecific();

                foreach (GUIComponent c in previousCrewList.Content.Children)
                {
                    if (!(c.UserData is Character character) || character.IsDead || character.Removed) { continue; }
                    AddCharacter(character);
                    DisplayCharacterOrder(character, character.CurrentOrder, (character.AIController as HumanAIController)?.CurrentOrderOption);
                }
            }

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
            if (!AllowCharacterSwitch) { return; }
            //make the previously selected character wait in place for some time
            //(so they don't immediately start idling and walking away from their station)
            if (Character.Controlled?.AIController?.ObjectiveManager != null)
            {
                Character.Controlled.AIController.ObjectiveManager.WaitTimer = CharacterWaitOnSwitch;
            }
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
                commandFrame == null && !clicklessSelectionActive && CanIssueOrders)
            {
                CreateCommandUI(GUI.MouseOn?.UserData as Character);
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

                if (!CanIssueOrders)
                {
                    DisableCommandUI();
                }
                else if (PlayerInput.SecondaryMouseButtonClicked() && characterContext == null &&
                         (optionNodes.Any(n => GUI.IsMouseOn(n.Item1)) || shortcutNodes.Any(n => GUI.IsMouseOn(n))))
                {
                    var node = optionNodes.Find(n => GUI.IsMouseOn(n.Item1))?.Item1;
                    if (node == null)
                    {
                        node = shortcutNodes.Find(n => GUI.IsMouseOn(n));
                    }
                    // Make sure the node is for an option-less order...
                    if (node.UserData is Order order &&
                        !(order.ItemComponentType != null || order.ItemIdentifiers.Length > 0 || order.Options.Length > 1))
                    {
                        CreateAssignmentNodes(node);
                    }
                    // ...or an order option
                    else if (node.UserData is Tuple<Order, string>)
                    {
                        CreateAssignmentNodes(node);
                    }
                }
                // TODO: Consider using HUD.CloseHUD() instead of KeyHit(Escape), the former method is also used for health UI
                else if ((PlayerInput.KeyHit(InputType.Command) && selectedNode == null && !clicklessSelectionActive) ||
                         PlayerInput.KeyHit(InputType.Deselect) || PlayerInput.KeyHit(Keys.Escape))
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

                        if (closestNode == selectedNode)
                        {
                            timeSelected += deltaTime;
                            if (timeSelected >= selectionTime)
                            {
                                selectedNode.OnClicked?.Invoke(selectedNode, selectedNode.UserData);
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
                        (node.Item1 as GUIButton)?.OnClicked?.Invoke(node.Item1 as GUIButton, node.Item1.UserData);
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

            if (GUI.DisableUpperHUD) { return; }

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
                        ChatBox.CloseAfterMessageSent = !ChatBox.ToggleOpen;
                        ChatBox.ToggleOpen = true;
                        ChatBox.InputBox.Select(ChatBox.InputBox.Text.Length);
                    }

                    if (PlayerInput.KeyHit(InputType.RadioChat) && !ChatBox.InputBox.Selected)
                    {
                        ChatBox.InputBox.AddToGUIUpdateList();
                        ChatBox.GUIFrame.Flash(Color.YellowGreen, 0.5f);
                        ChatBox.CloseAfterMessageSent = !ChatBox.ToggleOpen;
                        ChatBox.ToggleOpen = true;

                        if (!ChatBox.InputBox.Text.StartsWith(ChatBox.RadioChatString))
                        {
                            ChatBox.InputBox.Text = ChatBox.RadioChatString;
                        }
                        ChatBox.InputBox.Select(ChatBox.InputBox.Text.Length);
                    }
                }
            }

            crewArea.Visible = characters.Count > 0 && CharacterHealth.OpenHealthWindow == null;

            foreach (GUIComponent child in crewList.Content.Children)
            {
                if (child.UserData is Character character)
                {
                    child.Visible = Character.Controlled == null || Character.Controlled.TeamID == character.TeamID;
                    if (child.Visible)
                    {
                        if (character == Character.Controlled && child.State != GUIComponent.ComponentState.Selected)
                        {
                            crewList.Select(character, force: true);
                        }
                        if (child.FindChild(c => c is GUILayoutGroup) is GUILayoutGroup layoutGroup)
                        {
                            if (GetCurrentOrderComponent(layoutGroup) is GUIComponent orderButton &&
                            orderButton.GetChildByUserData("colorsource") is GUIComponent orderIcon &&
                            orderButton.GetChildByUserData("cancel") is GUIComponent cancelIcon)
                            {
                                cancelIcon.Visible = GUI.IsMouseOn(orderIcon);
                            }
                            if (layoutGroup.GetChildByUserData("soundicons")?
                                    .FindChild(c => c.UserData is Pair<string, float> pair && pair.First == "soundicon") is GUIImage soundIcon)
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
            crewListOpenState = ToggleCrewListOpen ?
                Math.Min(crewListOpenState + deltaTime * 2.0f, 1.0f) :
                Math.Max(crewListOpenState - deltaTime * 2.0f, 0.0f);

            if (GUI.KeyboardDispatcher.Subscriber == null && PlayerInput.KeyHit(InputType.CrewOrders))
            {
                ToggleCrewListOpen = !ToggleCrewListOpen;
            }

            UpdateReports();
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
        private List<Tuple<GUIComponent, Keys>> optionNodes = new List<Tuple<GUIComponent, Keys>>();
        private Keys returnNodeHotkey = Keys.None, expandNodeHotkey = Keys.None;
        private List<GUIComponent> shortcutNodes = new List<GUIComponent>();
        private List<GUIComponent> extraOptionNodes = new List<GUIComponent>();
        private GUICustomComponent nodeConnectors;
        private GUIImage background;

        private GUIButton selectedNode;
        private float selectionTime = 0.75f, timeSelected = 0.0f;
        private bool clicklessSelectionActive, isOpeningClick, isSelectionHighlighted;

        private Point centerNodeSize, nodeSize, shortcutCenterNodeSize, shortcutNodeSize, returnNodeSize;
        private float centerNodeMargin, optionNodeMargin, shortcutCenterNodeMargin, shortcutNodeMargin, returnNodeMargin;

        private List<OrderCategory> availableCategories;
        private Stack<GUIButton> historyNodes = new Stack<GUIButton>();
        private List<Character> extraOptionCharacters = new List<Character>();

        /// <summary>
        /// node.Color = node.HighlightColor * nodeColorMultiplier
        /// </summary>
        private const float nodeColorMultiplier = 0.75f;
        private const int assignmentNodeMaxCount = 8;
        private int nodeDistance = (int)(GUI.Scale * 250);
        private float returnNodeDistanceModifier = 0.65f;
        private Order dismissedOrderPrefab;
        private Character characterContext;
        private Point shorcutCenterNodeOffset;
        private bool WasCommandInterfaceDisabledThisUpdate { get; set; }
        private bool CanIssueOrders
        {
            get
            {
                return Character.Controlled != null && Character.Controlled.SpeechImpediment < 100.0f;
            }
        }

        private bool CanSomeoneHearCharacter()
        {
            return Character.Controlled != null && characters.Any(c => c != Character.Controlled && c.CanHearCharacter(Character.Controlled));
        }

        private void CreateCommandUI(Character characterContext = null)
        {
            if (commandFrame != null) { DisableCommandUI(); }
            CharacterHealth.OpenHealthWindow = null;
            ScaleCommandUI();
            commandFrame = new GUIFrame(
                new RectTransform(Vector2.One, GUICanvas.Instance, anchor: Anchor.Center),
                style: null,
                color: Color.Transparent);
            background = new GUIImage(
                new RectTransform(Vector2.One, commandFrame.RectTransform, anchor: Anchor.Center),
                "CommandBackground");
            background.Color = background.Color * 0.8f;

            this.characterContext = characterContext;
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
                    Color = characterContext.Info.Job.Prefab.UIColor * nodeColorMultiplier,
                    HoverColor = characterContext.Info.Job.Prefab.UIColor,
                    UserData = "colorsource"
                };
                // Character icon
                new GUICustomComponent(
                    new RectTransform(Vector2.One, startNode.RectTransform, anchor: Anchor.Center),
                    (spriteBatch, _) =>
                    {
                        characterContext.Info.DrawIcon(spriteBatch, startNode.Center, startNode.Rect.Size.ToVector2() * 0.6f);
                    })
                {
                    ToolTip = characterContext.Info.DisplayName + " (" + characterContext.Info.Job.Name + ")"
                };
            }
            SetCenterNode(startNode);

            availableCategories ??= GetAvailableCategories();
            dismissedOrderPrefab ??= Order.GetPrefab("dismissed");

            CreateShortcutNodes();
            CreateOrderCategoryNodes();
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
            centerNodeSize = new Point((int)(100 * GUI.Scale));
            nodeSize = new Point((int)(100 * GUI.Scale));
            shortcutCenterNodeSize = new Point((int)(48 * GUI.Scale));
            shortcutNodeSize = new Point((int)(64 * GUI.Scale));
            returnNodeSize = new Point((int)(48 * GUI.Scale));
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
                if (category == OrderCategory.Undefined) { continue; }
                if (Order.PrefabList.Any(o => o.Category == category && !o.TargetAllCharacters))
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
            if (targetFrame == null || !targetFrame.Visible)
            {
                optionNodes.ForEach(n => DrawNodeConnector(startNodePos, centerNodeMargin, n.Item1, optionNodeMargin, spriteBatch));
            }
            DrawNodeConnector(startNodePos, centerNodeMargin, returnNode, returnNodeMargin, spriteBatch);
            DrawNodeConnector(startNodePos, centerNodeMargin, expandNode, optionNodeMargin, spriteBatch);
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
            SetReturnNode(centerNode, new Point(
                (int)(node.RectTransform.AbsoluteOffset.X * -returnNodeDistanceModifier),
                (int)(node.RectTransform.AbsoluteOffset.Y * -returnNodeDistanceModifier)));
            SetCenterNode(node);
            if (shortcutCenterNode != null)
            {
                commandFrame.RemoveChild(shortcutCenterNode);
                shortcutCenterNode = null;
            }
            CreateNodes(userData);
            if (returnNode != null && returnNode.Visible)
            {
                var hotkey = optionNodes.Count + 1;
                if (expandNode != null && expandNode.Visible) { hotkey += 1; }
                CreateHotkeyIcon(returnNode.RectTransform, hotkey % 10, true);
                returnNodeHotkey = Keys.D0 + hotkey % 10;
            }
            else
            {
                returnNodeHotkey = Keys.None;
            }
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
            if (returnNode != null && returnNode.Visible)
            {
                var hotkey = optionNodes.Count + 1;
                if (expandNode != null && expandNode.Visible) { hotkey += 1; }
                CreateHotkeyIcon(returnNode.RectTransform, hotkey % 10, true);
                returnNodeHotkey = Keys.D0 + hotkey % 10;
            }
            else
            {
                returnNodeHotkey = Keys.None;
            }
            return true;
        }

        private void SetCenterNode(GUIButton node)
        {
            node.RectTransform.Parent = commandFrame.RectTransform;
            node.RectTransform.MoveOverTime(Point.Zero, CommandNodeAnimDuration);
            node.RectTransform.ScaleOverTime(centerNodeSize, CommandNodeAnimDuration);
            node.RemoveChild(node.GetChildByUserData("hotkey"));
            foreach (GUIComponent c in node.Children)
            {
                c.Color = c.HoverColor * nodeColorMultiplier;
                c.HoverColor = c.Color;
                c.PressedColor = c.Color;
                c.SelectedColor = c.Color;
                c.ToolTip = characterContext != null ? characterContext.Info.DisplayName + " (" + characterContext.Info.Job.Name + ")" : null;
            }
            node.OnClicked = null;
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
            returnNode = node;
        }

        private bool CreateNodes(object userData)
        {
            if (userData == null)
            {
                CreateShortcutNodes();
                CreateOrderCategoryNodes();
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
            optionNodes.ForEach(node => commandFrame.RemoveChild(node.Item1));
            optionNodes.Clear();
            shortcutNodes.ForEach(node => commandFrame.RemoveChild(node));
            shortcutNodes.Clear();
            commandFrame.RemoveChild(expandNode);
            expandNode = null;
            expandNodeHotkey = Keys.None;
            RemoveExtraOptionNodes();
        }

        private void RemoveExtraOptionNodes()
        {
            extraOptionNodes.ForEach(node => commandFrame.RemoveChild(node));
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
                CreateNodeIcon(node.RectTransform, sprite.Item1, sprite.Item2, tooltip: tooltip);
            }
            CreateHotkeyIcon(node.RectTransform, hotkey % 10);
            optionNodes.Add(new Tuple<GUIComponent, Keys>(node, Keys.D0 + hotkey % 10));
        }

        private void CreateShortcutNodes()
        {
            var sub = Character.Controlled != null && Character.Controlled.TeamID == Character.TeamType.Team2 && Submarine.MainSubs.Length > 1 ?
                Submarine.MainSubs[1] : Submarine.MainSub;

            if (sub == null) { return; }

            shortcutNodes.Clear();

            var reactor = sub.GetItems(false).Find(i => i.HasTag("reactor"))?.GetComponent<Reactor>();
            if (reactor != null)
            {
                var reactorOutput = -reactor.CurrPowerConsumption;
                // If player is not an engineer AND the reactor is not powered up AND nobody is using the reactor
                // ---> Create shortcut node for "Operate Reactor" order's "Power Up" option
                if ((Character.Controlled == null || Character.Controlled.Info.Job.Prefab != JobPrefab.Get("engineer")) &&
                    reactorOutput < float.Epsilon && characters.None(c => c.SelectedConstruction == reactor.Item))
                {
                    var order = new Order(Order.GetPrefab("operatereactor"), reactor.Item, reactor, Character.Controlled);
                    shortcutNodes.Add(
                        CreateOrderOptionNode(shortcutNodeSize, null, Point.Zero, order, order.Prefab.Options[0], order.Prefab.OptionNames[0], -1));
                }
            }

            // TODO: Reconsider the conditions as bot captain can have the nav term selected without operating it
            // If player is not a captain AND nobody is using the nav terminal AND the nav terminal is powered up
            // --> Create shortcut node for Steer order
            if ((Character.Controlled == null || Character.Controlled.Info.Job.Prefab != JobPrefab.Get("captain")) &&
                sub.GetItems(false).Find(i => i.HasTag("navterminal")) is Item nav && characters.None(c => c.SelectedConstruction == nav) &&
                nav.GetComponent<Steering>() is Steering steering && steering.Voltage > steering.MinVoltage)
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, Order.GetPrefab("steer"), -1));
            }

            // If player is not a security officer AND invaders are reported
            // --> Create shorcut node for Fight Intruders order
            if ((Character.Controlled == null || Character.Controlled.Info.Job.Prefab != JobPrefab.Get("securityofficer")) &&
                (Order.GetPrefab("reportintruders") is Order reportIntruders && ActiveOrders.Any(o => o.First.Prefab == reportIntruders)))
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, Order.GetPrefab("fightintruders"), -1));
            }

            // If player is not a mechanic AND a breach has been reported
            // --> Create shorcut node for Fix Leaks order
            if ((Character.Controlled == null || Character.Controlled.Info.Job.Prefab != JobPrefab.Get("mechanic")) &&
                (Order.GetPrefab("reportbreach") is Order reportBreach && ActiveOrders.Any(o => o.First.Prefab == reportBreach)))
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, Order.GetPrefab("fixleaks"), -1));
            }

            // If player is not an engineer AND broken devices have been reported
            // --> Create shortcut node for Repair Damaged Systems order
            if ((Character.Controlled == null || Character.Controlled.Info.Job.Prefab != JobPrefab.Get("engineer")) &&
                (Order.GetPrefab("reportbrokendevices") is Order reportBrokenDevices && ActiveOrders.Any(o => o.First.Prefab == reportBrokenDevices)))
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, Order.GetPrefab("repairsystems"), -1));
            }

            // If fire is reported
            // --> Create shortcut node for Extinguish Fires order
            if (ActiveOrders.Any(o=> o.First.Prefab == Order.GetPrefab("reportfire")))
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, Order.GetPrefab("extinguishfires"), -1));
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
            for (int i = 0; i < shortcutNodes.Count; i++)
            {
                shortcutNodes[i].RectTransform.Parent = commandFrame.RectTransform;
                shortcutNodes[i].RectTransform.MoveOverTime(shorcutCenterNodeOffset + offsets[i + 1].ToPoint(), CommandNodeAnimDuration);
            }
        }

        private void CreateOrderNodes(OrderCategory orderCategory)
        {
            var orders = Order.PrefabList.FindAll(o => o.Category == orderCategory && !o.TargetAllCharacters);
            var offsets = MathUtils.GetPointsOnCircumference(Vector2.Zero, nodeDistance,
                GetCircumferencePointCount(orders.Count), GetFirstNodeAngle(orders.Count));
            for (int i = 0; i < orders.Count; i++)
            {
                optionNodes.Add(new Tuple<GUIComponent, Keys>(
                    CreateOrderNode(nodeSize, commandFrame.RectTransform, offsets[i].ToPoint(), orders[i], (i + 1) % 10),
                    CanSomeoneHearCharacter() ? Keys.D0 + (i + 1) % 10 : Keys.None));
            }
        }

        private GUIButton CreateOrderNode(Point size, RectTransform parent, Point offset, Order order, int hotkey)
        {
            var node = new GUIButton(
                new RectTransform(size, parent: parent, anchor: Anchor.Center), style: null)
            {
                UserData = order
            };

            node.RectTransform.MoveOverTime(offset, CommandNodeAnimDuration);

            var canSomeoneHearCharacter = CanSomeoneHearCharacter();
            var hasOptions = order.ItemComponentType != null || order.ItemIdentifiers.Length > 0 || order.Options.Length > 1;
            node.OnClicked = (button, userData) =>
            {
                if (!canSomeoneHearCharacter || !CanIssueOrders) { return false; }
                var o = userData as Order;
                // TODO: Consider defining orders' or order categories' quick-assignment possibility in the XML
                if (o.Category == OrderCategory.Movement && characterContext == null)
                {
                    CreateAssignmentNodes(node);
                }
                else if (hasOptions)
                {
                    NavigateForward(button, userData);
                }
                else
                {                    
                    SetCharacterOrder(characterContext ?? GetBestCharacterForOrder(o), o, null, Character.Controlled);
                    DisableCommandUI();
                }
                return true;
            };
            var icon = CreateNodeIcon(node.RectTransform, order.SymbolSprite, order.Color,
                tooltip: hasOptions || characterContext != null ? order.Name : order.Name +
                    "\n" + (!PlayerInput.MouseButtonsSwapped() ? TextManager.Get("input.leftmouse") : TextManager.Get("input.rightmouse")) + ": " + TextManager.Get("commandui.quickassigntooltip") +
                    "\n" + (!PlayerInput.MouseButtonsSwapped() ? TextManager.Get("input.rightmouse") : TextManager.Get("input.leftmouse")) + ": " + TextManager.Get("commandui.manualassigntooltip"));

            if (!canSomeoneHearCharacter)
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
            // This is largely based on the CreateOrderTargetFrame() method

            Submarine submarine = Character.Controlled != null && Character.Controlled.TeamID == Character.TeamType.Team2 && Submarine.MainSubs.Length > 1 ?
                Submarine.MainSubs[1] :
                Submarine.MainSub;

            List<Item> matchingItems = new List<Item>();
            if (order.ItemComponentType != null || order.ItemIdentifiers.Length > 0)
            {
                matchingItems = order.ItemIdentifiers.Length > 0 ?
                    Item.ItemList.FindAll(it => order.ItemIdentifiers.Contains(it.Prefab.Identifier) || it.HasTag(order.ItemIdentifiers)) :
                    Item.ItemList.FindAll(it => it.Components.Any(ic => ic.GetType() == order.ItemComponentType));

                matchingItems.RemoveAll(it => it.Submarine != submarine && !submarine.DockedTo.Contains(it.Submarine));
                matchingItems.RemoveAll(it => it.Submarine != null && it.Submarine.IsOutpost);
            }

            //more than one target item -> create a minimap-like selection with a pic of the sub
            if (matchingItems.Count > 1)
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

                List<GUIComponent> optionFrames = new List<GUIComponent>();
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

                    var optionFrame = new GUIFrame(
                        new RectTransform(
                            new Point((int)(250 * GUI.Scale), (int)((40 + order.Options.Length * 40) * GUI.Scale)),
                            parent: itemTargetFrame.RectTransform,
                            anchor: anchor),
                        style: "InnerFrame");

                    new GUIFrame(
                        new RectTransform(Vector2.One, optionFrame.RectTransform, anchor: Anchor.Center),
                        style: "OuterGlow",
                        color: Color.Black * 0.7f);

                    var optionContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.9f), optionFrame.RectTransform, anchor: Anchor.Center))
                    {
                        RelativeSpacing = 0.05f,
                        Stretch = true
                    };

                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), optionContainer.RectTransform), item != null ? item.Name : order.Name);

                    for (int i = 0; i < order.Options.Length; i++)
                    {
                        optionNodes.Add(new Tuple<GUIComponent, Keys>(
                            new GUIButton(
                                new RectTransform(new Vector2(1.0f, 0.2f), optionContainer.RectTransform),
                                text: order.OptionNames[i],
                                style: "GUITextBox")
                            {
                                UserData = new Tuple<Order, string>(
                                    item == null ? order : new Order(order, item, item.Components.FirstOrDefault(ic => ic.GetType() == order.ItemComponentType)),
                                    order.Options[i]),
                                Font = GUI.SmallFont,
                                OnClicked = (_, userData) =>
                                {
                                    if (!CanIssueOrders) { return false; }
                                    var o = userData as Tuple<Order, string>;
                                    SetCharacterOrder(characterContext ?? GetBestCharacterForOrder(o.Item1), o.Item1, o.Item2, Character.Controlled);
                                    DisableCommandUI();
                                    return true;
                                }
                            },
                            Keys.None));
                    }
                    optionFrames.Add(optionFrame);
                }
                GUI.PreventElementOverlap(optionFrames, clampArea: new Rectangle(10, 10, GameMain.GraphicsWidth - 20, GameMain.GraphicsHeight - 20));

                var shadow = new GUIFrame(
                    new RectTransform(targetFrame.Rect.Size + new Point((int)(200 * GUI.Scale)), targetFrame.RectTransform, anchor: Anchor.Center),
                    style: "OuterGlow",
                    color: matchingItems.Count > 1 ? Color.Black * 0.9f : Color.Black * 0.7f);
                shadow.SetAsFirstChild();
            }
            //only one target (or an order with no particular targets), just show options
            else
            {
                var item = matchingItems.Count > 0 ? matchingItems[0] : null;
                var o = item == null ? order : new Order(order, item, item.Components.FirstOrDefault(ic => ic.GetType() == order.ItemComponentType));
                var offsets = MathUtils.GetPointsOnCircumference(Vector2.Zero, nodeDistance,
                    GetCircumferencePointCount(order.Options.Length),
                    GetFirstNodeAngle(order.Options.Length));
                var offsetIndex = 0;
                for (int i = 0; i < order.Options.Length; i++)
                {
                    optionNodes.Add(new Tuple<GUIComponent, Keys>(
                        CreateOrderOptionNode(nodeSize, commandFrame.RectTransform, offsets[offsetIndex++].ToPoint(), o, order.Options[i], order.OptionNames[i], (i + 1) % 10),
                        Keys.D0 + (i + 1) % 10));
                }
            }
        }

        private GUIButton CreateOrderOptionNode(Point size, RectTransform parent, Point offset, Order order, string option, string optionName, int hotkey)
        {
            var node = new GUIButton(
                new RectTransform(size, parent: parent, anchor: Anchor.Center)
                {
                    AbsoluteOffset = offset
                },
                style: null)
            {
                UserData = new Tuple<Order, string>(order, option),
                OnClicked = (_, userData) =>
                {
                    if (!CanIssueOrders) { return false; }
                    var o = userData as Tuple<Order, string>;
                    SetCharacterOrder(characterContext ?? GetBestCharacterForOrder(o.Item1), o.Item1, o.Item2, Character.Controlled);
                    DisableCommandUI();
                    return true;
                }
            };
            GUIImage icon = null;
            if (order.Prefab.OptionSprites.TryGetValue(option, out Sprite sprite))
            {
                icon = CreateNodeIcon(node.RectTransform, sprite, order.Color,
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

        private void CreateAssignmentNodes(GUIComponent node)
        {
            var order = (node.UserData is Order) ?
                new Tuple<Order, string>(node.UserData as Order, null) :
                node.UserData as Tuple<Order, string>;
            var characters = GetCharactersSortedForOrder(order.Item1);
            if (characters.Count < 1) { return; }

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
            SetReturnNode(centerNode, new Point(
                (int)(node.RectTransform.AbsoluteOffset.X * -returnNodeDistanceModifier),
                (int)(node.RectTransform.AbsoluteOffset.Y * -returnNodeDistanceModifier)));
            if (targetFrame == null || !targetFrame.Visible)
            {
                SetCenterNode(node as GUIButton);
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
                    CreateNodeIcon(clickedOptionNode.RectTransform, sprite, order.Item1.Color, tooltip: order.Item2);
                }
                SetCenterNode(clickedOptionNode);
                node = null;
                targetFrame.Visible = false;
            }
            if (shortcutCenterNode != null)
            {
                commandFrame.RemoveChild(shortcutCenterNode);
                shortcutCenterNode = null;
            }

            var needToExpand = characters.Count > assignmentNodeMaxCount + 1;
            var nodeCount = needToExpand ? assignmentNodeMaxCount + 1 : characters.Count;
            var extraNodeDistance = Math.Max(nodeCount - 6, 0) * (GUI.Scale * 30);
            var offsets = MathUtils.GetPointsOnCircumference(Vector2.Zero, nodeDistance + extraNodeDistance,
                GetCircumferencePointCount(nodeCount),
                GetFirstNodeAngle(nodeCount));

            var i = 0;
            var assignmentNodeCount = (needToExpand ? nodeCount - 1 : nodeCount);
            for (; i < assignmentNodeCount; i++)
            {
                CreateAssignmentNode(order, characters[i], offsets[i].ToPoint(), (i + 1) % 10);
            }

            int hotkey;
            if (!needToExpand)
            {
                hotkey = optionNodes.Count + 1;
                CreateHotkeyIcon(returnNode.RectTransform, hotkey % 10, true);
                returnNodeHotkey = Keys.D0 + hotkey % 10;
                expandNodeHotkey = Keys.None;
                return;
            }

            extraOptionCharacters.Clear();
            extraOptionCharacters.AddRange(characters.GetRange(i, characters.Count - i));

            expandNode = new GUIButton(
                new RectTransform(nodeSize, parent: commandFrame.RectTransform, anchor: Anchor.Center)
                {
                    AbsoluteOffset = offsets[i].ToPoint()
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
        }

        private bool ExpandAssignmentNodes(GUIButton node, object userData)
        {
            node.OnClicked = (button, _) =>
            {
                RemoveExtraOptionNodes();
                button.OnClicked = ExpandAssignmentNodes;
                return true;
            };

            var order = userData as Tuple<Order, string>;
            // TODO: The value 100 should be determined by how large the inner circle is
            var offsets = MathUtils.GetPointsOnCircumference(Vector2.Zero, (nodeDistance + GUI.Scale * 100) * 1.55f,
                GetCircumferencePointCount(extraOptionCharacters.Count),
                GetFirstNodeAngle(extraOptionCharacters.Count));
            for (int i = 0; i < extraOptionCharacters.Count; i++)
            {
                CreateAssignmentNode(order, extraOptionCharacters[i], offsets[i].ToPoint(), -1);
            }
            return true;
        }

        private void CreateAssignmentNode(Tuple<Order, string> order, Character character, Point offset, int hotkey)
        {
            // Button
            var node = new GUIButton(
                new RectTransform(nodeSize, parent: commandFrame.RectTransform, anchor: Anchor.Center),
                style: null)
            {
                OnClicked = (button, userData) =>
                {
                    if (!CanIssueOrders) { return false; }
                    SetCharacterOrder(character, order.Item1, order.Item2, Character.Controlled);
                    DisableCommandUI();
                    return true;
                }
            };
            node.RectTransform.MoveOverTime(offset, CommandNodeAnimDuration);
            // Container
            var icon = new GUIImage(
                new RectTransform(new Vector2(1.2f), node.RectTransform, anchor: Anchor.Center),
                "CommandNodeContainer",
                scaleToFit: true)
            {
                Color = character.Info.Job.Prefab.UIColor * nodeColorMultiplier,
                HoverColor = character.Info.Job.Prefab.UIColor,
                PressedColor = character.Info.Job.Prefab.UIColor,
                SelectedColor = character.Info.Job.Prefab.UIColor,
                UserData = "colorsource"
            };
            // Character icon
            new GUICustomComponent(
                new RectTransform(Vector2.One, node.RectTransform),
                (spriteBatch, _) =>
                {
                    character.Info.DrawIcon(spriteBatch, node.Center, node.Rect.Size.ToVector2() * 0.75f);
                })
            {
                ToolTip = character.Info.DisplayName + " (" + character.Info.Job.Name + ")"
            };

            bool canHear = character.CanHearCharacter(Character.Controlled);
            if (!canHear)
            {
                node.CanBeFocused = icon.CanBeFocused = false;
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

        private GUIImage CreateNodeIcon(RectTransform parent, Sprite sprite, Color color, string tooltip = null)
        {
            // Icon
            return new GUIImage(
                new RectTransform(Vector2.One, parent),
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
            new GUIImage(new RectTransform(Vector2.One, parent, anchor: Anchor.Center), cancelIcon, scaleToFit: true)
            {
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

        #region Crew Member Assignment Logic

        private Character GetBestCharacterForOrder(Order order)
        {
            if (Character.Controlled == null) { return null; }
            return characters.FindAll(c => c != Character.Controlled && c.TeamID == Character.Controlled.TeamID)
                .OrderByDescending(c => c.CurrentOrder == null || c.CurrentOrder.Identifier == dismissedOrderPrefab.Identifier)
                .ThenByDescending(c => order.HasAppropriateJob(c))
                .ThenBy(c => c.CurrentOrder?.Weight)
                .FirstOrDefault();
        }

        private List<Character> GetCharactersSortedForOrder(Order order)
        {
            if (Character.Controlled == null) { return new List<Character>(); }
            if (order.Identifier == "follow")
            {
                return characters.FindAll(c => c != Character.Controlled && c.TeamID == Character.Controlled.TeamID)
                    .OrderByDescending(c => c.CurrentOrder == null || c.CurrentOrder.Identifier == dismissedOrderPrefab.Identifier)
                    .ToList();
            }
            else
            {
                return characters.FindAll(c => c.TeamID == Character.Controlled.TeamID)
                    .OrderByDescending(c => c.CurrentOrder == null || c.CurrentOrder.Identifier == dismissedOrderPrefab.Identifier)
                    .ThenByDescending(c => order.HasAppropriateJob(c))
                    .ThenBy(c => c.CurrentOrder?.Weight)
                    .ToList();
            }
        }

        #endregion

        #endregion

        /// <summary>
        /// Creates a listbox that includes all the characters in the crew, can be used externally (round info menus etc)
        /// </summary>
        public void CreateCrewListFrame(IEnumerable<Character> crew, GUIFrame crewFrame)
        {
            List<Character.TeamType> teamIDs = crew.Select(c => c.TeamID).Distinct().ToList();

            if (!teamIDs.Any()) teamIDs.Add(Character.TeamType.None);

            int listBoxHeight = 300 / teamIDs.Count;

            var content = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), crewFrame.RectTransform))
            {
                Stretch = true
            };

            for (int i = 0; i < teamIDs.Count; i++)
            {
                if (teamIDs.Count > 1)
                {
                    new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.1f), content.RectTransform), CombatMission.GetTeamName(teamIDs[i]));
                }

                GUIListBox crewList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.5f), content.RectTransform) { RelativeOffset = new Vector2(0.0f, 0.1f) })
                {
                    UserData = crewFrame
                };
                crewList.OnSelected = (component, obj) =>
                {
                    SelectCrewCharacter(component.UserData as Character, crewList);
                    return true;
                };

                foreach (Character character in crew.Where(c => c.TeamID == teamIDs[i]))
                {
                    GUIFrame frame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.15f), crewList.Content.RectTransform), style: "ListBoxElement")
                    {
                        UserData = character,
                        Color = (GameMain.NetworkMember != null && GameMain.Client.Character == character) ? Color.Gold * 0.2f : Color.Transparent
                    };

                    var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.9f), frame.RectTransform, Anchor.Center), isHorizontal: true)
                    {
                        RelativeSpacing = 0.05f,
                        Stretch = true
                    };

                    new GUICustomComponent(new RectTransform(new Vector2(0.2f, 1.0f), paddedFrame.RectTransform, Anchor.CenterLeft),
                        onDraw: (sb, component) => character.Info.DrawIcon(sb, component.Rect.Center.ToVector2(), targetAreaSize: component.Rect.Size.ToVector2()))
                    {
                        CanBeFocused = false,
                        HoverColor = Color.White,
                        SelectedColor = Color.White
                    };

                    GUITextBlock textBlock = new GUITextBlock(new RectTransform(Vector2.One, paddedFrame.RectTransform),
                        ToolBox.LimitString(character.Info.Name + " (" + character.Info.Job.Name + ")", GUI.Font, paddedFrame.Rect.Width - paddedFrame.Rect.Height),
                        textColor: character.Info.Job.Prefab.UIColor);
                }
            }
        }

        /// <summary>
        /// Select a character from CrewListFrame
        /// </summary>
        protected bool SelectCrewCharacter(Character character, GUIComponent crewList)
        {
            if (character == null) { return false; }

            GUIComponent crewFrame = (GUIComponent)crewList.UserData;
            GUIComponent existingPreview = crewFrame.FindChild("SelectedCharacter");
            if (existingPreview != null) { crewFrame.RemoveChild(existingPreview); }

            var previewPlayer = new GUIFrame(new RectTransform(new Vector2(0.5f, 0.7f), crewFrame.RectTransform, Anchor.TopRight), style: null)
            {
                UserData = "SelectedCharacter"
            };

            character.Info.CreateInfoFrame(previewPlayer);

            if (GameMain.NetworkMember != null) { GameMain.Client.SelectCrewCharacter(character, previewPlayer); }

            return true;
        }

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
                canIssueOrders = radio != null && radio.CanTransmit();
            }

            if (canIssueOrders)
            {
                //report buttons are hidden when accessing another character's inventory
                ReportButtonFrame.Visible = !Character.Controlled.ShouldLockHud() &&
                    (Character.Controlled?.SelectedCharacter?.Inventory == null ||
                    !Character.Controlled.SelectedCharacter.CanInventoryBeAccessed);

                var reportButtonParent = ChatBox ?? GameMain.Client?.ChatBox;
                if (reportButtonParent == null) { return; }

                ReportButtonFrame.RectTransform.AbsoluteOffset = new Point(reportButtonParent.GUIFrame.Rect.Right + (int)(10 * GUI.Scale), reportButtonParent.GUIFrame.Rect.Y);

                bool hasFires = Character.Controlled.CurrentHull.FireSources.Count > 0;
                ToggleReportButton("reportfire", hasFires);

                bool hasLeaks = Character.Controlled.CurrentHull.Submarine != null && Character.Controlled.CurrentHull.ConnectedGaps.Any(g => !g.IsRoomToRoom && g.Open > 0.0f);
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
            characters.Clear();

            WayPoint[] waypoints = WayPoint.SelectCrewSpawnPoints(characterInfos, Submarine.MainSub);

            for (int i = 0; i < waypoints.Length; i++)
            {
                Character character;
                character = Character.Create(characterInfos[i], waypoints[i].WorldPosition, characterInfos[i].Name);

                if (character.Info != null)
                {
                    if (!character.Info.StartItemsGiven && character.Info.InventoryData != null)
                    {
                        DebugConsole.ThrowError($"Error when initializing a single player round: character \"{character.Name}\" has not been given their initial items but has saved inventory data. Using the saved inventory data instead of giving the character new items.");
                    }
                    if (character.Info.InventoryData != null)
                    {
                        character.Info.SpawnInventoryItems(character.Inventory, character.Info.InventoryData);
                    }
                    else if (!character.Info.StartItemsGiven)
                    {
                        character.GiveJobItems(waypoints[i]);
                    }
                    character.Info.StartItemsGiven = true;
                }

                AddCharacter(character);
                if (i == 0)
                {
                    Character.Controlled = character;
                }
            }

            conversationTimer = Rand.Range(5.0f, 10.0f);
        }

        public void EndRound()
        {
            //remove characterinfos whose characters have been removed or killed
            characterInfos.RemoveAll(c => c.Character == null || c.Character.Removed || c.CauseOfDeath != null);

            characters.Clear();
            crewList.ClearChildren();
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
                if (ci.InventoryData != null)
                {
                    infoElement.Add(ci.InventoryData);
                }
            }
            parentElement.Add(element);
        }
    }
}
