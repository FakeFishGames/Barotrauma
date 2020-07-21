namespace Barotrauma
{
    partial class MissionMode : GameMode
    {
        private readonly Mission mission;

        public override Mission Mission
        {
            get
            {
                return mission;
            }
        }

        public MissionMode(GameModePreset preset, MissionPrefab missionPrefab)
            : base(preset)
        {
            Location[] locations = { GameMain.GameSession.StartLocation, GameMain.GameSession.EndLocation };
            mission = missionPrefab.Instantiate(locations);
        }

        public MissionMode(GameModePreset preset, MissionType missionType, string seed)
            : base(preset)
        {
            Location[] locations = { GameMain.GameSession.StartLocation, GameMain.GameSession.EndLocation };
            mission = Mission.LoadRandom(locations, seed, false, missionType);
        }
    }
}
