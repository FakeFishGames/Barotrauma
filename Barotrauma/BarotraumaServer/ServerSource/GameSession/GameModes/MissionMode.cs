namespace Barotrauma
{
    partial class MissionMode : GameMode
    {
        public override void ShowStartMessage()
        {
            if (mission == null) return;

            Networking.GameServer.Log(TextManager.Get("Mission") + ": " + mission.Name, Networking.ServerLog.MessageType.ServerMessage);
            Networking.GameServer.Log(mission.Description, Networking.ServerLog.MessageType.ServerMessage);
        }
    }
}
