using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Barotrauma.Extensions;

namespace Barotrauma
{
    public enum Anchor
    {
        TopLeft, TopCenter, TopRight,
        CenterLeft, Center, CenterRight,
        BottomLeft, BottomCenter, BottomRight
    }

    public enum Pivot
    {
        TopLeft, TopCenter, TopRight,
        CenterLeft, Center, CenterRight,
        BottomLeft, BottomCenter, BottomRight
    }

    public class RectTransform
    {
        #region Fields and Properties
        private RectTransform parent;
        public RectTransform Parent
        {
            get { return parent; }
            set
            {
                parent = value;
                if (parent != null)
                {
                    parent.children.Add(this);
                }
            }
        }

        private HashSet<RectTransform> children = new HashSet<RectTransform>();
        public IEnumerable<RectTransform> Children { get { return children; } }

        private Vector2 relativeSize = Vector2.One;
        /// <summary>
        /// Relative to the parent rect.
        /// </summary>
        public Vector2 RelativeSize
        {
            get { return relativeSize; }
            set
            {
                relativeSize = value;
                RecalculateAbsoluteSize(ParentRect);
            }
        }

        private Point nonScaledSize;
        /// <summary>
        /// Absolute size before scale multiplications.
        /// </summary>
        public Point NonScaledSize
        {
            get { return nonScaledSize; }
            set
            {
                nonScaledSize = value;
                RecalculateRelativeSize(ParentRect);
            }
        }
        /// <summary>
        /// Absolute size after scale multiplications.
        /// </summary>
        public Point ScaledSize { get { return NonScaledSize.Multiply(GlobalScale); } }

        public Vector2 LocalScale { get; set; } = Vector2.One;
        public Vector2 GlobalScale
        {
            get
            {
                var parents = GetParents();
                if (parents.Any())
                {
                    return parents.Select(rt => rt.LocalScale).Aggregate((parent, child) => parent * child) * LocalScale;
                }
                else
                {
                    return LocalScale;
                }
            }
        }
        /// <summary>
        /// Relative to the anchor point. Calculated away from the anchor point.
        /// Note that the offset is still in pixels. Only the direction of the offset is relative, not the amount!
        /// </summary>
        public Point RelativeOffset { get; set; } = Point.Zero;
        /// <summary>
        /// Absolute, screen space offset. From top left corner.
        /// </summary>
        public Point AbsoluteOffset { get; set; } = Point.Zero;
        /// <summary>
        /// Calculated from the selected pivot.
        /// </summary>
        public Point PivotOffset { get; private set; }
        public Point AnchorPoint { get; private set; }

        public Point TopLeft { get { return AnchorPoint + PivotOffset + CorrectedOffset + AbsoluteOffset; } }
        public Rectangle Rect { get { return new Rectangle(TopLeft, ScaledSize); } }
        public Rectangle ParentRect { get { return Parent != null ? Parent.Rect : new Rectangle(0, 0, GameMain.GraphicsWidth, GameMain.GraphicsHeight); } }

        /// <summary>
        /// Calculates the offset from the relative offset, so that the offset is always away from the anchor point.
        /// </summary>
        protected Point CorrectedOffset
        {
            get
            {
                switch (Anchor)
                {
                    case Anchor.BottomRight:
                        return RelativeOffset.Inverse();
                    case Anchor.BottomLeft:
                    case Anchor.BottomCenter:
                        return new Point(RelativeOffset.X, -RelativeOffset.Y);
                    case Anchor.TopRight:
                    case Anchor.CenterRight:
                        return new Point(-RelativeOffset.X, RelativeOffset.Y);
                    default:
                        return RelativeOffset;
                }
            }
        }

        private Pivot pivot;
        public Pivot Pivot
        {
            get { return pivot; }
            set
            {
                pivot = value;
                RecalculatePivotOffset(ScaledSize);
            }
        }

        private Anchor anchor;
        public Anchor Anchor
        {
            get { return anchor; }
            set
            {
                anchor = value;
                RecalculateAnchorPoint(ParentRect);
            }
        }
        #endregion

        #region Initialization
        public RectTransform(Vector2 relativeSize, RectTransform parent, Point? offset = null, Anchor anchor = Anchor.TopLeft, Pivot? pivot = null)
        {
            Init(parent, offset, anchor, pivot);
            RelativeSize = relativeSize;
        }

        public RectTransform(Point absoluteSize, RectTransform parent = null, Point? offset = null, Anchor anchor = Anchor.TopLeft, Pivot? pivot = null)
        {
            Init(parent, offset, anchor, pivot);
            NonScaledSize = absoluteSize;
        }

        private void Init(RectTransform parent = null, Point? offset = null, Anchor anchor = Anchor.TopLeft, Pivot? pivot = null)
        {
            Parent = parent;
            RelativeOffset = offset ?? Point.Zero;
            Anchor = anchor;
            Pivot = pivot ?? MatchPivotToAnchor(Anchor);
        }
        #endregion

