namespace Barotrauma
{
    partial class GoToMission : Mission
    {
        public override bool DisplayAsCompleted => stateControlsCompletion ? State == successState : State >= Prefab.MaxProgressState;
        public override bool DisplayAsFailed => State == displayAsFailedState;
    }
}
