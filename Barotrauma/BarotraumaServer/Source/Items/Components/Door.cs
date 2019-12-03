using Microsoft.Xna.Framework;
using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    partial class Door
    {
        partial void SetState(bool open, bool isNetworkMessage, bool sendNetworkMessage, bool forcedOpen)
        {
            if (IsStuck || isOpen == open)
            {
                return;
            }
            isOpen = open;

            //opening a partially stuck door makes it less stuck
            if (isOpen) stuck = MathHelper.Clamp(stuck - 30.0f, 0.0f, 100.0f);

            if (sendNetworkMessage)
            {
                GameMain.Server.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ComponentState, item.GetComponentIndex(this), forcedOpen });
            }
        }

        public override void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            base.ServerWrite(msg, c, extraData);

            msg.Write(isOpen);
            msg.Write(extraData.Length == 3 ? (bool)extraData[2] : false); //forced open
            msg.WriteRangedSingle(stuck, 0.0f, 100.0f, 8);
        }
    }
}
