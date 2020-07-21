using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class StatusEffectAction : EventAction
    {
        private void ServerWrite(IEnumerable<Entity> targets)
        {
            IWriteMessage outmsg = new WriteOnlyMessage();
            outmsg.Write((byte)ServerPacketHeader.EVENTACTION);
            outmsg.Write((byte)EventManager.NetworkEventType.STATUSEFFECT);
            outmsg.Write(ParentEvent.Prefab.Identifier);
            outmsg.Write((UInt16)actionIndex);
            outmsg.Write((UInt16)targets.Count());
            foreach (Entity target in targets)
            {
                outmsg.Write(target.ID);
            }
            foreach (Client c in GameMain.Server.ConnectedClients)
            {
                GameMain.Server.ServerPeer?.Send(outmsg, c.Connection, DeliveryMethod.Reliable);
            }
        }
    }
}