using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    class ServerEntityEvent : NetEntityEvent
    {
        private IServerSerializable serializable;

        public bool Sent;

#if DEBUG
        public string StackTrace;
#endif

        private double createTime;
        public double CreateTime
        {
            get { return createTime; }
        }

        public ServerEntityEvent(IServerSerializable entity, UInt16 id)
            : base(entity, id)
        {
            serializable = entity;

            createTime = Timing.TotalTime;

#if DEBUG
            StackTrace = Environment.StackTrace.ToString();
#endif
        }

        public void Write(NetBuffer msg, Client recipient)
        {
            serializable.ServerWrite(msg, recipient, Data);
        }
    }

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

            if (((Entity)entity).Removed && !(entity is Level))
            {
                DebugConsole.ThrowError("Can't create an entity event for " + entity + " - the entity has been removed.\n"+Environment.StackTrace);
                return;
            }
            if (((Entity)entity).IdFreed)
            {
                DebugConsole.ThrowError("Can't create an entity event for " + entity + " - the ID of the entity has been freed.\n"+Environment.StackTrace);
                return;
            }

            var newEvent = new ServerEntityEvent(entity, (UInt16)(ID + 1));
            if (extraData != null) newEvent.SetData(extraData);
            
            //remove events that have been sent to all clients, they are redundant now
            //keep at least one event in the list (lastSentToAll == e.ID) so we can use it to keep track of the latest ID
            events.RemoveAll(e => NetIdUtils.IdMoreRecent(lastSentToAll, e.ID));

            if (server.ConnectedClients.Count(c => c.InGame) == 0 && events.Count > 1)
            {
                events.RemoveRange(0, events.Count - 1);
            }

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
                var uniqueEvent = new ServerEntityEvent(entity, (UInt16)(uniqueEvents.Count + 1));
                uniqueEvent.SetData(extraData);

                uniqueEvents.Add(uniqueEvent);
            }
        }

        public void Update(List<Client> clients)
        {
            foreach (BufferedEvent bufferedEvent in bufferedEvents)
            {
                if (bufferedEvent.Character == null || bufferedEvent.Character.IsDead)
                {
                    bufferedEvent.IsProcessed = true;
                    continue;
                }

                //delay reading the events until the inputs for the corresponding frame have been processed

                //UNLESS the character is unconscious, in which case we'll read the messages immediately (because further inputs will be ignored)
                //atm the "give in" command is the only thing unconscious characters can do, other types of events are ignored
                if (!bufferedEvent.Character.IsUnconscious &&
                    NetIdUtils.IdMoreRecent(bufferedEvent.CharacterStateID, bufferedEvent.Character.LastProcessedID))
                {
                    continue;
                }
                
                try
                {
                    ReadEvent(bufferedEvent.Data, bufferedEvent.TargetEntity, bufferedEvent.Sender);
                }

                catch (Exception e)
                {
                    string entityName = bufferedEvent.TargetEntity == null ? "null" : bufferedEvent.TargetEntity.ToString();
                    if (GameSettings.VerboseLogging)
                    {
                        DebugConsole.ThrowError("Failed to read server event for entity \"" + entityName + "\"!", e);
                    }
                    GameAnalyticsManager.AddErrorEventOnce("ServerEntityEventManager.Read:ReadFailed" + entityName,
                        GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Failed to read server event for entity \"" + entityName + "\"!\n" + e.StackTrace);
                }

                bufferedEvent.IsProcessed = true;
            }
            
            var inGameClients = clients.FindAll(c => c.InGame && !c.NeedsMidRoundSync);
            if (inGameClients.Count > 0)
            {
                lastSentToAll = inGameClients[0].LastRecvEntityEventID;
                inGameClients.ForEach(c => { if (NetIdUtils.IdMoreRecent(lastSentToAll, c.LastRecvEntityEventID)) lastSentToAll = c.LastRecvEntityEventID; });

                ServerEntityEvent firstEventToResend = events.Find(e => e.ID == (ushort)(lastSentToAll + 1));
                if (firstEventToResend != null && (Timing.TotalTime - firstEventToResend.CreateTime) > 10.0f)
                {
                    //it's been 10 seconds since this event was created
                    //kick everyone that hasn't received it yet, this is way too old
                    List<Client> toKick = inGameClients.FindAll(c => NetIdUtils.IdMoreRecent((UInt16)(lastSentToAll + 1), c.LastRecvEntityEventID));
                    toKick.ForEach(c =>
                    {
                        DebugConsole.NewMessage(c.Name + " was kicked due to excessive desync (expected old event " + c.LastRecvEntityEventID.ToString() + ")", Microsoft.Xna.Framework.Color.Red);
                        GameServer.Log("Disconnecting client " + c.Name + " due to excessive desync (expected old event " + c.LastRecvEntityEventID.ToString() + ").", ServerLog.MessageType.ServerMessage);
                        server.DisconnectClient(c, "", "You have been disconnected because of excessive desync");
                    }
                    );
                }

                if (events.Count > 0)
                {
                    //the client is waiting for an event that we don't have anymore
                    //(the ID they're expecting is smaller than the ID of the first event in our list)
                    List<Client> toKick = inGameClients.FindAll(c => NetIdUtils.IdMoreRecent(events[0].ID, (UInt16)(c.LastRecvEntityEventID + 1)));
                    toKick.ForEach(c =>
                    {
                        DebugConsole.NewMessage(c.Name + " was kicked due to excessive desync (expected " + c.LastRecvEntityEventID.ToString() + ", last available is " + events[0].ID.ToString() + ")", Microsoft.Xna.Framework.Color.Red);
                        GameServer.Log("Disconnecting client " + c.Name + " due to excessive desync (expected " + c.LastRecvEntityEventID.ToString() + ", last available is " + events[0].ID.ToString() + ")", ServerLog.MessageType.ServerMessage);
                        server.DisconnectClient(c, "", "You have been disconnected because of excessive desync");
                    });
                }
            }
            
            var timedOutClients = clients.FindAll(c => c.InGame && c.NeedsMidRoundSync && Timing.TotalTime > c.MidRoundSyncTimeOut);
            foreach (Client timedOutClient in timedOutClients)
            {
                GameServer.Log("Disconnecting client " + timedOutClient.Name + ". Syncing the client with the server took too long.", ServerLog.MessageType.ServerMessage);
                GameMain.Server.DisconnectClient(timedOutClient, "", "You have been disconnected because syncing your client with the server took too long.");
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
                client.EntityEventLastSent[entityEvent.ID] = (float)NetTime.Now;
            }

            if (client.NeedsMidRoundSync)
            {
                msg.Write((byte)ServerNetObject.ENTITY_EVENT_INITIAL);

                //how many (unique) events the clients had missed before joining
                client.UnreceivedEntityEventCount = (UInt16)uniqueEvents.Count;
                //ID of the first event sent after the client joined 
                //(after the client has been synced they'll switch their lastReceivedID 
                //to the one before this, and the eventmanagers will start to function "normally")
                client.FirstNewEventID = events.Count == 0 ? (UInt16)0 : events[events.Count - 1].ID;

                msg.Write(client.UnreceivedEntityEventCount);                
                msg.Write(client.FirstNewEventID);
                //DebugConsole.NewMessage(eventsToSync[0].ID.ToString(), Microsoft.Xna.Framework.Color.Yellow);
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
            if (eventList.Count == 0) return eventsToSync;

            //find the index of the first event the client hasn't received
            int startIndex = eventList.Count;
            while (startIndex > 0 &&
                NetIdUtils.IdMoreRecent(eventList[startIndex - 1].ID,client.LastRecvEntityEventID))
            {
                startIndex--;
            }
            
            for (int i = startIndex; i < eventList.Count; i++)
            {
                //find the first event that hasn't been sent in 1.5 * roundtriptime or at all
                float lastSent = 0;
                client.EntityEventLastSent.TryGetValue(eventList[i].ID, out lastSent);

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
                client.FirstNewEventID = 0;
                client.NeedsMidRoundSync = false;
            }
            else
            {
                double midRoundSyncTimeOut = uniqueEvents.Count / MaxEventsPerWrite * server.UpdateInterval.TotalSeconds;
                midRoundSyncTimeOut = Math.Max(10.0f, midRoundSyncTimeOut * 2.0f);

                client.UnreceivedEntityEventCount = (UInt16)uniqueEvents.Count;
                client.FirstNewEventID = 0;
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

                if (entityID == Entity.NullEntityID)
                {
                    msg.ReadPadBits();
                    if (thisEventID == (UInt16)(sender.LastSentEntityEventID + 1)) sender.LastSentEntityEventID++;
                    continue;
                }

                byte msgLength = msg.ReadByte();

                IClientSerializable entity = Entity.FindEntityByID(entityID) as IClientSerializable;

                //skip the event if we've already received it
                if (thisEventID != (UInt16)(sender.LastSentEntityEventID + 1))
                {
                    if (GameSettings.VerboseLogging)
                    {
                        DebugConsole.NewMessage("received msg " + thisEventID, Microsoft.Xna.Framework.Color.Red);
                    }
                    msg.Position += msgLength * 8;
                }
                else if (entity == null)
                {
                    //entity not found -> consider the even read and skip over it
                    //(can happen, for example, when a client uses a medical item repeatedly 
                    //and creates an event for it before receiving the event about it being removed)
                    if (GameSettings.VerboseLogging)
                    {
                        DebugConsole.NewMessage(
                            "received msg " + thisEventID + ", entity " + entityID + " not found",
                            Microsoft.Xna.Framework.Color.Orange);
                    }
                    sender.LastSentEntityEventID++;
                    msg.Position += msgLength * 8;
                }
                else
                {
                    if (GameSettings.VerboseLogging)
                    {
                        DebugConsole.NewMessage("received msg " + thisEventID, Microsoft.Xna.Framework.Color.Green);
                    }

                    UInt16 characterStateID = msg.ReadUInt16();

                    NetBuffer buffer = new NetBuffer();
                    buffer.Write(msg.ReadBytes(msgLength - 2));
                    BufferEvent(new BufferedEvent(sender, sender.Character, characterStateID, entity, buffer));

                    sender.LastSentEntityEventID++;
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
                c.EntityEventLastSent.Clear();
                c.LastRecvEntityEventID = 0;
                c.LastSentEntityEventID = 0;
            }
        }
    }
}
