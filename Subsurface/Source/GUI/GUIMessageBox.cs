using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma
{
    public class GUIMessageBox : GUIFrame
    {
        public static Queue<GUIComponent> MessageBoxes = new Queue<GUIComponent>();

        const int DefaultWidth=400, DefaultHeight=250;

        //public delegate bool OnClickedHandler(GUIButton button, object obj);
        //public OnClickedHandler OnClicked;

        //GUIFrame frame;
        public GUIButton[] Buttons;

        public static GUIComponent VisibleBox
        {
            get { return MessageBoxes.Count==0 ? null : MessageBoxes.Peek(); }
        }

        public string Text
        {
            get { return (children[0].children[1] as GUITextBlock).Text; }
            set { (children[0].children[1] as GUITextBlock).Text = value; }
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
        
        public GUIMessageBox(string header, string text, string[] buttons, int width=DefaultWidth, int height=DefaultHeight, Alignment textAlignment = Alignment.TopLeft, GUIComponent parent = null)
            : base(new Rectangle(0,0, GameMain.GraphicsWidth, GameMain.GraphicsHeight),
                Color.Black*0.5f, Alignment.TopLeft, null, parent)
        {
            var frame = new GUIFrame(new Rectangle(0,0,width,height), null, Alignment.Center, GUI.Style, this);

            new GUITextBlock(new Rectangle(0, 0, 0, 30), header, Color.Transparent, Color.White, textAlignment, GUI.Style, frame, true);
            if (!string.IsNullOrWhiteSpace(text))
            {
                new GUITextBlock(new Rectangle(0, 30, 0, height - 70), text, 
                    Color.Transparent, Color.White, textAlignment, GUI.Style, frame, true);
            }

            int x = 0;
            this.Buttons = new GUIButton[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
            {
                this.Buttons[i] = new GUIButton(new Rectangle(x, 0, 150, 30), buttons[i], Alignment.Left | Alignment.Bottom, GUI.Style, frame);

                x += this.Buttons[i].Rect.Width + 20;
            }

            MessageBoxes.Enqueue(this);
        }



        public bool Close(GUIButton button, object obj)
        {
            if (parent != null) parent.RemoveChild(this);
            if (MessageBoxes.Contains(this)) MessageBoxes.Dequeue();

            return true;
        }

        public static void CloseAll()
        {
            MessageBoxes.Clear();
        }
    }
}
