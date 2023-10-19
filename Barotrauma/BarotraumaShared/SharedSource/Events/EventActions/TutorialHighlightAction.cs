namespace Barotrauma;

partial class TutorialHighlightAction : EventAction
{
    [Serialize("", IsPropertySaveable.Yes)]
    public Identifier TargetTag { get; set; }

    [Serialize(true, IsPropertySaveable.Yes)]
    public bool State { get; set; }

    private bool isFinished;

    public TutorialHighlightAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) 
    {
        if (GameMain.NetworkMember != null)
        {
            DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\": {nameof(TutorialHighlightAction)} is not supported in multiplayer.");
        }
    }

    public override void Update(float deltaTime)
    {
        if (isFinished) { return; }
        UpdateProjSpecific();
        isFinished = true;
    }

    partial void UpdateProjSpecific();

    public override bool IsFinished(ref string goToLabel) => isFinished;

    public override void Reset() => isFinished = false;
}