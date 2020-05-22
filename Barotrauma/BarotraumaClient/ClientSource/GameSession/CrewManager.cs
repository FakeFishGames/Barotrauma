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
        private Point crewListEntrySize;

        private GUIFrame contextMenu;
        private GUIListBox subContextMenu;

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
                if (!subElement.Name.ToString().Equals("character", StringComparison.OrdinalIgnoreCase)) { continue; }

                var characterInfo = new CharacterInfo(subElement);
                characterInfos.Add(characterInfo);
                foreach (XElement invElement in subElement.Elements())
                {
                    if (!invElement.Name.ToString().Equals("inventory", StringComparison.OrdinalIgnoreCase)) { continue; }
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

            #region Crew Area

            var crewAreaWithButtons = new GUIFrame(
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
                    ToggleCrewListOpen = !ToggleCrewListOpen;
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
            if (chatBox != null)
            {
                chatBox.ToggleButton = new GUIButton(new RectTransform(new Point((int)(182f * GUI.Scale * 0.4f), (int)(99f * GUI.Scale * 0.4f)), chatBox.GUIFrame.Parent.RectTransform), style: "ChatToggleButton");
                chatBox.ToggleButton.RectTransform.AbsoluteOffset = new Point(0, HUDLayoutSettings.ChatBoxArea.Height - chatBox.ToggleButton.Rect.Height);
                chatBox.ToggleButton.OnClicked += (GUIButton btn, object userdata) =>
                {
                    chatBox.ToggleOpen = !chatBox.ToggleOpen;
                    chatBox.CloseAfterMessageSent = false;
                    return true;
                };
            }

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
                        var sub = Character.Controlled.Submarine;
                        if (sub == null || sub.TeamID != Character.Controlled.TeamID || sub.Info.IsWreck) { return false; }
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
            DisplayCharacterOrder(character, character.CurrentOrder, character.CurrentOrderOption);
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

        #region Crew List Order Displayment

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
                AddOrder(new Order(order.Prefab ?? order, hull, null, orderGiver), order.FadeOutTime);
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
                //can't issue an order if no characters are available
                if (character == null) { return; }

                if (IsSinglePlayer)
                {
                    character.SetOrder(order, option, orderGiver, speak: orderGiver != character);
                    orderGiver?.Speak(
                        order.GetChatMessage(character.Name, orderGiver.CurrentHull?.DisplayName, givingOrderToSelf: character == orderGiver, orderOption: option), null);
                }
                else if (orderGiver != null)
                {
                    OrderChatMessage msg = new OrderChatMessage(order, option, order?.TargetEntity ?? order?.TargetItemComponent?.Item, character, orderGiver);
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
            var currentOrderComponent = GetCurrentOrderComponent(layoutGroup);

            if (order != null)
            {
                var prevOrderComponent = GetPreviousOrderComponent(layoutGroup);
                if (currentOrderComponent?.UserData is OrderInfo currentOrderInfo)
                {
                    if (order.Identifier == currentOrderInfo.Order.Identifier &&
                        option == currentOrderInfo.OrderOption &&
                        order.TargetEntity == currentOrderInfo.Order.TargetEntity) { return; }

                    layoutGroup.RemoveChild(prevOrderComponent);
                    DisplayPreviousCharacterOrder(character, layoutGroup, currentOrderInfo);
                }
                else if (order.Identifier != dismissedOrderPrefab.Identifier &&
                         prevOrderComponent?.UserData is OrderInfo prevOrderInfo &&
                         order.Identifier == prevOrderInfo.Order.Identifier &&
                         option == prevOrderInfo.OrderOption &&
                         order.TargetEntity == prevOrderInfo.Order.TargetEntity)
                {
                    layoutGroup.RemoveChild(prevOrderComponent);
                }
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

        private void DisplayPreviousCharacterOrder(Character character, GUILayoutGroup characterComponent, OrderInfo orderInfo)
        {
            if (orderInfo.Order == null || orderInfo.Order.Identifier == dismissedOrderPrefab.Identifier) { return; }

            var previousOrderInfo = new OrderInfo(orderInfo);
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
            return characterComponent?.FindChild(c => c?.UserData is OrderInfo orderInfo && orderInfo.ComponentIdentifier == "currentorder");
        }

        private GUIComponent GetPreviousOrderComponent(GUILayoutGroup characterComponent)
        {
            return characterComponent?.FindChild(c => c?.UserData is OrderInfo orderInfo && orderInfo.ComponentIdentifier == "previousorder");
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
            if (IsSinglePlayer || client == null || (GameMain.NetworkMember?.ConnectedClients?.All(match => match != client) ?? true)) { return; }

            contextMenu = new GUIFrame(new RectTransform(new Vector2(0.1f, 0.12f), GUI.Canvas) { ScreenSpaceOffset = mousePos }, style: "GUIToolTip") { UserData = client };

            var nameLabel = new GUITextBlock(new RectTransform(new Vector2(1f, 0.2f), contextMenu.RectTransform), client.Name, font: GUI.SubHeadingFont)
            {
                Padding = new Vector4(8), 
                TextColor = client.Character?.Info?.Job.Prefab.UIColor ?? Color.White
            };

            var optionsList = new GUIListBox(new RectTransform(new Vector2(1f, 0.8f), contextMenu.RectTransform, Anchor.BottomLeft), style: null)
            {
                Padding = new Vector4(4, 0, 4, 4)
            };

            bool hasSteam = client.SteamID > 0 && SteamManager.IsInitialized,
                 canKick  = GameMain.Client.HasPermission(ClientPermissions.Kick),
                 canBan   = GameMain.Client.HasPermission(ClientPermissions.Ban) && client.AllowKicking,
                 canPromo = GameMain.Client.HasPermission(ClientPermissions.ManagePermissions);

            // Disable options if we are targeting ourselves
            if (client.ID == GameMain.Client?.ID)
            {
                canKick = canBan = canPromo = false;
            }

            RectTransform parent = optionsList.Content.RectTransform;
            new GUITextBlock(new RectTransform(Point.Zero, parent), TextManager.Get("viewsteamprofile"), font: GUI.SmallFont)
            {
                Padding = new Vector4(4),
                Enabled = hasSteam,
                UserData = "steam"
            };

            new GUITextBlock(new RectTransform(Point.Zero, parent), TextManager.Get("permissions"), font: GUI.SmallFont)
            {
                Padding = new Vector4(4),
                Enabled = canPromo,
                UserData = "promote"
            };

            new GUITextBlock(new RectTransform(Point.Zero, parent), TextManager.Get(client.MutedLocally ? "unmute" : "mute"), font: GUI.SmallFont)
            {
                Padding = new Vector4(4),
                Enabled = client.ID != GameMain.Client?.ID,
                UserData = "mute"
            };

            new GUITextBlock(new RectTransform(Point.Zero, parent), TextManager.Get(canKick ? "kick" : "votetokick"), font: GUI.SmallFont)
            {
                Padding = new Vector4(4),
                Enabled = client.ID != GameMain.Client?.ID && client.AllowKicking,
                UserData = canKick ? "kick" : "votekick"
            };

            new GUITextBlock(new RectTransform(Point.Zero, parent), TextManager.Get("ban"), font: GUI.SmallFont)
            {
                Padding = new Vector4(4),
                Enabled = canBan,
                UserData = "ban"
            };

            foreach (GUIComponent c in optionsList.Content.Children)
            {
                if (c is GUITextBlock child && !child.Enabled)
                {
                    child.TextColor *= 0.5f;
                }
            }

            var children = optionsList.Content.Children.ToList();

            // Resize all children to the size of their text
            foreach (GUITextBlock block in children.Where(c => c is GUITextBlock).Cast<GUITextBlock>())
            {
                block.RectTransform.NonScaledSize = new Point((int) (block.TextSize.X + (block.Padding.X + block.Padding.Z)), (int)(18 * GUI.Scale));
            }

            int horizontalPadding = (int)(optionsList.Padding.X + optionsList.Padding.Z);
            int verticalPadding = (int)(optionsList.Padding.Y + optionsList.Padding.W);
            int largestWidth = children.Max(c => c.Rect.Width + horizontalPadding);

            // If the name is bigger than any of the options then overwrite
            nameLabel.RectTransform.MinSize = new Point((int)(nameLabel.TextSize.X + (nameLabel.Padding.X + nameLabel.Padding.Z)), nameLabel.RectTransform.NonScaledSize.Y);
            if (largestWidth < nameLabel.RectTransform.MinSize.X) { largestWidth = nameLabel.RectTransform.MinSize.X; }

            // Resize all children to the size of the longest element
            foreach (GUIComponent c in children) { c.RectTransform.MinSize = new Point(largestWidth, c.Rect.Height); }
            
            // crop the context menu
            contextMenu.RectTransform.NonScaledSize = new Point(largestWidth, (children.Sum(c => c.Rect.Height) + verticalPadding) + nameLabel.Rect.Height);

            // if the menu would go off the screen then move it up
            if (contextMenu.Rect.Bottom > GameMain.GraphicsHeight)
            {
                contextMenu.RectTransform.ScreenSpaceOffset = new Point(mousePos.X, mousePos.Y - contextMenu.Rect.Height);
            }
            
            optionsList.OnSelected = (component, obj) =>
            {
                if (component.Enabled)
                {
                    switch (obj) 
                    {
                        case "steam":
                            Steamworks.SteamFriends.OpenWebOverlay($"https://steamcommunity.com/profiles/{client.SteamID}");
                            break;
                        case "mute":
                            client.MutedLocally = !client.MutedLocally;
                            break;
                        case "kick":
                            GameMain.Client?.CreateKickReasonPrompt(client.Name, false);
                            break;
                        case "votekick":
                            GameMain.Client?.VoteForKick(client);
                            break;
                        case "ban":
                            GameMain.Client?.CreateKickReasonPrompt(client.Name, true);
                            break;
                    }
                    contextMenu = null;
                    return true;
                }
                return false;
            };
        }

        private void CreatePromoteSubMenu(Point pos, Client client)
        {
            if (client == null ) { return; }
            
            subContextMenu = new GUIListBox(new RectTransform(new Vector2(0.1f, 0.1f), GUI.Canvas) { ScreenSpaceOffset = pos }, style: "GUIToolTip");

            foreach (var rank in PermissionPreset.List)
            {
                new GUITextBlock(new RectTransform(Point.Zero, subContextMenu.Content.RectTransform), rank.Name, font: GUI.SmallFont)
                {
                    ToolTip = rank.Description,
                    UserData = rank, 
                    Padding = new Vector4(4)
                };
            }
            
            var children = subContextMenu.Content.Children.ToList();

            // Resize all children to the size of their text
            foreach (GUITextBlock block in children.Where(c => c is GUITextBlock).Cast<GUITextBlock>())
            {
                block.RectTransform.NonScaledSize = new Point((int) (block.TextSize.X + (block.Padding.X + block.Padding.Z)), (int)(18 * GUI.Scale));
            }

            int horizontalPadding = (int)(subContextMenu.Padding.X + subContextMenu.Padding.Z);
            int largestWidth = children.Max(c => c.Rect.Width + horizontalPadding);

            // Resize all children to the size of the longest element
            foreach (GUIComponent c in children) { c.RectTransform.MinSize = new Point(largestWidth, c.Rect.Height); }
            
            // crop the context menu
            subContextMenu.RectTransform.NonScaledSize = new Point(largestWidth, children.Sum(c => c.Rect.Height) + horizontalPadding);
            
            // if the menu would go off the screen then move it up
            if (subContextMenu.Rect.Bottom > GameMain.GraphicsHeight)
            {
                subContextMenu.RectTransform.ScreenSpaceOffset = new Point(pos.X, pos.Y - subContextMenu.Rect.Height);
            }
            
            subContextMenu.OnSelected = (component, obj) =>
            {
                if (component.Enabled && obj is PermissionPreset preset)
                {
                    var label = TextManager.GetWithVariables(preset.Permissions == ClientPermissions.None ?  "clearrankprompt" : "giverankprompt", new []{ "[user]", "[rank]" }, new []{ client.Name, preset.Name });
                    
                    var msgBox = new GUIMessageBox(string.Empty, label, new[] { TextManager.Get("Yes"), TextManager.Get("Cancel") });
                    
                    msgBox.Buttons[0].OnClicked = (yesBtn, userdata) =>
                    {
                        client.SetPermissions(preset.Permissions, preset.PermittedCommands);
                        GameMain.Client.UpdateClientPermissions(client);
                        msgBox.Close();
                        return true;
                    };
                    msgBox.Buttons[1].OnClicked = (_, userdata) =>
                    {
                        msgBox.Close();
                        return true;
                    };
                    contextMenu = null;
                    subContextMenu = null;
                    return true;
                }
                return false;
            };
        }

        private static bool IsMouseOnContextMenu(Rectangle rect)
        {
            Rectangle expandedRect = rect; 
            expandedRect.Inflate(20, 20);
            return expandedRect.Contains(PlayerInput.MousePosition);
        }
        
        #endregion

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
                    if (GetPreviousOrderComponent(c.GetChild<GUILayoutGroup>())?.UserData is OrderInfo prevInfo &&
                        crewList.Content.Children.FirstOrDefault(c => c?.UserData == character)?.GetChild<GUILayoutGroup>() is GUILayoutGroup newLayoutGroup)
                    {
                        DisplayPreviousCharacterOrder(character, newLayoutGroup, prevInfo);
                    }
                }
            }

            guiFrame.AddToGUIUpdateList();
            contextMenu?.AddToGUIUpdateList(false, 1);
            subContextMenu?.AddToGUIUpdateList(false, 1);
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
            
            // context menu behavior
            if (contextMenu != null)
            {
                var promote = contextMenu.GetChild<GUIListBox>()?.Content.GetChildByUserData("promote");
                
                if (promote != null && promote.Enabled)
                {
                    promote.ExternalHighlight = subContextMenu != null;
                    
                    if (GUI.IsMouseOn(promote))
                    {
                        if (contextMenu.UserData is Client client && subContextMenu == null)
                        {
                            CreatePromoteSubMenu(new Point(promote.Rect.Right, promote.Rect.Y), client);
                        }
                    } 
                    else if (subContextMenu != null && !IsMouseOnContextMenu(subContextMenu.Rect))
                    {
                        subContextMenu = null;
                    }
                }
                else
                {
                    subContextMenu = null;
                }

                if (subContextMenu == null && !IsMouseOnContextMenu(contextMenu.Rect))
                {
                    contextMenu = null;
                }
            }

            if (contextMenu == null && subContextMenu != null)
            {
                subContextMenu = null;
            }

            if (GUI.DisableHUD) { return; }

            #region Command UI

            WasCommandInterfaceDisabledThisUpdate = false;

            if (PlayerInput.KeyDown(InputType.Command) && (GUI.KeyboardDispatcher.Subscriber == null || GUI.KeyboardDispatcher.Subscriber == crewList) &&
                commandFrame == null && !clicklessSelectionActive && CanIssueOrders)
            {
                if (PlayerInput.KeyDown(Keys.LeftShift) || PlayerInput.KeyDown(Keys.RightShift))
                {
                    CreateCommandUI(FindEntityContext(), true);
                }
                else
                {
                    CreateCommandUI(HUDLayoutSettings.BottomRightInfoArea.Contains(PlayerInput.MousePosition) ? Character.Controlled : GUI.MouseOn?.UserData as Character);
                }
                GUI.PlayUISound(GUISoundType.PopupMenu);
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
                    var node = optionNodes.Find(n => GUI.IsMouseOn(n.Item1))?.Item1 ?? shortcutNodes.Find(n => GUI.IsMouseOn(n));
                    // Make sure the node is for an option-less order or an order option
                    if ((node.UserData is Order order && !order.HasOptions && (!order.MustSetTarget || itemContext != null)) || node.UserData is Tuple<Order, string>)
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

                        if (closestNode != null && closestNode == selectedNode)
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
                    GUI.PlayUISound(GUISoundType.PopupMenu);
                    ToggleCrewListOpen = !ToggleCrewListOpen;
                }
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
        private bool isContextual;
        private readonly List<Order> contextualOrders = new List<Order>();
        private Point shorcutCenterNodeOffset;
        private const int maxShorcutNodeCount = 4;
        private bool WasCommandInterfaceDisabledThisUpdate { get; set; }
        private bool CanIssueOrders
        {
            get
            {
#if DEBUG
                return Character.Controlled == null || Character.Controlled.Info != null && Character.Controlled.SpeechImpediment < 100.0f;
#else
                return Character.Controlled?.Info != null && Character.Controlled.SpeechImpediment < 100.0f;
#endif
            }
        }

        private bool CanSomeoneHearCharacter()
        {
#if DEBUG
            return true;
#else
            return Character.Controlled != null && characters.Any(c => c != Character.Controlled && c.CanHearCharacter(Character.Controlled));
#endif
        }

        private Entity FindEntityContext()
        {
            if (Character.Controlled?.FocusedCharacter != null)
            {
                if (Character.Controlled?.FocusedItem != null)
                {
                    Vector2 mousePos = GameMain.GameScreen.Cam.ScreenToWorld(PlayerInput.MousePosition);
                    if (Vector2.Distance(mousePos, Character.Controlled.FocusedCharacter.WorldPosition) < Vector2.Distance(mousePos, Character.Controlled.FocusedItem.WorldPosition))
                    {
                        return Character.Controlled.FocusedCharacter;
                    }
                    else
                    {
                        return Character.Controlled.FocusedItem;
                    }
                }
                else
                {
                    return Character.Controlled.FocusedCharacter;
                }

            }
            else if (TryGetBreachedHullAtHoveredWall(out Hull breachedHull))
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
                isContextual = false;
            }
            else if (entityContext is Item item)
            {
                itemContext = item;
                isContextual = true;
            }
            else if (entityContext is Hull hull)
            {
                hullContext = hull;
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
                    Color = characterContext.Info.Job.Prefab.UIColor * nodeColorMultiplier,
                    HoverColor = characterContext.Info.Job.Prefab.UIColor,
                    UserData = "colorsource"
                };
                // Character icon
                new GUICustomComponent(
                    new RectTransform(Vector2.One, startNode.RectTransform, anchor: Anchor.Center),
                    (spriteBatch, _) =>
                    {
                        if (!(entityContext is Character character)) { return; }
                        var node = startNode;
                        character.Info.DrawJobIcon(spriteBatch,
                            new Rectangle((int)(node.Rect.X + node.Rect.Width * 0.5f), (int)(node.Rect.Y + node.Rect.Height * 0.1f), (int)(node.Rect.Width * 0.6f), (int)(node.Rect.Height * 0.8f)));
                        character.Info.DrawIcon(spriteBatch, new Vector2(node.Rect.X + node.Rect.Width * 0.35f, node.Center.Y), node.Rect.Size.ToVector2() * 0.7f);
                    })
                {
                    ToolTip = characterContext.Info.DisplayName + " (" + characterContext.Info.Job.Name + ")"
                };
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
            var offset = node?.UserData is Order order && order.GetMatchingItems(true).Count > 1 ?
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
            Submarine sub = GetTargetSubmarine();

            if (sub == null) { return; }

            shortcutNodes.Clear();

            if (shortcutNodes.Count < maxShorcutNodeCount && sub.GetItems(false).Find(i => i.HasTag("reactor") && !i.NonInteractable)?.GetComponent<Reactor>() is Reactor reactor)
            {
                var reactorOutput = -reactor.CurrPowerConsumption;
                // If player is not an engineer AND the reactor is not powered up AND nobody is using the reactor
                // ---> Create shortcut node for "Operate Reactor" order's "Power Up" option
                if ((Character.Controlled == null || Character.Controlled.Info.Job.Prefab != JobPrefab.Get("engineer")) &&
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
            if (shortcutNodes.Count < maxShorcutNodeCount && (Character.Controlled == null || Character.Controlled.Info.Job.Prefab != JobPrefab.Get("captain")) &&
                sub.GetItems(false).Find(i => i.HasTag("navterminal") && !i.NonInteractable) is Item nav && characters.None(c => c.SelectedConstruction == nav) &&
                nav.GetComponent<Steering>() is Steering steering && steering.Voltage > steering.MinVoltage)
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, Order.GetPrefab("steer"), -1));
            }

            // If player is not a security officer AND invaders are reported
            // --> Create shorcut node for Fight Intruders order
            if (shortcutNodes.Count < maxShorcutNodeCount && (Character.Controlled == null || Character.Controlled.Info.Job.Prefab != JobPrefab.Get("securityofficer")) &&
                (Order.GetPrefab("reportintruders") is Order reportIntruders && ActiveOrders.Any(o => o.First.Prefab == reportIntruders)))
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, Order.GetPrefab("fightintruders"), -1));
            }

            // If player is not a mechanic AND a breach has been reported
            // --> Create shorcut node for Fix Leaks order
            if (shortcutNodes.Count < maxShorcutNodeCount && (Character.Controlled == null || Character.Controlled.Info.Job.Prefab != JobPrefab.Get("mechanic")) &&
                (Order.GetPrefab("reportbreach") is Order reportBreach && ActiveOrders.Any(o => o.First.Prefab == reportBreach)))
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, Order.GetPrefab("fixleaks"), -1));
            }

            // If player is not an engineer AND broken devices have been reported
            // --> Create shortcut node for Repair Damaged Systems order
            if (shortcutNodes.Count < maxShorcutNodeCount && (Character.Controlled == null || Character.Controlled.Info.Job.Prefab != JobPrefab.Get("engineer")) &&
                (Order.GetPrefab("reportbrokendevices") is Order reportBrokenDevices && ActiveOrders.Any(o => o.First.Prefab == reportBrokenDevices)))
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, Order.GetPrefab("repairsystems"), -1));
            }

            // If fire is reported
            // --> Create shortcut node for Extinguish Fires order
            if (shortcutNodes.Count < maxShorcutNodeCount && ActiveOrders.Any(o=> o.First.Prefab == Order.GetPrefab("reportfire")))
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, Order.GetPrefab("extinguishfires"), -1));
            }

            if (shortcutNodes.Count < maxShorcutNodeCount && characterContext?.Info?.Job?.Prefab?.AppropriateOrders != null)
            {
                foreach (string orderIdentifier in characterContext.Info.Job.Prefab.AppropriateOrders)
                {
                    if (Order.GetPrefab(orderIdentifier) is Order orderPrefab &&
                        shortcutNodes.None(n => (n.UserData is Order order && order.Identifier == orderIdentifier) ||
                                                (n.UserData is Tuple<Order, string> orderWithOption && orderWithOption.Item1.Identifier == orderIdentifier)) &&
                        !orderPrefab.TargetAllCharacters && orderPrefab.Category != null)
                    {
                        if (!orderPrefab.MustSetTarget || orderPrefab.GetMatchingItems(sub, true).Any())
                        {
                            shortcutNodes.Add(CreateOrderNode(shortcutNodeSize, null, Point.Zero, orderPrefab, -1));
                        }
                        if (shortcutNodes.Count >= maxShorcutNodeCount) { break; }
                    }
                }
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
            var orders = Order.PrefabList.FindAll(o => o.Category == orderCategory && !o.TargetAllCharacters);
            Order order;
            bool disableNode;
            var offsets = MathUtils.GetPointsOnCircumference(Vector2.Zero, nodeDistance,
                GetCircumferencePointCount(orders.Count), GetFirstNodeAngle(orders.Count));
            for (int i = 0; i < orders.Count; i++)
            {
                order = orders[i];
                disableNode = !CanSomeoneHearCharacter() ||
                    (order.MustSetTarget && (order.ItemComponentType != null || order.ItemIdentifiers.Length > 0) && order.GetMatchingItems(true).None());
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
                // Check if targeting an item or a hull
                if (itemContext != null && !itemContext.NonInteractable)
                {
                    foreach (Order p in Order.PrefabList)
                    {
                        if ((p.ItemIdentifiers.Length > 0 && (p.ItemIdentifiers.Contains(itemContext.Prefab.Identifier) || itemContext.HasTag(p.ItemIdentifiers))) ||
                            (p.ItemComponentType != null && itemContext.Components.Any(c => c?.GetType() == p.ItemComponentType)))
                        {
                            contextualOrders.Add(p.HasOptions ? p :
                                new Order(p, itemContext, itemContext.Components.FirstOrDefault(c => c?.GetType() == p.ItemComponentType), Character.Controlled));
                        }
                    }

                    // If targeting a periscope connected to a turret, show the 'operateweapons' order
                    var orderIdentifier = "operateweapons";
                    var operateWeaponsPrefab = Order.GetPrefab(orderIdentifier);
                    if (contextualOrders.None(o => o.Identifier.Equals(orderIdentifier)) && itemContext.Components.Any(c => c is Controller))
                    {
                        var turret = itemContext.GetConnectedComponents<Turret>().FirstOrDefault(c => operateWeaponsPrefab.ItemIdentifiers.Contains(c.Item.Prefab.Identifier)) ??
                            itemContext.GetConnectedComponents<Turret>(recursive: true).FirstOrDefault(c => operateWeaponsPrefab.ItemIdentifiers.Contains(c.Item.Prefab.Identifier));
                        if (turret != null) { contextualOrders.Add(new Order(operateWeaponsPrefab, turret.Item, turret, Character.Controlled)); }
                    }

                    // If targeting a repairable item, show the 'repairsystems' order
                    orderIdentifier = "repairsystems";
                    if (contextualOrders.None(o => o.Identifier.Equals(orderIdentifier)) && itemContext.Repairables.Any())
                    {
                        contextualOrders.Add(new Order(Order.GetPrefab(orderIdentifier), itemContext, null, Character.Controlled));
                        if (itemContext.Repairables.Any(r => r != null && r.requiredSkills.Any(s => s != null && s.Identifier.Equals("electrical"))))
                        {
                            contextualOrders.Add(new Order(Order.GetPrefab("repairelectrical"), itemContext, null, Character.Controlled));
                        }
                        else if (itemContext.Repairables.Any(r => r != null && r.requiredSkills.Any(s => s != null && s.Identifier.Equals("mechanical"))))
                        {
                            contextualOrders.Add(new Order(Order.GetPrefab("repairmechanical"), itemContext, null, Character.Controlled));
                        }
                    }

                    // Always show the 'wait' order if there are other crew members alive
                    orderIdentifier = "wait";
                    if (contextualOrders.None(o => o.Identifier.Equals(orderIdentifier)) && characters.Any(c => c != Character.Controlled))
                    {
                        contextualOrders.Add(new Order(Order.GetPrefab(orderIdentifier), itemContext, null, Character.Controlled));
                    }

                    // Remove the 'pumpwater' order if the target pump is auto-controlled (as it will immediately overwrite the work done by the bot)
                    orderIdentifier = "pumpwater";
                    if (contextualOrders.FirstOrDefault(o => o.Identifier.Equals(orderIdentifier)) is Order o &&
                        itemContext.Components.FirstOrDefault(c => c.GetType() == o.ItemComponentType) is Pump pump)
                    {
                        if (pump.IsAutoControlled) { contextualOrders.Remove(o); }
                    }
                }
                else if(hullContext != null)
                {
                    contextualOrders.Add(new Order(Order.GetPrefab("fixleaks"), hullContext, null, Character.Controlled));
                }

                // Show the 'follow' and 'dismissed' orders if there are other crew members alive
                if (characters.Any(c => c != Character.Controlled))
                {
                    var orderIdentifier = "follow";
                    if (contextualOrders.None(o => o.Identifier.Equals(orderIdentifier)))
                    {
                        contextualOrders.Add(Order.GetPrefab(orderIdentifier));
                    }
                    // Show 'dismissed' order only when there are crew members with active orders
                    orderIdentifier = "dismissed";
                    if (contextualOrders.None(o => o.Identifier.Equals(orderIdentifier)) &&
                        characters.Any(c => c.CurrentOrder != null && !c.CurrentOrder.Identifier.Equals(orderIdentifier)))
                    {
                        contextualOrders.Add(Order.GetPrefab(orderIdentifier));
                    }
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

        public static bool DoesItemHaveContextualOrders(Item item)
        {
            if (Order.PrefabList.Any(o => o.ItemIdentifiers.Length > 0 && o.ItemIdentifiers.Contains(item.Prefab.Identifier))) { return true; }
            if (Order.PrefabList.Any(o => item.HasTag(o.ItemIdentifiers))) { return true; }
            if (Order.PrefabList.Any(o => o.ItemComponentType != null && item.Components.Any(c => c?.GetType() == o.ItemComponentType))) { return true; }

            if (item.Repairables.Any()) { return true; }
            var operateWeaponsPrefab = Order.GetPrefab("operateweapons");
            return item.Components.Any(c => c is Controller) &&
                (item.GetConnectedComponents<Turret>().Any(c => operateWeaponsPrefab.ItemIdentifiers.Contains(c.Item.Prefab.Identifier)) ||
                 item.GetConnectedComponents<Turret>(recursive: true).Any(c => operateWeaponsPrefab.ItemIdentifiers.Contains(c.Item.Prefab.Identifier))); 
        }

        private GUIButton CreateOrderNode(Point size, RectTransform parent, Point offset, Order order, int hotkey, bool disableNode = false, bool checkIfOrderCanBeHeard = true)
        {
            var node = new GUIButton(
                new RectTransform(size, parent: parent, anchor: Anchor.Center), style: null)
            {
                UserData = order
            };

            node.RectTransform.MoveOverTime(offset, CommandNodeAnimDuration);

            if (checkIfOrderCanBeHeard && !disableNode) { disableNode = !CanSomeoneHearCharacter(); }
            var mustSetOptionOrTarget = order.HasOptions || (order.MustSetTarget && itemContext == null);
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
                    SetCharacterOrder(characterContext ?? GetCharacterForQuickAssignment(o), o, null, Character.Controlled);
                    DisableCommandUI();
                }
                return true;
            };
            var icon = CreateNodeIcon(node.RectTransform, order.SymbolSprite, order.Color,
                tooltip: mustSetOptionOrTarget || characterContext != null ? order.Name : order.Name +
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
            var matchingItems = (itemContext == null && order.MustSetTarget) ? order.GetMatchingItems(submarine, true) : new List<Item>();

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

                        new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.3f), optionContainer.RectTransform), item != null ? item.Name : order.Name);

                        for (int i = 0; i < order.Options.Length; i++)
                        {
                            optionNodes.Add(new Tuple<GUIComponent, Keys>(
                                new GUIButton(
                                    new RectTransform(new Vector2(1.0f, 0.2f), optionContainer.RectTransform),
                                    text: order.GetOptionName(i),
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
                                        SetCharacterOrder(characterContext ?? GetCharacterForQuickAssignment(o.Item1), o.Item1, o.Item2, Character.Controlled);
                                        DisableCommandUI();
                                        return true;
                                    }
                                },
                                Keys.None));
                        }
                    }
                    else
                    {
                        var userData = new Tuple<Order, string>(item == null ? order : new Order(order, item, item.Components.FirstOrDefault(ic => ic.GetType() == order.ItemComponentType)), "");
                        optionElement = new GUIButton(
                            new RectTransform(
                                new Point((int)(50 * GUI.Scale)),
                                parent: itemTargetFrame.RectTransform,
                                anchor: anchor),
                            style: null)
                        {
                            UserData = userData,
                            Font = GUI.SmallFont,
                            ToolTip = item?.Name ?? order.Name,
                            OnClicked = (_, userData) =>
                            {
                                if (!CanIssueOrders) { return false; }
                                var o = userData as Tuple<Order, string>;
                                SetCharacterOrder(characterContext ?? GetCharacterForQuickAssignment(o.Item1), o.Item1, o.Item2, Character.Controlled);
                                DisableCommandUI();
                                return true;
                            }
                        };

                        Sprite icon = null;
                        order.MinimapIcons?.TryGetValue(item.Prefab.Identifier, out icon);
                        var colorMultiplier = characters.Any(c => c.CurrentOrder != null &&
                            c.CurrentOrder.Identifier == userData.Item1.Identifier &&
                            c.CurrentOrder.TargetEntity == userData.Item1.TargetEntity) ? 0.5f : 1f;
                        CreateNodeIcon(optionElement.RectTransform, icon ?? order.SymbolSprite, order.Color * colorMultiplier);
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
                var o = item == null || !order.IsPrefab ? order : new Order(order, item, item.Components.FirstOrDefault(ic => ic.GetType() == order.ItemComponentType));
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
                    SetCharacterOrder(characterContext ?? GetCharacterForQuickAssignment(o.Item1), o.Item1, o.Item2, Character.Controlled);
                    DisableCommandUI();
                    return true;
                }
            };
            node.RectTransform.MoveOverTime(offset, CommandNodeAnimDuration);

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
            var characters = GetCharactersForManualAssignment(order.Item1);
            if (characters.None()) { return; }

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
                        CreateNodeIcon(clickedOptionNode.RectTransform, sprite, order.Item1.Color, tooltip: order.Item2);
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
                return;
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
            for (int i = 0; i < extraOptionCharacters.Count; i++)
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
                    SetCharacterOrder(userData as Character, order.Item1, order.Item2, Character.Controlled);
                    DisableCommandUI();
                    return true;
                }
            };
            node.RectTransform.MoveOverTime(offset, CommandNodeAnimDuration);

            var jobColor = character.Info?.Job?.Prefab?.UIColor ?? Color.White;

            // Order icon
            GUIImage orderIcon;
            if (character.CurrentOrder != null && !character.CurrentOrder.Identifier.Equals("dismissed"))
            {
                orderIcon = new GUIImage(new RectTransform(new Vector2(1.2f), node.RectTransform, anchor: Anchor.Center), character.CurrentOrder.SymbolSprite, scaleToFit: true);
                var tooltip = character.CurrentOrder.Name;
                if (!string.IsNullOrWhiteSpace(character.CurrentOrderOption)) { tooltip += " (" + character.CurrentOrder.GetOptionName(character.CurrentOrderOption) + ")"; };
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

#if DEBUG
            bool canHear = true;
#else
            bool canHear = character.CanHearCharacter(Character.Controlled);
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

        private bool TryGetBreachedHullAtHoveredWall(out Hull breachedHull)
        {
            breachedHull = null;
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
                if (Character.Controlled.TeamID == Character.TeamType.Team2 && Submarine.MainSubs.Length > 1)
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

        #region Crew Member Assignment Logic

        private Character GetCharacterForQuickAssignment(Order order)
        {
#if !DEBUG
            if (Character.Controlled == null) { return null; }
#endif
            if (order.Category == OrderCategory.Operate && HumanAIController.IsItemOperatedByAnother(null, order.TargetItemComponent, out Character operatingCharacter))
            {
                return operatingCharacter;
            }
            return GetCharactersSortedForOrder(order, false).FirstOrDefault() ?? Character.Controlled;
        }

        private List<Character> GetCharactersForManualAssignment(Order order)
        {
#if !DEBUG
            if (Character.Controlled == null) { return new List<Character>(); }
#endif
            if (order.Identifier == dismissedOrderPrefab.Identifier)
            {
                return characters.FindAll(c => c.CurrentOrder != null && c.CurrentOrder.Identifier != dismissedOrderPrefab.Identifier)
                    .OrderBy(c => c.Info.DisplayName).ToList();
            }
            return GetCharactersSortedForOrder(order, order.Identifier != "follow").ToList();
        }

        private IEnumerable<Character> GetCharactersSortedForOrder(Order order, bool includeSelf)
        {
            return characters.FindAll(c => Character.Controlled == null || ((includeSelf || c != Character.Controlled) && c.TeamID == Character.Controlled.TeamID))
                    .OrderByDescending(c => c.CurrentOrder != null && order.Category == OrderCategory.Operate && c.CurrentOrder.Identifier == order.Identifier && c.CurrentOrder.TargetEntity == order.TargetEntity)
                    .ThenByDescending(c => c.CurrentOrder == null || c.CurrentOrder.Identifier == dismissedOrderPrefab.Identifier)
                    .ThenBy(c => c.CurrentOrder != null && c.CurrentOrder.Identifier == order.Identifier && c.CurrentOrder.TargetEntity == order.TargetEntity)
                    .ThenByDescending(c => order.HasAppropriateJob(c))
                    .ThenBy(c => c.CurrentOrder?.Weight)
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
