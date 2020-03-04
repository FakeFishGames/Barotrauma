using Barotrauma.Networking;

namespace Barotrauma
{
    partial class CombatMission
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
                return descriptions[GameMain.Client.Character.TeamID == Character.TeamType.Team1 ? 1 : 2];
            }
        }

        public override void ClientReadInitial(IReadMessage msg)
        {
            //do nothing
        }
    }
}
