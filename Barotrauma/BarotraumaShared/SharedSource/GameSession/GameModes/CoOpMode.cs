using System;

namespace Barotrauma
{
    class CoOpMode : MissionMode
    {
        public CoOpMode(GameModePreset preset, MissionPrefab missionPrefab) : base(preset, ValidateMissionPrefab(missionPrefab, MissionPrefab.CoOpMissionClasses)) { }

        public CoOpMode(GameModePreset preset, MissionType missionType, string seed) : base(preset, ValidateMissionType(missionType, MissionPrefab.CoOpMissionClasses), seed) { }
    }
}
