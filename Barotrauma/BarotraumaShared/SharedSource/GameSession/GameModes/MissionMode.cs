using System;
using System.Collections.Generic;

namespace Barotrauma
{
    abstract partial class MissionMode : GameMode
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

        protected static MissionPrefab ValidateMissionPrefab(MissionPrefab missionPrefab, Dictionary<MissionType, Type> missionClasses)
        {
            if (ValidateMissionType(missionPrefab.Type, missionClasses) != missionPrefab.Type)
            {
                throw new InvalidOperationException("Cannot start gamemode with mission type " + missionPrefab.Type);
            }
            return missionPrefab;
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
