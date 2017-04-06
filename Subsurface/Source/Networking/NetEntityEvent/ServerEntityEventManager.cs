using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    class ServerEntityEventManager : NetEntityEventManager
    {
        private List<ServerEntityEvent> events;

        //list of unique events (i.e. !IsDuplicate) created during the round
        //used for syncing clients who join mid-round
        private List<ServerEntityEvent> uniqueEvents;

        private UInt16 lastSentToAll;

        public List<ServerEntityEvent> Events
        {
            get { return events; }
        }

        public List<ServerEntityEvent> UniqueEvents
        {
            get { return uniqueEvents; }
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

        private UInt16 ID;

        private GameServer server;
        
        public ServerEntityEventManager(GameServer server) 
        {
            events = new List<ServerEntityEvent>();

            this.server = server;

            bufferedEvents = new List<BufferedEvent>();

            uniqueEvents = new List<ServerEntityEvent>();
        }

        public void CreateEvent(IServerSerializable entity, object[] extraData = null)
        {
            if (entity == null || !(entity is Entity))
            {
                DebugConsole.ThrowError("Can't create an entity event for " + entity + "!");
                return;
            }

            var newEvent = new ServerEntityEvent(entity, (UInt16)(ID + 1));
            if (extraData != null) newEvent.SetData(extraData);
            
            //remove events that have been sent to all clients, they are redundant now
            //keep at least one event in the list (lastSentToAll == e.ID) so we can use it to keep track of the latest ID
            events.RemoveAll(e => NetIdUtils.IdMoreRecent(lastSentToAll, e.ID)); 
            
            for (int i = events.Count - 1; i >= 0; i--)
            {
                //we already have an identical event that's waiting to be sent
                // -> no need to add a new one
                if (events[i].IsDuplicate(newEvent) && !events[i].Sent) return;
            }

            ID++;

            events.Add(newEvent);

            if (!uniqueEvents.Any(e => e.IsDuplicate(newEvent)))
            {
                //create a copy of the event and give it a new ID
                var uniqueEvent = new ServerEntityEvent(entity, (UInt16)uniqueEvents.Count);
                uniqueEvent.SetData(extraData);

                uniqueEvents.Add(uniqueEvent);
            }
        }

        public void Update(List<Client> clients)
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

            var inGameClients = clients.FindAll(c => c.inGame && !c.NeedsMidRoundSync);
            if (inGameClients.Count > 0)
            {
                lastSentToAll = inGameClients[0].lastRecvEntityEventID;
                inGameClients.ForEach(c => { if (NetIdUtils.IdMoreRecent(lastSentToAll, c.lastRecvEntityEventID)) lastSentToAll = c.lastRecvEntityEventID; });

                ServerEntityEvent firstEventToResend = events.Find(e => e.ID == (lastSentToAll + 1));
                if (firstEventToResend != null && (Timing.TotalTime - firstEventToResend.CreateTime) > 10.0f)
                {
                    //it's been 10 seconds since this event was created
                    //kick everyone that hasn't received it yet, this is way too old
                    List<Client> toKick = inGameClients.FindAll(c => 
                        NetIdUtils.IdMoreRecent((UInt16)(lastSentToAll + 1), c.lastRecvEntityEventID) && 
                        (Timing.TotalTime - c.MidRoundSyncTimeOut) > 10.0f); //give mid-round joining players extra 10 seconds to receive the events

                    if (toKick != null) toKick.ForEach(c => server.DisconnectClient(c, "", "You have been disconnected because of excessive desync"));
                }

                if (events.Count > 0)
                {
                    //the client is waiting for an event that we don't have anymore
                    //(the ID they're expecting is smaller than the ID of the first event in our list)
                    List<Client> toKick = inGameClients.FindAll(c => NetIdUtils.IdMoreRecent(events[0].ID, (UInt16)(c.lastRecvEntityEventID+1)));
                    if (toKick != null) toKick.ForEach(c => server.DisconnectClient(c, "", "You have been disconnected because of excessive desync"));
                }
            }
            
            var timedOutClients = clients.FindAll(c => c.inGame && c.NeedsMidRoundSync && Timing.TotalTime > c.MidRoundSyncTimeOut);
            if (timedOutClients != null) timedOutClients.ForEach(c => GameMain.Server.DisconnectClient(c, "", "You have been disconnected because syncing your client with the server took too long."));
            
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

            List<NetEntityEvent> eventsToSync = null;
            if (client.NeedsMidRoundSync)
            {
                eventsToSync = GetEventsToSync(client, uniqueEvents);
            }
            else
            {
                eventsToSync = GetEventsToSync(client, events);
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

            if (client.NeedsMidRoundSync)
            {
                msg.Write((byte)ServerNetObject.ENTITY_EVENT_INITIAL);
                //how many (unique) events the clients had missed before joining
                client.UnreceivedEntityEventCount = (UInt16)uniqueEvents.Count;
                msg.Write(client.UnreceivedEntityEventCount);

                //ID of the first event sent after the client joined 
                //(after the client has been synced they'll switch their lastReceivedID 
                //to the one before this, and the eventmanagers will start to function "normally")
                msg.Write(events.Count == 0 ? (UInt16)0 : events[events.Count - 1].ID);

                Write(msg, eventsToSync, client);
            }
            else
            {
                msg.Write((byte)ServerNetObject.ENTITY_EVENT);
                Write(msg, eventsToSync, client);
            }
        }

        /// <summary>
        /// Returns a list of events that should be sent to the client from the eventList 
        /// </summary>
        /// <param name="client"></param>
        /// <param name="eventList"></param>
        /// <returns></returns>
        private List<NetEntityEvent> GetEventsToSync(Client client, List<ServerEntityEvent> eventList)
        {
            List<NetEntityEvent> eventsToSync = new List<NetEntityEvent>();

            //find the index of the first event the client hasn't received
            int startIndex = eventList.Count;
            while (startIndex > 0 &&
                NetIdUtils.IdMoreRecent(eventList[startIndex - 1].ID,client.lastRecvEntityEventID))
            {
                startIndex--;
            }
            
            for (int i = startIndex; i < eventList.Count; i++)
            {
                //find the first event that hasn't been sent in 1.5 * roundtriptime or at all
                float lastSent = 0;
                client.entityEventLastSent.TryGetValue(eventList[i].ID, out lastSent);

                if (lastSent > NetTime.Now - client.Connection.AverageRoundtripTime * 1.5f)
                {
                    continue;
                }

                eventsToSync.AddRange(eventList.GetRange(i, eventList.Count - i));
                break;
            }

            return eventsToSync;
        }

        public void InitClientMidRoundSync(Client client)
        {
            //no need for midround syncing if no events have been created,
            //or if the first created unique event is still in the event list
            if (uniqueEvents.Count == 0 || (events.Count > 0 && events[0].ID == uniqueEvents[0].ID))
            {
                client.UnreceivedEntityEventCount = 0;
                client.NeedsMidRoundSync = false;
            }
            else
            {
                double midRoundSyncTimeOut = uniqueEvents.Count / MaxEventsPerWrite * server.UpdateInterval.TotalSeconds;
                midRoundSyncTimeOut = Math.Max(5.0f, midRoundSyncTimeOut * 1.5f);

                client.UnreceivedEntityEventCount = (UInt16)uniqueEvents.Count;
                client.NeedsMidRoundSync = true;
                client.MidRoundSyncTimeOut = Timing.TotalTime + midRoundSyncTimeOut;
            }
        }

        /// <summary>
        /// Read the events from the message, ignoring ones we've already received
        /// </summary>
        public void Read(NetIncomingMessage msg, Client sender = null)
        {
            UInt16 firstEventID = msg.ReadUInt16();
            int eventCount = msg.ReadByte();

            for (int i = 0; i < eventCount; i++)
            {
                UInt16 thisEventID = (UInt16)(firstEventID + (UInt16)i);
                UInt16 entityID = msg.ReadUInt16();

                if (entityID == 0)
                {
                    msg.ReadPadBits();
                    sender.lastSentEntityEventID++;
                    continue;
                }

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
                    DebugConsole.NewMessage("received msg " + thisEventID, Microsoft.Xna.Framework.Color.Green);

                    UInt16 characterStateID = msg.ReadUInt16();

                    NetBuffer buffer = new NetBuffer();
                    buffer.Write(msg.ReadBytes(msgLength - 2));
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

            lastSentToAll = 0;

            uniqueEvents.Clear();

            foreach (Client c in server.ConnectedClients)
            {
                c.entityEventLastSent.Clear();
                c.lastRecvEntityEventID = 0;
                c.lastSentEntityEventID = 0;
            }
        }
    }
}
