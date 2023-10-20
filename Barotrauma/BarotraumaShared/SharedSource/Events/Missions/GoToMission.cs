using System;

namespace Barotrauma
{
    partial class GoToMission : Mission
    {
        private readonly bool stateControlsCompletion;
        private readonly int successState;
        private readonly int displayAsFailedState;

        public GoToMission(MissionPrefab prefab, Location[] locations, Submarine sub)
            : base(prefab, locations, sub)
        {
            stateControlsCompletion = prefab.ConfigElement.GetAttributeBool(nameof(stateControlsCompletion), false);
            successState = prefab.ConfigElement.GetAttributeInt(nameof(successState), 1);
            displayAsFailedState = prefab.ConfigElement.GetAttributeInt(nameof(displayAsFailedState), -1);

            if (successState == displayAsFailedState)
            {
                DebugConsole.AddWarning($"GoTo mission with identifier: '{prefab.Identifier}' has the successstate equal to the displayasfailedstate, this may cause unintentional side effects.");
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
            if (stateControlsCompletion)
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
