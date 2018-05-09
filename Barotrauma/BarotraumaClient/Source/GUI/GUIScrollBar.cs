using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

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

        /// <summary>
        /// Scroll bar can be positioned outside of the parent. Clamping to parent, effectively makes the scroll bar non-interactive.
        /// </summary>
        public override bool ClampMouseRectToParent { get; set; } = false;

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
            set
            {
                enabled = value;
                bar.Enabled = value;
            }
        }

        public float BarScroll
        {
            get { return step == 0.0f ? barScroll : MathUtils.RoundTowardsClosest(barScroll, step); }
            set
            {
                barScroll = MathHelper.Clamp(value, 0.0f, 1.0f);
                int newX = bar.Rect.X - frame.Rect.X;
                int newY = bar.Rect.Y - frame.Rect.Y;
                float newScroll = step == 0.0f ? barScroll : MathUtils.RoundTowardsClosest(barScroll, step);
                if (isHorizontal)
                {
                    newX = (int)(frame.Padding.X + newScroll * (frame.Rect.Width - bar.Rect.Width - frame.Padding.X - frame.Padding.Z));
                    newX = MathHelper.Clamp(newX, (int)frame.Padding.X, frame.Rect.Width - bar.Rect.Width - (int)frame.Padding.Z);
                }
                else
                {
                    newY = (int)(frame.Padding.Y + newScroll * (frame.Rect.Height - bar.Rect.Height - frame.Padding.Y - frame.Padding.W));
                    newY = MathHelper.Clamp(newY, (int)frame.Padding.Y, frame.Rect.Height - bar.Rect.Height - (int)frame.Padding.W);
                }
                if (RectTransform != null)
                {
                    bar.RectTransform.AbsoluteOffset = new Point(newX, newY);
                }
                else
                {

                    bar.Rect = new Rectangle(newX + frame.Rect.X, newY + frame.Rect.Y, bar.Rect.Width, bar.Rect.Height);
                }
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
                barSize = Math.Min(Math.Max(value, 0.0f), 1.0f);
                UpdateRect();
            }
        }

        public GUIScrollBar(Rectangle rect, string style, float barSize, GUIComponent parent = null)
            : this(rect, null, barSize, style, parent)
        {
        }

        public GUIScrollBar(Rectangle rect, Color? color, float barSize, string style = "", GUIComponent parent = null)
            : this(rect, color, barSize, Alignment.TopLeft, style, parent)
        {
        }


        public GUIScrollBar(Rectangle rect, Color? color, float barSize, Alignment alignment, string style = "", GUIComponent parent = null)
            : base(style)
        {
            this.rect = rect;
            //GetDimensions(parent);

            this.alignment = alignment;

            if (parent != null)
                parent.AddChild(this);

            isHorizontal = (rect.Width > rect.Height);
            frame = new GUIFrame(new Rectangle(0,0,0,0), style, this);
            GUI.Style.Apply(frame, isHorizontal ? "GUIFrameHorizontal" : "GUIFrameVertical", this);

            this.barSize = barSize;

            bar = new GUIButton(new Rectangle(0, 0, 0, 0), "", color, "", this);
            GUI.Style.Apply(bar, isHorizontal ? "GUIButtonHorizontal" : "GUIButtoneVertical", this);
            
            bar.OnPressed = SelectBar;

            enabled = true;

            UpdateRect();
        }

        /// <summary>
        /// This is the new constructor.
        /// </summary>
        public GUIScrollBar(RectTransform rectT, float barSize = 1, Color? color = null, string style = "") : base(style, rectT)
        {
            isHorizontal = (Rect.Width > Rect.Height);
            frame = new GUIFrame(new RectTransform(Vector2.One, rectT));
            GUI.Style.Apply(frame, isHorizontal ? "GUIFrameHorizontal" : "GUIFrameVertical", this);
            this.barSize = barSize;
            bar = new GUIButton(new RectTransform(Vector2.One, rectT), color: color);
            GUI.Style.Apply(bar, isHorizontal ? "GUIButtonHorizontal" : "GUIButtoneVertical", this);
            bar.OnPressed = SelectBar;
            enabled = true;
            UpdateRect();
        }

        private void UpdateRect()
        {
            if (RectTransform != null)
            {
                var newSize = isHorizontal ? new Vector2(barSize, 1) : new Vector2(1, barSize);
                bar.RectTransform.Resize(newSize);
            }
            else
            {
                float width = frame.Rect.Width - frame.Padding.X - frame.Padding.Z;
                float height = frame.Rect.Height - frame.Padding.Y - frame.Padding.W;

                bar.Rect = new Rectangle(
                    bar.Rect.X,
                    bar.Rect.Y,
                    isHorizontal ? (int)(width * barSize) : (int)width,
                    isHorizontal ? (int)height : (int)(height * barSize));

                ClampRect();

                foreach (GUIComponent child in bar.Children)
                {
                    child.Rect = bar.Rect;
                }
            }
        }

        private void ClampRect()
        {
            // TODO: doesn't work for RectTransforms. Do we need this?
            bar.Rect = new Rectangle(
                (int)MathHelper.Clamp(bar.Rect.X, frame.Rect.X + frame.Padding.X, frame.Rect.Right - bar.Rect.Width - frame.Padding.X - frame.Padding.Z),
                (int)MathHelper.Clamp(bar.Rect.Y, frame.Rect.Y + frame.Padding.Y, frame.Rect.Bottom - bar.Rect.Height - frame.Padding.Y - frame.Padding.W), 
                bar.Rect.Width, 
                bar.Rect.Height);
        }

        public override void Update(float deltaTime)
        {
            if (!Visible) return;

            base.Update(deltaTime);

            if (!enabled) return;

            if (GUI.MouseOn == frame)
            {
                if (PlayerInput.LeftButtonClicked())
                {
                    MoveButton(new Vector2(
                        Math.Sign(PlayerInput.MousePosition.X - bar.Rect.Center.X) * bar.Rect.Width,
                        Math.Sign(PlayerInput.MousePosition.Y - bar.Rect.Center.Y) * bar.Rect.Height));
                }
            }

            if (draggingBar == this)
            {
                if (!PlayerInput.LeftButtonHeld()) draggingBar = null;
                MoveButton(PlayerInput.MouseSpeed);
            }       
        }

        private bool SelectBar()
        {
            if (!enabled) return false;
            // This doesn't work
            if (barSize == 1.0f) return false;

            draggingBar = this;

            return true;

        }

        private void MoveButton(Vector2 moveAmount)
        {
            if (isHorizontal)
            {
                moveAmount.Y = 0.0f;
                barScroll += moveAmount.X / (frame.Rect.Width - bar.Rect.Width - frame.Padding.X - frame.Padding.Z);
            }
            else
            {
                moveAmount.X = 0.0f;
                barScroll += moveAmount.Y / (frame.Rect.Height - bar.Rect.Height - frame.Padding.Y - frame.Padding.W);
            }

            BarScroll = barScroll;

            if (moveAmount != Vector2.Zero && OnMoved != null) OnMoved(this, BarScroll);
        }
    }
}
