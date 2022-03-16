using Barotrauma.Networking;
using System;

namespace Barotrauma.Items.Components
{
    partial class DockingPort : ItemComponent, IDrawableComponent, IServerSerializable
    {
        public void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData extraData = null)
        {
            msg.Write(docked);

            if (docked)
            {
                msg.Write(DockingTarget.item.ID);
                msg.Write(IsLocked);
            }
        }
    }
}
