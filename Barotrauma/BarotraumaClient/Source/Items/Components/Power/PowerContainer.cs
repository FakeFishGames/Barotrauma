using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma.Items.Components
{
    partial class PowerContainer : Powered, IDrawableComponent, IServerSerializable, IClientSerializable
    {
        private GUIProgressBar chargeIndicator;
        private GUIScrollBar rechargeSpeedSlider;

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
                    if (GameMain.Server != null)
                    {
                        item.CreateServerEvent(this);
                        GameServer.Log(Character.Controlled.LogName + " set the recharge speed of " + item.Name + " to " + (int)((rechargeSpeed / maxRechargeSpeed) * 100.0f) + " %", ServerLog.MessageType.ItemInteraction);
                    }
                    else if (GameMain.Client != null)
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
                    return charge / capacity;
                }
            };
        }

        public override bool Select(Character character)
        {
            rechargeSpeedSlider.BarScroll = rechargeSpeed / MaxRechargeSpeed;
            return base.Select(character);
        }

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            float chargeRatio = charge / capacity;
            chargeIndicator.Color = ToolBox.GradientLerp(chargeRatio, Color.Red, Color.Orange, Color.Green);
        }

        public void Draw(SpriteBatch spriteBatch, bool editing = false)
        {
            //TODO: proper sprite for the battery recharge indicator

            GUI.DrawRectangle(spriteBatch,
                new Vector2(item.DrawPosition.X - 4, -item.DrawPosition.Y),
                new Vector2(8, 22), Color.Black);

            if (charge > 0)
                GUI.DrawRectangle(spriteBatch,
                    new Vector2(item.DrawPosition.X - 3, -item.DrawPosition.Y + 1 + (20.0f * (1.0f - charge / capacity))),
                    new Vector2(6, 20 * (charge / capacity)), Color.Green, true);
        }
        
        public void ClientWrite(NetBuffer msg, object[] extraData)
        {
            msg.WriteRangedInteger(0, 10, (int)(rechargeSpeed / MaxRechargeSpeed * 10));
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            if (correctionTimer > 0.0f)
            {
                StartDelayedCorrection(type, msg.ExtractBits(4 + 8), sendingTime);
                return;
            }

            RechargeSpeed = msg.ReadRangedInteger(0, 10) / 10.0f * maxRechargeSpeed;
            Charge = msg.ReadRangedSingle(0.0f, 1.0f, 8) * capacity;
        }
    }
}
