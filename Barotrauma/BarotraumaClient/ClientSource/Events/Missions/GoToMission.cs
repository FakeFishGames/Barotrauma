namespace Barotrauma
{
    partial class GoToMission : Mission
    {
        public override bool DisplayAsCompleted => State >= Prefab.MaxProgressState;
        public override bool DisplayAsFailed => false;
    }
}
