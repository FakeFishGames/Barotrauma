using Barotrauma.Networking;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class SalvageMission : Mission
    {
        private bool usedExistingItem;

        private UInt16 originalItemID;
        private UInt16 originalInventoryID;

        private readonly List<Pair<int, int>> executedEffectIndices = new List<Pair<int, int>>();

        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            msg.Write(usedExistingItem);
            if (usedExistingItem)
            {
                msg.Write(originalItemID);
            }
            else
            {
                item.WriteSpawnData(msg, originalItemID, originalInventoryID);
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
