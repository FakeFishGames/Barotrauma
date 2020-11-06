using Barotrauma.Networking;

namespace Barotrauma
{
    partial class NestMission : Mission
    {
        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            msg.Write(nestPosition.X);
            msg.Write(nestPosition.Y);
            msg.Write((ushort)items.Count);
            foreach (Item item in items)
            {
                item.WriteSpawnData(msg, item.ID, Entity.NullEntityID, 0);
            }
        }
    }
}
