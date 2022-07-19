using System;

namespace Barotrauma.Networking
{
    class ClientEntityEvent : NetEntityEvent
    {
        private readonly IClientSerializable serializable;

        public readonly UInt16 CharacterStateID;

        public ClientEntityEvent(IClientSerializable entity, UInt16 eventId, UInt16 characterStateId)
            : base(entity, eventId)
        {
            serializable = entity;
            CharacterStateID = characterStateId;
        }

        public void Write(IWriteMessage msg)
        {
            msg.Write(CharacterStateID);
            serializable.ClientEventWrite(msg, Data);
        }
    }
}
