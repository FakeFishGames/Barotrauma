using Barotrauma.Extensions;
using Barotrauma.Networking;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class PvPMode : MissionMode
    {
        public PvPMode(GameModePreset preset, IEnumerable<MissionPrefab> missionPrefabs) : 
            base(preset, ValidateMissionPrefabs(missionPrefabs, MissionPrefab.PvPMissionClasses))
        {
            if (Missions.None())
            {
                throw new System.Exception($"Attempted to start {nameof(PvPMode)} without a mission.");
            }
        }

        public PvPMode(GameModePreset preset, IEnumerable<Identifier> missionTypes, string seed) : 
            base(preset, ValidateMissionTypes(missionTypes, MissionPrefab.PvPMissionClasses), seed) 
        {
            if (Missions.None())
            {
                throw new System.Exception($"Attempted to start {nameof(PvPMode)} without a mission.");
            }
        }
    }
}
