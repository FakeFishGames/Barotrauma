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

        const float ConversationIntervalMin = 100.0f;
        const float ConversationIntervalMax = 180.0f;
        
        private List<CharacterInfo> characterInfos;
        private List<Character> characters;

        public int WinningTeam = 1;
        
        private float conversationTimer, conversationLineTimer;
        private List<Pair<Character, string>> pendingConversationLines = new List<Pair<Character, string>>();

        #region UI

        private GUIFrame guiFrame;
        private GUIFrame crewArea;
        private GUIListBox characterListBox;

        private GUIButton scrollButtonUp, scrollButtonDown;

        private GUIButton toggleCrewButton;
        private Vector2 crewAreaOffset;
        private bool toggleCrewAreaOpen;
        private int crewAreaWidth;
        private int characterInfoWidth;

        private ChatBox chatBox;

        //listbox for report buttons that appear at the corner of the screen 
        //when there's something to report in the hull the character is currently in
        private GUIListBox reportButtonContainer;

        private GUIComponent orderTargetFrame;

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

                characterInfos.Add(new CharacterInfo(subElement));
            }
        }

        partial void InitProjectSpecific()
        {
            characters = new List<Character>();
            characterInfos = new List<CharacterInfo>();

            guiFrame = new GUIFrame(new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.Transparent);
            guiFrame.Padding = Vector4.Zero;
            guiFrame.CanBeFocused = false;

            int scrollButtonHeight = (int)(30 * GUI.Scale);

            crewArea = new GUIFrame(HUDLayoutSettings.CrewArea, null, guiFrame);
            crewArea.Padding = Vector4.Zero;
            toggleCrewButton = new GUIButton(new Rectangle(crewArea.Rect.Width + 10, 0, 25, 70), "", Alignment.CenterLeft, "GUIButtonHorizontalArrow", crewArea);
            toggleCrewButton.ClampMouseRectToParent = false;
            toggleCrewButton.OnClicked += (GUIButton btn, object userdata) =>
            {
                toggleCrewAreaOpen = !toggleCrewAreaOpen;
                foreach (GUIComponent child in btn.children)
                {
                    child.SpriteEffects = toggleCrewAreaOpen ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
                }
                return true;
            };

            characterListBox = new GUIListBox(new Rectangle(0, (int)(scrollButtonHeight * 0.8f), 0, (int)(crewArea.Rect.Height - scrollButtonHeight * 1.6f)), Color.Transparent, null, crewArea);
            characterListBox.Spacing = (int)(3 * GUI.Scale);
            characterListBox.ScrollBarEnabled = false;
            characterListBox.CanBeFocused = false;

            scrollButtonUp = new GUIButton(new Rectangle(0, 0, characterListBox.Rect.Width, scrollButtonHeight), "", "GUIButtonVerticalArrow", crewArea);
            scrollButtonUp.ClampMouseRectToParent = false;
            scrollButtonUp.Visible = false;
            scrollButtonDown = new GUIButton(new Rectangle(0, crewArea.Rect.Height - scrollButtonHeight, characterListBox.Rect.Width, scrollButtonHeight), "", "GUIButtonVerticalArrow", crewArea);
            scrollButtonDown.ClampMouseRectToParent = false;
            scrollButtonDown.children.ForEach(c => c.SpriteEffects = SpriteEffects.FlipVertically);
            scrollButtonDown.Visible = false;

            //PH: make space for the icon part of the report button
            Rectangle rect = HUDLayoutSettings.ReportArea;
            rect = new Rectangle(rect.X, rect.Y + 64, rect.Width, rect.Height);
            reportButtonContainer = new GUIListBox(rect, null, Alignment.TopRight, null);
            reportButtonContainer.Color = Color.Transparent;
            reportButtonContainer.Spacing = 50;
            reportButtonContainer.HideChildrenOutsideFrame = false;

            if (isSinglePlayer)
            {
                chatBox = new ChatBox(guiFrame, true);
            }
        }

        #endregion

        #region Character list management
        
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

            //commander.UpdateCharacters();
            CreateCharacterFrame(character, characterListBox);

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
        private GUIFrame CreateCharacterFrame(Character character, GUIComponent parent)
        {
            int correctOrderCount = 0, neutralOrderCount = 0, wrongOrderCount = 0;
            List<Order> orders = new List<Order>();
            foreach (Order order in Order.PrefabList)
            {
                if (!order.TargetAllCharacters)
                {
                    if (order.AppropriateJobs == null || order.AppropriateJobs.Length == 0)
                    {
                        neutralOrderCount++;
                        orders.Add(order);
                    }
                    else if (order.HasAppropriateJob(character))
                    {
                        correctOrderCount++;
                        orders.Insert(0, order);
                    }
                }
            }
            foreach (Order order in Order.PrefabList)
            {
                if (!order.TargetAllCharacters && !orders.Contains(order))
                {
                    wrongOrderCount++;
                    orders.Add(order);
                }
            }

            int height = (int)(45 * GUI.Scale);
            int iconWidth = (int)(40 * GUI.Scale);
            int padding = (int)(8 * GUI.Scale);

            characterInfoWidth = (int)(170 * GUI.Scale) + height;
            crewAreaWidth = orders.Count * (iconWidth + padding) + characterInfoWidth;

            var frame = new GUIFrame(new Rectangle(0, 0, crewAreaWidth, height), null, Alignment.TopLeft, null, parent);
            frame.UserData = character;

            var orderButtonFrame = new GUIFrame(new Rectangle(characterInfoWidth, 0, frame.Rect.Width - characterInfoWidth, 0), null, frame);
            orderButtonFrame.Padding = Vector4.Zero;
            orderButtonFrame.UserData = "orderbuttons";

            scrollButtonUp.Rect = new Rectangle(frame.Rect.Right - crewAreaWidth, scrollButtonUp.Rect.Y, crewAreaWidth, scrollButtonUp.Rect.Height);
            scrollButtonDown.Rect = new Rectangle(frame.Rect.Right - crewAreaWidth, scrollButtonDown.Rect.Y, crewAreaWidth, scrollButtonDown.Rect.Height);

            int x = 0;// -characterInfoWidth;
            int correctAreaWidth = correctOrderCount * iconWidth + (correctOrderCount - 1) * padding;
            int neutralAreaWidth = neutralOrderCount * iconWidth + (neutralOrderCount - 1) * padding;
            int wrongAreaWidth = wrongOrderCount * iconWidth + (wrongOrderCount - 1) * padding;
            new GUIFrame(new Rectangle(x, 0, correctAreaWidth, 0), Color.LightGreen, Alignment.CenterLeft, "InnerFrame", orderButtonFrame);
            new GUIFrame(new Rectangle(x+correctAreaWidth + padding, 0, neutralAreaWidth, 0), Color.LightGray, Alignment.CenterLeft, "InnerFrame", orderButtonFrame);
            new GUIFrame(new Rectangle(x + correctAreaWidth + neutralAreaWidth + padding * 2, 0, wrongAreaWidth, 0), Color.Red, Alignment.CenterLeft, "InnerFrame", orderButtonFrame);
            foreach (Order order in orders)
            {
                if (order.TargetAllCharacters) continue;
                var btn = new GUIButton(new Rectangle(x, 0, iconWidth, iconWidth), "", Alignment.CenterLeft, null, orderButtonFrame);
                var img = new GUIImage(new Rectangle(0, 0, iconWidth, iconWidth), order.Prefab.SymbolSprite, Alignment.TopLeft, btn);
                img.Scale = iconWidth / (float)img.SourceRect.Width;
                img.Color = order.Color;
                img.ToolTip = order.Name;

                img.HoverColor = Color.Lerp(img.Color, Color.White, 0.5f);

                btn.OnClicked += (GUIButton button, object userData) =>
                {
                    if (Character.Controlled == null || !Character.Controlled.CanSpeak) return false;

                    if (order.ItemComponentType != null || !string.IsNullOrEmpty(order.ItemName) || order.Options.Length > 1)
                    {
                        CreateOrderTargetFrame(button, character, order);
                    }
                    else
                    {
                        SetCharacterOrder(character, order, null, Character.Controlled);
                    }
                    return true;
                };

                btn.ToolTip = order.Name;
                x += iconWidth + padding;
            }

            var reportButtonFrame = new GUIFrame(new Rectangle(0, 0, frame.Rect.Width - characterInfoWidth, 0), null, frame);
            reportButtonFrame.UserData = "reportbuttons";
            reportButtonFrame.Visible = false;
            x = 0;
            foreach (Order order in Order.PrefabList)
            {
                if (!order.TargetAllCharacters) continue;
                var btn = new GUIButton(new Rectangle(x, 0, iconWidth, iconWidth), "", Alignment.CenterRight, null, reportButtonFrame);
                var img = new GUIImage(new Rectangle(0, 0, iconWidth, iconWidth), order.Prefab.SymbolSprite, Alignment.TopLeft, btn);
                img.Scale = iconWidth / (float)img.SourceRect.Width;
                img.Color = order.Color;
                img.ToolTip = order.Name;
                img.HoverColor = Color.Lerp(img.Color, Color.White, 0.5f);

                btn.OnClicked += (GUIButton button, object userData) =>
                {
                    if (Character.Controlled == null || !Character.Controlled.CanSpeak) return false;
                    SetCharacterOrder(character, order, null, Character.Controlled);
                    return true;
                };

                btn.ToolTip = order.Name;
                x -= iconWidth + padding;
            }

            var characterArea = new GUIButton(new Rectangle(0, 0, characterInfoWidth - padding - (int)(height * 0.7f), 0), null, Alignment.CenterLeft, "GUITextBox", frame)
            {
                Padding = Vector4.Zero,
                UserData = character
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

            var characterImage = new GUIImage(new Rectangle(0, 0, 0, 0), character.Info.HeadSprite, Alignment.CenterLeft, characterArea)
            {
                CanBeFocused = false,
                HoverColor = Color.White,
                SelectedColor = Color.White
            };

            var characterName = new GUITextBlock(new Rectangle(0, 0, characterArea.Rect.Width - characterImage.Rect.Width, 0), character.Name, "", Alignment.CenterRight, Alignment.CenterLeft, characterArea, true, GUI.SmallFont)
            {
                HoverColor = Color.Transparent,
                SelectedColor = Color.Transparent,
                CanBeFocused = false
            };
            return frame;
        }
        
        /// <summary>
        /// Sets which character is selected in the crew UI (highlight effect etc)
        /// </summary>
        public bool CharacterClicked(GUIComponent component, object selection)
        {
            Character character = selection as Character;
            if (character == null || character.IsDead || character.IsUnconscious) return false;
            Character.Controlled = character;
            return true;
        }

        /// <summary>
        /// Sets which character is selected in the crew UI (highlight effect etc)
        /// </summary>
        public void SetCharacterSelected(Character character)
        {
            if (character != null && !characters.Contains(character)) return;
            
            GUIComponent selectedCharacterFrame = null;
            foreach (GUIComponent child in characterListBox.children)
            {
                GUIButton button = child.children.Find(c => c.UserData is Character) as GUIButton;
                if (button == null) continue;

                bool isSelectedCharacter = (Character)button.UserData == character;

                button.Selected = isSelectedCharacter;
                child.GetChild("reportbuttons").Visible = isSelectedCharacter;
                child.GetChild("orderbuttons").Visible = !isSelectedCharacter;

                if ((Character)button.UserData == character)
                {
                    selectedCharacterFrame = child;
                }
            }

            if (selectedCharacterFrame != null)
            {
                //move the selected character to the top of the list
                characterListBox.RemoveChild(selectedCharacterFrame);
                characterListBox.children.Insert(0, selectedCharacterFrame);
                characterListBox.BarScroll = 0.0f; 
            }      
        }

        public void ReviveCharacter(Character revivedCharacter)
        {
            GUIComponent characterBlock = characterListBox.GetChild(revivedCharacter) as GUIComponent;
            if (characterBlock != null)
            {
                characterBlock.Color = Color.Transparent;
            }
            else
            {
                AddCharacter(revivedCharacter);
            }
        }

        public void KillCharacter(Character killedCharacter)
        {
            GUIComponent characterBlock = characterListBox.GetChild(killedCharacter) as GUIComponent;
            if (characterBlock != null)
            {
                CoroutineManager.StartCoroutine(KillCharacterAnim(characterBlock));
            }
            RemoveCharacter(killedCharacter);
        }

        private IEnumerable<object> KillCharacterAnim(GUIComponent component)
        {
            List<GUIComponent> components = component.GetAllChildren();
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
                    comp.Rect = new Rectangle(component.Rect.X, component.Rect.Y, component.Rect.Width, (int)(component.Rect.Height * (1.0f - (timer / hideDuration))));
                }
                timer += CoroutineManager.DeltaTime;
                yield return CoroutineStatus.Running;
            }
            component.Parent.RemoveChild(component);
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
                availableSpeakers.RemoveAll(c => !(c.AIController is HumanAIController) || c.IsDead || !c.CanSpeak);
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
                    if (!pendingConversationLines[0].First.CanSpeak)
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
            if (order.TargetAllCharacters)
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
            foreach (GUIComponent child in characterListBox.children)
            {
                var characterFrame = characterListBox.FindChild(character);
                if (characterFrame == null) continue;

                var currentOrderIcon = characterFrame.FindChild("currentorder");
                if (currentOrderIcon != null)
                {
                    characterFrame.RemoveChild(currentOrderIcon);
                }

                int iconSize = (int)(characterFrame.Rect.Height * 0.8f);
                var img = new GUIImage(new Rectangle(characterInfoWidth - iconSize - 10, 0, iconSize, iconSize), order.SymbolSprite, Alignment.CenterLeft, characterFrame);
                img.Scale = characterFrame.Rect.Height / (float)img.SourceRect.Width * 0.8f;
                img.Color = order.Color;
                img.UserData = "currentorder";
                img.ToolTip = order.Name;
            }
        }
        
        /// <summary>
        /// Create the UI panel that's used to select the target and options for a given order 
        /// (which railgun to use, whether to power up the reactor or shut it down...)
        /// </summary>
        private void CreateOrderTargetFrame(GUIComponent orderButton, Character character, Order order)
        {
            List<Item> matchingItems = new List<Item>();
            if (order.ItemComponentType != null || !string.IsNullOrEmpty(order.ItemName))
            {
                matchingItems = !string.IsNullOrEmpty(order.ItemName) ?
                    Item.ItemList.FindAll(it => it.Name == order.ItemName) :
                    Item.ItemList.FindAll(it => it.components.Any(ic => ic.GetType() == order.ItemComponentType));
                orderTargetFrame = new GUIFrame(new Rectangle(orderButton.Rect.Center.X, orderButton.Rect.Center.Y, 200, matchingItems.Count * (order.Options.Length + 1) * 30 + 20), "InnerFrame", null);
            }
            else
            {
                matchingItems.Add(null);
                orderTargetFrame = new GUIFrame(new Rectangle(orderButton.Rect.Center.X, orderButton.Rect.Center.Y, 200, (order.Options.Length + 1) * 30 + 20), "InnerFrame", null);
            }
            orderTargetFrame.Padding = Vector4.One * 10;

            int y = 0;
            foreach (Item item in matchingItems)
            {
                new GUITextBlock(new Rectangle(0, y, 0, 20), item != null ? item.Name : order.Name, "", Alignment.TopLeft, Alignment.CenterLeft, orderTargetFrame);
                y += 20;
                
                foreach (string orderOption in order.Options)
                {
                    var optionButton = new GUIButton(new Rectangle(10, y, 0, 30), orderOption, null, Alignment.TopLeft, Alignment.TopLeft, "GUITextBox", orderTargetFrame);

                    optionButton.UserData = item == null ? order : new Order(order, item, item.components.Find(ic => ic.GetType() == order.ItemComponentType));
                    optionButton.OnClicked += (btn, userData) =>
                    {
                        if (Character.Controlled == null) return false;
                        SetCharacterOrder(character, userData as Order, orderOption, Character.Controlled);
                        orderTargetFrame = null;
                        return true;
                    };

                    orderButton.Padding = Vector4.Zero;
                    y += 30;
                }
            }
        }

        #region Updating and drawing the UI

        public void AddToGUIUpdateList()
        {
            guiFrame.AddToGUIUpdateList();
            if (orderTargetFrame != null) orderTargetFrame.AddToGUIUpdateList();
            
            if (reportButtonContainer.CountChildren > 0 && ReportButtonsVisible())
            {
                reportButtonContainer.AddToGUIUpdateList();
            }
        }

        partial void UpdateProjectSpecific(float deltaTime)
        {
            guiFrame.Update(deltaTime);
            if (chatBox != null) chatBox.Update(deltaTime);
            
            bool crewMenuOpen = toggleCrewAreaOpen || orderTargetFrame != null;
            int toggleButtonX = Math.Min((int)crewAreaOffset.X + crewAreaWidth + characterInfoWidth, crewAreaWidth + toggleCrewButton.Rect.Width);
            if (crewArea.Rect.Contains(PlayerInput.MousePosition))
            {
                if (PlayerInput.MousePosition.X < toggleButtonX + toggleCrewButton.Rect.Width * 2) crewMenuOpen = true;
            }

            scrollButtonUp.Visible = characterListBox.BarScroll > 0.0f && characterListBox.BarSize < 1.0f;
            scrollButtonUp.Rect = new Rectangle(crewArea.Rect.X, scrollButtonUp.Rect.Y, toggleButtonX - crewArea.Rect.X, scrollButtonUp.Rect.Height);
            if (GUIComponent.MouseOn == scrollButtonUp || scrollButtonUp.IsParentOf(GUIComponent.MouseOn))
            {
                characterListBox.BarScroll -= deltaTime * 2.0f * (float)Math.Sqrt(characterListBox.BarSize);
            }
            scrollButtonDown.Visible = characterListBox.BarScroll < 1.0f && characterListBox.BarSize < 1.0f;
            scrollButtonDown.Rect = new Rectangle(crewArea.Rect.X, scrollButtonDown.Rect.Y, toggleButtonX - crewArea.Rect.X, scrollButtonDown.Rect.Height);
            if (GUIComponent.MouseOn == scrollButtonDown || scrollButtonDown.IsParentOf(GUIComponent.MouseOn))
            {
                characterListBox.BarScroll += deltaTime * 2.0f * (float)Math.Sqrt(characterListBox.BarSize);
            }

            foreach (GUIComponent child in characterListBox.children)
            {
                child.Visible = Character.Controlled != null && Character.Controlled.TeamID == ((Character)child.UserData).TeamID;
            }

            crewAreaOffset.X = MathHelper.Lerp(
                crewAreaOffset.X,
                crewMenuOpen ? characterInfoWidth + crewArea.Rect.X : -crewAreaWidth, 
                deltaTime * 10.0f);
            //crewAreaOffset.Y = crewArea.Rect.Y;
            //crewArea.Rect = new Rectangle(crewAreaOffset.ToPoint(), crewArea.Rect.Size);
            foreach (GUIComponent child in characterListBox.children)
            {
                var orderButtons = child.GetChild("orderbuttons");
                var reportButtons = child.GetChild("reportbuttons");

                orderButtons.Rect = new Rectangle(new Point((int)crewAreaOffset.X, orderButtons.Rect.Y), orderButtons.Rect.Size);
                reportButtons.Rect = new Rectangle(new Point((int)crewAreaOffset.X, reportButtons.Rect.Y), reportButtons.Rect.Size);
            }

            toggleCrewButton.Rect = new Rectangle(new Point(toggleButtonX, toggleCrewButton.Rect.Y), toggleCrewButton.Rect.Size);

            if (GUIComponent.KeyboardDispatcher.Subscriber == null && 
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
                hoverArea.Inflate(100,100);
                if (!hoverArea.Contains(PlayerInput.MousePosition))
                {
                    orderTargetFrame = null;
                }
                else
                {
                    orderTargetFrame.Update(deltaTime);
                }
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            crewArea.Visible = characters.Count > 0 && CharacterHealth.OpenHealthWindow == null;
            if (orderTargetFrame != null) orderTargetFrame.Visible = characterListBox.Visible;
            
            guiFrame.Draw(spriteBatch);

            if (orderTargetFrame != null) orderTargetFrame.Draw(spriteBatch);


            if (reportButtonContainer.CountChildren > 0 && ReportButtonsVisible())
            {
                reportButtonContainer.Draw(spriteBatch);
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

            int y = 20;
            for (int i = 0; i < teamIDs.Count; i++)
            {
                if (teamIDs.Count > 1)
                {
                    new GUITextBlock(new Rectangle(0, y - 20, 100, 20), CombatMission.GetTeamName(teamIDs[i]), "", crewFrame);
                }

                GUIListBox crewList = new GUIListBox(new Rectangle(0, y, 280, listBoxHeight), Color.White * 0.7f, "", crewFrame);
                crewList.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);
                crewList.OnSelected = (component, obj) =>
                {
                    SelectCrewCharacter(component.UserData as Character, crewList);
                    return true;
                };
            
                foreach (Character character in crew.FindAll(c => c.TeamID == teamIDs[i]))
                {
                    GUIFrame frame = new GUIFrame(new Rectangle(0, 0, 0, 40), Color.Transparent, "ListBoxElement", crewList);
                    frame.UserData = character;
                    frame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
                    frame.Color = (GameMain.NetworkMember != null && GameMain.NetworkMember.Character == character) ? Color.Gold * 0.2f : Color.Transparent;

                    GUITextBlock textBlock = new GUITextBlock(
                        new Rectangle(40, 0, 0, 25),
                        ToolBox.LimitString(character.Info.Name + " (" + character.Info.Job.Name + ")", GUI.Font, frame.Rect.Width-20),
                        null,null,
                        Alignment.Left, Alignment.Left,
                        "", frame);
                    textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

                    new GUIImage(new Rectangle(-10, 0, 0, 0), character.AnimController.Limbs[0].sprite, Alignment.Left, frame);
                }

                y += crewList.Rect.Height + 30;
            }
        }

        /// <summary>
        /// Select a character from CrewListFrame
        /// </summary>
        protected bool SelectCrewCharacter(Character character, GUIComponent crewList)
        {
            if (character == null) return false;

            GUIComponent existingFrame = crewList.Parent.FindChild("SelectedCharacter");
            if (existingFrame != null) crewList.Parent.RemoveChild(existingFrame);

            var previewPlayer = new GUIFrame(
                new Rectangle(0, 0, 230, 300),
                new Color(0.0f, 0.0f, 0.0f, 0.8f), Alignment.TopRight, "", crewList.Parent);
            previewPlayer.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
            previewPlayer.UserData = "SelectedCharacter";

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
            bool hasRadio = false;
            if (Character.Controlled?.CurrentHull != null && Character.Controlled.CanSpeak)
            {
                WifiComponent radio = GetHeadset(Character.Controlled, true);
                hasRadio = radio != null && radio.CanTransmit();
            }

            if (hasRadio)
            {
                bool hasFires = Character.Controlled.CurrentHull.FireSources.Count > 0;
                ToggleReportButton("reportfire", hasFires);

                bool hasLeaks = Character.Controlled.CurrentHull.ConnectedGaps.Any(g => !g.IsRoomToRoom && g.Open > 0.0f);
                ToggleReportButton("reportbreach", hasLeaks);

                bool hasIntruders = Character.CharacterList.Any(c =>
                    c.CurrentHull == Character.Controlled.CurrentHull && !c.IsDead &&
                    (c.AIController is EnemyAIController || c.TeamID != Character.Controlled.TeamID));

                ToggleReportButton("reportintruders", hasIntruders);

                if (reportButtonContainer.CountChildren > 0 && ReportButtonsVisible())
                {
                    reportButtonContainer.Update(deltaTime);
                }
            }
            else
            {
                reportButtonContainer.ClearChildren();
            }
        }

        /// <summary>
        /// Should report buttons be visible on the screen atm?
        /// </summary>
        private bool ReportButtonsVisible()
        {
            return CharacterHealth.OpenHealthWindow == null;
        }

        private GUIButton CreateReportButton(Rectangle rect, Order order, GUIComponent parent, bool createSymbol = true)
        {
            var orderButton = new GUIButton(rect, order.Name, null, Alignment.TopCenter, Alignment.Center, "GUITextBox", parent);
            orderButton.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
            orderButton.UserData = order;
            orderButton.OnClicked = ReportButtonClicked;

            if (createSymbol)
            {
                var symbol = new GUIImage(new Rectangle(0, -60, 64, 64), order.SymbolSprite, Alignment.TopCenter, orderButton);
                symbol.Color = order.Color;

                orderButton.children.Insert(0, symbol);
                orderButton.children.RemoveAt(orderButton.children.Count - 1);
            }

            return orderButton;
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
            var existingButton = reportButtonContainer.GetChild(order);

            //already reported, disable the button
            if (GameMain.GameSession.CrewManager.ActiveOrders.Any(o =>
                o.First.TargetEntity == Character.Controlled.CurrentHull &&
                o.First.AITag == orderAiTag))
            {
                enabled = false;
            }

            if (enabled)
            {
                if (existingButton == null)
                {
                    CreateReportButton(new Rectangle(0, 0, 0, 20), order, reportButtonContainer, true);
                }
            }
            else
            {
                if (existingButton != null) reportButtonContainer.RemoveChild(existingButton);
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
            foreach (Character c in characters)
            {
                c.Info.UpdateCharacterItems();
            }

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
            reportButtonContainer.ClearChildren();
        }

        public void Save(XElement parentElement)
        {
            XElement element = new XElement("crew");

            foreach (CharacterInfo ci in characterInfos)
            {
                ci.Save(element);
            }

            parentElement.Add(element);
        }
    }
}
