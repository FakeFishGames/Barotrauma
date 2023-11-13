using Barotrauma.Networking;

namespace Barotrauma
{
    partial class EndMission : Mission
    {
        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            base.ServerWriteInitial(msg, c);

            boss.WriteSpawnData(msg, boss.ID, restrictMessageSize: false);
            msg.WriteByte((byte)minions.Length);
            foreach (Character minion in minions)
            {
                minion.WriteSpawnData(msg, minion.ID, restrictMessageSize: false);
            }
        }
    }
}
