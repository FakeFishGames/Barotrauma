using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Networking
{
    abstract class NetEntityEventManager
    {
        public const int MaxEventBufferLength = 1024;
        
        /// <summary>
        /// Write the events to the outgoing message. The recipient parameter is only needed for ServerEntityEventManager
        /// </summary>
        protected void Write(IWriteMessage msg, List<NetEntityEvent> eventsToSync, out List<NetEntityEvent> sentEvents, Client recipient = null)
        {
            //write into a temporary buffer so we can write the number of events before the actual data
            IWriteMessage tempBuffer = new WriteOnlyMessage();

            sentEvents = new List<NetEntityEvent>();

            int eventCount = 0;
            foreach (NetEntityEvent e in eventsToSync)
            {
                //write into a temporary buffer so we can write the length before the actual data
                IWriteMessage tempEventBuffer = new WriteOnlyMessage();
                try
                {
                    WriteEvent(tempEventBuffer, e, recipient);
                }
                catch (Exception exception)
                {
                    DebugConsole.ThrowError("Failed to write an event for the entity \"" + e.Entity + "\"", exception);
                    GameAnalyticsManager.AddErrorEventOnce("NetEntityEventManager.Write:WriteFailed" + e.Entity.ToString(),
                        GameAnalyticsManager.ErrorSeverity.Error,
                        "Failed to write an event for the entity \"" + e.Entity + "\"\n" + exception.StackTrace.CleanupStackTrace());

                    //write an empty event to avoid messing up IDs
                    //(otherwise the clients might read the next event in the message and think its ID 
                    //is consecutive to the previous one, even though we skipped over this broken event)
                    tempBuffer.WriteUInt16(Entity.NullEntityID);
                    eventCount++;
                    continue;
                }

                if (eventCount > 0 &&
                    msg.LengthBytes + tempBuffer.LengthBytes + tempEventBuffer.LengthBytes > MaxEventBufferLength)
                {
                    //no more room in this packet
                    break;
                }

                tempBuffer.WriteUInt16(e.EntityID);
                tempBuffer.WriteVariableUInt32((uint)tempEventBuffer.LengthBytes);
                tempBuffer.WriteBytes(tempEventBuffer.Buffer, 0, tempEventBuffer.LengthBytes);
                sentEvents.Add(e);

                eventCount++;
            }
            
            if (eventCount > 0)
            {
                msg.WritePadBits();
                msg.WriteUInt16(eventsToSync[0].ID);
                msg.WriteByte((byte)eventCount);
                msg.WriteBytes(tempBuffer.Buffer, 0, tempBuffer.LengthBytes);
            }
        }

        protected static bool ValidateEntity(INetSerializable entity)
        {
            void error(string reason)
                => DebugConsole.ThrowError($"Can't create an entity event for {entity} - {reason}.\n{Environment.StackTrace.CleanupStackTrace()}");

            if (entity is Entity { Removed: var removed, IdFreed: var idFreed })
            {
                if (removed)
                {
                    error("the entity has been removed");
                    return false;
                }
                if (idFreed)
                {
                    error("the ID of the entity has been freed");
                    return false;
                }
            }
            else
            {
                error($"input is not of type {nameof(Entity)}");
                return false;
            }
            return true;
        }

        protected abstract void WriteEvent(IWriteMessage buffer, NetEntityEvent entityEvent, Client recipient = null);
    }    
}
