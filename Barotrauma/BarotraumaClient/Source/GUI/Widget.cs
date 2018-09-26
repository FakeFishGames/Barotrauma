using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    class Widget
    {
        public enum Shape
        {
            Rectangle,
            Circle
        }

        public Shape shape;
        public string tooltip;
        public Rectangle DrawRect => new Rectangle((int)DrawPos.X - size / 2, (int)DrawPos.Y - size / 2, size, size);
        public Rectangle InputRect
        {
            get
            {
                var inputRect = DrawRect;
                inputRect.Inflate(inputAreaMargin.X, inputAreaMargin.Y);
                return inputRect;
            }
        }

        public Vector2 DrawPos { get; set; }
        public int size = 10;
        /// <summary>
        /// Used only for circles.
        /// </summary>
        public int sides = 40;
        /// <summary>
        /// Currently used only for rectangles.
        /// </summary>
        public bool isFilled;
        public Point inputAreaMargin;
        public Color color = Color.Red;
        public Color textColor = Color.White;
        public Color textBackgroundColor = Color.Black * 0.5f;
        public readonly string id;

        public event Action Hovered;
        public event Action Clicked;
        public event Action MouseDown;
        public event Action MouseUp;
        public event Action MouseHeld;
        public event Action<SpriteBatch, float> PreDraw;
        public event Action<SpriteBatch, float> PostDraw;

        public Action refresh;

        public bool IsSelected => selectedWidget == this;
        public bool IsControlled => IsSelected && PlayerInput.LeftButtonHeld();
        public bool IsMouseOver => GUI.MouseOn == null && InputRect.Contains(PlayerInput.MousePosition);
        public Vector2? tooltipOffset;

        public Widget linkedWidget;

        public static Widget selectedWidget;

        public Widget(string id, int size, Shape shape)
        {
            this.id = id;
            this.size = size;
            this.shape = shape;
        }

        public virtual void Update(float deltaTime)
        {
            if (IsMouseOver)
            {
                if (selectedWidget == null)
                {
                    selectedWidget = this;
                }
                Hovered?.Invoke();
            }
            else if (selectedWidget == this)
            {
                selectedWidget = null;
            }
            if (IsSelected)
            {
                if (PlayerInput.LeftButtonHeld())
                {
                    MouseHeld?.Invoke();
                }
                if (PlayerInput.LeftButtonDown())
                {
                    MouseDown?.Invoke();
                }
                if (PlayerInput.LeftButtonReleased())
                {
                    MouseUp?.Invoke();
                }
                if (PlayerInput.LeftButtonClicked())
                {
                    Clicked?.Invoke();
                }
            }
        }

        public virtual void Draw(SpriteBatch spriteBatch, float deltaTime)
        {
            PreDraw?.Invoke(spriteBatch, deltaTime);
            var drawRect = DrawRect;
            switch (shape)
            {
                case Shape.Rectangle:
                    GUI.DrawRectangle(spriteBatch, drawRect, color, isFilled, thickness: IsSelected ? 3 : 1);
                    break;
                case Shape.Circle:
                    ShapeExtensions.DrawCircle(spriteBatch, DrawPos, size / 2, sides, color, thickness: IsSelected ? 3 : 1);
                    break;
                default: throw new NotImplementedException(shape.ToString());
            }
            if (IsSelected)
            {
                if (!string.IsNullOrEmpty(tooltip))
                {
                    var offset = tooltipOffset ?? new Vector2(size, -size / 2);
                    GUI.DrawString(spriteBatch, DrawPos + offset, tooltip, textColor, textBackgroundColor);
                }
            }
            PostDraw?.Invoke(spriteBatch, deltaTime);
        }
    }
}
