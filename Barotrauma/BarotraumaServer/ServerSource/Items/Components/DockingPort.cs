using Barotrauma.Networking;
using System;

namespace Barotrauma.Items.Components
{
    partial class DockingPort : ItemComponent, IDrawableComponent, IServerSerializable
    {
        private UInt16 originalDockingTargetID;

        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            msg.Write(docked);

            if (docked)
            {
                msg.Write(originalDockingTargetID);
                msg.Write(IsLocked);
            }
        }
    }
}
