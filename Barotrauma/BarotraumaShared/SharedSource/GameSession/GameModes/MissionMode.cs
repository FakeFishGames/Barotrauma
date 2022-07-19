using System;
using System.Collections.Generic;

namespace Barotrauma
{
    abstract partial class MissionMode : GameMode
    {
        private readonly List<Mission> missions = new List<Mission>();

        public override IEnumerable<Mission> Missions
        {
            get
            {
                return missions;
            }
        }

        public MissionMode(GameModePreset preset, IEnumerable<MissionPrefab> missionPrefabs)
            : base(preset)
        {
            Location[] locations = { GameMain.GameSession.StartLocation, GameMain.GameSession.EndLocation };
            foreach (MissionPrefab missionPrefab in missionPrefabs)
            {
                missions.Add(missionPrefab.Instantiate(locations, Submarine.MainSub));
            }
        }

        public MissionMode(GameModePreset preset, MissionType missionType, string seed)
            : base(preset)
        {
            Location[] locations = { GameMain.GameSession.StartLocation, GameMain.GameSession.EndLocation };
            var mission = Mission.LoadRandom(locations, seed, false, missionType);
            if (mission != null)
            {
                missions.Add(mission);
            }
        }

        protected static IEnumerable<MissionPrefab> ValidateMissionPrefabs(IEnumerable<MissionPrefab> missionPrefabs, Dictionary<MissionType, Type> missionClasses)
        {
            foreach (MissionPrefab missionPrefab in missionPrefabs)
            {
                if (ValidateMissionType(missionPrefab.Type, missionClasses) != missionPrefab.Type)
                {
                    throw new InvalidOperationException("Cannot start gamemode with mission type " + missionPrefab.Type);
                }
            }
            return missionPrefabs;
        }

        protected static MissionType ValidateMissionType(MissionType missionType, Dictionary<MissionType, Type> missionClasses)
        {
            var missionTypes = (MissionType[])Enum.GetValues(typeof(MissionType));
            for (int i = 0; i < missionTypes.Length; i++)
            {
                var type = missionTypes[i];
                if (type == MissionType.None || type == MissionType.All) { continue; }
                if (!missionClasses.ContainsKey(type))
                {
                    missionType &= ~(type);
                }
            }
            return missionType;
        }
    }
}
