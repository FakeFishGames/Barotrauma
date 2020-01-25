using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma
{
    public class GUIScrollBar : GUIComponent
    {
        public static GUIScrollBar DraggingBar
        {
            get; private set;
        }

        private bool isHorizontal;

        public GUIFrame Frame { get; private set; }
        public GUIButton Bar { get; private set; }
        private float barSize;
        private float barScroll;

        private float step;

        private Vector2? dragStartPos;

        public delegate bool OnMovedHandler(GUIScrollBar scrollBar, float barScroll);
        public OnMovedHandler OnMoved;
        public OnMovedHandler OnReleased;

        public bool IsBooleanSwitch;

        public override string ToolTip
        {
            get { return base.ToolTip; }
            set
            {
                base.ToolTip = value;
                Frame.ToolTip = value;
                Bar.ToolTip = value;
            }
        }

        private float minValue;
        public float MinValue
        {
            get { return minValue; }
            set
            {
                minValue = MathHelper.Clamp(value, 0.0f, 1.0f);
                BarScroll = Math.Max(minValue, barScroll);
            }
        }

        private float maxValue = 1.0f;
        public float MaxValue
        {
            get { return maxValue; }
            set
            {
                maxValue = MathHelper.Clamp(value, 0.0f, 1.0f);
                BarScroll = Math.Min(maxValue, barScroll);
            }
        }

        public bool IsHorizontal
        {
            get { return isHorizontal; }
            /*set 
            { 
                if (isHorizontal == value) return;
                isHorizontal = value;
                UpdateRect();
            }*/
        }

        public override bool Enabled
        {
            get { return enabled; }
            set
            {
                enabled = value;
                Bar.Enabled = value;
                if (!enabled) Bar.Selected = false;
            }
        }

        public Vector4 Padding
        {
            get
            {
                if (Frame?.Style == null) return Vector4.Zero;
                return Frame.Style.Padding;
            }
        }

        private Vector2 range;
        public Vector2 Range
        {
            get
            {
                return range;
            }
            set
            {
                float oldBarScrollValue = BarScrollValue;
                range = value;
                BarScrollValue = oldBarScrollValue;
            }
        }

        public delegate float ScrollConversion(GUIScrollBar scrollBar, float f);
        public ScrollConversion ScrollToValue = null;
        public ScrollConversion ValueToScroll = null;

        public float BarScrollValue
        {
            get
            {
                if (ScrollToValue == null) return (BarScroll * (Range.Y - Range.X)) + Range.X;
                return ScrollToValue(this, BarScroll);
            }
            set
            {
                if (ValueToScroll == null) BarScroll = (value - Range.X) / (Range.Y - Range.X);
                else BarScroll = ValueToScroll(this, value);
            }
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
                int newX = Bar.RectTransform.AbsoluteOffset.X;
                int newY = Bar.RectTransform.AbsoluteOffset.Y;
                float newScroll = step == 0.0f ? barScroll : MathUtils.RoundTowardsClosest(barScroll, step);
                if (isHorizontal)
                {
                    newX = (int)(Padding.X + newScroll * (Frame.Rect.Width - Bar.Rect.Width - Padding.X - Padding.Z));
                    newX = MathHelper.Clamp(newX, (int)Padding.X, Frame.Rect.Width - Bar.Rect.Width - (int)Padding.Z);
                }
                else
                {
                    newY = (int)(Padding.Y + newScroll * (Frame.Rect.Height - Bar.Rect.Height - Padding.Y - Padding.W));
                    newY = MathHelper.Clamp(newY, (int)Padding.Y, Frame.Rect.Height - Bar.Rect.Height - (int)Padding.W);
                }

                Bar.RectTransform.AbsoluteOffset = new Point(newX, newY);
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

        public float StepValue
        {
            get
            {
                return step * (Range.Y - Range.X);
            }
            set
            {
                Step = value / (Range.Y - Range.X);
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
        
        public GUIScrollBar(RectTransform rectT, float barSize = 1, Color? color = null, string style = "", bool? isHorizontal = null) : base(style, rectT)
        {
            CanBeFocused = true;
            this.isHorizontal = isHorizontal ?? (Rect.Width > Rect.Height);
            Frame = new GUIFrame(new RectTransform(Vector2.One, rectT));
            GUI.Style.Apply(Frame, IsHorizontal ? "GUIFrameHorizontal" : "GUIFrameVertical", this);
            this.barSize = barSize;
            Bar = new GUIButton(new RectTransform(Vector2.One, rectT, IsHorizontal ? Anchor.CenterLeft : Anchor.TopCenter), color: color);

            switch (style)
            {
                case "":
                    HoverCursor = CursorState.Default;
                    Bar.HoverCursor = CursorState.Default;
                    break;
                case "GUISlider":
                    HoverCursor = CursorState.Default;
                    Bar.HoverCursor = CursorState.Hand;
                    break;
                default:
                    HoverCursor = CursorState.Hand;
                    Bar.HoverCursor = CursorState.Hand;
                    break;
            }

            GUI.Style.Apply(Bar, IsHorizontal ? "GUIButtonHorizontal" : "GUIButtonVertical", this);
            Bar.OnPressed = SelectBar;
            enabled = true;
            UpdateRect();
            BarScroll = 0.0f;

            rectT.SizeChanged += UpdateRect;
            rectT.ScaleChanged += UpdateRect;
            Bar.RectTransform.SizeChanged += () => { BarScroll = barScroll; };
        }

        private void UpdateRect()
        {
            Vector4 padding = Frame.Style.Padding;
            var newSize = new Point((int)(Rect.Size.X - padding.X - padding.Z), (int)(Rect.Size.Y - padding.Y - padding.W));
            newSize = IsHorizontal ? newSize.Multiply(new Vector2(BarSize, 1)) : newSize.Multiply(new Vector2(1, BarSize));
            Bar.RectTransform.Resize(newSize);
            BarScroll = barScroll;
        }
        
        protected override void Update(float deltaTime)
        {
            if (!Visible) { return; }

            if (!enabled) { return; }
            
            Frame.State = GUI.MouseOn == Frame ? ComponentState.Hover : ComponentState.None;
            if (Frame.State == ComponentState.Hover && PlayerInput.PrimaryMouseButtonHeld())
            {
                Frame.State = ComponentState.Pressed;
            }            

            if (IsBooleanSwitch && 
                (!PlayerInput.PrimaryMouseButtonHeld() || (GUI.MouseOn != this && !IsParentOf(GUI.MouseOn))))
            {
                int dir = Math.Sign(barScroll - (minValue + maxValue) / 2.0f);
                if (dir == 0) dir = 1;
                if ((barScroll <= maxValue && dir > 0) || 
                    (barScroll > minValue && dir < 0))
                {
                    BarScroll += dir * 0.1f;
                }
            }
            
            if (DraggingBar == this)
            {
                GUI.ForceMouseOn(this);
                if (dragStartPos == null) { dragStartPos = PlayerInput.MousePosition; }

                if (!PlayerInput.PrimaryMouseButtonHeld())
                {
                    if (IsBooleanSwitch && GUI.MouseOn == Bar && Vector2.Distance(dragStartPos.Value, PlayerInput.MousePosition) < 5)
                    {
                        BarScroll = BarScroll > 0.5f ? 0.0f : 1.0f;
                        OnMoved?.Invoke(this, BarScroll);
                    }
                    OnReleased?.Invoke(this, BarScroll);
                    DraggingBar = null;
                    dragStartPos = null;

                }
                if ((isHorizontal && PlayerInput.MousePosition.X > Rect.X && PlayerInput.MousePosition.X < Rect.Right) ||
                    (!isHorizontal && PlayerInput.MousePosition.Y > Rect.Y && PlayerInput.MousePosition.Y < Rect.Bottom))
                {
                    MoveButton(PlayerInput.MouseSpeed);
                }
            }
            else if (GUI.MouseOn == Frame)
            {
                if (PlayerInput.PrimaryMouseButtonClicked())
                {
                    DraggingBar?.OnReleased?.Invoke(DraggingBar, DraggingBar.BarScroll);
                    if (IsBooleanSwitch)
                    {
                        MoveButton(new Vector2(
                            Math.Sign(PlayerInput.MousePosition.X - Bar.Rect.Center.X) * Rect.Width,
                            Math.Sign(PlayerInput.MousePosition.Y - Bar.Rect.Center.Y) * Rect.Height));                        
                    }
                    else
                    {
                        MoveButton(new Vector2(
                            Math.Sign(PlayerInput.MousePosition.X - Bar.Rect.Center.X) * Bar.Rect.Width,
                            Math.Sign(PlayerInput.MousePosition.Y - Bar.Rect.Center.Y) * Bar.Rect.Height));
                    }
                }
            }       
        }

        private bool SelectBar()
        {
            if (!enabled || !PlayerInput.PrimaryMouseButtonDown()) { return false; }
            if (barSize >= 1.0f) { return false; }

            DraggingBar = this;

            return true;
        }

        public void MoveButton(Vector2 moveAmount)
        {
            float newScroll = barScroll;
            if (isHorizontal)
            {
                moveAmount.Y = 0.0f;
                newScroll += moveAmount.X / (Frame.Rect.Width - Bar.Rect.Width - Padding.X - Padding.Z);
            }
            else
            {
                moveAmount.X = 0.0f;
                newScroll += moveAmount.Y / (Frame.Rect.Height - Bar.Rect.Height - Padding.Y - Padding.W);
            }

            BarScroll = newScroll;

            if (moveAmount != Vector2.Zero && OnMoved != null) { OnMoved(this, BarScroll); }
        }
    }
}
