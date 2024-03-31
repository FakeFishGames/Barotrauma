using Microsoft.Xna.Framework;

namespace Barotrauma.Extensions
{
    public static class RectangleExtensions
    {
        public static Rectangle Multiply(this Rectangle rect, float f)
        {
            Vector2 location = new Vector2(rect.X, rect.Y) * f;
            return new Rectangle(new Point((int)location.X, (int)location.Y), rect.MultiplySize(f));
        }

        public static Rectangle Divide(this Rectangle rect, float f)
        {
            Vector2 location = new Vector2(rect.X, rect.Y) / f;
            return new Rectangle(new Point((int)location.X, (int)location.Y), rect.DivideSize(f));
        }

        public static Point DivideSize(this Rectangle rect, float f)
        {
            return new Point((int)(rect.Width / f), (int)(rect.Height / f));
        }

        public static Point DivideSize(this Rectangle rect, Vector2 f)
        {
            return new Point((int)(rect.Width / f.X), (int)(rect.Height / f.Y));
        }

        public static Point MultiplySize(this Rectangle rect, float f)
        {
            return new Point((int)(rect.Width * f), (int)(rect.Height * f));
        }

        public static Point MultiplySize(this Rectangle rect, Vector2 f)
        {
            return new Point((int)(rect.Width * f.X), (int)(rect.Height * f.Y));
        }

        public static Vector2 CalculateRelativeSize(this Rectangle rect, Rectangle relativeRect)
        {
            return new Vector2(rect.Width, rect.Height) / new Vector2(relativeRect.Width, relativeRect.Height);
        }

        public static Rectangle ScaleSize(this Rectangle rect, Rectangle relativeTo)
        {
            return rect.ScaleSize(rect.CalculateRelativeSize(relativeTo));
        }

        public static Rectangle ScaleSize(this Rectangle rect, Vector2 scale)
        {
            var size = rect.MultiplySize(scale);
            return new Rectangle(rect.X, rect.Y, size.X, size.Y);
        }

        public static Rectangle ScaleSize(this Rectangle rect, float scale)
        {
            var size = rect.MultiplySize(scale);
            return new Rectangle(rect.X, rect.Y, size.X, size.Y);
        }

        public static bool IntersectsWorld(this Rectangle rect, Rectangle value)
        {
            int bottom = rect.Y - rect.Height;
            int otherBottom = value.Y - value.Height;
            return value.Left < rect.Right && rect.Left < value.Right &&
                   value.Top > bottom && rect.Top > otherBottom;
        }

        /// <summary>
        /// Like the XNA method, but treats the y-coordinate so that up is greater and down is lower.
        /// </summary>
        public static bool ContainsWorld(this Rectangle rect, Rectangle other)
        {
            return 
                (rect.X <= other.X) && ((other.X + other.Width) <= (rect.X + rect.Width)) && 
                (rect.Y >= other.Y) && ((other.Y - other.Height) >= (rect.Y - rect.Height));
        }

        /// <summary>
        /// Like the XNA method, but treats the y-coordinate so that up is greater and down is lower.
        /// </summary>
        public static bool ContainsWorld(this Rectangle rect, Vector2 point)
        {
            return 
                (rect.X <= point.X) && (point.X < (rect.X + rect.Width)) &&
                (rect.Y >= point.Y) && (point.Y > (rect.Y - rect.Height));
        }

        /// <summary>
        /// Like the XNA method, but treats the y-coordinate so that up is greater and down is lower.
        /// </summary>
        public static bool ContainsWorld(this Rectangle rect, Point point)
        {
            return
                (rect.X <= point.X) && (point.X < (rect.X + rect.Width)) &&
                (rect.Y >= point.Y) && (point.Y > (rect.Y - rect.Height));
        }
    }
}
