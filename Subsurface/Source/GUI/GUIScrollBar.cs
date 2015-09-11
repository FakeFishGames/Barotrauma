using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Subsurface
{
    public class GUIScrollBar : GUIComponent
    {
        public static GUIScrollBar draggingBar;

        private bool isHorizontal;

        private GUIFrame frame;
        private GUIButton bar;
        private float barSize;
        private float barScroll;

        private bool enabled;

        public delegate bool OnMovedHandler(object obj);
        public OnMovedHandler OnMoved;

        public bool IsHorizontal
        {
            get { return isHorizontal; }
        }

        public bool Enabled
        {
            get { return enabled; }
            set { enabled = value; }
        }

        public float BarScroll
        {
            get { return barScroll; }
            set
            {
                barScroll = MathHelper.Clamp(value, 0.0f, 1.0f);
                int newX = bar.Rect.X - frame.Rect.X, newY = bar.Rect.Y - frame.Rect.Y;
                if (isHorizontal)
                {
                    newX = (int)(barScroll *(frame.Rect.Width - bar.Rect.Width));
                    newX = Math.Max(newX, 0);
                    newX = Math.Min(newX, frame.Rect.Width - bar.Rect.Width);

                }
                else
                {
                    newY = (int)(barScroll * (frame.Rect.Height- bar.Rect.Height));
                    newY = Math.Max(newY, 0);
                    newY = Math.Min(newY, frame.Rect.Height - bar.Rect.Height);

                }
                bar.Rect = new Rectangle(newX + frame.Rect.X, newY + frame.Rect.Y, bar.Rect.Width, bar.Rect.Height);
            }
        }

        public float BarSize
        {
            get { return barSize; }
            set 
            {
                float oldBarSize = barSize;
                barSize = Math.Min(Math.Max(value, 0.0f), 1.0f);
                if (barSize != oldBarSize) UpdateRect();
            }
        }

        public GUIScrollBar(Rectangle rect, GUIStyle style, float barSize, GUIComponent parent = null)
            : this(rect, null, barSize, style, parent)
        {
        }

        public GUIScrollBar(Rectangle rect, Color? color, float barSize, GUIStyle style = null, GUIComponent parent = null)
            : this(rect, color, barSize, Alignment.TopLeft, style, parent)
        {
        }


        public GUIScrollBar(Rectangle rect, Color? color, float barSize, Alignment alignment, GUIStyle style = null, GUIComponent parent = null)
            : base(style)
        {
            this.rect = rect;
            //GetDimensions(parent);

            this.alignment = alignment;

            if (parent != null)
                parent.AddChild(this);

            isHorizontal = (rect.Width > rect.Height);
            frame = new GUIFrame(new Rectangle(0,0,0,0), Color.White, style, this);
            //AddChild(frame);

            //System.Diagnostics.Debug.WriteLine(frame.rect);

            bar = new GUIButton(new Rectangle(0, 0, 0, 0), "", color, style, this);
            
            bar.OnPressed = SelectBar;
            //AddChild(bar);

            enabled = true;

            UpdateRect();
        }

        private void UpdateRect()
        {
            
            bar.Rect = new Rectangle(
                bar.Rect.X,
                bar.Rect.Y,
                isHorizontal ? (int)(frame.Rect.Width * barSize) : frame.Rect.Width,
                isHorizontal ? frame.Rect.Height : (int)(frame.Rect.Height * barSize));

            foreach (GUIComponent child in bar.children)
            {
                child.Rect = bar.Rect;
            }
        }

        public override void Update(float deltaTime)
        {
            if (!Visible) return;

            base.Update(deltaTime);

            if (draggingBar != this) return;
            if (!PlayerInput.LeftButtonDown()) draggingBar = null;

            MoveButton();            
        }

        public override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;

            DrawChildren(spriteBatch);
        }

        private bool SelectBar()
        {
            if (!enabled) return false;
            if (barSize == 1.0f) return false;

            draggingBar = this;

            return true;

        }

        private void MoveButton()
        {
            //if (!enabled) return false;
            //if (barSize == 1.0f) return false;

            int newX = bar.Rect.X - frame.Rect.X, newY = bar.Rect.Y - frame.Rect.Y;
            int moveAmount;
            if (isHorizontal)
            {
                moveAmount = (int)PlayerInput.MouseSpeed.X;
                newX = Math.Min(Math.Max(newX + moveAmount, 0), frame.Rect.Width - bar.Rect.Width);

                barScroll = (float)newX / ((float)frame.Rect.Width - (float)bar.Rect.Width);
            }
            else
            {
                moveAmount = (int)PlayerInput.MouseSpeed.Y;
                newY = Math.Min(Math.Max(newY+moveAmount, 0), frame.Rect.Height - bar.Rect.Height);

                barScroll = (float)newY / ((float)frame.Rect.Height - (float)bar.Rect.Height);
            }

            if (moveAmount != 0 && OnMoved != null) OnMoved(moveAmount);

            bar.Rect = new Rectangle(newX + frame.Rect.X, newY + frame.Rect.Y, bar.Rect.Width, bar.Rect.Height);

        }

    }
}
