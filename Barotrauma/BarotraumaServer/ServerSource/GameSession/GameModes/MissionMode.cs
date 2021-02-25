namespace Barotrauma
{
    abstract partial class MissionMode : GameMode
    {
        public override void ShowStartMessage()
        {
            foreach (Mission mission in missions)
            {
                Networking.GameServer.Log(TextManager.Get("Mission") + ": " + mission.Name, Networking.ServerLog.MessageType.ServerMessage);
                Networking.GameServer.Log(mission.Description, Networking.ServerLog.MessageType.ServerMessage);
            }
        }
    }
}
