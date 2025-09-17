namespace Barotrauma
{
    partial class GoToMission : Mission
    {
        public override bool DisplayAsCompleted => 
            State >= Prefab.MaxProgressState && 
            //if there's some additional check for completion, don't display as completed until we've checked it and set the mission as completed
            (Completed || completeCheckDataAction == null);
        public override bool DisplayAsFailed => false;
    }
}
