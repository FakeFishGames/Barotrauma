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

        //when was a specific entity event last sent to the client
        //  key = event id, value = NetTime.Now when sending
        public Dictionary<UInt32, float> eventLastSent;

        public UInt32 LastReceivedID
        {
            get { return lastReceivedID; }
        }

        private UInt32 lastReceivedID;

        public ClientEntityEventManager(GameClient client) 
        {
            events = new List<ClientEntityEvent>();
            eventLastSent = new Dictionary<uint, float>();

            thisClient = client;
        }

        public void CreateEvent(IClientSerializable entity, object[] extraData = null)
        {
            if (!(entity is Entity))
            {
                DebugConsole.ThrowError("Can't create an entity event for " + entity + "!");
                return;
            }

            ID++;
            var newEvent = new ClientEntityEvent(entity, ID);
            if (extraData != null) newEvent.SetData(extraData);

            events.Add(newEvent);
        }

        public void Write(NetOutgoingMessage msg, NetConnection serverConnection)
        {
            if (events.Count == 0) return;

            List<NetEntityEvent> eventsToSync = new List<NetEntityEvent>();

            //find the index of the first event the server hasn't received
            int startIndex = events.Count;
            while (startIndex > 0 &&
                events[startIndex-1].ID > thisClient.LastSentEntityEventID)
            {
                startIndex--;
            }

            for (int i = startIndex; i < events.Count; i++)
            {
                //find the first event that hasn't been sent in 1.5 * roundtriptime or at all
                float lastSent = 0;
                eventLastSent.TryGetValue(events[i].ID, out lastSent);

                if (lastSent > NetTime.Now - serverConnection.AverageRoundtripTime * 1.5f)
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
            if (eventsToSync.Count == 0) return;

            foreach (NetEntityEvent entityEvent in eventsToSync)
            {
                eventLastSent[entityEvent.ID] = (float)NetTime.Now;
            }

            msg.Write((byte)ClientNetObject.ENTITY_STATE);
            Write(msg, eventsToSync);
        }

        public void Read(NetIncomingMessage msg, float sendingTime)
        {
            base.Read(msg, sendingTime, ref lastReceivedID);
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

            serverEntity.ClientRead(ServerNetObject.ENTITY_STATE, buffer, sendingTime);
        }

        public void Clear()
        {
            ID = 0;

            events.Clear();
            eventLastSent.Clear();
        }
    }
}
