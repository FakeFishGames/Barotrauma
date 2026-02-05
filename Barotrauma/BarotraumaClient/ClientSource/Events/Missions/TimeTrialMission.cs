namespace Barotrauma
{
    partial class TimeTrialMission : Mission
    {
        public override bool DisplayAsCompleted => State >= Prefab.MaxProgressState;
        public override bool DisplayAsFailed => false;
    }
}