        #region Calculations
        public Point CalculatePivot(Pivot pivot, Point size)
        {
            int width = size.X;
            int height = size.Y;
            switch (pivot)
            {
                case Pivot.TopLeft:
                    return Point.Zero;
                case Pivot.TopCenter:
                    return new Point(-width / 2, 0);
                case Pivot.TopRight:
                    return new Point(-width, 0);
                case Pivot.CenterLeft:
                    return new Point(0, -height / 2);
                case Pivot.Center:
                    return size.Divide(2).Inverse();
                case Pivot.CenterRight:
                    return new Point(-width, -height / 2);
                case Pivot.BottomLeft:
                    return new Point(0, -height);
                case Pivot.BottomCenter:
                    return new Point(-width / 2, -height);
                case Pivot.BottomRight:
                    return new Point(-width, -height);
                default:
                    throw new NotImplementedException(pivot.ToString());
            }
        }

        public Point CalculateAnchor(Anchor anchor, Rectangle parent)
        {
            switch (anchor)
            {
                case Anchor.TopLeft:
                    return parent.Location;
                case Anchor.TopCenter:
                    return new Point(parent.Center.X, parent.Top);
                case Anchor.TopRight:
                    return new Point(parent.Right, parent.Top);
                case Anchor.CenterLeft:
                    return new Point(parent.Left, parent.Center.Y);
                case Anchor.Center:
                    return parent.Center;
                case Anchor.CenterRight:
                    return new Point(parent.Right, parent.Center.Y);
                case Anchor.BottomLeft:
                    return new Point(parent.Left, parent.Bottom);
                case Anchor.BottomCenter:
                    return new Point(parent.Center.X, parent.Bottom);
                case Anchor.BottomRight:
                    return new Point(parent.Right, parent.Bottom);
                default:
                    throw new NotImplementedException(anchor.ToString());
            }
        }

        public void RecalculatePivotOffset(Point size)
        {
            PivotOffset = CalculatePivot(Pivot, size);
        }

        public void RecalculateAnchorPoint(Rectangle parentRect)
        {
            AnchorPoint = CalculateAnchor(Anchor, parentRect);
        }

        public void RecalculateRelativeSize(Rectangle parentRect)
        {
            relativeSize = new Vector2(NonScaledSize.X, NonScaledSize.Y) / new Vector2(parentRect.Width, parentRect.Height);
        }

        public void RecalculateAbsoluteSize(Rectangle parentRect)
        {
            nonScaledSize = parentRect.Size.Multiply(relativeSize);
        }
        #endregion

        #region Instance methods
        public void RecalculateAll(bool resize, bool withChildren = true)
        {
            if (resize)
            {
                RecalculateAbsoluteSize(ParentRect);
            }
            RecalculateAnchorPoint(ParentRect);
            RecalculatePivotOffset(ScaledSize);
            if (withChildren)
            {
                RecalculateChildren(resize);
            }
        }

        public void RecalculateChildren(bool resize)
        {
            foreach (var child in children)
            {
                child.RecalculateAll(resize, withChildren: true);
            }
        }

        public void SetPosition(Anchor anchor, Pivot? pivot = null)
        {
            Anchor = anchor;
            Pivot = pivot ?? MatchPivotToAnchor(anchor);
            AbsoluteOffset = Point.Zero;
            RecalculateChildren(false);
        }

        public void Resize(Point newSize)
        {
            NonScaledSize = newSize;
            RecalculateAnchorPoint(ParentRect);
            RecalculatePivotOffset(ScaledSize);
            RecalculateChildren(true);
        }

        // TODO: After the scale changes, the children sizes are calculated too small.
        public void ChangeScale(Vector2 newScale)
        {
            LocalScale = newScale;
            RecalculateAnchorPoint(ParentRect);
            RecalculatePivotOffset(ScaledSize);
            RecalculateChildren(false);
        }

        /// <summary>
        /// Manipulates AbsoluteOffset.
        /// </summary>
        public void Translate(Point translation)
        {
            AbsoluteOffset += translation;
            RecalculateChildren(false);
        }

        public IEnumerable<RectTransform> GetParents()
        {
            var parents = new List<RectTransform>();
            if (Parent != null)
            {
                parents.Add(Parent);
                return parents.Concat(Parent.GetParents());
            }
            else
            {
                return parents;
            }
        }
        #endregion

        #region Static methods
        public static Pivot MatchPivotToAnchor(Anchor anchor)
        {
            if (!Enum.TryParse(anchor.ToString(), out Pivot pivot))
            {
                throw new Exception($"[RectTransform] Cannot match pivot to anchor {anchor}");
            }
            return pivot;
        }
        #endregion
    }
}
