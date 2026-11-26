#nullable enable

using Barotrauma.Items.Components;
using Barotrauma.Networking;
using System;
using System.Collections.Generic;
using System.Linq;
namespace Barotrauma;

internal sealed partial class SwappableProsthetic : ItemComponent
{
    private RoundSound? swapSound;

    private bool setLinkedItemHotkeyTexts;
    private bool wasAnotherItemTakingFireModeInput;

    private int requestedMode = 0;

    private LocalizedString inventoryHotkeyTooltip = LocalizedString.EmptyString;
    public override LocalizedString InventoryHotkeyTooltip
    {
        get
        {
            //if the character is holding a sprayer, disable the hotkey on this item and let the sprayer take the input instead
            if (IsAnotherItemTakingFireModeInput())
            {
                return LocalizedString.EmptyString;
            }
            return inventoryHotkeyTooltip;
        }
        set { /*can't be set*/ }
    }

    partial void InitProjectSpecific(Item item, ContentXElement element)
    {
        foreach (ContentXElement subElement in element.Elements())
        {
            switch (subElement.Name.ToString().ToLowerInvariant())
            {
                case "swapsound" when swapSound == null:
                    swapSound = RoundSound.Load(subElement);
                    break;
            }
        }
    }

    private void RefreshInventoryHotkeyTooltip()
    {
        if (ForceMode > -1)
        {
            // A forced mode can't be toggled, so there's no hotkey to advertise
            inventoryHotkeyTooltip = LocalizedString.EmptyString;
            return;
        }
        if (LastSlotType == InvSlotType.RightHand)
        {
            inventoryHotkeyTooltip = TextManager.GetWithVariables(
                "prosthetic.swaphotkeyrighthand.tooltip",
                ("[nextfiremode]", GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.NextFireMode)));
        }
        else if (LastSlotType == InvSlotType.LeftHand)
        {
            inventoryHotkeyTooltip = TextManager.GetWithVariables(
                "prosthetic.swaphotkeylefthand.tooltip",
                ("[previousfiremode]", GameSettings.CurrentConfig.KeyMap.KeyBindText(InputType.PreviousFireMode)));
        }
        else
        {
            inventoryHotkeyTooltip = LocalizedString.EmptyString;
        }
    }

    public override void Equip(Character character)
    {
        setLinkedItemHotkeyTexts = true;
    }

    private bool IsAnotherItemTakingFireModeInput()
    {
        //if the character is holding a sprayer, disable the hotkey on this item and let the sprayer take the input instead
        return item.ParentInventory is CharacterInventory { Owner: Character character } &&
               (HasInterfaceUsingFiringModeKeybind(character) || character.HeldItems.Any(it => it != item && it.GetComponent<Sprayer>() != null));

        static bool HasInterfaceUsingFiringModeKeybind(Character character)
                   => character.SelectedItem is { } it && it.GetComponent<CustomInterface>() is { } ci && ci.GetKeyBinds.Any(static kb => kb is InputType.NextFireMode or InputType.PreviousFireMode);
    }

    partial void UpdateLocalControls(Character wearer, InvSlotType currSlotType)
    {
        if (wearer != Character.Controlled || wearer == null) { return; }
        if (wearer.IsIncapacitated || wearer.LockHands) { return; }
        if (ForceMode > -1) { return; }
        if (linkedItems == null) { return; }

        int nextMode = Mode;

        bool isAnotherItemTakingFireModeInput = IsAnotherItemTakingFireModeInput();
        if (!isAnotherItemTakingFireModeInput)
        {
            if (currSlotType.HasFlag(InvSlotType.LeftHand) && wearer.IsKeyHit(InputType.PreviousFireMode)) { nextMode++; }
            if (currSlotType.HasFlag(InvSlotType.RightHand) && wearer.IsKeyHit(InputType.NextFireMode)) { nextMode++; }
        }
        nextMode = MathUtils.PositiveModulo(nextMode, linkedItems.Count + 1);

        if (wasAnotherItemTakingFireModeInput != isAnotherItemTakingFireModeInput)
        {
            wasAnotherItemTakingFireModeInput = isAnotherItemTakingFireModeInput;
            setLinkedItemHotkeyTexts = true;
        }

        if (setLinkedItemHotkeyTexts)
        {
            RefreshInventoryHotkeyTooltip();
            foreach (var linkedItem in linkedItems)
            {
                if (linkedItem.GetComponent<Holdable>() is Holdable holdable)
                {
                    holdable.InventoryHotkeyTooltip = InventoryHotkeyTooltip;                    
                }
            }
            setLinkedItemHotkeyTexts = false;
        }

        if (nextMode == Mode) { return; }

        if (GameMain.Client != null)
        {
            requestedMode = nextMode;
            item.CreateClientEvent(this);
        }
        else
        {
            SetMode(wearer, nextMode);
        }
    }

    private void PlaySwapSound()
    {
        // Use a cooldown to prevent audio muting issues that seem to be caused by players being able to repeatedly retrigger the sound very rapidly
        if (swapSound == null || Timing.TotalTime <= lastSwitchSoundTime + switchSoundCooldown) { return; }
        item.PlaySound(swapSound);
        lastSwitchSoundTime = Timing.TotalTime;
    }

    private static void FlashSlot(CharacterInventory wearerInventory, InvSlotType slotType)
    {
        int slotIndex = wearerInventory.FindLimbSlot(slotType);
        if (slotIndex == -1) { return; }
        VisualSlot slot = wearerInventory.visualSlots[slotIndex];
        slot.ShowBorderHighlight(GUIStyle.Red, 0.1f, 0.9f);
    }

    public void ClientEventWrite(IWriteMessage msg, NetEntityEvent.IData? extraData = null)
    {
        if (linkedItems == null)
        {
            DebugConsole.ThrowError($"Tried to write event data for {nameof(SwappableProsthetic)} ({item.Name}) before linked items had been initialized.\n" + Environment.StackTrace);
            return;
        }

        msg.WriteRangedInteger(requestedMode, 0, linkedItems.Count);
    }

    public void ClientEventRead(IReadMessage msg, float sendingTime)
    {
        byte linkedItemsCount = msg.ReadByte();
        linkedItems = new List<Item>(linkedItemsCount);
        for (int i = 0; i < linkedItemsCount; i++)
        {
            UInt16 id = msg.ReadUInt16();
            if (Entity.FindEntityByID(id) is Item item)
            {
                linkedItems.Add(item);
                InitializeProstheticItem(item);
            }
            else
            {
                DebugConsole.ThrowError($"Error while reading linked items from swappable prosthetics, couldn't find valid item with id {id}");
            }
        }
        int newMode = msg.ReadRangedInteger(0, linkedItems.Count);
        LastSlotType = (InvSlotType)msg.ReadUInt32();

        int prevMode = Mode;
        if (newMode == 0)
        {
            ResetMode(createNetworkEvent: false, ignoreForcedMode: true);
        }
        else if (item.ParentInventory?.Owner is Character wearer)
        {
            SetMode(wearer, newMode, playSound: false);
        }
        //ensure the value is correct even if something (like the item already being in hands) interrupted SetMode
        Mode = newMode;

        if (prevMode != newMode && item.ParentInventory?.Owner is Character)
        {
            PlaySwapSound();
        }
    }
}
