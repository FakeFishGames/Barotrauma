using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    public class GUIScrollBar : GUIComponent
    {
        public static GUIScrollBar draggingBar;

        private bool isHorizontal;

        private GUIFrame frame;
        private GUIButton bar;
        private float barSize;
        private float barScroll;

        private float step;

        private bool enabled;

        public delegate bool OnMovedHandler(GUIScrollBar scrollBar, float barScroll);
        public OnMovedHandler OnMoved;

        public bool IsHorizontal
        {
            get { return isHorizontal; }
            set 
            { 
                if (isHorizontal == value) return;
                isHorizontal = value;
                UpdateRect();
            }
        }

        public bool Enabled
        {
            get { return enabled; }
            set { enabled = value; }
        }

        public float BarScroll
        {
            get { return step == 0.0f ? barScroll : MathUtils.RoundTowardsClosest(barScroll, step); }
            set
            {
                barScroll = MathHelper.Clamp(value, 0.0f, 1.0f);
                int newX = bar.Rect.X - frame.Rect.X, newY = bar.Rect.Y - frame.Rect.Y;

                float newScroll = step == 0.0f ? barScroll : MathUtils.RoundTowardsClosest(barScroll, step);

                if (isHorizontal)
                {
                    newX = (int)(newScroll * (frame.Rect.Width - bar.Rect.Width));
                    newX = MathHelper.Clamp(newX, 0, frame.Rect.Width - bar.Rect.Width);

                }
                else
                {
                    newY = (int)(newScroll * (frame.Rect.Height - bar.Rect.Height));
                    newY = MathHelper.Clamp(newY, 0, frame.Rect.Height - bar.Rect.Height);

                }
                bar.Rect = new Rectangle(newX + frame.Rect.X, newY + frame.Rect.Y, bar.Rect.Width, bar.Rect.Height);
            }
        }

        public float Step
        {
            get
            {
                return step;
            }
            set
            {
                step = MathHelper.Clamp(value, 0.0f, 1.0f);
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
            frame = new GUIFrame(new Rectangle(0,0,0,0), Color.Black*0.8f, style, this);
            //AddChild(frame);

            //System.Diagnostics.Debug.WriteLine(frame.rect);

            this.barSize = barSize;

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
            if (!PlayerInput.LeftButtonHeld()) draggingBar = null;

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
            float moveAmount;
            if (isHorizontal)
            {
                moveAmount = PlayerInput.MouseSpeed.X;
                barScroll += moveAmount / (frame.Rect.Width - bar.Rect.Width);
            }
            else
            {
                moveAmount = PlayerInput.MouseSpeed.Y;
                barScroll += moveAmount / (frame.Rect.Height - bar.Rect.Height);
            }

            BarScroll = barScroll;

            if (moveAmount != 0 && OnMoved != null) OnMoved(this, BarScroll);


            //bar.Rect = new Rectangle(newX + frame.Rect.X, newY + frame.Rect.Y, bar.Rect.Width, bar.Rect.Height);

        }

    }
}
