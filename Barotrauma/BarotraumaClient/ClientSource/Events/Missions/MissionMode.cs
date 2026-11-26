using System;

namespace Barotrauma
{
    abstract partial class MissionMode : GameMode
    {
        public override void ShowStartMessage()
        {
            foreach (Mission mission in missions)
            {
                if (!mission.Prefab.ShowStartMessage || mission.Description.IsNullOrEmpty()) { continue; }
                new GUIMessageBox(RichString.Rich(mission.NameWithContentPackage), RichString.Rich(mission.Description), Array.Empty<LocalizedString>(), type: GUIMessageBox.Type.InGame, icon: mission.Prefab.Icon)
                {
                    IconColor = mission.Prefab.IconColor,
                    UserData = "missionstartmessage"
                };
            }
        }
    }
}
