using Barotrauma;
using Barotrauma.Plugins;

namespace HomeAndHarbor;

/// <summary>
/// Creates a campaign map shortcut in the current location.
/// </summary>
class CreateShortcutAction : EventAction
{
    [Serialize(true, IsPropertySaveable.Yes, description: "Should the outpost be player-owned or not?")]
    public bool IsPlayerOwned { get; set; }

    public CreateShortcutAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
    { 
    }

    private bool isFinished = false;

    public override bool IsFinished(ref string goTo)
    {
        return isFinished;
    }

    public override void Reset(bool shouldEndConversations = true)
    {
        isFinished = false;
    }

    public override void Update(float deltaTime)
    {
        if (isFinished) { return; }
        if (GameMain.GameSession?.Campaign is not { } campaign)
        {
            PluginServiceProvider.GetService<IDebugConsole>().ThrowError($"Error in event {ParentEvent.Prefab.Identifier}: cannot use {nameof(CreateShortcutAction)} when a campaign is not active.");
            return;
        }
        LocationShortcutManager.AddShortcut(campaign.CurrentLocation);
        isFinished = true;
    }

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(CreateShortcutAction)}";
    }
}
