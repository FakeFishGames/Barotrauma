using Barotrauma;
using Barotrauma.Networking;
using Barotrauma.Plugins;

namespace HomeAndHarbor
{
    public partial class ItemEntityEventHandler : ICustomSerializable<Item>
    {
        public void ClientWrite(Item entity, IWriteMessage msg, NetEntityEvent.IData? extraData)
        {
            // do nothing
        }

        public void ClientRead(Item entity, IReadMessage msg, float sendingTime)
        {
            Identifier id = msg.ReadIdentifier();
            DebugConsole.NewMessage($"Received item with identifier: {id}");
        }
    }
}