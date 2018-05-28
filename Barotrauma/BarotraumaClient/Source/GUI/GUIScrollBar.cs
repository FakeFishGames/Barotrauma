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
        
        public delegate bool OnMovedHandler(GUIScrollBar scrollBar, float barScroll);
        public OnMovedHandler OnMoved;

        /// <summary>
        /// Scroll bar can be positioned outside of the parent. Clamping to parent, effectively makes the scroll bar non-interactive.
        /// </summary>
        public override bool ClampMouseRectToParent { get; set; } = false;

        public bool IsBooleanSwitch;

        private float minValue;
        public float MinValue
        {
            get { return minValue; }
            set { minValue = MathHelper.Clamp(value, 0.0f, 1.0f); }
        }

        private float maxValue = 1.0f;
        public float MaxValue
        {
            get { return maxValue; }
            set { maxValue = MathHelper.Clamp(value, 0.0f, 1.0f); }
        }

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

        public override bool Enabled
        {
            get { return enabled; }
            set
            {
                enabled = value;
                bar.Enabled = value;
            }
        }

        private Vector4 padding;
        public Vector4 Padding
        {
            get { return padding; }
            set { padding = value; }
        }

        public float BarScroll
        {
            get { return step == 0.0f ? barScroll : MathUtils.RoundTowardsClosest(barScroll, step); }
            set
            {
                if (float.IsNaN(value))
                {
                    return;
                }

                barScroll = MathHelper.Clamp(value, minValue, maxValue);
                int newX = bar.Rect.X - frame.Rect.X;
                int newY = bar.Rect.Y - frame.Rect.Y;
                float newScroll = step == 0.0f ? barScroll : MathUtils.RoundTowardsClosest(barScroll, step);
                if (isHorizontal)
                {
                    newX = (int)(Padding.X + newScroll * (frame.Rect.Width - bar.Rect.Width - Padding.X - Padding.Z));
                    newX = MathHelper.Clamp(newX, (int)Padding.X, frame.Rect.Width - bar.Rect.Width - (int)Padding.Z);
                }
                else
                {
                    newY = (int)(Padding.Y + newScroll * (frame.Rect.Height - bar.Rect.Height - Padding.Y - Padding.W));
                    newY = MathHelper.Clamp(newY, (int)Padding.Y, frame.Rect.Height - bar.Rect.Height - (int)Padding.W);
                }

                bar.RectTransform.AbsoluteOffset = new Point(newX, newY);
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



        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIScrollBar(Rectangle rect, string style, float barSize, GUIComponent parent = null)
            : this(rect, null, barSize, style, parent)
        {
        }

        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIScrollBar(Rectangle rect, Color? color, float barSize, string style = "", GUIComponent parent = null)
            : this(rect, color, barSize, Alignment.TopLeft, style, parent)
        {
        }
        
        [System.Obsolete("Use RectTransform instead of Rectangle")]
        public GUIScrollBar(Rectangle rect, Color? color, float barSize, Alignment alignment, string style = "", GUIComponent parent = null)
            : base(style)
        {
            this.rect = rect;
            //GetDimensions(parent);

            this.alignment = alignment;

            if (parent != null)
                parent.AddChild(this);

            isHorizontal = (this.rect.Width > this.rect.Height);
            frame = new GUIFrame(new Rectangle(0,0,0,0), style, this);
            GUI.Style.Apply(frame, isHorizontal ? "GUIFrameHorizontal" : "GUIFrameVertical", this);

            this.barSize = barSize;

            bar = new GUIButton(new Rectangle(0, 0, 0, 0), "", color, "", this);
            GUI.Style.Apply(bar, isHorizontal ? "GUIButtonHorizontal" : "GUIButtonVertical", this);
            
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
            GUI.Style.Apply(bar, isHorizontal ? "GUIButtonHorizontal" : "GUIButtonVertical", this);
            bar.OnPressed = SelectBar;
            enabled = true;
            UpdateRect();
        }

        public override void ApplyStyle(GUIComponentStyle style)
        {
            base.ApplyStyle(style);
            padding = style.Padding;
        }

        private void UpdateRect()
        {
            var newSize = isHorizontal ? new Vector2(barSize, 1) : new Vector2(1, barSize);
            bar.RectTransform.Resize(newSize);
        }

        /*private void ClampRect()
        {
            // TODO: doesn't work for RectTransforms. Do we need this?
            bar.Rect = new Rectangle(
                (int)MathHelper.Clamp(bar.Rect.X, frame.Rect.X + frame.Padding.X, frame.Rect.Right - bar.Rect.Width - frame.Padding.X - frame.Padding.Z),
                (int)MathHelper.Clamp(bar.Rect.Y, frame.Rect.Y + frame.Padding.Y, frame.Rect.Bottom - bar.Rect.Height - frame.Padding.Y - frame.Padding.W), 
                bar.Rect.Width, 
                bar.Rect.Height);
        }*/

        protected override void Update(float deltaTime)
        {
            if (!Visible) return;

            base.Update(deltaTime);

            if (!enabled) return;

            if (IsBooleanSwitch && 
                (!PlayerInput.LeftButtonHeld() || (GUI.MouseOn != this && !IsParentOf(GUI.MouseOn))))
            {
                int dir = Math.Sign(barScroll - (minValue + maxValue) / 2.0f);
                if (dir == 0) dir = 1;
                if ((barScroll <= maxValue && dir > 0) || 
                    (barScroll > minValue && dir < 0))
                {
                    BarScroll += dir * 0.1f;
                }
            }

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

        public void MoveButton(Vector2 moveAmount)
        {
            float newScroll = barScroll;
            if (isHorizontal)
            {
                moveAmount.Y = 0.0f;
                newScroll += moveAmount.X / (frame.Rect.Width - bar.Rect.Width - Padding.X - Padding.Z);
            }
            else
            {
                moveAmount.X = 0.0f;
                newScroll += moveAmount.Y / (frame.Rect.Height - bar.Rect.Height - Padding.Y - Padding.W);
            }

            BarScroll = newScroll;

            if (moveAmount != Vector2.Zero && OnMoved != null) OnMoved(this, BarScroll);
        }
    }
}
