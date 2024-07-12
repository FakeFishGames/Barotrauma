#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    internal class CircuitBoxWireRenderer
    {
        private const int VertsPerQuad = 4, // how many points per quad
                          QuadsPerLine = 10, // how many quads per line
                          VertsPerLine = QuadsPerLine * VertsPerQuad, // how many points we need to draw all the quads for a single line
                          TotalVertsPerWire = VertsPerLine * 2; // we are drawing 2 lines

        private readonly Texture2D texture;

        private VertexPositionColorTexture[] verts = new VertexPositionColorTexture[TotalVertsPerWire];
        private readonly Vector2[][] colliders = new Vector2[2][];
        private SquareLine skeleton;

        private Vector2 lastStart, lastEnd;
        private Color lastColor;
        private readonly Option<CircuitBoxWire> wire;

        public CircuitBoxWireRenderer(Option<CircuitBoxWire> wire, Vector2 start, Vector2 end, Color color, Sprite? wireSprite)
        {
            this.wire = wire;
            texture = wireSprite?.Texture ?? GUI.WhiteTexture;
            Recompute(start, end, color);
        }

        private void UpdateColor(Color color)
        {
            for (int i = 0; i < TotalVertsPerWire; i++)
            {
                verts[i].Color = color;
            }

            lastColor = color;
        }

        public void Recompute(Vector2 start, Vector2 end, Color color)
        {
            if (MathUtils.NearlyEqual(lastStart, start) && MathUtils.NearlyEqual(lastEnd, end))
            {
                if (lastColor == color) { return; }

                UpdateColor(color);
                return;
            }

            lastStart = start;
            lastEnd = end;
            lastColor = color;

            skeleton = ToolBox.GetSquareLineBetweenPoints(start, end, CircuitBoxSizes.WireKnobLength);
            var points = skeleton.Points;

            Vector2 centerOfLine = (points[2] + points[3]) / 2f;

            ImmutableArray<Vector2> points1 = GetLinePoints(points[1], points[2], centerOfLine),
                                    points2 = GetLinePoints(centerOfLine, points[3], points[4]);

            colliders[0] = ConstructQuads(ref verts, 0, points1, color);
            colliders[1] = ConstructQuads(ref verts, VertsPerLine, points2, color);

            static ImmutableArray<Vector2> GetLinePoints(Vector2 start, Vector2 control, Vector2 end)
            {
                var points = ImmutableArray.CreateBuilder<Vector2>(QuadsPerLine);
                for (int i = 0; i < QuadsPerLine; i++)
                {
                    float t = (float)i / (QuadsPerLine - 1);
                    Vector2 pos = MathUtils.Bezier(start, control, end, t);
                    points.Add(pos);
                }

                return points.ToImmutable();
            }

            static Vector2[] ConstructQuads(ref VertexPositionColorTexture[] verts, int startOffset, IReadOnlyList<Vector2> points, Color color)
            {
                // ok I don't know why this needs to be one quad less, maybe we are drawing with only 9 quads lol
                var collider = new Vector2[VertsPerLine - VertsPerQuad];

                int leftIndex = collider.Length - 1,
                    rightIndex = 0;

                // we need to calculate half of the width since the way we expand the quads from origin, otherwise the line will be twice as wide
                const float halfWidth = CircuitBoxSizes.WireWidth / 2f;

                // draw the line using quads
                for (int i = 0; i < points.Count - 1; i++)
                {
                    bool isFirst = i == 0 && startOffset == 0,
                         isLast = i == points.Count - 2 && startOffset > 0;

                    Vector2 start = points[i],
                            end = points[i + 1];

                    Vector2 dir = Vector2.Normalize(end - start);
                    Vector2 length = new Vector2(dir.Y, -dir.X) * halfWidth;

                    int vertIndex = startOffset + i * 4;

                    Vector2 topRight = end + length;
                    Vector2 topLeft = end - length;

                    Vector2 bottomRight;
                    Vector2 bottomLeft;

                    // get previous points if any
                    int prevIndex = vertIndex - 4;

                    if ((prevIndex - startOffset) >= 0)
                    {
                        // connect the previous "upper" corners into the current "lower" corners to stitch the line together
                        Vector3 prevTopRight = verts[TopRight(prevIndex)].Position,
                                prevTopLeft = verts[TopLeft(prevIndex)].Position;

                        bottomRight = ToVector2(prevTopRight);
                        bottomLeft = ToVector2(prevTopLeft);
                    }
                    else
                    {
                        bottomRight = start + length;
                        bottomLeft = start - length;
                    }

                    if (isFirst)
                    {
                        if (MathF.Abs(dir.Y) > MathF.Abs(dir.X))
                        {
                            float offset = dir.Y < 0 ? halfWidth : -halfWidth;
                            // if the line is more vertical than horizontal, we want to move the bottom corners to the left
                            bottomRight.Y = start.Y - offset;
                            bottomLeft.Y = start.Y - offset;
                        }
                        else
                        {
                            // otherwise we want to move the bottom corners to the top
                            bottomRight.X = start.X;
                            bottomLeft.X = start.X;
                        }
                    }
                    else if (isLast)
                    {
                        if (MathF.Abs(dir.Y) > MathF.Abs(dir.X))
                        {
                            float offset = dir.Y < 0 ? halfWidth : -halfWidth;
                            // if the line is more vertical than horizontal, we want to move the bottom corners to the left
                            topRight.Y = end.Y + offset;
                            topLeft.Y = end.Y + offset;
                        }
                        else
                        {
                            // otherwise we want to move the bottom corners to the top
                            topRight.X = end.X;
                            topLeft.X = end.X;
                        }
                    }

                    collider[rightIndex++] = bottomLeft;
                    collider[rightIndex++] = topLeft;

                    collider[leftIndex--] = bottomRight;
                    collider[leftIndex--] = topRight;

                    // adjust this if we want sprites to support sourceRects
                    Vector2 uvTopRight = new Vector2(0, 1),
                            uvTopLeft = new Vector2(0, 0),
                            uvBottomRight = new Vector2(1, 1),
                            uvBottomLeft = new Vector2(1, 0);

                    SetPos(ref verts, TopRight(vertIndex), topRight, color, uvTopRight);
                    SetPos(ref verts, TopLeft(vertIndex), topLeft, color, uvTopLeft);
                    SetPos(ref verts, BottomRight(vertIndex), bottomRight, color, uvBottomRight);
                    SetPos(ref verts, BottomLeft(vertIndex), bottomLeft, color, uvBottomLeft);

                    static void SetPos(ref VertexPositionColorTexture[] verts, int index, Vector2 pos, Color color, Vector2 uv)
                    {
                        verts[index].Position = ToVector3(pos);
                        verts[index].Color = color;
                        verts[index].TextureCoordinate = uv;
                        static Vector3 ToVector3(Vector2 v) => new Vector3(v.X, v.Y, 0f);
                    }

                    static int TopRight(int vertIndex) => vertIndex;
                    static int TopLeft(int vertIndex) => vertIndex + 1;
                    static int BottomRight(int vertIndex) => vertIndex + 2;
                    static int BottomLeft(int vertIndex) => vertIndex + 3;

                    static Vector2 ToVector2(Vector3 v) => new Vector2(v.X, v.Y);
                }

                return collider;
            }
        }

        public bool Contains(Vector2 pos)
        {
            pos.Y = -pos.Y;
            foreach (Vector2[] collider in colliders)
            {
                if (ToolBox.PointIntersectsWithPolygon(pos, collider, checkBoundingBox: false)) { return true; }
            }

            return false;
        }

        public void Draw(SpriteBatch spriteBatch, Color selectionColor)
        {
            if (GameMain.DebugDraw)
            {
                for (int i = 0; i < skeleton.Points.Length; i++)
                {
                    Vector2 point = skeleton.Points[i];
                    spriteBatch.DrawPoint(point, Color.White, 25f);
                    GUI.DrawString(spriteBatch, point - new Vector2(5f, 17f), i.ToString(), Color.Black, font: GUIStyle.LargeFont);
                }

                spriteBatch.DrawLine(skeleton.Points[0], skeleton.Points[1], GUIStyle.Green, thickness: 2f);
                spriteBatch.DrawLine(skeleton.Points[1], skeleton.Points[2], GUIStyle.Green, thickness: 2f);
                spriteBatch.DrawLine(skeleton.Points[2], skeleton.Points[3], GUIStyle.Green, thickness: 2f);
                spriteBatch.DrawLine(skeleton.Points[3], skeleton.Points[4], GUIStyle.Green, thickness: 2f);
                spriteBatch.DrawLine(skeleton.Points[4], skeleton.Points[5], GUIStyle.Green, thickness: 2f);
            }

            bool isSelected = wire.TryUnwrap(out var w) && w.IsSelected;

            if (isSelected)
            {
                foreach (var colliderPolys in colliders)
                {
                    spriteBatch.DrawPolygon(Vector2.Zero, colliderPolys, selectionColor, 5f);
                }
            }

            spriteBatch.Draw(texture, verts, 0f);

            if (skeleton.Type is SquareLine.LineType.SixPointBackwardsLine)
            {
                // we need to expand the start and end points to make the line look like it's connected to the "smooth" part of the line
                Vector2 expandedEnd = skeleton.Points[1],
                        expandedStart = skeleton.Points[4];

                expandedEnd.X += CircuitBoxSizes.WireWidth / 2f;
                expandedStart.X -= CircuitBoxSizes.WireWidth / 2f;

                spriteBatch.DrawLineWithTexture(texture, skeleton.Points[0], expandedEnd, lastColor, thickness: CircuitBoxSizes.WireWidth);
                spriteBatch.DrawLineWithTexture(texture, expandedStart, skeleton.Points[5], lastColor, thickness: CircuitBoxSizes.WireWidth);

                const float rectSize = CircuitBoxSizes.WireWidth * 1.5f;
                RectangleF startKnob = new RectangleF(skeleton.Points[1] - new Vector2(rectSize / 2f), new Vector2(rectSize)),
                           endKnob = new RectangleF(skeleton.Points[4] - new Vector2(rectSize / 2f), new Vector2(rectSize));

                GUI.DrawFilledRectangle(spriteBatch, startKnob, lastColor);
                GUI.DrawFilledRectangle(spriteBatch, endKnob, lastColor);
            }

            if (!GameMain.DebugDraw) { return; }

            foreach (var colliderPolys in colliders)
            {
                spriteBatch.DrawPolygonInner(Vector2.Zero, colliderPolys, Color.Lime, 1f);
            }
        }
    }
}