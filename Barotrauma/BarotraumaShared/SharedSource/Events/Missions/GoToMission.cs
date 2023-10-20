using System;

namespace Barotrauma
{
    partial class GoToMission : Mission
    {
        private bool StateControlsCompletion => successState != -1;
        private readonly int successState;
        private readonly int failState;

        public GoToMission(MissionPrefab prefab, Location[] locations, Submarine sub)
            : base(prefab, locations, sub)
        {
            successState = prefab.ConfigElement.GetAttributeInt(nameof(successState), -1);
            failState = prefab.ConfigElement.GetAttributeInt(nameof(failState), -1);

            if (successState == failState && StateControlsCompletion)
            {
                DebugConsole.AddWarning($"GoTo mission with identifier: '{prefab.Identifier}' has the successstate equal to the failstate, this may cause unintentional side effects.");
            }
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
            if (StateControlsCompletion)
            {
                return State == successState;
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
