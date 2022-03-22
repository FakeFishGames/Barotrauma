using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class CombatMission
    {
        private readonly bool[] teamDead = new bool[2];

        private List<Character>[] crews;

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

        protected override void UpdateMissionSpecific(float deltaTime)
        {
            if (!initialized)
            {
                crews[0].Clear();
                crews[1].Clear();
                foreach (Character character in Character.CharacterList)
                {
                    if (character.TeamID == CharacterTeamType.Team1)
                    {
                        crews[0].Add(character);
                    }
                    else if (character.TeamID == CharacterTeamType.Team2)
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
                teamDead[0] = crews[0].All(c => c.IsDead || c.IsIncapacitated);
                teamDead[1] = crews[1].All(c => c.IsDead || c.IsIncapacitated);
            }

            if (state == 0)
            {
                for (int i = 0; i < teamDead.Length; i++)
                {
                    if (!teamDead[i] && teamDead[1 - i])
                    {
                        //make sure nobody in the other team can be revived because that would be pretty weird
                        crews[1 - i].ForEach(c => { if (!c.IsDead) c.Kill(CauseOfDeathType.Unknown, null); });

                        GameMain.GameSession.WinningTeam = i == 0 ? CharacterTeamType.Team1 : CharacterTeamType.Team2;

                        state = 1;
                        break;
                    }
                }
            }
            else
            {
                if (teamDead[0] && teamDead[1])
                {
                    GameMain.GameSession.WinningTeam = CharacterTeamType.None;
                    if (GameMain.Server != null) { GameMain.Server.EndGame(); }
                }
                else if (GameMain.GameSession.WinningTeam != CharacterTeamType.None)
                {
                    GameMain.Server.EndGame();
                }
            }
        }
    }
}
