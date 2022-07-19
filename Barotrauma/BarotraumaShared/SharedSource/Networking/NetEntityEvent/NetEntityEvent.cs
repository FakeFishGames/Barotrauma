using System;

namespace Barotrauma.Networking
{
    abstract class NetEntityEvent
    {
        public interface IData { }

        public readonly Entity Entity;
        public readonly UInt16 ID;

        public UInt16 EntityID => Entity.ID;

        //arbitrary extra data that will be passed to the Write method of the serializable entity
        //(the index of an itemcomponent for example)
        public IData Data { get; private set; }

        public bool Sent;

        protected NetEntityEvent(INetSerializable serializableEntity, UInt16 id)
        {
            this.ID = id;
            this.Entity = serializableEntity as Entity;
        }

        public void SetData(IData data)
        {
            this.Data = data;
        }

        public bool IsDuplicate(NetEntityEvent other)
        {
            if (other.Entity != this.Entity) { return false; }

            return Equals(Data, other.Data);
        }
    }
}
