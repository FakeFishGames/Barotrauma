﻿using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class CharacterInventory : Inventory
    {        
        public enum Layout
        {
            Default,
            Left, 
            Right,
            Center
        }
        
        private enum QuickUseAction
        {
            None,
            Equip,
            Unequip,
            Drop,
            TakeFromContainer,
            TakeFromCharacter,
            PutToContainer,
            PutToCharacter,
            PutToEquippedItem,
            UseTreatment,
        }

        private static Dictionary<InvSlotType, Sprite> limbSlotIcons;
        public static Dictionary<InvSlotType, Sprite> LimbSlotIcons
        {
            get
            {
                if (limbSlotIcons == null)
                {
                    limbSlotIcons = new Dictionary<InvSlotType, Sprite>();
                    int margin = 2;
                    limbSlotIcons.Add(InvSlotType.Headset, new Sprite("Content/UI/MainIconsAtlas.png", new Rectangle(384 + margin, 128 + margin, 128 - margin * 2, 128 - margin * 2)));
                    limbSlotIcons.Add(InvSlotType.InnerClothes, new Sprite("Content/UI/MainIconsAtlas.png", new Rectangle(512 + margin, 128 + margin, 128 - margin * 2, 128 - margin * 2)));
                    limbSlotIcons.Add(InvSlotType.Card, new Sprite("Content/UI/MainIconsAtlas.png", new Rectangle(640 + margin, 128 + margin, 128 - margin * 2, 128 - margin * 2)));

                    limbSlotIcons.Add(InvSlotType.Head, new Sprite("Content/UI/MainIconsAtlas.png", new Rectangle(896 + margin, 128 + margin, 128 - margin * 2, 128 - margin * 2)));
                    limbSlotIcons.Add(InvSlotType.LeftHand, new Sprite("Content/UI/InventoryUIAtlas.png", new Rectangle(634, 0, 128, 128)));
                    limbSlotIcons.Add(InvSlotType.RightHand, new Sprite("Content/UI/InventoryUIAtlas.png", new Rectangle(762, 0, 128, 128)));
                    limbSlotIcons.Add(InvSlotType.OuterClothes, new Sprite("Content/UI/MainIconsAtlas.png", new Rectangle(256 + margin, 128 + margin, 128 - margin * 2, 128 - margin * 2)));
                    limbSlotIcons.Add(InvSlotType.Bag, new Sprite("Content/UI/CommandUIAtlas.png", new Rectangle(639, 926, 128,80)));
                }
                return limbSlotIcons;
            }
        }

        public const InvSlotType PersonalSlots = InvSlotType.Card | InvSlotType.Bag | InvSlotType.Headset | InvSlotType.InnerClothes | InvSlotType.OuterClothes | InvSlotType.Head;

        private Point screenResolution;

        public Vector2[] SlotPositions;
        public static Point SlotSize;

        private Layout layout;
        public Layout CurrentLayout
        {
            get { return layout; }
            set
            {
                if (layout == value) return;
                layout = value;
                SetSlotPositions(layout);
            }
        }

        private Rectangle personalSlotArea;

        partial void InitProjSpecific(XElement element)
        {
            SlotPositions = new Vector2[SlotTypes.Length];
            CurrentLayout = Layout.Default;
            SetSlotPositions(layout);
        }

        protected override ItemInventory GetActiveEquippedSubInventory(int slotIndex)
        {
            Item item = slots[slotIndex].FirstOrDefault();
            if (item == null) { return null; }

            var container = item.GetComponent<ItemContainer>();
            if (container == null || !container.KeepOpenWhenEquippedBy(character))
            {
                return null;
            }
            return container.Inventory;
        }

        public override void CreateSlots()
        {
            visualSlots ??= new VisualSlot[capacity];

            float multiplier = UIScale * GUI.AspectRatioAdjustment;
            
            for (int i = 0; i < capacity; i++)
            {
                VisualSlot prevSlot = visualSlots[i];
                
                Sprite slotSprite = SlotSpriteSmall;
                Rectangle slotRect = new Rectangle(
                    (int)SlotPositions[i].X, 
                    (int)SlotPositions[i].Y,
                    (int)(slotSprite.size.X * multiplier), (int)(slotSprite.size.Y * multiplier));

                if (SlotTypes[i] == InvSlotType.HealthInterface &&
                    character.CharacterHealth?.InventorySlotContainer != null)
                {
                    slotRect.Width = slotRect.Height = (int)(character.CharacterHealth.InventorySlotContainer.Rect.Width * 1.2f);
                }
             
                ItemContainer itemContainer = slots[i].FirstOrDefault()?.GetComponent<ItemContainer>();
                if (itemContainer != null)
                {
                    if (itemContainer.InventoryTopSprite != null) slotRect.Width = Math.Max(slotRect.Width, (int)(itemContainer.InventoryTopSprite.size.X * UIScale));
                    if (itemContainer.InventoryBottomSprite != null) slotRect.Width = Math.Max(slotRect.Width, (int)(itemContainer.InventoryBottomSprite.size.X * UIScale));
                }                

                visualSlots[i] = new VisualSlot(slotRect)
                {
                    SubInventoryDir = Math.Sign(GameMain.GraphicsHeight / 2 - slotRect.Center.Y),
                    Disabled = false,
                    SlotSprite = slotSprite,
                    Color = SlotTypes[i] == InvSlotType.Any ? Color.White * 0.2f : Color.White * 0.4f
                };
                if (prevSlot != null)
                {
                    visualSlots[i].DrawOffset = prevSlot.DrawOffset;
                    visualSlots[i].Color = prevSlot.Color;
                    prevSlot.MoveBorderHighlight(visualSlots[i]);
                }
                if (selectedSlot?.ParentInventory == this && selectedSlot.SlotIndex == i)
                {
                    selectedSlot = new SlotReference(this, visualSlots[i], i, selectedSlot.IsSubSlot, selectedSlot.Inventory);
                }
            }

            AssignQuickUseNumKeys();

            highlightedSubInventorySlots.RemoveWhere(s => s.Inventory.OpenState <= 0.0f);
            foreach (var subSlot in highlightedSubInventorySlots)
            {
                if (subSlot.ParentInventory == this && subSlot.SlotIndex > 0 && subSlot.SlotIndex < visualSlots.Length)
                {
                    subSlot.Slot = visualSlots[subSlot.SlotIndex];
                }
            }

            screenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
            CalculateBackgroundFrame();
        }

        protected override void CalculateBackgroundFrame()
        {
            Rectangle frame = Rectangle.Empty;
            for (int i = 0; i < capacity; i++)
            {
                if (HideSlot(i)) continue;
                if (frame == Rectangle.Empty)
                {
                    frame = visualSlots[i].Rect;
                    continue;
                }
                frame = Rectangle.Union(frame, visualSlots[i].Rect);
            }
            frame.Inflate(10, 30);
            frame.Location -= new Point(0, 25);
            BackgroundFrame = frame;
        }

        protected override bool HideSlot(int i)
        {
            if (visualSlots[i].Disabled || (slots[i].HideIfEmpty && slots[i].Empty())) { return true; }

            if (CharacterHealth.OpenHealthWindow != Character.Controlled?.CharacterHealth && SlotTypes[i] == InvSlotType.HealthInterface) { return true; }

            if (layout == Layout.Default)
            {
                if (PersonalSlots.HasFlag(SlotTypes[i]) && !personalSlotArea.Contains(visualSlots[i].Rect.Center + visualSlots[i].DrawOffset.ToPoint())) { return true; }
            }

            Item item = slots[i].FirstOrDefault();

            //no need to draw the right hand slot if the item is in both hands
            if (item != null && SlotTypes[i] == InvSlotType.RightHand && IsInLimbSlot(item, InvSlotType.LeftHand))
            {
                return true;
            }

            //don't show the limb-specific slot if the item is also in an Any slot
            if (item != null && SlotTypes[i] != InvSlotType.Any)
            {
                if (IsInLimbSlot(item, InvSlotType.Any)) { return true; }
            }

            //don't draw equipment slots in wiring mode
            if (Screen.Selected == GameMain.SubEditorScreen && GameMain.SubEditorScreen.WiringMode)
            {
                if (SlotTypes[i] != InvSlotType.Any && SlotTypes[i] != InvSlotType.LeftHand && SlotTypes[i] != InvSlotType.RightHand)
                {
                    return true;
                }
            }

            return false;
        }

        private void SetSlotPositions(Layout layout)
        {
            int spacing = GUI.IntScale(5);

            SlotSize = (SlotSpriteSmall.size * UIScale * GUI.AspectRatioAdjustment).ToPoint();
            int bottomOffset = SlotSize.Y + spacing * 2 + ContainedIndicatorHeight;
            int personalSlotY = GameMain.GraphicsHeight - bottomOffset * 2 - spacing * 2 - (int)(UnequippedIndicator.size.Y * UIScale);

            if (visualSlots == null) { CreateSlots(); }
            if (visualSlots.None()) { return; }

            switch (layout)
            {
                case Layout.Default:
                    {
                        int personalSlotCount = SlotTypes.Count(s => PersonalSlots.HasFlag(s));
                        int normalSlotCount = SlotTypes.Count(s => !PersonalSlots.HasFlag(s) && s != InvSlotType.HealthInterface);

                        int x = GameMain.GraphicsWidth / 2 - normalSlotCount * (SlotSize.X + spacing) / 2;
                        int upperX = HUDLayoutSettings.BottomRightInfoArea.X - SlotSize.X - spacing;

                        //make sure the rightmost normal slot doesn't overlap with the personal slots
                        x -= Math.Max((x + normalSlotCount * (SlotSize.X + spacing)) - (upperX - personalSlotCount * (SlotSize.X + spacing)), 0);

                        int hideButtonSlotIndex = -1;
                        for (int i = 0; i < SlotPositions.Length; i++)
                        {
                            if (PersonalSlots.HasFlag(SlotTypes[i]))
                            {
                                SlotPositions[i] = new Vector2(upperX, GameMain.GraphicsHeight - bottomOffset);
                                upperX -= SlotSize.X + spacing;
                                personalSlotArea = (hideButtonSlotIndex == -1) ? 
                                    new Rectangle(SlotPositions[i].ToPoint(), SlotSize) :
                                    Rectangle.Union(personalSlotArea, new Rectangle(SlotPositions[i].ToPoint(), SlotSize));
                                hideButtonSlotIndex = i;
                            }
                            else
                            {
                                SlotPositions[i] = new Vector2(x, GameMain.GraphicsHeight - bottomOffset);
                                x += SlotSize.X + spacing;
                            }
                        }
                    }
                    break;
                case Layout.Right:
                    {
                        int x = HUDLayoutSettings.InventoryAreaLower.Right;
                        int personalSlotX = HUDLayoutSettings.InventoryAreaLower.Right - SlotSize.X - spacing;
                        for (int i = 0; i < visualSlots.Length; i++)
                        {
                            if (HideSlot(i) || SlotTypes[i] == InvSlotType.HealthInterface) { continue; }
                            if (SlotTypes[i] == InvSlotType.RightHand || SlotTypes[i] == InvSlotType.LeftHand) { continue; }
                            if (PersonalSlots.HasFlag(SlotTypes[i]))
                            {
                                //upperX -= slotSize.X + spacing;
                            }
                            else
                            {
                                x -= SlotSize.X + spacing;
                            }
                        }

                        int lowerX = x;
                        int handSlotX = x;
                        for (int i = 0; i < SlotPositions.Length; i++)
                        {
                            if (SlotTypes[i] == InvSlotType.RightHand || SlotTypes[i] == InvSlotType.LeftHand)
                            {
                                SlotPositions[i] = new Vector2(handSlotX, personalSlotY);
                                handSlotX += visualSlots[i].Rect.Width + spacing;
                                continue;
                            }

                            if (HideSlot(i) || SlotTypes[i] == InvSlotType.HealthInterface) { continue; }
                            if (PersonalSlots.HasFlag(SlotTypes[i]))
                            {
                                SlotPositions[i] = new Vector2(personalSlotX, personalSlotY);
                                personalSlotX -= visualSlots[i].Rect.Width + spacing;
                            }
                            else
                            {
                                SlotPositions[i] = new Vector2(x, GameMain.GraphicsHeight - bottomOffset);
                                x += visualSlots[i].Rect.Width + spacing;
                            }
                        }

                        x = lowerX;
                        for (int i = 0; i < SlotPositions.Length; i++)
                        {
                            if (!HideSlot(i) || SlotTypes[i] == InvSlotType.HealthInterface) { continue; }
                            if (SlotTypes[i] == InvSlotType.RightHand || SlotTypes[i] == InvSlotType.LeftHand) { continue; }
                            x -= visualSlots[i].Rect.Width + spacing;
                            SlotPositions[i] = new Vector2(x, GameMain.GraphicsHeight - bottomOffset);
                        }
                    }
                    break;
                case Layout.Left:
                    {
                        int x = HUDLayoutSettings.InventoryAreaLower.X;
                        int personalSlotX = x;

                        for (int i = 0; i < SlotPositions.Length; i++)
                        {
                            if (HideSlot(i) || SlotTypes[i] == InvSlotType.HealthInterface) { continue; }
                            if (SlotTypes[i] == InvSlotType.RightHand || SlotTypes[i] == InvSlotType.LeftHand) { continue; }
                            if (PersonalSlots.HasFlag(SlotTypes[i]))
                            {
                                SlotPositions[i] = new Vector2(personalSlotX, personalSlotY);
                                personalSlotX += visualSlots[i].Rect.Width + spacing;
                            }
                            else
                            {
                                SlotPositions[i] = new Vector2(x, GameMain.GraphicsHeight - bottomOffset);
                                x += visualSlots[i].Rect.Width + spacing;
                            }
                        }
                        int handSlotX = x - visualSlots[0].Rect.Width - spacing;
                        for (int i = 0; i < SlotPositions.Length; i++)
                        {
                            if (SlotTypes[i] == InvSlotType.RightHand || SlotTypes[i] == InvSlotType.LeftHand)
                            {
                                bool rightSlot = SlotTypes[i] == InvSlotType.RightHand;
                                SlotPositions[i] = new Vector2(rightSlot ? handSlotX : handSlotX - visualSlots[0].Rect.Width - spacing, personalSlotY);
                                continue;
                            }
                            if (!HideSlot(i) || SlotTypes[i] == InvSlotType.HealthInterface) { continue; }
                            SlotPositions[i] = new Vector2(x, GameMain.GraphicsHeight - bottomOffset);
                            x += visualSlots[i].Rect.Width + spacing;
                        }
                    }
                    break;
                case Layout.Center:
                    {
                        int columns = 5;
                        int startX = (GameMain.GraphicsWidth / 2) - (SlotSize.X * columns + spacing * (columns - 1)) / 2;
                        int startY = GameMain.GraphicsHeight / 2 - (SlotSize.Y * 2);
                        int x = startX, y = startY;
                        for (int i = 0; i < SlotPositions.Length; i++)
                        {
                            if (HideSlot(i) || SlotTypes[i] == InvSlotType.HealthInterface) { continue; }
                            if (SlotTypes[i] == InvSlotType.Card || SlotTypes[i] == InvSlotType.Headset || SlotTypes[i] == InvSlotType.InnerClothes)
                            {
                                SlotPositions[i] = new Vector2(x, y);
                                x += visualSlots[i].Rect.Width + spacing;
                            }
                        }
                        y += visualSlots[0].Rect.Height + spacing + ContainedIndicatorHeight + visualSlots[0].EquipButtonRect.Height;
                        x = startX;
                        int n = 0;
                        for (int i = 0; i < SlotPositions.Length; i++)
                        {
                            if (HideSlot(i) || SlotTypes[i] == InvSlotType.HealthInterface) { continue; }
                            if (SlotTypes[i] != InvSlotType.Card && SlotTypes[i] != InvSlotType.Headset && SlotTypes[i] != InvSlotType.InnerClothes)
                            {
                                SlotPositions[i] = new Vector2(x, y);
                                x += visualSlots[i].Rect.Width + spacing;
                                n++;
                                if (n >= columns)
                                {
                                    x = startX;
                                    y += visualSlots[i].Rect.Height + spacing + ContainedIndicatorHeight + visualSlots[i].EquipButtonRect.Height;
                                    n = 0;
                                }
                            }
                        }
                    }
                    break;
            }

            if (character.CharacterHealth?.UseHealthWindow ?? false)
            {
                Vector2 pos = character.CharacterHealth.InventorySlotContainer.Rect.Location.ToVector2();
                for (int i = 0; i < capacity; i++)
                {
                    if (SlotTypes[i] != InvSlotType.HealthInterface) { continue; }
                    SlotPositions[i] = pos;
                    pos.Y += visualSlots[i].Rect.Height + spacing;
                }
            }

            CreateSlots();
            if (layout == Layout.Default)
            {
                HUDLayoutSettings.InventoryTopY = visualSlots[0].EquipButtonRect.Y - (int)(15 * GUI.Scale);
            }
            else
            {
                for (int i = 0; i < capacity; i++)
                {
                    visualSlots[i].DrawOffset = Vector2.Zero;
                }
            }
        }

        protected override void ControlInput(Camera cam)
        {
            base.ControlInput(cam);
            // Ignore the background frame of this object in purpose, because it encompasses half of the screen.
            if (highlightedSubInventorySlots.Any(i => i.Inventory != null && i.Inventory.BackgroundFrame.Contains(PlayerInput.MousePosition)))
            {
                cam.Freeze = true;
            }
        }

        private readonly static List<SlotReference> hideSubInventories = new List<SlotReference>();
        private readonly static List<SlotReference> tempHighlightedSubInventorySlots = new List<SlotReference>();

        public override void Update(float deltaTime, Camera cam, bool isSubInventory = false)
        {
            if (!AccessibleWhenAlive && !character.IsDead && !AccessibleByOwner)
            {
                syncItemsDelay = Math.Max(syncItemsDelay - deltaTime, 0.0f);
                doubleClickedItems.Clear();
                return;
            }

            base.Update(deltaTime, cam);

            bool hoverOnInventory = GUI.MouseOn == null &&
                ((selectedSlot != null && selectedSlot.IsSubSlot) || (DraggingItems.Any() && (DraggingSlot == null || !DraggingSlot.MouseOn())));
            if (CharacterHealth.OpenHealthWindow != null) { hoverOnInventory = true; }

            if (hoverOnInventory) { HideTimer = 0.5f; }
            if (HideTimer > 0.0f) { HideTimer -= deltaTime; }

            UpdateSlotInput();

            hideSubInventories.Clear();
            //remove highlighted subinventory slots that can no longer be accessed
            highlightedSubInventorySlots.RemoveWhere(s => 
                s.ParentInventory == this &&
                ((s.SlotIndex < 0 || s.SlotIndex >= slots.Length || slots[s.SlotIndex] == null) || (Character.Controlled != null && !Character.Controlled.CanAccessInventory(s.Inventory))));
            //remove highlighted subinventory slots that refer to items no longer in this inventory
            highlightedSubInventorySlots.RemoveWhere(s => s.Item != null && s.ParentInventory == this && s.Item.ParentInventory != this);
            tempHighlightedSubInventorySlots.Clear();
            tempHighlightedSubInventorySlots.AddRange(highlightedSubInventorySlots);
            foreach (var highlightedSubInventorySlot in tempHighlightedSubInventorySlots)
            {
                if (highlightedSubInventorySlot.ParentInventory == this)
                {
                    UpdateSubInventory(deltaTime, highlightedSubInventorySlot.SlotIndex, cam);
                }

                if (!highlightedSubInventorySlot.Inventory.IsInventoryHoverAvailable(character, null)) continue;

                Rectangle hoverArea = GetSubInventoryHoverArea(highlightedSubInventorySlot);
                if (highlightedSubInventorySlot.Inventory?.visualSlots == null || (!hoverArea.Contains(PlayerInput.MousePosition)))
                {
                    hideSubInventories.Add(highlightedSubInventorySlot);
                }
                else
                {
                    highlightedSubInventorySlot.Inventory.HideTimer = 1.0f;
                }
            }

            //activate the subinventory of the currently selected slot
            if (selectedSlot?.ParentInventory == this)
            {
                var subInventory = GetSubInventory(selectedSlot.SlotIndex);
                if (subInventory != null && subInventory.IsInventoryHoverAvailable(character, null))
                {
                    selectedSlot.Inventory = subInventory;
                    if (!highlightedSubInventorySlots.Any(s => s.Inventory == subInventory))
                    {
                        ShowSubInventory(selectedSlot, deltaTime, cam, hideSubInventories, false);
                    }
                }
            }

            // In sub editor we cannot hover over the slot because they are not rendered so we override it here
            if (Screen.Selected is SubEditorScreen subEditor && !subEditor.WiringMode)
            {
                for (int i = 0; i < visualSlots.Length; i++)
                {
                    var subInventory = GetSubInventory(i);
                    if (subInventory != null)
                    {
                        ShowSubInventory(new SlotReference(this, visualSlots[i], i, false, subInventory), deltaTime, cam, hideSubInventories, true);
                    }
                }
            }
                       
            foreach (var subInventorySlot in hideSubInventories)
            {
                if (subInventorySlot.Inventory == null) { continue; }
                subInventorySlot.Inventory.HideTimer -= deltaTime;
                if (subInventorySlot.Inventory.HideTimer < 0.25f)
                {
                    highlightedSubInventorySlots.Remove(subInventorySlot);
                }
            }

            if (character == Character.Controlled && character.SelectedCharacter == null) // Permanently open subinventories only available when the default UI layout is in use -> not when grabbing characters
            {
                //remove the highlighted slots of other characters' inventories when not grabbing anyone
                highlightedSubInventorySlots.RemoveWhere(s => s.ParentInventory != this && s.ParentInventory?.Owner is Character);

                for (int i = 0; i < capacity; i++)
                {
                    var item = slots[i].FirstOrDefault();
                    if (item != null)
                    {
                        if (HideSlot(i)) { continue; }
                        if (character.HasEquippedItem(item)) // Keep a subinventory display open permanently when the container is equipped
                        {
                            var itemContainer = item.GetComponent<ItemContainer>();
                            if (itemContainer != null && 
                                itemContainer.KeepOpenWhenEquippedBy(character) && 
                                character.CanAccessInventory(itemContainer.Inventory) &&
                                !highlightedSubInventorySlots.Any(s => s.Inventory == itemContainer.Inventory))
                            {
                                ShowSubInventory(new SlotReference(this, visualSlots[i], i, false, itemContainer.Inventory), deltaTime, cam, hideSubInventories, true);
                            }
                        }
                    }
                }
            }

            if (doubleClickedItems.Any())
            {
                var quickUseAction = GetQuickUseAction(doubleClickedItems.First(), true, true, true);
                foreach (Item doubleClickedItem in doubleClickedItems)
                {
                    QuickUseItem(doubleClickedItem, true, true, true, quickUseAction, playSound: doubleClickedItem == doubleClickedItems.First());
                    //only use one item if we're equipping or using it as a treatment
                    if (quickUseAction == QuickUseAction.Equip || quickUseAction == QuickUseAction.UseTreatment)
                    {
                        break;
                    }
                    //if the item was put in a limb slot, only put one item from the stack
                    if (doubleClickedItem.ParentInventory == this && !IsInLimbSlot(doubleClickedItem, InvSlotType.Any))
                    {
                        break;
                    }
                    //if putting an item to a container with a max stack size of 1, only put one item from the stack
                    if (quickUseAction == QuickUseAction.PutToContainer && (character.SelectedItem?.GetComponent<ItemContainer>()?.MaxStackSize ?? 0) <= 1)
                    {
                        break;
                    }
                }
            }

            for (int i = 0; i < capacity; i++)
            {
                var item = slots[i].FirstOrDefault();
                if (item != null)
                {
                    var slot = visualSlots[i];
                    if (item.AllowedSlots.Any(a => a != InvSlotType.Any && a != InvSlotType.HealthInterface))
                    {
                        HandleButtonEquipStates(item, slot, deltaTime);
                    }
                }
            }

            //cancel dragging if too far away from the container of the dragged item
            if (DraggingItems.Any())
            {
                var rootContainer = DraggingItems.First().RootContainer;
                var rootInventory = DraggingItems.First().ParentInventory;

                if (rootContainer != null)
                {
                    rootInventory = rootContainer.ParentInventory ?? rootContainer.GetComponent<ItemContainer>().Inventory;
                }

                if (rootInventory != null &&
                    rootInventory.Owner != Character.Controlled &&
                    rootInventory.Owner != Character.Controlled.SelectedItem &&
                    rootInventory.Owner != Character.Controlled.SelectedCharacter)
                {
                    //allow interacting if the container is linked to the item the character is interacting with
                    if (!(rootContainer != null && 
                        rootContainer.DisplaySideBySideWhenLinked && 
                        Character.Controlled.SelectedItem != null &&
                        rootContainer.linkedTo.Contains(Character.Controlled.SelectedItem)))
                    {
                        DraggingItems.Clear();
                    }
                }
            }
            doubleClickedItems.Clear();
        }

        public void UpdateSlotInput()
        {
            for (int i = 0; i < capacity; i++)
            {
                var firstItem = slots[i].FirstOrDefault();
                if (firstItem != null && !DraggingItems.Contains(firstItem) && Character.Controlled?.Inventory == this &&
                    GUI.KeyboardDispatcher.Subscriber == null && !CrewManager.IsCommandInterfaceOpen && PlayerInput.InventoryKeyHit(visualSlots[i].InventoryKeyIndex))
                {
                    if (SubEditorScreen.IsSubEditor() && SubEditorScreen.SkipInventorySlotUpdate) { continue; }
#if LINUX
                    // some window managers on Linux use windows key + number to change workspaces or perform other actions
                    if (PlayerInput.KeyDown(Keys.RightWindows) || PlayerInput.KeyDown(Keys.LeftWindows)) { continue; }
#endif
                    var quickUseAction = GetQuickUseAction(firstItem, true, false, true);
                    foreach (Item itemToUse in slots[i].Items.ToList())
                    {
                        QuickUseItem(itemToUse, true, true, true, quickUseAction, playSound: itemToUse == firstItem);
                        if (quickUseAction == QuickUseAction.Equip || quickUseAction == QuickUseAction.UseTreatment)
                        {
                            break;
                        }
                    }
                }
            }
        }

        private void HandleButtonEquipStates(Item item, VisualSlot slot, float deltaTime)
        {
            slot.EquipButtonState = slot.EquipButtonRect.Contains(PlayerInput.MousePosition) ?
                        GUIComponent.ComponentState.Hover : GUIComponent.ComponentState.None;
            if (PlayerInput.PrimaryMouseButtonHeld() && PlayerInput.SecondaryMouseButtonHeld())
            {
                slot.EquipButtonState = GUIComponent.ComponentState.None;
            }

            if (slot.EquipButtonState != GUIComponent.ComponentState.Hover)
            {
                slot.QuickUseTimer = Math.Max(0.0f, slot.QuickUseTimer - deltaTime * 5.0f);
                return;
            }

            var quickUseAction = GetQuickUseAction(item, allowEquip: true, allowInventorySwap: false, allowApplyTreatment: false);

            if (quickUseAction != QuickUseAction.Drop)
            {
                slot.QuickUseButtonToolTip = quickUseAction == QuickUseAction.None ?
                    "" : TextManager.GetWithVariable("QuickUseAction." + quickUseAction.ToString(), "[equippeditem]", character.HeldItems.FirstOrDefault()?.Name ?? item?.Name);
                if (PlayerInput.PrimaryMouseButtonDown()) { slot.EquipButtonState = GUIComponent.ComponentState.Pressed; }
                if (PlayerInput.PrimaryMouseButtonClicked())
                {
                    QuickUseItem(item, allowEquip: true, allowInventorySwap: false, allowApplyTreatment: false);
                }
            }
        }

        private void ShowSubInventory(SlotReference slotRef, float deltaTime, Camera cam, List<SlotReference> hideSubInventories, bool isEquippedSubInventory)
        {
            Rectangle hoverArea = GetSubInventoryHoverArea(slotRef);
            if (isEquippedSubInventory)
            {
                foreach (SlotReference highlightedSubInventorySlot in highlightedSubInventorySlots)
                {
                    if (highlightedSubInventorySlot == slotRef) continue;
                    if (hoverArea.Intersects(GetSubInventoryHoverArea(highlightedSubInventorySlot)))
                    {
                        return; // If an equipped one intersects with a currently active hover one, do not open
                    }
                }
            }

            if (isEquippedSubInventory)
            {
                slotRef.Inventory.OpenState = 1.0f; // Reset animation when initially equipped
            }

            highlightedSubInventorySlots.Add(slotRef);
            slotRef.Inventory.HideTimer = 1f;
            UpdateSubInventory(deltaTime, slotRef.SlotIndex, cam);

            //hide previously opened subinventories if this one overlaps with them
            foreach (SlotReference highlightedSubInventorySlot in highlightedSubInventorySlots)
            {
                if (highlightedSubInventorySlot == slotRef) continue;
                if (hoverArea.Intersects(GetSubInventoryHoverArea(highlightedSubInventorySlot)))
                {
                    hideSubInventories.Add(highlightedSubInventorySlot);
                    highlightedSubInventorySlot.Inventory.HideTimer = 0.0f;
                }
            }

            HintManager.OnShowSubInventory(slotRef?.Item);
        }
        
        public void AssignQuickUseNumKeys()
        {
            int keyBindIndex = 0;
            for (int i = 0; i < visualSlots.Length; i++)
            {
                if (HideSlot(i)) continue;
                if (SlotTypes[i] == InvSlotType.Any)
                {
                    visualSlots[i].InventoryKeyIndex = keyBindIndex;
                    keyBindIndex++;
                }
            }
        }

        private QuickUseAction GetQuickUseAction(Item item, bool allowEquip, bool allowInventorySwap, bool allowApplyTreatment)
        {
            if (allowApplyTreatment && CharacterHealth.OpenHealthWindow != null && 
                //if the item can be equipped in the health interface slot, don't use it as a treatment but try to equip it
                !item.AllowedSlots.Contains(InvSlotType.HealthInterface))
            {
                return QuickUseAction.UseTreatment;
            }
            
            if (item.ParentInventory != this)
            {
                if (Screen.Selected == GameMain.GameScreen)
                {
                    if (item.NonInteractable || item.NonPlayerTeamInteractable)
                    {
                        return QuickUseAction.None;
                    }
                }
                if (item.ParentInventory == null || item.ParentInventory.Locked)
                {
                    return QuickUseAction.None;
                }
                //in another inventory -> attempt to place in the character's inventory
                else if (allowInventorySwap)
                {
                    if (item.Container == null || character.Inventory.FindIndex(item.Container) == -1) // Not a subinventory in the character's inventory
                    {
                        if (character.HeldItems.Any(i => i.OwnInventory != null && i.OwnInventory.CanBePut(item)))
                        {
                            return QuickUseAction.PutToEquippedItem;
                        }
                        else
                        {
                            return item.ParentInventory is CharacterInventory ? QuickUseAction.TakeFromCharacter : QuickUseAction.TakeFromContainer;
                        }
                    }
                    else
                    {
                        var selectedContainer = character.SelectedItem?.GetComponent<ItemContainer>();
                        if (selectedContainer != null &&
                            selectedContainer.Inventory != null &&
                            !selectedContainer.Inventory.Locked)
                        {
                            // Move the item from the subinventory to the selected container
                            return QuickUseAction.PutToContainer;
                        }
                        else if (character.Inventory.AccessibleWhenAlive || character.Inventory.AccessibleByOwner)
                        {
                            // Take from the subinventory and place it in the character's main inventory if no target container is selected
                            return QuickUseAction.TakeFromContainer;
                        }
                    }
                }
            }
            else
            {
                var selectedContainer = character.SelectedItem?.GetComponent<ItemContainer>();

                if (selectedContainer != null && 
                    selectedContainer.Inventory != null && 
                    !selectedContainer.Inventory.Locked && 
                    allowInventorySwap)
                {
                    //player has selected the inventory of another item -> attempt to move the item there
                    return QuickUseAction.PutToContainer;
                }
                else if (character.SelectedCharacter?.Inventory != null && 
                    !character.SelectedCharacter.Inventory.Locked && 
                    allowInventorySwap)
                {
                    //player has selected the inventory of another character -> attempt to move the item there
                    return QuickUseAction.PutToCharacter;
                }
                else if (character.SelectedBy?.Inventory != null && 
                    Character.Controlled == character.SelectedBy &&
                    !character.SelectedBy.Inventory.Locked &&
                    (character.SelectedBy.Inventory.AccessibleWhenAlive || character.SelectedBy.Inventory.AccessibleByOwner) &&
                    allowInventorySwap)
                {
                    return QuickUseAction.TakeFromCharacter;
                }
                else if (character.HeldItems.Any(i => 
                    i.OwnInventory != null &&
                    (i.OwnInventory.CanBePut(item) || ((i.OwnInventory.Capacity == 1 || i.OwnInventory.Container.HasSubContainers) && i.OwnInventory.AllowSwappingContainedItems && i.OwnInventory.Container.CanBeContained(item)))))
                {
                    return QuickUseAction.PutToEquippedItem;
                }
                else if (allowEquip) //doubleclicked and no other inventory is selected
                {
                    //not equipped -> attempt to equip
                    if (!character.HasEquippedItem(item) || item.GetComponents<Pickable>().Count() > 1)
                    {
                        return QuickUseAction.Equip;
                    }
                    //equipped -> attempt to unequip
                    else if (item.AllowedSlots.Contains(InvSlotType.Any))
                    {
                        return QuickUseAction.Unequip;
                    }
                    else
                    {
                        return QuickUseAction.Drop;
                    }
                }
            }

            return QuickUseAction.None;
        }

        private void QuickUseItem(Item item, bool allowEquip, bool allowInventorySwap, bool allowApplyTreatment, QuickUseAction? action = null, bool playSound = true)
        {
            if (Screen.Selected is SubEditorScreen editor && !editor.WiringMode && !Submarine.Unloading)
            {
                // Find the slot the item was contained in and flash it
                if (item.ParentInventory?.visualSlots != null)
                {
                    var invSlots = item.ParentInventory.visualSlots;
                    for (int i = 0; i < invSlots.Length; i++)
                    {
                        if (i < 0 || invSlots.Length <= i || i < 0 || item.ParentInventory.Capacity <= i) { break; }
                        
                        var slot = invSlots[i];
                        if (item.ParentInventory.GetItemAt(i) == item)
                        {
                            slot.ShowBorderHighlight(GUIStyle.Red, 0.1f, 0.4f);
                            SoundPlayer.PlayUISound(GUISoundType.PickItem);
                            break;
                        }
                    }
                }
                
                SubEditorScreen.StoreCommand(new AddOrDeleteCommand(new List<MapEntity> { item }, true));

                item.Remove();
                return;
            }

            QuickUseAction quickUseAction = action ?? GetQuickUseAction(item, allowEquip, allowInventorySwap, allowApplyTreatment);
            bool success = false;
            switch (quickUseAction)
            {
                case QuickUseAction.Equip:
                    if (string.IsNullOrEmpty(item.Prefab.EquipConfirmationText) || character != Character.Controlled)
                    {
                        Equip();
                    }
                    else
                    {
                        if (GUIMessageBox.MessageBoxes.Any(mb => mb.UserData as string == "equipconfirmation")) { return; }
                        var equipConfirmation = new GUIMessageBox(string.Empty, TextManager.Get(item.Prefab.EquipConfirmationText),
                            new LocalizedString[] { TextManager.Get("yes"), TextManager.Get("no") })
                        {
                            UserData = "equipconfirmation"
                        };
                        equipConfirmation.Buttons[0].OnClicked = (btn, userdata) =>
                        {
                            Equip();
                            equipConfirmation.Close();
                            return true;
                        };
                        equipConfirmation.Buttons[1].OnClicked = equipConfirmation.Close;
                    }

                    void Equip()
                    {
                        //attempt to put in a free slot first
                        for (int i = capacity - 1; i >= 0; i--)
                        {
                            if (!slots[i].Empty()) { continue; }
                            if (SlotTypes[i] == InvSlotType.Any || !item.AllowedSlots.Any(a => a.HasFlag(SlotTypes[i]))) { continue; }
                            success = TryPutItem(item, i, true, false, Character.Controlled, true);
                            if (success) { break; }
                        }

                        if (!success)
                        {
                            for (int i = capacity - 1; i >= 0; i--)
                            {
                                if (SlotTypes[i] == InvSlotType.Any || !item.AllowedSlots.Any(a => a.HasFlag(SlotTypes[i]))) { continue; }
                                // something else already equipped in a hand slot, attempt to unequip it so items aren't unnecessarily swapped to it
                                if (!slots[i].Empty() && slots[i].First().AllowedSlots.Contains(InvSlotType.Any) && 
                                    (SlotTypes[i] == InvSlotType.LeftHand || SlotTypes[i] == InvSlotType.RightHand))
                                {
                                    TryPutItem(slots[i].First(), Character.Controlled, new List<InvSlotType>() { InvSlotType.Any }, true);
                                }
                                success = TryPutItem(item, i, true, false, Character.Controlled, true);
                                if (success) { break; }
                            }
                        }
                    }
                    break;
                case QuickUseAction.Unequip:
                    if (item.AllowedSlots.Contains(InvSlotType.Any))
                    {
                        success = TryPutItem(item, Character.Controlled, new List<InvSlotType>() { InvSlotType.Any }, true);
                    }
                    break;
                case QuickUseAction.UseTreatment:
                    CharacterHealth.OpenHealthWindow?.OnItemDropped(item, ignoreMousePos: true);
                    return;
                case QuickUseAction.Drop:
                    //do nothing, the item is dropped after a delay
                    return;
                case QuickUseAction.PutToCharacter:
                    if (character.SelectedCharacter != null && character.SelectedCharacter.Inventory != null)
                    {
                        //player has selected the inventory of another character -> attempt to move the item there
                        success = character.SelectedCharacter.Inventory.TryPutItem(item, Character.Controlled, item.AllowedSlots, true);
                    }
                    break;
                case QuickUseAction.PutToContainer:
                    var selectedContainer = character.SelectedItem?.GetComponent<ItemContainer>();
                    if (selectedContainer != null && selectedContainer.Inventory != null)
                    {
                        //player has selected the inventory of another item -> attempt to move the item there
                        success = selectedContainer.Inventory.TryPutItem(item, Character.Controlled, item.AllowedSlots, true);
                    }
                    break;
                case QuickUseAction.TakeFromCharacter:
                    if (character.SelectedBy != null && Character.Controlled == character.SelectedBy &&
                        character.SelectedBy.Inventory != null)
                    {
                        //item is in the inventory of another character -> attempt to get the item from there
                        success = character.SelectedBy.Inventory.TryPutItemWithAutoEquipCheck(item, Character.Controlled, item.AllowedSlots, true);
                    }
                    break;
                case QuickUseAction.TakeFromContainer:
                    // Check open subinventories and put the item in it if equipped
                    ItemInventory activeSubInventory = null;
                    for (int i = 0; i < capacity; i++)
                    {
                        activeSubInventory = GetActiveEquippedSubInventory(i);
                        if (activeSubInventory != null)
                        {
                            success = activeSubInventory.TryPutItem(item, Character.Controlled, item.AllowedSlots, true);
                            break;
                        }
                    }                            

                    // No subinventory found or placing unsuccessful -> attempt to put in the character's inventory
                    if (!success)
                    {
                        success = TryPutItemWithAutoEquipCheck(item, Character.Controlled, item.AllowedSlots, true);
                    }
                    break;
                case QuickUseAction.PutToEquippedItem:
                    //order by the condition of the contained item to prefer putting into the item with the emptiest ammo/battery/tank
                    foreach (Item heldItem in character.HeldItems.OrderByDescending(heldItem => GetContainPriority(item, heldItem)))
                    {
                        if (heldItem.OwnInventory == null) { continue; }
                        //don't allow swapping if we're moving items into an item with 1 slot holding a stack of items
                        //(in that case, the quick action should just fill up the stack)
                        bool disallowSwapping = 
                            (heldItem.OwnInventory.Capacity == 1 || heldItem.OwnInventory.Container.HasSubContainers) &&
                            heldItem.OwnInventory.GetItemAt(0)?.Prefab == item.Prefab && 
                            heldItem.OwnInventory.GetItemsAt(0).Count() > 1;
                        if (heldItem.OwnInventory.TryPutItem(item, Character.Controlled) || 
                            ((heldItem.OwnInventory.Capacity == 1 || heldItem.OwnInventory.Container.HasSubContainers) && heldItem.OwnInventory.TryPutItem(item, 0, allowSwapping: !disallowSwapping, allowCombine: false, user: Character.Controlled)))
                        {
                            success = true;
                            for (int j = 0; j < capacity; j++)
                            {
                                if (slots[j].Contains(heldItem)) { visualSlots[j].ShowBorderHighlight(GUIStyle.Green, 0.1f, 0.4f); }
                            }
                            break;
                        }
                    }
                    break;

                    static float GetContainPriority(Item item, Item containerItem)
                    {
                        var container = containerItem.GetComponent<ItemContainer>();
                        if (container == null) { return 0.0f; }
                        for (int i = 0; i < container.Inventory.Capacity; i++)
                        {
                            var containedItems = container.Inventory.GetItemsAt(i);
                            if (containedItems.Any() && container.Inventory.CanBePutInSlot(item, i))
                            {
                                //if there's a stack in the contained item that we can add the item to, prefer that
                                return 10.0f;
                            }
                        }
                        return -container.GetContainedIndicatorState();
                    }
            }

            if (success)
            {
                for (int i = 0; i < capacity; i++)
                {
                    if (slots[i].Contains(item)) { visualSlots[i].ShowBorderHighlight(GUIStyle.Green, 0.1f, 0.4f); }
                }
            }

            DraggingItems.Clear();
            if (playSound)
            {
                SoundPlayer.PlayUISound(success ? GUISoundType.PickItem : GUISoundType.PickItemFail);
            }
        }

        public bool CanBeAutoMovedToCorrectSlots(Item item)
        {
            if (item == null) { return false; }
            foreach (var allowedSlot in item.AllowedSlots)
            {
                InvSlotType slotsFree = InvSlotType.None;
                for (int i = 0; i < slots.Length; i++)
                {
                    if (allowedSlot.HasFlag(SlotTypes[i]) && slots[i].Empty()) { slotsFree |= SlotTypes[i]; }
                }
                if (allowedSlot == slotsFree) { return true; }
            }
            return false;
        }

        /// <summary>
        /// Flash the slots the item is allowed to go in (not taking into account whether there's already something in those slots)
        /// </summary>
        public void FlashAllowedSlots(Item item, Color color)
        {
            if (item == null || visualSlots == null) { return; }
            bool flashed = false;
            foreach (var allowedSlot in item.AllowedSlots)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    if (allowedSlot.HasFlag(SlotTypes[i]))
                    {
                        visualSlots[i].ShowBorderHighlight(color, 0.1f, 0.9f);
                        flashed = true;
                    }
                }
            }
            if (flashed)
            {
                SoundPlayer.PlayUISound(GUISoundType.PickItemFail);
            }
        }


        public void DrawOwn(SpriteBatch spriteBatch)
        {
            if (!AccessibleWhenAlive && !character.IsDead && !AccessibleByOwner) { return; }
            if (capacity == 0) { return; }
            if (visualSlots == null) { CreateSlots(); }
            if (GameMain.GraphicsWidth != screenResolution.X ||
                GameMain.GraphicsHeight != screenResolution.Y ||
                prevUIScale != UIScale ||
                prevHUDScale != GUI.Scale)
            {
                CreateSlots();
                SetSlotPositions(layout);
                prevUIScale = UIScale;
                prevHUDScale = GUI.Scale;
            }

            if (layout == Layout.Center)
            {
                CalculateBackgroundFrame();
                GUI.DrawRectangle(spriteBatch, BackgroundFrame, Color.Black * 0.8f, true);
                GUI.DrawString(spriteBatch,
                    new Vector2((int)(BackgroundFrame.Center.X - GUIStyle.Font.MeasureString(character.Name).X / 2), (int)BackgroundFrame.Y + 5),
                    character.Name, Color.White * 0.9f);
            }

            for (int i = 0; i < capacity; i++)
            {
                if (HideSlot(i) || SlotTypes[i] == InvSlotType.HealthInterface) { continue; }

                //don't draw the item if it's being dragged out of the slot
                bool drawItem = !DraggingItems.Any() || !slots[i].Items.All(it => DraggingItems.Contains(it)) || visualSlots[i].MouseOn();

                DrawSlot(spriteBatch, this, visualSlots[i], slots[i].FirstOrDefault(), i, drawItem, SlotTypes[i]);
            }
            
            VisualSlot highlightedQuickUseSlot = null;
            Rectangle inventoryArea = Rectangle.Empty;

            for (int i = 0; i < capacity; i++)
            {
                if (HideSlot(i)) { continue; }

                inventoryArea = inventoryArea == Rectangle.Empty ? visualSlots[i].InteractRect : Rectangle.Union(inventoryArea, visualSlots[i].InteractRect);

                if (slots[i].Empty() || 
                    (DraggingItems.Any(it => slots[i].Contains(it)) && !visualSlots[i].InteractRect.Contains(PlayerInput.MousePosition)) || 
                    !slots[i].First().AllowedSlots.Any(a => a != InvSlotType.Any))
                {
                    //draw limb icons on empty slots
                    if (LimbSlotIcons.ContainsKey(SlotTypes[i]))
                    {
                        var icon = LimbSlotIcons[SlotTypes[i]];
                        icon.Draw(spriteBatch, visualSlots[i].Rect.Center.ToVector2() + visualSlots[i].DrawOffset, GUIStyle.EquipmentSlotIconColor, origin: icon.size / 2, scale: visualSlots[i].Rect.Width / icon.size.X);
                    }
                    continue;
                }
                if (DraggingItems.Any(it => slots[i].Contains(it)) && !visualSlots[i].IsHighlighted) { continue; }
                
                //draw hand icons if the item is equipped in a hand slot
                if (IsInLimbSlot(slots[i].First(), InvSlotType.LeftHand))
                {
                    var icon = LimbSlotIcons[InvSlotType.LeftHand];
                    icon.Draw(spriteBatch, new Vector2(visualSlots[i].Rect.X, visualSlots[i].Rect.Bottom) + visualSlots[i].DrawOffset, Color.White * 0.6f, origin: new Vector2(icon.size.X * 0.35f, icon.size.Y * 0.75f), scale: visualSlots[i].Rect.Width / icon.size.X * 0.7f);
                }
                if (IsInLimbSlot(slots[i].First(), InvSlotType.RightHand))
                {
                    var icon = LimbSlotIcons[InvSlotType.RightHand];
                    icon.Draw(spriteBatch, new Vector2(visualSlots[i].Rect.Right, visualSlots[i].Rect.Bottom) + visualSlots[i].DrawOffset, Color.White * 0.6f, origin: new Vector2(icon.size.X * 0.65f, icon.size.Y * 0.75f), scale: visualSlots[i].Rect.Width / icon.size.X * 0.7f);
                }

                GUIComponent.ComponentState state = visualSlots[i].EquipButtonState;
                if (state == GUIComponent.ComponentState.Hover)
                {       
                    highlightedQuickUseSlot = visualSlots[i];
                }

                if (slots[i].First().AllowedSlots.Count() == 1 || SlotTypes[i] == InvSlotType.HealthInterface)
                {
                    continue;
                }

                Color color = Color.White;
                if (Locked)
                { 
                    color *= 0.5f; 
                }

                Vector2 indicatorScale = new Vector2(
                     visualSlots[i].EquipButtonRect.Size.X / EquippedIndicator.size.X,
                    visualSlots[i].EquipButtonRect.Size.Y / EquippedIndicator.size.Y);

                bool isEquipped = character.HasEquippedItem(slots[i].First());
                var sprite = state switch
                {
                    GUIComponent.ComponentState.None
                        => isEquipped ? EquippedIndicator : UnequippedIndicator,
                    GUIComponent.ComponentState.Hover
                        => isEquipped ? EquippedHoverIndicator : UnequippedHoverIndicator,
                    GUIComponent.ComponentState.Pressed
                    or GUIComponent.ComponentState.Selected
                    or GUIComponent.ComponentState.HoverSelected
                        => isEquipped ? EquippedClickedIndicator : UnequippedClickedIndicator,
                    _ => throw new NotImplementedException()
                };
                sprite.Draw(spriteBatch, visualSlots[i].EquipButtonRect.Center.ToVector2(), color, EquippedIndicator.Origin, 0, indicatorScale);
            }

            if (Locked)
            {
                GUI.DrawRectangle(spriteBatch, inventoryArea, new Color(30,30,30,100), isFilled: true);
                var lockIcon = GUIStyle.GetComponentStyle("LockIcon")?.GetDefaultSprite();
                lockIcon?.Draw(spriteBatch, inventoryArea.Center.ToVector2(), scale: Math.Min(inventoryArea.Height / lockIcon.size.Y * 0.7f, 1.0f));
                if (inventoryArea.Contains(PlayerInput.MousePosition) && character.LockHands)
                {
                    GUIComponent.DrawToolTip(spriteBatch, TextManager.Get("handcuffed"), new Rectangle(inventoryArea.Center - new Point(inventoryArea.Height / 2), new Point(inventoryArea.Height)));
                }
            }
            else if (highlightedQuickUseSlot != null && !highlightedQuickUseSlot.QuickUseButtonToolTip.IsNullOrEmpty())
            {
                GUIComponent.DrawToolTip(spriteBatch, highlightedQuickUseSlot.QuickUseButtonToolTip, highlightedQuickUseSlot.EquipButtonRect);
            }
        }
    }
}
