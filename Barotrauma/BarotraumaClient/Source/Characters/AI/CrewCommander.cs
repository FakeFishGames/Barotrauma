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

        private GUIButton showAllButton;

        private bool infoTextShown;

        private int characterFrameBottom;

        public GUIFrame Frame
        {
            get { return IsOpen ? frame : null; }
        }

        public bool IsOpen
        {
            get;
            private set;
        }

        public CrewCommander(CrewManager crewManager)
        {
            this.crewManager = crewManager;
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

            var generalOrderFrame = new GUIFrame(new Rectangle(-400, 0, 200, 0), Color.Black * 0.6f, null, frame);
            generalOrderFrame.ClampMouseRectToParent = false;
            int y = 0;
            foreach (Order order in Order.PrefabList)
            {
                if (!order.TargetAllCharacters) continue;
                CreateOrderButton(new Rectangle(0,y,0,20), order, generalOrderFrame);

                y += 80;
            }

            GUIButton closeButton = new GUIButton(new Rectangle(0, 0, 100, 30), "Close", Alignment.BottomCenter, "", frame);
            closeButton.OnClicked = (GUIButton button, object userData) =>
            {
                ToggleGUIFrame();
                return false;
            };

            showAllButton = new GUIButton(new Rectangle(0, 50, 200, 40), "Show all commands", Alignment.BottomCenter, "", frame);
            showAllButton.Visible = false;
            showAllButton.OnClicked = (GUIButton button, object userData) =>
            {
                var selectedButton = frame.children.Find(c => c.UserData is Character && c is GUIButton && ((GUIButton)c).Selected);
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
            float startAngle = (MathHelper.Pi - arc) / 2;

            //int y = 250;
            int buttonWidth = 130;
            int spacing = 20;
            
            int ordersPerRow = Math.Min(orders.Count, 5);
            int startX = -(buttonWidth * ordersPerRow + spacing * (ordersPerRow - 1)) / 2;

            int i = 0;
            float angle = startAngle;

            float archWidth = GameMain.GraphicsWidth * 0.35f * scaleRatio;
            float archHeight = GameMain.GraphicsHeight * 0.35f * scaleRatio;

            foreach (Order order in orders)
            {
                int x = (int)(Math.Cos(angle)* archWidth);
                int y = (int)(100 + (float)Math.Sin(angle) * archHeight);

                angle += angleStep;

                if (order.ItemComponentType != null || !string.IsNullOrEmpty(order.ItemName))
                {
                    List<Item> matchingItems = !string.IsNullOrEmpty(order.ItemName) ?
                        Item.ItemList.FindAll(it => it.Name == order.ItemName) :
                        Item.ItemList.FindAll(it => it.components.Any(ic => ic.GetType() == order.ItemComponentType));

                    int y2 = y;
                    foreach (Item it in matchingItems)
                    {
                        var newOrder = new Order(order, it, it.components.Find(ic => ic.GetType() == order.ItemComponentType));

                        CreateOrderButton(new Rectangle(x, y2, buttonWidth, 20), newOrder, frame, y2 == y);
                        y2 += 25;
                    }
                }
                else
                {
                    CreateOrderButton(new Rectangle(x, y, buttonWidth, 20), order, frame);
                }
            }            
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

            List<GUIComponent> prevCharacterFrames = new List<GUIComponent>();
            foreach (GUIComponent child in frame.children)
            {
                if (!(child.UserData is Character)) continue;

                prevCharacterFrames.Add(child);
            }
            
            foreach (GUIComponent child in prevCharacterFrames)
            {
                frame.RemoveChild(child);
            }
            
            List<Character> aliveCharacters = crewManager.GetCharacters().FindAll(c => !c.IsDead);

            characterFrameBottom = 0;
            int charactersPerRow = 4;
            int spacing = 5;

            int i = 0;
            foreach (Character character in aliveCharacters)
            {
                if (character == Character.Controlled) continue;
                if (Character.Controlled?.TeamID != character.TeamID) continue;

                int rowCharacterCount = Math.Min(charactersPerRow, aliveCharacters.Count);

                int startX = -(150 * rowCharacterCount + spacing * (rowCharacterCount - 1)) / 2;
                int x = startX + (150 + spacing) * (i % Math.Min(charactersPerRow, aliveCharacters.Count));
                int y = (105 + spacing)*((int)Math.Floor((double)i / charactersPerRow));
                
                GUIButton characterButton = new GUIButton(new Rectangle(x + 75, y, 150, 60), "", null, Alignment.TopCenter, "GUITextBox", frame);                
                characterButton.UserData = character;
                characterButton.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);                
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

                var characterPortrait = new GUIImage(new Rectangle(-5, -5, 0, 0), character.AnimController.Limbs[0].sprite, Alignment.Left, characterButton);

                bool hasHeadset = false;
                bool headSetInRange = false;
                var radioItem = character.Inventory.Items.FirstOrDefault(it => it != null && it.GetComponent<WifiComponent>() != null);
                if (radioItem != null && character.HasEquippedItem(radioItem))
                {
                    var radio = radioItem.GetComponent<WifiComponent>();
                    if (radio.CanTransmit())
                    {
                        hasHeadset = true;
                        if (Character.Controlled != null)
                        {
                            var ownRadioItem = Character.Controlled.Inventory.Items.FirstOrDefault(it => it != null && it.GetComponent<WifiComponent>() != null);
                            if (ownRadioItem != null && Character.Controlled.HasEquippedItem(ownRadioItem))
                            {
                                var ownRadio = ownRadioItem.GetComponent<WifiComponent>();
                                if (radio.CanReceive(ownRadio)) headSetInRange = true;                                
                            }
                        }
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

            foreach (GUIComponent child in frame.children)
            {
                if (!(child.UserData is Order)) continue;

                Rectangle rect = child.Rect;
                rect.Y += characterFrameBottom;

                child.Rect = rect;
            }
        }

        public void SelectCharacter(Character character)
        {
            foreach (GUIComponent child in frame.children)
            {
                GUIButton button = child as GUIButton;
                if (button == null) continue;
                Character buttonCharacter = child.UserData as Character;
                button.Selected = buttonCharacter == character;
            }
        }

        private bool SetOrder(GUIButton button, object userData)
        {
            Order order = userData as Order;
            if (order.TargetAllCharacters)
            {
                if (Character.Controlled == null) return false;
                crewManager.AddOrder(new Order(order.Prefab, Character.Controlled.CurrentHull, null), order.Prefab.FadeOutTime);
                OrderChatMessage msg = new OrderChatMessage(order, "", Character.Controlled.CurrentHull, null, Character.Controlled);
                if (GameMain.Client != null)
                {
                    GameMain.Client.SendChatMessage(msg);
                }
                else if (GameMain.Server != null)
                {
                    GameMain.Server.SendOrderChatMessage(msg, null);
                }

                return true;
            }

            foreach (GUIComponent child in frame.children)
            {
                Character character = child.UserData as Character;
                if (character == null) continue;

                var characterButton = child as GUIButton;
                if (!characterButton.Selected) continue;

                CreateCharacterOrderFrame(characterButton, order, "");
                character.SetOrder(order, "");                
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
            optionList.OnSelected = SelectOrderOption;

        }

        private bool SelectOrderOption(GUIComponent component, object userData)
        {
            string option = userData.ToString();
            Order order = component.Parent.UserData as Order;
            Character character = component.Parent.Parent.Parent.UserData as Character;

            character.SetOrder(order, option);

            OrderChatMessage msg = new OrderChatMessage(order, option, order.TargetItemComponent?.Item, character, Character.Controlled);
            if (GameMain.Client != null)
            {
                GameMain.Client.SendChatMessage(msg);
            }
            else if (GameMain.Server != null)
            {
                GameMain.Server.SendOrderChatMessage(msg, null);
            }

            return true;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (!IsOpen) return;

            frame.Draw(spriteBatch);
        }
    }
}
