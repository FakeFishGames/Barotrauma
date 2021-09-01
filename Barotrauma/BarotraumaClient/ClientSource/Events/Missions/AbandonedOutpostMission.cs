using Barotrauma.Networking;

namespace Barotrauma
{
    partial class AbandonedOutpostMission : Mission
    {
        public override int State
        {
            get { return base.State; }
            protected set
            {
                if (state != value)
                {
                    base.State = value;
                    if (state == HostagesKilledState && !string.IsNullOrEmpty(hostagesKilledMessage))
                    {
                        CreateMessageBox(string.Empty, hostagesKilledMessage);
                    }
                }
            }
        }

        public override void ClientReadInitial(IReadMessage msg)
        {
            ushort targetItemCount = msg.ReadUInt16();
            for (int i = 0; i < targetItemCount; i++)
            {
                var item = Item.ReadSpawnData(msg);
                items.Add(item);
            }

            byte characterCount = msg.ReadByte();

            for (int i = 0; i < characterCount; i++)
            {
                Character character = Character.ReadSpawnData(msg);
                characters.Add(character);
                if (msg.ReadBoolean()) { requireKill.Add(character); }
                if (msg.ReadBoolean()) 
                { 
                    requireRescue.Add(character);
#if CLIENT
                    GameMain.GameSession.CrewManager.AddCharacterToCrewList(character);
#endif
                }
                ushort itemCount = msg.ReadUInt16();
                for (int j = 0; j < itemCount; j++)
                {
                    Item.ReadSpawnData(msg);
                }
                if (character.Submarine != null && character.AIController is EnemyAIController enemyAi)
                {
                    enemyAi.UnattackableSubmarines.Add(character.Submarine);
                    enemyAi.UnattackableSubmarines.Add(Submarine.MainSub);
                    foreach (Submarine sub in Submarine.MainSub.DockedTo)
                    {
                        enemyAi.UnattackableSubmarines.Add(sub);
                    }
                }
            }
            if (characters.Contains(null))
            {
                throw new System.Exception("Error in AbandonedOutpostMission.ClientReadInitial: character list contains null (mission: " + Prefab.Identifier + ")");
            }
            if (characters.Count != characterCount)
            {
                throw new System.Exception("Error in AbandonedOutpostMission.ClientReadInitial: character count does not match the server count (" + characters + " != " + characters.Count + "mission: " + Prefab.Identifier + ")");
            }
        }
    }
}