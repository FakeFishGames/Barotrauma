using System;

namespace Barotrauma
{
    partial class GoToMission : Mission
    {
        private readonly bool maxProgressStateDeterminsCompleted;
        private readonly int failState;
        public GoToMission(MissionPrefab prefab, Location[] locations, Submarine sub)
            : base(prefab, locations, sub)
        {
            maxProgressStateDeterminsCompleted = prefab.ConfigElement.GetAttributeBool("maxprogressdeterminescompleted", false);
            failState = prefab.ConfigElement.GetAttributeInt("failstate", -1);
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
