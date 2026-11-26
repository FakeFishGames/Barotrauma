using Barotrauma;

namespace HomeAndHarbor;

/// <summary>
/// Enables the campaign map shortcut network globally. Shortcuts are pre-generated
/// along with the map, but this makes them discoverable in the first place.
/// </summary>
class EnableShortcutNetworkAction(ScriptedEvent parentEvent, ContentXElement element) : EventAction(parentEvent, element)
{
    private bool isFinished = false;

    public override bool IsFinished(ref string goTo) => isFinished;

    public override void Reset(bool shouldEndConversations = true) => isFinished = false;

    public override void Update(float deltaTime)
    {
        if (isFinished) { return; }

        if (GameMain.GameSession?.Campaign is not CampaignMode campaign)
        {
            DebugConsole.ThrowError($"Error in event {ParentEvent.Prefab.Identifier}: cannot use {nameof(EnableShortcutNetworkAction)} when a campaign is not active.");
            return;
        }
        LocationShortcutManager.SetShortcutsUnlocked(campaign, unlocked: true, createNetworkEvent: true);
#if DEBUG
        DebugConsole.NewMessage($"EnableShortcutNetworkAction triggered, shortcuts enabled");
#endif
        isFinished = true;
    }

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(EnableShortcutNetworkAction)}";
    }
}
