using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma
{
    public class GUIProgressBar : GUIComponent
    {
        private bool isHorizontal;

        private GUIFrame frame, slider;
        private float barSize;
        private bool showFrame;
                
        public delegate float ProgressGetterHandler();
        public ProgressGetterHandler ProgressGetter;

        public bool IsHorizontal
        {
            get { return isHorizontal; }
            set { isHorizontal = value; }
        }

        public float BarSize
        {
            get { return barSize; }
            set
            {
                if (!MathUtils.IsValid(value))
                {
                    GameAnalyticsManager.AddErrorEventOnce(
                        "GUIProgressBar.BarSize_setter", 
                        GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Attempted to set the BarSize of a GUIProgressBar to an invalid value (" + value + ")\n" + Environment.StackTrace);
                    return;
                }
                barSize = MathHelper.Clamp(value, 0.0f, 1.0f);
                //UpdateRect();
            }
        }
        
        public GUIProgressBar(RectTransform rectT, float barSize, Color? color = null, string style = "", bool showFrame = true) : base(style, rectT)
        {
            if (color.HasValue)
            {
                this.color = color.Value;
            }
            isHorizontal = (Rect.Width > Rect.Height);
            frame = new GUIFrame(new RectTransform(Vector2.One, rectT));
            GUI.Style.Apply(frame, "", this);
            slider = new GUIFrame(new RectTransform(Vector2.One, rectT));
            GUI.Style.Apply(slider, "Slider", this);
            this.showFrame = showFrame;
            this.barSize = barSize;
            Enabled = true;
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) { return; }

            if (ProgressGetter != null)
            {
                float newSize = MathHelper.Clamp(ProgressGetter(), 0.0f, 1.0f);
                if (!MathUtils.IsValid(newSize))
                {
                    GameAnalyticsManager.AddErrorEventOnce(
                        "GUIProgressBar.Draw:GetProgress",
                        GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "ProgressGetter of a GUIProgressBar (" + ProgressGetter.Target.ToString() + " - " + ProgressGetter.Method.ToString() + ") returned an invalid value (" + newSize + ")\n" + Environment.StackTrace);
                }
                else
                {
                    BarSize = newSize;
                }
            }

            Rectangle sliderRect = new Rectangle(
                    frame.Rect.X + (int)style.Padding.X,
                    (int)(frame.Rect.Y + (int)style.Padding.Y + (isHorizontal ? 0 : frame.Rect.Height * (1.0f - barSize))),
                    isHorizontal ? (int)((frame.Rect.Width - style.Padding.X - style.Padding.Z) * barSize) : frame.Rect.Width,
                    isHorizontal ? (int)(frame.Rect.Height - style.Padding.Y - style.Padding.W) : (int)(frame.Rect.Height * barSize));

            sliderRect.Width = Math.Max(sliderRect.Width, 1);
            sliderRect.Height = Math.Max(sliderRect.Height, 1);

            slider.RectTransform.AbsoluteOffset = new Point((int)style.Padding.X, (int)style.Padding.Y);
            slider.RectTransform.MaxSize = new Point(
                (int)(Rect.Width - style.Padding.X + style.Padding.Z), 
                (int)(Rect.Height - style.Padding.Y + style.Padding.W));
            frame.Visible = showFrame;
            slider.Visible = true;

            if (showFrame)
            {
                if (AutoDraw)
                {
                    frame.DrawAuto(spriteBatch);
                }
                else
                {
                    frame.DrawManually(spriteBatch);
                }
            }

            Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
            if (BarSize <= 1.0f)
            {
                spriteBatch.End();
                spriteBatch.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(prevScissorRect, sliderRect);
                spriteBatch.Begin(SpriteSortMode.Deferred, samplerState: GUI.SamplerState, rasterizerState: GameMain.ScissorTestEnable);
            }

            Color currColor = GetColor(State);

            slider.Color = currColor;
            if (AutoDraw)
            {
                slider.DrawAuto(spriteBatch);
            }
            else
            {
                slider.DrawManually(spriteBatch);
            }
            //hide the slider, we've already drawn it manually
            frame.Visible = false;
            slider.Visible = false;
            if (BarSize <= 1.0f)
            {
                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Deferred, rasterizerState: GameMain.ScissorTestEnable);
                spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
            }
        }
    }
}
