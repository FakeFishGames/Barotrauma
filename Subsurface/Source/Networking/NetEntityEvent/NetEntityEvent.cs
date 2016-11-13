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
    }

    class ServerEntityEvent : NetEntityEvent
    {
        private IServerSerializable serializable;

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
