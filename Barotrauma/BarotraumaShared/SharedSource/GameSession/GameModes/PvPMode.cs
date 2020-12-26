using System;

namespace Barotrauma
{
    class PvPMode : MissionMode
    {
        public PvPMode(GameModePreset preset, MissionPrefab missionPrefab) : base(preset, ValidateMissionPrefab(missionPrefab, MissionPrefab.PvPMissionClasses)) { }

        public PvPMode(GameModePreset preset, MissionType missionType, string seed) : base(preset, ValidateMissionType(missionType, MissionPrefab.PvPMissionClasses), seed) { }
    }
}
