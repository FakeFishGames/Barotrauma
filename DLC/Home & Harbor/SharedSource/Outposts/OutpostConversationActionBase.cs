using Barotrauma;

namespace HomeAndHarbor;

/// <summary>
/// Base class for event actions that show a message about the current player-owned outpost in dialogue.
/// Handles all the shared boilerplate, subclasses just need to provide the actual message via <see cref="BuildMessageAndApply"/>.
/// </summary>
internal abstract partial class OutpostConversationActionBase(ScriptedEvent parentEvent, ContentXElement element) : EventAction(parentEvent, element)
{
    public static void Init()
    {
        InitProjectSpecific();
    }

    static partial void InitProjectSpecific();

    [Serialize("", IsPropertySaveable.Yes, description: "Optional tag of the character who's speaking. Used to show the conversation properly.")]
    public Identifier SpeakerTag { get; set; }

    [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character(s) to show the message to. Used in multiplayer to identify which client should receive the message.")]
    public Identifier TargetTag { get; set; }

    private bool isFinished = false;
    private bool dialogShown = false;

    public override bool IsFinished(ref string goTo)
    {
        return isFinished;
    }

    public override void Reset(bool shouldEndConversations = true)
    {
        isFinished = false;
        dialogShown = false;
        ResetProjSpecific();
    }

    partial void ResetProjSpecific();

    public override void Update(float deltaTime)
    {
        if (isFinished) { return; }

#if CLIENT
        // If we've already shown the dialog, check if it's been closed
        if (dialogShown)
        {
            if (IsDialogClosed())
            {
                isFinished = true;
            }
            return;
        }
#endif

        if (GameMain.GameSession?.Campaign is not { } campaign)
        {
            DebugConsole.ThrowError($"Error in event {ParentEvent.Prefab.Identifier}: {GetType().Name} cannot be used when a campaign is not active.");
            isFinished = true;
            return;
        }

        if (campaign.Map?.CurrentLocation is not { } location)
        {
            DebugConsole.ThrowError($"Error in event {ParentEvent.Prefab.Identifier}: {GetType().Name} cannot be used, no current location found.");
            isFinished = true;
            return;
        }

        if (PersistentOutpostData.GetPersistentOutpostData(location) is not PersistentOutpostData { IsPlayerOwned: true } outpostData)
        {
            DebugConsole.ThrowError($"Error in event {ParentEvent.Prefab.Identifier}: {GetType().Name} cannot be used, outpost is not player-owned.");
            isFinished = true;
            return;
        }

        Character? speaker = null;
        if (!SpeakerTag.IsEmpty)
        {
            speaker = ParentEvent.GetTargets(SpeakerTag).FirstOrDefault(e => e is Character) as Character;
        }

        ShowMessageProjSpecific(outpostData, location, speaker);
        dialogShown = true;

        // In multiplayer (both server and client), finish immediately without waiting for dialog to close
        // (Server doesn't show dialogs, client will receive the message via network message)
        if (GameMain.NetworkMember != null)
        {
            isFinished = true;
        }
    }

    partial void ShowMessageProjSpecific(PersistentOutpostData outpostData, Location location, Character? speaker);

#if CLIENT
    private bool IsDialogClosed()
    {
        // Once the dialog has been opened and is now closed, we're done
        return wasDialogOpen && !ConversationAction.IsDialogOpen;
    }
#endif

    /// <summary>
    /// Build the message to show AND possibly apply any side effects the action might have. Only called locally in SP or on the server in MP.
    /// </summary>
    protected abstract LocalizedString BuildMessageAndApply(PersistentOutpostData outpostData, Location location);

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(isFinished)} {GetType().Name}";
    }
}
