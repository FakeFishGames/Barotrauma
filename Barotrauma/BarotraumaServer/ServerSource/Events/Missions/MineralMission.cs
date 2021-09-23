using Barotrauma.Networking;

namespace Barotrauma
{
    partial class MineralMission : Mission
    {
        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            msg.Write((byte)caves.Count);
            foreach (var cave in caves)
            {
                msg.Write((byte)(Level.Loaded == null || !Level.Loaded.Caves.Contains(cave) ? 255 : Level.Loaded.Caves.IndexOf(cave)));
            }

            foreach (var kvp in spawnedResources)
            {
                msg.Write((byte)kvp.Value.Count);
                var rotation = resourceClusters[kvp.Key].rotation;
                msg.Write(rotation);
                foreach (var r in kvp.Value)
                {
                    r.WriteSpawnData(msg, r.ID, Entity.NullEntityID, 0);
                }
            }

            foreach (var kvp in relevantLevelResources)
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
