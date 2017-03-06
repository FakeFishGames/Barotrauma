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
                WriteEvent(tempBuffer, e, recipient);

                Debug.Assert(
                    tempBuffer.LengthBytes < 128, 
                    "Maximum EntityEvent size exceeded when serializing \""+e.Entity+"\"!");

#if DEBUG
                if (Entity.FindEntityByID(e.Entity.ID) != e.Entity)
                {
                    DebugConsole.ThrowError("Error in NetEntityEventManager.Write (FindEntityByID(e.Entity.ID) != e.Entity)");
                }
#endif
                

                msg.Write((UInt16)e.Entity.ID);
                msg.Write((byte)tempBuffer.LengthBytes);
                msg.Write(tempBuffer);
                msg.WritePadBits();
            }
        }
       
        protected virtual void WriteEvent(NetBuffer buffer, NetEntityEvent entityEvent, Client recipient = null)
        {
            throw new NotImplementedException();
        }
    }    
}
