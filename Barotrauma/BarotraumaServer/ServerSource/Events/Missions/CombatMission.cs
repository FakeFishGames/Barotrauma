using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class CombatMission
    {
        private readonly bool[] teamDead = new bool[2];

        private bool initialized = false;

        public override string Description
        {
            get
            {
                if (descriptions == null) return "";
                
                //non-team-specific description
                return descriptions[0];
            }
        }

        public override bool AssignTeamIDs(List<Client> clients)
        {
            List<Client> randList = new List<Client>(clients);
            for (int i = 0; i < randList.Count; i++)
            {
                Client a = randList[i];
                int oi = Rand.Range(0, randList.Count - 1);
                Client b = randList[oi];
                randList[i] = b;
                randList[oi] = a;
            }
            int halfPlayers = randList.Count / 2;
            for (int i = 0; i < randList.Count; i++)
            {
                if (i < halfPlayers)
                {
                    randList[i].TeamID = Character.TeamType.Team1;
                }
                else
                {
                    randList[i].TeamID = Character.TeamType.Team2;
                }
            }
            return true;
        }

        public override void Update(float deltaTime)
        {
            if (!initialized)
            {
                crews[0].Clear();
                crews[1].Clear();
                foreach (Character character in Character.CharacterList)
                {
                    if (character.TeamID == Character.TeamType.Team1)
                    {
                        crews[0].Add(character);
                    }
                    else if (character.TeamID == Character.TeamType.Team2)
                    {
                        crews[1].Add(character);
                    }
                }

                initialized = true;
            }

            if (crews[0].Count == 0 || crews[1].Count == 0)
            {
                //if there are no characters in either crew, end the round
                teamDead[0] = teamDead[1] = true;
                state = 1;
            }
            else
            {
                teamDead[0] = crews[0].All(c => c.IsDead || c.IsUnconscious);
                teamDead[1] = crews[1].All(c => c.IsDead || c.IsUnconscious);
            }

            if (state == 0)
            {
                for (int i = 0; i < teamDead.Length; i++)
                {
                    if (!teamDead[i] && teamDead[1 - i])
                    {
                        //make sure nobody in the other team can be revived because that would be pretty weird
                        crews[1 - i].ForEach(c => { if (!c.IsDead) c.Kill(CauseOfDeathType.Unknown, null); });

                        GameMain.GameSession.WinningTeam = i == 0 ? Character.TeamType.Team1 : Character.TeamType.Team2;

                        state = 1;
                        break;
                    }
                }
            }
            else
            {
                if (teamDead[0] && teamDead[1])
                {
                    GameMain.GameSession.WinningTeam = Character.TeamType.None;
                    if (GameMain.Server != null) { GameMain.Server.EndGame(); }
                }
                else if (GameMain.GameSession.WinningTeam != Character.TeamType.None)
                {
                    GameMain.Server.EndGame();
                }
            }
        }
    }
}
