using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    partial class MissionMode : GameMode
    {
        public override void MsgBox()
        {
            if (mission == null) return;

            var missionMsg = new GUIMessageBox(mission.Name, mission.Description, new Vector2(0.25f, 0.0f), new Point(400, 200))
            {
                UserData = "missionstartmessage"
            };

#if SERVER
            Networking.GameServer.Log(TextManager.Get("Mission") + ": " + mission.Name, Networking.ServerLog.MessageType.ServerMessage);
            Networking.GameServer.Log(mission.Description, Networking.ServerLog.MessageType.ServerMessage);
#endif
        }
    }
}
