using Barotrauma.Networking;

namespace Barotrauma
{
    partial class MineralMission : Mission
    {
        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            foreach (var kvp in SpawnedResources)
            {
                msg.Write((byte)kvp.Value.Count);
                var rotation = ResourceClusters[kvp.Key].Second;
                msg.Write(rotation);
                foreach (var r in kvp.Value)
                {
                    r.WriteSpawnData(msg, r.ID, Entity.NullEntityID, 0);
                }
            }

            foreach (var kvp in RelevantLevelResources)
            {
                msg.Write(kvp.Key);
                msg.Write((byte)kvp.Value.Length);
                foreach (var i in kvp.Value)
                {
                    msg.Write(i.ID);
                }
            }
        }
    }
}
