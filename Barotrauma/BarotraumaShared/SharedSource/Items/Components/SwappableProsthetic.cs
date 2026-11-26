#nullable enable

using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Xml.Linq;
namespace Barotrauma;

/// <summary>
/// Allows using keybinds to equip/unequip contained items from the parent item when placed in either prosthetic arm slot.<br/>
/// - <see cref="InputType.PreviousFireMode"/> controls the <see cref="InvSlotType.LeftHandProsthetic"/> slot.<br/>
/// - <see cref="InputType.NextFireMode"/> controls the <see cref="InvSlotType.RightHandProsthetic"/> slot.<br/>
/// <br/>
/// Use <see cref="ItemContainer.SpawnWithId"/> to spawn equippable items. Order determines the cycling of items.<br/>
/// NOTE: <see cref="ItemContainer.SpawnWithIdOnce"/> should very likely also be set to true, so that a new item isn't
/// spawned inside the prosthetic every time it's loaded.<br/>
/// Cycling equipped items requires a freeable hand slot depending on which prosthetic arm slot the parent item is placed in.<br/>
/// Equippable items held in both hand slots require both to be freeable, regardless of the parent item's slot.
/// </summary>
internal sealed partial class SwappableProsthetic : ItemComponent, IServerSerializable, IClientSerializable
{
    private List<Item>? linkedItems;

    /// <summary>
    /// = 0 - No item equipped.<br/>
    /// > 1 - Index + 1 of equipped item.
    /// </summary>
    public int Mode { get; private set; }

    /// <summary>
    /// Can be used by status effects
    /// </summary>
    public int CurrentMode 
    { 
        get => Mode;
        set
        {
            if (item.ParentInventory.Owner is Character wearer && value != Mode)
            {
                SetMode(wearer, value);
            }
        }
    }

    /// <summary>
    /// Stores the previously saved <see cref="Mode"/> value so it can be restored after all linked items have been initialized.    
    /// </summary>
    private int? pendingModeSwitch = null;

    [Serialize(-1, IsPropertySaveable.No, description: "Can be used to force the prosthetic to stay in a specific mode. -1 = not used.")]
    public int ForceMode { get; set; }

    [Serialize(true, IsPropertySaveable.No, description: "Should the currently active/equipped item be drawn? May be useful to disable if the equipped item is already visible in the wearable sprite.")]
    public bool DrawActiveItem { get; set; }

    private float switchSoundCooldown = 0.1f;
    private double lastSwitchSoundTime;

    private InvSlotType lastSlotType = InvSlotType.None;
    private InvSlotType LastSlotType
    {
        get => lastSlotType;
        set
        {
            if (lastSlotType != value)
            {
                lastSlotType = value;

                // Refresh hand slot restriction
                RestrictLinkedItemHandSlots(value);
#if CLIENT
                RefreshInventoryHotkeyTooltip();
#endif
            }
        }
    }

    public override bool IsActive => LastSlotType != InvSlotType.None;
    public override bool UpdateWhenInactive => true;

    #region Saving and Loading
    public SwappableProsthetic(Item item, ContentXElement element) : base(item, element)
    {
        InitProjectSpecific(item, element);

        var itemContainer = item.GetComponent<ItemContainer>();
        if (itemContainer != null && !itemContainer.SpawnWithId.IsNullOrEmpty() && !itemContainer.SpawnWithIdOnce)
        {
            DebugConsole.AddWarning(
                $"The item {item.Prefab.Identifier} is configured to always spawn with {itemContainer.SpawnWithId}, even if the item has already been spawned previously."
                + "You should probably add SpawnWithIdOnce=\"true\" to the item.", 
                    contentPackage: item.ContentPackage);
        }
    }

    partial void InitProjectSpecific(Item item, ContentXElement element);

    public override XElement Save(XElement parentElement)
    {
        XElement componentElement = base.Save(parentElement);
        componentElement.Add(new XAttribute(nameof(pendingModeSwitch), Mode));
        return componentElement;
    }

