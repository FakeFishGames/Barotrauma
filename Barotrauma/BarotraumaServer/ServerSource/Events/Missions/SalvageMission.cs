using Barotrauma.Networking;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class SalvageMission : Mission
    {
        private bool usedExistingItem;

        private UInt16 originalInventoryID;
        private byte originalItemContainerIndex;
        private int originalSlotIndex;

        private readonly List<Pair<int, int>> executedEffectIndices = new List<Pair<int, int>>();

        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            base.ServerWriteInitial(msg, c);

            msg.WriteBoolean(usedExistingItem);
            if (usedExistingItem)
            {
                msg.WriteUInt16(item.ID);
            }
            else
            {
                item.WriteSpawnData(msg, item.ID, originalInventoryID, originalItemContainerIndex, originalSlotIndex);
            }

            msg.WriteByte((byte)executedEffectIndices.Count);
            foreach (Pair<int, int> effectIndex in executedEffectIndices)
            {
                msg.WriteByte((byte)effectIndex.First);
                msg.WriteByte((byte)effectIndex.Second);
            }
        }
    }
}
