using System;

namespace Barotrauma.Networking
{
    class ClientEntityEvent : NetEntityEvent
    {
        private IClientSerializable serializable;

        public UInt16 CharacterStateID;

        public ClientEntityEvent(IClientSerializable entity, UInt16 id)
            : base(entity, id)
        {
            serializable = entity;
        }

        public void Write(IWriteMessage msg)
        {
            msg.Write(CharacterStateID);
            serializable.ClientWrite(msg, Data);
        }
    }
}
