using System;

namespace Barotrauma
{
    /// <summary>
    /// A time trial mission where players must reach the end of the level within a time limit.
    /// The mission succeeds if the submarine reaches the exit before time runs out.
    /// </summary>
    partial class TimeTrialMission : Mission
    {
        private readonly float timeLimit;
        private float elapsedTime;

        public override bool DisplayAsCompleted => false;
        public override bool DisplayAsFailed => State < 0;

        public TimeTrialMission(MissionPrefab prefab, Location[] locations, Submarine sub)
            : base(prefab, locations, sub)
        {
            timeLimit = prefab.ConfigElement.GetAttributeFloat("timelimit", 300f);
            elapsedTime = 0f;
        }

        protected override void StartMissionSpecific(Level level)
        {
            elapsedTime = 0f;
        }

        protected override void UpdateMissionSpecific(float deltaTime)
        {
            if (State < 0) { return; } // Already failed

            elapsedTime += deltaTime;

            // Check if time ran out
            if (elapsedTime >= timeLimit)
            {
                State = -1; // Failed
            }
            // Check if at exit
            else if (Submarine.MainSub is { AtEndExit: true })
            {
                State = Math.Max(1, State);
            }
        }

        protected override bool DetermineCompleted()
        {
            // Completed if we reached the exit before time ran out
            return State >= 1 && Submarine.MainSub is { AtEndExit: true };
        }

        public override void End()
        {
            completed = DetermineCompleted();
            if (completed)
            {
                GiveReward();
            }
        }
    }
}