    public override void Load(ContentXElement componentElement, bool usePrefabValues, IdRemap idRemap, bool isItemSwap)
    {
        base.Load(componentElement, usePrefabValues, idRemap, isItemSwap);
        pendingModeSwitch = componentElement.GetAttributeInt(nameof(pendingModeSwitch), -1);
        if (pendingModeSwitch == -1) { pendingModeSwitch = null; }
    }
    #endregion

    public override void Update(float deltaTime, Camera cam)
    {
        // The contained items don't necessarily exist yet when the prosthetic is first updated (Entity.Spawner might not
        // have flushed yet), so we keep retrying until the container actually has something in it.
        if (linkedItems is not { Count: > 0 })
        {
            if (GameMain.NetworkMember is { IsClient: true })
            {
                // Clients get the linked items from the server
                return;
            }
            else if (linkedItems != null && item.ContainedItems.None())
            {
                // Looks like this is intended to be empty, so let's not run FindLinkedItems on every frame
                return;
            }
            else
            {
                FindLinkedItems();
            }
        }

        if (linkedItems.Count == 0) { return; }

        if (item.ParentInventory is not CharacterInventory { Owner: Character wearer } wearerInventory)
        {
            LastSlotType = InvSlotType.None;
            if (GameMain.NetworkMember is not { IsClient: true })
            {
                ResetMode();
            }
            return;
        }

        InvSlotType currSlotType = GetCorrespondingHandSlotTypes(wearerInventory);
        if (currSlotType == InvSlotType.None || currSlotType != LastSlotType)
        {
            LastSlotType = currSlotType;
            if (GameMain.NetworkMember is not { IsClient: true })
            {
                ResetMode();
            }
            return;
        }

        // Figure out what the mode should be and correct it if it's out of sync with the actually equipped swappable item
        if (GameMain.NetworkMember is not { IsClient: true })
        {
            int targetMode;
            if (ForceMode > -1)
            {
                targetMode = ValidateMode(ForceMode);
            }
            else
            {
                // Make the mode match whichever linked item is currently equipped in the prosthetic's slot.
                // This keeps the mode in sync even if the linked item is equipped/unequipped by something other than
                // SwappableProsthetic (e.g. a bot equipping it directly, or AttemptToFreeHands stowing it for handcuffs).
                targetMode = 0; // = none
                for (int i = 0; i < linkedItems.Count; i++)
                {
                    // Look for just the single equipped swappable item and stop there.
                    // Otherwise prosthetics with multiple swappable items would keep resetting to 0 because of the unequipped items.
                    if (wearer.Inventory.IsInLimbSlot(linkedItems[i], currSlotType))
                    {
                        targetMode = i + 1;
                        break;
                    }
                }
            }

            if (Mode != targetMode)
            {
                SetMode(wearer, targetMode);
#if SERVER
                item.CreateServerEvent(this);
#endif
            }
        }

        UpdateLocalControls(wearer, currSlotType);

        ApplyStatusEffects(ActionType.OnActive, deltaTime);
    }

    [MemberNotNull(nameof(linkedItems))]
    private void FindLinkedItems()
    {
        linkedItems = item.ContainedItems.ToList();

        if (item.ParentInventory is CharacterInventory { Owner: Character wearer } wearerInventory)
        {
            InvSlotType correspondingSlot = GetCorrespondingHandSlotTypes(wearerInventory);

            Item? existingSwappable = wearerInventory.GetItemInLimbSlot(correspondingSlot);
            if (existingSwappable != null && existingSwappable.ProstheticSwappableIndex >= 0)
            {
                linkedItems.Insert(existingSwappable.ProstheticSwappableIndex, existingSwappable);

                LastSlotType = correspondingSlot;
            }

            InitializeLinkedItems();

            // Only restore the pending mode once there are items to restore it for, else we'd consume the restore for nothing
            if (pendingModeSwitch != null && linkedItems.Count > 0)
            {
                ResetMode(createNetworkEvent: false, ignoreForcedMode: true);
                SetMode(wearer, pendingModeSwitch.Value, playSound: false);
                Mode = ValidateMode(pendingModeSwitch.Value);
                pendingModeSwitch = null;
            }
        }
        else
        {
            InitializeLinkedItems();
        }


#if SERVER
        // Only sync once there's something to sync. Otherwise, this event would arrive before the actual spawn event,
        // and would refer to an item that doesn't exist yet.
        if (linkedItems.Count > 0)
        {
            item.CreateServerEvent(this);
        }
#endif
    }

