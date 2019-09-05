using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    partial class MissionMode : GameMode
    {
        public override void ShowStartMessage()
        {
            if (mission == null) return;

            new GUIMessageBox(mission.Name, mission.Description, new string[0], type: GUIMessageBox.Type.InGame)
            {
                UserData = "missionstartmessage"
            };
        }
    }
}
