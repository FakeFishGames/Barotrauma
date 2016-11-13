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
        const int MaxEventsPerWrite = 255;

        public UInt32 LastReceivedEntityEventID
        {
            get { return lastReceivedEntityEventID; }
        }

        private UInt32 lastReceivedEntityEventID;
                
        /// <summary>
        /// Write the events to the outgoing message. The recipient parameter is only needed for ServerEntityEventManager
        /// </summary>
        protected void Write(NetOutgoingMessage msg, List<NetEntityEvent> eventsToSync, Client recipient = null)
        {
            //too many events for one packet
            if (eventsToSync.Count > MaxEventsPerWrite)
            {
                eventsToSync.RemoveRange(MaxEventsPerWrite, eventsToSync.Count - MaxEventsPerWrite);
            }

            msg.Write((byte)ServerNetObject.ENTITY_STATE);

            msg.Write(eventsToSync[0].ID);
            msg.Write((byte)eventsToSync.Count);

            foreach (NetEntityEvent e in eventsToSync)
            {
                //write into a temporary buffer so we can write the length before the actual data
                NetBuffer tempBuffer = new NetBuffer();
                WriteEvent(tempBuffer, e, recipient);
                tempBuffer.WritePadBits();

                Debug.Assert(
                    tempBuffer.LengthBytes < 256, 
                    "Maximum EntityEvent size exceeded when serializing \""+e.Entity+"\"!");

                msg.Write((UInt16)e.Entity.ID);
                msg.Write((byte)tempBuffer.LengthBytes);
                msg.Write(tempBuffer);
            }
        }

        /// <summary>
        /// Read the events from the message, ignoring ones we've already received
        /// </summary>
        public void Read(NetIncomingMessage msg, float sendingTime)
        {
            UInt32 firstEventID = msg.ReadUInt32();
            byte eventCount = msg.ReadByte();

            for (int i = 0; i < eventCount; i++)
            {
                UInt32 thisEventID  = firstEventID + (UInt32)i;
                UInt16 entityID     = msg.ReadUInt16();
                byte msgLength      = msg.ReadByte();

                INetSerializable entity = Entity.FindEntityByID(entityID) as INetSerializable;

                //skip the event if we've already received it or if the entity isn't found
                if (thisEventID != lastReceivedEntityEventID+1 || entity == null)
                {
                    DebugConsole.NewMessage("received msg "+thisEventID, Microsoft.Xna.Framework.Color.Red);
                    msg.Position += msgLength * 8; 
                }
                else
                {
                    DebugConsole.NewMessage("received msg "+thisEventID, Microsoft.Xna.Framework.Color.Green);
                    ReadEvent(msg, entity, sendingTime);
                    lastReceivedEntityEventID = thisEventID;
                }
                msg.ReadPadBits();
            }            
        }

        protected virtual void WriteEvent(NetBuffer buffer, NetEntityEvent entityEvent, Client recipient = null)
        {
            throw new NotImplementedException();
        }

        protected virtual void ReadEvent(NetIncomingMessage buffer, INetSerializable entity, float sendingTime, Client sender = null)
        {
            throw new NotImplementedException();
        }
    }    
}
