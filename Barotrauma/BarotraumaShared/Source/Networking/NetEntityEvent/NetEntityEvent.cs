using System;

namespace Barotrauma.Networking
{
    abstract class NetEntityEvent
    {
        public enum Type
        {
            Invalid,
            ComponentState, 
            InventoryState,
            Status,
            Treatment,
            ApplyStatusEffect,
            ChangeProperty,
            Control,
            UpdateSkills
        }

        public readonly Entity Entity;
        public readonly UInt16 ID;

        public UInt16 EntityID
        {
            get;
            private set;
        }

        //arbitrary extra data that will be passed to the Write method of the serializable entity
        //(the index of an itemcomponent for example)
        protected object[] Data;

        public bool Sent;

        protected NetEntityEvent(INetSerializable serializableEntity, UInt16 id)
        {
            this.ID = id;
            this.Entity = serializableEntity as Entity;
            RefreshEntityID();
        }

        public void RefreshEntityID()
        {
            this.EntityID = this.Entity is Entity entity ? entity.ID : Entity.NullEntityID;
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
                if (Data.Length != other.Data.Length) return false;
                
                for (int i = 0; i < Data.Length; i++)
                {
                    if (Data[i] == null)
                    {
                        if (other.Data[i] != null) return false;
                    }
                    else
                    {
                        if (other.Data[i] == null) return false;
                        if (!Data[i].Equals(other.Data[i])) return false;
                    }
                }
                return true;
            }

            return Data == other.Data;
        }
    }
}
