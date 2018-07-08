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

            if (param is string)
            {
                mission = Mission.LoadRandom(locations, GameMain.NetLobbyScreen.LevelSeed, (string)param);
            }
            else if (param is MissionPrefab)
            {
                mission = ((MissionPrefab)param).Instantiate(locations);
            }
            else if (param is Mission)
            {
                mission = (Mission)param;
            }
            else
            {
                throw new System.ArgumentException("Unrecognized MissionMode parameter \"" + param + "\"");
            }
        }
    }
}
