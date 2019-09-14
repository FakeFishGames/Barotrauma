using System;
using System.Collections.Generic;

namespace Barotrauma.Networking
{
    class ClientEntityEventManager : NetEntityEventManager
    {
        private List<ClientEntityEvent> events;

        private UInt16 ID;

        private GameClient thisClient;

        //when was a specific entity event last sent to the client
        //  key = event id, value = NetTime.Now when sending
        public Dictionary<UInt16, float> eventLastSent;

        public UInt16 LastReceivedID
        {
            get { return lastReceivedID; }
        }

        private UInt16 lastReceivedID;

        public bool MidRoundSyncing
        {
            get { return firstNewID.HasValue; }
        }

        public ClientEntityEventManager(GameClient client) 
        {
            events = new List<ClientEntityEvent>();
            eventLastSent = new Dictionary<UInt16, float>();

            thisClient = client;
        }

        public void CreateEvent(IClientSerializable entity, object[] extraData = null)
        {
            if (GameMain.Client == null || GameMain.Client.Character == null) return;

            if (!(entity is Entity))
            {
                DebugConsole.ThrowError("Can't create an entity event for " + entity + "!");
                return;
            }

            if (((Entity)entity).Removed)
            {
                DebugConsole.ThrowError("Can't create an entity event for " + entity + " - the entity has been removed.\n" + Environment.StackTrace);
                return;
            }
            if (((Entity)entity).IdFreed)
            {
                DebugConsole.ThrowError("Can't create an entity event for " + entity + " - the ID of the entity has been freed.\n" + Environment.StackTrace);
                return;
            }

            var newEvent = new ClientEntityEvent(entity, (UInt16)(ID + 1))
            {
                CharacterStateID = GameMain.Client.Character.LastNetworkUpdateID
            };
            if (extraData != null) { newEvent.SetData(extraData); }

            for (int i = events.Count - 1; i >= 0; i--)
            {
                //we already have an identical event that's waiting to be sent
                // -> no need to add a new one
                if (!events[i].Sent && events[i].IsDuplicate(newEvent)) return;
            }

            ID++;

            events.Add(newEvent);
        }

        public void Write(IWriteMessage msg, NetworkConnection serverConnection)
        {
            if (events.Count == 0 || serverConnection == null) return;

            List<NetEntityEvent> eventsToSync = new List<NetEntityEvent>();

            //find the index of the first event the server hasn't received
            int startIndex = events.Count;
            while (startIndex > 0 &&
                NetIdUtils.IdMoreRecent(events[startIndex - 1].ID, thisClient.LastSentEntityEventID))
            {
                startIndex--;
            }

            //remove events the server has already received
            events.RemoveRange(0, startIndex);

            for (int i = 0; i < events.Count; i++)
            {
                //find the first event that hasn't been sent in roundtriptime or at all
                eventLastSent.TryGetValue(events[i].ID, out float lastSent);

                if (lastSent > Lidgren.Network.NetTime.Now - 0.2) //TODO: reimplement serverConnection.AverageRoundtripTime
                {
                    continue;
                }

                eventsToSync.AddRange(events.GetRange(i, events.Count - i));
                break;
            }
            if (eventsToSync.Count == 0) { return; }

            foreach (NetEntityEvent entityEvent in eventsToSync)
            {
                eventLastSent[entityEvent.ID] = (float)Lidgren.Network.NetTime.Now;
            }

            msg.Write((byte)ClientNetObject.ENTITY_STATE);
            Write(msg, eventsToSync, out _);
        }

        private UInt16? firstNewID;

