using Barotrauma.Networking;

namespace Barotrauma
{
    partial class CombatMission : Mission
    {
        public override string Description
        {
            get
            {
                if (descriptions == null) return "";

                if (GameMain.Client?.Character == null)
                {
                    //non-team-specific description
                    return descriptions[0];
                }

                //team specific
                return descriptions[GameMain.Client.Character.TeamID == CharacterTeamType.Team1 ? 1 : 2];
            }
        }
    }
}
