using Barotrauma.Networking;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class SalvageMission : Mission
    {
        private bool usedExistingItem;

        private readonly List<Pair<int, int>> executedEffectIndices = new List<Pair<int, int>>();

        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            msg.Write(usedExistingItem);
            if (usedExistingItem)
            {
                msg.Write(item.ID);
            }
            else
            {
                item.WriteSpawnData(msg, item.ID, item.ParentInventory?.Owner?.ID ?? 0);
            }

            msg.Write((byte)executedEffectIndices.Count);
            foreach (Pair<int, int> effectIndex in executedEffectIndices)
            {
                msg.Write((byte)effectIndex.First);
                msg.Write((byte)effectIndex.Second);
            }
        }
    }
}
