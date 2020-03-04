using Barotrauma.Networking;

namespace Barotrauma
{
    abstract partial class CampaignMode : GameMode
    {
        public override void ShowStartMessage()
        {
            if (Mission == null) return;

            Networking.GameServer.Log(TextManager.Get("Mission") + ": " + Mission.Name, Networking.ServerLog.MessageType.ServerMessage);
            Networking.GameServer.Log(Mission.Description, Networking.ServerLog.MessageType.ServerMessage);
        }
    }
}
