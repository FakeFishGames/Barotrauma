using Barotrauma.Networking;

namespace Barotrauma
{
    partial class ScanMission : Mission
    {
        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            base.ServerWriteInitial(msg, c);
            msg.WriteUInt16((ushort)startingItems.Count);
            foreach (var item in startingItems)
            {
                item.WriteSpawnData(msg,
                    item.ID,
                    parentInventoryIDs.ContainsKey(item) ? parentInventoryIDs[item] : Entity.NullEntityID,
                    parentItemContainerIndices.ContainsKey(item) ? parentItemContainerIndices[item] : (byte)0,
                    inventorySlotIndices.ContainsKey(item) ? inventorySlotIndices[item] : -1);
            }
            ServerWriteScanTargetStatus(msg);
        }

        public override void ServerWrite(IWriteMessage msg)
        {
            base.ServerWrite(msg);
            ServerWriteScanTargetStatus(msg);
        }

        private void ServerWriteScanTargetStatus(IWriteMessage msg)
        {
            msg.WriteByte((byte)scanTargets.Count);
            foreach (var kvp in scanTargets)
            {
                msg.WriteUInt16(kvp.Key != null ? kvp.Key.ID : Entity.NullEntityID);
                msg.WriteBoolean(kvp.Value);
            }
        }
    }
}