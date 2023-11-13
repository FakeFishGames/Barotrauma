using Barotrauma.Networking;

namespace Barotrauma
{
    partial class AlienRuinMission : Mission
    {
        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            base.ServerWriteInitial(msg, c);
            msg.WriteUInt16((ushort)existingTargets.Count);
            foreach (var t in existingTargets)
            {
                msg.WriteUInt16(t != null ? t.ID : Entity.NullEntityID);
            }
            msg.WriteUInt16((ushort)spawnedTargets.Count);
            foreach (var t in spawnedTargets)
            {
                t.WriteSpawnData(msg, t.ID, false);
            }
        }
    }
}