using Barotrauma.Networking;

namespace Barotrauma
{
    partial class CargoMission : Mission
    {
        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            msg.Write((ushort)items.Count);
            foreach (Item item in items)
            {
                item.WriteSpawnData(msg, item.ID, item.ParentInventory?.Owner?.ID ?? 0);
            }
        }
    }
}
