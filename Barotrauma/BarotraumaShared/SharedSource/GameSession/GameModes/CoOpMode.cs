using System.Collections.Generic;

namespace Barotrauma
{
    class CoOpMode : MissionMode
    {
        public CoOpMode(GameModePreset preset, IEnumerable<MissionPrefab> missionPrefabs) : base(preset, ValidateMissionPrefabs(missionPrefabs, MissionPrefab.CoOpMissionClasses)) { }

        public CoOpMode(GameModePreset preset, IEnumerable<Identifier> missionTypes, string seed) : base(preset, ValidateMissionTypes(missionTypes, MissionPrefab.CoOpMissionClasses), seed) { }
    }
}
