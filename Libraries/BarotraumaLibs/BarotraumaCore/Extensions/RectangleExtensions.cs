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
        /// Converts the y-coordinate of the rectangle so that up is greater and down is lower 
        /// (i.e. so the Location of the rectangle is at the top-left corner in world coordinates).
        /// </summary>
        public static Rectangle ToWorldRect(this Rectangle rect)
        {
            return new Rectangle(rect.X, rect.Y - rect.Height, rect.Size.X, rect.Size.Y);
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

        /// <summary>
        /// Clips a line to the boundaries of a rectangle.
        /// </summary>
        /// <remarks>
        /// Adapted from the Liang-Barsky function by Daniel White (<see href="http://www.skytopia.com/project/articles/compsci/clipping.html"/>).
        /// </remarks>
        /// <returns>
        /// - <see langword="true"/> if the line should be drawn.<br/>
        /// - <see langword="false"/> if the line shouldn't be drawn.
        /// </returns>
        public static bool ClipLine(this Rectangle rect, ref Vector2 start, ref Vector2 end, bool flipY = false)
        {
            Vector2 delta = end - start;
            float startOffset = 0f, endOffset = 1f;

            if (!ClipEdge(-delta.X, start.X - rect.Left)) { return false; }
            if (!ClipEdge(delta.X, rect.Right - start.X)) { return false; }
            if (flipY)
            {
                if (!ClipEdge(-delta.Y, start.Y - rect.Top)) { return false; }
                if (!ClipEdge(delta.Y, rect.Bottom - start.Y)) { return false; }
            }
            else
            {
                if (!ClipEdge(delta.Y, rect.Top - start.Y)) { return false; }
                if (!ClipEdge(-delta.Y, start.Y - rect.Bottom)) { return false; }
            }

            end = start + delta * endOffset;
            start += delta * startOffset;
            return true;

            bool ClipEdge(float relativeDelta, float underlap)
            {
                if (relativeDelta == 0f) { return underlap > 0f; }
                float offset = underlap / relativeDelta;
                switch (relativeDelta)
                {
                    case < 0f when offset > endOffset:
                    case > 0f when offset < startOffset:
                        return false;
                    case < 0f when offset > startOffset:
                        startOffset = offset;
                        break;
                    case > 0f when offset < endOffset:
                        endOffset = offset;
                        break;
                }
                return true;
            }
        }
    }
}
