using Barotrauma;

namespace HomeAndHarbor;

internal partial class OpenCampaignInteractionAction
{
    static partial void InitProjectSpecific()
    {
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, OpenCampaignInteractionData>(
            Plugin.NetworkHeaders.OpenCampaignInteraction,
            ClientRead);
    }

    private static void ClientRead(OpenCampaignInteractionData data)
    {
        if (GameMain.GameSession?.GameMode is not CampaignMode campaignMode) { return; }

        if (Character.Controlled is not Character interactor)
        {
            DebugConsole.ThrowError($"{nameof(OpenCampaignInteractionAction)}: received a request to open the {data.InteractionType} menu, but we're not controlling a character.");
            return;
        }
        if (Entity.FindEntityByID(data.SpeakerId) is not Character speaker)
        {
            DebugConsole.ThrowError($"{nameof(OpenCampaignInteractionAction)}: received a request to open the {data.InteractionType} menu, but the speaker (entity ID {data.SpeakerId}) was not found.");
            return;
        }

        campaignMode.ShowNPCMenuInteraction(speaker, interactor, data.InteractionType);
    }
}
