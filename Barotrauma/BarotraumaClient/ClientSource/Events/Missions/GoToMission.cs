namespace Barotrauma
{
    partial class GoToMission : Mission
    {
        public override bool DisplayAsCompleted => StateControlsCompletion ? State == successState : State >= Prefab.MaxProgressState;
        public override bool DisplayAsFailed => StateControlsCompletion && State == failState;
    }
}
