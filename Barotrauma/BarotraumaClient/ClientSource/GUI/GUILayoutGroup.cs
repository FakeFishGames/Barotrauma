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
                    if (child.ScaleBasis == ScaleBasis.BothHeight) { child.MinSize = new Point(child.Rect.Height, child.MinSize.Y); }
                    if (child.ScaleBasis == ScaleBasis.BothWidth) { child.MinSize = new Point(child.MinSize.X, child.Rect.Width); }
                    if (child.ScaleBasis == ScaleBasis.Smallest)
                    {
                        if (Rect.Width < Rect.Height)
                        {
                            child.MinSize = new Point(child.MinSize.X, child.Rect.Width);
                        }
                        else
                        {
                            child.MinSize = new Point(child.Rect.Height, child.MinSize.Y);
                        }
                    }
                    if (child.ScaleBasis == ScaleBasis.Largest)
                    {
                        if (Rect.Width > Rect.Height)
                        {
                            child.MinSize = new Point(child.MinSize.X, child.Rect.Width);
                        }
                        else
                        {
                            child.MinSize = new Point(child.Rect.Height, child.MinSize.Y);
                        }
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

                stretchFactor = totalSize <= 0.0f || minSize >= thisSize ? 
                    1.0f : 
                    (thisSize - minSize) / (totalSize - minSize);
            }

            int absPos = 0;
            float relPos = 0;
            foreach (var child in RectTransform.Children)
            {
                if (child.GUIComponent.IgnoreLayoutGroups) { continue; }
                child.SetPosition(childAnchor);
                if (isHorizontal)
                {
                    child.RelativeOffset = new Vector2(relPos, child.RelativeOffset.Y);
                    child.AbsoluteOffset = new Point(absPos, child.AbsoluteOffset.Y);
                    if (child.IsFixedSize)
                    {
                        absPos += child.NonScaledSize.X + absoluteSpacing;
                    }
                    else
                    {
                        absPos += (int)(MathHelper.Clamp(child.Rect.Width * stretchFactor, child.MinSize.X, child.MaxSize.X) + (absoluteSpacing * stretchFactor));
                        if (stretch)
                        {
                            child.RelativeSize = new Vector2(child.RelativeSize.X * stretchFactor, child.RelativeSize.Y);
                        }
                    }
                }
                else
                {
                    child.RelativeOffset = new Vector2(child.RelativeOffset.X, relPos);
                    child.AbsoluteOffset = new Point(child.AbsoluteOffset.X, absPos);
                    if (child.IsFixedSize)
                    {
                        absPos += child.NonScaledSize.Y + absoluteSpacing;
                    }
                    else
                    {
                        absPos += (int)(MathHelper.Clamp(child.Rect.Height * stretchFactor, child.MinSize.Y, child.MaxSize.Y) + (absoluteSpacing * stretchFactor));
                        if (stretch)
                        {
                            child.RelativeSize = new Vector2(child.RelativeSize.X, child.RelativeSize.Y * stretchFactor);
                        }
                    }
                }
                relPos += relativeSpacing * stretchFactor;
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
