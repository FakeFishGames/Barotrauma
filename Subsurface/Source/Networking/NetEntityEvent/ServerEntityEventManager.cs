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

        public List<ServerEntityEvent> Events
        {
            get { return events; }
        }

        private class BufferedEvent
        {
            public readonly Client Sender;

            public readonly UInt16 CharacterStateID;
            public readonly NetBuffer Data;

            public readonly Character Character;

            public readonly IClientSerializable TargetEntity;

            public bool IsProcessed;

            public BufferedEvent(Client sender, Character senderCharacter, UInt16 characterStateID, IClientSerializable targetEntity, NetBuffer data)
            {
                this.Sender = sender;
                this.Character = senderCharacter;
                this.CharacterStateID = characterStateID;

                this.TargetEntity = targetEntity;

                this.Data = data;
            }
        }

        private List<BufferedEvent> bufferedEvents;

        private UInt32 ID;

        private GameServer server;
        
        public ServerEntityEventManager(GameServer server) 
        {
            events = new List<ServerEntityEvent>();

            this.server = server;

            bufferedEvents = new List<BufferedEvent>();
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

        public void Update()
        {
            foreach (BufferedEvent bufferedEvent in bufferedEvents)
            {
                if (bufferedEvent.Character == null)
                {
                    bufferedEvent.IsProcessed = true;
                    continue;
                }

                if (NetIdUtils.IdMoreRecent(bufferedEvent.CharacterStateID, bufferedEvent.Character.LastProcessedID)) continue;
                
                try
                {
                    ReadEvent(bufferedEvent.Data, bufferedEvent.TargetEntity, bufferedEvent.Sender);
                }

                catch (Exception e)
                {
#if DEBUG
                        DebugConsole.ThrowError("Failed to read event for entity \"" + bufferedEvent.TargetEntity.ToString() + "\"!", e);
#endif
                }

                bufferedEvent.IsProcessed = true;
            }

            bufferedEvents.RemoveAll(b => b.IsProcessed);           
        }

        private void BufferEvent(BufferedEvent bufferedEvent)
        {
            if (bufferedEvents.Count > 512)
            {
                //should normally never happen

                //a client could potentially spam events with a much higher character state ID 
                //than the state of their character and/or stop sending character inputs,
                //so we'll drop some events to make sure no-one blows up our buffer
                bufferedEvents.RemoveRange(0, 256);
            }

            bufferedEvents.Add(bufferedEvent);
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

        /// <summary>
        /// Read the events from the message, ignoring ones we've already received
        /// </summary>
        public void Read(NetIncomingMessage msg, Client sender = null)
        {
            UInt32 firstEventID = msg.ReadUInt32();
            int eventCount = msg.ReadByte();

            for (int i = 0; i < eventCount; i++)
            {
                UInt32 thisEventID = firstEventID + (UInt32)i;
                UInt16 entityID = msg.ReadUInt16();
                byte msgLength = msg.ReadByte();

                IClientSerializable entity = Entity.FindEntityByID(entityID) as IClientSerializable;

                //skip the event if we've already received it or if the entity isn't found
                if (thisEventID != sender.lastSentEntityEventID + 1 || entity == null)
                {
                    if (thisEventID != sender.lastSentEntityEventID + 1)
                    {
                        DebugConsole.NewMessage("received msg " + thisEventID, Microsoft.Xna.Framework.Color.Red);
                    }
                    else if (entity == null)
                    {
                        DebugConsole.NewMessage("received msg " + thisEventID + ", entity " + entityID + " not found", Microsoft.Xna.Framework.Color.Red);
                    }
                    msg.Position += msgLength * 8;
                }
                else
                {                    
                    UInt16 characterStateID = msg.ReadUInt16();

                    NetBuffer buffer = new NetBuffer();
                    buffer.Write(msg.ReadBytes(msgLength-2));
                    BufferEvent(new BufferedEvent(sender, sender.Character, characterStateID, entity, buffer));

                    sender.lastSentEntityEventID++;
                }
                msg.ReadPadBits();
            }
        }

        protected override void WriteEvent(NetBuffer buffer, NetEntityEvent entityEvent, Client recipient = null)
        {
            var serverEvent = entityEvent as ServerEntityEvent;
            if (serverEvent == null) return;

            serverEvent.Write(buffer, recipient);
        }

        protected void ReadEvent(NetBuffer buffer, INetSerializable entity, Client sender = null)
        {
            var clientEntity = entity as IClientSerializable;
            if (clientEntity == null) return;
            
            clientEntity.ServerRead(ClientNetObject.ENTITY_STATE, buffer, sender);
        }
        
        public void Clear()
        {
            ID = 0;
            events.Clear();

            bufferedEvents.Clear();

            server.ConnectedClients.ForEach(c => c.entityEventLastSent.Clear());
        }
    }
}
