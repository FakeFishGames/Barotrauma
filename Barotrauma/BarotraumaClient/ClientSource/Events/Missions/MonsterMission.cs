using Barotrauma.Networking;

namespace Barotrauma
{
    partial class MonsterMission : Mission
    {
        public override bool IsAtCompletionState => State > 0;
        public override bool IsAtFailureState => false;

        public override void ClientReadInitial(IReadMessage msg)
        {
            base.ClientReadInitial(msg);
            byte monsterCount = msg.ReadByte();
            for (int i = 0; i < monsterCount; i++)
            {
                monsters.Add(Character.ReadSpawnData(msg));
            }
            if (monsters.Contains(null))
            {
                throw new System.Exception("Error in MonsterMission.ClientReadInitial: monster list contains null (mission: " + Prefab.Identifier + ")");
            }
            if (monsters.Count != monsterCount)
            {
                throw new System.Exception("Error in MonsterMission.ClientReadInitial: monster count does not match the server count (" + monsterCount + " != " + monsters.Count + "mission: " + Prefab.Identifier + ")");
            }
            InitializeMonsters(monsters);
        }
    }
}
