using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Subsurface
{
    class GUIMessageBox : GUIFrame
    {
        public static Queue<GUIMessageBox> messageBoxes = new Queue<GUIMessageBox>();

        const int DefaultWidth=400, DefaultHeight=200;

        //public delegate bool OnClickedHandler(GUIButton button, object obj);
        //public OnClickedHandler OnClicked;

        //GUIFrame frame;
        public GUIButton[] Buttons;

        public GUIMessageBox(string header, string text)
            : this(header, text, new string[] {"OK"})
        {
            this.Buttons[0].OnClicked = Close;
        }

        public GUIMessageBox(string header, string text, string[] buttons, Alignment textAlignment = (Alignment.Left | Alignment.Top))
            : base(new Rectangle(Game1.GraphicsWidth / 2 - DefaultWidth / 2, Game1.GraphicsHeight / 2 - DefaultHeight / 2, DefaultWidth, DefaultHeight),
                GUI.style.backGroundColor, Alignment.CenterX, GUI.style)
        {
            Padding = GUI.style.smallPadding;

            if (buttons == null || buttons.Length == 0)
            {
                DebugConsole.ThrowError("Creating a message box with no buttons isn't allowed");
                return;
            }

            new GUITextBlock(new Rectangle(0, 0, 0, 30), header, Color.Transparent, Color.White, textAlignment, this, true);
            new GUITextBlock(new Rectangle(0, 30, 0, DefaultHeight - 70), text, Color.Transparent, Color.White, textAlignment, this, true);

            int x = 0;
            this.Buttons = new GUIButton[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
            {
                this.Buttons[i] = new GUIButton(new Rectangle(x, 0, 150, 30), buttons[i], GUI.style, Alignment.Left | Alignment.Bottom, this);

                x += this.Buttons[i].Rect.Width + 20;
            }

            messageBoxes.Enqueue(this);
        }

        public bool Close(GUIButton button, object obj)
        {
            messageBoxes.Dequeue();
            return true;
        }
    }
}
