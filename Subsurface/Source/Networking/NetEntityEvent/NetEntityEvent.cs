using Lidgren.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Barotrauma.Networking
{
    abstract class NetEntityEvent
    {
        public enum Type
        {
            ComponentState, 
            InventoryState,
            Status
        }

        public readonly Entity Entity;
        public readonly UInt32 ID;

        //arbitrary extra data that will be passed to the Write method of the serializable entity
        //(the index of an itemcomponent for example)
        protected object[] Data;

        protected NetEntityEvent(INetSerializable entity, UInt32 id)
        {
            this.ID = id;
            this.Entity = entity as Entity;
        }

        public void SetData(object[] data)
        {
            this.Data = data;
        }

        public bool IsDuplicate(NetEntityEvent other)
        {
            if (other.Entity != this.Entity) return false;

            if (Data != null && other.Data != null)
            {
                if (Data.Length == other.Data.Length)
                {
                    for (int i = 0; i<Data.Length; i++)
                    {
                        if (!Data[i].Equals(other.Data[i])) return false;
                    }
                    return true;
                }
                else
                {
                    return false;
                }
            }

            return Data == other.Data;
        }
    }

    class ServerEntityEvent : NetEntityEvent
    {
        private IServerSerializable serializable;

        public bool Sent;

        public ServerEntityEvent(IServerSerializable entity, UInt32 id)
            : base(entity, id)
        { 
            serializable = entity;
        }

        public void Write(NetBuffer msg, Client recipient)
        {
            serializable.ServerWrite(msg, recipient, Data);
        } 
    }
    
    class ClientEntityEvent : NetEntityEvent
    {
        private IClientSerializable serializable;

        public ClientEntityEvent(IClientSerializable entity, UInt32 id)
            : base(entity, id)
        { 
            serializable = entity;
        }

        public void Write(NetBuffer msg)
        {
            serializable.ClientWrite(msg, Data);
        } 
    }

}
