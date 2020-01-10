using Microsoft.Xna.Framework;

namespace Barotrauma.Extensions
{
    public static class PointExtensions
    {
        public static Point Multiply(this Point p, float f)
        {
            return new Point((int)(p.X * f), (int)(p.Y * f));
        }

        public static Point Multiply(this Point p, int i)
        {
            return new Point(p.X * i, p.Y * i);
        }

        public static Point Multiply(this Point p, Vector2 v)
        {
            return new Point((int)(p.X * v.X), (int)(p.Y * v.Y));
        }

        public static Point Divide(this Point p, int i)
        {
            if (i == 0) { return Point.Zero; }
            return new Point(p.X / i, p.Y / i);
        }

        public static Point Divide(this Point p, float f)
        {
            if (f == 0) { return Point.Zero; }
            return new Point((int)(p.X / f), (int)(p.Y / f));
        }

        public static Point Divide(this Point p, Vector2 v)
        {
            if (v.X == 0 || v.Y == 0) { return Point.Zero; }
            return new Point((int)(p.X / v.X), (int)(p.Y / v.Y));
        }

        public static Point Inverse(this Point p)
        {
            return new Point(-p.X, -p.Y);
        }

        public static Point Clamp(this Point p, Point min, Point max)
        {
            return new Point(MathHelper.Clamp(p.X, min.X, max.X), MathHelper.Clamp(p.Y, min.Y, max.Y));
        }
    }
}
