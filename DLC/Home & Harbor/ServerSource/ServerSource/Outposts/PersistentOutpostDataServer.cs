#nullable enable

using Barotrauma;
using Barotrauma.Networking;

namespace HomeAndHarbor;

internal partial class PersistentOutpostData : ISerializableEntity
{
    public void ServerWriteState()
    {
        GameNetwork.Broadcast(Plugin.NetworkHeaders.UpdatePersistentOutpostData, new PersistentOutpostNetworkData(this));        
    }

    public void ServerWriteResources(bool createUINotification)
    {
        if (ResourceManager != null)
        {
            GameNetwork.Broadcast(Plugin.NetworkHeaders.UpdateResources, new ResourcesNetworkData(this, ResourceManager, createUINotification));
        }      
    }

    public void ServerWriteState(Client client)
    {
        GameNetwork.SendToClient(client, Plugin.NetworkHeaders.UpdatePersistentOutpostData, new PersistentOutpostNetworkData(this));
    }
}