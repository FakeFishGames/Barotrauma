using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

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

        public bool MidRoundSyncingDone
        {
            get;
            private set;
        }

        public ClientEntityEventManager(GameClient client) 
        {
            events = new List<ClientEntityEvent>();
            eventLastSent = new Dictionary<UInt16, float>();

            thisClient = client;
        }

        public void CreateEvent(IClientSerializable entity, NetEntityEvent.IData extraData = null)
        {
            if (GameMain.Client?.Character == null) { return; }

            if (!ValidateEntity(entity)) { return; }

            var newEvent = new ClientEntityEvent(
                entity,
                eventId: (UInt16)(ID + 1),
                characterStateId: GameMain.Client.Character.LastNetworkUpdateID);
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

                if (GameSettings.CurrentConfig.VerboseLogging)
                {
                    DebugConsole.NewMessage(
                        "received midround syncing msg, unreceived: " + unreceivedEntityEventCount +
                        ", first new ID: " + firstNewID, Microsoft.Xna.Framework.Color.Yellow);
                }
            }
            else
            {
                MidRoundSyncingDone = true;
                if (firstNewID != null)
                {
                    if (GameSettings.CurrentConfig.VerboseLogging)
                    {
                        DebugConsole.NewMessage("midround syncing complete, switching to ID " + (UInt16) (firstNewID - 1),
                            Microsoft.Xna.Framework.Color.Yellow);
                    }
                    lastReceivedID = (UInt16)(firstNewID - 1);
                    firstNewID = null;
                }
            }

            entities.Clear();

            msg.ReadPadBits();
            UInt16 firstEventID = msg.ReadUInt16();
            int eventCount = msg.ReadByte();

            for (int i = 0; i < eventCount; i++)
            {
                //16 = entity ID, 8 = msg length
                if (msg.BitPosition + 16 + 8 > msg.LengthBits)
                {
                    string errorMsg = $"Error while reading a message from the server. Entity event data exceeds the size of the buffer (current position: {msg.BitPosition}, length: {msg.LengthBits}).";
                    errorMsg += "\nPrevious entities:";
                    for (int j = entities.Count - 1; j >= 0; j--)
                    {
                        errorMsg += "\n" + (entities[j] == null ? "NULL" : entities[j].ToString());
                    }
                    DebugConsole.ThrowError(errorMsg);
                    return false;
                }

                UInt16 thisEventID = (UInt16)(firstEventID + (UInt16)i);                
                UInt16 entityID = msg.ReadUInt16();

                if (entityID == Entity.NullEntityID)
                {
                    if (GameSettings.CurrentConfig.VerboseLogging)
                    {
                        DebugConsole.NewMessage("received msg " + thisEventID + " (null entity)",
                            Microsoft.Xna.Framework.Color.Orange);
                    }
                    entities.Add(null);
                    if (thisEventID == (UInt16)(lastReceivedID + 1)) { lastReceivedID++; }
                    continue;
                }

                int msgLength = (int)msg.ReadVariableUInt32();
                
                IServerSerializable entity = Entity.FindEntityByID(entityID) as IServerSerializable;
                entities.Add(entity);
                
                //skip the event if we've already received it or if the entity isn't found
                if (thisEventID != (UInt16)(lastReceivedID + 1) || entity == null)
                {
                    if (thisEventID != (UInt16) (lastReceivedID + 1))
                    {
                        if (GameSettings.CurrentConfig.VerboseLogging)
                        {
                            DebugConsole.NewMessage(
                                "Received msg " + thisEventID + " (waiting for " + (lastReceivedID + 1) + ")",
                                NetIdUtils.IdMoreRecent(thisEventID, (UInt16)(lastReceivedID + 1))
                                    ? GUIStyle.Red
                                    : Microsoft.Xna.Framework.Color.Yellow);
                        }
                    }
                    else if (entity == null)
                    {
                        DebugConsole.NewMessage(
                            "Received msg " + thisEventID + ", entity " + entityID + " not found",
                            GUIStyle.Red);
                        GameMain.Client.ReportError(ClientNetError.MISSING_ENTITY, eventId: thisEventID, entityId: entityID);
                        return false;
                    }
                    
                    msg.BitPosition += msgLength * 8;
                }
                else
                {
                    int msgPosition = msg.BitPosition;
                    if (GameSettings.CurrentConfig.VerboseLogging)
                    {
                        DebugConsole.NewMessage("received msg " + thisEventID + " (" + entity.ToString() + ")",
                            Microsoft.Xna.Framework.Color.Green);
                    }
                    lastReceivedID++;
                    try
                    {
                        ReadEvent(msg, entity, sendingTime);
                        msg.ReadPadBits();

                        if (msg.BitPosition != msgPosition + msgLength * 8)
                        {
                            var prevEntity = entities.Count >= 2 ? entities[entities.Count - 2] : null;
                            ushort prevId = prevEntity is Entity p ? p.ID : (ushort)0;
                            string errorMsg = $"Message byte position incorrect after reading an event for the entity \"{entity}\" (ID {(entity is Entity e ? e.ID : 0)}). "
                                +$"The previous entity was \"{prevEntity}\" (ID {prevId}) "
                                +$"Read {msg.BitPosition - msgPosition} bits, expected message length was {msgLength * 8} bits.";

                            DebugConsole.ThrowError(errorMsg);

                            GameAnalyticsManager.AddErrorEventOnce("ClientEntityEventManager.Read:BitPosMismatch", GameAnalyticsManager.ErrorSeverity.Error, errorMsg);

                            //TODO: force the BitPosition to correct place? Having some entity in a potentially incorrect state is not as bad as a desync kick
                            //msg.BitPosition = (int)(msgPosition + msgLength * 8);
                        }
                    }
                    catch (Exception e)
                    {
                        string errorMsg = $"Failed to read event {thisEventID} for entity \"{entity}\"" +
                                          $"{(entity is Entity { ID: var entityId } ? $", id {entityId}" : "")} ";
                        DebugConsole.ThrowError(errorMsg, e);

                        errorMsg += $"({e.Message})! (MidRoundSyncing: {thisClient.MidRoundSyncing})\n{e.StackTrace.CleanupStackTrace()}";
                        errorMsg += "\nPrevious entities:";
                        for (int j = entities.Count - 2; j >= 0; j--)
                        {
                            errorMsg += "\n" + (entities[j] == null ? "NULL" : entities[j].ToString());
                        }

                        GameAnalyticsManager.AddErrorEventOnce("ClientEntityEventManager.Read:ReadFailed" + entity.ToString(),
                            GameAnalyticsManager.ErrorSeverity.Error, errorMsg);
                        msg.BitPosition = (int)(msgPosition + msgLength * 8);
                    }
                }
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
            entity.ClientEventRead(buffer, sendingTime);
        }

        public void Clear()
        {
            ID = 0;

            lastReceivedID = 0;

            firstNewID = null;

            events.Clear();
            eventLastSent.Clear();

            MidRoundSyncingDone = false;
        }

        /// <summary>
        /// Clears events generated by the current client, used
        /// when resynchronizing with the server after a timeout.
        /// </summary>
        public void ClearSelf()
        {
            ID = 0;
            events.Clear();
        }
    }
}
