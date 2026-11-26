#nullable enable
using Barotrauma.Networking;

namespace Barotrauma.Items.Components
{
    public partial class Planter
    {
        private const float ServerUpdateDelay = 1.0f;
        private float serverUpdateTimer;

        private float lastSentWater, lastSentFertilizer, lastSentSalinity, lastSentTargetWaterLevel;

        private bool NeedsUpdate =>
            !MathUtils.NearlyEqual(lastSentWater, CurrentWater) ||
            !MathUtils.NearlyEqual(lastSentFertilizer, Fertilizer) ||
            !MathUtils.NearlyEqual(lastSentSalinity, Salinity) ||
            !MathUtils.NearlyEqual(lastSentTargetWaterLevel, TargetWaterLevel);

        public override void ServerEventWrite(IWriteMessage msg, Client c, NetEntityEvent.IData? extraData = default)
        {
            msg.WriteRangedSingle(CurrentWater, 0f, MaxWater, 16);
            msg.WriteRangedSingle(Fertilizer, 0f, FertilizerCapacity, 16);
            msg.WriteRangedSingle(Salinity, 0f, SeawaterSalinity, 16);
            msg.WriteRangedSingle(TargetWaterLevel, 0f, 100f, 16);
        }

        public void ServerEventRead(IReadMessage msg, Client c)
        {
            PlanterSyncEvent syncEvent = (PlanterSyncEvent)msg.ReadByte();

            float incomingTargetWaterLevel = 0f;
            ushort plantedSeedId = Entity.NullEntityID;
            if (syncEvent == PlanterSyncEvent.Settings)
            {
                incomingTargetWaterLevel = msg.ReadRangedSingle(0f, 100f, 16);
            }
            else if (syncEvent == PlanterSyncEvent.Plant)
            {
                plantedSeedId = msg.ReadUInt16();
            }

            if (!item.CanClientAccess(c)) { return; }

            switch (syncEvent)
            {
                case PlanterSyncEvent.Uproot:
                    c.Character.SelectedItem = null;
                    StartUprootingOrPlanting(c.Character);
                    break;
                case PlanterSyncEvent.Plant:
                    // Check the client can actually access the seed they appear to be planting
                    if (Entity.FindEntityByID(plantedSeedId) is Item { ParentInventory: Inventory seedInventory } seedItem &&
                        c.Character.CanAccessInventory(seedInventory))
                    {
                        // Deselect so the picking coroutine isn't immediately cancelled (same as with uprooting)
                        c.Character.SelectedItem = null;
                        TryPlantItem(seedItem, c.Character);
                    }
                    break;
                case PlanterSyncEvent.Empty:
                    c.Character.SelectedItem = null;
                    StartEmptying(c.Character);
                    break;
                case PlanterSyncEvent.Settings:
                    TargetWaterLevel = incomingTargetWaterLevel;
                    ServerUpdateNow();
                    break;
            }
        }


        private void ServerUpdateNow()
        {
            item.CreateServerEvent(this);
            serverUpdateTimer = 0;

            lastSentWater = CurrentWater;
            lastSentFertilizer = Fertilizer;
            lastSentSalinity = Salinity;
            lastSentTargetWaterLevel = TargetWaterLevel;
        }
    }
}