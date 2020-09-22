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
                msg.Write(hulls != null && hulls[0] != null && hulls[1] != null && gap != null);
            }
        }
    }
}
