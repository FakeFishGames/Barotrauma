using Barotrauma.Networking;

namespace Barotrauma
{
    partial class AlienRuinMission : Mission
    {
        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            msg.Write((ushort)existingTargets.Count);
            foreach (var t in existingTargets)
            {
                msg.Write(t != null ? t.ID : Entity.NullEntityID);
            }
            msg.Write((ushort)spawnedTargets.Count);
            foreach (var t in spawnedTargets)
            {
                t.WriteSpawnData(msg, t.ID, false);
            }
        }
    }
}