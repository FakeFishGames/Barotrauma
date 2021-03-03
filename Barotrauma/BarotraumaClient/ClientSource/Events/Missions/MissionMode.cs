namespace Barotrauma
{
    abstract partial class MissionMode : GameMode
    {
        public override void ShowStartMessage()
        {
            foreach (Mission mission in missions)
            {
                new GUIMessageBox(mission.Name, mission.Description, new string[0], type: GUIMessageBox.Type.InGame, icon: mission.Prefab.Icon)
                {
                    IconColor = mission.Prefab.IconColor,
                    UserData = "missionstartmessage"
                };
            }
        }
    }
}
