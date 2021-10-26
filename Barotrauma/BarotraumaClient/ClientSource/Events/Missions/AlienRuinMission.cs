using Barotrauma.Networking;

namespace Barotrauma
{
    partial class AlienRuinMission : Mission
    {
        public override void ClientReadInitial(IReadMessage msg)
        {
            base.ClientReadInitial(msg);
            existingTargets.Clear();
            spawnedTargets.Clear();
            allTargets.Clear();
            ushort existingTargetsCount = msg.ReadUInt16();
            for (int i = 0; i < existingTargetsCount; i++)
            {
                ushort targetId = msg.ReadUInt16();
                if (targetId == Entity.NullEntityID) { continue; }
                Entity target = Entity.FindEntityByID(targetId);
                if (target == null) { continue; }
                existingTargets.Add(target);
                allTargets.Add(target);
            }
            ushort spawnedTargetsCount = msg.ReadUInt16();
            for (int i = 0; i < spawnedTargetsCount; i++)
            {
                var enemy = Character.ReadSpawnData(msg);
                existingTargets.Add(enemy);
                allTargets.Add(enemy);
            }
        }
    }
}