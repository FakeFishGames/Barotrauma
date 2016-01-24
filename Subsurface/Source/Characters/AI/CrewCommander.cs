using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Barotrauma
{
    class CrewCommander
    {
        private CrewManager crewManager;

        private GUIFrame frame;
        //GUIListBox characterList;

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
        }

        private void CreateGUIFrame()
        {
            frame = new GUIFrame(Rectangle.Empty, Color.Black * 0.6f);
            frame.Padding = new Vector4(200.0f, 100.0f, 200.0f, 100.0f);

            GUIButton closeButton = new GUIButton(new Rectangle(0, 50, 100, 20), "Close", Alignment.BottomCenter, GUI.Style, frame);
            closeButton.OnClicked = (GUIButton button, object userData) =>
            {
                ToggleGUIFrame();
                return false;
            };

            //UpdateCharacters();

            int buttonWidth = 130;
            int spacing = 20;

            int y = 50;

            for (int n = 0; n<2; n++)
            {                                      
                List<Order> orders = (n==0) ? 
                    Order.PrefabList.FindAll(o => o.ItemComponentType == null) : 
                    Order.PrefabList.FindAll(o=> o.ItemComponentType != null);

                int startX = -(buttonWidth * orders.Count + spacing * (orders.Count - 1)) / 2;
                
                int i=0;
                foreach (Order order in orders)
                {
                    int x = startX + (buttonWidth + spacing) * (i % orders.Count);

                    if (order.ItemComponentType!=null)
                    {
                        var matchingItems = Item.ItemList.FindAll(it => it.components.Find(ic => ic.GetType() == order.ItemComponentType) != null);
                        int y2 = y;
                        foreach (Item it in matchingItems)
                        {
                            var newOrder = new Order(order, it.components.Find(ic => ic.GetType() == order.ItemComponentType));

                            CreateOrderButton(new Rectangle(x + buttonWidth / 2, y2, buttonWidth, 20), newOrder, y2==y);
                            y2 += 25;
                        }
                    }
                    else
                    {
                        CreateOrderButton(new Rectangle(x + buttonWidth / 2, y, buttonWidth, 20), order);
                    }
                    i++;
                }

                y += 100;
            }
        }

        private GUIButton CreateOrderButton(Rectangle rect, Order order, bool createSymbol = true)
        {
            var orderButton = new GUIButton(rect, order.Name, Color.Black * 0.7f, Alignment.TopCenter, Alignment.Center, null, frame);
            orderButton.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
            orderButton.TextColor = Color.White;
            orderButton.Color = Color.Black * 0.5f;
            orderButton.HoverColor = Color.LightGray * 0.5f;
            orderButton.OutlineColor = Color.LightGray * 0.8f;
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
                if (child.UserData as Character == null) continue;

                prevCharacterFrames.Add(child);
            }
            
            foreach (GUIComponent child in prevCharacterFrames)
            {
                frame.RemoveChild(child);
            }

            List<Character> aliveCharacters = crewManager.characters.FindAll(c => !c.IsDead);

            characterFrameBottom = 0;

            int charactersPerRow = 4;

            int spacing = 5;

            int rows = (int)Math.Ceiling((double)aliveCharacters.Count / charactersPerRow);

            int i = 0;
            foreach (Character character in aliveCharacters)
            {
                int rowCharacterCount = Math.Min(charactersPerRow, aliveCharacters.Count);
                //if (i >= aliveCharacters.Count - charactersPerRow && aliveCharacters.Count % charactersPerRow > 0) rowCharacterCount = aliveCharacters.Count % charactersPerRow;

               // rowCharacterCount = Math.Min(rowCharacterCount, aliveCharacters.Count - i);
                int startX = (int)-(150 * rowCharacterCount + spacing * (rowCharacterCount - 1)) / 2;


                int x = startX + (150 + spacing) * (i % Math.Min(charactersPerRow, aliveCharacters.Count));
                int y = (105 + spacing)*((int)Math.Floor((double)i / charactersPerRow));

                GUIButton characterButton = new GUIButton(new Rectangle(x+75, y, 150, 40), "", Color.Black, Alignment.TopCenter, null, frame);
                
                characterButton.UserData = character;
                characterButton.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
                
                characterButton.Color = Character.Controlled == character ? Color.Gold * 0.2f : Color.Black * 0.5f;
                characterButton.HoverColor = Color.LightGray * 0.5f;
                characterButton.SelectedColor = Color.Gold * 0.6f;
                characterButton.OutlineColor = Color.LightGray * 0.8f;

                characterFrameBottom = Math.Max(characterFrameBottom, characterButton.Rect.Bottom);
                
                string name = character.Info.Name;
                if (character.Info.Job != null) name += '\n' + "(" + character.Info.Job.Name + ")";

                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(40, 0, 0, 25),
                    name,
                    Color.Transparent, Color.White,
                    Alignment.Left, Alignment.Left,
                    null, characterButton, false);
                textBlock.Font = GUI.SmallFont;
                textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

                new GUIImage(new Rectangle(-5, -5, 0, 0), character.AnimController.Limbs[0].sprite, Alignment.Left, characterButton);

                var humanAi = character.AIController as HumanAIController;
                if (humanAi != null && humanAi.CurrentOrder != null)
                {
                    CreateCharacterOrderFrame(characterButton, humanAi.CurrentOrder, humanAi.CurrentOrderOption);
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
            var characterButton = frame.FindChild(character) as GUIButton;

            if (characterButton == null) return;

            characterButton.Selected = true;
        }

        private bool SetOrder(GUIButton button, object userData)
        {
            Order order = userData as Order;

            List<Character> selectedCharacters = new List<Character>();
            foreach (GUIComponent child in frame.children)
            {
                var characterButton = child as GUIButton;
                characterButton.State = GUIComponent.ComponentState.None;

                if (!characterButton.Selected) continue;
                characterButton.Selected = false;

                CreateCharacterOrderFrame(characterButton, order, "");

                var humanAi = (characterButton.UserData as Character).AIController as HumanAIController;

                humanAi.SetOrder(order, "");
            }

            //characterList.Deselect();

            return true;
        }

        private void CreateCharacterOrderFrame(GUIComponent characterFrame, Order order, string selectedOption)
        {
            var character = characterFrame.UserData as Character;
            if (character == null) return;

            var humanAi = character.AIController as HumanAIController;
            if (humanAi == null) return;

            var existingOrder = characterFrame.children.Find(c => c.UserData as Order != null);
            if (existingOrder != null) characterFrame.RemoveChild(existingOrder);

            var orderFrame = new GUIFrame(new Rectangle(-5, characterFrame.Rect.Height, characterFrame.Rect.Width, 30 + order.Options.Length * 15), null, characterFrame);
            orderFrame.OutlineColor = Color.LightGray * 0.5f;
            orderFrame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
            orderFrame.UserData = order;

            var img = new GUIImage(new Rectangle(0, 0, 20, 20), order.SymbolSprite, Alignment.TopLeft, orderFrame);
            img.Scale = 20.0f / img.SourceRect.Width;
            img.Color = order.Color;
            img.CanBeFocused = false;

            new GUITextBlock(new Rectangle(0, 0, 0, 20), order.DoingText, GUI.Style, Alignment.TopLeft, Alignment.TopCenter, orderFrame);





            var optionList = new GUIListBox(new Rectangle(0, 20, 0, 80), Color.Transparent, null, orderFrame);
            optionList.UserData = order;

            for (int i = 0; i < order.Options.Length; i++ )
            {
                var optionBox = new GUITextBlock(new Rectangle(0, 0, 0, 15), order.Options[i], GUI.Style, optionList);
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

            var humanAi = character.AIController as HumanAIController;
            if (humanAi == null) return false;

            humanAi.SetOrder(order, option);

            return true;
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            if (!IsOpen) return;

            frame.Draw(spriteBatch);
        }
    }
}
