using Microsoft.Xna.Framework;

namespace Barotrauma
{
    partial class MissionMode : GameMode
    {
        public override void MsgBox()
        {
            if (mission == null) return;

            var missionMsg = new GUIMessageBox(mission.Name, mission.Description, 400, 400);
            missionMsg.UserData = "missionstartmessage";

            Networking.GameServer.Log("Mission: " + mission.Name, Networking.ServerLog.MessageType.ServerMessage);
            Networking.GameServer.Log(mission.Description, Networking.ServerLog.MessageType.ServerMessage);
        }
    }
}
