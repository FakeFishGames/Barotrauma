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
            
            msg.WriteByte((byte)characters.Count);
            foreach (Character character in characters)
            {
                character.WriteSpawnData(msg, character.ID, restrictMessageSize: false);
                var items = characterItems[character];
                msg.WriteUInt16((ushort)items.Count);
                foreach (Item item in items)
                {
                    item.WriteSpawnData(msg, item.ID, item.ParentInventory?.Owner?.ID ?? Entity.NullEntityID, 0, item.ParentInventory?.FindIndex(item) ?? -1);
                }
            }

            foreach (var target in targets)
            {
                bool targetFound = spawnInfo.TryGetValue(target, out SpawnInfo sInfo) && target.Item != null;
                msg.WriteBoolean(targetFound);
                if (!targetFound) { continue; }
                msg.WriteBoolean(sInfo.UsedExistingItem);
                if (sInfo.UsedExistingItem)
                {
                    msg.WriteUInt16(target.Item.ID);
                }
                else
                {
                    target.Item.WriteSpawnData(msg, 
                        target.Item.ID, 
                        sInfo.OriginalInventoryID, 
                        sInfo.OriginalItemContainerIndex,
                        sInfo.OriginalSlotIndex);
                    msg.WriteUInt16(target.ParentTarget?.Item?.ID ?? Entity.NullEntityID);
                }

                msg.WriteByte((byte)sInfo.ExecutedEffectIndices.Count);
                foreach ((int listIndex, int effectIndex) in sInfo.ExecutedEffectIndices)
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
            foreach (Target t in targets)
            {
                msg.WriteByte((byte)t.State);
            }
        }
    }
}
