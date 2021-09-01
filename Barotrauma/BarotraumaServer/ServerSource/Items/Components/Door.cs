using Microsoft.Xna.Framework;
using Barotrauma.Networking;
using System;

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
            if (isOpen) { stuck = MathHelper.Clamp(stuck - StuckReductionOnOpen, 0.0f, 100.0f); }

            if (sendNetworkMessage)
            {
                GameMain.Server.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ComponentState, item.GetComponentIndex(this), forcedOpen });
            }
        }

        public override void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            base.ServerWrite(msg, c, extraData);

            msg.Write(isOpen);
            msg.Write(isBroken);
            msg.Write(extraData.Length == 3 ? (bool)extraData[2] : false); //forced open
            msg.Write(isStuck);
            msg.Write(isJammed);
            msg.WriteRangedSingle(stuck, 0.0f, 100.0f, 8);
            msg.Write(lastUser == null ? (UInt16)0 : lastUser.ID);
        }
    }
}
