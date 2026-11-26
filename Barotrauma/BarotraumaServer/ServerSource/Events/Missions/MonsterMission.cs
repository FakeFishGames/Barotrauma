using Barotrauma.Networking;
using System;

namespace Barotrauma
{
    partial class MonsterMission : Mission
    {
        private const float EndDelay = 5.0f;
        private float endTimer;

        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            base.ServerWriteInitial(msg, c);

            if (allMonsters.Count == 0 && monsterWaves.Count > 0)
            {
                throw new InvalidOperationException("Server attempted to write monster mission data when no monsters had been spawned.");
            }

            msg.WriteByte((byte)allMonsters.Count);
            foreach (Character monster in allMonsters)
            {
                monster.WriteSpawnData(msg, monster.ID, restrictMessageSize: false);
            }
        }
    }
}
