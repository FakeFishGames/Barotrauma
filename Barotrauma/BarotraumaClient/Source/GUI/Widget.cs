using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class Widget
    {
        public enum Shape
        {
            Rectangle,
            Circle,
            Cross
        }

        public Shape shape;
        public string tooltip;
        public bool showTooltip = true;
        public Rectangle DrawRect => new Rectangle((int)(DrawPos.X - (float)size / 2), (int)(DrawPos.Y - (float)size / 2), size, size);
        public Rectangle InputRect
        {
            get
            {
                var inputRect = DrawRect;
                inputRect.Inflate(inputAreaMargin, inputAreaMargin);
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
        public int inputAreaMargin;
        public Color color = Color.Red;
        public Color? secondaryColor;
        public Color textColor = Color.White;
        public Color textBackgroundColor = Color.Black * 0.5f;
        public readonly string id;

        public event Action Selected;
        public event Action Deselected;
        public event Action Hovered;
        public event Action MouseUp;
        public event Action MouseDown;
        public event Action<float> MouseHeld;
        public event Action<float> PreUpdate;
        public event Action<float> PostUpdate;
        public event Action<SpriteBatch, float> PreDraw;
        public event Action<SpriteBatch, float> PostDraw;

        public bool RequireMouseOn = true;

        public Action refresh;

        public object data;

        public bool IsSelected => enabled && selectedWidgets.Contains(this);
        public bool IsControlled => IsSelected && PlayerInput.LeftButtonHeld();
        public bool IsMouseOver => GUI.MouseOn == null && InputRect.Contains(PlayerInput.MousePosition);
        private bool enabled = true;
        public bool Enabled
        {
            get { return enabled; }
            set
            {
                enabled = value;
                if (!enabled && selectedWidgets.Contains(this))
                {
                    selectedWidgets.Remove(this);
                }
            }
        }

        private static bool multiselect;
        public static bool EnableMultiSelect
        {
            get { return multiselect; }
            set
            {
                multiselect = value;
                if (!multiselect && selectedWidgets.Multiple())
                {
                    selectedWidgets = selectedWidgets.Take(1).ToList();
                }
            }
        }
        public Vector2? tooltipOffset;

        public Widget linkedWidget;

        public static List<Widget> selectedWidgets = new List<Widget>();

        public Widget(string id, int size, Shape shape)
        {
            this.id = id;
            this.size = size;
            this.shape = shape;
        }

        public virtual void Update(float deltaTime)
        {
            PreUpdate?.Invoke(deltaTime);
            if (!enabled) { return; }
            if (IsMouseOver || (!RequireMouseOn && selectedWidgets.Contains(this) && PlayerInput.LeftButtonHeld()))
            {
                Hovered?.Invoke();
                if (RequireMouseOn || PlayerInput.LeftButtonDown())
                {
                    if ((multiselect && !selectedWidgets.Contains(this)) || selectedWidgets.None())
                    {
                        selectedWidgets.Add(this);
                        Selected?.Invoke();
                    }
                }
            }
            else if (selectedWidgets.Contains(this))
            {
                selectedWidgets.Remove(this);
                Deselected?.Invoke();
            }
            if (IsSelected)
            {
                if (PlayerInput.LeftButtonDown())
                {
                    MouseDown?.Invoke();
                }
                if (PlayerInput.LeftButtonHeld())
                {
                    MouseHeld?.Invoke(deltaTime);
                }
                if (PlayerInput.LeftButtonClicked())
                {
                    MouseUp?.Invoke();
                }
            }
            PostUpdate?.Invoke(deltaTime);
        }

        public virtual void Draw(SpriteBatch spriteBatch, float deltaTime)
        {
            PreDraw?.Invoke(spriteBatch, deltaTime);
            var drawRect = DrawRect;
            switch (shape)
            {
                case Shape.Rectangle:
                    if (secondaryColor.HasValue)
                    {
                        GUI.DrawRectangle(spriteBatch, drawRect, secondaryColor.Value, isFilled, thickness: 2);
                    }
                    GUI.DrawRectangle(spriteBatch, drawRect, color, isFilled, thickness: IsSelected ? 3 : 1);
                    break;
                case Shape.Circle:
                    if (secondaryColor.HasValue)
                    {
                        ShapeExtensions.DrawCircle(spriteBatch, DrawPos, size / 2, sides, secondaryColor.Value, thickness: 2);
                    }
                    ShapeExtensions.DrawCircle(spriteBatch, DrawPos, size / 2, sides, color, thickness: IsSelected ? 3 : 1);
                    break;
                case Shape.Cross:
                    float halfSize = size / 2;
                    if (secondaryColor.HasValue)
                    {
                        GUI.DrawLine(spriteBatch, DrawPos + Vector2.UnitY * halfSize, DrawPos - Vector2.UnitY * halfSize, secondaryColor.Value, width: 2);
                        GUI.DrawLine(spriteBatch, DrawPos + Vector2.UnitX * halfSize, DrawPos - Vector2.UnitX * halfSize, secondaryColor.Value, width: 2);
                    }
                    GUI.DrawLine(spriteBatch, DrawPos + Vector2.UnitY * halfSize, DrawPos - Vector2.UnitY * halfSize, color, width: IsSelected ? 3 : 1);
                    GUI.DrawLine(spriteBatch, DrawPos + Vector2.UnitX * halfSize, DrawPos - Vector2.UnitX * halfSize, color, width: IsSelected ? 3 : 1);
                    break;
                default: throw new NotImplementedException(shape.ToString());
            }
            if (IsSelected)
            {
                if (showTooltip && !string.IsNullOrEmpty(tooltip))
                {
                    var offset = tooltipOffset ?? new Vector2(size, -size / 2);
                    GUI.DrawString(spriteBatch, DrawPos + offset, tooltip, textColor, textBackgroundColor);
                }
            }
            PostDraw?.Invoke(spriteBatch, deltaTime);
        }
    }
}
