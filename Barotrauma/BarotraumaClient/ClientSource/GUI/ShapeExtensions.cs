using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    ///     Sprite batch extensions for drawing primitive shapes
    ///     Modified from: https://github.com/craftworkgames/MonoGame.Extended/blob/develop/Source/MonoGame.Extended/ShapeExtensions.cs
    /// </summary>
    public static class ShapeExtensions
    {
        private static Texture2D _whitePixelTexture;

        private static Texture2D GetTexture(SpriteBatch spriteBatch)
        {
            if (_whitePixelTexture == null)
            {
                CrossThread.RequestExecutionOnMainThread(() =>
                {
                    _whitePixelTexture = new Texture2D(spriteBatch.GraphicsDevice, 1, 1, false, SurfaceFormat.Color);
                    _whitePixelTexture.SetData(new[] { Color.White });
                });
            }

            return _whitePixelTexture;
        }

        /// <summary>
        ///     Draws a closed polygon from a <see cref="Polygon" /> shape
        /// </summary>
        public static void DrawPolygon(this SpriteBatch spriteBatch, Vector2 position, Polygon polygon, Color color,
            float thickness = 1f)
        {
            DrawPolygon(spriteBatch, position, polygon.Vertices, color, thickness);
        }

        /// <summary>
        ///     Draws a closed polygon from an array of points
        /// </summary>
        public static void DrawPolygon(this SpriteBatch spriteBatch, Vector2 offset, IReadOnlyList<Vector2> points, Color color,
            float thickness = 1f)
        {
            if (points.Count == 0)
                return;

            if (points.Count == 1)
            {
                DrawPoint(spriteBatch, points[0], color, (int)thickness);
                return;
            }

            var texture = GetTexture(spriteBatch);

            for (var i = 0; i < points.Count - 1; i++)
                DrawPolygonEdge(spriteBatch, points[i] + offset, points[i + 1] + offset, color, thickness);

            DrawPolygonEdge(spriteBatch, points[points.Count - 1] + offset, points[0] + offset, color,
                thickness);
        }
        
        /// <summary>
        ///     Draws a closed polygon from an array of points
        /// </summary>
        public static void DrawPolygonInner(this SpriteBatch spriteBatch, Vector2 offset, IReadOnlyList<Vector2> points, Color color, float thickness = 1f)
        {
            if (points.Count == 0) { return; }

            if (points.Count == 1)
            {
                DrawPoint(spriteBatch, points[0], color, (int)thickness);
                return;
            }

            for (var i = 0; i < points.Count - 1; i++)
            {
                Vector2 point1 = points[i] + offset, 
                        point2 = points[i + 1] + offset;

                DrawPolygonEdgeInner(spriteBatch, point1, point2, color, thickness);
            }

            DrawPolygonEdgeInner(spriteBatch, points[^1] + offset, points[0] + offset, color, thickness);
        }

        private static void DrawPolygonEdgeInner(SpriteBatch spriteBatch, Vector2 point1, Vector2 point2, Color color, float thickness)
        {
            var length = Vector2.Distance(point1, point2) + thickness;
            var angle = (float)Math.Atan2(point2.Y - point1.Y, point2.X - point1.X);
            var scale = new Vector2(length, thickness);
            Vector2 middle = new Vector2((point1.X + point2.X) / 2f, (point1.Y + point2.Y) / 2f);
            Texture2D tex = GetTexture(spriteBatch);
            spriteBatch.Draw(GetTexture(spriteBatch), middle, null, color, angle, new Vector2(tex.Width / 2f, tex.Height / 2f), scale, SpriteEffects.None, 0);
        }

        private static void DrawPolygonEdge(SpriteBatch spriteBatch, Vector2 point1, Vector2 point2, Color color, float thickness)
        {
            var length = Vector2.Distance(point1, point2);
            var angle = (float)Math.Atan2(point2.Y - point1.Y, point2.X - point1.X);
            var scale = new Vector2(length, thickness);
            spriteBatch.Draw(GetTexture(spriteBatch), point1, null, color, angle, Vector2.Zero, scale, SpriteEffects.None, 0);
        }

        /// <summary>
        ///     Draws a line from point1 to point2 with an offset
        /// </summary>
        public static void DrawLine(this SpriteBatch spriteBatch, float x1, float y1, float x2, float y2, Color color,
            float thickness = 1f)
        {
            DrawLine(spriteBatch, new Vector2(x1, y1), new Vector2(x2, y2), color, thickness);
        }

        /// <summary>
        ///     Draws a line from point1 to point2 with an offset
        /// </summary>
        public static void DrawLine(this SpriteBatch spriteBatch, Vector2 point1, Vector2 point2, Color color,
            float thickness = 1f)
        {
            // calculate the distance between the two vectors
            var distance = Vector2.Distance(point1, point2);

            // calculate the angle between the two vectors
            var angle = (float)Math.Atan2(point2.Y - point1.Y, point2.X - point1.X);

            DrawLine(spriteBatch, point1, distance, angle, color, thickness);
        }

        /// <summary>
        ///     Draws a line from point1 to point2 with an offset
        /// </summary>
        public static void DrawLine(this SpriteBatch spriteBatch, Vector2 point, float length, float angle, Color color,
            float thickness = 1f)
        {
            var origin = new Vector2(0f, 0.5f);
            var scale = new Vector2(length, thickness);
            spriteBatch.Draw(GetTexture(spriteBatch), point, null, color, angle, origin, scale, SpriteEffects.None, 0);
        }

        /// <summary>
        ///     Draws a point at the specified x, y position. The center of the point will be at the position.
        /// </summary>
        public static void DrawPoint(this SpriteBatch spriteBatch, float x, float y, Color color, float size = 1f)
        {
            DrawPoint(spriteBatch, new Vector2(x, y), color, size);
        }

        /// <summary>
        ///     Draws a point at the specified position. The center of the point will be at the position.
        /// </summary>
        public static void DrawPoint(this SpriteBatch spriteBatch, Vector2 position, Color color, float size = 1f)
        {
            var scale = Vector2.One * size;
            var offset = new Vector2(0.5f) - new Vector2(size * 0.5f);
            spriteBatch.Draw(GetTexture(spriteBatch), position + offset, null, color, 0.0f, Vector2.Zero, Vector2.One, SpriteEffects.None, 0);
        }

        public static void DrawCircle(this SpriteBatch spriteBatch, Vector2 center, float radius, int sides, Color color,
            float thickness = 1f)
        {
            DrawPolygon(spriteBatch, center, CreateCircle(radius, sides), color, thickness);
        }

        public static void DrawCircle(this SpriteBatch spriteBatch, float x, float y, float radius, int sides,
            Color color, float thickness = 1f)
        {
            DrawPolygon(spriteBatch, new Vector2(x, y), CreateCircle(radius, sides), color, thickness);
        }

        public static void DrawSector(this SpriteBatch spriteBatch, Vector2 center, float radius, float radians, int sides, Color color, float offset = 0, float thickness = 1)
        {
            DrawPolygon(spriteBatch, center, CreateSector(radius, sides, radians, offset), color, thickness);
        }

        private static Vector2[] CreateSector(double radius, int sides, float radians, float offset = 0)
        {
            //circle sectors need one extra point at the center
            var points = new Vector2[radians < MathHelper.TwoPi ? sides + 1 : sides];
            var step = radians / sides;

            double theta = offset;
            for (var i = 0; i < sides; i++)
            {
                points[i] = new Vector2((float)Math.Cos(theta), (float)Math.Sin(theta)) * (float)radius;
                theta += step;
            }

            return points;
        }

        private static Vector2[] CreateCircle(double radius, int sides)
        {
            return CreateSector(radius, sides, MathHelper.TwoPi);
        }
    }

    /// <summary>
    /// Original source: https://github.com/craftworkgames/MonoGame.Extended/blob/develop/Source/MonoGame.Extended/Shapes/Polygon.cs
    /// </summary>
    public class Polygon : IEquatable<Polygon>
    {
        public Polygon(IEnumerable<Vector2> vertices)
        {
            _localVertices = vertices.ToArray();
            _transformedVertices = _localVertices;
            _offset = Vector2.Zero;
            _rotation = 0;
            _scale = Vector2.One;
            _isDirty = false;
        }

        private readonly Vector2[] _localVertices;
        private Vector2[] _transformedVertices;
        private Vector2 _offset;
        private float _rotation;
        private Vector2 _scale;
        private bool _isDirty;

        public Vector2[] Vertices
        {
            get
            {
                if (_isDirty)
                {
                    _transformedVertices = GetTransformedVertices();
                    _isDirty = false;
                }

                return _transformedVertices;
            }
        }

        public float Left
        {
            get { return Vertices.Min(v => v.X); }
        }

        public float Right
        {
            get { return Vertices.Max(v => v.X); }
        }

        public float Top
        {
            get { return Vertices.Min(v => v.Y); }
        }

        public float Bottom
        {
            get { return Vertices.Max(v => v.Y); }
        }

        public void Offset(Vector2 amount)
        {
            _offset += amount;
            _isDirty = true;
        }

        public void Rotate(float amount)
        {
            _rotation += amount;
            _isDirty = true;
        }

        public void Scale(Vector2 amount)
        {
            _scale += amount;
            _isDirty = true;
        }

        private Vector2[] GetTransformedVertices()
        {
            var newVertices = new Vector2[_localVertices.Length];
            var isScaled = _scale != Vector2.One;

            for (var i = 0; i < _localVertices.Length; i++)
            {
                var p = _localVertices[i];

                if (isScaled)
                    p *= _scale;

                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (_rotation != 0)
                {
                    var cos = (float)Math.Cos(_rotation);
                    var sin = (float)Math.Sin(_rotation);
                    p = new Vector2(cos * p.X - sin * p.Y, sin * p.X + cos * p.Y);
                }

                newVertices[i] = p + _offset;
            }

            return newVertices;
        }

        public Polygon TransformedCopy(Vector2 offset, float rotation, Vector2 scale)
        {
            var polygon = new Polygon(_localVertices);
            polygon.Offset(offset);
            polygon.Rotate(rotation);
            polygon.Scale(scale - Vector2.One);
            return new Polygon(polygon.Vertices);
        }

        public bool Contains(Vector2 point)
        {
            return Contains(point.X, point.Y);
        }

        public bool Contains(float x, float y)
        {
            var intersects = 0;
            var vertices = Vertices;

            for (var i = 0; i < vertices.Length; i++)
            {
                var x1 = vertices[i].X;
                var y1 = vertices[i].Y;
                var x2 = vertices[(i + 1) % vertices.Length].X;
                var y2 = vertices[(i + 1) % vertices.Length].Y;

                if ((((y1 <= y) && (y < y2)) || ((y2 <= y) && (y < y1))) && (x < (x2 - x1) / (y2 - y1) * (y - y1) + x1))
                    intersects++;
            }

            return (intersects & 1) == 1;
        }

        public static bool operator ==(Polygon a, Polygon b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(Polygon a, Polygon b)
        {
            return !(a == b);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is Polygon && Equals((Polygon)obj);
        }

        public bool Equals(Polygon other)
        {
            return Vertices.SequenceEqual(other.Vertices);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return Vertices.Aggregate(27, (current, v) => current + 13 * current + v.GetHashCode());
            }
        }
    }
}