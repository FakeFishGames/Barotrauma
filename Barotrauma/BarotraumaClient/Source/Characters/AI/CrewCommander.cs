using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class CrewCommander
    {
        private CrewManager crewManager;

        private GUIFrame frame;

        //container for order buttons that are targeted for the whole crew
        private GUIFrame generalOrderFrame;

        //listbox for report buttons that appear at the corner of the screen 
        //when there's something to report in the hull the character is currently in
        private GUIListBox reportButtonContainer;

        private GUIButton showAllButton;

        private GUIListBox characterList;

        private bool infoTextShown;

        private int characterFrameBottom;

        private bool autoScrolling;
        
        public bool IsOpen
        {
            get;
            private set;
        }

        public CrewCommander(CrewManager crewManager)
        {
            this.crewManager = crewManager;
            //PH: make space for the icon part of the report button
            Rectangle rect = HUDLayoutSettings.ReportArea;
            rect = new Rectangle(rect.X, rect.Y + 64, rect.Width, rect.Height);
            reportButtonContainer = new GUIListBox(rect, null, Alignment.TopRight, null);
            reportButtonContainer.Color = Color.Transparent;
            reportButtonContainer.Spacing = 50;
            reportButtonContainer.HideChildrenOutsideFrame = false;
        }

        private bool ReportButtonsVisible()
        {
            return CharacterHealth.OpenHealthWindow == null;
        }

        public void ToggleGUIFrame()
        {
            IsOpen = !IsOpen;

            if (IsOpen) 
            {
                if (!infoTextShown)
                {
                    GUI.AddMessage("Press " + GameMain.Config.KeyBind(InputType.CrewOrders) + " to open/close the command menu", Color.Cyan, 5.0f);
                    infoTextShown = true;
                }

                if (frame == null) CreateGUIFrame();
                UpdateCharacters();
            }
            showAllButton.Visible = false;
        }

        private void CreateGUIFrame()
        {
            frame = new GUIFrame(new Rectangle(200,0,0,0), Color.Black * 0.6f, null);
            frame.Padding = new Vector4(200.0f, 100.0f, 200.0f, 100.0f);
            
            generalOrderFrame = new GUIFrame(new Rectangle(-(int)(frame.Padding.X + frame.Rect.X), -(int)frame.Padding.Y, 200, GameMain.GraphicsHeight), Color.Black * 0.6f, null, frame);
            generalOrderFrame.Padding = new Vector4(10.0f, 100.0f, 10.0f, 100.0f);
            generalOrderFrame.ClampMouseRectToParent = false;
            int y = 0;
            foreach (Order order in Order.PrefabList)
            {
                if (!order.TargetAllCharacters) continue;
                CreateOrderButton(new Rectangle(0, y, 0, 20), order, generalOrderFrame);

                y += 80;
            }

            characterList = new GUIListBox(new Rectangle(0, 0, 0, 130), Color.Transparent, Alignment.TopCenter, "", frame, true);
            characterList.Color = Color.Transparent;
            characterList.BarScroll = 0.5f;

            GUIButton closeButton = new GUIButton(new Rectangle(0, 50, 100, 30), TextManager.Get("Close"), Alignment.BottomCenter, "", frame);
            closeButton.OnClicked = (GUIButton button, object userData) =>
            {
                ToggleGUIFrame();
                return false;
            };

            showAllButton = new GUIButton(new Rectangle(0, 0, 200, 40), "Show all commands", Alignment.BottomCenter, "", frame);
            showAllButton.Visible = false;
            showAllButton.OnClicked = (GUIButton button, object userData) =>
            {
                var selectedButton = characterList.children.Find(c => c.UserData is Character && c is GUIButton && ((GUIButton)c).Selected);
                if (selectedButton != null)
                {
                    CreateOrderButtons(selectedButton.UserData as Character, false);
                }
                return false;
            };
        }

        private void CreateOrderButtons(Character character, bool requireAppropriateJob)
        {
            var prevOrderButtons = frame.children.FindAll(c => c.UserData is Order);
            foreach (GUIComponent button in prevOrderButtons)
            {
                frame.RemoveChild(button);
            }
            
            List<Order> orders = requireAppropriateJob ?
                Order.PrefabList.FindAll(o => !o.TargetAllCharacters && o.HasAppropriateJob(character)) :
                Order.PrefabList.FindAll(o => !o.TargetAllCharacters);

            float scaleRatio = MathHelper.Lerp(0.3f, 1.2f, orders.Count / 10.0f);

            float arc = MathHelper.Pi * 0.7f * scaleRatio;
            float angleStep = arc / (orders.Count - 1);
            float startAngle = MathHelper.Pi - ((MathHelper.Pi - arc) / 2);
            
            int buttonWidth = 130;
            int spacing = 20;
            
            int ordersPerRow = Math.Min(orders.Count, 5);
            int startX = -(buttonWidth * ordersPerRow + spacing * (ordersPerRow - 1)) / 2;

            int i = 0;
            float angle = startAngle;

            float archWidth = frame.Rect.Width * 0.35f * Math.Min(scaleRatio, 1.0f);
            float archHeight = GameMain.GraphicsHeight * 0.35f * scaleRatio;

            foreach (Order order in orders)
            {
                int x = (int)(Math.Cos(angle)* archWidth);
                int y = (int)(120 + (float)Math.Sin(angle) * archHeight);

                angle -= angleStep;

                if (order.ItemComponentType != null || !string.IsNullOrEmpty(order.ItemName))
                {
                    List<Item> matchingItems = !string.IsNullOrEmpty(order.ItemName) ?
                        Item.ItemList.FindAll(it => it.Name == order.ItemName) :
                        Item.ItemList.FindAll(it => it.components.Any(ic => ic.GetType() == order.ItemComponentType));

                    int y2 = y;
                    foreach (Item it in matchingItems)
                    {
                        var newOrder = new Order(order, it, it.components.Find(ic => ic.GetType() == order.ItemComponentType));

                        var btn = CreateOrderButton(new Rectangle(x, y2, buttonWidth, 20), newOrder, frame, y2 == y);
                        CoroutineManager.StartCoroutine(MoveGUIComponent(btn, characterList.Rect.Center - new Point(btn.Rect.Width / 2, 0), btn.Rect.Location, 0.5f));
                        y2 += 25;
                    }
                }
                else
                {
                    var btn = CreateOrderButton(new Rectangle(x, y, buttonWidth, 20), order, frame);
                    CoroutineManager.StartCoroutine(MoveGUIComponent(btn, characterList.Rect.Center - new Point(btn.Rect.Width / 2, 0), btn.Rect.Location, 0.5f));
                }
            }
        }

        private IEnumerable<object> MoveGUIComponent(GUIComponent component, Point from, Point to, float duration)
        {
            float t = 0.0f;
            while (t < duration)
            {
                component.Rect = new Rectangle(new Point(
                    (int)MathHelper.Lerp(from.X, to.X, t / duration),
                    (int)MathHelper.Lerp(from.Y, to.Y, t / duration)), component.Rect.Size);

                t += CoroutineManager.DeltaTime;
                yield return CoroutineStatus.Running;
            }

            component.Rect = new Rectangle(to, component.Rect.Size);

            yield return CoroutineStatus.Success;
        }

        private GUIButton CreateOrderButton(Rectangle rect, Order order, GUIComponent parent, bool createSymbol = true)
        {
            var orderButton = new GUIButton(rect, order.Name, null, Alignment.TopCenter, Alignment.Center, "GUITextBox", parent);
            orderButton.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
            orderButton.UserData = order;
            orderButton.OnClicked = SetOrder;
            
            if (createSymbol)
            {
                var symbol = new GUIImage(new Rectangle(0,-60,64,64), order.SymbolSprite, Alignment.TopCenter, orderButton);
                symbol.Color = order.Color;

                orderButton.children.Insert(0, symbol);
                orderButton.children.RemoveAt(orderButton.children.Count-1);
            }
            
            return orderButton;
        }

        public void UpdateCharacters()
        {
            CreateGUIFrame();

            characterList.ClearChildren();
            new GUIFrame(new Rectangle(0, 0, characterList.Rect.Width / 2, 0), null, characterList);
            
            List<Character> aliveCharacters = crewManager.GetCharacters().FindAll(c => !c.IsDead);

            characterFrameBottom = 0;
            int charactersPerRow = 4;

            int i = 0;
            foreach (Character character in aliveCharacters)
            {
                if (character == Character.Controlled) continue;
                if (Character.Controlled?.TeamID != character.TeamID) continue;

                int rowCharacterCount = Math.Min(charactersPerRow, aliveCharacters.Count);
                
                GUIButton characterButton = new GUIButton(new Rectangle(0, 0, 150, 45), "", null, Alignment.TopCenter, "GUITextBox", characterList);                
                characterButton.UserData = character;
                characterButton.Padding = new Vector4(5.0f, 10.0f, 5.0f, 5.0f);                
                characterButton.Color = Character.Controlled == character ? Color.Gold : Color.White;
                characterButton.SelectedColor = Color.White;
                characterButton.OnClicked += (btn, userdata) =>
                {
                    SelectCharacter(userdata as Character);
                    CreateOrderButtons(userdata as Character, true);
                    showAllButton.Visible = true;

                    return true;
                };

                characterFrameBottom = Math.Max(characterFrameBottom, characterButton.Rect.Bottom);
                
                string name = character.Info.Name;
                if (character.Info.Job != null) name += '\n' + "(" + character.Info.Job.Name + ")";

                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(40, 0, 0, 25),
                    name,
                    Color.Transparent, null,
                    Alignment.Left, Alignment.Left,
                    "", characterButton, false);
                textBlock.Font = GUI.SmallFont;
                textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

                var characterPortrait = new GUIImage(new Rectangle(0, 0, 0, 0), character.AnimController.Limbs[0].sprite, Alignment.Left, characterButton);

                bool hasHeadset = false;
                bool headSetInRange = false;
                var targetRadio = GetHeadset(character, true);
                if (targetRadio != null && targetRadio.CanTransmit())
                {
                    hasHeadset = true;
                    if (Character.Controlled != null && Character.Controlled.Inventory != null)
                    {
                        var ownRadio = GetHeadset(Character.Controlled, true);
                        if (ownRadio != null && targetRadio.CanReceive(ownRadio)) headSetInRange = true;
                    }
                }

                if (!hasHeadset || !headSetInRange)
                {
                    characterButton.Enabled = false;
                    characterButton.ToolTip = !hasHeadset ? "Not wearing a headset." : "Out of radio range.";
                    textBlock.ToolTip = characterButton.ToolTip;
                    characterPortrait.ToolTip = characterButton.ToolTip;
                    characterPortrait.Color *= 0.5f;
                    textBlock.TextColor *= 0.5f;
                }
                else
                {
                    var humanAi = character.AIController as HumanAIController;
                    if (humanAi != null && humanAi.CurrentOrder != null)
                    {
                        CreateCharacterOrderFrame(characterButton, humanAi.CurrentOrder, humanAi.CurrentOrderOption);
                    }
                }

                i++;
            }
            
            new GUIFrame(new Rectangle(0, 0, characterList.Rect.Width / 2, 0), null, characterList);

            characterList.BarScroll = 0.5f;
        }

        private WifiComponent GetHeadset(Character character, bool requireEquipped)
        {
            if (character?.Inventory == null) return null;

            var radioItem = character.Inventory.Items.FirstOrDefault(it => it != null && it.GetComponent<WifiComponent>() != null);
            if (radioItem == null) return null;
            if (requireEquipped && !character.HasEquippedItem(radioItem)) return null;
            
            return  radioItem.GetComponent<WifiComponent>();
        }

        public void SelectCharacter(Character character)
        {
            foreach (GUIComponent child in characterList.children)
            {
                GUIButton button = child as GUIButton;
                if (button == null) continue;

                Character buttonCharacter = child.UserData as Character;
                button.Selected = buttonCharacter == character;
                if (button.Selected) autoScrolling = true;
            }
        }

        public void SetOrder(Character character, Order order)
        {
            if (order.TargetAllCharacters)
            {
                if (Character.Controlled == null || Character.Controlled.CurrentHull == null) return;
                crewManager.AddOrder(new Order(order.Prefab, Character.Controlled.CurrentHull, null), order.Prefab.FadeOutTime);

                if (crewManager.IsSinglePlayer)
                {
                    Character.Controlled.Speak(
                        order.GetChatMessage("", Character.Controlled.CurrentHull?.RoomName), ChatMessageType.Order);
                }
                else
                {
                    OrderChatMessage msg = new OrderChatMessage(order, "", Character.Controlled.CurrentHull, null, Character.Controlled);
                    if (GameMain.Client != null)
                    {
                        GameMain.Client.SendChatMessage(msg);
                    }
                    else if (GameMain.Server != null)
                    {
                        GameMain.Server.SendOrderChatMessage(msg, null);
                    }
                }
                return;
            }

            character.SetOrder(order, "");
            if (crewManager.IsSinglePlayer)
            {
                Character.Controlled?.Speak(
                    order.GetChatMessage(character.Name, Character.Controlled.CurrentHull?.RoomName), ChatMessageType.Order);
            }
            else
            {
                OrderChatMessage msg = new OrderChatMessage(order, "", order.TargetItemComponent?.Item, character, Character.Controlled);
                if (GameMain.Client != null)
                {
                    GameMain.Client.SendChatMessage(msg);
                }
                else if (GameMain.Server != null)
                {
                    GameMain.Server.SendOrderChatMessage(msg, null);
                }
            }            
        }

        private bool SetOrder(GUIButton button, object userData)
        {
            //order targeted to all characters
            Order order = userData as Order;
            if (order.TargetAllCharacters)
            {
                if (Character.Controlled == null || Character.Controlled.CurrentHull == null) return false;
                crewManager.AddOrder(new Order(order.Prefab, Character.Controlled.CurrentHull, null), order.Prefab.FadeOutTime);

                if (crewManager.IsSinglePlayer)
                {
                    Character.Controlled.Speak(
                        order.GetChatMessage("", Character.Controlled.CurrentHull?.RoomName), ChatMessageType.Order);
                }
                else
                {
                    OrderChatMessage msg = new OrderChatMessage(order, "", Character.Controlled.CurrentHull, null, Character.Controlled);
                    if (GameMain.Client != null)
                    {
                        GameMain.Client.SendChatMessage(msg);
                    }
                    else if (GameMain.Server != null)
                    {
                        GameMain.Server.SendOrderChatMessage(msg, null);
                    }
                }

                return true;
            }

            //order targeted to a specific character
            foreach (GUIComponent child in characterList.children)
            {
                Character character = child.UserData as Character;
                if (character == null) continue;

                var characterButton = child as GUIButton;
                if (!characterButton.Selected) continue;

                CreateCharacterOrderFrame(characterButton, order, "");
                character.SetOrder(order, "");        
                
                if (crewManager.IsSinglePlayer)
                {
                    Character.Controlled.Speak(
                        order.GetChatMessage(character.Name, Character.Controlled.CurrentHull?.RoomName), ChatMessageType.Order);
                }
                else
                {
                    OrderChatMessage msg = new OrderChatMessage(order, "", order.TargetItemComponent?.Item, character, Character.Controlled);
                    if (GameMain.Client != null)
                    {
                        GameMain.Client.SendChatMessage(msg);
                    }
                    else if (GameMain.Server != null)
                    {
                        GameMain.Server.SendOrderChatMessage(msg, null);
                    }     
                }
            }
            
            return true;
        }

        private void CreateCharacterOrderFrame(GUIComponent characterButton, Order order, string selectedOption)
        {
            var character = characterButton.UserData as Character;
            if (character == null) return;
            
            var existingOrder = characterButton.children.Find(c => c.UserData is Order);
            if (existingOrder != null) characterButton.RemoveChild(existingOrder);
            
            var orderFrame = new GUIFrame(new Rectangle(-5, 40, characterButton.Rect.Width, 30 + order.Options.Length * 15), "InnerFrame", characterButton);
            orderFrame.ClampMouseRectToParent = false;
            orderFrame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
            orderFrame.UserData = order;

            var img = new GUIImage(new Rectangle(0, 0, 20, 20), order.SymbolSprite, Alignment.TopLeft, orderFrame);
            img.Scale = 20.0f / img.SourceRect.Width;
            img.Color = order.Color;
            img.CanBeFocused = false;

            new GUITextBlock(new Rectangle(img.Rect.Width, 0, 0, 20), order.DoingText, "", Alignment.TopLeft, Alignment.CenterLeft, orderFrame);
            
            var optionList = new GUIListBox(new Rectangle(0, 20, 0, 80), Color.Transparent, null, orderFrame);
            optionList.ClampMouseRectToParent = false;
            optionList.UserData = order;

            for (int i = 0; i < order.Options.Length; i++ )
            {
                var optionBox = new GUITextBlock(new Rectangle(0, 0, 0, 15), order.Options[i], "", Alignment.TopLeft, Alignment.CenterLeft, optionList);
                optionBox.Font = GUI.SmallFont;
                optionBox.UserData = order.Options[i];

                if (selectedOption == order.Options[i])
                {
                    optionList.Select(i);
                }
            }
            //optionList.OnSelected = SelectOrderOption;

        }

        public bool SetOrderOption(Character character, Order order, string option)
        {
            if (crewManager.IsSinglePlayer)
            {
                Character.Controlled.Speak(
                    order.GetChatMessage(character.Name, Character.Controlled.CurrentHull?.RoomName, option), ChatMessageType.Order);
            }
            else
            {
                OrderChatMessage msg = new OrderChatMessage(order, option, order.TargetItemComponent?.Item, character, Character.Controlled);
                if (GameMain.Client != null)
                {
                    GameMain.Client.SendChatMessage(msg);
                }
                else if (GameMain.Server != null)
                {
                    GameMain.Server.SendOrderChatMessage(msg, null);
                }
            }

            character.SetOrder(order, option);

            return true;
        }

        public void AddToGUIUpdateList()
        {
            if (IsOpen && frame != null) frame.AddToGUIUpdateList();

            if (reportButtonContainer.CountChildren > 0 && ReportButtonsVisible())
            {
                reportButtonContainer.AddToGUIUpdateList();
            }
        }

        public void Update(float deltaTime)
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

            if (frame == null || !IsOpen) return;

            foreach (GUIComponent child in generalOrderFrame.children)
            {
                GUIButton orderButton = child as GUIButton;
                if (orderButton == null) continue;
                if (Character.Controlled == null || Character.Controlled.CurrentHull == null || !Character.Controlled.CanSpeak)
                {
                    orderButton.Enabled = false;
                }
                else
                {
                    WifiComponent radio = GetHeadset(Character.Controlled, true);
                    orderButton.Enabled = radio != null && radio.CanTransmit();
                }
            }

            frame.Update(deltaTime);
            if (characterList.Selected != null && autoScrolling)
            {
                float xDiff = frame.Rect.Center.X - characterList.Selected.Rect.Center.X;
                characterList.BarScroll -= MathHelper.Clamp(xDiff * 0.01f, -10.0f, 10.0f) * deltaTime;

                if (Math.Abs(xDiff) < 5.0f) autoScrolling = false;
            }

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
                    CreateOrderButton(new Rectangle(0,0,0,20), order, reportButtonContainer, true);
                }
            }
            else
            {
                if (existingButton != null) reportButtonContainer.RemoveChild(existingButton);
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (reportButtonContainer.CountChildren > 0 && ReportButtonsVisible())
            {
                reportButtonContainer.Draw(spriteBatch);
            }

            if (!IsOpen) return;
            frame.Draw(spriteBatch);
        }
    }
}
