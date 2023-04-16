using System;

namespace Barotrauma
{
    partial class GoToMission : Mission
    {
        public GoToMission(MissionPrefab prefab, Location[] locations, Submarine sub)
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
