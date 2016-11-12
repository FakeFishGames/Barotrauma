using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    class ClientEntityEventManager : NetEntityEventManager
    {
        private List<ClientEntityEvent> events;

        private UInt32 ID;

        private GameClient thisClient;

        public ClientEntityEventManager(GameClient client) { }

        public void CreateEvent(IClientSerializable entity)
        {
            if (!(entity is Entity))
            {
                DebugConsole.ThrowError("Can't create an entity event for " + entity + "!");
                return;
            }

            ID++;
            events.Add(new ClientEntityEvent(entity, ID));
        }

        public void Write(NetOutgoingMessage msg)
        {
            var eventsToSync = events.SkipWhile(e => e.ID >= thisClient.LastSentEntityEventID).ToList();
            if (eventsToSync.Count == 0) return;

            Write(msg, eventsToSync.Cast<NetEntityEvent>().ToList());
        }

        protected override void WriteEvent(NetBuffer buffer, NetEntityEvent entityEvent, Client recipient = null)
        {
            var clientEvent = entityEvent as ClientEntityEvent;
            if (clientEvent == null) return;

            clientEvent.Write(buffer);
        }

        protected override void ReadEvent(NetIncomingMessage buffer, INetSerializable entity, float sendingTime, Client sender = null)
        {
            var clientEntity = entity as IClientSerializable;
            if (clientEntity == null) return;

            clientEntity.ServerRead(buffer, sender);
        }
    }
}
