using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    partial class CharacterInventory : Inventory
    {
        const float HiddenPos = 130.0f;

        private static Texture2D icons;
        private static Sprite toggleArrow;

        public Vector2[] SlotPositions;

        private GUIButton[] useOnSelfButton;

        private bool hidden;

        partial void InitProjSpecific()
        {
            useOnSelfButton = new GUIButton[2];

            if (icons == null) icons = TextureLoader.FromFile("Content/UI/inventoryIcons.png");

            if (toggleArrow == null)
            {
                toggleArrow = new Sprite("Content/UI/inventoryAtlas.png", new Rectangle(585,973,67,23), null);
                toggleArrow.Origin = toggleArrow.size / 2;
            }

            hidden = true;

            SlotPositions = new Vector2[SlotTypes.Length];

            int x = GameMain.GraphicsWidth / 2;
            int spacing = 10;

            for (int i = 0; i < SlotPositions.Length; i++)
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
                        SlotPositions[i] = new Vector2(100, GameMain.GraphicsHeight - 160 * UIScale);
                        hideEmptySlot[i] = true;
                        break;
                    case InvSlotType.LeftHand:
                        SlotPositions[i] = new Vector2(180, GameMain.GraphicsHeight - 160 * UIScale);
                        hideEmptySlot[i] = true;
                        break;
                    case InvSlotType.RightHand:
                        SlotPositions[i] = new Vector2(260, GameMain.GraphicsHeight - 160 * UIScale);
                        hideEmptySlot[i] = true;
                        break;
                    case InvSlotType.Any:
                        SlotPositions[i] = new Vector2(x, GameMain.GraphicsHeight - 160 * UIScale);
                        x += (int)((slotSpriteVertical.size.X + spacing) * UIScale);
                        break;
                }
            }
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
                    (int)(SlotPositions[i].X + DrawOffset.X), 
                    (int)(SlotPositions[i].Y + DrawOffset.Y),
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

        public override void Update(float deltaTime, bool isSubInventory = false)
        {
            base.Update(deltaTime);

            Rectangle arrowRect = new Rectangle(
                (int)(slots[7].Rect.Center.X + slots[7].DrawOffset.X - toggleArrow.size.X / 2),
                (int)(slots[7].Rect.Y + slots[7].DrawOffset.Y - 50 - toggleArrow.size.Y / 2), 
                (int)toggleArrow.size.X, (int)toggleArrow.size.Y);
            arrowRect.Inflate(30, 0);

            if (arrowRect.Contains(PlayerInput.MousePosition))
            {
                if (PlayerInput.LeftButtonClicked())
                {
                    hidden = !hidden;
                }
            }

            bool hoverOnInventory = false;
            if ((slots[7].DrawOffset.Y < 10.0f && PlayerInput.MousePosition.Y > arrowRect.Bottom ||
                slots[7].DrawOffset.Y > 10.0f && PlayerInput.MousePosition.Y > slots[7].EquipButtonRect.Bottom) 
                && 
                slots.Any(s => PlayerInput.MousePosition.X > s.InteractRect.X - 10 && PlayerInput.MousePosition.X < s.InteractRect.Right + 10))
            {
                hoverOnInventory = true;
            }

            for (int i = 0; i < capacity; i++)
            {
                if (SlotTypes[i] == InvSlotType.Any || SlotTypes[i] == InvSlotType.OuterClothes ||
                    SlotTypes[i] == InvSlotType.LeftHand || SlotTypes[i] == InvSlotType.RightHand)
                {
                    if (hidden && !hoverOnInventory)
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
                DoubleClickItem(doubleClickedItem);
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
                hoverArea.Inflate(10, 10);

                if (highlightedSubInventory.slots == null || 
                    (!hoverArea.Contains(PlayerInput.MousePosition) && !highlightedSubInventory.slots.Any(s => s.MouseOn())))
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

                    if (Items[i] != null && Items[i].AllowedSlots.Any(a => a != InvSlotType.Any))
                    {
                        slots[i].EquipButtonState = slots[i].EquipButtonRect.Contains(PlayerInput.MousePosition) ? 
                            GUIComponent.ComponentState.Hover : GUIComponent.ComponentState.None;

                        if (slots[i].EquipButtonState == GUIComponent.ComponentState.Hover)
                        {
                            if (PlayerInput.LeftButtonDown()) slots[i].EquipButtonState = GUIComponent.ComponentState.Pressed;
                            if (PlayerInput.LeftButtonClicked())
                            {
                                DoubleClickItem(Items[i]);
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

        private void DoubleClickItem(Item item)
        {
            bool wasPut = false;
            if (item.ParentInventory != this)
            {
                wasPut = TryPutItem(item, Character.Controlled, item.AllowedSlots, true);
            }
            else
            {
                var selectedContainer = character.SelectedConstruction?.GetComponent<ItemContainer>();
                if (selectedContainer != null && selectedContainer.Inventory != null)
                {
                    wasPut = selectedContainer.Inventory.TryPutItem(item, Character.Controlled, item.AllowedSlots, true);
                }
                else if (character.SelectedCharacter != null && character.SelectedCharacter.Inventory != null)
                {
                    wasPut = character.SelectedCharacter.Inventory.TryPutItem(item, Character.Controlled, item.AllowedSlots, true);
                }
                else if (character.SelectedBy != null && Character.Controlled == character.SelectedBy && character.SelectedBy.Inventory != null)
                {
                    wasPut = character.SelectedBy.Inventory.TryPutItem(item, Character.Controlled, item.AllowedSlots, true);
                }
                else //doubleclicked and no other inventory is selected
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

        /*private void MergeSlots()
        {
            for (int i = 0; i < capacity - 1; i++)
            {
                slots[i].State = GUIComponent.ComponentState.None;
                if (slots[i].Disabled || Items[i] == null) continue;

                for (int n = i + 1; n < capacity; n++)
                {
                    if (Items[n] == Items[i])
                    {
                        slots[i].Rect = Rectangle.Union(slots[i].Rect, slots[n].Rect);
                        slots[i].InteractRect = Rectangle.Union(slots[i].InteractRect, slots[n].InteractRect);
                        slots[n].Disabled = true;
                    }
                }
            }

            highlightedSubInventory = null;
            highlightedSubInventorySlot = null;
            selectedSlot = null;
        }*/

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
                if (slots[i].IsHighlighted)
                {
                    DrawSubInventory(spriteBatch, i);
                }
                if (Items[i] != null && Items[i].AllowedSlots.Any(a => a != InvSlotType.Any))
                {
                    Color color = slots[i].EquipButtonState == GUIComponent.ComponentState.Hover ? Color.White : Color.White * 0.8f;
                    if (slots[i].EquipButtonState == GUIComponent.ComponentState.Pressed) color = Color.Gray;
                    
                    equipIndicator.Draw(spriteBatch, slots[i].EquipButtonRect.Center.ToVector2(), color, equipIndicator.size / 2, 0, UIScale);
                    if (character.HasEquippedItem(Items[i]))
                    {
                        equipIndicatorOn.Draw(spriteBatch, slots[i].EquipButtonRect.Center.ToVector2(), color * 0.9f, equipIndicatorOn.size / 2, 0, UIScale * 0.85f);
                    }
                }
            }

            toggleArrow.Draw(spriteBatch, slots[7].DrawOffset + new Vector2(slots[7].Rect.Center.X, slots[7].Rect.Y - 50), hidden ? 0 : MathHelper.Pi);
        }
    }
}
