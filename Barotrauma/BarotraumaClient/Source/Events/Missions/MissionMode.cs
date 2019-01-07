namespace Barotrauma
{
    partial class MissionMode : GameMode
    {
        public override void MsgBox()
        {
            if (mission == null) return;

            var missionMsg = new GUIMessageBox(mission.Name, mission.Description, 400, 400);
            missionMsg.UserData = "missionstartmessage";

#if SERVER
            Networking.GameServer.Log(TextManager.Get("Mission") + ": " + mission.Name, Networking.ServerLog.MessageType.ServerMessage);
            Networking.GameServer.Log(mission.Description, Networking.ServerLog.MessageType.ServerMessage);
#endif
        }
    }
}
