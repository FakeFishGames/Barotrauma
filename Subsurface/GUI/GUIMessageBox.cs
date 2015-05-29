using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Subsurface
{
    class GUIMessageBox
    {
        public static Queue<GUIMessageBox> messageBoxes = new Queue<GUIMessageBox>();

        const int DefaultWidth=400, DefaultHeight=200;

        public delegate bool OnClickedHandler(GUIButton button, object obj);
        public OnClickedHandler OnClicked;

        GUIFrame frame;
        GUIButton[] buttons;

        public GUIMessageBox(string header, string text)
            : this(header, text, new string[] {"OK"})
        { }

        public GUIMessageBox(string header, string text, string[] buttons, Alignment textAlignment = (Alignment.Left |Alignment.Top))
        {
            frame = new GUIFrame(new Rectangle(Game1.GraphicsWidth/2-DefaultWidth/2, Game1.GraphicsHeight/2-DefaultHeight/2, DefaultWidth, DefaultHeight), 
                GUI.style.backGroundColor, Alignment.CenterX, GUI.style);
            frame.Padding = GUI.style.smallPadding;

            if (buttons == null || buttons.Length == 0)
            {
                DebugConsole.ThrowError("Creating a message box with no buttons isn't allowed");
            }

            new GUITextBlock(new Rectangle(0, 5, 0, 30), header, Color.Transparent, Color.White, textAlignment, frame, true);
            new GUITextBlock(new Rectangle(0,50,0,DefaultHeight-70),text, Color.Transparent, Color.White, textAlignment, frame, true);

            int x = 0;
            this.buttons = new GUIButton[buttons.Length];
            for (int i=0; i<buttons.Length; i++)
            {
                this.buttons[i] = new GUIButton(new Rectangle(x,0,150,30), buttons[i], GUI.style, Alignment.Left, frame);
                this.buttons[i].OnClicked = ButtonClicked;
                x += this.buttons[i].Rect.Width + 20;
            }
        }

        private bool ButtonClicked(GUIButton button, object obj)
        {
            if (OnClicked==null) return true;

            return OnClicked(button, obj);

        }
    }
}
