namespace Barotrauma
{
    partial class GoToMission : Mission
    {
        public override bool DisplayAsFailed => State == displayAsFailedState;
        public override bool DisplayAsCompleted => StateControlsCompletion ? State == successState : State >= Prefab.MaxProgressState;
    }
}
