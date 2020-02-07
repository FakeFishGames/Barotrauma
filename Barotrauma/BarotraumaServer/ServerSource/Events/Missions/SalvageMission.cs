using Barotrauma.Networking;

namespace Barotrauma
{
    partial class SalvageMission : Mission    
    {
        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            item.WriteSpawnData(msg, item.ID);
        }
    }
}
