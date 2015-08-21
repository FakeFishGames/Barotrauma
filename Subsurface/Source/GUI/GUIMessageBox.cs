using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Subsurface
{
    class GUIMessageBox : GUIFrame
    {
        public static Queue<GUIMessageBox> MessageBoxes = new Queue<GUIMessageBox>();

        const int DefaultWidth=400, DefaultHeight=200;

        //public delegate bool OnClickedHandler(GUIButton button, object obj);
        //public OnClickedHandler OnClicked;

        //GUIFrame frame;
        public GUIButton[] Buttons;

        public string Text
        {
            get { return (children[1] as GUITextBlock).Text; }
            set { (children[1] as GUITextBlock).Text = value; }
        }

        public GUIMessageBox(string header, string text)
            : this(header, text, new string[] {"OK"})
        {
            this.Buttons[0].OnClicked = Close;
        }

        public GUIMessageBox(string header, string text, int width, int height)
            : this(header, text, new string[] { "OK" }, width, height)
        {
            this.Buttons[0].OnClicked = Close;
        }
        
        public GUIMessageBox(string header, string text, string[] buttons, int width=DefaultWidth, int height=DefaultHeight, Alignment textAlignment = Alignment.TopLeft)
            : base(new Rectangle(0,0, width, height),
                null, Alignment.Center, GUI.style, null)
        {
            //Padding = GUI.style.smallPadding;

            //if (buttons == null || buttons.Length == 0)
            //{
            //    DebugConsole.ThrowError("Creating a message box with no buttons isn't allowed");
            //    return;
            //}

            new GUITextBlock(new Rectangle(0, 0, 0, 30), header, Color.Transparent, Color.White, textAlignment, GUI.style, this, true);
            new GUITextBlock(new Rectangle(0, 30, 0, height - 70), text, Color.Transparent, Color.White, textAlignment, GUI.style, this, true);

            int x = 0;
            this.Buttons = new GUIButton[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
            {
                this.Buttons[i] = new GUIButton(new Rectangle(x, 0, 150, 30), buttons[i], Alignment.Left | Alignment.Bottom, GUI.style, this);

                x += this.Buttons[i].Rect.Width + 20;
            }

            MessageBoxes.Enqueue(this);
        }

        public bool Close(GUIButton button, object obj)
        {
            if (parent != null) parent.RemoveChild(this);

            MessageBoxes.Dequeue();
            return true;
        }
    }
}
