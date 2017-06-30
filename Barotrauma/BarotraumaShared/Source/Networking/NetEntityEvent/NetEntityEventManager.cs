using Lidgren.Network;
using System;
using System.Collections.Generic;

namespace Barotrauma.Networking
{
    abstract class NetEntityEventManager
    {
        public const int MaxEventBufferLength = 1024;
        public const int MaxEventsPerWrite = 64;
        
        /// <summary>
        /// Write the events to the outgoing message. The recipient parameter is only needed for ServerEntityEventManager
        /// </summary>
        protected void Write(NetOutgoingMessage msg, List<NetEntityEvent> eventsToSync, Client recipient = null)
        {
            //write into a temporary buffer so we can write the number of events before the actual data
            NetBuffer tempBuffer = new NetBuffer();

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

                    //write an empty event to avoid messing up IDs
                    //(otherwise the clients might read the next event in the message and think its ID 
                    //is consecutive to the previous one, even though we skipped over this broken event)
                    tempBuffer.Write((UInt16)0);
                    tempBuffer.WritePadBits();
                    eventCount++;
                    continue;
                }

                if (msg.LengthBytes + tempBuffer.LengthBytes + tempEventBuffer.LengthBytes > NetPeerConfiguration.kDefaultMTU - 20)
                {
                    //no more room in this packet
                    break;
                }
                
                //the ID has been taken by another entity (the original entity has been removed) -> write an empty event
                if (Entity.FindEntityByID(e.Entity.ID) != e.Entity)
                {
                    //technically the clients don't have any use for these, but removing events and shifting the IDs of all 
                    //consecutive ones is so error-prone that I think this is a safer option
                    tempBuffer.Write((UInt16)0);
                    tempBuffer.WritePadBits();
                }
                else
                {
                    tempBuffer.Write((UInt16)e.Entity.ID);
                    tempBuffer.Write((byte)tempEventBuffer.LengthBytes);
                    tempBuffer.Write(tempEventBuffer);
                    tempBuffer.WritePadBits();
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
       
        protected virtual void WriteEvent(NetBuffer buffer, NetEntityEvent entityEvent, Client recipient = null)
        {
            throw new NotImplementedException();
        }
    }    
}
