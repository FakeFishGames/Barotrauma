using Microsoft.Xna.Framework;

namespace Barotrauma
{
    class MissionMode : GameMode
    {
        private Mission mission;

        public override Mission Mission
        {
            get
            {
                return mission;
            }
        }

        public MissionMode(GameModePreset preset, object param)
            : base(preset, param)
        {
            Location[] locations = { GameMain.GameSession.StartLocation, GameMain.GameSession.EndLocation };

            MTRandom rand = new MTRandom(ToolBox.StringToInt(GameMain.NetLobbyScreen.LevelSeed));
            mission = Mission.LoadRandom(locations, rand, param as string);
        }

        public override void MsgBox()
        {
            if (mission == null) return;

            var missionMsg = new GUIMessageBox(mission.Name, mission.Description, 400, 400);
            missionMsg.UserData = "missionstartmessage";

            Networking.GameServer.Log("Mission: " + mission.Name, Networking.ServerLog.MessageType.Error);
            Networking.GameServer.Log(mission.Description, Networking.ServerLog.MessageType.Error);
        }
    }
}
