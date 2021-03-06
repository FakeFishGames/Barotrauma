using Barotrauma.Networking;

namespace Barotrauma
{
    partial class OutpostDestroyMission : AbandonedOutpostMission
    {
        public override void ClientReadInitial(IReadMessage msg)
        {
            base.ClientReadInitial(msg);
            ushort itemCount = msg.ReadUInt16();
            for (int i = 0; i < itemCount; i++)
            {
                var item = Item.ReadSpawnData(msg);
                items.Add(item);
            }
        }
    }
}