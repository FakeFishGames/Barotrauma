using Barotrauma.Networking;

namespace Barotrauma
{
    partial class SalvageMission : Mission    
    {
        public override void ServerWriteInitial(IWriteMessage msg, Client c)
        {
            msg.Write(usedExistingItem);
            if (usedExistingItem)
            {
                msg.Write(item.ID);
            }
            else
            {
                item.WriteSpawnData(msg, item.ID);
            }
        }
    }
}
