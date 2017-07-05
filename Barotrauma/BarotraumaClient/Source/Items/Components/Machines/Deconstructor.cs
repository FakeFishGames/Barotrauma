using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework.Graphics;

namespace Barotrauma.Items.Components
{
    partial class Deconstructor : Powered, IServerSerializable, IClientSerializable
    {
        GUIProgressBar progressBar;
        GUIButton activateButton;

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            GuiFrame.Draw(spriteBatch);
        }

        public override void AddToGUIUpdateList()
        {
            GuiFrame.AddToGUIUpdateList();
        }

        public override void UpdateHUD(Character character)
        {
            GuiFrame.Update((float)Timing.Step);
        }

        private bool ToggleActive(GUIButton button, object obj)
        {
            SetActive(!IsActive, Character.Controlled);

            currPowerConsumption = IsActive ? powerConsumption : 0.0f;

            if (GameMain.Server != null)
            {
                item.CreateServerEvent(this);
            }
            else if (GameMain.Client != null)
            {
                item.CreateClientEvent(this);
            }

            return true;
        }

        public void ClientWrite(NetBuffer msg, object[] extraData = null)
        {
            msg.Write(IsActive);
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            SetActive(msg.ReadBoolean());
        }
    }
}
