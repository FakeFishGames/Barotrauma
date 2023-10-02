#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal partial class CircuitBoxNode : CircuitBoxSelectable
    {
        public Vector2 Size;
        public RectangleF Rect;
        private Vector2 position;

        public Vector2 Position
        {
            get => position;
            set
            {
                const float clampSize = CircuitBoxSizes.PlayableAreaSize / 2f;

                position = new Vector2(Math.Clamp(value.X, -clampSize, clampSize),
                                       Math.Clamp(value.Y, -clampSize, clampSize));
                UpdatePositions();
            }
        }

        public ImmutableArray<CircuitBoxConnection> Connectors;

        public static float Opacity = 0.8f;

        public readonly CircuitBox CircuitBox;

        public CircuitBoxNode(CircuitBox circuitBox)
        {
            CircuitBox = circuitBox;
        }

        public static Vector2 CalculateSize(IReadOnlyList<CircuitBoxConnection> conns)
        {
            Vector2 leftSize = Vector2.Zero,
                    rightSize = Vector2.Zero;

            foreach (var c in conns)
            {
                if (c.IsOutput)
                {
                    rightSize.X = MathF.Max(rightSize.X, c.Length);
                }
                else
                {
                    leftSize.X = MathF.Max(leftSize.X, c.Length);
                }

                if (c.IsOutput)
                {
                    rightSize.Y += CircuitBoxConnection.Size;
                }
                else
                {
                    leftSize.Y += CircuitBoxConnection.Size;
                }
            }

            return new Vector2(leftSize.X + CircuitBoxSizes.NodeInitialPadding + rightSize.X, CircuitBoxSizes.NodeInitialPadding + MathF.Max(leftSize.Y, rightSize.Y));
        }

        protected void UpdatePositions()
        {
            Vector2 rectStart = Position - Size / 2f;
            Vector2 rectSize = Size;
            rectSize.Y += CircuitBoxSizes.NodeHeaderHeight;
            Rect = new RectangleF(rectStart, rectSize);

#if CLIENT
            UpdateDrawRects();
#endif

            int leftIndex = 0,
                rightIndex = 0;

            int inputCount = 0,
                outputCount = 0;

            foreach (var c in Connectors)
            {
                if (c.IsOutput)
                {
                    outputCount++;
                }
                else
                {
                    inputCount++;
                }
            }

            Vector2 drawPos = Position;
            drawPos.Y = -drawPos.Y;

            foreach (var c in Connectors)
            {
                bool isOutput = c.IsOutput;

                int yIndex = isOutput ? rightIndex : leftIndex;
                int count = isOutput ? outputCount : inputCount;

                float totalHeight = (count * CircuitBoxConnection.Size) / 2f;
                float y = (yIndex * CircuitBoxConnection.Size) - totalHeight;

                float halfWidth = Rect.Width / 2f - CircuitBoxConnection.Size / 2f;

                halfWidth -= 16f;

                float xOffset = c.IsOutput ? halfWidth : -halfWidth;

                Vector2 inputPos = drawPos + new Vector2(xOffset, y + c.Rect.Height / 2f);
                c.Position = inputPos;

                if (isOutput)
                {
                    rightIndex++;
                }
                else
                {
                    leftIndex++;
                }
            }
        }
    }
}