    private void InitializeLinkedItems()
    {
        if (linkedItems != null)
        {
            for (int i = 0; i < linkedItems.Count; i++)
            {
                linkedItems[i].ProstheticSwappableIndex = i;
                InitializeProstheticItem(linkedItems[i]);
            }
        }
    }

    private void InitializeProstheticItem(Item item)
    {
        item.GetComponents<Holdable>().ForEach(static holdable => holdable.CanBeUnheld = false);

        // Disallow selling the prosthetics swappable items
        item.DisallowSelling = true;

        if (!DrawActiveItem)
        {
            item.HiddenInGame = true;
        }
    }

    private readonly Dictionary<Pickable, List<InvSlotType>> originalAllowedSlots = new Dictionary<Pickable, List<InvSlotType>>();

    /// <summary>
    /// Restricts the linked items to the hand slot(s) the prosthetic is currently attached to, so that eg. bots don't
    /// attempt to equip the tool of a right arm prosthetic in the left hand. Always re-derived from the original slots. 
    /// Only hands need this for now, as they're the only limb type with both a prosthetic slot and a slot for holding items.
    /// </summary>
    /// <param name="handSlotsToRestrictTo">The hand slot(s) to restrict the items to, or <see cref="InvSlotType.None"/> to restore the slots defined in the items' XML.</param>
    private void RestrictLinkedItemHandSlots(InvSlotType handSlotsToRestrictTo)
    {
        if (linkedItems == null) { return; }
        foreach (var linkedItem in linkedItems)
        {
            foreach (var pickable in linkedItem.GetComponents<Pickable>())
            {
                // The first time we get here, store the Pickable's original list of allowed slots so they're not lost and can be reverted to
                if (!originalAllowedSlots.TryGetValue(pickable, out List<InvSlotType>? originalSlots))
                {
                    originalSlots = new List<InvSlotType>(pickable.AllowedSlots);
                    originalAllowedSlots.Add(pickable, originalSlots);
                }

                List<InvSlotType> restrictedSlots = new List<InvSlotType>();
                foreach (var originalSlot in originalSlots)
                {
                    InvSlotType originalHandFlags = originalSlot & CharacterInventory.HandSlots;
                    // Drop only the entries that name just the hand(s) the prosthetic isn't attached to.
                    // In other words, we keep the entry if...
                    if (handSlotsToRestrictTo == InvSlotType.None // we're restoring the original,
                        || originalHandFlags == InvSlotType.None // the entry doesn't concern hands at all (eg. "Any"),
                        || originalHandFlags.HasAnyFlag(handSlotsToRestrictTo)) // or it covers one of the current hand(s) (including "bothhands")
                    {
                        restrictedSlots.Add(originalSlot);
                    }
                }
                pickable.SetAllowedSlots(restrictedSlots);
            }
        }
    }

    partial void UpdateLocalControls(Character wearer, InvSlotType currSlotType);

    public override void AttemptToFreeHands(Item itemToEquip, bool force = false)
    {
        // If the currently-equipped tool/weapon/item has CanBeUnheld == false, equipping something else in that hand should be blocked,
        // except for handcuffs and other restraints, which should cause the item to be stowed back inside the prosthetic.
        // Callers that have already committed to equipping the item get to force it stowed too.
        int currMode = ValidateMode(Mode);
        Item? currentTool = currMode == 0 || linkedItems == null ? null : linkedItems[currMode - 1];
        if (currentTool is { CanBeUnheld: false } && (force || itemToEquip.HasTag(Tags.HandLockerItem)))
        {
            ResetMode();
        }
    }


    private bool warnedAboutInvalidMode;

