using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma.Items.Components
{
    partial class PowerContainer : Powered, IDrawableComponent, IServerSerializable, IClientSerializable
    {
        private GUIProgressBar chargeIndicator;
        private GUIScrollBar rechargeSpeedSlider;

        [Serialize(0.0f, true)]
        public float RechargeWarningIndicatorLow { get; set; }

        [Serialize(0.0f, true)]
        public float RechargeWarningIndicatorHigh { get; set; }

        public Vector2 DrawSize
        {
            //use the extents of the item as the draw size
            get { return Vector2.Zero; }
        }

        partial void InitProjSpecific()
        {
            if (GuiFrame == null) { return; }

            var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.75f, 0.75f), GuiFrame.RectTransform, Anchor.Center)
            {
                //RelativeOffset = new Vector2(0, 0.05f)
            }, style: null);

            var upperArea = new GUIFrame(new RectTransform(new Vector2(1, 0.4f), paddedFrame.RectTransform, Anchor.TopCenter), style: null);
            var lowerArea = new GUIFrame(new RectTransform(new Vector2(1, 0.6f), paddedFrame.RectTransform, Anchor.BottomCenter), style: null);

            var rechargeRateContainer = new GUIFrame(new RectTransform(new Vector2(1, 0.4f), upperArea.RectTransform), style: null);
            var rechargeLabel = new GUITextBlock(new RectTransform(new Vector2(0.4f, 0.0f), rechargeRateContainer.RectTransform, Anchor.CenterLeft),
                TextManager.Get("rechargerate"), textColor: GUI.Style.TextColor, font: GUI.SubHeadingFont, textAlignment: Alignment.CenterLeft);
            string kW = TextManager.Get("kilowatt");
            var rechargeText = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1), rechargeRateContainer.RectTransform, Anchor.CenterRight),
                "", textColor: GUI.Style.TextColor, font: GUI.Font, textAlignment: Alignment.CenterRight)
            {
                TextGetter = () => $"{(int)MathF.Round(currPowerConsumption)} {kW} ({(int)MathF.Round(RechargeRatio * 100)} %)"
            };
            if (rechargeText.TextSize.X > rechargeText.Rect.Width) { rechargeText.Font = GUI.SmallFont; }

            var rechargeSliderContainer = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.4f), upperArea.RectTransform, Anchor.BottomCenter));

            if (RechargeWarningIndicatorLow > 0.0f || RechargeWarningIndicatorHigh > 0.0f)
            {
                var rechargeSliderFill = new GUICustomComponent(new RectTransform(new Vector2(0.95f, 0.9f), rechargeSliderContainer.RectTransform, Anchor.Center), (SpriteBatch sb, GUICustomComponent c) =>
                {
                    if (RechargeWarningIndicatorLow > 0.0f)
                    {
                        float warningLow = c.Rect.Width * RechargeWarningIndicatorLow;
                        GUI.DrawRectangle(sb, new Vector2(c.Rect.X + warningLow, c.Rect.Y), new Vector2(c.Rect.Width - warningLow, c.Rect.Height), GUI.Style.Orange, isFilled: true);
                    }
                    if (RechargeWarningIndicatorHigh > 0.0f)
                    {
                        float warningHigh = c.Rect.Width * RechargeWarningIndicatorHigh;
                        GUI.DrawRectangle(sb, new Vector2(c.Rect.X + warningHigh, c.Rect.Y), new Vector2(c.Rect.Width - warningHigh, c.Rect.Height), GUI.Style.Red, isFilled: true);
                    }
                });
            }

            rechargeSpeedSlider = new GUIScrollBar(new RectTransform(Vector2.One, rechargeSliderContainer.RectTransform, Anchor.Center), 
                barSize: 0.15f, style: "DeviceSliderSeeThrough")
            {
                Step = 0.1f,
                OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
                {
                    float newRechargeSpeed = maxRechargeSpeed * barScroll;
                    if (Math.Abs(newRechargeSpeed - rechargeSpeed) < 0.1f) { return false; }

                    RechargeSpeed = newRechargeSpeed;
                    if (GameMain.Client != null)
                    {
                        item.CreateClientEvent(this);
                        correctionTimer = CorrectionDelay;
                    }
                    return true;
                }
            };
            rechargeSpeedSlider.Bar.RectTransform.MaxSize = new Point(rechargeSpeedSlider.Bar.Rect.Height);
            
            // lower area --------------------------

            var chargeTextContainer = new GUIFrame(new RectTransform(new Vector2(1, 0.4f), lowerArea.RectTransform), style: null);
            var chargeLabel = new GUITextBlock(new RectTransform(new Vector2(0.4f, 0.0f), chargeTextContainer.RectTransform, Anchor.CenterLeft),
                TextManager.Get("charge"), textColor: GUI.Style.TextColor, font: GUI.SubHeadingFont, textAlignment: Alignment.CenterLeft)
            {
                ToolTip = TextManager.Get("PowerTransferTipPower")
            };
            string kWmin = TextManager.Get("kilowattminute");
            var chargeText = new GUITextBlock(new RectTransform(new Vector2(0.6f, 1), chargeTextContainer.RectTransform, Anchor.CenterRight), 
                "", textColor: GUI.Style.TextColor, font: GUI.Font, textAlignment: Alignment.CenterRight)
            {
                TextGetter = () => $"{(int)MathF.Round(charge)}/{(int)capacity} {kWmin} ({(int)MathF.Round(MathUtils.Percentage(charge, capacity))} %)"
            };
            if (chargeText.TextSize.X > chargeText.Rect.Width) { chargeText.Font = GUI.SmallFont; }

            chargeIndicator = new GUIProgressBar(new RectTransform(new Vector2(1.1f, 0.5f), lowerArea.RectTransform, Anchor.BottomCenter)
            {
                RelativeOffset = new Vector2(0, 0.1f)
            }, barSize: 0.0f, style: "DeviceProgressBar")
            {
                ProgressGetter = () =>
                {
                    return capacity <= 0.0f ? 1.0f : charge / capacity;
                }
            };
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            if (rechargeSpeedSlider != null)
            {
                rechargeSpeedSlider.BarScroll = rechargeSpeed / MaxRechargeSpeed;
            }
        }
        
        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            if (chargeIndicator != null)
            {
                float chargeRatio = charge / capacity;
                chargeIndicator.Color = ToolBox.GradientLerp(chargeRatio, Color.Red, Color.Orange, Color.Green);
            }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing = false, float itemDepth = -1)
        {
            if (indicatorSize.X <= 1.0f || indicatorSize.Y <= 1.0f) { return; }

            Vector2 itemSize = new Vector2(item.Sprite.SourceRect.Width, item.Sprite.SourceRect.Height) * item.Scale;
            Vector2 indicatorPos = -itemSize / 2 + indicatorPosition * item.Scale;
            if (item.FlippedX && item.Prefab.CanSpriteFlipX) { indicatorPos.X = -indicatorPos.X - indicatorSize.X * item.Scale; }
            if (item.FlippedY && item.Prefab.CanSpriteFlipY) { indicatorPos.Y = -indicatorPos.Y - indicatorSize.Y * item.Scale; }

            if (charge > 0 && capacity > 0)
            {
                float chargeRatio = MathHelper.Clamp(charge / capacity, 0.0f, 1.0f);
                Color indicatorColor = ToolBox.GradientLerp(chargeRatio, Color.Red, Color.Orange, Color.Green);
                if (!isHorizontal)
                {
                    GUI.DrawRectangle(spriteBatch,
                        new Vector2(item.DrawPosition.X, -item.DrawPosition.Y + ((indicatorSize.Y * item.Scale) * (1.0f - chargeRatio))) + indicatorPos,
                        new Vector2(indicatorSize.X * item.Scale, (indicatorSize.Y * item.Scale) * chargeRatio), indicatorColor, true,
                        depth: item.SpriteDepth - 0.00001f);
                }
                else
                {
                    GUI.DrawRectangle(spriteBatch,
                        new Vector2(item.DrawPosition.X, -item.DrawPosition.Y) + indicatorPos,
                        new Vector2((indicatorSize.X * item.Scale) * chargeRatio, indicatorSize.Y * item.Scale), indicatorColor, true, 
                        depth: item.SpriteDepth - 0.00001f);
                }
            }
            GUI.DrawRectangle(spriteBatch,
                new Vector2(item.DrawPosition.X, -item.DrawPosition.Y) + indicatorPos,
                indicatorSize * item.Scale, Color.Black, depth: item.SpriteDepth - 0.000015f);
        }

        public void ClientWrite(IWriteMessage msg, object[] extraData)
        {
            msg.WriteRangedInteger((int)(rechargeSpeed / MaxRechargeSpeed * 10), 0, 10);
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            if (correctionTimer > 0.0f)
            {
                StartDelayedCorrection(type, msg.ExtractBits(4 + 8), sendingTime);
                return;
            }

            float rechargeRate = msg.ReadRangedInteger(0, 10) / 10.0f;
            RechargeSpeed = rechargeRate * MaxRechargeSpeed;
#if CLIENT
            if (rechargeSpeedSlider != null)
            {
                rechargeSpeedSlider.BarScroll = rechargeRate;
            }
#endif
            Charge = msg.ReadRangedSingle(0.0f, 1.0f, 8) * capacity;
        }
    }
}
