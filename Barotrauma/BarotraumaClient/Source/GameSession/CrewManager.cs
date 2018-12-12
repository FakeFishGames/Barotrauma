using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
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
        const float ChatMessageFadeTime = 10.0f;

        /// <summary>
        /// How long the previously selected character waits doing nothing when switching to another character
        /// </summary>
        const float CharacterWaitOnSwitch = 20.0f;

        const float ConversationIntervalMin = 100.0f;
        const float ConversationIntervalMax = 180.0f;

        private List<CharacterInfo> characterInfos = new List<CharacterInfo>();
        private List<Character> characters = new List<Character>();

        private Point screenResolution;

        public int WinningTeam = 1;

        private float conversationTimer, conversationLineTimer;
        private List<Pair<Character, string>> pendingConversationLines = new List<Pair<Character, string>>();

        #region UI

        private GUIFrame guiFrame;
        private GUIFrame crewArea;
        private GUIListBox characterListBox;

        private GUIComponent reportButtonFrame;

        private GUIButton scrollButtonUp, scrollButtonDown;

        private GUIButton toggleCrewButton;
        private float crewAreaOpenState;
        private bool toggleCrewAreaOpen = true;
        private int characterInfoWidth;

        private ChatBox chatBox;

        private float prevUIScale;
        
        private GUIComponent orderTargetFrame, orderTargetFrameShadow;

        public bool ToggleCrewAreaOpen
        {
            get { return toggleCrewAreaOpen; }
            set { toggleCrewAreaOpen = value; }
        }

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

            Point scrollButtonSize = new Point((int)(200 * GUI.Scale), (int)(30 * GUI.Scale));

            crewArea = new GUIFrame(HUDLayoutSettings.ToRectTransform(HUDLayoutSettings.CrewArea, guiFrame.RectTransform), "", Color.Transparent)
            {
                CanBeFocused = false
            };
            toggleCrewButton = new GUIButton(new RectTransform(new Point((int)(30 * GUI.Scale), HUDLayoutSettings.CrewArea.Height), guiFrame.RectTransform)
            { AbsoluteOffset = HUDLayoutSettings.CrewArea.Location },
                "", style: "UIToggleButton");
            toggleCrewButton.OnClicked += (GUIButton btn, object userdata) =>
            {
                toggleCrewAreaOpen = !toggleCrewAreaOpen;
                foreach (GUIComponent child in btn.Children)
                {
                    child.SpriteEffects = toggleCrewAreaOpen ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
                }
                return true;
            };

            characterListBox = new GUIListBox(new RectTransform(new Point(100, (int)(crewArea.Rect.Height - scrollButtonSize.Y * 1.6f)), crewArea.RectTransform, Anchor.CenterLeft), false, Color.Transparent, null)
            {
                Spacing = (int)(3 * GUI.Scale),
                ScrollBarEnabled = false,
                ScrollBarVisible = false,
                CanBeFocused = false
            };

            scrollButtonUp = new GUIButton(new RectTransform(scrollButtonSize, crewArea.RectTransform, Anchor.TopLeft, Pivot.TopLeft), "", Alignment.Center, "GUIButtonVerticalArrow")
            {
                Visible = false,
                UserData = -1,
                OnClicked = ScrollCharacterList
            };
            scrollButtonDown = new GUIButton(new RectTransform(scrollButtonSize, crewArea.RectTransform, Anchor.BottomLeft, Pivot.BottomLeft), "", Alignment.Center, "GUIButtonVerticalArrow")
            {
                Visible = false,
                UserData = 1,
                OnClicked = ScrollCharacterList
            };
            scrollButtonDown.Children.ForEach(c => c.SpriteEffects = SpriteEffects.FlipVertically);

            if (isSinglePlayer)
            {
                chatBox = new ChatBox(guiFrame, isSinglePlayer: true)
                {
                    OnEnterMessage = (textbox, text) =>
                    {
                        if (Character.Controlled == null) { return true; }

                        textbox.TextColor = ChatMessage.MessageColor[(int)ChatMessageType.Default];

                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            string msgCommand = ChatMessage.GetChatMessageCommand(text, out string msg);
                            AddSinglePlayerChatMessage(
                                Character.Controlled.Info.Name,
                                msg,
                                ((msgCommand == "r" || msgCommand == "radio") && ChatMessage.CanUseRadio(Character.Controlled)) ? ChatMessageType.Radio : ChatMessageType.Default,
                                Character.Controlled);
                        }
                        textbox.Deselect();
                        textbox.Text = "";
                        return true;
                    }                    
                };
                chatBox.InputBox.OnTextChanged += chatBox.TypingChatMessage;
            }

            var reports = Order.PrefabList.FindAll(o => o.TargetAllCharacters && o.SymbolSprite != null);
            reportButtonFrame = new GUILayoutGroup(new RectTransform(
                new Point((HUDLayoutSettings.CrewArea.Height - (int)((reports.Count - 1) * 5 * GUI.Scale)) / reports.Count, HUDLayoutSettings.CrewArea.Height), guiFrame.RectTransform))
            {
                AbsoluteSpacing = (int)(5 * GUI.Scale),
                UserData = "reportbuttons",
                CanBeFocused = false
            };

            //report buttons
            foreach (Order order in reports)
            {
                if (!order.TargetAllCharacters || order.SymbolSprite == null) continue;
                var btn = new GUIButton(new RectTransform(new Point(reportButtonFrame.Rect.Width), reportButtonFrame.RectTransform), style: null)
                {
                    OnClicked = (GUIButton button, object userData) =>
                    {
                        if (Character.Controlled == null || Character.Controlled.SpeechImpediment >= 100.0f) return false;
                        SetCharacterOrder(null, order, null, Character.Controlled);
                        return true;
                    },
                    UserData = order,
                    ToolTip = order.Name
                };

                new GUIFrame(new RectTransform(new Vector2(1.5f), btn.RectTransform, Anchor.Center), "OuterGlow")
                {
                    Color = Color.Red * 0.8f,
                    HoverColor = Color.Red * 1.0f,
                    PressedColor = Color.Red * 0.6f,
                    UserData = "highlighted",
                    CanBeFocused = false,
                    Visible = false
                };

                var img = new GUIImage(new RectTransform(Vector2.One, btn.RectTransform), order.Prefab.SymbolSprite, scaleToFit: true)
                {
                    Color = order.Color,
                    HoverColor = Color.Lerp(order.Color, Color.White, 0.5f),
                    ToolTip = order.Name
                };
            }

            screenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);

            prevUIScale = GUI.Scale;
        }


        #endregion

        #region Character list management

        public Rectangle GetCharacterListArea()
        {
            return characterListBox.Rect;
        }

        public List<Character> GetCharacters()
        {
            return new List<Character>(characters);
        }

        public List<CharacterInfo> GetCharacterInfos()
        {
            return new List<CharacterInfo>(characterInfos);
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

            if (!characters.Contains(character)) characters.Add(character);
            if (!characterInfos.Contains(character.Info))
            {
                characterInfos.Add(character.Info);
            }

            CreateCharacterFrame(character, characterListBox.Content);
            characterListBox.Content.RectTransform.SortChildren((c1, c2) => { return c2.NonScaledSize.X - c1.NonScaledSize.X; });

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
            if (removeInfo) characterInfos.Remove(character.Info);
        }

        /// <summary>
        /// Remove info of a selected character. The character will not be visible in any menus or the round summary.
        /// </summary>
        /// <param name="characterInfo"></param>
        public void RemoveCharacterInfo(CharacterInfo characterInfo)
        {
            characterInfos.Remove(characterInfo);
        }

        /// <summary>
        /// Create the UI component that holds the character's portrait and order/report buttons for the character
        /// </summary>
        private GUIComponent CreateCharacterFrame(Character character, GUIComponent parent)
        {
            int correctOrderCount = 0, neutralOrderCount = 0, wrongOrderCount = 0;
            //sort the orders
            //  1. generic orders (follow, wait, etc)
            //  2. orders appropriate for the character's job (captain -> steer, etc)
            //  3. orders inappropriate for the job (captain -> operate reactor, etc)
            List<Order> orders = new List<Order>();
            foreach (Order order in Order.PrefabList)
            {
                if (order.TargetAllCharacters || order.SymbolSprite == null) continue;
                if (order.AppropriateJobs == null || order.AppropriateJobs.Length == 0)
                {
                    orders.Insert(0, order);
                    correctOrderCount++;
                }
                else if (order.HasAppropriateJob(character))
                {
                    orders.Add(order);
                    neutralOrderCount++;
                }
            }
            foreach (Order order in Order.PrefabList)
            {
                if (order.SymbolSprite == null) continue;
                if (!order.TargetAllCharacters && !orders.Contains(order))
                {
                    orders.Add(order);
                    wrongOrderCount++;
                }
            }

            int spacing = (int)(10 * GUI.Scale);
            int height = (int)(45 * GUI.Scale);
            characterInfoWidth = (int)(170 * GUI.Scale);

            float charactersPerView = characterListBox.Rect.Height / (float)(height + characterListBox.Spacing);

            //if we can fit less than 25% of the last character or more than 75%,
            //change the size of the character frame slightly to fit them more nicely in the list box
            if (charactersPerView % 1.0f < 0.25f || charactersPerView % 1.0f > 0.75f)
            {
                height = (int)(characterListBox.Rect.Height / (float)Math.Round(charactersPerView)) - characterListBox.Spacing;
            }

            int iconSize = (int)(height * 0.8f);

            var frame = new GUIFrame(new RectTransform(new Point(GameMain.GraphicsWidth, height), parent.RectTransform), style: "InnerFrame")
            {
                UserData = character,
                CanBeFocused = false
            };
            frame.Color = character.Info.Job.Prefab.UIColor;
            frame.SelectedColor = Color.Lerp(frame.Color, Color.White, 0.5f);
            frame.HoverColor = Color.Lerp(frame.Color, Color.White, 0.9f);

            //---------------- character area ----------------

            string characterToolTip = character.Info.Name;
            if (character.Info.Job != null)
            {
                characterToolTip += " (" + character.Info.Job.Name + ")";
            }
            var characterArea = new GUIButton(new RectTransform(new Point(characterInfoWidth, frame.Rect.Height), frame.RectTransform, Anchor.CenterLeft), style: "GUITextBox")
            {
                UserData = character,
                Color = frame.Color,
                SelectedColor = frame.SelectedColor,
                HoverColor = frame.HoverColor,
                ToolTip = characterToolTip
            };
            if (isSinglePlayer)
            {
                characterArea.OnClicked = CharacterClicked;
            }
            else
            {
                characterArea.CanBeFocused = false;
                characterArea.CanBeSelected = false;
            }

            var characterImage = new GUIImage(new RectTransform(new Point(characterArea.Rect.Height, characterArea.Rect.Height), characterArea.RectTransform, Anchor.CenterLeft),
                character.Info.HeadSprite, scaleToFit: true)
            {
                CanBeFocused = false,
                HoverColor = Color.White,
                SelectedColor = Color.White,
                ToolTip = characterToolTip
            };

            var characterName = new GUITextBlock(new RectTransform(new Point(characterArea.Rect.Width - characterImage.Rect.Width, characterArea.Rect.Height), characterArea.RectTransform, Anchor.CenterRight),
                character.Name, textColor: frame.Color, font: GUI.SmallFont, wrap: true)
            {
                Color = frame.Color,
                HoverColor = Color.Transparent,
                SelectedColor = Color.Transparent,
                CanBeFocused = false,
                ToolTip = characterToolTip,
                AutoScale = true
            };

            //---------------- order buttons ----------------

            var orderButtonFrame = new GUILayoutGroup(new RectTransform(new Point(100, frame.Rect.Height), frame.RectTransform)
            { AbsoluteOffset = new Point(characterInfoWidth + spacing, 0) },
                isHorizontal: true, childAnchor: Anchor.CenterLeft)
            {
                AbsoluteSpacing = (int)(10 * GUI.Scale),
                UserData = "orderbuttons",
                CanBeFocused = false
            };

            //listbox for holding the orders inappropriate for this character
            //(so we can easily toggle their visibility)
            var wrongOrderList = new GUIListBox(new RectTransform(new Point(50, orderButtonFrame.Rect.Height), orderButtonFrame.RectTransform), isHorizontal: true, style: null)
            {
                ScrollBarEnabled = false,
                ScrollBarVisible = false,
                Enabled = false,
                Spacing = spacing,
                ClampMouseRectToParent = false
            };
            wrongOrderList.Content.ClampMouseRectToParent = false;

            for (int i = 0; i < orders.Count; i++)
            {
                var order = orders[i];
                if (order.TargetAllCharacters) continue;

                RectTransform btnParent = (i >= correctOrderCount + neutralOrderCount) ? 
                    wrongOrderList.Content.RectTransform : 
                    orderButtonFrame.RectTransform;

                var btn = new GUIButton(new RectTransform(new Point(iconSize, iconSize), btnParent, Anchor.CenterLeft),
                    style: null);

                new GUIFrame(new RectTransform(new Vector2(1.5f), btn.RectTransform, Anchor.Center), "OuterGlow")
                {
                    Color = Color.Lerp(order.Color, frame.Color, 0.5f) * 0.8f,
                    HoverColor = Color.Lerp(order.Color, frame.Color, 0.5f) * 1.0f,
                    PressedColor = Color.Lerp(order.Color, frame.Color, 0.5f) * 0.6f,
                    UserData = "selected",
                    CanBeFocused = false,
                    Visible = false
                };

                var img = new GUIImage(new RectTransform(Vector2.One, btn.RectTransform), order.Prefab.SymbolSprite);
                img.Scale = iconSize / (float)img.SourceRect.Width;
                img.Color = Color.Lerp(order.Color, frame.Color, 0.5f);
                img.ToolTip = order.Name;
                img.HoverColor = Color.Lerp(img.Color, Color.White, 0.5f);
                
                btn.OnClicked += (GUIButton button, object userData) =>
                {
                    if (Character.Controlled == null || Character.Controlled.SpeechImpediment >= 100.0f) return false;
                    
                    if (btn.GetChildByUserData("selected").Visible)
                    {
                        SetCharacterOrder(character, Order.PrefabList.Find(o => o.AITag == "dismissed"), null, Character.Controlled);
                    }
                    else
                    {
                        if (order.ItemComponentType != null || order.ItemIdentifiers.Length > 0 || order.Options.Length > 1)
                        {
                            CreateOrderTargetFrame(button, character, order);
                        }
                        else
                        {
                            SetCharacterOrder(character, order, null, Character.Controlled);
                        }
                    }
                    return true;
                };
                btn.UserData = order;
                btn.ToolTip = order.Name;

                //divider between different groups of orders
                if (i == correctOrderCount - 1 || i == correctOrderCount + neutralOrderCount - 1)
                {
                    //TODO: divider sprite
                    new GUIFrame(new RectTransform(new Point(8, iconSize), orderButtonFrame.RectTransform), style: "GUIButton");
                }
            }

            var toggleWrongOrderBtn = new GUIButton(new RectTransform(new Point((int)(30 * GUI.Scale), wrongOrderList.Rect.Height), wrongOrderList.Content.RectTransform),
                "", style: "UIToggleButton")
            {
                CanBeFocused = false
            };

            wrongOrderList.RectTransform.NonScaledSize = new Point(
                wrongOrderList.Content.Children.Sum(c => c.Rect.Width + wrongOrderList.Spacing),
                wrongOrderList.RectTransform.NonScaledSize.Y);
            wrongOrderList.RectTransform.SetAsLastChild();

            new GUIFrame(new RectTransform(new Point(
                wrongOrderList.Rect.Width - toggleWrongOrderBtn.Rect.Width - wrongOrderList.Spacing * 2, 
                wrongOrderList.Rect.Height), wrongOrderList.Content.RectTransform), 
                style: null);

            //scale to fit the content
            orderButtonFrame.RectTransform.NonScaledSize = new Point(
                orderButtonFrame.Children.Sum(c => c.Rect.Width + orderButtonFrame.AbsoluteSpacing),
                orderButtonFrame.RectTransform.NonScaledSize.Y);

            frame.RectTransform.NonScaledSize = new Point(
                characterInfoWidth + spacing + (orderButtonFrame.Rect.Width - wrongOrderList.Rect.Width),
                frame.RectTransform.NonScaledSize.Y);

            characterListBox.RectTransform.NonScaledSize = new Point(
                characterListBox.Content.Children.Max(c => c.Rect.Width) + wrongOrderList.Rect.Width,
                characterListBox.RectTransform.NonScaledSize.Y);
            characterListBox.Content.RectTransform.NonScaledSize = characterListBox.RectTransform.NonScaledSize;
            characterListBox.UpdateScrollBarSize();
            return frame;
        }

        /// <summary>
        /// Sets which character is selected in the crew UI (highlight effect etc)
        /// </summary>
        public bool CharacterClicked(GUIComponent component, object selection)
        {
            Character character = selection as Character;
            if (character == null || character.IsDead || character.IsUnconscious) return false;
            SelectCharacter(character);
            return true;
        }

        /// <summary>
        /// Sets which character is selected in the crew UI (highlight effect etc)
        /// </summary>
        public void SetCharacterSelected(Character character)
        {
            if (character != null && !characters.Contains(character)) return;

            //GUIComponent selectedCharacterFrame = null;
            foreach (GUIComponent child in characterListBox.Content.Children)
            {
                GUIButton button = child.Children.FirstOrDefault(c => c.UserData is Character) as GUIButton;
                if (button == null) continue;

                child.Visible = (Character)button.UserData != character;
            }
        }

        public void ReviveCharacter(Character revivedCharacter)
        {
            if (characterListBox.Content.GetChildByUserData(revivedCharacter) is GUIComponent characterBlock)
            {
                characterBlock.Parent.RemoveChild(characterBlock);
            }
            if (characterInfos.Contains(revivedCharacter.Info)) AddCharacter(revivedCharacter);
        }

        public void KillCharacter(Character killedCharacter)
        {
            if (characterListBox.Content.GetChildByUserData(killedCharacter) is GUIComponent characterBlock)
            {
                CoroutineManager.StartCoroutine(KillCharacterAnim(characterBlock));
            }
            RemoveCharacter(killedCharacter);
        }

        private bool ScrollCharacterList(GUIButton button, object obj)
        {
            if (characterListBox.Content.CountChildren == 0) return false;
            int dir = (int)obj;

            float step = 
                (characterListBox.Content.Children.First().Rect.Height + characterListBox.Spacing) / 
                (characterListBox.TotalSize - characterListBox.Rect.Height);
            characterListBox.BarScroll -= characterListBox.BarScroll % step;
            characterListBox.BarScroll += dir * step;

            return false;
        }

        private IEnumerable<object> KillCharacterAnim(GUIComponent component)
        {
            List<GUIComponent> components = component.GetAllChildren().ToList();
            components.Add(component);
            foreach (GUIComponent comp in components)
            {
                comp.Color = Color.DarkRed;
            }

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
            component.Parent?.RemoveChild(component);
            characterListBox.UpdateScrollBarSize();
            yield return CoroutineStatus.Success;
        }

        #endregion

        #region Dialog

        private void UpdateConversations(float deltaTime)
        {
            conversationTimer -= deltaTime;
            if (conversationTimer <= 0.0f)
            {
                List<Character> availableSpeakers = GameMain.GameSession.CrewManager.GetCharacters();
                availableSpeakers.RemoveAll(c => !(c.AIController is HumanAIController) || c.IsDead || c.SpeechImpediment >= 100.0f);
                if (GameMain.Server != null)
                {
                    foreach (Client client in GameMain.Server.ConnectedClients)
                    {
                        if (client.Character != null) availableSpeakers.Remove(client.Character);
                    }
                    if (GameMain.Server.Character != null) availableSpeakers.Remove(GameMain.Server.Character);
                }

                pendingConversationLines.AddRange(NPCConversation.CreateRandom(availableSpeakers));
                conversationTimer = Rand.Range(ConversationIntervalMin, ConversationIntervalMax);
            }

            if (pendingConversationLines.Count > 0)
            {
                conversationLineTimer -= deltaTime;
                if (conversationLineTimer <= 0.0f)
                {
                    //speaker of the next line can't speak, interrupt the conversation
                    if (pendingConversationLines[0].First.SpeechImpediment >= 100.0f)
                    {
                        pendingConversationLines.Clear();
                        return;
                    }

                    pendingConversationLines[0].First.Speak(pendingConversationLines[0].Second, null);
                    if (pendingConversationLines.Count > 1)
                    {
                        conversationLineTimer = MathHelper.Clamp(pendingConversationLines[0].Second.Length * 0.1f, 1.0f, 5.0f);
                    }
                    pendingConversationLines.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// Adds the message to the single player chatbox.
        /// </summary>
        public void AddSinglePlayerChatMessage(string senderName, string text, ChatMessageType messageType, Character sender)
        {
            if (!isSinglePlayer)
            {
                DebugConsole.ThrowError("Cannot add messages to single player chat box in multiplayer mode!\n" + Environment.StackTrace);
                return;
            }

            chatBox.AddMessage(ChatMessage.Create(senderName, text, messageType, sender));
        }

        private WifiComponent GetHeadset(Character character, bool requireEquipped)
        {
            if (character?.Inventory == null) return null;

            var radioItem = character.Inventory.Items.FirstOrDefault(it => it != null && it.GetComponent<WifiComponent>() != null);
            if (radioItem == null) return null;
            if (requireEquipped && !character.HasEquippedItem(radioItem)) return null;

            return radioItem.GetComponent<WifiComponent>();
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
                if (orderGiver == null || orderGiver.CurrentHull == null) return;
                AddOrder(new Order(order.Prefab, orderGiver.CurrentHull, null), order.Prefab.FadeOutTime);

                if (IsSinglePlayer)
                {
                    orderGiver.Speak(
                        order.GetChatMessage("", orderGiver.CurrentHull?.RoomName), ChatMessageType.Order);
                }
                else
                {
                    OrderChatMessage msg = new OrderChatMessage(order, "", orderGiver.CurrentHull, null, orderGiver);
                    if (GameMain.Client != null)
                    {
                        GameMain.Client.SendChatMessage(msg);
                    }
                    else if (GameMain.Server != null)
                    {
                        GameMain.Server.SendOrderChatMessage(msg);
                    }
                }
                return;
            }

            character.SetOrder(order, option, orderGiver);
            if (IsSinglePlayer)
            {
                orderGiver?.Speak(
                    order.GetChatMessage(character.Name, orderGiver.CurrentHull?.RoomName, option), ChatMessageType.Order);
            }
            else if (orderGiver != null)
            {
                OrderChatMessage msg = new OrderChatMessage(order, option, order.TargetItemComponent?.Item, character, orderGiver);
                if (GameMain.Client != null)
                {
                    GameMain.Client.SendChatMessage(msg);
                }
                else if (GameMain.Server != null)
                {
                    GameMain.Server.SendOrderChatMessage(msg);
                }
            }
            DisplayCharacterOrder(character, order);
        }

        /// <summary>
        /// Displays the specified order in the crew UI next to the character. 
        /// </summary>
        public void DisplayCharacterOrder(Character character, Order order)
        {
            foreach (GUIComponent characterListElement in characterListBox.Content.Children)
            {
                var characterFrame = characterListElement.FindChild(character);
                if (characterFrame == null) continue;

                var orderButtonFrame = characterListElement.GetChildByUserData("orderbuttons");
                
                //get all order buttons from the frame
                List<GUIButton> orderButtons = new List<GUIButton>();
                foreach (GUIComponent child in orderButtonFrame.Children)
                {
                    if (child is GUIButton orderBtn)
                    {
                        orderButtons.Add(orderBtn);
                    }
                    //the non-character-appropriate orders are in a hideable listbox, we need to go deeper!
                    else if (child is GUIListBox listBox)
                    {
                        foreach (GUIComponent listBoxElement in listBox.Content.Children)
                        {
                            if (listBoxElement is GUIButton orderBtn2 && listBoxElement.UserData is Order) orderButtons.Add(orderBtn2);
                        }
                    }
                }

                foreach (GUIButton button in orderButtons)
                {
                    var selectedIndicator = button.GetChildByUserData("selected");
                    if (selectedIndicator != null)
                    {
                        selectedIndicator.Visible = (order != null && ((Order)button.UserData).Prefab == order.Prefab);
                    }
                }
            }
        }

        /// <summary>
        /// Create the UI panel that's used to select the target and options for a given order 
        /// (which railgun to use, whether to power up the reactor or shut it down...)
        /// </summary>
        private void CreateOrderTargetFrame(GUIComponent orderButton, Character character, Order order)
        {
            Submarine submarine = Character.Controlled != null && Character.Controlled.TeamID > 1 && Submarine.MainSubs.Length > 1 ?
                Submarine.MainSubs[1] : 
                Submarine.MainSub;

            List<Item> matchingItems = new List<Item>();
            if (order.ItemComponentType != null || order.ItemIdentifiers.Length > 0)
            {
                matchingItems = order.ItemIdentifiers.Length > 0 ?
                    Item.ItemList.FindAll(it => order.ItemIdentifiers.Contains(it.Prefab.Identifier) || it.HasTag(order.ItemIdentifiers)) :
                    Item.ItemList.FindAll(it => it.components.Any(ic => ic.GetType() == order.ItemComponentType));

                matchingItems.RemoveAll(it => it.Submarine != submarine && !submarine.DockedTo.Contains(it.Submarine));
            }


            //more than one target item -> create a minimap-like selection with a pic of the sub
            if (matchingItems.Count > 1)
            {
                Rectangle subBorders = submarine.GetDockedBorders();

                Point frameSize;
                if (subBorders.Width > subBorders.Height)
                {
                    //make sure the right side doesn't go over the right side of the screen
                    frameSize.X = Math.Min(GameMain.GraphicsWidth / 2, GameMain.GraphicsWidth - orderButton.Rect.Center.X - 50);
                    //height depends on the dimensions of the sub
                    frameSize.Y = (int)(frameSize.X * (subBorders.Height / (float)subBorders.Width));
                }
                else
                {
                    //make sure the bottom side doesn't go over the bottom of the screen
                    frameSize.Y = Math.Min((int)(GameMain.GraphicsHeight * 0.6f), GameMain.GraphicsHeight - orderButton.Rect.Center.Y - 50);
                    //width depends on the dimensions of the sub
                    frameSize.X = (int)(frameSize.Y * (subBorders.Width / (float)subBorders.Height));
                }
                orderTargetFrame = new GUIFrame(new RectTransform(frameSize, GUI.Canvas)
                    { AbsoluteOffset = new Point(orderButton.Rect.Center.X, orderButton.Rect.Bottom) },
                    style: "InnerFrame")
                {
                    UserData = character
                };
                submarine.CreateMiniMap(orderTargetFrame, matchingItems);

                List<GUIComponent> optionFrames = new List<GUIComponent>();
                foreach (Item item in matchingItems)
                {
                    var itemTargetFrame = orderTargetFrame.Children.First().FindChild(item);
                    if (itemTargetFrame == null) continue;

                    Anchor anchor = Anchor.TopLeft;
                    if (itemTargetFrame.RectTransform.RelativeOffset.X < 0.5f && itemTargetFrame.RectTransform.RelativeOffset.Y < 0.5f)
                        anchor = Anchor.BottomRight;
                    else if (itemTargetFrame.RectTransform.RelativeOffset.X > 0.5f && itemTargetFrame.RectTransform.RelativeOffset.Y < 0.5f)
                        anchor = Anchor.BottomLeft;
                    else if (itemTargetFrame.RectTransform.RelativeOffset.X < 0.5f && itemTargetFrame.RectTransform.RelativeOffset.Y > 0.5f)
                        anchor = Anchor.TopRight;

                    var optionFrame = new GUIFrame(new RectTransform(new Point((int)(250 * GUI.Scale), (int)((40 + order.Options.Length * 40) * GUI.Scale)), itemTargetFrame.RectTransform, anchor),
                        style: "InnerFrame");
                    optionFrames.Add(optionFrame);

                    var optionContainer = new GUILayoutGroup(new RectTransform(new Vector2(0.9f), optionFrame.RectTransform, Anchor.Center))
                    {
                        Stretch = true,
                        RelativeSpacing = 0.05f
                    };

                    new GUITextBlock(new RectTransform(new Vector2(1.0f,0.3f), optionContainer.RectTransform), item != null ? item.Name : order.Name);
                    for (int i = 0; i < order.Options.Length; i++)
                    {
                        string option = order.Options[i];
                        var optionButton = new GUIButton(new RectTransform(new Vector2(1.0f, 0.2f), optionContainer.RectTransform),
                            order.OptionNames[i], style: "GUITextBox")
                        {
                            UserData = item == null ? order : new Order(order, item, item.components.Find(ic => ic.GetType() == order.ItemComponentType)),
                            Font = GUI.SmallFont,
                            OnClicked = (btn, userData) =>
                            {
                                if (Character.Controlled == null) return false;
                                SetCharacterOrder(character, userData as Order, option, Character.Controlled);
                                orderTargetFrame = null;
                                return true;
                            }
                        };
                    }
                }

                GUI.PreventElementOverlap(optionFrames, null, new Rectangle(10, 10, GameMain.GraphicsWidth - 20, GameMain.GraphicsHeight - 20));
            }
            //only one target (or an order with no particular targets), just show options
            else
            {
                orderTargetFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.2f + order.Options.Length * 0.1f, 0.18f), GUI.Canvas)
                    { AbsoluteOffset = new Point(orderButton.Rect.Center.X, orderButton.Rect.Bottom) },
                    isHorizontal: true, childAnchor: Anchor.BottomLeft)
                {
                    UserData = character,
                    Stretch = true
                };
                //line connecting the order button to the option buttons
                //TODO: sprite
                new GUIFrame(new RectTransform(new Vector2(0.5f, 1.0f), orderTargetFrame.RectTransform), style: null);
                
                for (int i = 0; i < order.Options.Length; i++)
                {
                    Item item = matchingItems.Count > 0 ? matchingItems[0] : null;
                    string option = order.Options[i];
                    var optionButton = new GUIButton(new RectTransform(new Vector2(0.5f, 0.5f), orderTargetFrame.RectTransform),
                        order.OptionNames[i], style: "GUITextBox")
                    {
                        UserData = item == null ? order : new Order(order, item, item.components.Find(ic => ic.GetType() == order.ItemComponentType)),
                        OnClicked = (btn, userData) =>
                        {
                            if (Character.Controlled == null) return false;
                            SetCharacterOrder(character, userData as Order, option, Character.Controlled);
                            orderTargetFrame = null;
                            return true;
                        }
                    };
                    //lines between the order buttons
                    if (i < order.Options.Length - 1)
                    {
                        //TODO: sprite
                        new GUIFrame(new RectTransform(new Vector2(0.1f, 1.0f), orderTargetFrame.RectTransform), style: null);
                    }
                }
            }
            int shadowSize = (int)(200 * GUI.Scale);
            orderTargetFrameShadow = new GUIFrame(new RectTransform(orderTargetFrame.Rect.Size + new Point(shadowSize * 2), GUI.Canvas)
                { AbsoluteOffset = orderTargetFrame.Rect.Location - new Point(shadowSize) }, style: "OuterGlow", color: Color.Black * 0.65f);
        }

        #region Updating and drawing the UI

        public void AddToGUIUpdateList()
        {
            if (GUI.DisableHUD) return;
            if (GameMain.GraphicsWidth != screenResolution.X || GameMain.GraphicsHeight != screenResolution.Y ||
                prevUIScale != GUI.Scale)
            {
                var prevCharacterListBox = characterListBox;
                InitProjectSpecific();

                foreach (GUIComponent c in prevCharacterListBox.Content.Children)
                {
                    Character character = c.UserData as Character;
                    if (character == null) continue;
                    AddCharacter(character);
                    DisplayCharacterOrder(character, character.CurrentOrder);
                }
            }

            guiFrame.AddToGUIUpdateList();
            if (orderTargetFrame != null)
            {
                orderTargetFrameShadow?.AddToGUIUpdateList();
                orderTargetFrame?.AddToGUIUpdateList();
            }
        }

        public void SelectNextCharacter()
        {
            if (GameMain.IsMultiplayer) { return; }
            if (characters.None()) { return; }
            SelectCharacter(characters[TryAdjustIndex(1)]);
        }

        public void SelectPreviousCharacter()
        {
            if (GameMain.IsMultiplayer) { return; }
            if (characters.None()) { return; }
            SelectCharacter(characters[TryAdjustIndex(-1)]);
        }

        private void SelectCharacter(Character character)
        {
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

            if (GUI.DisableHUD) return;
            if (chatBox != null)
            {
                chatBox.Update(deltaTime);
                chatBox.InputBox.Visible = Character.Controlled != null;

                if ((PlayerInput.KeyHit(InputType.Chat) || PlayerInput.KeyHit(InputType.RadioChat)) &&
                    !DebugConsole.IsOpen && chatBox.InputBox.Visible)
                {
                    if (chatBox.InputBox.Selected)
                    {
                        chatBox.InputBox.Text = "";
                        chatBox.InputBox.Deselect();
                    }
                    else
                    {
                        chatBox.InputBox.Select();
                        if (PlayerInput.KeyHit(InputType.RadioChat))
                        {
                            chatBox.InputBox.Text = "r; ";
                        }
                    }
                }
            }

            crewArea.Visible = characters.Count > 0 && CharacterHealth.OpenHealthWindow == null;
            if (orderTargetFrame != null) orderTargetFrame.Visible = characterListBox.Visible;

            scrollButtonUp.Visible = characterListBox.BarScroll > 0.01f && characterListBox.BarSize < 1.0f;
            scrollButtonDown.Visible = characterListBox.BarScroll < 0.99 && characterListBox.BarSize < 1.0f;

            foreach (GUIComponent child in characterListBox.Content.Children)
            {
                child.Visible = 
                    Character.Controlled == null || 
                    (Character.Controlled != ((Character)child.UserData) && Character.Controlled.TeamID == ((Character)child.UserData).TeamID);

                if (child.Visible)
                {
                    GUIListBox wrongOrderList = child.GetChildByUserData("orderbuttons")?.GetChild<GUIListBox>();
                    if (wrongOrderList != null)
                    {
                        Rectangle hoverRect = wrongOrderList.Rect;
                        if (wrongOrderList.BarScroll < 0.5f)
                        {
                            //higher tolerance when the orderlist is open (mouse needs to be moved further before it closes)
                            hoverRect.Inflate((int)(50 * GUI.Scale), (int)(50 * GUI.Scale));
                        }
                        else
                        {
                            hoverRect.Inflate((int)(30 * GUI.Scale), (int)(0 * GUI.Scale));
                        }

                        bool toggleOpen = 
                            characterListBox.Content.Rect.Contains(PlayerInput.MousePosition) && 
                            hoverRect.Contains(PlayerInput.MousePosition);

                        //order target frame open on this character, check if we're giving any of the orders in wrongOrderList
                        if (!toggleOpen && orderTargetFrame != null && orderTargetFrame.UserData == child.UserData)
                        {
                            toggleOpen = wrongOrderList.Content.Children.Any(c =>
                                c.UserData is Order order &&
                                orderTargetFrame.Children.Any(c2 => c2.UserData == c.UserData));
                        }

                        float scroll = MathHelper.Clamp(wrongOrderList.BarScroll + (toggleOpen ? -deltaTime * 5.0f : deltaTime * 5.0f), 0.0f, 1.0f);
                        if (Math.Abs(wrongOrderList.BarScroll - scroll) > 0.01f) { wrongOrderList.BarScroll = scroll; }
                    }
                }
            }

            crewArea.RectTransform.AbsoluteOffset =
                Vector2.SmoothStep(new Vector2(-crewArea.Rect.Width, 0), new Vector2(toggleCrewButton.Rect.Width, 0), crewAreaOpenState).ToPoint();
            crewAreaOpenState = toggleCrewAreaOpen ?
                Math.Min(crewAreaOpenState + deltaTime * 2.0f, 1.0f) :
                Math.Max(crewAreaOpenState - deltaTime * 2.0f, 0.0f);

            if (GUI.KeyboardDispatcher.Subscriber == null &&
                PlayerInput.KeyHit(InputType.CrewOrders) &&
                characters.Contains(Character.Controlled))
            {
                //deselect construction unless it's the ladders the character is climbing
                if (Character.Controlled != null &&
                    Character.Controlled.SelectedConstruction != null &&
                    Character.Controlled.SelectedConstruction.GetComponent<Items.Components.Ladder>() == null)
                {
                    Character.Controlled.SelectedConstruction = null;
                }
                toggleCrewAreaOpen = !toggleCrewAreaOpen;
            }

            UpdateReports(deltaTime);
            UpdateConversations(deltaTime);

            if (orderTargetFrame != null)
            {
                Rectangle hoverArea = orderTargetFrame.Rect;
                foreach (GUIComponent child in orderTargetFrame.Children.First().Children)
                {
                    if (!(child.UserData is Item)) continue;
                    foreach (GUIComponent grandChild in child.Children)
                    {
                        hoverArea = Rectangle.Union(hoverArea, grandChild.Rect);
                    }
                }
                hoverArea.Inflate(100, 100);

                if (!hoverArea.Contains(PlayerInput.MousePosition)) orderTargetFrame = null;
            }
        }

        #endregion

        /// <summary>
        /// Creates a listbox that includes all the characters in the crew, can be used externally (round info menus etc)
        /// </summary>
        public void CreateCrewListFrame(List<Character> crew, GUIFrame crewFrame)
        {
            List<byte> teamIDs = crew.Select(c => c.TeamID).Distinct().ToList();

            if (!teamIDs.Any()) teamIDs.Add(0);

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

                foreach (Character character in crew.FindAll(c => c.TeamID == teamIDs[i]))
                {
                    GUIFrame frame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.15f), crewList.Content.RectTransform), style: "ListBoxElement")
                    {
                        UserData = character,
                        Color = (GameMain.NetworkMember != null && GameMain.NetworkMember.Character == character) ? Color.Gold * 0.2f : Color.Transparent
                    };

                    var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.9f), frame.RectTransform, Anchor.Center), isHorizontal: true)
                    {
                        RelativeSpacing = 0.05f,
                        Stretch = true
                    };

                    new GUIImage(new RectTransform(new Vector2(0.2f, 1.0f), paddedFrame.RectTransform), character.AnimController.Limbs[0].ActiveSprite, scaleToFit: true);

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
            if (character == null) return false;

            GUIComponent crewFrame = (GUIComponent)crewList.UserData;

            GUIComponent existingPreview = crewFrame.FindChild("SelectedCharacter");
            if (existingPreview != null) crewFrame.RemoveChild(existingPreview);

            var previewPlayer = new GUIFrame(new RectTransform(new Vector2(0.4f, 0.8f), crewFrame.RectTransform, Anchor.CenterRight) { RelativeOffset = new Vector2(0.05f, 0.0f) }, style: "InnerFrame")
            {
                UserData = "SelectedCharacter"
            };

            character.Info.CreateInfoFrame(previewPlayer);

            if (GameMain.NetworkMember != null) GameMain.NetworkMember.SelectCrewCharacter(character, previewPlayer);

            return true;
        }

        #region Reports

        /// <summary>
        /// Enables/disables report buttons when needed
        /// </summary>
        public void UpdateReports(float deltaTime)
        {
            bool canIssueOrders = false;
            if (Character.Controlled?.CurrentHull != null && Character.Controlled.SpeechImpediment < 100.0f)
            {
                WifiComponent radio = GetHeadset(Character.Controlled, true);
                canIssueOrders = radio != null && radio.CanTransmit();
            }

            if (canIssueOrders)
            {
                reportButtonFrame.Visible = true;

                var reportButtonParent = chatBox ?? GameMain.NetworkMember.ChatBox;
                reportButtonFrame.RectTransform.AbsoluteOffset = new Point(
                    Math.Min(reportButtonParent.GUIFrame.Rect.X, reportButtonParent.ToggleButton.Rect.X) - reportButtonFrame.Rect.Width - (int)(10 * GUI.Scale),
                    reportButtonParent.GUIFrame.Rect.Y);

                bool hasFires = Character.Controlled.CurrentHull.FireSources.Count > 0;
                ToggleReportButton("reportfire", hasFires);

                bool hasLeaks = Character.Controlled.CurrentHull.ConnectedGaps.Any(g => !g.IsRoomToRoom && g.Open > 0.0f);
                ToggleReportButton("reportbreach", hasLeaks);

                bool hasIntruders = Character.CharacterList.Any(c =>
                    c.CurrentHull == Character.Controlled.CurrentHull && !c.IsDead &&
                    (c.AIController is EnemyAIController || c.TeamID != Character.Controlled.TeamID));

                ToggleReportButton("reportintruders", hasIntruders);

                foreach (GUIComponent reportButton in reportButtonFrame.Children)
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
                reportButtonFrame.Visible = false;
            }
        }

        /// <summary>
        /// Should report buttons be visible on the screen atm?
        /// </summary>
        private bool ReportButtonsVisible()
        {
            return CharacterHealth.OpenHealthWindow == null;
        }
        
        private bool ReportButtonClicked(GUIButton button, object userData)
        {
            //order targeted to all characters
            Order order = userData as Order;
            if (order.TargetAllCharacters)
            {
                if (Character.Controlled == null || Character.Controlled.CurrentHull == null) return false;
                AddOrder(new Order(order.Prefab, Character.Controlled.CurrentHull, null), order.Prefab.FadeOutTime);
                SetCharacterOrder(null, order, "", Character.Controlled);
            }
            return true;
        }

        private void ToggleReportButton(string orderAiTag, bool enabled)
        {
            Order order = Order.PrefabList.Find(o => o.AITag == orderAiTag);
                        
            //already reported, disable the button
            /*if (GameMain.GameSession.CrewManager.ActiveOrders.Any(o =>
                o.First.TargetEntity == Character.Controlled.CurrentHull &&
                o.First.AITag == orderAiTag))
            {
                enabled = false;
            }*/

            var reportButton = reportButtonFrame.GetChildByUserData(order);
            if (reportButton != null)
            {
                reportButton.GetChildByUserData("highlighted").Visible = enabled;
            }            
        }

        #endregion

        public void InitSinglePlayerRound()
        {
            characterListBox.ClearChildren();
            characters.Clear();

            WayPoint[] waypoints = WayPoint.SelectCrewSpawnPoints(characterInfos, Submarine.MainSub);

            for (int i = 0; i < waypoints.Length; i++)
            {
                Character character;

                if (characterInfos[i].HullID != null)
                {
                    var hull = Entity.FindEntityByID((ushort)characterInfos[i].HullID) as Hull;
                    if (hull == null) continue;
                    character = Character.Create(characterInfos[i], hull.WorldPosition, characterInfos[i].Name);
                }
                else
                {
                    character = Character.Create(characterInfos[i], waypoints[i].WorldPosition, characterInfos[i].Name);

                    if (character.Info != null && !character.Info.StartItemsGiven)
                    {
                        character.GiveJobItems(waypoints[i]);
                        character.Info.StartItemsGiven = true;
                    }
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

            if (characters.Any()) characterListBox.Select(0);

            conversationTimer = Rand.Range(5.0f, 10.0f);
        }

        public void EndRound()
        {
            //remove characterinfos whose characters have been removed or killed
            characterInfos.RemoveAll(c => c.Character == null || c.Character.Removed || c.CauseOfDeath != null);

            characters.Clear();
            characterListBox.ClearChildren();
        }

        public void Reset()
        {
            characters.Clear();
            characterInfos.Clear();
            characterListBox.ClearChildren();
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
