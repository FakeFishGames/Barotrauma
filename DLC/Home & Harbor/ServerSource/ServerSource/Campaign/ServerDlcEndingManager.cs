namespace HomeAndHarbor;

using Barotrauma;
using Barotrauma.Networking;

partial class DlcEndingManager
{
    partial void TriggerProjSpecific(Identifier slideshowPrefabIdentifier)
    {
        GameNetwork.Broadcast(Plugin.NetworkHeaders.TriggerEnding, 
            new TriggerEndingNetworkData(slideshowPrefabIdentifier));
    }
}
