using Barotrauma.Networking;

namespace Barotrauma
{
    partial class NestMission : Mission
    {
        private Level.Cave selectedCave; 

        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            base.ServerWriteInitial(msg, c);
            msg.Write((byte)(selectedCave == null || Level.Loaded == null || !Level.Loaded.Caves.Contains(selectedCave) ? 255 : Level.Loaded.Caves.IndexOf(selectedCave)));
            msg.Write(nestPosition.X);
            msg.Write(nestPosition.Y);
            msg.Write((ushort)items.Count);
            foreach (Item item in items)
            {
                item.WriteSpawnData(msg, item.ID, Entity.NullEntityID, 0, -1);
            }
        }
    }
}
