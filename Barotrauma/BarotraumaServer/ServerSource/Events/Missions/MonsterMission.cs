using Barotrauma.Networking;
using System;

namespace Barotrauma
{
    partial class MonsterMission : Mission
    {
        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            base.ServerWriteInitial(msg, c);

            if (monsters.Count == 0 && monsterPrefabs.Count > 0)
            {
                throw new InvalidOperationException("Server attempted to write monster mission data when no monsters had been spawned.");
            }

            msg.Write((byte)monsters.Count);
            foreach (Character monster in monsters)
            {
                monster.WriteSpawnData(msg, monster.ID, restrictMessageSize: false);
            }
        }
    }
}
