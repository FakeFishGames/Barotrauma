using System;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma
{
    public class PanelAnimator
    {
        private readonly GUIScissorComponent container;
        
        private readonly GUIFrame leftFrame;
        private readonly GUIComponent middleFrame;
        private readonly GUIFrame rightFrame;

        private readonly GUIButton leftButton;
        private readonly GUIButton rightButton;

        private float leftAnimState = 1.0f;
        private float rightAnimState = 0.0f;

        public bool LeftEnabled
        {
            get => leftButton.Enabled;
            set => leftButton.Enabled = value;
        }
        public bool RightEnabled
        {
            get => rightButton.Enabled;
            set => rightButton.Enabled = value;
        }
        
        public bool LeftVisible = true;
        public bool RightVisible = false;
        
        public PanelAnimator(RectTransform rectTransform, GUIFrame leftFrame, GUIComponent middleFrame, GUIFrame rightFrame)
        {
            container = new GUIScissorComponent(rectTransform);
            
            this.leftFrame = leftFrame;
            this.middleFrame = middleFrame;
            this.rightFrame = rightFrame;

            void own(GUIComponent component)
            {
                component.RectTransform.Parent = container.Content.RectTransform;
                component.RectTransform.Anchor = Anchor.TopLeft;
                component.RectTransform.Pivot = Pivot.TopLeft;

                component.GetAllChildren<GUIDropDown>().ForEach(dd => dd.RefreshListBoxParent());
            }
            
            GUIButton makeButton(Action action)
                => new GUIButton(new RectTransform(new Vector2(0.01f, 1.0f), container.Content.RectTransform)
                        { MinSize = new Point(20, 0), MaxSize = new Point(int.MaxValue, (int)(150 * GUI.Scale)) },
                    style: "UIToggleButton")
                {
                    OnClicked = (_, __) =>
                    {
                        action();
                        return false;
                    }
                };
            
            own(leftFrame);
            this.leftButton = makeButton(() => LeftVisible = !LeftVisible);
            
            own(middleFrame);
            
            this.rightButton = makeButton(() => RightVisible = !RightVisible);
            own(rightFrame);
        }
        
        public void Update()
        {
            if (!LeftEnabled) { LeftVisible = false; }
            if (!RightEnabled) { RightVisible = false; }
            
            static void updateState(ref float state, bool visible)
                => state = MathHelper.Lerp(state, visible ? 0.0f : 1.0f, 0.5f);
            updateState(ref leftAnimState, LeftVisible);
            updateState(ref rightAnimState, RightVisible);

            static int width(GUIComponent c)
                => c.RectTransform.NonScaledSize.X;

            int height = container.RectTransform.NonScaledSize.Y;
            int buttonY = height/2 - leftButton.RectTransform.NonScaledSize.Y/2;
            
            leftFrame.RectTransform.AbsoluteOffset = new Point((int)(-width(leftFrame) * leftAnimState), 0);
            leftButton.RectTransform.AbsoluteOffset = leftFrame.RectTransform.AbsoluteOffset
                                                      + new Point(width(leftFrame), buttonY);
            leftButton.Children.ForEach(c => c.SpriteEffects = LeftVisible
                ? SpriteEffects.FlipHorizontally
                : SpriteEffects.None);
            
            rightFrame.RectTransform.AbsoluteOffset = new Point((int)(width(container) + width(rightFrame) * (rightAnimState-1f)), 0);
            rightButton.RectTransform.AbsoluteOffset = rightFrame.RectTransform.AbsoluteOffset
                                                       + new Point(-width(rightButton), buttonY);
            rightButton.Children.ForEach(c => c.SpriteEffects = RightVisible
                ? SpriteEffects.None
                : SpriteEffects.FlipHorizontally);

            middleFrame.RectTransform.AbsoluteOffset = new Point(
                leftButton.RectTransform.AbsoluteOffset.X + width(leftButton),
                0);
            middleFrame.RectTransform.NonScaledSize = new Point(
                rightButton.RectTransform.AbsoluteOffset.X - middleFrame.RectTransform.AbsoluteOffset.X,
                height);
        }
    }
}