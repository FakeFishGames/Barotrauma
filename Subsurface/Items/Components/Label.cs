using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Subsurface.Items.Components
{
    class Label : ItemComponent
    {
        GUITextBox textBox;

        private string text;

        [HasDefaultValue("", true)]
        public string Text
        {
            get { return text; }
            set
            {
                text = value;
            }
        }



        public Label(Item item, XElement element)
            : base(item, element)
        {

        }

        public override bool Select(Character character)
        {
            if (textBox == null)
            {
                textBox = new GUITextBox(Rectangle.Empty, GUI.style, GuiFrame);
                textBox.Wrap = true;
                textBox.OnTextChanged = TextChanged;
                textBox.LimitText = true;

                GUIButton button = new GUIButton(new Rectangle(0,0,100,15), "OK", null, Alignment.BottomRight, GUI.style, GuiFrame);
                button.OnClicked = Close;
            }

            textBox.Text = text;

            textBox.Select();
            
            return base.Select(character);
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            //isActive = true;
            GuiFrame.Update((float)Physics.step);
            GuiFrame.Draw(spriteBatch);

            //int width = 300, height = 300;
            //int x = Game1.GraphicsWidth / 2 - width / 2;
            //int y = Game1.GraphicsHeight / 2 - height / 2 - 50;

            //GUI.DrawRectangle(spriteBatch, new Rectangle(x, y, width, height), Color.Black, true);
            if (!textBox.Selected) character.SelectedConstruction = null;
        }

        private bool TextChanged(GUITextBox textBox, string text)
        {
            this.text = text;
            item.NewComponentEvent(this, true);

            return true;
        }

        private bool Close(GUIButton button, object obj)
        {
            textBox.Deselect();

            return true;
        }

        public override void FillNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetOutgoingMessage message)
        {
            message.Write(Text);
        }

        public override void ReadNetworkData(Networking.NetworkEventType type, Lidgren.Network.NetIncomingMessage message)
        {
            string newText = "";
            try
            {
                newText = message.ReadString();
            }

            catch
            {
                return;
            }

            Text = newText;
        }
    }
}
