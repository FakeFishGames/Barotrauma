using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    public class GUIDragHandle : GUIComponent
    {
        private readonly RectTransform elementToMove;

        private Vector2 dragStart;
        private bool dragStarted;

        public Rectangle DragArea;

        public GUIDragHandle(RectTransform rectT, RectTransform elementToMove, string style = "GUIDragIndicator")
            : base(style, rectT)
        {
            this.elementToMove = elementToMove;
            DragArea = new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight);
        }

        protected override void Update(float deltaTime)
        {
            if (!Visible) return;
            base.Update(deltaTime);
            Enabled = true;
            if (dragStarted)
            {
                Point moveAmount = (PlayerInput.MousePosition - dragStart).ToPoint() - elementToMove.ScreenSpaceOffset;
                Rectangle rect = elementToMove.Rect;
                rect.Location += moveAmount;
                
                moveAmount.X += Math.Max(DragArea.X - rect.X, 0);
                moveAmount.X -= Math.Max(rect.Right - DragArea.Right, 0);
                moveAmount.Y += Math.Max(DragArea.Y - rect.Y, 0);
                moveAmount.Y -= Math.Max(rect.Bottom - DragArea.Bottom, 0);

                if (moveAmount != Point.Zero)
                {
                    elementToMove.ScreenSpaceOffset += moveAmount;
                }

                if (!PlayerInput.PrimaryMouseButtonHeld())
                {
                    dragStarted = false;
                }
            }
            else if (Rect.Contains(PlayerInput.MousePosition) && CanBeFocused && Enabled && GUI.IsMouseOn(this))
            {
                State = Selected ? ComponentState.HoverSelected : ComponentState.Hover;
                if (PlayerInput.PrimaryMouseButtonDown())
                {
                    dragStart = PlayerInput.MousePosition - elementToMove.ScreenSpaceOffset.ToVector2();
                    dragStarted = true;
                }
            }
            else
            {
                if (!ExternalHighlight)
                {
                    State = Selected ? ComponentState.Selected : ComponentState.None;
                }
                else
                {
                    State = ComponentState.Hover;
                }
            }

            foreach (GUIComponent child in Children)
            {
                child.State = State;
            }
        }
    }
}
