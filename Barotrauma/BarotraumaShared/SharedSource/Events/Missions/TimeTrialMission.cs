using System;

namespace Barotrauma
{
    /// <summary>
    /// A time trial mission where players must reach the end of the level.
    /// Similar to GoToMission but may have time-based elements in the content.
    /// </summary>
    partial class TimeTrialMission : Mission
    {
        public TimeTrialMission(MissionPrefab prefab, Location[] locations, Submarine sub)
            : base(prefab, locations, sub)
        {
        }

        protected override void UpdateMissionSpecific(float deltaTime)
        {
            if (Level.Loaded?.Type == LevelData.LevelType.Outpost)
            {
                State = Math.Max(1, State);
            }
        }

        protected override bool DetermineCompleted()
        {
            if (Level.Loaded?.Type == LevelData.LevelType.Outpost)
            {
                return true;
            }
            else
            {
                return Submarine.MainSub is { AtEndExit: true };
            }
        }
    }
}
