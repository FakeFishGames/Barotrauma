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

        public ServerEntityEventManager(GameServer server) 
        {
            events = new List<ServerEntityEvent>();
        }

        public void CreateEvent(IServerSerializable entity, object[] extraData = null)
        {
            if (!(entity is Entity))
            {
                DebugConsole.ThrowError("Can't create an entity event for " + entity + "!");
                return;
            }

            ID++;

            var newEvent = new ServerEntityEvent(entity, ID);
            if (extraData != null) newEvent.SetData(extraData);

            events.Add(newEvent);
        }

        /// <summary>
        /// Writes all the events that the client hasn't received yet into the outgoing message
        /// </summary>
        public void Write(Client client, NetOutgoingMessage msg)
        {
            if (events.Count == 0) return;

            List<NetEntityEvent> eventsToSync = new List<NetEntityEvent>();
            for (int i = events.Count - 1; i >= 0 && events[i].ID > client.lastRecvEntityEventID; i--)
            {
                float lastSent = 0;
                client.entityEventLastSent.TryGetValue(events[i].ID, out lastSent);

                if (lastSent > NetTime.Now - client.Connection.AverageRoundtripTime)
                {
                    break;
                }

                eventsToSync.Insert(0, events[i]);
            }
            if (eventsToSync.Count == 0) return;
            
            foreach (NetEntityEvent entityEvent in eventsToSync)
            {
                client.entityEventLastSent[entityEvent.ID] = (float)NetTime.Now;
            }

            Write(msg, eventsToSync, client);
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

        public void Clear()
        {
            events.Clear();
        }
    }
}