        /// <summary>
        /// Read the events from the message, ignoring ones we've already received. Returns false if reading the events fails.
        /// </summary>
        public bool Read(ServerNetObject type, IReadMessage msg, float sendingTime, List<IServerSerializable> entities)
        {
            UInt16 unreceivedEntityEventCount = 0;

            if (type == ServerNetObject.ENTITY_EVENT_INITIAL)
            {
                unreceivedEntityEventCount = msg.ReadUInt16();
                firstNewID = msg.ReadUInt16();

                if (GameSettings.VerboseLogging)
                {
                    DebugConsole.NewMessage(
                        "received midround syncing msg, unreceived: " + unreceivedEntityEventCount +
                        ", first new ID: " + firstNewID, Microsoft.Xna.Framework.Color.Yellow);
                }
            }
            else if (firstNewID != null)
            {
                if (GameSettings.VerboseLogging)
                {
                    DebugConsole.NewMessage("midround syncing complete, switching to ID " + (UInt16) (firstNewID - 1),
                        Microsoft.Xna.Framework.Color.Yellow);
                }

                lastReceivedID = (UInt16)(firstNewID - 1);
                firstNewID = null;
            }

            entities.Clear();

            UInt16 firstEventID = msg.ReadUInt16();
            int eventCount = msg.ReadByte();
            
            for (int i = 0; i < eventCount; i++)
            {
                UInt16 thisEventID = (UInt16)(firstEventID + (UInt16)i);                
                UInt16 entityID = msg.ReadUInt16();
                
                if (entityID == Entity.NullEntityID)
                {
                    if (GameSettings.VerboseLogging)
                    {
                        DebugConsole.NewMessage("received msg " + thisEventID + " (null entity)",
                            Microsoft.Xna.Framework.Color.Orange);
                    }
                    msg.ReadPadBits();
                    entities.Add(null);
                    if (thisEventID == (UInt16)(lastReceivedID + 1)) lastReceivedID++;
                    continue;
                }

                byte msgLength = msg.ReadByte();
                
                IServerSerializable entity = Entity.FindEntityByID(entityID) as IServerSerializable;
                entities.Add(entity);
                
                //skip the event if we've already received it or if the entity isn't found
                if (thisEventID != (UInt16)(lastReceivedID + 1) || entity == null)
                {
                    if (thisEventID != (UInt16) (lastReceivedID + 1))
                    {
                        if (GameSettings.VerboseLogging)
                        {
                            DebugConsole.NewMessage(
                                "Received msg " + thisEventID + " (waiting for " + (lastReceivedID + 1) + ")",
                                NetIdUtils.IdMoreRecent(thisEventID, (UInt16)(lastReceivedID + 1))
                                    ? Microsoft.Xna.Framework.Color.Red
                                    : Microsoft.Xna.Framework.Color.Yellow);
                        }
                    }
                    else if (entity == null)
                    {
                        DebugConsole.NewMessage(
                            "Received msg " + thisEventID + ", entity " + entityID + " not found",
                            Microsoft.Xna.Framework.Color.Red);
                        GameMain.Client.ReportError(ClientNetError.MISSING_ENTITY, eventID: thisEventID, entityID: entityID);
                        return false;
                    }
                    
                    msg.BitPosition += msgLength * 8;
                }
                else
                {
                    long msgPosition = msg.BitPosition;
                    if (GameSettings.VerboseLogging)
                    {
                        DebugConsole.NewMessage("received msg " + thisEventID + " (" + entity.ToString() + ")",
                            Microsoft.Xna.Framework.Color.Green);
                    }
                    lastReceivedID++;
                    try
                    {
                        ReadEvent(msg, entity, sendingTime);
                    }

                    catch (Exception e)
                    {
                        string errorMsg = "Failed to read event for entity \"" + entity.ToString() + "\" (" + e.Message + ")! (MidRoundSyncing: " + thisClient.MidRoundSyncing + ")\n" + e.StackTrace;
                        errorMsg += "\nPrevious entities:";
                        for (int j = entities.Count - 2; j >= 0; j--)
                        {
                            errorMsg += "\n" + (entities[j] == null ? "NULL" : entities[j].ToString());
                        }

                        if (GameSettings.VerboseLogging)
                        {
                            DebugConsole.ThrowError("Failed to read event for entity \"" + entity.ToString() + "\"!", e);
                        }
                        GameAnalyticsManager.AddErrorEventOnce("ClientEntityEventManager.Read:ReadFailed" + entity.ToString(),
                            GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                        msg.BitPosition = (int)(msgPosition + msgLength * 8);
                    }
                }
                msg.ReadPadBits();
            }
            return true;
        }

        protected override void WriteEvent(IWriteMessage buffer, NetEntityEvent entityEvent, Client recipient = null)
        {
            var clientEvent = entityEvent as ClientEntityEvent;
            if (clientEvent == null) return;

            clientEvent.Write(buffer);
            clientEvent.Sent = true;
        }

        protected void ReadEvent(IReadMessage buffer, IServerSerializable entity, float sendingTime)
        {
            entity.ClientRead(ServerNetObject.ENTITY_EVENT, buffer, sendingTime);
        }

        public void Clear()
        {
            ID = 0;

            lastReceivedID = 0;

            firstNewID = null;

            events.Clear();
            eventLastSent.Clear();
        }
    }
}
