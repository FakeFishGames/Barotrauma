using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    class ServerEntityEventManager : NetEntityEventManager
    {
        private List<ServerEntityEvent> events;

        private UInt32 ID;

        public ServerEntityEventManager(GameServer server) { }

        public void CreateEvent(IServerSerializable entity)
        {
            if (!(entity is Entity))
            {
                DebugConsole.ThrowError("Can't create an entity event for " + entity + "!");
                return;
            }

            ID++;
            events.Add(new ServerEntityEvent(entity, ID));
        }

        /// <summary>
        /// Writes all the events that the client hasn't received yet into the outgoing message
        /// </summary>
        public void Write(Client client, NetOutgoingMessage msg)
        {
            var eventsToSync = events.SkipWhile(e => e.ID >= client.lastRecvEntityEventID).ToList();
            if (eventsToSync.Count == 0) return;

            Write(msg, eventsToSync.Cast<NetEntityEvent>().ToList(), client);
        }

        protected override void WriteEvent(NetBuffer buffer, NetEntityEvent entityEvent, Client recipient = null)
        {
            var serverEvent = entityEvent as ServerEntityEvent;
            if (serverEvent == null) return;

            serverEvent.Write(buffer, recipient);
        }

        protected override void ReadEvent(NetIncomingMessage buffer, INetSerializable entity, float sendingTime, Client sender = null)
        {
            var clientEntity = entity as IClientSerializable;
            if (clientEntity == null) return;

            clientEntity.ServerRead(buffer, sender);
        }
    }
}
