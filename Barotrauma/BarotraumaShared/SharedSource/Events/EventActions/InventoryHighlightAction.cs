namespace Barotrauma;

partial class InventoryHighlightAction : EventAction
{
    [Serialize("", IsPropertySaveable.Yes)]
    public Identifier TargetTag { get; set; }

    [Serialize("", IsPropertySaveable.Yes)]
    public Identifier ItemIdentifier { get; set; }

    [Serialize(-1, IsPropertySaveable.Yes)]
    public int ItemContainerIndex { get; set; }

    [Serialize(false, IsPropertySaveable.Yes)]
    public bool Recursive { get; set; }

    private bool isFinished;

    public InventoryHighlightAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

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