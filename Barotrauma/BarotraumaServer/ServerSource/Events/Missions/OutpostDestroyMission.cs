using Barotrauma.Networking;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class OutpostDestroyMission : AbandonedOutpostMission
    {
        private readonly List<Item> spawnedItems = new List<Item>();
        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            base.ServerWriteInitial(msg, c);
            msg.Write((ushort)spawnedItems.Count);
            foreach (Item item in spawnedItems)
            {
                item.WriteSpawnData(msg, item.ID, Entity.NullEntityID, 0);
            }
        }
    }
}