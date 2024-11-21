using System;
using System.Collections.Generic;
using System.Linq;

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

        public MissionMode(GameModePreset preset, IEnumerable<Identifier> missionTypes, string seed)
            : base(preset)
        {
            Location[] locations = { GameMain.GameSession.StartLocation, GameMain.GameSession.EndLocation };
            var mission = Mission.LoadRandom(locations, seed, requireCorrectLocationType: false, missionTypes, difficultyLevel: GameMain.NetworkMember.ServerSettings.SelectedLevelDifficulty);
            if (mission != null)
            {
                missions.Add(mission);
            }
        }

        protected static IEnumerable<MissionPrefab> ValidateMissionPrefabs(IEnumerable<MissionPrefab> missionPrefabs, Dictionary<Identifier, Type> missionClasses)
        {
            foreach (MissionPrefab missionPrefab in missionPrefabs)
            {
                if (!missionClasses.ContainsValue(missionPrefab.MissionClass))
                {
                    throw new InvalidOperationException($"Cannot start gamemode with a {missionPrefab.MissionClass} mission.");
                }
            }
            return missionPrefabs;
        }

        /// <summary>
        /// Returns the mission types that are valid for the given mission classes (e.g. all mission types suitable for the PvP mission classes).
        /// </summary>
        public static IEnumerable<Identifier> ValidateMissionTypes(IEnumerable<Identifier> missionTypes, Dictionary<Identifier, Type> missionClasses)
        {
            return missionTypes.Where(type => 
                MissionPrefab.Prefabs.OrderBy(missionPrefab => missionPrefab.UintIdentifier)
                    .Any(missionPrefab => missionPrefab.Type == type && missionClasses.ContainsValue(missionPrefab.MissionClass)));
        }
    }
}
