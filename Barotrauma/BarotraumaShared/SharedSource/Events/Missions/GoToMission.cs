using System;

namespace Barotrauma
{
    partial class GoToMission : Mission
    {
        private readonly bool maxProgressStateDeterminsCompleted;
        public GoToMission(MissionPrefab prefab, Location[] locations, Submarine sub)
            : base(prefab, locations, sub)
        {
            maxProgressStateDeterminsCompleted = prefab.ConfigElement.GetAttributeBool("maxprogressdeterminescompleted", false);
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
            if (maxProgressStateDeterminsCompleted)
            {
                return Prefab.MaxProgressState == State;
            }

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
