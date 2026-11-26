using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    /// <summary>
    /// A scrollable area with both vertical and horizontal scrollbars.
    /// Add child elements to the <see cref="Content"/> property.
    /// </summary>
    public sealed class GUIDualScrollArea : GUIFrame
    {
        public readonly GUIFrame Content;

        public readonly GUIScrollBar VerticalScrollBar;
        public readonly GUIScrollBar HorizontalScrollBar;

        private readonly GUIScissorComponent scissor;
        private Rectangle childTotalRect;

        public Rectangle ScrollAreaRect => scissor.Rect;

        public bool AllowScroll = true;

        private CoroutineHandle scrollCoroutine;

        public GUIDualScrollArea(RectTransform rectT, string style = "") : base(rectT, style)
        {
            scissor = new GUIScissorComponent(new RectTransform(Vector2.One, rectT));
            Content = new GUIFrame(new RectTransform(Vector2.One, scissor.Content.RectTransform), style: null)
            {
                ClampMouseRectToGUIComponent = scissor,
                CanBeFocused = false
            };
            childTotalRect = Content.Rect;

            VerticalScrollBar = new GUIScrollBar(new RectTransform((0f, 1f), rectT, Anchor.TopRight), barSize: 0.1f) { BarSize = 1f };
            HorizontalScrollBar = new GUIScrollBar(new RectTransform((1f, 0f), rectT, Anchor.BottomLeft), barSize: 0.1f, isHorizontal: true) { BarSize = 1f };

            int scrollBarSize = GUIListBox.GetScrollBarSize(rectT.Rect);

            VerticalScrollBar.RectTransform.MinSize = new Point(scrollBarSize, 0);
            HorizontalScrollBar.RectTransform.MinSize = new Point(0, scrollBarSize);
            VerticalScrollBar.RectTransform.NonScaledSize -= new Point(0, scrollBarSize);
            HorizontalScrollBar.RectTransform.NonScaledSize -= new Point(scrollBarSize, 0);
            scissor.RectTransform.NonScaledSize -= new Point(scrollBarSize, scrollBarSize);

            // offset the area by 1 pixel so the background border shows in top left
            scissor.RectTransform.NonScaledSize -= new Point(1);
            scissor.RectTransform.AbsoluteOffset += new Point(1);

            rectT.ChildrenChanged += changed =>
            {
                if (changed == scissor.RectTransform
                    || changed == VerticalScrollBar.RectTransform
                    || changed == HorizontalScrollBar.RectTransform) { return; }

               throw new InvalidOperationException($"Children were added to {nameof(GUIDualScrollArea)}. Add them to {nameof(GUIDualScrollArea)}.{nameof(Content)} instead.");
            };
            Content.RectTransform.SizeChanged += Recalculate;
            Content.RectTransform.ScaleChanged += Recalculate;
            Recalculate();
        }

        public void Scroll(float horizontal, float vertical)
        {
            float clampedHorizontal = Math.Clamp(horizontal, min: 0, max: 1f);
            float clampedVertical = Math.Clamp(vertical, min: 0, max: 1f);

            HorizontalScrollBar.BarScroll = clampedHorizontal;
            ScrollHorizontally(clampedHorizontal);

            VerticalScrollBar.BarScroll = clampedVertical;
            ScrollVertically(clampedVertical);
        }

        /// <summary>
        /// Scrolls the area so the specified element is in the middle of the view.
        /// </summary>
        public void ScrollToElement(GUIComponent component, float duration = 0.0f)
        {
            if (duration <= 0.0f)
            {
                PerformScroll(component, HorizontalScrollBar.BarScroll, VerticalScrollBar.BarScroll, 1.0f);
                PerformScroll(component, HorizontalScrollBar.BarScroll, VerticalScrollBar.BarScroll, 1.0f);
            }
            else
            {
                if (scrollCoroutine != null && CoroutineManager.IsCoroutineRunning(scrollCoroutine))
                {
                    CoroutineManager.StopCoroutines(scrollCoroutine);
                }
                scrollCoroutine = CoroutineManager.StartCoroutine(ScrollCoroutine(component, duration));
            }
        }

        IEnumerable<CoroutineStatus> ScrollCoroutine(GUIComponent component, float duration)
        {
            float t = 0.0f;
            while (t < duration)
            {
                PerformScroll(component, HorizontalScrollBar.BarScroll, VerticalScrollBar.BarScroll, t);
                t += CoroutineManager.DeltaTime;
                yield return CoroutineStatus.Running;
            }
            PerformScroll(component, HorizontalScrollBar.BarScroll, VerticalScrollBar.BarScroll, 1.0f);
            yield return CoroutineStatus.Success;
        }

        private void PerformScroll(GUIComponent c, float startScrollX, float startScrollY, float t)
        {
            // Calculate the target position to center the component in the scroll area
            float targetScrollX = (c.Rect.Center.X - Content.Rect.X) - (ScrollAreaRect.Width / 2f);
            float targetScrollY = (c.Rect.Center.Y - Content.Rect.Y) - (ScrollAreaRect.Height / 2f);

            // Normalize the target scroll values to a range of 0 to 1
            targetScrollX = Math.Clamp(targetScrollX / Math.Max(1, childTotalRect.Width - ScrollAreaRect.Width), 0f, 1f);
            targetScrollY = Math.Clamp(targetScrollY / Math.Max(1, childTotalRect.Height - ScrollAreaRect.Height), 0f, 1f);

            HorizontalScrollBar.BarScroll = MathHelper.Lerp(startScrollX, targetScrollX, t);
            ScrollHorizontally(HorizontalScrollBar.BarScroll);

            VerticalScrollBar.BarScroll = MathHelper.Lerp(startScrollY, targetScrollY, t);
            ScrollVertically(VerticalScrollBar.BarScroll);
        }

        public void Recalculate()
        {
            Rectangle rect = Content.Rect;
            foreach (GUIComponent child in Content.GetAllChildren())
            {
                child.ClampMouseRectToGUIComponent = scissor;
                rect = Rectangle.Union(rect, child.Rect);
            }

            childTotalRect = rect;
            VerticalScrollBar.BarSize = Math.Clamp((float)scissor.Rect.Height / childTotalRect.Height, min: 0.01f, max: 1f);
            HorizontalScrollBar.BarSize = Math.Clamp((float)scissor.Rect.Width / childTotalRect.Width, min: 0.01f, max: 1f);

            VerticalScrollBar.Enabled = !MathUtils.NearlyEqual(VerticalScrollBar.BarSize, 1f);
            HorizontalScrollBar.Enabled = !MathUtils.NearlyEqual(HorizontalScrollBar.BarSize, 1f);

            HorizontalScrollBar.OnMoved = (_, scroll) =>
            {
                ScrollHorizontally(scroll);
                return true;
            };

            VerticalScrollBar.OnMoved = (_, scroll) =>
            {
                ScrollVertically(scroll);
                return true;
            };
        }

        private void ScrollVertically(float scroll)
            => Content.RectTransform.AbsoluteOffset = new Point(Content.RectTransform.AbsoluteOffset.X, (int)(-scroll * (childTotalRect.Height - Content.Rect.Height)));

        private void ScrollHorizontally(float scroll)
            => Content.RectTransform.AbsoluteOffset = new Point((int)(-scroll * (childTotalRect.Width - Content.Rect.Width)), Content.RectTransform.AbsoluteOffset.Y);

        public void ScrollArea(Vector2 offset)
        {
            Vector2 newOffset = Content.RectTransform.AbsoluteOffset.ToVector2() + offset;

            newOffset = Vector2.Clamp(newOffset, min: -(childTotalRect.Size.ToVector2() - ScrollAreaRect.Size.ToVector2()) , max: Vector2.Zero);
            Content.RectTransform.AbsoluteOffset = newOffset.ToPoint();

            HorizontalScrollBar.BarScroll = newOffset.X / -(childTotalRect.Width - Content.Rect.Width);
            VerticalScrollBar.BarScroll = newOffset.Y / -(childTotalRect.Height - Content.Rect.Height);
        }

        protected override void Update(float deltaTime)
        {
            if (!GUI.IsMouseOn(this)) { return; }

            if (AllowScroll && PlayerInput.ScrollWheelSpeed != 0)
            {
                if (PlayerInput.IsShiftDown())
                {
                    HorizontalScrollBar.BarScroll -= GetScrollStep(PlayerInput.ScrollWheelSpeed, HorizontalScrollBar);
                    ScrollHorizontally(HorizontalScrollBar.BarScroll);
                }
                else
                {
                    VerticalScrollBar.BarScroll -= GetScrollStep(PlayerInput.ScrollWheelSpeed, VerticalScrollBar);
                    ScrollVertically(VerticalScrollBar.BarScroll);
                }
            }

            if (AllowScroll && PlayerInput.HorizontalScrollWheelSpeed != 0)
            {
                HorizontalScrollBar.BarScroll -= GetScrollStep(PlayerInput.HorizontalScrollWheelSpeed, HorizontalScrollBar);
                ScrollHorizontally(HorizontalScrollBar.BarScroll);
            }

            return;

            static float GetScrollStep(int scrollWheelSpeed, GUIScrollBar scrollBar)
                => scrollWheelSpeed / 500f * scrollBar.BarSize;
        }
    }
}