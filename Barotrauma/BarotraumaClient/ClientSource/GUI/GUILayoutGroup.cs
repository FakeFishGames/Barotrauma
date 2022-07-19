using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma
{
    public class GUILayoutGroup : GUIComponent
    {
        private bool isHorizontal;
        public bool IsHorizontal
        {
            get { return isHorizontal; }
            set
            {
                isHorizontal = value;
                needsToRecalculate = true;
            }
        }

        private bool stretch;
        /// <summary>
        /// Note that stretching cannot be undone, because the previous child sizes are not stored.
        /// </summary>
        public bool Stretch
        {
            get { return stretch; }
            set
            {
                stretch = value;
                needsToRecalculate = true;
            }
        }

        private int absoluteSpacing;
        public int AbsoluteSpacing
        {
            get { return absoluteSpacing; }
            set
            {
                absoluteSpacing = MathHelper.Clamp(value, 0, int.MaxValue);
                needsToRecalculate = true;
            }
        }

        private float relativeSpacing;
        public float RelativeSpacing
        {
            get { return relativeSpacing; }
            set
            {
                relativeSpacing = MathHelper.Clamp(value, -1, 1);
                needsToRecalculate = true;
            }
        }

        private Anchor childAnchor;
        public Anchor ChildAnchor
        {
            get { return childAnchor; }
            set
            {
                childAnchor = value;
                needsToRecalculate = true;
            }
        }

        private bool needsToRecalculate;
        public bool NeedsToRecalculate
        {
            get { return needsToRecalculate; }
            set 
            {
                if (value) { needsToRecalculate = true; }
            }
        }

        public GUILayoutGroup(RectTransform rectT, bool isHorizontal = false, Anchor childAnchor = Anchor.TopLeft) : base(null, rectT)
        {
            CanBeFocused = false;
            this.isHorizontal = isHorizontal;
            this.childAnchor = childAnchor;
            rectT.ChildrenChanged += (child) => needsToRecalculate = true;
            rectT.ScaleChanged += () => needsToRecalculate = true;
            rectT.SizeChanged += () => needsToRecalculate = true;
        }

        public void Recalculate()
        {
            float stretchFactor = 1.0f;
            if (stretch && RectTransform.Children.Count() > 0)
            {
                foreach (RectTransform child in RectTransform.Children)
                {
                    if (child.GUIComponent.IgnoreLayoutGroups) { continue; }

                    switch (child.ScaleBasis)
                    {
                        case ScaleBasis.BothHeight:
                        case ScaleBasis.Smallest when Rect.Height <= Rect.Width:
                        case ScaleBasis.Largest when Rect.Height > Rect.Width:
                            child.MinSize = new Point((int)((child.Rect.Height * child.RelativeSize.X) / child.RelativeSize.Y), child.MinSize.Y);
                            break;
                        case ScaleBasis.BothWidth:
                        case ScaleBasis.Smallest when Rect.Width <= Rect.Height:
                        case ScaleBasis.Largest when Rect.Width > Rect.Height:
                            child.MinSize = new Point(child.MinSize.X, (int)((child.Rect.Width * child.RelativeSize.Y) / child.RelativeSize.X));
                            break;
                    }
                }

                float minSize = RectTransform.Children
                    .Where(c => !c.GUIComponent.IgnoreLayoutGroups)
                    .Sum(c => isHorizontal ? (c.IsFixedSize ? c.NonScaledSize.X : c.MinSize.X) : (c.IsFixedSize ? c.NonScaledSize.Y : c.MinSize.Y));

                float totalSize = RectTransform.Children
                    .Where(c => !c.GUIComponent.IgnoreLayoutGroups)
                    .Sum(c => isHorizontal ?
                        (c.IsFixedSize ? c.Rect.Width : MathHelper.Clamp(c.Rect.Width, c.MinSize.X, c.MaxSize.X)) :
                        (c.IsFixedSize ? c.Rect.Height : MathHelper.Clamp(c.Rect.Height, c.MinSize.Y, c.MaxSize.Y)));

                float thisSize = (isHorizontal ? Rect.Width : Rect.Height);

                totalSize +=
                    (RectTransform.Children.Count(c => !c.GUIComponent.IgnoreLayoutGroups) - 1) * 
                    (absoluteSpacing + relativeSpacing * thisSize);

                stretchFactor = totalSize <= 0.0f || minSize >= thisSize || totalSize == minSize ? 
                    1.0f : 
                    (thisSize - minSize) / (totalSize - minSize);
            }

            int absPos = 0;
            float relPos = 0;
            foreach (var child in RectTransform.Children)
            {
                if (child.GUIComponent.IgnoreLayoutGroups) { continue; }

                float currentStretchFactor = child.ScaleBasis == ScaleBasis.Normal ? stretchFactor : 1.0f;
                child.SetPosition(childAnchor);

                void advancePositionsAndCalculateChildSizes(
                    ref int childNonScaledSize,
                    ref float childRelativeSize,
                    int childMinSize,
                    int childMaxSize,
                    int childRectSize,
                    int selfRectSize)
                {
                    if (child.IsFixedSize)
                    {
                        absPos += childNonScaledSize + absoluteSpacing;
                    }
                    else
                    {
                        absPos += (int)Math.Round(MathHelper.Clamp(childRectSize * currentStretchFactor, childMinSize, childMaxSize) + (absoluteSpacing * currentStretchFactor));
                        if (stretch)
                        {
                            float relativeSize =
                                MathF.Round(childRelativeSize * currentStretchFactor * selfRectSize) / selfRectSize;
                            childRelativeSize = relativeSize;
                        }
                    }
                }

                Point childNonScaledSize = child.NonScaledSize;
                Vector2 childRelativeSize = child.RelativeSize;
                if (isHorizontal)
                {
                    child.RelativeOffset = new Vector2(relPos, child.RelativeOffset.Y);
                    child.AbsoluteOffset = new Point(absPos, child.AbsoluteOffset.Y);
                    advancePositionsAndCalculateChildSizes(
                        ref childNonScaledSize.X,
                        ref childRelativeSize.X,
                        child.MinSize.X,
                        child.MaxSize.X,
                        child.Rect.Width,
                        Rect.Width);
                }
                else
                {
                    child.RelativeOffset = new Vector2(child.RelativeOffset.X, relPos);
                    child.AbsoluteOffset = new Point(child.AbsoluteOffset.X, absPos);
                    advancePositionsAndCalculateChildSizes(
                        ref childNonScaledSize.Y,
                        ref childRelativeSize.Y,
                        child.MinSize.Y,
                        child.MaxSize.Y,
                        child.Rect.Height,
                        Rect.Height);
                }
                child.NonScaledSize = childNonScaledSize;
                child.RelativeSize = childRelativeSize;
                relPos += relativeSpacing * stretchFactor;
                if (isHorizontal) { relPos = MathF.Round(relPos * Rect.Width) / Rect.Width; }
                else { relPos = MathF.Round(relPos * Rect.Height) / Rect.Height; }
            }
            needsToRecalculate = false;
        }

        protected override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (needsToRecalculate)
            {
                Recalculate();
            }
        }
    }
}
