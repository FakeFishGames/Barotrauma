using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class GUILayoutGroup : GUIComponent
    {
        private bool isHorizontal;
        private int spacing;

        private Anchor childAnchor;
        public Anchor ChildAnchor
        {
            get { return childAnchor; }
            set
            {
                childAnchor = value;
                Recalculate();
            }
        }

        private int pos;

        public GUILayoutGroup(RectTransform rectT, bool isHorizontal = false, Anchor childAnchor = Anchor.TopLeft, int spacing = 0) : base(null, rectT)
        {
            this.isHorizontal = isHorizontal;
            this.childAnchor = childAnchor;
            this.spacing = spacing;
        }

        public override void AddChild(GUIComponent child)
        {
            child.IgnoreLayoutGroups = false;
            child.RectTransform.Parent = RectTransform;
            child.RectTransform.SetPosition(childAnchor);
            if (isHorizontal)
            {
                child.RectTransform.AbsoluteOffset = new Point(pos, 0);
                pos += child.Rect.Width + spacing;
            }
            else
            {
                child.RectTransform.AbsoluteOffset = new Point(0, pos);
                pos += child.Rect.Height + spacing;
            }
        }

        public override void RemoveChild(GUIComponent child)
        {
            base.RemoveChild(child);
            child.RectTransform.Parent = null;
            if (isHorizontal)
            {
                pos -= child.Rect.Width + spacing;
            }
            else
            {
                pos -= child.Rect.Height + spacing;
            }
        }

        public void Recalculate()
        {
            pos = 0;
            foreach (var child in RectTransform.Children)
            {
                if (child.GUIComponent.IgnoreLayoutGroups) { continue; }
                child.SetPosition(childAnchor);
                if (isHorizontal)
                {
                    child.AbsoluteOffset = new Point(pos, 0);
                    pos += child.Rect.Width + spacing;
                }
                else
                {
                    child.AbsoluteOffset = new Point(0, pos);
                    pos += child.Rect.Height + spacing;
                }
            }
        }
    }
}