    /// <summary>
    /// Ensures the mode is within range, and thus safe to use as an index into <see cref="linkedItems"/>.
    /// </summary>
    /// <returns>The mode itself if it's within range, otherwise 0 (=no item equipped).</returns>
    private int ValidateMode(int mode)
    {
        if (linkedItems != null && mode >= 0 && mode <= linkedItems.Count)
        {
            return mode;
        }

        // Warn only once per item, an invalid mode is likely a content error so it'd spam every frame
        if (mode != 0 && !warnedAboutInvalidMode)
        {
            warnedAboutInvalidMode = true;
            DebugConsole.AddWarning(
                $"Invalid mode ({mode}) on the {nameof(SwappableProsthetic)} of the item {item.Prefab.Identifier}, which has {linkedItems?.Count ?? 0} swappable item(s).",
                    contentPackage: item.ContentPackage);
        }
        return 0;
    }

    /// <param name="newMode">
    /// = 0 - No item equipped.<br/>
    /// > 1 - Index + 1 of equipped item.
    /// </param>
    private bool SetMode(Character wearer, int newMode, bool playSound = true)
    {
        if (ForceMode > -1)
        {
            newMode = ForceMode;
        }
        if (linkedItems == null)
        {
            //just set the mode if the items haven't been initialized yet
            pendingModeSwitch = newMode;
            return false;
        }
        // Only validate once the items are there
        newMode = ValidateMode(newMode);

        // Already in the requested mode, nothing to do
        if (Mode == newMode) { return false; }

        IEnumerable<InvSlotType> prostheticHandSlots = EnumExtensions.GetIndividualFlags(GetCorrespondingHandSlotTypes(wearer.Inventory));
        int currMode = ValidateMode(Mode);
        Item? currItem = currMode == 0 ? null : linkedItems[currMode - 1];
        Item? nextItem = newMode == 0 ? null : linkedItems[newMode - 1];

        if (nextItem != null && !TryClearHandSlotsForItem(wearer, nextItem, prostheticHandSlots)) 
        { 
            return false; 
        }

        if (currItem != null && !item.OwnInventory.TryPutItem(currItem, wearer, ignoreStuckInHand: true, createNetworkEvent: false)) 
        { 
            return false; 
        }

        if (nextItem != null && !nextItem.Removed)
        {
            IEnumerable<InvSlotType> slots = ItemRequiresBothHands(nextItem) ? [CharacterInventory.HandSlots] : prostheticHandSlots;
            if (!wearer.Inventory.TryPutItem(nextItem, wearer, slots, createNetworkEvent: false)) { return false; }
        }

#if CLIENT
        if (playSound) { PlaySwapSound(); }
#endif

        Mode = newMode;

#if SERVER
        // Sync the affected inventories as a whole here, otherwise the client can receive things out of order and drop the moved item
        item.OwnInventory.CreateNetworkEvent();
        wearer.Inventory.CreateNetworkEvent();
#endif

        return true;
    }

    /// <summary>
    /// Resets the mode back to default (unequipping any equipped items and putting them back inside the item)
    /// </summary>
    /// <param name="ignoreForcedMode">Should <see cref="ForceMode"/> be ignored? When the prosthetic is removed from a character for example, the equipped item should go back inside it even if that's normally not possible.</param>
    private void ResetMode(bool createNetworkEvent = true, bool ignoreForcedMode = false)
    {
        if (ForceMode > -1 && !ignoreForcedMode) { return; }
        if (linkedItems == null)
        {
            DebugConsole.ThrowError($"Tried to reset the mode on {nameof(SwappableProsthetic)} ({item.Name}) before linked items had been initialized.\n" + Environment.StackTrace);
            return;
        }

        foreach (var linkedItem in linkedItems)
        {
            if (linkedItem.ParentInventory != item.OwnInventory &&
                !item.OwnInventory.TryPutItem(linkedItem, user: null, ignoreStuckInHand: true, createNetworkEvent: createNetworkEvent))
            {
                DebugConsole.AddWarning($"Failed to put the item {linkedItem.Name} back inside the item {item.Name} when resetting the mode.");
            }
        }

        if (Mode != 0)
        {
            Mode = 0;
        
            if (createNetworkEvent)
            {
#if SERVER
                item.GetComponent<ItemContainer>()?.Inventory?.CreateNetworkEvent();
                item.CreateServerEvent(this);
#endif
            }
        }
    }

    public override void Drop(Character dropper, bool setTransform = true)
    {
        base.Drop(dropper, setTransform);

        if (GameMain.NetworkMember is { IsClient: true }) { return; }

        if (linkedItems != null)
        {
            ResetMode(ignoreForcedMode: true);

#if SERVER
            item.CreateServerEvent(this);
#endif
        }
    }

