using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class CrewManager
    {
        const float ChatMessageFadeTime = 10.0f;

        const float ConversationIntervalMin = 100.0f;
        const float ConversationIntervalMax = 180.0f;
        
        private List<CharacterInfo> characterInfos;
        private List<Character> characters;

        //orders that have not been issued to a specific character
        private List<Pair<Order, float>> activeOrders = new List<Pair<Order, float>>();

        public int WinningTeam = 1;
        
        private GUIFrame guiFrame;
        private GUIFrame characterFrame;
        private GUIListBox characterListBox;

        private float conversationTimer, conversationLineTimer;
        private List<Pair<Character, string>> pendingConversationLines = new List<Pair<Character, string>>();
        
        private GUIButton toggleCrewButton;
        private Vector2 crewAreaOffset;
        private bool toggleCrewAreaOpen;
        private int crewAreaWidth;
        private int characterInfoWidth;

        private ChatBox chatBox;

        private CrewCommander commander;

        private bool isSinglePlayer;

        private GUIComponent orderTargetFrame;

        public CrewCommander CrewCommander
        {
            get { return commander; }
        }

        public bool IsSinglePlayer
        {
            get { return isSinglePlayer; }
        }

        public List<Pair<Order, float>> ActiveOrders
        {
            get { return activeOrders; }
        }
                
        public CrewManager(bool isSinglePlayer)
        {
            this.isSinglePlayer = isSinglePlayer;
            characters = new List<Character>();
            characterInfos = new List<CharacterInfo>();
            
            guiFrame = new GUIFrame(new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight), Color.Transparent);
            guiFrame.Padding = Vector4.One * 5.0f;
            guiFrame.CanBeFocused = false;

            characterFrame = new GUIFrame(HUDLayoutSettings.CrewArea, null, guiFrame);
            toggleCrewButton = new GUIButton(new Rectangle(characterFrame.Rect.Width + 10, 0, 25, 70), "", "GUIButtonHorizontalArrow", characterFrame);
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
            
            characterListBox = new GUIListBox(Rectangle.Empty, Color.Transparent, null, characterFrame);
            characterListBox.Spacing = (int)(5 * GUI.Scale);
            characterListBox.ScrollBarEnabled = false;
            characterListBox.CanBeFocused = false;
            
            if (isSinglePlayer)
            {
                chatBox = new ChatBox(guiFrame, true);
            }

            commander = new CrewCommander(this);
        }

        public CrewManager(XElement element, bool isSinglePlayer)
            : this(isSinglePlayer)
        {
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "character") continue;

                characterInfos.Add(new CharacterInfo(subElement));
            }
        }

        public List<Character> GetCharacters()
        {
            return new List<Character>(characters);
        }

        public List<CharacterInfo> GetCharacterInfos()
        {
            return new List<CharacterInfo>(characterInfos);
        }

        /// <summary>
        /// Sets which character is selected in the crew UI (highlight effect etc)
        /// </summary>
        public bool SetCharacterSelected(GUIComponent component, object selection)
        {
            SetCharacterSelected(selection as Character);
            return true;
        }

        /// <summary>
        /// Sets which character is selected in the crew UI (highlight effect etc)
        /// </summary>
        public void SetCharacterSelected(Character character)
        {
            if (character == null || character.IsDead || character.IsUnconscious) return;
            if (!characters.Contains(character)) return;
            
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
            //move the selected character to the top of the list
            characterListBox.RemoveChild(selectedCharacterFrame);
            characterListBox.children.Insert(0, selectedCharacterFrame);
            characterListBox.BarScroll = 0.0f;

            Character.Controlled = character;
            
        }


        public void AddSinglePlayerChatMessage(string senderName, string text, ChatMessageType messageType, Character sender)
        {
            if (!isSinglePlayer)
            {
                DebugConsole.ThrowError("Cannot add messages to single player chat box in multiplayer mode!\n" + Environment.StackTrace);
                return;
            }

            chatBox.AddMessage(ChatMessage.Create(senderName, text, messageType, sender));
        }

        private void UpdateConversations(float deltaTime)
        {
            if (PlayerInput.KeyHit(Microsoft.Xna.Framework.Input.Keys.P))
            {
                conversationTimer = 0.0f;
            }

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
        
        public bool AddOrder(Order order, float fadeOutTime)
        {
            if (order.TargetEntity == null)
            {
                DebugConsole.ThrowError("Attempted to add an order with no target entity to CrewManager!\n" + Environment.StackTrace);
                return false;
            }

            Pair<Order, float> existingOrder = activeOrders.Find(o => o.First.Prefab == order.Prefab && o.First.TargetEntity == order.TargetEntity);
            if (existingOrder != null)
            {
                existingOrder.Second = fadeOutTime;
                return false;
            }
            else
            {
                activeOrders.Add(new Pair<Order, float>(order, fadeOutTime));
                return true;
            }
        }

        public void RemoveOrder(Order order)
        {
            activeOrders.RemoveAll(o => o.First == order);
        }

        public void SetCharacterOrder(Character character, Order order, string option = null)
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

                var img = new GUIImage(new Rectangle(0, 0, characterFrame.Rect.Height, characterFrame.Rect.Height), order.SymbolSprite, Alignment.CenterRight, characterFrame);
                img.Scale = characterFrame.Rect.Height / (float)img.SourceRect.Width;
                img.Color = order.Color;
                img.UserData = "currentorder";
                img.ToolTip = order.Name;
            }
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

            characters.Add(character);
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
                commander.SetOrder(character, ai.CurrentOrder);
            }
        }

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

            int height = (int)(60 * GUI.Scale);
            int iconWidth = (int)(40 * GUI.Scale);
            int padding = (int)(8 * GUI.Scale);

            characterInfoWidth = (int)(170 * GUI.Scale) + height;
            crewAreaWidth = orders.Count * (iconWidth + padding) + characterInfoWidth;

            var frame = new GUIFrame(new Rectangle(0, 0, 0, height), null, Alignment.TopRight, null, parent);
            frame.UserData = character;

            var orderButtonFrame = new GUIFrame(new Rectangle(0,0,frame.Rect.Width - characterInfoWidth, 0), null, frame);
            orderButtonFrame.UserData = "orderbuttons";

            int x = 0;// -characterInfoWidth;
            int correctAreaWidth = correctOrderCount * iconWidth + (correctOrderCount - 1) * padding;
            int neutralAreaWidth = neutralOrderCount * iconWidth + (neutralOrderCount - 1) * padding;
            int wrongAreaWidth = wrongOrderCount * iconWidth + (wrongOrderCount - 1) * padding;
            new GUIFrame(new Rectangle(x, 0, correctAreaWidth, 0), Color.LightGreen, Alignment.CenterRight, "InnerFrame", orderButtonFrame);
            new GUIFrame(new Rectangle(x - correctAreaWidth - padding, 0, neutralAreaWidth, 0), Color.LightGray, Alignment.CenterRight, "InnerFrame", orderButtonFrame);
            new GUIFrame(new Rectangle(x - correctAreaWidth - neutralAreaWidth - padding * 2, 0, wrongAreaWidth, 0), Color.Red, Alignment.CenterRight, "InnerFrame", orderButtonFrame);
            foreach (Order order in orders)
            {
                if (order.TargetAllCharacters) continue;
                var btn = new GUIButton(new Rectangle(x, 0, iconWidth, iconWidth), "", Alignment.CenterRight, null, orderButtonFrame);
                var img = new GUIImage(new Rectangle(0, 0, iconWidth, iconWidth), order.Prefab.SymbolSprite, Alignment.TopLeft, btn);
                img.Scale = iconWidth / (float)img.SourceRect.Width;
                img.Color = order.Color;
                img.ToolTip = order.Name;

                /*if (order.AppropriateJobs == null || order.AppropriateJobs.Length == 0) img.Color *= 0.8f;
                if (!order.HasAppropriateJob(character)) img.Color *= 0.6f;*/
                img.HoverColor = Color.Lerp(img.Color, Color.White, 0.5f);

                btn.OnClicked += (GUIButton button, object userData) =>
                {
                    if (order.ItemComponentType != null || !string.IsNullOrEmpty(order.ItemName) || order.Options.Length > 1)
                    {
                        CreateOrderTargetFrame(button, character, order);
                    }
                    else
                    {
                        commander.SetOrder(character, order);
                        SetCharacterOrder(character, order);
                    }
                    return true;
                };

                btn.ToolTip = order.Name;
                x -= iconWidth + padding;
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
                    commander.SetOrder(character, order);                    
                    return true;
                };

                btn.ToolTip = order.Name;
                x -= iconWidth + padding;
            }

            var characterArea = new GUIButton(new Rectangle(-height, 0, characterInfoWidth - padding - height, 0), null, Alignment.CenterRight, "GUITextBox", frame)
            {
                Padding = Vector4.Zero,
                UserData = character
            };
            if (isSinglePlayer) characterArea.OnClicked = SetCharacterSelected;

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
                        commander.SetOrderOption(character, userData as Order, orderOption);
                        SetCharacterOrder(character, userData as Order, orderOption);
                        orderTargetFrame = null;
                        return true;
                    };

                    orderButton.Padding = Vector4.Zero;
                    y += 30;
                }
            }
        }

        public void RemoveCharacter(Character character)
        {
            characters.Remove(character);
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

        public void AddToGUIUpdateList()
        {
            guiFrame.AddToGUIUpdateList();
            commander.AddToGUIUpdateList();
            if (orderTargetFrame != null) orderTargetFrame.AddToGUIUpdateList();
        }

        public void Update(float deltaTime)
        {
            guiFrame.Update(deltaTime);
            if (chatBox != null) chatBox.Update(deltaTime);

            if (commander.IsOpen &&
                (Character.Controlled == null || !characters.Contains(Character.Controlled)))
            {
                commander.ToggleGUIFrame();
            }

            bool crewMenuOpen = toggleCrewAreaOpen || orderTargetFrame != null;

            if (characterFrame.Rect.Contains(PlayerInput.MousePosition))
            {
                if (crewAreaOffset.X > -characterFrame.Rect.Width + characterInfoWidth + 50 || PlayerInput.MousePosition.X < 0) crewMenuOpen = true;
            }
            
            crewAreaOffset.X = MathHelper.Lerp(
                crewAreaOffset.X,
                crewMenuOpen ? -characterFrame.Rect.Width + crewAreaWidth + 40 : -characterFrame.Rect.Width + characterInfoWidth + 20, 
                deltaTime * 10.0f);
            crewAreaOffset.Y = HUDLayoutSettings.CrewArea.Location.Y;
            characterFrame.Rect = new Rectangle(crewAreaOffset.ToPoint(), characterFrame.Rect.Size);
            
            if (GUIComponent.KeyboardDispatcher.Subscriber == null && 
                GameMain.Config.KeyBind(InputType.CrewOrders).IsHit() &&
                characters.Contains(Character.Controlled))
            {
                //deselect construction unless it's the ladders the character is climbing
                if (!commander.IsOpen && Character.Controlled != null && 
                    Character.Controlled.SelectedConstruction != null && 
                    Character.Controlled.SelectedConstruction.GetComponent<Items.Components.Ladder>() == null)
                {
                    Character.Controlled.SelectedConstruction = null;
                }
                
                commander.ToggleGUIFrame();                
            }

            UpdateConversations(deltaTime);

            foreach (Pair<Order, float> order in activeOrders)
            {
                order.Second -= deltaTime;
            }
            activeOrders.RemoveAll(o => o.Second <= 0.0f);

            commander.Update(deltaTime);

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

            /*if (isSinglePlayer)
            {
                for (int i = chatBox.children.Count - 1; i >= 0; i--)
                {
                    var textBlock = chatBox.children[i] as GUITextBlock;
                    if (textBlock == null) continue;

                    float alpha = (float)textBlock.UserData - (1.0f / ChatMessageFadeTime * deltaTime);
                    textBlock.UserData = alpha;
                    textBlock.TextColor = new Color(textBlock.TextColor, alpha);
                }
            }*/
        }

        public void ReviveCharacter(Character revivedCharacter)
        {
            GUIComponent characterBlock = characterListBox.GetChild(revivedCharacter) as GUIComponent;
            if (characterBlock != null) characterBlock.Color = Color.Transparent;

            if (revivedCharacter is AICharacter)
            {
                //commander.UpdateCharacters();
            }
        }

        public void KillCharacter(Character killedCharacter)
        {
            GUIComponent characterBlock = characterListBox.GetChild(killedCharacter) as GUIComponent;
            CoroutineManager.StartCoroutine(KillCharacterAnim(characterBlock));

            /*if (killedCharacter is AICharacter)
            {
                commander.UpdateCharacters();
            }*/        
        }

        private IEnumerable<object> KillCharacterAnim(GUIComponent component)
        {
            component.Color = Color.DarkRed;
            List<GUIComponent> components = new List<GUIComponent>();
            components.Add(component);
            components.AddRange(component.children);

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

        public void CreateCrewFrame(List<Character> crew, GUIFrame crewFrame)
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
        

        public void StartRound()
        {
            characterListBox.ClearChildren();
            characters.Clear();

            WayPoint[] waypoints = WayPoint.SelectCrewSpawnPoints(characterInfos, Submarine.MainSub, false);

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
                    Character.Controlled = character;
                    SetCharacterSelected(character);

                    if (character.Info != null && !character.Info.StartItemsGiven)
                    {
                        character.GiveJobItems(waypoints[i]);
                        character.Info.StartItemsGiven = true;
                    }
                }

                AddCharacter(character);
            }

            if (characters.Any()) characterListBox.Select(0);

            conversationTimer = Rand.Range(5.0f, 10.0f);
        }

        public void EndRound()
        {
            foreach (Character c in characters)
            {
                if (!c.IsDead)
                {
                    c.Info.UpdateCharacterItems();
                    continue;
                }

                characterInfos.Remove(c.Info);
            }

            //remove characterinfos whose character doesn't exist anymore
            //(i.e. character was removed during the round)
            characterInfos.RemoveAll(c => c.Character == null);
            
            characters.Clear();
            characterListBox.ClearChildren();
        }

        public void Reset()
        {
            characters.Clear();
            characterInfos.Clear();
            characterListBox.ClearChildren();
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            characterFrame.Visible = !commander.IsOpen && characters.Count > 0 && CharacterHealth.OpenHealthWindow == null;
            if (orderTargetFrame != null) orderTargetFrame.Visible = characterListBox.Visible;
            
            guiFrame.Draw(spriteBatch);
            commander.Draw(spriteBatch);

            if (orderTargetFrame != null) orderTargetFrame.Draw(spriteBatch);
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
