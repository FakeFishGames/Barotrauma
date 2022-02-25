using Barotrauma.Networking;

namespace Barotrauma
{
    abstract partial class CampaignMode : GameMode
    {
        public bool MirrorLevel
        {
            get;
            protected set;
        }

        public override void ShowStartMessage()
        {
            foreach (Mission mission in Missions)
            {
                GameServer.Log($"{TextManager.Get("Mission")}: {mission.Name}", ServerLog.MessageType.ServerMessage);
                GameServer.Log(mission.Description.Value, ServerLog.MessageType.ServerMessage);
            }
        }
    }
}
