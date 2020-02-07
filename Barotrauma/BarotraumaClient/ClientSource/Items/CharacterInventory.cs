using Barotrauma.Extensions;
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
        private static Sprite inventoryBackgroundSprite, inventoryExtendButton, inventoryExtendUpArrow, inventoryExtendDownArrow;

        public Rectangle InventoryToggleArea;
        public bool InventoryToggleContains = false;
        private Vector2 inventoryExtendButtonOffset, inventoryArrowOffset;
        private int inventoryOpeningOffset;

        public const InvSlotType PersonalSlots = InvSlotType.Card | InvSlotType.Headset | InvSlotType.InnerClothes | InvSlotType.OuterClothes | InvSlotType.Head;

        public Vector2[] SlotPositions;
        private Vector2 bgScale;

        private Layout layout;
        public Layout CurrentLayout
        {
            get { return layout; }
            set
            {
                if (layout == value) return;
                layout = value;
                SetSlotPositions();
            }
        }
        public bool Hidden { get; set; }
        
        private bool hidePersonalSlots;
        private float hidePersonalSlotsState;
        private GUIButton hideButton;
        private Rectangle personalSlotArea;
        private bool inventoryOpen = false;
        private bool wasInventoryToggledAutomatically = false;

        public bool HidePersonalSlots
        {
            get { return hidePersonalSlots; }
        }

        public Rectangle PersonalSlotArea
        {
            get { return personalSlotArea; }
        }
        
        partial void InitProjSpecific(XElement element)
        {
            Hidden = true;

            hideButton = new GUIButton(new RectTransform(new Point((int)(30 * GUI.Scale), (int)(60 * GUI.Scale)), GUI.Canvas)
            { AbsoluteOffset = HUDLayoutSettings.CrewArea.Location },
                "", style: "UIToggleButton");
            hideButton.Children.ForEach(c => c.SpriteEffects = SpriteEffects.FlipHorizontally);
            hideButton.OnClicked += (GUIButton btn, object userdata) =>
            {
                hidePersonalSlots = !hidePersonalSlots;
                foreach (GUIComponent child in btn.Children)
                {
                    child.SpriteEffects = hidePersonalSlots ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
                }
                return true;
            };

            hidePersonalSlots = false;

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

                inventoryBackgroundSprite = new Sprite("Content/UI/InventoryUIAtlas.png", new Rectangle(252, 317, 263, 197));
                inventoryExtendButton = new Sprite("Content/UI/InventoryUIAtlas.png", new Rectangle(533, 300, 96, 19));
                inventoryExtendUpArrow = new Sprite("Content/UI/InventoryUIAtlas.png", new Rectangle(640, 310, 10, 10));
                inventoryExtendDownArrow = new Sprite("Content/UI/InventoryUIAtlas.png", new Rectangle(668, 310, 10, 10));
            }

            SlotPositions = new Vector2[SlotTypes.Length];
            CurrentLayout = Layout.Default;
            SetSlotPositions();
            GameMain.Instance.OnResolutionChanged += SetSlotPositions;
        }

        protected override ItemInventory GetActiveEquippedSubInventory(int slotIndex)
        {
            var item = Items[slotIndex];
            if (item == null) return null;

            var container = item.GetComponent<ItemContainer>();
            if (container == null || 
                !character.CanAccessInventory(container.Inventory) ||
                !container.KeepOpenWhenEquipped || 
                !character.HasEquippedItem(container.Item))
            {
                return null;
            }

            return container.Inventory;
        }

        protected override void PutItem(Item item, int i, Character user, bool removeItem = true, bool createNetworkEvent = true)
        {
            base.PutItem(item, i, user, removeItem, createNetworkEvent);
            CreateSlots();
        }

        public override void RemoveItem(Item item)
        {
            if (!Items.Contains(item)) { return; }
            base.RemoveItem(item);
            CreateSlots();
        }

        public override void CreateSlots()
        {
            if (slots == null) { slots = new InventorySlot[capacity]; }
            
            for (int i = 0; i < capacity; i++)
            {
                InventorySlot prevSlot = slots[i];
                
                Sprite slotSprite = SlotSpriteSmall;
                Rectangle slotRect = new Rectangle(
                    (int)(SlotPositions[i].X), 
                    (int)(SlotPositions[i].Y),
                    (int)(slotSprite.size.X * UIScale), (int)(slotSprite.size.Y * UIScale));

                if (Items[i] != null)
                {
                    ItemContainer itemContainer = Items[i].GetComponent<ItemContainer>();
                    if (itemContainer != null)
                    {
                        if (itemContainer.InventoryTopSprite != null) slotRect.Width = Math.Max(slotRect.Width, (int)(itemContainer.InventoryTopSprite.size.X * UIScale));
                        if (itemContainer.InventoryBottomSprite != null) slotRect.Width = Math.Max(slotRect.Width, (int)(itemContainer.InventoryBottomSprite.size.X * UIScale));
                    }
                }

                slots[i] = new InventorySlot(slotRect)
                {
                    SubInventoryDir = Math.Sign(GameMain.GraphicsHeight / 2 - slotRect.Center.Y),
                    Disabled = false,
                    SlotSprite = slotSprite,
                    Color = SlotTypes[i] == InvSlotType.Any ? Color.White * 0.2f : Color.White * 0.4f
                };
                if (prevSlot != null)
                {
                    slots[i].DrawOffset = prevSlot.DrawOffset;
                    slots[i].Color = prevSlot.Color;
                }

                if (selectedSlot?.ParentInventory == this && selectedSlot.SlotIndex == i)
                {
                    selectedSlot = new SlotReference(this, slots[i], i, selectedSlot.IsSubSlot, selectedSlot.Inventory);
                }
            }

            AssignQuickUseNumKeys();

            highlightedSubInventorySlots.RemoveWhere(s => s.Inventory.OpenState <= 0.0f);
            foreach (var subSlot in highlightedSubInventorySlots)
            {
                if (subSlot.ParentInventory == this && subSlot.SlotIndex > 0 && subSlot.SlotIndex < slots.Length)
                {
                    subSlot.Slot = slots[subSlot.SlotIndex];
                }
            }

            CalculateBackgroundFrame();
        }

        protected override void CalculateBackgroundFrame()
        {
            Rectangle frame = Rectangle.Empty;
            int firstSlotLocationY = 0;
            int lastSlotLocationY = 0;

            for (int i = 0; i < capacity; i++)
            {
                if (HideSlot(i)) continue;
                if (PersonalSlots.HasFlag(SlotTypes[i])) continue;
                if (IsHandSlot(SlotTypes[i])) continue;
                if (frame == Rectangle.Empty)
                {
                    firstSlotLocationY = slots[i].Rect.Location.Y;
                    frame = slots[i].Rect;
                    continue;
                }
                frame = Rectangle.Union(frame, slots[i].Rect);
                lastSlotLocationY = slots[i].Rect.Location.Y;
            }

            frame.Inflate(25f * UIScale, 25f * UIScale);

            if (layout == Layout.Default)
            {
                inventoryOpeningOffset = firstSlotLocationY - lastSlotLocationY;

                bgScale = new Vector2((float)frame.Width / (float)inventoryBackgroundSprite.SourceRect.Width, (float)frame.Height / (float)inventoryBackgroundSprite.SourceRect.Height);

                inventoryExtendButtonOffset = new Vector2(inventoryExtendButton.size.X * UIScale * 1.5f / 2f, inventoryExtendButton.size.Y * UIScale * 1.5f / 2f + UIScale * 6);
                inventoryArrowOffset = new Vector2(inventoryExtendUpArrow.size.X * UIScale * 1.25f / 2f, inventoryExtendUpArrow.size.Y * UIScale * 1.25f / 2f);
                InventoryToggleArea = new Rectangle(new Point(frame.Center.X, frame.Top) - inventoryExtendButtonOffset.ToPoint(), inventoryExtendButton.size.ToPoint() + new Point(0, (int)(UIScale * 6f)));
                if (!inventoryOpen)
                {
                    frame.Offset(0, inventoryOpeningOffset);
                    InventoryToggleArea.Offset(0, inventoryOpeningOffset);
                }
            }

            BackgroundFrame = frame;
        }

        protected override bool HideSlot(int i)
        {
            if (slots[i].Disabled) return true;

            if (layout == Layout.Default)
            {
                if (PersonalSlots.HasFlag(SlotTypes[i]) && !personalSlotArea.Contains(slots[i].Rect.Center + slots[i].DrawOffset.ToPoint())) return true;
            }

            //no need to draw the right hand slot if the item is in both hands
            if (Items[i] != null && SlotTypes[i] == InvSlotType.RightHand && IsInLimbSlot(Items[i], InvSlotType.LeftHand))
            {
                return true;
            }

            return false;
        }

        protected override bool IsSlotHiddenDueToToggleState(int i)
        {
            return layout == Layout.Default && !inventoryOpen && slots[i].QuickUseKey == Keys.None && SlotTypes[i] == InvSlotType.Any;
        }

        private void SetSlotPositions()
        {
            Layout layout = CurrentLayout;
            int spacing = (int)(10 * UIScale);
            Point slotSize = (SlotSpriteSmall.size * UIScale).ToPoint();
            int bottomOffset = slotSize.Y + spacing * 2 + ContainedIndicatorHeight;

            if (slots == null) CreateSlots();

            hideButton.Visible = false;

            switch (layout)
            {
                case Layout.Default:
                    {
                        int personalSlotCount = SlotTypes.Count(s => PersonalSlots.HasFlag(s));
                        int normalSlotCount = SlotTypes.Count(s => !PersonalSlots.HasFlag(s));

                        int firstRowSlotCount = hotkeyCount > normalSlotCount ? normalSlotCount : hotkeyCount;

                        int startX = GameMain.GraphicsWidth / 2 - firstRowSlotCount * (slotSize.X + spacing) / 2;
                        int x = startX;
                        int equipmentX = GameMain.GraphicsWidth - slotSize.X * 2;
                        int buttonIndex = 0;
                        int startY = GameMain.GraphicsHeight - bottomOffset;
                        int y = startY;
                        int handIndex = 1;

                        //make sure the rightmost normal slot doesn't overlap with the personal slots
                        x -= Math.Max(x + firstRowSlotCount * (slotSize.X + spacing) - (equipmentX - personalSlotCount * (slotSize.X + spacing)), 0);

                        int hideButtonSlotIndex = -1;
                        for (int i = 0; i < SlotPositions.Length; i++)
                        {
                            if (PersonalSlots.HasFlag(SlotTypes[i]))
                            {
                                SlotPositions[i] = new Vector2(equipmentX, startY);
                                equipmentX -= slotSize.X + spacing;
                                personalSlotArea = (hideButtonSlotIndex == -1) ? 
                                    new Rectangle(SlotPositions[i].ToPoint(), slotSize) :
                                    Rectangle.Union(personalSlotArea, new Rectangle(SlotPositions[i].ToPoint(), slotSize));
                                hideButtonSlotIndex = i;
                            }
                            else if (IsHandSlot(SlotTypes[i]))
                            {
                                SlotPositions[i] = new Vector2(startX - (slotSize.X + spacing * 4) - (slotSize.X + spacing) * handIndex, startY);
                                handIndex--;
                            }
                            else
                            {
                                if (buttonIndex >= hotkeyCount)
                                {
                                    y -= bottomOffset;
                                    buttonIndex = 0;
                                    x = startX;
                                }

                                buttonIndex++;
                                SlotPositions[i] = new Vector2(x, y);
                                x += slotSize.X + spacing;
                            }
                        }

                        if (hideButtonSlotIndex > -1)
                        {
                            hideButton.RectTransform.SetPosition(Anchor.TopLeft, Pivot.TopLeft);
                            hideButton.RectTransform.NonScaledSize = new Point(slotSize.X / 2, slotSize.Y + slots[hideButtonSlotIndex].EquipButtonRect.Height);
                            hideButton.RectTransform.AbsoluteOffset = new Point(
                                personalSlotArea.Right + spacing, 
                                personalSlotArea.Y - slots[hideButtonSlotIndex].EquipButtonRect.Height);
                            hideButton.Visible = true;
                        }
                    }
                    break;
                case Layout.Right:
                    {
                        int x = HUDLayoutSettings.InventoryAreaLower.Right;
                        int personalSlotX = HUDLayoutSettings.InventoryAreaLower.Right - slotSize.X - spacing;
                        for (int i = 0; i < slots.Length; i++)
                        {
                            if (HideSlot(i)) continue;
                            if (PersonalSlots.HasFlag(SlotTypes[i]))
                            {
                                //upperX -= slotSize.X + spacing;
                            }
                            else
                            {      
                                if (i < slots.Length - 5)
                                {
                                    x -= slotSize.X + spacing;
                                }
                            }
                        }

                        int lowerX = x;
                        int y = GameMain.GraphicsHeight - bottomOffset;

                        for (int i = 0; i < SlotPositions.Length; i++)
                        {
                            if (HideSlot(i)) continue;
                            if (PersonalSlots.HasFlag(SlotTypes[i]))
                            {
                                SlotPositions[i] = new Vector2(personalSlotX, y - bottomOffset);
                                personalSlotX -= slots[i].Rect.Width + spacing;
                            }
                            else
                            {
                                SlotPositions[i] = new Vector2(x, y);
                                x += slots[i].Rect.Width + spacing;

                                if (i == SlotPositions.Length - 6)
                                {
                                    x = lowerX + (slots[i].Rect.Width + spacing) * 2;
                                    y -= bottomOffset;
                                }
                            }
                        }

                        x = lowerX;
                        for (int i = 0; i < SlotPositions.Length; i++)
                        {
                            if (!HideSlot(i)) continue;
                            x -= slots[i].Rect.Width + spacing;
                            SlotPositions[i] = new Vector2(x, y);
                        }
                    }
                    break;
                case Layout.Left:
                    {
                        int x = HUDLayoutSettings.InventoryAreaLower.Left;
                        int y = GameMain.GraphicsHeight - bottomOffset;
                        int personalSlotX = x;

                        for (int i = 0; i < SlotPositions.Length; i++)
                        {
                            if (HideSlot(i)) continue;
                            if (PersonalSlots.HasFlag(SlotTypes[i]))
                            {
                                SlotPositions[i] = new Vector2(personalSlotX, y - bottomOffset);
                                personalSlotX += slots[i].Rect.Width + spacing;
                            }
                            else
                            {
                                SlotPositions[i] = new Vector2(x, y);
                                x += slots[i].Rect.Width + spacing;

                                if (i == SlotPositions.Length - 7)
                                {
                                    x -= (slots[i].Rect.Width + spacing) * 6;
                                    y -= bottomOffset;
                                }
                            }
                        }

                        for (int i = 0; i < SlotPositions.Length; i++)
                        {
                            if (!HideSlot(i)) continue;
                            SlotPositions[i] = new Vector2(x, GameMain.GraphicsHeight - bottomOffset);
                            x += slots[i].Rect.Width + spacing;
                        }
                    }
                    break;
                case Layout.Center:
                    {
                        int columns = 5;
                        int startX = (GameMain.GraphicsWidth / 2) - (slotSize.X * columns + spacing * (columns - 1)) / 2;
                        int startY = GameMain.GraphicsHeight / 2 - (slotSize.Y * 2);
                        int x = startX, y = startY;
                        for (int i = 0; i < SlotPositions.Length; i++)
                        {
                            if (HideSlot(i)) continue;
                            if (SlotTypes[i] == InvSlotType.Card || SlotTypes[i] == InvSlotType.Headset || SlotTypes[i] == InvSlotType.InnerClothes)
                            {
                                SlotPositions[i] = new Vector2(x, y);
                                x += slots[i].Rect.Width + spacing;
                            }
                        }
                        y += slots[0].Rect.Height + spacing + ContainedIndicatorHeight + slots[0].EquipButtonRect.Height;
                        x = startX;
                        int n = 0;
                        for (int i = 0; i < SlotPositions.Length; i++)
                        {
                            if (HideSlot(i)) continue;
                            if (SlotTypes[i] != InvSlotType.Card && SlotTypes[i] != InvSlotType.Headset && SlotTypes[i] != InvSlotType.InnerClothes)
                            {
                                SlotPositions[i] = new Vector2(x, y);
                                x += slots[i].Rect.Width + spacing;
                                n++;
                                if (n >= columns)
                                {
                                    x = startX;
                                    y += slots[i].Rect.Height + spacing + ContainedIndicatorHeight + slots[i].EquipButtonRect.Height;
                                    n = 0;
                                }
                            }
                        }
                    }
                    break;
            }
            
            CreateSlots();
            if (layout == Layout.Default)
            {
                HUDLayoutSettings.InventoryTopY = slots[0].EquipButtonRect.Y - (int)(15 * GUI.Scale);
            }
        }
        
        private bool IsHandSlot(InvSlotType slotType)
        {
            return slotType == InvSlotType.LeftHand || slotType == InvSlotType.RightHand;
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

        public void ToggleInventory(bool automaticToggle = false)
        {
            if (Character.Controlled == null || !Character.Controlled.IsHuman || wasInventoryToggledAutomatically || inventoryOpen && automaticToggle) return;
            inventoryOpen = !inventoryOpen;
            if (inventoryOpen)
            {
                wasInventoryToggledAutomatically = automaticToggle;
                BackgroundFrame.Offset(0, -inventoryOpeningOffset);
                InventoryToggleArea.Offset(0, -inventoryOpeningOffset);
            }
            else
            {
                BackgroundFrame.Offset(0, inventoryOpeningOffset);
                InventoryToggleArea.Offset(0, inventoryOpeningOffset);
            }
        }

        private void HandleAutomaticInventoryState()
        {
            if (wasInventoryToggledAutomatically && inventoryOpen && Character.Controlled.SelectedConstruction == null && Character.Controlled.SelectedCharacter == null)
            {
                wasInventoryToggledAutomatically = false;
                ToggleInventory();
            }
        }

        public override void Update(float deltaTime, Camera cam, bool isSubInventory = false)
        {
            if (!AccessibleWhenAlive && !character.IsDead)
            {
                syncItemsDelay = Math.Max(syncItemsDelay - deltaTime, 0.0f);
                return;
            }

            HandleAutomaticInventoryState();

            base.Update(deltaTime, cam);

            bool hoverOnInventory = GUI.MouseOn == null &&
                ((selectedSlot != null && selectedSlot.IsSubSlot) || (draggingItem != null && (draggingSlot == null || !draggingSlot.MouseOn())));
            if (CharacterHealth.OpenHealthWindow != null) hoverOnInventory = true;
            
            if (layout == Layout.Default && hideButton.Visible)
            {
                hideButton.AddToGUIUpdateList();
                hideButton.UpdateManually(deltaTime, alsoChildren: true);

                hidePersonalSlotsState = hidePersonalSlots ? 
                    Math.Min(hidePersonalSlotsState + deltaTime * 5.0f, 1.0f) : 
                    Math.Max(hidePersonalSlotsState -  deltaTime * 5.0f, 0.0f);
                
                for (int i = 0; i < slots.Length; i++)
                {
                    if (!PersonalSlots.HasFlag(SlotTypes[i])) { continue; }
                    if (HidePersonalSlots)
                    {
                        if (selectedSlot?.Slot == slots[i]) { selectedSlot = null; }
                        highlightedSubInventorySlots.RemoveWhere(s => s.Slot == slots[i]);
                    }
                    slots[i].DrawOffset = Vector2.Lerp(Vector2.Zero, new Vector2(personalSlotArea.Width, 0.0f), hidePersonalSlotsState);
                }
            }
            
            if (hoverOnInventory) HideTimer = 0.5f;
            if (HideTimer > 0.0f) HideTimer -= deltaTime;

            for (int i = 0; i < capacity; i++)
            {
                if (Items[i] != null && Items[i] != draggingItem && Character.Controlled?.Inventory == this &&
                    GUI.KeyboardDispatcher.Subscriber == null && !CrewManager.IsCommandInterfaceOpen &&
                    slots[i].QuickUseKey != Keys.None && PlayerInput.KeyHit(slots[i].QuickUseKey))
                {
                    QuickUseItem(Items[i], true, false, true);
                }
            }

            //force personal slots open if an item is running out of battery/fuel/oxygen/etc
            if (hidePersonalSlots)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    if (Items[i]?.OwnInventory != null && Items[i].OwnInventory.Capacity == 1 && PersonalSlots.HasFlag(SlotTypes[i]))
                    {
                        if (Items[i].OwnInventory.Items[0] != null &&
                            Items[i].OwnInventory.Items[0].Condition > 0.0f &&
                            Items[i].OwnInventory.Items[0].Condition / Items[i].OwnInventory.Items[0].MaxCondition < 0.15f)
                        {
                            hidePersonalSlots = false;
                        }
                    }
                }
            }
            
            List<SlotReference> hideSubInventories = new List<SlotReference>();
            highlightedSubInventorySlots.RemoveWhere(s => 
                s.ParentInventory == this &&
                ((s.SlotIndex < 0 || s.SlotIndex >= Items.Length || Items[s.SlotIndex] == null) || (Character.Controlled != null && !Character.Controlled.CanAccessInventory(s.Inventory))));
            foreach (var highlightedSubInventorySlot in highlightedSubInventorySlots)
            {
                if (highlightedSubInventorySlot.ParentInventory == this)
                {
                    UpdateSubInventory(deltaTime, highlightedSubInventorySlot.SlotIndex, cam);
                }

                if (!highlightedSubInventorySlot.Inventory.IsInventoryHoverAvailable(character, null)) continue;

                Rectangle hoverArea = GetSubInventoryHoverArea(highlightedSubInventorySlot);
                if (highlightedSubInventorySlot.Inventory?.slots == null || (!hoverArea.Contains(PlayerInput.MousePosition)))
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
                       
            foreach (var subInventorySlot in hideSubInventories)
            {
                if (subInventorySlot.Inventory == null) continue;
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
                    var item = Items[i];
                    if (item != null)
                    {
                        if (HideSlot(i)) continue;
                        if (character.HasEquippedItem(item)) // Keep a subinventory display open permanently when the container is equipped
                        {
                            var itemContainer = item.GetComponent<ItemContainer>();
                            if (itemContainer != null && 
                                itemContainer.KeepOpenWhenEquipped && 
                                character.CanAccessInventory(itemContainer.Inventory) &&
                                !highlightedSubInventorySlots.Any(s => s.Inventory == itemContainer.Inventory))
                            {
                                ShowSubInventory(new SlotReference(this, slots[i], i, false, itemContainer.Inventory), deltaTime, cam, hideSubInventories, true);
                            }
                        }
                    }
                }
            }

            if (doubleClickedItem != null)
            {
                QuickUseItem(doubleClickedItem, true, true, true);
            }

            for (int i = 0; i < capacity; i++)
            {
                var item = Items[i];
                if (item != null)
                {
                    var slot = slots[i];
                    if (item.AllowedSlots.Any(a => a != InvSlotType.Any))
                    {
                        HandleButtonEquipStates(item, slot, deltaTime);
                    }
                }
            }

            //cancel dragging if too far away from the container of the dragged item
            if (draggingItem != null)
            {
                var rootContainer = draggingItem.GetRootContainer();
                var rootInventory = draggingItem.ParentInventory;

                if (rootContainer != null)
                {
                    rootInventory = rootContainer.ParentInventory ?? rootContainer.GetComponent<ItemContainer>().Inventory;
                }

                if (rootInventory != null &&
                    rootInventory.Owner != Character.Controlled &&
                    rootInventory.Owner != Character.Controlled.SelectedConstruction &&
                    rootInventory.Owner != Character.Controlled.SelectedCharacter)
                {
                    //allow interacting if the container is linked to the item the character is interacting with
                    if (!(rootContainer != null && 
                        rootContainer.DisplaySideBySideWhenLinked && 
                        Character.Controlled.SelectedConstruction != null &&
                        rootContainer.linkedTo.Contains(Character.Controlled.SelectedConstruction)))
                    {
                        draggingItem = null;
                    }
                }
            }

            doubleClickedItem = null;
        }

        private void HandleButtonEquipStates(Item item, InventorySlot slot, float deltaTime)
        {
            Rectangle modifiedRect = slot.EquipButtonRect;
            modifiedRect.Width = slot.InteractRect.Width;

            slot.EquipButtonState = modifiedRect.Contains(PlayerInput.MousePosition) ?
                        GUIComponent.ComponentState.Hover : GUIComponent.ComponentState.None;
            if (PlayerInput.LeftButtonHeld() && PlayerInput.RightButtonHeld())
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
                "" : TextManager.GetWithVariable("QuickUseAction." + quickUseAction.ToString(), "[equippeditem]", character.SelectedItems.FirstOrDefault(i => i != null)?.Name);
                if (PlayerInput.PrimaryMouseButtonDown()) slot.EquipButtonState = GUIComponent.ComponentState.Pressed;
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
        }
        
        private void AssignQuickUseNumKeys()
        {
            int num = 1;
            for (int i = 0; i < slots.Length; i++)
            {
                if (HideSlot(i))
                {
                    slots[i].QuickUseKey = Keys.None;
                    continue;
                }

                if (num > hotkeyCount) break;

                if (SlotTypes[i] == InvSlotType.Any)
                {
                    slots[i].QuickUseKey = Keys.D0 + num % 10;
                    num++;
                }
            }
        }

        private QuickUseAction GetQuickUseAction(Item item, bool allowEquip, bool allowInventorySwap, bool allowApplyTreatment)
        {
            if (allowApplyTreatment && CharacterHealth.OpenHealthWindow != null)
            {
                return QuickUseAction.UseTreatment;
            }
            
            if (item.ParentInventory != this)
            {
                if (item.ParentInventory == null || item.ParentInventory.Locked)
                {
                    return QuickUseAction.None;
                }
                //in another inventory -> attempt to place in the character's inventory
                else if (allowInventorySwap)
                {
                    if (item.Container == null || character.Inventory.FindIndex(item.Container) == -1) // Not a subinventory in the character's inventory
                    {
                        return item.ParentInventory is CharacterInventory ?
                            QuickUseAction.TakeFromCharacter : QuickUseAction.TakeFromContainer;
                    }
                    else
                    {
                        var selectedContainer = character.SelectedConstruction?.GetComponent<ItemContainer>();
                        if (selectedContainer != null &&
                            selectedContainer.Inventory != null &&
                            !selectedContainer.Inventory.Locked &&
                            allowInventorySwap)
                        {
                            // Move the item from the subinventory to the selected container
                            return QuickUseAction.PutToContainer;
                        }
                        else
                        {
                            // Take from the subinventory and place it in the character's main inventory if no target container is selected
                            return QuickUseAction.TakeFromContainer;
                        }
                    }
                }
            }
            else
            {
                var selectedContainer = character.SelectedConstruction?.GetComponent<ItemContainer>();
                if (selectedContainer != null && 
                    selectedContainer.Inventory != null && 
                    !selectedContainer.Inventory.Locked && 
                    allowInventorySwap)
                {
                    //player has selected the inventory of another item -> attempt to move the item there
                    return QuickUseAction.PutToContainer;
                }
                else if (character.SelectedCharacter != null && 
                    character.SelectedCharacter.Inventory != null && 
                    !character.SelectedCharacter.Inventory.Locked && 
                    allowInventorySwap)
                {
                    //player has selected the inventory of another character -> attempt to move the item there
                    return QuickUseAction.PutToCharacter;
                }
                else if (character.SelectedBy != null && Character.Controlled == character.SelectedBy &&
                    character.SelectedBy.Inventory != null && !character.SelectedBy.Inventory.Locked && allowInventorySwap)
                {
                    return QuickUseAction.TakeFromCharacter;
                }
                else if (character.SelectedItems.Any(i => i?.OwnInventory != null && i.OwnInventory.CanBePut(item)))
                {
                    return QuickUseAction.PutToEquippedItem;
                }
                else if (allowEquip) //doubleclicked and no other inventory is selected
                {
                    //not equipped -> attempt to equip
                    if (!character.HasEquippedItem(item))
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

        private void QuickUseItem(Item item, bool allowEquip, bool allowInventorySwap, bool allowApplyTreatment)
        {
            var quickUseAction = GetQuickUseAction(item, allowEquip, allowInventorySwap, allowApplyTreatment);
            bool success = false;
            switch (quickUseAction)
            {
                case QuickUseAction.Equip:
                    //attempt to put in a free slot first
                    for (int i = 0; i < capacity; i++)
                    {
                        if (Items[i] != null) continue;
                        if (SlotTypes[i] == InvSlotType.Any || !item.AllowedSlots.Any(a => a.HasFlag(SlotTypes[i]))) continue;
                        success = TryPutItem(item, i, true, false, Character.Controlled, true);
                        if (success) break;
                    }

                    if (!success)
                    {
                        for (int i = 0; i < capacity; i++)
                        {
                            if (SlotTypes[i] == InvSlotType.Any || !item.AllowedSlots.Any(a => a.HasFlag(SlotTypes[i]))) continue;
                            //something else already equipped in the slot, attempt to unequip it
                            if (Items[i] != null && Items[i].AllowedSlots.Contains(InvSlotType.Any))
                            {
                                TryPutItem(Items[i], Character.Controlled, new List<InvSlotType>() { InvSlotType.Any }, true);
                            }
                            success = TryPutItem(item, i, true, false, Character.Controlled, true);
                            if (success) break;
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
                        success = character.SelectedCharacter.Inventory.TryPutItem(item, Character.Controlled, item.AllowedSlots, true, true);
                    }
                    break;
                case QuickUseAction.PutToContainer:
                    var selectedContainer = character.SelectedConstruction?.GetComponent<ItemContainer>();
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
                        success = character.SelectedBy.Inventory.TryPutItemWithAutoEquipCheck(item, Character.Controlled, item.AllowedSlots, true, true);
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
                        success = TryPutItemWithAutoEquipCheck(item, Character.Controlled, item.AllowedSlots, true, true);
                    }
                    break;
                case QuickUseAction.PutToEquippedItem:
                    for (int i = 0; i < character.SelectedItems.Length; i++)
                    {
                        if (character.SelectedItems[i]?.OwnInventory != null && 
                            character.SelectedItems[i].OwnInventory.TryPutItem(item, Character.Controlled))
                        {
                            success = true;
                            for (int j = 0; j < capacity; j++)
                            {
                                if (Items[j] == character.SelectedItems[i]) slots[j].ShowBorderHighlight(GUI.Style.Green, 0.1f, 0.4f);
                            }
                            break;
                        }
                    }
                    break;
            }

            if (success)
            {
                for (int i = 0; i < capacity; i++)
                {
                    if (Items[i] == item) slots[i].ShowBorderHighlight(GUI.Style.Green, 0.1f, 0.4f);
                }
            }

            draggingItem = null;
            GUI.PlayUISound(success ? GUISoundType.PickItem : GUISoundType.PickItemFail);
        }
        
        public void DrawThis(SpriteBatch spriteBatch)
        {
            if (!AccessibleWhenAlive && !character.IsDead) return;
            if (slots == null) CreateSlots();

            if (hideButton != null && hideButton.Visible && !Locked)
            {
                hideButton.DrawManually(spriteBatch, alsoChildren: true);
            }

            if (layout == Layout.Default)
            {
                inventoryBackgroundSprite.Draw(spriteBatch, BackgroundFrame.Location.ToVector2(), Color.White, Vector2.Zero, rotate: 0, scale: bgScale);

                Vector2 backgroundFrameCenter = new Vector2(BackgroundFrame.Center.X, BackgroundFrame.Top);
                inventoryExtendButton.Draw(spriteBatch, backgroundFrameCenter - inventoryExtendButtonOffset, Color.White, scale: UIScale * 1.5f);

                Vector2 arrowPosition = backgroundFrameCenter - inventoryArrowOffset;
                if (inventoryOpen)
                {
                    inventoryExtendDownArrow.Draw(spriteBatch, arrowPosition, Color.White, scale: UIScale * 1.25f);
                }
                else
                {
                    inventoryExtendUpArrow.Draw(spriteBatch, arrowPosition, Color.White, scale: UIScale * 1.25f);
                }

                GUI.DrawString(spriteBatch, arrowPosition + new Vector2(UIScale * 25, -3 * UIScale), GameMain.Config.KeyBindText(InputType.ToggleInventory), Color.White, font: GUI.HotkeyFont);

                InventoryToggleContains = InventoryToggleArea.Contains(PlayerInput.MousePosition);
                if (InventoryToggleContains && PlayerInput.PrimaryMouseButtonClicked())
                {
                    ToggleInventory();
                }
            }
            
            InventorySlot highlightedQuickUseSlot = null;
            for (int i = 0; i < capacity; i++)
            {
                if (HideSlot(i)) continue;
                if (IsSlotHiddenDueToToggleState(i)) continue;

                InventorySlot slot = slots[i];

                Item item = Items[i];
                InvSlotType slotType = SlotTypes[i];

                Rectangle interactRect = slot.InteractRect;
                interactRect.Location += slot.DrawOffset.ToPoint();

                //don't draw the item if it's being dragged out of the slot
                bool drawItem = draggingItem == null || draggingItem != item || interactRect.Contains(PlayerInput.MousePosition);
                DrawSlot(spriteBatch, this, slot, item, i, drawItem, slotType);

                if (item == null || (draggingItem == item && !slot.InteractRect.Contains(PlayerInput.MousePosition)) || !item.AllowedSlots.Any(a => a != InvSlotType.Any))
                {
                    //draw limb icons on empty slots
                    if (limbSlotIcons.ContainsKey(slotType))
                    {
                        var icon = limbSlotIcons[slotType];
                        icon.Draw(spriteBatch, slot.Rect.Center.ToVector2() + slot.DrawOffset, GUIColorSettings.EquipmentSlotIconColor, origin: icon.size / 2, scale: slot.Rect.Width / icon.size.X);
                    }

                    // Draw always on hotkeys
                    if (slot.QuickUseKey != Keys.None)
                    {
                        DrawIndicatorAndHotkey(spriteBatch, slot, slot.EquipButtonState == GUIComponent.ComponentState.Hover);
                    }

                    continue;
                }

                if (draggingItem == item && !slot.IsHighlighted) continue;

                bool hover = slot.EquipButtonState == GUIComponent.ComponentState.Hover;
                if (hover) highlightedQuickUseSlot = slot;

                if (!item.AllowedSlots.Any(a => a == InvSlotType.Any)) continue;

                DrawIndicatorAndHotkey(spriteBatch, slot, hover);

            }

            if (highlightedQuickUseSlot != null && !string.IsNullOrEmpty(highlightedQuickUseSlot.QuickUseButtonToolTip))
            {
                GUIComponent.DrawToolTip(spriteBatch, highlightedQuickUseSlot.QuickUseButtonToolTip, highlightedQuickUseSlot.EquipButtonRect);
            }
        }

        private void DrawIndicatorAndHotkey(SpriteBatch spriteBatch, InventorySlot slot, bool hover)
        {

            Color indicatorColor = (hover) ? Color.White : Color.White * 0.8f;

            /*if (character.HasEquippedItem(item))
            {
                indicatorColor = hover ? GUIColorSettings.InventorySlotEquippedColor : GUIColorSettings.InventorySlotEquippedColor * 0.8f;
            }
            else
            {
                indicatorColor = hover ? GUIColorSettings.InventorySlotColor : GUIColorSettings.InventorySlotColor * 0.8f;
            }*/

            if (Locked) indicatorColor *= 0.3f;

            Rectangle equipRect = slot.EquipButtonRect;
            EquipIndicator.Draw(spriteBatch, new Vector2(equipRect.Center.X, equipRect.Bottom), indicatorColor, EquipIndicator.Origin, 0, UIScale * 0.6f);

            if (slot.QuickUseKey != Keys.None)
            {
                GUI.DrawString(spriteBatch, new Vector2(slot.Rect.Center.X - 2, slot.Rect.Top), slot.QuickUseKey.ToString().Substring(1, 1), Color.Black, font: GUI.HotkeyFont);
            }
        }
    }
}
