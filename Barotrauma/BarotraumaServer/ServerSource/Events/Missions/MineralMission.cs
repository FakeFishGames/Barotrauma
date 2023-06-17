using Barotrauma.Networking;
using System.Linq;

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
                msg.WriteSingle(kvp.Value.FirstOrDefault()?.Rotation ?? 0.0f);
                foreach (var item in kvp.Value)
                {
                    item.WriteSpawnData(msg, item.ID, Entity.NullEntityID, 0, -1);
                }
            }

            foreach (var kvp in relevantLevelResources)
            {
                msg.WriteIdentifier(kvp.Key);
                msg.WriteByte((byte)kvp.Value.Length);
                foreach (var item in kvp.Value)
                {
                    msg.WriteUInt16(item.ID);
                }
            }
        }
    }
}
