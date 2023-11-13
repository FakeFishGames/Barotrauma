using Barotrauma.Networking;

namespace Barotrauma
{
    partial class MonsterMission : Mission
    {
        public override bool DisplayAsCompleted => State > 0;
        public override bool DisplayAsFailed => false;

        public override void ClientReadInitial(IReadMessage msg)
        {
            base.ClientReadInitial(msg);
            byte monsterCount = msg.ReadByte();
            for (int i = 0; i < monsterCount; i++)
            {
                var monster = Character.ReadSpawnData(msg);
                if (monster == null)
                {
                    throw new System.Exception($"Error in MonsterMission.ClientReadInitial: failed to create a monster (mission: {Prefab.Identifier}, index: {i})");
                }
                monsters.Add(monster);
            }
            if (monsters.Count != monsterCount)
            {
                throw new System.Exception("Error in MonsterMission.ClientReadInitial: monster count does not match the server count (" + monsterCount + " != " + monsters.Count + "mission: " + Prefab.Identifier + ")");
            }
            InitializeMonsters(monsters);
        }
    }
}
