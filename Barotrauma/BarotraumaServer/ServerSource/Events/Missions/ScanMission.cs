using Barotrauma.Networking;

namespace Barotrauma
{
    partial class ScanMission : Mission
    {
        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            base.ServerWriteInitial(msg, c);
            msg.Write((ushort)startingItems.Count);
            foreach (var item in startingItems)
            {
                item.WriteSpawnData(msg,
                    item.ID,
                    parentInventoryIDs.ContainsKey(item) ? parentInventoryIDs[item] : Entity.NullEntityID,
                    parentItemContainerIndices.ContainsKey(item) ? parentItemContainerIndices[item] : (byte)0);
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
            msg.Write((byte)scanTargets.Count);
            foreach (var kvp in scanTargets)
            {
                msg.Write(kvp.Key != null ? kvp.Key.ID : Entity.NullEntityID);
                msg.Write(kvp.Value);
            }
        }
    }
}