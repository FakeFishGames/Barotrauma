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

        public Vector2 DrawSize
        {
            //use the extents of the item as the draw size
            get { return Vector2.Zero; }
        }

        partial void InitProjSpecific()
        {
            if (GuiFrame == null) return;

            GUILayoutGroup paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.7f), GuiFrame.RectTransform, Anchor.Center))
                { RelativeSpacing = 0.1f, Stretch = true };

            string rechargeStr = TextManager.Get("PowerContainerRechargeRate");
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), paddedFrame.RectTransform), "RechargeRate", textAlignment: Alignment.Center)
            {
                TextGetter = () =>
                {
                    return rechargeStr.Replace("[rate]", ((int)((rechargeSpeed / maxRechargeSpeed) * 100.0f)).ToString());
                }
            };

            var sliderArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform, Anchor.BottomCenter), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            new GUITextBlock(new RectTransform(new Vector2(0.15f, 1.0f), sliderArea.RectTransform),
                "0 %", textAlignment: Alignment.Center);
            rechargeSpeedSlider = new GUIScrollBar(new RectTransform(new Vector2(0.8f, 1.0f), sliderArea.RectTransform), barSize: 0.25f, style: "GUISlider")
            {
                Step = 0.1f,
                OnMoved = (GUIScrollBar scrollBar, float barScroll) =>
                {
                    float newRechargeSpeed = maxRechargeSpeed * barScroll;
                    if (Math.Abs(newRechargeSpeed - rechargeSpeed) < 0.1f) return false;

                    RechargeSpeed = newRechargeSpeed;
                    if (GameMain.Client != null)
                    {
                        item.CreateClientEvent(this);
                        correctionTimer = CorrectionDelay;
                    }
                    return true;
                }
            };
            new GUITextBlock(new RectTransform(new Vector2(0.15f, 1.0f), sliderArea.RectTransform),
                "100 %", textAlignment: Alignment.Center);

            string chargeStr = TextManager.Get("PowerContainerCharge");
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), paddedFrame.RectTransform), "Charge", textAlignment: Alignment.Center)
            {
                TextGetter = () =>
                {
                    return chargeStr.Replace("[charge]", (int)charge + "/" + (int)capacity).Replace("[percentage]", ((int)((charge / capacity) * 100.0f)).ToString());
                }
            };

            chargeIndicator = new GUIProgressBar(new RectTransform(new Vector2(1.0f, 0.2f), paddedFrame.RectTransform), barSize: 0.0f)
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
            float chargeRatio = charge / capacity;
            chargeIndicator.Color = ToolBox.GradientLerp(chargeRatio, Color.Red, Color.Orange, Color.Green);
        }

        public void Draw(SpriteBatch spriteBatch, bool editing = false, float itemDepth = -1)
        {
            if (indicatorSize.X <= 1.0f || indicatorSize.Y <= 1.0f) return;

            GUI.DrawRectangle(spriteBatch,
                new Vector2(
                    item.DrawPosition.X - item.Sprite.SourceRect.Width / 2 * item.Scale + indicatorPosition.X * item.Scale,
                    -item.DrawPosition.Y - item.Sprite.SourceRect.Height / 2 * item.Scale + indicatorPosition.Y * item.Scale),
                indicatorSize * item.Scale, Color.Black, depth: item.SpriteDepth - 0.00001f);

            if (charge > 0)
            {
                Color indicatorColor = ToolBox.GradientLerp(charge / capacity, Color.Red, Color.Orange, Color.Green);
                if (!isHorizontal)
                {
                    GUI.DrawRectangle(spriteBatch,
                    new Vector2(
                        item.DrawPosition.X - item.Sprite.SourceRect.Width / 2 * item.Scale + indicatorPosition.X * item.Scale + 1,
                        -item.DrawPosition.Y - item.Sprite.SourceRect.Height / 2 * item.Scale + indicatorPosition.Y * item.Scale + 1 + ((indicatorSize.Y * item.Scale) * (1.0f - charge / capacity))),
                    new Vector2(indicatorSize.X * item.Scale - 2, (indicatorSize.Y * item.Scale - 2) * (charge / capacity)), indicatorColor, true, 
                    depth: item.SpriteDepth - 0.00001f);
                }
                else
                {
                    GUI.DrawRectangle(spriteBatch,
                    new Vector2(
                        item.DrawPosition.X - item.Sprite.SourceRect.Width / 2 * item.Scale + indicatorPosition.X * item.Scale + 1 ,
                        -item.DrawPosition.Y - item.Sprite.SourceRect.Height / 2 * item.Scale + indicatorPosition.Y * item.Scale + 1),
                    new Vector2((indicatorSize.X * item.Scale - 2) * (charge / capacity), indicatorSize.Y * item.Scale - 2), indicatorColor, true, 
                    depth: item.SpriteDepth - 0.00001f);
                }
            }

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
