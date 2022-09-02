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
            outmsg.WriteByte((byte)ServerPacketHeader.EVENTACTION);
            outmsg.WriteByte((byte)EventManager.NetworkEventType.STATUSEFFECT);
            outmsg.WriteIdentifier(ParentEvent.Prefab.Identifier);
            outmsg.WriteUInt16((UInt16)actionIndex);
            outmsg.WriteUInt16((UInt16)targets.Count());
            foreach (Entity target in targets)
            {
                outmsg.WriteUInt16(target.ID);
            }
            foreach (Client c in GameMain.Server.ConnectedClients)
            {
                GameMain.Server.ServerPeer?.Send(outmsg, c.Connection, DeliveryMethod.Reliable);
            }
        }
    }
}