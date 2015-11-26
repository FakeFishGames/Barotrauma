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
        CrewManager crewManager;

        GUIFrame frame;
        //GUIListBox characterList;

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

            if (IsOpen && frame == null) CreateGUIFrame();
        }

        private void CreateGUIFrame()
        {
            frame = new GUIFrame(Rectangle.Empty, Color.Black * 0.3f);
            frame.Padding = new Vector4(200.0f, 100.0f, 200.0f, 100.0f);

            int x = 0, y = 0;
            foreach (Character character in crewManager.characters)
            {
                GUIButton characterButton = new GUIButton(new Rectangle(x,y, 150, 40), "", Color.Transparent, null, frame);
                characterButton.UserData = character;
                characterButton.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);


                if (character == Character.Controlled)
                {
                    characterButton.CanBeSelected = false;
                    characterButton.Color = Color.LightGray * 0.3f;
                }
                else
                {
                    characterButton.HoverColor = Color.LightGray * 0.5f;
                    characterButton.SelectedColor = Color.Gold * 0.5f;
                }

                string name = character.Info.Name.Replace(' ', '\n');

                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(40, 0, 0, 25),
                    name,
                    Color.Transparent, Color.White,
                    Alignment.Left, Alignment.Left,
                    null, characterButton, false);
                textBlock.Font = GUI.SmallFont;
                textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

                new GUIImage(new Rectangle(-10, -5, 0, 0), character.AnimController.Limbs[0].sprite, Alignment.Left, characterButton);

                x += 160;               
            }

            x = 0;
            y = 150;
            foreach (Order command in Order.List)
            {
                var button = new GUIButton(new Rectangle(x, y, 150, 20), command.Name, GUI.Style, frame);
                button.UserData = command;
                button.OnClicked = SetOrder;

                x += button.Rect.Width + 10;
            }
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

                var character = child.UserData as Character;
                if (character == null) continue;

                var humanAi = character.AIController as HumanAIController;
                if (humanAi == null) continue;

                var existingOrder = characterButton.children.Find(c => c.UserData as Order != null);
                if (existingOrder != null) characterButton.RemoveChild(existingOrder);

                var orderFrame = new GUIFrame(new Rectangle(0, characterButton.Rect.Height, 0, 30 + order.Options.Length*15), null, characterButton);
                orderFrame.OutlineColor = Color.LightGray * 0.8f;
                orderFrame.Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f);
                orderFrame.UserData = order;
                new GUITextBlock(new Rectangle(0,0,0,20), order.DoingText, GUI.Style, Alignment.TopLeft, Alignment.TopCenter, orderFrame);

                var optionList = new GUIListBox(new Rectangle(0,20,0,80), Color.Transparent, null, orderFrame);
                optionList.UserData = order;
                optionList.OnSelected = SelectOrderOption;
                foreach (string option in order.Options)
                {
                    var optionBox = new GUITextBlock(new Rectangle(0,0,0,15), option, GUI.Style, optionList);
                    optionBox.Font = GUI.SmallFont;
                    optionBox.UserData = option;
                }

                humanAi.SetOrder(order, "");
            }

            //characterList.Deselect();

            return true;
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
