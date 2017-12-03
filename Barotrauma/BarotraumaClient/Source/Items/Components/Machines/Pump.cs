using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    partial class Pump : Powered, IServerSerializable, IClientSerializable
    {
        private GUITickBox isActiveTickBox;

        partial void InitProjSpecific()
        {
            isActiveTickBox = new GUITickBox(new Rectangle(0, 0, 20, 20), "Running", Alignment.TopLeft, GuiFrame);
            isActiveTickBox.OnSelected = (GUITickBox box) =>
            {
                targetLevel = null;
                IsActive = !IsActive;
                if (!IsActive) currPowerConsumption = 0.0f;

                if (GameMain.Server != null)
                {
                    item.CreateServerEvent(this);
                    GameServer.Log(Character.Controlled + (IsActive ? " turned on " : " turned off ") + item.Name, ServerLog.MessageType.Set);
                }
                else if (GameMain.Client != null)
                {
                    correctionTimer = CorrectionDelay;
                    item.CreateClientEvent(this);
                }

                return true;
            };

            var button = new GUIButton(new Rectangle(160, 40, 35, 30), "OUT", "", GuiFrame);
            button.OnClicked = (GUIButton btn, object obj) =>
            {
                FlowPercentage -= 10.0f;

                if (GameMain.Server != null)
                {
                    item.CreateServerEvent(this);
                    GameServer.Log(Character.Controlled + " set the pumping speed of " + item.Name + " to " + (int)(flowPercentage) + " %", ServerLog.MessageType.Set);
                }
                else if (GameMain.Client != null)
                {
                    correctionTimer = CorrectionDelay;
                    item.CreateClientEvent(this);
                }

                return true;
            };

            button = new GUIButton(new Rectangle(210, 40, 35, 30), "IN", "", GuiFrame);
            button.OnClicked = (GUIButton btn, object obj) =>
            {
                FlowPercentage += 10.0f;

                if (GameMain.Server != null)
                {
                    item.CreateServerEvent(this);
                    GameServer.Log(Character.Controlled + " set the pumping speed of " + item.Name + " to " + (int)(flowPercentage) + " %", ServerLog.MessageType.Set);
                }
                else if (GameMain.Client != null)
                {
                    correctionTimer = CorrectionDelay;
                    item.CreateClientEvent(this);
                }

                return true;
            };
        }
        
        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            int x = GuiFrame.Rect.X;
            int y = GuiFrame.Rect.Y;

            GuiFrame.Draw(spriteBatch);

            GUI.Font.DrawString(spriteBatch, "Pumping speed: " + (int)flowPercentage + " %", new Vector2(x + 40, y + 85), Color.White);

        }

        public override void AddToGUIUpdateList()
        {
            GuiFrame.AddToGUIUpdateList();
        }

        public override void UpdateHUD(Character character)
        {
            GuiFrame.Update(1.0f / 60.0f);
        }

        public void ClientWrite(Lidgren.Network.NetBuffer msg, object[] extraData = null)
        {
            //flowpercentage can only be adjusted at 10% intervals -> no need for more accuracy than this
            msg.WriteRangedInteger(-10, 10, (int)(flowPercentage / 10.0f));
            msg.Write(IsActive);
        }

        public void ClientRead(ServerNetObject type, Lidgren.Network.NetBuffer msg, float sendingTime)
        {
            if (correctionTimer > 0.0f)
            {
                StartDelayedCorrection(type, msg.ExtractBits(5 + 1), sendingTime);
                return;
            }

            FlowPercentage = msg.ReadRangedInteger(-10, 10) * 10.0f;
            IsActive = msg.ReadBoolean();
        }
    }
}
