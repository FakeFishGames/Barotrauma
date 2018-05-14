namespace Barotrauma
{
    partial class MissionMode : GameMode
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
            mission = Mission.CreateRandom(locations, rand, false, param as string);
        }
    }
}
