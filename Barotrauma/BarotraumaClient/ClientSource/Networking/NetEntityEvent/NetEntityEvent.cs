using System;

namespace Barotrauma.Networking
{
    class ClientEntityEvent : NetEntityEvent
    {
        public readonly IClientSerializable Serializable;

        public readonly UInt16 CharacterStateID;

        /// <inheritdoc cref="NetworkMember.CreateEntityEvent"/>
        public ClientEntityEvent(IClientSerializable entity, UInt16 eventId, UInt16 characterStateId, Option<uint> pluginContentPackageHash)
            : base(entity, eventId, pluginContentPackageHash)
        {
            Serializable = entity;
            CharacterStateID = characterStateId;
        }

        public void Write(IWriteMessage msg)
        {
            msg.WriteUInt16(CharacterStateID);
            Serializable.ClientEventWrite(msg, Data);
        }
    }
}
