using System;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Networking;
using Lidgren.Network;

namespace Barotrauma.Items.Components
{
    partial class PowerContainer : Powered, IDrawableComponent, IServerSerializable, IClientSerializable
    {
        partial void InitProjSpecific()
        {
            if (canBeSelected)
            {
                var button = new GUIButton(new Rectangle(160, 50, 30, 30), "-", "", GuiFrame);
                button.OnClicked = (GUIButton btn, object obj) =>
                {
                    RechargeSpeed = rechargeSpeed - maxRechargeSpeed * 0.1f;

                    if (GameMain.Server != null)
                    {
                        item.CreateServerEvent(this);
                        GameServer.Log(Character.Controlled + " set the recharge speed of " + item.Name + " to " + (int)((rechargeSpeed / maxRechargeSpeed) * 100.0f) + " %", ServerLog.MessageType.ItemInteraction);
                    }
                    else if (GameMain.Client != null)
                    {
                        item.CreateClientEvent(this);
                        correctionTimer = CorrectionDelay;
                    }

                    return true;
                };

                button = new GUIButton(new Rectangle(200, 50, 30, 30), "+", "", GuiFrame);
                button.OnClicked = (GUIButton btn, object obj) =>
                {
                    RechargeSpeed = rechargeSpeed + maxRechargeSpeed * 0.1f;

                    if (GameMain.Server != null)
                    {
                        item.CreateServerEvent(this);
                        GameServer.Log(Character.Controlled + " set the recharge speed of " + item.Name + " to " + (int)((rechargeSpeed / maxRechargeSpeed) * 100.0f) + " %", ServerLog.MessageType.ItemInteraction);
                    }
                    else if (GameMain.Client != null)
                    {
                        item.CreateClientEvent(this);
                        correctionTimer = CorrectionDelay;
                    }

                    return true;
                };
            }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing = false)
        {
            GUI.DrawRectangle(spriteBatch,
                new Vector2(item.DrawPosition.X - 4, -item.DrawPosition.Y),
                new Vector2(8, 22), Color.Black);

            if (charge > 0)
                GUI.DrawRectangle(spriteBatch,
                    new Vector2(item.DrawPosition.X - 3, -item.DrawPosition.Y + 1 + (20.0f * (1.0f - charge / capacity))),
                    new Vector2(6, 20 * (charge / capacity)), Color.Green, true);
        }

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            GuiFrame.Draw(spriteBatch);

            int x = GuiFrame.Rect.X;
            int y = GuiFrame.Rect.Y;
            //GUI.DrawRectangle(spriteBatch, new Rectangle(x, y, width, height), Color.Black, true);

            GUI.Font.DrawString(spriteBatch,
                "Charge: " + (int)charge + "/" + (int)capacity + " kWm (" + (int)((charge / capacity) * 100.0f) + " %)",
                new Vector2(x + 30, y + 30), Color.White);

            GUI.Font.DrawString(spriteBatch, "Recharge rate: " + (int)((rechargeSpeed / maxRechargeSpeed) * 100.0f) + " %", new Vector2(x + 30, y + 95), Color.White);
        }

        public override void AddToGUIUpdateList()
        {
            GuiFrame.AddToGUIUpdateList();
        }

        public override void UpdateHUD(Character character)
        {
            GuiFrame.Update(1.0f / 60.0f);
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
