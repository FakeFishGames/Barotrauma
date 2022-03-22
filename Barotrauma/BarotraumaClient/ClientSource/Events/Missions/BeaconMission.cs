namespace Barotrauma
{
    partial class BeaconMission : Mission
    {
        public override bool DisplayAsCompleted => State > 0;
        public override bool DisplayAsFailed => false;
    }
}
