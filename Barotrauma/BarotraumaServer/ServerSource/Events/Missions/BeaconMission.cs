using Barotrauma.Networking;

namespace Barotrauma
{
    partial class BeaconMission : Mission
    {
        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            return;
        }
    }
}