    /// <returns>
    /// - <see langword="true"/> if there is a clear hand slot.<br/>
    /// - <see langword="false"/> if a hand slot could not be cleared.
    /// </returns>
    private bool TryClearHandSlotsForItem(Character wearer, Item linkedItem, IEnumerable<InvSlotType> availableHandSlots)
    {
        if (linkedItems == null)
        {
            DebugConsole.ThrowError($"Tried to clear hand slots for {nameof(SwappableProsthetic)} ({item.Name}) before linked items had been initialized.\n" + Environment.StackTrace);
            return false;
        }

        if (linkedItem.AllowedSlots.None(allowedSlot => availableHandSlots.Any(slot => allowedSlot.HasFlag(slot)))) { return false; }
        if (ItemRequiresBothHands(linkedItem))
        {
            Item? leftBlockingItem = wearer.Inventory.GetItemInLimbSlot(InvSlotType.LeftHand);
            Item? rightBlockingItem = wearer.Inventory.GetItemInLimbSlot(InvSlotType.RightHand);

            if (linkedItems.Contains(leftBlockingItem)) { leftBlockingItem = null; }
            if (linkedItems.Contains(rightBlockingItem)) { rightBlockingItem = null; }

            bool leftBlocked = leftBlockingItem != null && !CanBePutOrDropped(leftBlockingItem, wearer);
            bool rightBlocked = rightBlockingItem != null && !CanBePutOrDropped(rightBlockingItem, wearer);

            if (leftBlocked || rightBlocked)
            {
#if CLIENT
                if (leftBlocked) { FlashSlot(wearer.Inventory, InvSlotType.LeftHand); }
                if (rightBlocked) { FlashSlot(wearer.Inventory, InvSlotType.RightHand); }
#endif
                return false;
            }

            if (leftBlockingItem != null) { PutOrDropItem(wearer, leftBlockingItem, CharacterInventory.HandSlots); }
            if (rightBlockingItem != null) { PutOrDropItem(wearer, rightBlockingItem, CharacterInventory.HandSlots); }

            return true;
        }

        Item? removableItem = null;
        InvSlotType potentialSlot = InvSlotType.None;
        foreach (InvSlotType availableSlot in availableHandSlots)
        {
            if (wearer.Inventory.GetItemInLimbSlot(availableSlot) is not Item blockingItem) { return true; }
            if (linkedItems.Contains(blockingItem)) { return true; }
            if (!CanBePutOrDropped(blockingItem, wearer)) { continue; }
            potentialSlot = availableSlot;
            removableItem = blockingItem;
        }

        if (potentialSlot == InvSlotType.None) { return false; }
        PutOrDropItem(wearer, removableItem!, potentialSlot);
        return true;
    }

    private static void PutOrDropItem(Character wearer, Item linkedItem, InvSlotType disallowedSlots)
    {
        IEnumerable<InvSlotType> allowedSlotTypes = linkedItem.AllowedSlots.Select(slotTypes => slotTypes & ~disallowedSlots);
        if (!wearer.Inventory.TryPutItem(linkedItem, wearer, allowedSlotTypes)) { linkedItem.Drop(wearer); }
    }

    private static bool ItemRequiresBothHands(Item linkedItem) => linkedItem.AllowedSlots.Any(static allowedSlot => allowedSlot.HasFlag(CharacterInventory.HandSlots));
    private static bool CanBePutOrDropped(Item blockingItem, Character wearer) => blockingItem.IsInteractable(wearer) && blockingItem.CanBeUnheld;
    private InvSlotType GetCorrespondingHandSlotTypes(CharacterInventory wearerInventory)
    {
        InvSlotType slotType = InvSlotType.None;
        if (wearerInventory.IsInLimbSlot(item, InvSlotType.LeftHandProsthetic)) { slotType |= InvSlotType.LeftHand; }
        if (wearerInventory.IsInLimbSlot(item, InvSlotType.RightHandProsthetic)) { slotType |= InvSlotType.RightHand; }
        return slotType;
    }
}
