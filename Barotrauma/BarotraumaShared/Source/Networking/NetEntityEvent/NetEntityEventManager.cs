using Lidgren.Network;
using System;
using System.Collections.Generic;

namespace Barotrauma.Networking
{
    abstract class NetEntityEventManager
    {
        public const int MaxEventBufferLength = 1024;
        
        /// <summary>
        /// Write the events to the outgoing message. The recipient parameter is only needed for ServerEntityEventManager
        /// </summary>
        protected void Write(NetOutgoingMessage msg, List<NetEntityEvent> eventsToSync, out List<NetEntityEvent> sentEvents, Client recipient = null)
        {
            //write into a temporary buffer so we can write the number of events before the actual data
            NetBuffer tempBuffer = new NetBuffer();

            sentEvents = new List<NetEntityEvent>();

            int eventCount = 0;
            foreach (NetEntityEvent e in eventsToSync)
            {
                //write into a temporary buffer so we can write the length before the actual data
                NetBuffer tempEventBuffer = new NetBuffer();
                try
                {
                    WriteEvent(tempEventBuffer, e, recipient);
                }

                catch (Exception exception)
                {
                    DebugConsole.ThrowError("Failed to write an event for the entity \"" + e.Entity + "\"", exception);
                    GameAnalyticsManager.AddErrorEventOnce("NetEntityEventManager.Write:WriteFailed" + e.Entity.ToString(),
                        GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Failed to write an event for the entity \"" + e.Entity + "\"\n" + exception.StackTrace);

                    //write an empty event to avoid messing up IDs
                    //(otherwise the clients might read the next event in the message and think its ID 
                    //is consecutive to the previous one, even though we skipped over this broken event)
                    tempBuffer.Write(Entity.NullEntityID);
                    tempBuffer.WritePadBits();
                    eventCount++;
                    continue;
                }

                if (msg.LengthBytes + tempBuffer.LengthBytes + tempEventBuffer.LengthBytes > MaxEventBufferLength)
                {
                    //no more room in this packet
                    break;
                }

                if (tempEventBuffer.LengthBytes > 255)
                {
                    DebugConsole.ThrowError("Too much data in network event for entity \"" + e.Entity.ToString() + "\" (" + tempEventBuffer.LengthBytes + " bytes");
                    GameAnalyticsManager.AddErrorEventOnce("NetEntityEventManager.Write:TooLong" + e.Entity.ToString(),
                        GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Too much data in network event for entity \"" + e.Entity.ToString() + "\" (" + tempEventBuffer.LengthBytes + " bytes");

                    //write an empty event to prevent breaking the event syncing
                    tempBuffer.Write((UInt16)0);
                    tempBuffer.WritePadBits();
                    eventCount++;
                    continue;
                }
                //the ID has been taken by another entity (the original entity has been removed) -> write an empty event
                /*else if (Entity.FindEntityByID(e.Entity.ID) != e.Entity || e.Entity.IdFreed)
                {
                    //technically the clients don't have any use for these, but removing events and shifting the IDs of all 
                    //consecutive ones is so error-prone that I think this is a safer option
                    tempBuffer.Write(Entity.NullEntityID);
                    tempBuffer.WritePadBits();
                }*/
                else
                {
                    tempBuffer.Write((UInt16)e.Entity.ID);
                    tempBuffer.Write((byte)tempEventBuffer.LengthBytes);
                    tempBuffer.Write(tempEventBuffer);
                    tempBuffer.WritePadBits();
                    sentEvents.Add(e);
                }

                eventCount++;
            }
            
            if (eventCount > 0)
            {
                msg.Write(eventsToSync[0].ID);
                msg.Write((byte)eventCount);
                msg.Write(tempBuffer);
            }
        }

        protected abstract void WriteEvent(NetBuffer buffer, NetEntityEvent entityEvent, Client recipient = null);
    }    
}
