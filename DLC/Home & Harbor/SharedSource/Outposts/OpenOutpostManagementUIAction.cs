using Barotrauma;

namespace HomeAndHarbor;

/// <summary>
/// Action for opening the Outpost Management UI.
/// </summary>
internal partial class OpenOutpostManagementUIAction(ScriptedEvent parentEvent, ContentXElement element) : EventAction(parentEvent, element)
{
    public static void Init()
    {
        InitProjectSpecific();
    }

    static partial void InitProjectSpecific();

    [Serialize("", IsPropertySaveable.Yes, description: "Tag of the character(s) to open the UI for. Used in multiplayer to identify which client should open the UI.")]
    public Identifier TargetTag { get; set; }

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

#if CLIENT
        if (OutpostManagementUI.Instance != null)
        {
            OutpostManagementUI.Instance.Open(OutpostManagementUI.Tab.Building);
        }
        else
        {
            DebugConsole.ThrowError($"Error in event {ParentEvent.Prefab.Identifier}: {nameof(OpenOutpostManagementUIAction)} cannot be used, OutpostManagementUI instance not available.");
        }
#endif

        OpenUIProjSpecific();
        isFinished = true;
    }

    partial void OpenUIProjSpecific();

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(OpenOutpostManagementUIAction)}";
    }
}
