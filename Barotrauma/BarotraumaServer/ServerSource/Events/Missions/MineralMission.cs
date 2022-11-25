using Barotrauma.Networking;

namespace Barotrauma
{
    partial class MineralMission : Mission
    {
        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            base.ServerWriteInitial(msg, c);
            msg.WriteByte((byte)caves.Count);
            foreach (var cave in caves)
            {
                msg.WriteByte((byte)(Level.Loaded == null || !Level.Loaded.Caves.Contains(cave) ? 255 : Level.Loaded.Caves.IndexOf(cave)));
            }

            foreach (var kvp in spawnedResources)
            {
                msg.WriteByte((byte)kvp.Value.Count);
                var rotation = resourceClusters[kvp.Key].Rotation;
                msg.WriteSingle(rotation);
                foreach (var r in kvp.Value)
                {
                    r.WriteSpawnData(msg, r.ID, Entity.NullEntityID, 0, -1);
                }
            }

            foreach (var kvp in relevantLevelResources)
            {
                msg.WriteIdentifier(kvp.Key);
                msg.WriteByte((byte)kvp.Value.Length);
                foreach (var i in kvp.Value)
                {
                    msg.WriteUInt16(i.ID);
                }
            }
        }
    }
}
