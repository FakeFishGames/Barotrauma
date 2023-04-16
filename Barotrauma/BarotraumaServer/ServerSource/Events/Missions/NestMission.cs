using Barotrauma.Networking;

namespace Barotrauma
{
    partial class NestMission : Mission
    {
        private Level.Cave selectedCave; 

        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            base.ServerWriteInitial(msg, c);
            msg.WriteByte((byte)(selectedCave == null || Level.Loaded == null || !Level.Loaded.Caves.Contains(selectedCave) ? 255 : Level.Loaded.Caves.IndexOf(selectedCave)));
            msg.WriteSingle(nestPosition.X);
            msg.WriteSingle(nestPosition.Y);
            msg.WriteUInt16((ushort)items.Count);
            foreach (Item item in items)
            {
                item.WriteSpawnData(msg, item.ID, Entity.NullEntityID, 0, -1);
            }
        }
    }
}
