using Barotrauma.Networking;
using System;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class SalvageMission : Mission
    {
        struct SpawnInfo
        {
            public readonly bool UsedExistingItem;
            public readonly UInt16 OriginalInventoryID;
            public readonly byte OriginalItemContainerIndex;
            public readonly int OriginalSlotIndex;
            public readonly List<(int listIndex, int effectIndex)> ExecutedEffectIndices;

            public SpawnInfo(bool usedExistingItem, UInt16 originalInventoryID, byte originalItemContainerIndex, int originalSlotIndex, List<(int listIndex, int effectIndex)> executedEffectIndices)
            {
                UsedExistingItem = usedExistingItem;
                OriginalInventoryID = originalInventoryID;
                OriginalItemContainerIndex = originalItemContainerIndex;
                OriginalSlotIndex = originalSlotIndex;
                ExecutedEffectIndices = executedEffectIndices;
            }
        }

        private readonly Dictionary<Target, SpawnInfo> spawnInfo = new Dictionary<Target, SpawnInfo>();

        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            base.ServerWriteInitial(msg, c);

            foreach (var target in targets)
            {
                bool targetFound = spawnInfo.ContainsKey(target) && target.Item != null;
                msg.WriteBoolean(targetFound);
                if (!targetFound) { continue; }

                msg.WriteBoolean(spawnInfo[target].UsedExistingItem);
                if (spawnInfo[target].UsedExistingItem)
                {
                    msg.WriteUInt16(target.Item.ID);
                }
                else
                {
                    target.Item.WriteSpawnData(msg, 
                        target.Item.ID, 
                        spawnInfo[target].OriginalInventoryID, 
                        spawnInfo[target].OriginalItemContainerIndex,
                        spawnInfo[target].OriginalSlotIndex);
                }

                msg.WriteByte((byte)spawnInfo[target].ExecutedEffectIndices.Count);
                foreach ((int listIndex, int effectIndex) in spawnInfo[target].ExecutedEffectIndices)
                {
                    msg.WriteByte((byte)listIndex);
                    msg.WriteByte((byte)effectIndex);
                }
            }
        }

        public override void ServerWrite(IWriteMessage msg)
        {
            base.ServerWrite(msg);
            msg.WriteByte((byte)targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                msg.WriteByte((byte)targets[i].State);
            }
        }
    }
}
