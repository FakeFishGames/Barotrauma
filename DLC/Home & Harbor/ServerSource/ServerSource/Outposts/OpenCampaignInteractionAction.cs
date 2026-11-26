using Barotrauma;
using Barotrauma.Networking;

namespace HomeAndHarbor;

internal partial class OpenCampaignInteractionAction
{
    partial void OpenMenuProjSpecific(Character speaker)
    {
        // Tell the client to open the menu
        foreach (Entity? entity in ParentEvent.GetTargets(TargetTag))
        {
            if (entity is Character { IsRemotePlayer: true } character)
            {
                if (GameMain.Server.ConnectedClients.Find(c => c.Character == character) is Client client)
                {
                    GameNetwork.SendToClient(client, Plugin.NetworkHeaders.OpenCampaignInteraction,
                        new OpenCampaignInteractionData(InteractionType, speaker.ID));
                }
            }
        }
    }
}
