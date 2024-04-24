namespace Barotrauma;

/// <summary>
/// Highlights specific items in a specific inventory.
/// </summary>
partial class InventoryHighlightAction : EventAction
{
    [Serialize("", IsPropertySaveable.Yes, description: "Tag of the entity or entities whose inventory the item should be highlighted in. Must be a character or an item with an inventory.")]
    public Identifier TargetTag { get; set; }

    [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the item(s) to highlight.")]
    public Identifier ItemIdentifier { get; set; }

    [Serialize(-1, IsPropertySaveable.Yes, description: "If the target is an item with multiple ItemContainer components (i.e. multiple inventories), such as a fabricator, this determines which inventory to highlight the item in (0 = first, 1 = second). If negative, it doesn't matter which inventory the item is in.")]
    public int ItemContainerIndex { get; set; }

    [Serialize(false, IsPropertySaveable.Yes, description: "If enabled, the action will go look through all the containers in the target inventory (e.g. highlighting a tank in a welding tool in the target inventory).")]
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