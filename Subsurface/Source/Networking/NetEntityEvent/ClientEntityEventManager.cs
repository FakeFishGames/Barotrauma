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

        public ClientEntityEventManager(GameClient client) 
        {
            events = new List<ClientEntityEvent>();
        }

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
            if (events.Count == 0) return;

            List<NetEntityEvent> eventsToSync = new List<NetEntityEvent>();
            for (int i = events.Count - 1; i >= 0 && events[i].ID > thisClient.LastSentEntityEventID; i--)
            {
                eventsToSync.Add(events[i]);
            }
            if (eventsToSync.Count == 0) return;

            Write(msg, eventsToSync);
        }

        protected override void WriteEvent(NetBuffer buffer, NetEntityEvent entityEvent, Client recipient = null)
        {
            var clientEvent = entityEvent as ClientEntityEvent;
            if (clientEvent == null) return;

            clientEvent.Write(buffer);
        }

        protected override void ReadEvent(NetIncomingMessage buffer, INetSerializable entity, float sendingTime, Client sender = null)
        {
            var serverEntity = entity as IServerSerializable;
            if (serverEntity == null) return;

            serverEntity.ClientRead(buffer, sendingTime);
        }

        public void Clear()
        {
            events.Clear();
        }
    }
}
