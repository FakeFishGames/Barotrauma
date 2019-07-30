using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    partial class MissionMode : GameMode
    {
        public override void ShowStartMessage()
        {
            if (mission == null) return;

            new GUIMessageBox(mission.Name, mission.Description, new Vector2(0.25f, 0.0f), new Point(400, 200))
            {
                UserData = "missionstartmessage"
            };
        }
    }
}
