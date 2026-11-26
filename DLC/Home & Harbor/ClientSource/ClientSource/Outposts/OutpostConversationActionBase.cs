using Barotrauma;

namespace HomeAndHarbor;

internal abstract partial class OutpostConversationActionBase
{
    private bool wasDialogOpen = false;

    static partial void InitProjectSpecific()
    {
        GameNetwork.RegisterHandler<Plugin.NetworkHeaders, ShowOutpostMessageData>(
            Plugin.NetworkHeaders.ShowOutpostMessage,
            ClientRead);
    }

    partial void ResetProjSpecific()
    {
        wasDialogOpen = false;
    }

    partial void ShowMessageProjSpecific(PersistentOutpostData outpostData, Location location, Character? speaker)
    {
        // In multiplayer, the server will send us the message via network message, so just wait for ClientRead()
        if (GameMain.NetworkMember != null)
        {
            return;
        }

        // In singleplayer, show the message directly
        ConversationAction.CreateDialog(
            text: BuildMessageAndApply(outpostData, location),
            speaker: speaker,
            options: [TextManager.Get("ok").Value],
            closingOptions: [0],
            eventSprite: "",
            actionId: 0,
            fadeToBlack: false,
            dialogType: ConversationAction.DialogTypes.Regular,
            continueConversation: false
        );

        wasDialogOpen = true;
    }

    private static void ClientRead(ShowOutpostMessageData data)
    {
        // Get the speaker character if available
        Character? speaker = null;
        if (data.SpeakerId != Entity.NullEntityID && Entity.FindEntityByID(data.SpeakerId) is Character speakerChar)
        {
            speaker = speakerChar;
        }

        ConversationAction.CreateDialog(
            text: RichString.Rich(data.MessageText ?? "No message available"),
            speaker: speaker,
            options: [TextManager.Get("ok").Value],
            closingOptions: [0],
            eventSprite: "",
            actionId: 0,
            fadeToBlack: false,
            dialogType: ConversationAction.DialogTypes.Regular,
            continueConversation: false
        );
    }
}
