using Barotrauma;
using Barotrauma.Networking;
using Barotrauma.Plugins;

namespace HomeAndHarbor
{
    public class ItemEntityEventHandler : ICustomSerializable<Item>
    {
        public void ServerWrite(Item entity, IWriteMessage msg, Client c, NetEntityEvent.IData? extraData) { msg.WriteIdentifier(entity.Prefab.Identifier); }

        public void ServerRead(Item entity, IReadMessage msg, Client c)
        {
            // do nothing
        }
    }
}