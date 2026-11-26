using System;
using Barotrauma.Plugins;

namespace Barotrauma.Networking
{
    public abstract class NetEntityEvent
    {
        public interface IData { }

        public readonly Entity Entity;
        public readonly UInt16 ID;

        /// <summary>
        /// Optional hash of the plugin content package that the entity event may be associated with.
        /// None in the case of a vanilla event and Some in the case of an event sent by a plugin via
        /// <see cref="IGameNetwork.CreateEntityEvent{TSerializable}"/>.
        /// Used to determine if the event should be handled via vanilla methods or passed to custom
        /// plugin-defined methods via <see cref="PluginData.WriteEvent"/>
        /// </summary>
        public readonly Option<uint> PluginContentPackageHash;

        public UInt16 EntityID => Entity.ID;

        //arbitrary extra data that will be passed to the Write method of the serializable entity
        //(the index of an itemcomponent for example)
        public IData Data { get; private set; }

        public bool Sent;

        protected NetEntityEvent(INetSerializable serializableEntity, UInt16 id, Option<uint> pluginContentPackageHash)
        {
            this.ID = id;
            this.Entity = serializableEntity as Entity;
            this.PluginContentPackageHash = pluginContentPackageHash;
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
