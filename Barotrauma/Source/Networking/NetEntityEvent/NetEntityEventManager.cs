using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    abstract class NetEntityEventManager
    {
        public const int MaxEventBufferLength = 1024;
        public const int MaxEventsPerWrite = 64;

        //public UInt16 LastReceivedEntityEventID
        //{
        //    get { return lastReceivedEntityEventID; }
        //}

        /// <summary>
        /// Write the events to the outgoing message. The recipient parameter is only needed for ServerEntityEventManager
        /// </summary>
        protected void Write(NetOutgoingMessage msg, List<NetEntityEvent> eventsToSync, Client recipient = null)
        {
            msg.Write(eventsToSync[0].ID);
            msg.Write((byte)eventsToSync.Count);

            foreach (NetEntityEvent e in eventsToSync)
            {
                //write into a temporary buffer so we can write the length before the actual data
                NetBuffer tempBuffer = new NetBuffer();
                try
                {
                    WriteEvent(tempBuffer, e, recipient);
                }

                catch (Exception exception)
                {
                    DebugConsole.ThrowError("Failed to write an event for the entity \""+e.Entity+"\"", exception);
                    continue;
                }

                Debug.Assert(
                    tempBuffer.LengthBytes < 128, 
                    "Maximum EntityEvent size exceeded when serializing \""+e.Entity+"\"!");

                //the ID has been taken by another entity (the original entity has been removed) -> write an empty event
                if (Entity.FindEntityByID(e.Entity.ID) != e.Entity)
                {
                    //technically the clients don't have any use for these, but removing events and shifting the IDs of all 
                    //consecutive ones is so error-prone that I think this is a safer option
                    msg.Write((UInt16)0);
                    msg.WritePadBits();
                }
                else
                {
                    msg.Write((UInt16)e.Entity.ID);
                    msg.Write((byte)tempBuffer.LengthBytes);
                    msg.Write(tempBuffer);
                    msg.WritePadBits();
                }
            }
        }
       
        protected virtual void WriteEvent(NetBuffer buffer, NetEntityEvent entityEvent, Client recipient = null)
        {
            throw new NotImplementedException();
        }
    }    
}
