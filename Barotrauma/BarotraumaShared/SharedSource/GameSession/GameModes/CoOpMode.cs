using System;
using System.Collections.Generic;

namespace Barotrauma
{
    class CoOpMode : MissionMode
    {
        public CoOpMode(GameModePreset preset, IEnumerable<MissionPrefab> missionPrefabs) : base(preset, ValidateMissionPrefabs(missionPrefabs, MissionPrefab.CoOpMissionClasses)) { }

        public CoOpMode(GameModePreset preset, MissionType missionType, string seed) : base(preset, ValidateMissionType(missionType, MissionPrefab.CoOpMissionClasses), seed) { }
    }
}
