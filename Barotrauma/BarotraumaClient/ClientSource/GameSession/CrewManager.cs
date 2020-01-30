using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
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
        private GUILayoutGroup activeCrew;
        private GUIFrame crewList;
        private GUIButton toggleCrewButton;
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
            }
        }

        public List<GUIButton> OrderOptionButtons = new List<GUIButton>();

        private Sprite jobIndicatorBackground;

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

            crewArea = new GUIFrame(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.CrewArea, guiFrame.RectTransform), "", Color.Transparent)
            {
                CanBeFocused = false
            };

            // Based on the sprite dimensions
            var buttonSize = new Point((int)(79.0f / 126.0f * crewArea.Rect.Height), crewArea.Rect.Height);

            var commandButton = new GUIButton(
                new RectTransform(buttonSize, parent: crewArea.RectTransform, anchor: Anchor.CenterRight),
                style: null)
            {
                OnClicked = (button, userData) =>
                {
                    ToggleCommandUI();
                    return true;
                }
            };

            new GUIImage(
                new RectTransform(Vector2.One, parent: commandButton.RectTransform),
                new Sprite("Content/UI/InventoryUIAtlas.png", new Rectangle(551, 1, 79, 126)),
                scaleToFit: true)
            {
                Color = GUIColorSettings.InventorySlotColor * 0.8f,
                HoverColor = GUIColorSettings.InventorySlotColor,
                PressedColor = GUIColorSettings.InventorySlotColor,
                SelectedColor = GUIColorSettings.InventorySlotColor * 0.8f,
                ToolTip = TextManager.Get("inputtype.command")
            };

            activeCrew = new GUILayoutGroup(
                new RectTransform(
                    new Point(crewArea.Rect.Width - commandButton.Rect.Width - HUDLayoutSettings.Padding, crewArea.Rect.Height),
                    parent: crewArea.RectTransform,
                    anchor: Anchor.CenterLeft),
                isHorizontal: true,
                childAnchor: Anchor.CenterRight)
            {
                AbsoluteSpacing = (int)(GUI.Scale * 5)
            };

            // AbsoluteOffset is set in UpdateProjectSpecific based on crewListOpenState
            crewList = new GUIFrame(
                new RectTransform(
                    new Point(
                        Math.Min(crewArea.Rect.Height * 10, 500),
                        Math.Min(crewArea.Rect.Height * 8, 400)),
                    parent: crewArea.RectTransform,
                    anchor: Anchor.BottomRight,
                    pivot: Pivot.TopCenter));

            var listBox = new GUIListBox(
                new RectTransform(
                    new Point((int)(crewList.Rect.Width / 2.0f - HUDLayoutSettings.Padding * 2), crewList.Rect.Height - HUDLayoutSettings.Padding * 4),
                    parent: crewList.RectTransform,
                    anchor: Anchor.CenterLeft)
                {
                    AbsoluteOffset = new Point(HUDLayoutSettings.Padding * 2, 0),
                },
                style: null)
            {
                AutoHideScrollBar = false,
                Spacing = (int)(GUI.Scale * 10)
            };

            // Based on the sprite dimensions
            buttonSize = new Point((int)(78.0f / 126.0f * crewArea.Rect.Height), crewArea.Rect.Height);

            toggleCrewButton = new GUIButton(
                new RectTransform(buttonSize, parent: crewList.RectTransform, pivot: Pivot.TopRight)
                {
                    AbsoluteOffset = new Point(-HUDLayoutSettings.Padding, 0)
                },
                style: null);
            toggleCrewButton.OnClicked = (GUIButton btn, object userdata) =>
            {
                ToggleCrewListOpen = !ToggleCrewListOpen;
                return true;
            };

            new GUIImage(
                new RectTransform(Vector2.One, parent: toggleCrewButton.RectTransform),
                new Sprite("Content/UI/InventoryUIAtlas.png", new Rectangle(891, 135, 78, 126)),
                scaleToFit: true)
            {
                Color = GUIColorSettings.InventorySlotColor * 0.8f,
                HoverColor = GUIColorSettings.InventorySlotColor,
                PressedColor = GUIColorSettings.InventorySlotColor,
                SelectedColor = GUIColorSettings.InventorySlotColor * 0.8f,
                ToolTip = TextManager.Get("crew")
            };

            jobIndicatorBackground = new Sprite("Content/UI/CommandUIAtlas.png", new Rectangle(0, 512, 128, 128));

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
                        return true;
                    }
                };

                ChatBox.InputBox.OnTextChanged += ChatBox.TypingChatMessage;
            }

            #endregion

            #region Reports

            var reports = Order.PrefabList.FindAll(o => o.TargetAllCharacters && o.SymbolSprite != null);
            if (reports.None())
            {
                DebugConsole.ThrowError("No valid orders for report buttons found! Cannot create report buttons. The orders for the report buttons must have 'targetallcharacters' attribute enabled and a valid 'symbolsprite' defined.");
                return;
            }
            ReportButtonFrame = new GUILayoutGroup(new RectTransform(
                new Point((HUDLayoutSettings.ChatBoxArea.Height - (int)((reports.Count - 1) * 5 * GUI.Scale)) / reports.Count, HUDLayoutSettings.ChatBoxArea.Height), guiFrame.RectTransform))
            {
                AbsoluteSpacing = (int)(5 * GUI.Scale),
                UserData = "reportbuttons",
                CanBeFocused = false
            };

            //report buttons
            foreach (Order order in reports)
            {
                if (!order.TargetAllCharacters || order.SymbolSprite == null) { continue; }
                var btn = new GUIButton(new RectTransform(new Point(ReportButtonFrame.Rect.Width), ReportButtonFrame.RectTransform), style: null)
                {
                    OnClicked = (GUIButton button, object userData) =>
                    {
                        if (Character.Controlled == null || Character.Controlled.SpeechImpediment >= 100.0f) { return false; }
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

                new GUIFrame(new RectTransform(new Vector2(1.5f), btn.RectTransform, Anchor.Center), "OuterGlow")
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

        private GUIComponent AddCharacterToActiveCrew(Character character)
        {
            int size = HUDLayoutSettings.CrewArea.Height;
            int iconSize = (int)(size * 0.9f);

            var characterFrame = new GUIFrame(new RectTransform(new Point(size), activeCrew.RectTransform, Anchor.Center), style: null)
            {
                UserData = character,
                CanBeFocused = false
            };

            var characterToolTip = character.Info?.Name;
            if (character.Info?.Job != null)
            {
                characterToolTip += " (" + character.Info.Job?.Name + ")";
            }
            var tooltipColor = character.Info?.Job.Prefab?.UIColor;
            var tooltipColorData = tooltipColor != null ? new List<ColorData>() { new ColorData() { Color = (Color)tooltipColor, EndIndex = characterToolTip.Length } } : null;

            var characterButton = new GUIButton(new RectTransform(Vector2.One, characterFrame.RectTransform, Anchor.Center), style: null)
            {
                UserData = character,
                ToolTip = characterToolTip,
                TooltipColorData = tooltipColorData
            };

            #region Sound Icon

            new GUIImage(
                new RectTransform(new Vector2(0.4f), characterFrame.RectTransform, anchor: Anchor.TopRight),
                sprite: GUI.Style.GetComponentStyle("GUISoundIcon").Sprites[GUIComponent.ComponentState.None].FirstOrDefault().Sprite,
                scaleToFit: true)
            {
                UserData = new Pair<string, float>("soundicon", 0.0f),
                CanBeFocused = false,
                Visible = true
            };
            new GUIImage(
                new RectTransform(new Vector2(0.5f), characterFrame.RectTransform, anchor: Anchor.TopRight),
                "GUISoundIconDisabled")
            {
                UserData = "soundicondisabled",
                CanBeFocused = true,
                Visible = false
            };

            #endregion

            if (IsSinglePlayer)
            {
                characterButton.OnClicked = CharacterClicked;
            }
            else
            {
                characterButton.CanBeSelected = false;
            }

            new GUICustomComponent(
                new RectTransform(new Point(iconSize), parent: characterFrame.RectTransform, anchor: Anchor.Center),
                onDraw: (sb, component) => character.Info.DrawIcon(sb, component.Rect.Center.ToVector2(), targetAreaSize: component.Rect.Size.ToVector2()))
            {
                CanBeFocused = false,
                HoverColor = Color.White,
                SelectedColor = Color.White,
                ToolTip = characterToolTip,
                TooltipColorData = tooltipColorData
            };

            if (character?.Info?.Job.Prefab?.Icon != null)
            {
                new GUIImage(
                    new RectTransform(new Vector2(0.5f), characterFrame.RectTransform, anchor: Anchor.BottomLeft),
                    jobIndicatorBackground,
                    scaleToFit: true)
                {
                    CanBeFocused = false
                };
                new GUIImage(
                    new RectTransform(new Vector2(0.5f), characterFrame.RectTransform, anchor: Anchor.BottomLeft),
                    character.Info.Job.Prefab.Icon,
                    scaleToFit: true)
                {
                    CanBeFocused = false,
                    Color = character.Info.Job.Prefab.UIColor,
                    ToolTip = characterToolTip,
                    TooltipColorData = tooltipColorData
                };
            }

            #region Combat Mission
            /*if (GameMain.GameSession?.GameMode?.Mission is CombatMission combatMission)
            {
                new GUIFrame(new RectTransform(Vector2.One, characterArea.RectTransform), style: "InnerGlow",
                    color: character.TeamID == Character.TeamType.Team1 ? Color.SteelBlue : Color.OrangeRed);
            }*/
            #endregion

            return characterFrame;
        }

        private void AddCharacterToCrewList(Character character)
        {
            GUIListBox listBox = (GUIListBox)crewList.FindChild(c => c is GUIListBox);
            int height = Math.Min(crewArea.Rect.Height, (int)(listBox.Content.Rect.Width * 0.3f));
            var characterButton = new GUIButton(new RectTransform(new Point(listBox.Content.Rect.Width, height), parent: listBox.Content.RectTransform), style: null)
            {
                UserData = character
            };

            if (IsSinglePlayer)
            {
                characterButton.OnClicked = CharacterClicked;
            }
            else
            {
                characterButton.CanBeSelected = false;
            }

            var characterIcon = new GUICustomComponent(
                new RectTransform(
                    new Point(height),
                    parent: characterButton.RectTransform,
                    anchor: Anchor.CenterLeft),
                onDraw: (sb, component) => character.Info.DrawIcon(sb, component.Rect.Center.ToVector2(), targetAreaSize: component.Rect.Size.ToVector2()))
            {
                CanBeFocused = false,
                HoverColor = Color.White,
                SelectedColor = Color.White
            };

            if (character?.Info?.Job.Prefab?.Icon != null)
            {
                new GUIImage(
                    new RectTransform(new Vector2(0.5f), characterIcon.RectTransform, anchor: Anchor.BottomLeft, pivot: Pivot.BottomLeft),
                    jobIndicatorBackground,
                    scaleToFit: true)
                {
                    CanBeFocused = false
                };
                new GUIImage(
                    new RectTransform(new Vector2(0.5f), characterIcon.RectTransform, anchor: Anchor.BottomLeft, pivot: Pivot.BottomLeft),
                    character.Info.Job.Prefab.Icon,
                    scaleToFit: true)
                {
                    CanBeFocused = false,
                    Color = character.Info.Job.Prefab.UIColor
                };
            }

            new GUITextBlock(
                new RectTransform(new Point(characterButton.Rect.Width - characterIcon.Rect.Width, height), characterButton.RectTransform, anchor: Anchor.CenterRight)
                {
                    AbsoluteOffset = new Point(HUDLayoutSettings.Padding, 0)
                },
                character.Name + "\n" + character.Info?.Job?.Name,
                textColor: character.Info?.Job?.Prefab?.UIColor);

            GUITextBlock.AutoScaleAndNormalize(listBox.Content.GetAllChildren<GUITextBlock>(), defaultScale: 1.0f);
        }

        /// <summary>
        /// Sets which character is selected in the crew UI (highlight effect etc)
        /// </summary>
        public bool CharacterClicked(GUIComponent component, object selection)
        {
            if (!AllowCharacterSwitch) { return false; }
            Character character = selection as Character;
            if (character == null || character.IsDead || character.IsUnconscious) return false;
            SelectCharacter(character);
            return true;
        }

        public void ReviveCharacter(Character revivedCharacter)
        {
            if (activeCrew.GetChildByUserData(revivedCharacter) is GUIComponent characterBlock)
            {
                characterBlock.Parent.RemoveChild(characterBlock);
            }
            if (characterInfos.Contains(revivedCharacter.Info)) AddCharacter(revivedCharacter);
        }

        public void KillCharacter(Character killedCharacter)
        {
            if (activeCrew.GetChildByUserData(killedCharacter) is GUIComponent characterBlock)
            {
                CoroutineManager.StartCoroutine(KillCharacterAnim(characterBlock));
            }
            else if (crewList.FindChild(c => c is GUIListBox) is GUIListBox listBox &&
                     listBox.Content.GetChildByUserData(killedCharacter) is GUIComponent characterComponent)
            {
                listBox.Content.RemoveChild(characterComponent);
                GUITextBlock.AutoScaleAndNormalize(listBox.Content.GetAllChildren<GUITextBlock>(), defaultScale: 1.0f);
                listBox.UpdateScrollBarSize();
            }
            RemoveCharacter(killedCharacter);
        }

        private IEnumerable<object> KillCharacterAnim(GUIComponent component)
        {
            List<GUIComponent> components = component.GetAllChildren().ToList();
            components.Add(component);
            components.RemoveAll(c => 
                c.UserData is Pair<string, float> pair && pair.First == "soundicon" || 
                c.UserData as string == "soundicondisabled" ||
                c is GUIButton || c is GUIFrame);

            components.ForEach(c => c.Color = Color.DarkRed);

            yield return new WaitForSeconds(1.0f);

            float timer = 0.0f;
            float hideDuration = 1.0f;
            while (timer < hideDuration)
            {
                foreach (GUIComponent comp in components)
                {
                    comp.Color = Color.Lerp(Color.DarkRed, Color.Transparent, timer / hideDuration);
                    comp.RectTransform.LocalScale = new Vector2(1.0f - (timer / hideDuration), comp.RectTransform.LocalScale.Y);
                }
                timer += CoroutineManager.DeltaTime;
                yield return CoroutineStatus.Running;
            }
            activeCrew.RemoveChild(component);
            activeCrew.Recalculate();

            var list = (GUIListBox)crewList.FindChild(c => c is GUIListBox);
            var crewListComponent = list.Content.GetChildByUserData(component.UserData);
            list.Content.RemoveChild(crewListComponent);
            GUITextBlock.AutoScaleAndNormalize(list.Content.GetAllChildren<GUITextBlock>(), defaultScale: 1.0f);
            list.UpdateScrollBarSize();

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

            var playerFrame = activeCrew.GetChildByUserData(client.Character) ?? AddCharacterToActiveCrew(client.Character);

            if (playerFrame == null) { return; }

            var soundIcon = playerFrame.FindChild(c => c.UserData is Pair<string, float> pair && pair.First == "soundicon");
            var soundIconDisabled = playerFrame.FindChild("soundicondisabled");
            soundIcon.Visible = !muted && !mutedLocally;
            soundIconDisabled.Visible = muted || mutedLocally;
            soundIconDisabled.ToolTip = TextManager.Get(mutedLocally ? "MutedLocally" : "MutedGlobally");
        }

        public void SetClientSpeaking(Client client)
        {
            if (client?.Character != null) { SetCharacterSpeaking(client.Character); }
        }

        public void SetCharacterSpeaking(Character character)
        {
            var playerFrame = activeCrew.GetChildByUserData(character);
            if (playerFrame == null && character != Character.Controlled)
            {
                 playerFrame = AddCharacterToActiveCrew(character);
            }

            if (playerFrame == null) { return; }

            var soundIcon = playerFrame.FindChild(c => c.UserData is Pair<string, float> pair && pair.First == "soundicon");
            soundIcon.Color = Color.White;
            Pair<string, float> userdata = soundIcon.UserData as Pair<string, float>;
            userdata.Second = 1.0f;
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
                    if (GameMain.Client != null)
                    {
                        GameMain.Client.SendChatMessage(msg);
                    }
                }
            }
            else
            {
                character.SetOrder(order, option, orderGiver, speak: orderGiver != character);
                if (IsSinglePlayer)
                {
                    orderGiver?.Speak(
                        order.GetChatMessage(character.Name, orderGiver.CurrentHull?.DisplayName, givingOrderToSelf: character == orderGiver, orderOption: option), null);
                }
                else if (orderGiver != null)
                {
                    OrderChatMessage msg = new OrderChatMessage(order, option, order.TargetItemComponent?.Item, character, orderGiver);
                    if (GameMain.Client != null)
                    {
                        GameMain.Client.SendChatMessage(msg);
                    }
                }
                DisplayCharacterOrder(character, order);
            }
        }

        /// <summary>
        /// Displays the specified order in the crew UI next to the character.
        /// </summary>
        public void DisplayCharacterOrder(Character character, Order order)
        {
            if (character == null) { return; }

            var characterFrame = activeCrew.GetChildByUserData(character);
            if (characterFrame != null && characterFrame.GetChildByUserData("order") is GUIComponent existingOrderFrame)
            {
                characterFrame.RemoveChild(existingOrderFrame);
            }

            if (order == null || order == dismissedOrder)
            {
                if (characterFrame != null)
                {
                    // Remove dismissed characters from active crew
                    activeCrew.RemoveChild(characterFrame);
                    activeCrew.Recalculate();
                }
                return;
            }

            characterFrame ??= AddCharacterToActiveCrew(character);

            var orderFrame = new GUIButton(
                new RectTransform(new Vector2(0.5f), characterFrame.RectTransform, Anchor.BottomCenter, Pivot.TopCenter)
                {
                    AbsoluteOffset = new Point(0, -HUDLayoutSettings.Padding)
                },
                style: null)
            {
                UserData = "order",
                OnClicked = (button, userData) =>
                {
                    SetCharacterOrder(character, dismissedOrder, null, Character.Controlled);
                    character.SetOrder(null, null, Character.Controlled);
                    return true;
                }
            };
            CreateNodeIcon(orderFrame.RectTransform, order.SymbolSprite, order.Color, order.Color, tooltip: order.Name);
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
                var previousCrewList = (GUIListBox)crewList.FindChild(c => c is GUIListBox);
                InitProjectSpecific();

                foreach (GUIComponent c in previousCrewList.Content.Children)
                {
                    if (!(c.UserData is Character character) || character.IsDead || character.Removed) { continue; }
                    AddCharacter(character);
                    DisplayCharacterOrder(character, character.CurrentOrder);
                }
            }

            guiFrame.AddToGUIUpdateList();
        }

        public void SelectNextCharacter()
        {
            if (!AllowCharacterSwitch) { return; }
            if (GameMain.IsMultiplayer) { return; }
            if (characters.None()) { return; }
            SelectCharacter(characters[TryAdjustIndex(1)]);
        }

        public void SelectPreviousCharacter()
        {
            if (!AllowCharacterSwitch) { return; }
            if (GameMain.IsMultiplayer) { return; }
            if (characters.None()) { return; }
            SelectCharacter(characters[TryAdjustIndex(-1)]);
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
            int index = Character.Controlled == null ? 0 : characters.IndexOf(Character.Controlled) + amount;
            int lastIndex = characters.Count - 1;
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

            if (PlayerInput.KeyDown(InputType.Command) && GUI.KeyboardDispatcher.Subscriber == null &&
                (!GameMain.IsMultiplayer || (GameMain.IsMultiplayer && DebugConsole.CheatsEnabled)) &&
                commandFrame == null && !clicklessSelectionActive)
            {
                bool canIssueOrders = false;
                if (Character.Controlled != null && Character.Controlled.SpeechImpediment < 100.0f)
                {
                    WifiComponent radio = GetHeadset(Character.Controlled, true);
                    canIssueOrders = radio != null && radio.CanTransmit();
                }

                if (canIssueOrders)
                {
                    CreateCommandUI();
                    clicklessSelectionActive = isOpeningClick = true;
                }
            }

            if (commandFrame != null)
            {
                if ((GameMain.IsMultiplayer && !DebugConsole.CheatsEnabled && Character.Controlled == null))
                {
                    DisableCommandUI();
                }
                else if (PlayerInput.RightButtonClicked() &&
                         (optionNodes.Any(n => GUI.IsMouseOn(n)) || shortcutNodes.Any(n => GUI.IsMouseOn(n))))
                {
                    var node = optionNodes.Find(n => GUI.IsMouseOn(n));
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
                         PlayerInput.KeyHit(InputType.Deselect) || PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.Escape))
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
                            selectedNode.Children.ForEach(c => c.Color = c.HoverColor * nodeColorMultiplier);
                            selectedNode = null;
                            timeSelected = 0;
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

                        optionNodes.ForEach(n => CheckIfClosest(n));
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
                                selectedNode.OnClicked.Invoke(selectedNode, selectedNode.UserData);
                                selectedNode = null;
                                timeSelected = 0;
                            }
                        }
                        else
                        {
                            if (selectedNode != null)
                            {
                                selectedNode.Children.ForEach(c => c.Color = c.HoverColor * nodeColorMultiplier);
                            }
                            selectedNode = closestNode as GUIButton;
                            selectedNode.Children.ForEach(c => c.Color = c.HoverColor);
                            timeSelected = 0;
                        }
                    }
                    else if (selectedNode != null)
                    {
                        selectedNode.Children.ForEach(c => c.Color = c.HoverColor * nodeColorMultiplier);
                        selectedNode = null;
                        timeSelected = 0;
                    }
                }
            }
            else if (PlayerInput.KeyUp(InputType.Command))
            {
                clicklessSelectionActive = false;
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
                        ChatBox.GUIFrame.Flash(Color.DarkGreen, 0.5f);
                        ChatBox.InputBox.Select();
                    }

                    if (PlayerInput.KeyHit(InputType.RadioChat) && !ChatBox.InputBox.Selected)
                    {
                        ChatBox.GUIFrame.Flash(Color.YellowGreen, 0.5f);
                        ChatBox.InputBox.Select();
                        ChatBox.InputBox.Text = "r; ";
                    }
                }
            }

            crewArea.Visible = characters.Count > 0 && CharacterHealth.OpenHealthWindow == null;

            var shouldBeRemoved = new List<GUIComponent>();
            foreach (GUIComponent child in activeCrew.Children)
            {
                Character character = (Character)child.UserData;
                if (character == null) { continue; }
                child.Visible =
                    Character.Controlled == null ||
                    (Character.Controlled.TeamID == character.TeamID);

                if (child.Visible)
                {
                    //child.GetChildByUserData("highlight").Visible = character == Character.Controlled;
                    var soundIcon = child.FindChild(c => c.UserData is Pair<string, float> pair && pair.First == "soundicon") as GUIImage;
                    VoipClient.UpdateVoiceIndicator(soundIcon, 0.0f, deltaTime);
                    if (soundIcon.UserData is Pair<string, float> soundStatus &&
                        soundStatus.Second < 0.1f && child.FindChild("order") == null)
                    {
                        shouldBeRemoved.Add(child);
                    }
                }
            }
            if (shouldBeRemoved.Any())
            {
                shouldBeRemoved.ForEach(c => activeCrew.RemoveChild(c));
                activeCrew.Recalculate();
            }

            crewList.RectTransform.AbsoluteOffset = Vector2.SmoothStep(
                    new Vector2(-HUDLayoutSettings.Padding - crewList.Rect.Width / 2.0f, -HUDLayoutSettings.Padding * 6),
                    new Vector2(0.0f, -HUDLayoutSettings.Padding * 6),
                    crewListOpenState).ToPoint();
            crewListOpenState = ToggleCrewListOpen ?
                Math.Min(crewListOpenState + deltaTime * 2.0f, 1.0f) :
                Math.Max(crewListOpenState - deltaTime * 2.0f, 0.0f);

            if (GUI.KeyboardDispatcher.Subscriber == null &&
                PlayerInput.KeyHit(InputType.CrewOrders) &&
                characters.Contains(Character.Controlled))
            {
                //deselect construction unless it's the ladders the character is climbing
                if (Character.Controlled != null && !Character.Controlled.IsClimbing)
                {
                    Character.Controlled.SelectedConstruction = null;
                }
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
                return commandFrame != null;
            }
        }
        private static GUIFrame commandFrame, targetFrame;
        private static GUIButton centerNode, returnNode, expandNode, shortcutCenterNode;
        private static List<GUIComponent> optionNodes = new List<GUIComponent>();
        private static List<GUIComponent> shortcutNodes = new List<GUIComponent>();
        private static List<GUIComponent> extraOptionNodes = new List<GUIComponent>();
        private static GUICustomComponent nodeConnectors;
        private static GUIImage background;

        private static GUIButton selectedNode;
        private static float selectionTime = 0.75f, timeSelected = 0.0f;
        private static bool clicklessSelectionActive, isOpeningClick;

        private Point centerNodeSize, nodeSize, shortcutCenterNodeSize, shortcutNodeSize, returnNodeSize;
        private float centerNodeMargin, optionNodeMargin, shortcutCenterNodeMargin, shortcutNodeMargin, returnNodeMargin;

        private List<OrderCategory> availableCategories;
        private static Stack<GUIButton> historyNodes = new Stack<GUIButton>();
        private static List<Character> extraOptionCharacters = new List<Character>();

        /// <summary>
        /// node.Color = node.HighlightColor * nodeColorMultiplier
        /// </summary>
        private const float nodeColorMultiplier = 0.75f;
        private const int assignmentNodeMaxCount = 5;
        private int nodeDistance = 150;
        private float returnNodeDistanceModifier = 0.75f;
        private Order dismissedOrder;

        private void CreateCommandUI()
        {
            ScaleCommandUI();
            commandFrame = new GUIFrame(
                new RectTransform(Vector2.Zero, GUICanvas.Instance, anchor: Anchor.Center),
                style: null,
                color: Color.Transparent);
            background = new GUIImage(
                new RectTransform(Vector2.One, commandFrame.RectTransform, anchor: Anchor.Center),
                Order.CommandBackground);
            background.Color = background.Color * 0.8f;
            var startNode = new GUIButton(
                new RectTransform(centerNodeSize, parent: commandFrame.RectTransform, anchor: Anchor.Center),
                style: null);
            CreateNodeIcon(startNode.RectTransform, Order.StartNode, Color.White, Color.White);
            SetCenterNode(startNode);

            if (availableCategories == null)
            {
                GetAvailableCategories();
            }
            if (dismissedOrder == null)
            {
                dismissedOrder = Order.GetPrefab("dismissed");
            }

            CreateShortcutNodes();
            CreateOrderCategoryNodes();
            CreateNodeConnectors();
        }

        private void ToggleCommandUI()
        {
            if (commandFrame == null)
            {
                CreateCommandUI();
            }
            else
            {
                DisableCommandUI();
            }
        }

        private void ScaleCommandUI()
        {
            centerNodeSize = new Point((int)(80 * GUI.Scale));
            nodeSize = new Point((int)(80 * GUI.Scale));
            shortcutCenterNodeSize = new Point((int)(48 * GUI.Scale));
            shortcutNodeSize = new Point((int)(64 * GUI.Scale));
            returnNodeSize = new Point((int)(48 * GUI.Scale));
            centerNodeMargin = centerNodeSize.X * 0.6f;
            optionNodeMargin = nodeSize.X * 0.6f;
            shortcutCenterNodeMargin = shortcutCenterNodeSize.X * 0.45f;
            shortcutNodeMargin = shortcutNodeSize.X * 0.6f;
            returnNodeMargin = returnNodeSize.X * 0.6f;
            nodeDistance = (int)(150 * GUI.Scale);
        }

        private void GetAvailableCategories()
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
                optionNodes.ForEach(n => DrawNodeConnector(startNodePos, centerNodeMargin, n, optionNodeMargin, spriteBatch));
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
            var colorSource = endNode.GetChildByUserData("container");
            if (colorSource == null) { colorSource = endNode.GetChildByUserData("icon"); }
            if ((selectedNode == null && endNode != shortcutCenterNode && GUI.IsMouseOn(endNode)) ||
                endNode == selectedNode || endNode == shortcutCenterNode && shortcutNodes.Any(n => GUI.IsMouseOn(n)))
            {
                GUI.DrawLine(spriteBatch, start, end, colorSource != null ? colorSource.HoverColor : Color.White, width: 4);
            }
            else
            {
                GUI.DrawLine(spriteBatch, start, end, colorSource != null ? colorSource.Color : Color.White * nodeColorMultiplier, width: 2);
            }
        }

        public static void DisableCommandUI()
        {
            if (commandFrame == null) { return; }
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
            clicklessSelectionActive = isOpeningClick = false;
        }

        private bool NavigateForward(GUIButton node, object userData)
        {
            if (!optionNodes.Remove(node)) { shortcutNodes.Remove(node); };
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
            SetCenterNode(node);
            if (shortcutCenterNode != null)
            {
                commandFrame.RemoveChild(shortcutCenterNode);
                shortcutCenterNode = null;
            }
            CreateNodes(userData);
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
                historyNode.Children.ForEach(child => child.Visible = true);
            }
            else
            {
                returnNode = null;
            }
            CreateNodes(userData);
            return true;
        }

        private void SetCenterNode(GUIButton node)
        {
            node.RectTransform.Parent = commandFrame.RectTransform;
            node.RectTransform.NonScaledSize = centerNodeSize;
            node.RectTransform.AbsoluteOffset = Point.Zero;
            foreach (GUIComponent c in node.Children)
            {
                c.Color = c.HoverColor * nodeColorMultiplier;
                c.HoverColor = c.Color;
                c.PressedColor = c.Color;
                c.SelectedColor = c.Color;
                c.ToolTip = null;
            }
            node.OnClicked = null;
            centerNode = node;
        }

        private void SetReturnNode(GUIButton node, Point offset)
        {
            node.RectTransform.NonScaledSize = returnNodeSize;
            node.RectTransform.AbsoluteOffset = offset;
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

        private static void RemoveOptionNodes()
        {
            optionNodes.ForEach(node => commandFrame.RemoveChild(node));
            optionNodes.Clear();
            shortcutNodes.ForEach(node => commandFrame.RemoveChild(node));
            shortcutNodes.Clear();
            commandFrame.RemoveChild(expandNode);
            RemoveExtraOptionNodes();
        }

        private static void RemoveExtraOptionNodes()
        {
            extraOptionNodes.ForEach(node => commandFrame.RemoveChild(node));
            extraOptionNodes.Clear();
        }

        private void CreateOrderCategoryNodes()
        {
            var points = shortcutCenterNode != null ?
                GetCircumferencePointCount(availableCategories.Count) :
                availableCategories.Count;
            var firstAngle = shortcutCenterNode != null ?
                GetFirstNodeAngle(availableCategories.Count) :
                0.0f;
            var offsets = MathUtils.GetPointsOnCircumference(Vector2.Zero, nodeDistance, points, firstAngle);
            var offsetIndex = 0;
            availableCategories.ForEach(oc => CreateOrderCategoryNode(oc, offsets[offsetIndex++].ToPoint()));
        }

        private void CreateOrderCategoryNode(OrderCategory category, Point offset)
        {
            var node = new GUIButton(
                new RectTransform(nodeSize, parent: commandFrame.RectTransform, anchor: Anchor.Center)
                {
                    AbsoluteOffset = offset
                },
                style: null)
            {
                UserData = category,
                OnClicked = NavigateForward
            };
            if (Order.OrderCategoryIcons.TryGetValue(category, out Sprite sprite))
            {
                var tooltip = TextManager.Get("ordercategorytitle." + category.ToString().ToLower());
                var categoryDescription = TextManager.Get("ordercategorydescription." + category.ToString(), true);
                if (!string.IsNullOrWhiteSpace(categoryDescription)) { tooltip += "\n" + categoryDescription; }
                CreateNodeIcon(node.RectTransform, sprite, Color.White, Color.White, tooltip: tooltip);
            }
            optionNodes.Add(node);
        }

        private void CreateShortcutNodes()
        {
            shortcutNodes.Clear();

            var sub = Character.Controlled != null && Character.Controlled.TeamID == Character.TeamType.Team2 && Submarine.MainSubs.Length > 1 ?
                Submarine.MainSubs[1] : Submarine.MainSub;
            var reactor = sub.GetItems(false).Find(i => i.HasTag("reactor")).GetComponent<Reactor>();
            var reactorOutput = -reactor.CurrPowerConsumption;

            // If player is not an engineer AND the reactor is not powered up AND nobody is using the reactor
            // ---> Create shortcut node for "Operate Reactor" order's "Power Up" option
            if ((Character.Controlled == null || Character.Controlled.Info.Job.Prefab != JobPrefab.Get("engineer")) &&
                reactorOutput < float.Epsilon && characters.None(c => c.SelectedConstruction == reactor.Item))
            {
                var order = new Order(Order.GetPrefab("operatereactor"), reactor.Item, reactor, Character.Controlled);
                shortcutNodes.Add(
                    CreateOrderOptionNode(shortcutNodeSize, null, Point.Zero, order, order.Prefab.Options[0], order.Prefab.OptionNames[0]));
            }

            // TODO: Reconsider the conditions as bot captain can have the nav term selected without operating it
            // If player is not a captain AND nobody is using the nav terminal AND the nav terminal is powered up
            // --> Create shortcut node for Steer order
            if ((Character.Controlled == null || Character.Controlled.Info.Job.Prefab != JobPrefab.Get("captain")) &&
                sub.GetItems(false).Find(i => i.HasTag("navterminal")) is Item nav && characters.None(c => c.SelectedConstruction == nav) &&
                nav.GetComponent<Steering>() is Steering steering && steering.Voltage > steering.MinVoltage)
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, Order.GetPrefab("steer")));
            }

            // If player is not a security officer AND invaders are reported
            // --> Create shorcut node for Fight Intruders order
            if ((Character.Controlled == null || Character.Controlled.Info.Job.Prefab != JobPrefab.Get("securityofficer")) &&
                (Order.GetPrefab("reportintruders") is Order reportIntruders && ActiveOrders.Any(o => o.First.Prefab == reportIntruders)))
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, Order.GetPrefab("fightintruders")));
            }

            // If player is not a mechanic AND a breach has been reported
            // --> Create shorcut node for Fix Leaks order
            if ((Character.Controlled == null || Character.Controlled.Info.Job.Prefab != JobPrefab.Get("mechanic")) &&
                (Order.GetPrefab("reportbreach") is Order reportBreach && ActiveOrders.Any(o => o.First.Prefab == reportBreach)))
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, Order.GetPrefab("fixleaks")));
            }

            // If player is not an engineer AND broken devices have been reported
            // --> Create shortcut node for Repair Damaged Systems order
            if ((Character.Controlled == null || Character.Controlled.Info.Job.Prefab != JobPrefab.Get("engineer")) &&
                (Order.GetPrefab("reportbrokendevices") is Order reportBrokenDevices && ActiveOrders.Any(o => o.First.Prefab == reportBrokenDevices)))
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, Order.GetPrefab("repairsystems")));
            }

            // If fire is reported
            // --> Create shortcut node for Extinguish Fires order
            if (ActiveOrders.Any(o=> o.First.Prefab == Order.GetPrefab("reportfire")))
            {
                shortcutNodes.Add(
                    CreateOrderNode(shortcutNodeSize, null, Point.Zero, Order.GetPrefab("extinguishfires")));
            }

            if (shortcutNodes.Count < 1) { return; }

            shortcutCenterNode = new GUIButton(
                new RectTransform(shortcutCenterNodeSize, parent: commandFrame.RectTransform, anchor: Anchor.Center)
                {
                    AbsoluteOffset = new Point(0, (int)(1.25f * nodeDistance))
                },
                style: null);
            CreateNodeIcon(shortcutCenterNode.RectTransform, Order.ShortcutNode, Color.Red, Color.Red, createContainer: false);
            foreach (GUIComponent c in shortcutCenterNode.Children)
            {
                c.HoverColor = c.Color;
                c.PressedColor = c.Color;
                c.SelectedColor = c.Color;
            }

            var nodeCountForCalculations = shortcutNodes.Count * 2 + 2;
            var offsets = MathUtils.GetPointsOnCircumference(Vector2.Zero, 0.75f * nodeDistance, nodeCountForCalculations);
            for (int i = 0; i < shortcutNodes.Count; i++)
            {
                shortcutNodes[i].RectTransform.Parent = commandFrame.RectTransform;
                shortcutNodes[i].RectTransform.AbsoluteOffset = shortcutCenterNode.RectTransform.AbsoluteOffset + offsets[i + 1].ToPoint();
            }
        }

        private void CreateOrderNodes(OrderCategory orderCategory)
        {
            var orders = Order.PrefabList.FindAll(o => o.Category == orderCategory && !o.TargetAllCharacters);
            var offsets = MathUtils.GetPointsOnCircumference(Vector2.Zero, nodeDistance,
                GetCircumferencePointCount(orders.Count), GetFirstNodeAngle(orders.Count));
            for(int i = 0; i < orders.Count; i++)
            {
                optionNodes.Add(CreateOrderNode(nodeSize, commandFrame.RectTransform, offsets[i].ToPoint(), orders[i]));
            }
        }

        private GUIButton CreateOrderNode(Point size, RectTransform parent, Point offset, Order order)
        {
            var node = new GUIButton(
                new RectTransform(size, parent: parent, anchor: Anchor.Center)
                {
                    AbsoluteOffset = offset
                },
                style: null)
            {
                UserData = order
            };
            var hasOptions = order.ItemComponentType != null || order.ItemIdentifiers.Length > 0 || order.Options.Length > 1;
            node.OnClicked = (button, userData) =>
            {
                if (Character.Controlled != null && Character.Controlled.SpeechImpediment >= 100.0f) { return false; }
                var o = userData as Order;
                // TODO: Consider defining orders' or order categories' quick-assignment possibility in the XML
                if (o.Category == OrderCategory.Movement)
                {
                    CreateAssignmentNodes(node);
                }
                else if (hasOptions)
                {
                    NavigateForward(button, userData);
                }
                else
                {
                    SetCharacterOrder(GetBestCharacterForOrder(o), o, null, Character.Controlled);
                    DisableCommandUI();
                }
                return true;
            };
            CreateNodeIcon(node.RectTransform, order.SymbolSprite, order.Color, order.Color,
                tooltip: hasOptions ? order.Name :
                    order.Name + "\nLMB: " + TextManager.Get("commandui.quickassigntooltip") + "\nRMB: " + TextManager.Get("commandui.manualassigntooltip"));
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
                        optionNodes.Add(new GUIButton(
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
                                if (GameMain.Client != null && Character.Controlled == null) { return false; }
                                var o = userData as Tuple<Order, string>;
                                SetCharacterOrder(GetBestCharacterForOrder(o.Item1), o.Item1, o.Item2, Character.Controlled);
                                DisableCommandUI();
                                return true;
                            }
                        });
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
                    optionNodes.Add(
                        CreateOrderOptionNode(nodeSize, commandFrame.RectTransform, offsets[offsetIndex++].ToPoint(), o, order.Options[i], order.OptionNames[i]));
                }
            }
        }

        private GUIButton CreateOrderOptionNode(Point size, RectTransform parent, Point offset, Order order, string option, string optionName)
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
                    if (GameMain.Client != null && Character.Controlled == null) { return false; }
                    var o = userData as Tuple<Order, string>;
                    SetCharacterOrder(GetBestCharacterForOrder(o.Item1), o.Item1, o.Item2, Character.Controlled);
                    DisableCommandUI();
                    return true;
                }
            };
            if (order.Prefab.OptionSprites.TryGetValue(option, out Sprite sprite))
            {
                CreateNodeIcon(node.RectTransform, sprite, order.Color, order.Color,
                    tooltip: optionName + "\nLMB: " + TextManager.Get("commandui.quickassigntooltip") + "\nRMB: " + TextManager.Get("commandui.manualassigntooltip"));
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

            if (!optionNodes.Remove(node)) { shortcutNodes.Remove(node); };
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
                var optionNode = new GUIButton(
                    new RectTransform(centerNodeSize, parent: commandFrame.RectTransform, anchor: Anchor.Center),
                    style: null)
                {
                    UserData = node.UserData
                };
                if (order.Item1.Prefab.OptionSprites.TryGetValue(order.Item2, out Sprite sprite))
                {
                    CreateNodeIcon(optionNode.RectTransform, sprite, order.Item1.Color, order.Item1.Color, tooltip: order.Item2);
                }
                SetCenterNode(optionNode);
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
            var offsets = MathUtils.GetPointsOnCircumference(Vector2.Zero, nodeDistance,
                GetCircumferencePointCount(nodeCount),
                GetFirstNodeAngle(nodeCount));

            var i = 0;
            var assignmentNodeCount = (needToExpand ? nodeCount - 1 : nodeCount);
            for (; i < assignmentNodeCount; i++)
            {
                CreateAssignmentNode(order, characters[i], offsets[i].ToPoint());
            }

            if (!needToExpand) { return; }

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
            CreateNodeIcon(expandNode.RectTransform, Order.ExpandNode, order.Item1.Color, order.Item1.Color, tooltip: TextManager.Get("commandui.expand"));
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
            var offsets = MathUtils.GetPointsOnCircumference(Vector2.Zero, nodeDistance * 2,
                GetCircumferencePointCount(extraOptionCharacters.Count),
                GetFirstNodeAngle(extraOptionCharacters.Count));
            for (int i = 0; i < extraOptionCharacters.Count; i++)
            {
                CreateAssignmentNode(order, extraOptionCharacters[i], offsets[i].ToPoint(), extraOption: true);
            }
            return true;
        }

        private void CreateAssignmentNode(Tuple<Order, string> order, Character character, Point offset, bool extraOption = false)
        {
            // Button
            var node = new GUIButton(
                new RectTransform(nodeSize, parent: commandFrame.RectTransform, anchor: Anchor.Center)
                {
                    AbsoluteOffset = offset
                },
                style: null)
            {
                OnClicked = (button, userData) =>
                {
                    SetCharacterOrder(character, order.Item1, order.Item2, Character.Controlled);
                    DisableCommandUI();
                    return true;
                }
            };
            // Character icon
            new GUICustomComponent(
                new RectTransform(Vector2.One, node.RectTransform),
                (spriteBatch, _) =>
                {
                    character.Info.DrawIcon(spriteBatch, node.Center, node.Rect.Size.ToVector2() * 0.75f);
                });
            // Smaller container
            new GUIImage(
                new RectTransform(new Vector2(1.2f), node.RectTransform, anchor: Anchor.Center),
                Order.NodeContainer,
                scaleToFit: true)
            {
                Color = character.Info.Job.Prefab.UIColor * nodeColorMultiplier,
                HoverColor = character.Info.Job.Prefab.UIColor,
                PressedColor = character.Info.Job.Prefab.UIColor,
                SelectedColor = character.Info.Job.Prefab.UIColor,
                UserData = "container"
            };
            // Bigger container
            new GUIImage(
                new RectTransform(new Vector2(1.4f), node.RectTransform, anchor: Anchor.Center),
                Order.NodeContainer,
                scaleToFit: true)
            {
                Color = character.Info.Job.Prefab.UIColor * nodeColorMultiplier,
                HoverColor = character.Info.Job.Prefab.UIColor,
                PressedColor = character.Info.Job.Prefab.UIColor,
                SelectedColor = character.Info.Job.Prefab.UIColor,
                UserData = "container",
                ToolTip = character.Info.DisplayName + " (" + character.Info.Job.Name + ")"
            };
            (extraOption ? extraOptionNodes : optionNodes).Add(node);
        }

        private void CreateNodeIcon(RectTransform parent, Sprite sprite, Color iconColor, Color containerColor, string tooltip = null, bool createContainer = true)
        {
            if (createContainer)
            {
                // Container
                new GUIImage(
                    new RectTransform(new Vector2(1.2f), parent, anchor: Anchor.Center),
                    Order.NodeContainer,
                    scaleToFit: true)
                {
                    Color = containerColor * nodeColorMultiplier,
                    HoverColor = containerColor,
                    PressedColor = containerColor,
                    SelectedColor = containerColor,
                    UserData = "container"
                };
            }
            // Icon
            new GUIImage(
                new RectTransform(Vector2.One, parent),
                sprite,
                scaleToFit: true)
            {
                Color = iconColor * nodeColorMultiplier,
                HoverColor = iconColor,
                PressedColor = iconColor,
                SelectedColor = iconColor,
                ToolTip = tooltip,
                UserData = "icon"
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
                    centerNode.RectTransform.AbsoluteOffset.ToVector2(),
                    returnNode.RectTransform.AbsoluteOffset.ToVector2());
            }
            else if (shortcutCenterNode != null)
            {
                bearing = GetBearing(
                    centerNode.RectTransform.AbsoluteOffset.ToVector2(),
                    shortcutCenterNode.RectTransform.AbsoluteOffset.ToVector2());
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
            return characters.FindAll(c => c != Character.Controlled)
                .OrderByDescending(c => c.CurrentOrder == null || c.CurrentOrder == dismissedOrder)
                .ThenByDescending(c => order.HasAppropriateJob(c))
                .FirstOrDefault();
        }

        private List<Character> GetCharactersSortedForOrder(Order order)
        {
            if (order.Identifier == "follow")
            {
                return characters.FindAll(c => c != Character.Controlled)
                    .OrderBy(c => c.CurrentOrder == null || c.CurrentOrder == dismissedOrder)
                    .ThenBy(c => order.HasAppropriateJob(c))
                    .ToList();
            }
            else
            {
                return characters.OrderBy(c => c.CurrentOrder == null || c.CurrentOrder == dismissedOrder)
                       .ThenBy(c => order.HasAppropriateJob(c))
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
                ReportButtonFrame.Visible = true;

                var reportButtonParent = ChatBox ?? GameMain.Client?.ChatBox;
                if (reportButtonParent == null) { return; }

                /*reportButtonFrame.RectTransform.AbsoluteOffset = new Point(
                    Math.Min(reportButtonParent.GUIFrame.Rect.X, reportButtonParent.ToggleButton.Rect.X) - reportButtonFrame.Rect.Width - (int)(10 * GUI.Scale),
                    reportButtonParent.GUIFrame.Rect.Y);*/

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
            activeCrew.ClearChildren();
            characters.Clear();

            WayPoint[] waypoints = WayPoint.SelectCrewSpawnPoints(characterInfos, Submarine.MainSub);

            for (int i = 0; i < waypoints.Length; i++)
            {
                Character character;
                character = Character.Create(characterInfos[i], waypoints[i].WorldPosition, characterInfos[i].Name);
                if (character.Info != null && !character.Info.StartItemsGiven)
                {
                    character.GiveJobItems(waypoints[i]);
                    character.Info.StartItemsGiven = true;
                }

                if (character.Info?.InventoryData != null)
                {
                    character.Info.SpawnInventoryItems(character.Inventory, character.Info.InventoryData);
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
            activeCrew.ClearChildren();
        }

        public void Reset()
        {
            characters.Clear();
            characterInfos.Clear();
            activeCrew.ClearChildren();
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
