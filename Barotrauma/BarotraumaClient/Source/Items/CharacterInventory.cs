using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class CharacterInventory : Inventory
    {
        const float HiddenPos = 130.0f;
        
        private static Sprite toggleArrow;
        private float arrowAlpha;

        private float hideTimer;

        public Vector2[] SlotPositions;

        private GUIButton[] useOnSelfButton;

        private Alignment alignment;
        public Alignment Alignment
        {
            get { return alignment; }
            set
            {
                if (alignment == value) return;
                alignment = value;
                SetSlotPositions(alignment);
            }
        }

        private bool hidden;
        public bool Hidden
        {
            get { return hidden; }
            set { hidden = value; }
        }

        partial void InitProjSpecific()
        {
            useOnSelfButton = new GUIButton[2];
            
            if (toggleArrow == null)
            {
                toggleArrow = new Sprite("Content/UI/inventoryAtlas.png", new Rectangle(585, 973, 67, 23), null);
                toggleArrow.Origin = toggleArrow.size / 2;
            }

            hidden = true;

            SlotPositions = new Vector2[SlotTypes.Length];
            Alignment = Alignment.Center;
        }

        private bool UseItemOnSelf(GUIButton button, object obj)
        {
            if (!(obj is int)) return false;

            int slotIndex = (int)obj;

            return UseItemOnSelf(slotIndex);
        }


        protected override void PutItem(Item item, int i, Character user, bool removeItem = true, bool createNetworkEvent = true)
        {
            base.PutItem(item, i, user, removeItem, createNetworkEvent);
            CreateSlots();
        }

        public override void RemoveItem(Item item)
        {
            base.RemoveItem(item);
            CreateSlots();
        }

        protected override void CreateSlots()
        {
            if (slots == null) slots = new InventorySlot[capacity];
            
            for (int i = 0; i < capacity; i++)
            {
                InventorySlot prevSlot = slots[i];

                Sprite slotSprite = slotSpriteSmall;
                switch (SlotTypes[i])
                {
                    case InvSlotType.InnerClothes:
                        slotSprite = slotSpriteHorizontal;
                        break;
                    case InvSlotType.OuterClothes:
                    case InvSlotType.LeftHand:
                    case InvSlotType.RightHand:
                    case InvSlotType.Any:
                        slotSprite = slotSpriteVertical;
                        break;
                }

                Rectangle slotRect = new Rectangle(
                    (int)(SlotPositions[i].X), 
                    (int)(SlotPositions[i].Y),
                    (int)(slotSprite.size.X * UIScale), (int)(slotSprite.size.Y * UIScale));

                slots[i] = new InventorySlot(slotRect);
                slots[i].Disabled = false;
                slots[i].SlotSprite = slotSprite;
                slots[i].Color = SlotTypes[i] == InvSlotType.Any ? Color.White * 0.2f : Color.White * 0.4f;
                if (prevSlot != null)
                {
                    slots[i].DrawOffset = prevSlot.DrawOffset;
                    slots[i].Color = prevSlot.Color;
                }
            }
        }

        protected override bool HideSlot(int i)
        {
            if (slots[i].Disabled || (hideEmptySlot[i] && Items[i] == null)) return true;

            //no need to draw the right hand slot if the item is in both hands
            if (Items[i] != null && SlotTypes[i] == InvSlotType.RightHand && IsInLimbSlot(Items[i], InvSlotType.LeftHand))
            {
                return true;
            }

            //don't show the equip slot if the item is also in the default inventory
            if (SlotTypes[i] != InvSlotType.Any && Items[i] != null)
            {
                for (int j = 0; j < capacity; j++)
                {
                    if (SlotTypes[j] == InvSlotType.Any && Items[j] == Items[i]) return true;
                }
            }

            return false;
        }

        private void SetSlotPositions(Alignment alignment)
        {
            int spacing = 10;
            int x = (alignment == Alignment.Center) ? GameMain.GraphicsWidth / 2 : 40;
            if (alignment == Alignment.Right) x = GameMain.GraphicsWidth - 40 - slots[0].Rect.Width; 

            for (int i = 0; i < SlotPositions.Length; i++)
            {
                if (alignment == Alignment.Center)
                {
                    switch (SlotTypes[i])
                    {
                        case InvSlotType.Headset:
                            SlotPositions[i] = new Vector2(GameMain.GraphicsWidth - (slotSpriteSmall.size.X + spacing) * UIScale, 50);
                            break;
                        case InvSlotType.Card:
                            SlotPositions[i] = new Vector2(GameMain.GraphicsWidth - (slotSpriteSmall.size.X + spacing) * 2 * UIScale, 50);
                            break;
                        case InvSlotType.InnerClothes:
                            SlotPositions[i] = new Vector2(GameMain.GraphicsWidth - ((slotSpriteSmall.size.X + spacing) * 2 + slotSpriteHorizontal.size.X + spacing) * UIScale, 50);
                            break;
                        case InvSlotType.Head:
                            SlotPositions[i] = new Vector2(GameMain.GraphicsWidth - (slotSpriteSmall.size.X * 3 + spacing * 4 + slotSpriteHorizontal.size.X) * UIScale, 50);
                            hideEmptySlot[i] = true;
                            break;
                        case InvSlotType.OuterClothes:
                            SlotPositions[i] = new Vector2(100 * UIScale, GameMain.GraphicsHeight - 160 * UIScale);
                            hideEmptySlot[i] = true;
                            break;
                        case InvSlotType.LeftHand:
                            SlotPositions[i] = new Vector2(180 * UIScale, GameMain.GraphicsHeight - 160 * UIScale);
                            hideEmptySlot[i] = true;
                            break;
                        case InvSlotType.RightHand:
                            SlotPositions[i] = new Vector2(260 * UIScale, GameMain.GraphicsHeight - 160 * UIScale);
                            hideEmptySlot[i] = true;
                            break;
                        case InvSlotType.Any:
                            SlotPositions[i] = new Vector2(x, GameMain.GraphicsHeight - 160 * UIScale);
                            x += (int)((slotSpriteVertical.size.X + spacing) * UIScale);
                            break;
                    }
                }
                else if (alignment == Alignment.Left)
                {
                    if (HideSlot(i)) continue;
                    SlotPositions[i] = new Vector2(x, GameMain.GraphicsHeight - 160 * UIScale);
                    x += (int)((slots[i].SlotSprite.size.X + spacing) * UIScale);
                }
                else if (alignment == Alignment.Right)
                {
                    if (HideSlot(i)) continue;
                    SlotPositions[i] = new Vector2(x, GameMain.GraphicsHeight - 160 * UIScale);
                    if (i < slots.Length - 1)
                    {
                        x -= (int)((slots[i + 1].SlotSprite.size.X + spacing) * UIScale);
                    }
                }
            }

            CreateSlots();
        }

        public override void Update(float deltaTime, bool isSubInventory = false)
        {
            base.Update(deltaTime);

            bool hoverOnInventory = GUIComponent.MouseOn == null &&
                (highlightedSubInventorySlot != null || (draggingItem != null && (draggingSlot == null || !draggingSlot.MouseOn())));

            if (alignment == Alignment.Center)
            {
                Rectangle arrowRect = new Rectangle(
                    (int)(slots[7].Rect.Center.X + slots[7].DrawOffset.X - toggleArrow.size.X / 2),
                    (int)(slots[7].Rect.Y + slots[7].DrawOffset.Y - 50 - toggleArrow.size.Y / 2), 
                    (int)toggleArrow.size.X, (int)toggleArrow.size.Y);
                arrowRect.Inflate(30, 0);

                if (arrowRect.Contains(PlayerInput.MousePosition))
                {
                    arrowAlpha = Math.Min(arrowAlpha + deltaTime * 10.0f, 1.0f);
                    if (PlayerInput.LeftButtonClicked())
                    {
                        hidden = !hidden;
                        hideTimer = 0.0f;
                    }
                }
                else
                {
                    arrowAlpha = Math.Max(arrowAlpha - deltaTime * 10.0f, 0.5f);
                }

                if (GUIComponent.MouseOn == null &&
                    (slots[7].DrawOffset.Y < 10.0f && PlayerInput.MousePosition.Y > arrowRect.Bottom ||
                    slots[7].DrawOffset.Y > 10.0f && PlayerInput.MousePosition.Y > slots[7].EquipButtonRect.Bottom) &&
                    slots.Any(s => PlayerInput.MousePosition.X > s.InteractRect.X - 10 && PlayerInput.MousePosition.X < s.InteractRect.Right + 10))
                {
                    hoverOnInventory = true;
                }
            }

            if (hoverOnInventory) hideTimer = 0.5f;
            if (hideTimer > 0.0f) hideTimer -= deltaTime;

            for (int i = 0; i < capacity; i++)
            {
                if (SlotTypes[i] == InvSlotType.Any || SlotTypes[i] == InvSlotType.OuterClothes ||
                    SlotTypes[i] == InvSlotType.LeftHand || SlotTypes[i] == InvSlotType.RightHand)
                {
                    if (hidden && !hoverOnInventory && alignment == Alignment.Center && hideTimer <= 0.0f)
                    {
                        slots[i].DrawOffset.Y = MathHelper.Lerp(slots[i].DrawOffset.Y, HiddenPos * UIScale, 10.0f * deltaTime);                        
                    }
                    else
                    {
                        slots[i].DrawOffset.Y = MathHelper.Lerp(slots[i].DrawOffset.Y, 0, 10.0f * deltaTime);
                    }
                }
            }


            if (doubleClickedItem != null)
            {
                DoubleClickItem(doubleClickedItem, true, true);
            }

            if (highlightedSubInventorySlot != null)
            {
                if (highlightedSubInventorySlot.Inventory == this)
                {
                    UpdateSubInventory(deltaTime, highlightedSubInventorySlot.SlotIndex);
                }

                Rectangle hoverArea = highlightedSubInventorySlot.Slot.Rect;
                hoverArea.Location += highlightedSubInventorySlot.Slot.DrawOffset.ToPoint();
                hoverArea = Rectangle.Union(hoverArea, highlightedSubInventorySlot.Slot.EquipButtonRect);
                if (highlightedSubInventory.slots != null)
                {
                    foreach (InventorySlot slot in highlightedSubInventory.slots)
                    {
                        Rectangle subSlotRect = slot.InteractRect;
                        subSlotRect.Location += slot.DrawOffset.ToPoint();
                        hoverArea = Rectangle.Union(hoverArea, subSlotRect);
                    }
                }
                hoverArea.Inflate(10, 10);

                if (highlightedSubInventory.slots == null || !hoverArea.Contains(PlayerInput.MousePosition))
                {
                    highlightedSubInventory = null;
                    highlightedSubInventorySlot = null;
                }
            }
            else
            {
                if (selectedSlot?.Inventory == this)
                {
                    var subInventory = GetSubInventory(selectedSlot.SlotIndex);
                    if (subInventory != null)
                    {
                        highlightedSubInventory = subInventory;
                        highlightedSubInventorySlot = selectedSlot;
                        UpdateSubInventory(deltaTime, highlightedSubInventorySlot.SlotIndex);
                    }
                }
            }

            if (character == Character.Controlled)
            {
                for (int i = 0; i < capacity; i++)
                {
                    if ((selectedSlot == null || selectedSlot.SlotIndex != i) &&
                        Items[i] != null && Items[i].CanUseOnSelf && character.HasSelectedItem(Items[i]))
                    {
                        //-3 because selected items are in slots 3 and 4 (hands)
                        useOnSelfButton[i - 3].Update(deltaTime);
                    }

                    if (Items[i] != null && Owner == Character.Controlled && Items[i].AllowedSlots.Any(a => a != InvSlotType.Any))
                    {
                        slots[i].EquipButtonState = slots[i].EquipButtonRect.Contains(PlayerInput.MousePosition) ? 
                            GUIComponent.ComponentState.Hover : GUIComponent.ComponentState.None;

                        if (slots[i].EquipButtonState == GUIComponent.ComponentState.Hover)
                        {
                            if (PlayerInput.LeftButtonDown()) slots[i].EquipButtonState = GUIComponent.ComponentState.Pressed;
                            if (PlayerInput.LeftButtonClicked())
                            {
                                DoubleClickItem(Items[i], true, false);
                            }
                        }
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
                    rootInventory = rootContainer.ParentInventory != null ?
                        rootContainer.ParentInventory : rootContainer.GetComponent<ItemContainer>().Inventory;
                }

                if (rootInventory != null &&
                    rootInventory.Owner != Character.Controlled &&
                    rootInventory.Owner != Character.Controlled.SelectedConstruction &&
                    rootInventory.Owner != Character.Controlled.SelectedCharacter)
                {
                    draggingItem = null;
                }
            }

            doubleClickedItem = null;
        }

        private void DoubleClickItem(Item item, bool allowEquip, bool allowInventorySwap)
        {
            bool wasPut = false;
            if (item.ParentInventory != this)
            {
                //in another inventory -> attempt to place in the character's inventory
                if (allowInventorySwap) wasPut = TryPutItem(item, Character.Controlled, item.AllowedSlots, true);
            }
            else
            {
                var selectedContainer = character.SelectedConstruction?.GetComponent<ItemContainer>();
                if (selectedContainer != null && selectedContainer.Inventory != null && allowInventorySwap)
                {
                    //player has selected the inventory of another item -> attempt to move the item there
                    wasPut = selectedContainer.Inventory.TryPutItem(item, Character.Controlled, item.AllowedSlots, true);
                }
                else if (character.SelectedCharacter != null && character.SelectedCharacter.Inventory != null && allowInventorySwap)
                {
                    //player has selected the inventory of another character -> attempt to move the item there
                    wasPut = character.SelectedCharacter.Inventory.TryPutItem(item, Character.Controlled, item.AllowedSlots, true);
                }
                else if (character.SelectedBy != null && Character.Controlled == character.SelectedBy && 
                    character.SelectedBy.Inventory != null && allowInventorySwap)
                {
                    //item is in the inventory of another character -> attempt to get the item from there
                     wasPut = character.SelectedBy.Inventory.TryPutItem(item, Character.Controlled, item.AllowedSlots, true);
                }
                else if (allowEquip) //doubleclicked and no other inventory is selected
                {
                    //not equipped -> attempt to equip
                    if (!character.HasEquippedItem(item))
                    {
                        for (int i = 0; i < capacity; i++)
                        {
                            if (SlotTypes[i] == InvSlotType.Any || !item.AllowedSlots.Any(a => a.HasFlag(SlotTypes[i]))) continue;

                            //something else already equipped in the slot, attempt to unequip it
                            if (Items[i] != null && Items[i].AllowedSlots.Contains(InvSlotType.Any))
                            {
                                TryPutItem(Items[i], Character.Controlled, new List<InvSlotType>() { InvSlotType.Any }, true);
                            }

                            wasPut = TryPutItem(item, i, true, false, Character.Controlled, true);
                            if (wasPut) break;
                        }
                    }
                    //equipped -> attempt to unequip
                    else if (item.AllowedSlots.Contains(InvSlotType.Any))
                    {
                        wasPut = TryPutItem(item, Character.Controlled, new List<InvSlotType>() { InvSlotType.Any }, true);
                    }
                    else
                    {
                        //cannot unequip, drop?
                        //maybe make only some items droppable so you don't accidentally drop diving suits or artifacts?
                    }
                }
            }

            if (wasPut)
            {
                for (int i = 0; i < capacity; i++)
                {
                    if (Items[i] == item) slots[i].ShowBorderHighlight(Color.Green, 0.1f, 0.9f);
                }
            }

            draggingItem = null;
            GUI.PlayUISound(wasPut ? GUISoundType.PickItem : GUISoundType.PickItemFail);
        }
        
        public void DrawOwn(SpriteBatch spriteBatch)
        {
            if (slots == null) CreateSlots();
            
            base.Draw(spriteBatch);

            if (character == Character.Controlled)
            {
                for (int i = 0; i < capacity; i++)
                {
                    if ((selectedSlot == null || selectedSlot.SlotIndex != i) &&
                        Items[i] != null && Items[i].CanUseOnSelf && character.HasSelectedItem(Items[i]))
                    {
                        useOnSelfButton[i - 3].Draw(spriteBatch);
                    }
                }
            }

            for (int i = 0; i < capacity; i++)
            {
                if (HideSlot(i)) continue;
                if (Items[i] != null && Owner == Character.Controlled && Items[i].AllowedSlots.Any(a => a != InvSlotType.Any))
                {
                    Color color = slots[i].EquipButtonState == GUIComponent.ComponentState.Hover ? Color.White : Color.White * 0.8f;
                    if (slots[i].EquipButtonState == GUIComponent.ComponentState.Pressed) color = Color.Gray;
                    
                    EquipIndicator.Draw(spriteBatch, slots[i].EquipButtonRect.Center.ToVector2(), color, EquipIndicator.size / 2, 0, UIScale);
                    if (character.HasEquippedItem(Items[i]))
                    {
                        EquipIndicatorOn.Draw(spriteBatch, slots[i].EquipButtonRect.Center.ToVector2(), color * 0.9f, EquipIndicatorOn.size / 2, 0, UIScale * 0.85f);
                    }
                }
            }

            if (Alignment == Alignment.Center)
            {
                toggleArrow.Draw(spriteBatch, 
                    slots[7].DrawOffset + new Vector2(slots[7].Rect.Center.X, slots[7].Rect.Y - 50), 
                    Color.White * arrowAlpha, hidden ? 0 : MathHelper.Pi);
            }
        
        }
    }
}
