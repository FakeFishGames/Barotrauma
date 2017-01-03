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

        private GameServer server;
        
        public ServerEntityEventManager(GameServer server) 
        {
            events = new List<ServerEntityEvent>();

            this.server = server;
        }

        public void CreateEvent(IServerSerializable entity, object[] extraData = null)
        {
            if (entity == null || !(entity is Entity))
            {
                DebugConsole.ThrowError("Can't create an entity event for " + entity + "!");
                return;
            }

            var newEvent = new ServerEntityEvent(entity, ID + 1);
            if (extraData != null) newEvent.SetData(extraData);

            for (int i = events.Count - 1; i >= 0 && !events[i].Sent; i--)
            {
                //we already have an identical event that's waiting to be sent
                // -> no need to add a new one
                if (events[i].IsDuplicate(newEvent)) return;
            }

            ID++;

            events.Add(newEvent);
        }

        /// <summary>
        /// Writes all the events that the client hasn't received yet into the outgoing message
        /// </summary>
        public void Write(Client client, NetOutgoingMessage msg)
        {
            if (events.Count == 0) return;

            List<NetEntityEvent> eventsToSync = new List<NetEntityEvent>();

            //find the index of the first event the client hasn't received
            int startIndex = events.Count;
            while (startIndex > 0 &&
                events[startIndex-1].ID > client.lastRecvEntityEventID)
            {
                startIndex--;
            }

            for (int i = startIndex; i < events.Count; i++)
            {
                //find the first event that hasn't been sent in 1.5 * roundtriptime or at all
                float lastSent = 0;
                client.entityEventLastSent.TryGetValue(events[i].ID, out lastSent);

                if (lastSent > NetTime.Now - client.Connection.AverageRoundtripTime * 1.5f)
                {
                    continue;
                }

                eventsToSync.AddRange(events.GetRange(i, events.Count - i));
                break;
            }
            if (eventsToSync.Count == 0) return;

            //too many events for one packet
            if (eventsToSync.Count > MaxEventsPerWrite)
            {
                eventsToSync.RemoveRange(MaxEventsPerWrite, eventsToSync.Count - MaxEventsPerWrite);
            }
            
            foreach (NetEntityEvent entityEvent in eventsToSync)
            {
                (entityEvent as ServerEntityEvent).Sent = true;
                client.entityEventLastSent[entityEvent.ID] = (float)NetTime.Now;
            }

            msg.Write((byte)ServerNetObject.ENTITY_STATE);
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

            clientEntity.ServerRead(ClientNetObject.ENTITY_STATE, buffer, sender);
        }

        public void Read(NetIncomingMessage msg, Client client)
        {
            base.Read(msg, 0.0f, ref client.lastSentEntityEventID, client);
        }

        public void Clear()
        {
            ID = 0;
            events.Clear();

            server.ConnectedClients.ForEach(c => c.entityEventLastSent.Clear());
        }
    }
}
