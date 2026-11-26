using System.Linq;
using Barotrauma;

namespace HomeAndHarbor;

/// <summary>
/// Conversation action for a service to clean up the sub of a particular kind of item.
/// Immediately removes the matching items from the player's submarine, and any subs docked to it (but not the outpost).
/// </summary>
internal class CleanupSubmarineItemsAction(ScriptedEvent parentEvent, ContentXElement element) : OutpostConversationActionBase(parentEvent, element)
{
    [Serialize("", IsPropertySaveable.Yes, description: "Items with any of these identifiers are removed (semicolon-separated).")]
    public Identifier[] ItemIdentifiers { get; set; } = [];

    [Serialize("", IsPropertySaveable.Yes, description: "Items with any of these tags are removed (semicolon-separated).")]
    public Identifier[] ItemTags { get; set; } = [];

    [Serialize(false, IsPropertySaveable.Yes, description: "Only remove containers that have nothing inside them. Items that aren't containers are always considered empty.")]
    public bool RequireEmpty { get; set; }

    [Serialize(float.MaxValue, IsPropertySaveable.Yes, description: "Only remove items whose condition is below this percentage.")]
    public float RemoveBelowCondition { get; set; }

    [Serialize("outpostservice.willremoveitems", IsPropertySaveable.Yes, description: "The NPC's reply agreeing to remove, regardless of whether anything was found to remove.")]
    public string Text { get; set; } = string.Empty;

    protected override LocalizedString BuildMessageAndApply(PersistentOutpostData outpostData, Location location)
    {
        if (ItemIdentifiers.Length == 0 && ItemTags.Length == 0)
        {
            DebugConsole.ThrowError($"Error in event {ParentEvent.Prefab.Identifier}: {nameof(CleanupSubmarineItemsAction)} has neither {nameof(ItemIdentifiers)} nor {nameof(ItemTags)} defined.");
            return LocalizedString.EmptyString;
        }

        if (Submarine.MainSub is not Submarine playerSub)
        {
            DebugConsole.ThrowError($"Error in event {ParentEvent.Prefab.Identifier}: {nameof(CleanupSubmarineItemsAction)} cannot be used, no player submarine found?");
            return LocalizedString.EmptyString;
        }

        foreach (Item item in playerSub.GetItems(alsoFromConnectedSubs: true).Where(CanBeCleanedUp).ToList())
        {
            // Removing through the spawner so the removal gets synced to clients in MP
            Entity.Spawner?.AddItemToRemoveQueue(item);
        }

        return TextManager.Get(Text).Fallback(Text);
    }

    private bool CanBeCleanedUp(Item item)
    {
        if (item.Removed || item.IsHidden) { return false; }
        if (!ItemIdentifiers.Contains(item.Prefab.Identifier) && !item.HasTag(ItemTags)) { return false; }
        if (item.GetRootInventoryOwner() is Character) { return false; }
        if (RequireEmpty && item.OwnInventory is { } inventory && !inventory.IsEmpty()) { return false; }
        if (item.ConditionPercentage >= RemoveBelowCondition) { return false; }
        return true;
    }
}
