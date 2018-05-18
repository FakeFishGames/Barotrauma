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

        public GUILayoutGroup(RectTransform rectT, bool isHorizontal = false, Anchor childAnchor = Anchor.TopLeft, int spacing = 0) : base(null, rectT)
        {
            this.isHorizontal = isHorizontal;
            this.childAnchor = childAnchor;
            this.spacing = spacing;
            rectT.ChildrenChanged += (child) => Recalculate();
        }

        private bool needsToRecalculate;
        public void Recalculate()
        {
            //TODO: option to stretch the children to fit the layout group?
            int pos = 0;
            foreach (var child in RectTransform.Children)
            {
                if (child.GUIComponent == null)
                {
                    // GUIComponent not yet set -> evaluate on the next frame
                    // This happens when the event is launched in the RectTransform constructor, before the GUIComponent constructor is ready.
                    needsToRecalculate = true;
                    break;
                }
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

        protected override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (needsToRecalculate)
            {
                Recalculate();
                needsToRecalculate = false;
            }
        }
    }
}
