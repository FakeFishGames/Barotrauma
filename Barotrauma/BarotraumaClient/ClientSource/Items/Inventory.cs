using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    class VisualSlot
    {
        public Rectangle Rect;

        public Rectangle InteractRect;

        public bool Disabled;

        public GUIComponent.ComponentState State;

        public Vector2 DrawOffset;

        public Color Color;

        public Color HighlightColor;
        public float HighlightScaleUpAmount;
        private CoroutineHandle highlightCoroutine;
        public float HighlightTimer;

        public Sprite SlotSprite;

        public int InventoryKeyIndex = -1;

        public int SubInventoryDir = -1;

        public bool IsHighlighted
        {
            get
            {
                return State == GUIComponent.ComponentState.Hover;
            }
        }

        public float QuickUseTimer;
        public string QuickUseButtonToolTip;
        public bool IsMoving = false;

        private static Rectangle offScreenRect = new Rectangle(new Point(-1000, 0), Point.Zero);
        public GUIComponent.ComponentState EquipButtonState;
        public Rectangle EquipButtonRect
        {
            get
            {
                // Returns a point off-screen, Rectangle.Empty places buttons in the top left of the screen
                if (IsMoving) return offScreenRect;

                int buttonDir = Math.Sign(SubInventoryDir);

                float sizeY = Inventory.UnequippedIndicator.size.Y * Inventory.UIScale * Inventory.IndicatorScaleAdjustment;

                Vector2 equipIndicatorPos = new Vector2(Rect.Left, Rect.Center.Y + (Rect.Height / 2 + 15 * Inventory.UIScale) * buttonDir - sizeY / 2f);
                equipIndicatorPos += DrawOffset;

                return new Rectangle((int)equipIndicatorPos.X, (int)equipIndicatorPos.Y, (int)Rect.Width, (int)sizeY);
            }
        }

        public VisualSlot(Rectangle rect)
        {
            Rect = rect;
            InteractRect = rect;
            InteractRect.Inflate(5, 5);
            State = GUIComponent.ComponentState.None;
            Color = Color.White * 0.4f;
        }

        public bool MouseOn()
        {
            Rectangle rect = InteractRect;
            rect.Location += DrawOffset.ToPoint();
            return rect.Contains(PlayerInput.MousePosition);
        }

        public void ShowBorderHighlight(Color color, float fadeInDuration, float fadeOutDuration, float scaleUpAmount = 0.5f)
        {
            if (highlightCoroutine != null)
            {
                CoroutineManager.StopCoroutines(highlightCoroutine);
                highlightCoroutine = null;
            }

            HighlightScaleUpAmount = scaleUpAmount;
            currentHighlightState = 0.0f;
            this.fadeInDuration = fadeInDuration;
            this.fadeOutDuration = fadeOutDuration;
            currentHighlightColor = color;
            HighlightTimer = 1.0f;
            highlightCoroutine = CoroutineManager.StartCoroutine(UpdateBorderHighlight());
        }

        private float currentHighlightState, fadeInDuration, fadeOutDuration;
        private Color currentHighlightColor;
        private IEnumerable<CoroutineStatus> UpdateBorderHighlight()
        {
            HighlightTimer = 1.0f;
            while (currentHighlightState < fadeInDuration + fadeOutDuration)
            {
                HighlightColor = (currentHighlightState < fadeInDuration) ?
                    Color.Lerp(Color.Transparent, currentHighlightColor, currentHighlightState / fadeInDuration) :
                    Color.Lerp(currentHighlightColor, Color.Transparent, (currentHighlightState - fadeInDuration) / fadeOutDuration);

                currentHighlightState += CoroutineManager.DeltaTime;
                HighlightTimer = 1.0f - currentHighlightState / (fadeInDuration + fadeOutDuration);

                yield return CoroutineStatus.Running;
            }

            HighlightTimer = 0.0f;
            HighlightColor = Color.Transparent;

            yield return CoroutineStatus.Success;
        }

        /// <summary>
        /// Moves the current border highlight animation (if one is running) to the new slot
        /// </summary>
        public void MoveBorderHighlight(VisualSlot newSlot)
        {
            if (highlightCoroutine == null) { return; }
            CoroutineManager.StopCoroutines(highlightCoroutine);
            highlightCoroutine = null;

            newSlot.HighlightScaleUpAmount = HighlightScaleUpAmount;
            newSlot.currentHighlightState = currentHighlightState;
            newSlot.fadeInDuration = fadeInDuration;
            newSlot.fadeOutDuration = fadeOutDuration;
            newSlot.currentHighlightColor = currentHighlightColor;
            newSlot.highlightCoroutine = CoroutineManager.StartCoroutine(newSlot.UpdateBorderHighlight());
        }
    }

    partial class Inventory
    {
        public static float UIScale
        {
            get { return (GameMain.GraphicsWidth / 1920.0f + GameMain.GraphicsHeight / 1080.0f) / 2.5f * GameSettings.InventoryScale; }
        }

        public static int ContainedIndicatorHeight
        {
            get { return (int)(15 * UIScale); }
        }

        protected float prevUIScale = UIScale;
        protected float prevHUDScale = GUI.Scale;
        protected Point prevScreenResolution;

        protected static Sprite slotHotkeySprite;

        private static Sprite slotSpriteSmall;
        public static Sprite SlotSpriteSmall
        {
            get
            {
                if (slotSpriteSmall == null)
                {
                    //TODO: define this in xml
                    slotSpriteSmall = new Sprite("Content/UI/InventoryUIAtlas.png", new Rectangle(10, 6, 119, 120), null, 0);
                    // Adjustment to match the old size of 75,71
                    SlotSpriteSmall.size = new Vector2(SlotSpriteSmall.SourceRect.Width * 0.575f, SlotSpriteSmall.SourceRect.Height * 0.575f);
                }
                return slotSpriteSmall;
            }
        }
        public static Sprite DraggableIndicator;
        public static Sprite UnequippedIndicator, UnequippedHoverIndicator, UnequippedClickedIndicator, EquippedIndicator, EquippedHoverIndicator, EquippedClickedIndicator;

        public static float IndicatorScaleAdjustment
        {
            get
            {
                return !GUI.IsFourByThree() ? 0.8f : 0.7f;
            }
        }
       
        public static Inventory DraggingInventory;

        public Inventory ReplacedBy;

        public Rectangle BackgroundFrame { get; protected set; }

        private List<ushort>[] receivedItemIDs;
        private CoroutineHandle syncItemsCoroutine;

        public float HideTimer;

        private bool isSubInventory;

        private const float movableFrameRectHeight = 40f;
        private Color movableFrameRectColor = new Color(60, 60, 60);
        private Rectangle movableFrameRect;
        private Point savedPosition, originalPos;
        private bool canMove = false;
        private bool positionUpdateQueued = false;
        private Vector2 draggableIndicatorOffset;
        private float draggableIndicatorScale;

        public class SlotReference
        {
            public readonly Inventory ParentInventory;
            public readonly int SlotIndex;
            public VisualSlot Slot;

            public Inventory Inventory;
            public readonly Item Item;
            public readonly bool IsSubSlot;
            public string Tooltip { get; private set; }
            public List<RichTextData> TooltipRichTextData { get; private set;}

            public int tooltipDisplayedCondition;

            public bool ForceTooltipRefresh;

            public SlotReference(Inventory parentInventory, VisualSlot slot, int slotIndex, bool isSubSlot, Inventory subInventory = null)
            {
                ParentInventory = parentInventory;
                Slot = slot;
                SlotIndex = slotIndex;
                Inventory = subInventory;
                IsSubSlot = isSubSlot;
                Item = ParentInventory.GetItemAt(slotIndex);

                RefreshTooltip();
            }

            public bool TooltipNeedsRefresh()
            {
                if (ForceTooltipRefresh) { return true; }
                if (Item == null) { return false; }
                return (int)Item.ConditionPercentage != tooltipDisplayedCondition;
            }

            public void RefreshTooltip()
            {
                ForceTooltipRefresh = false;
                if (Item == null) { return; }
                IEnumerable<Item> itemsInSlot = null;
                if (ParentInventory != null && Item != null)
                {
                    itemsInSlot = ParentInventory.GetItemsAt(SlotIndex);
                }
                TooltipRichTextData = RichTextData.GetRichTextData(GetTooltip(Item, itemsInSlot), out string newTooltip);
                Tooltip = newTooltip;
                tooltipDisplayedCondition = (int)Item.ConditionPercentage;
            }

            private string GetTooltip(Item item, IEnumerable<Item> itemsInSlot)
            {
                if (item == null) { return null; }

                string toolTip = "";
                if (GameMain.DebugDraw)
                {
                    toolTip = item.ToString();
                }
                else
                {
                    string description = item.Description;
                    if (item.Prefab.Identifier == "idcard" || item.Tags.Contains("despawncontainer"))
                    {
                        string[] readTags = item.Tags.Split(',');
                        string idName = null;
                        string idJob = null;
                        foreach (string tag in readTags)
                        {
                            string[] s = tag.Split(':');
                            if (s[0] == "name")
                                idName = s[1];
                            if (s[0] == "job")
                                idJob = s[1];
                        }
                        if (idName != null)
                        {
                            if (idJob == null)
                            {
                                description = TextManager.GetWithVariable("IDCardName", "[name]", idName);
                            }
                            else
                            {
                                description = TextManager.GetWithVariables("IDCardNameJob", new string[2] { "[name]", "[job]" }, new string[2] { idName, idJob }, new bool[2] { false, true });
                            }
                            if (!string.IsNullOrEmpty(item.Description))
                            {
                                description = description + " " + item.Description;
                            }
                        }
                    }

                    string name = item.Name;
                    foreach (ItemComponent component in item.Components)
                    {
                        component.AddTooltipInfo(ref name, ref description);
                    }

                    if (item.Prefab.ShowContentsInTooltip && item.OwnInventory != null)
                    {
                        foreach (string itemName in item.OwnInventory.AllItems.Select(it => it.Name).Distinct())
                        {
                            int itemCount = item.OwnInventory.AllItems.Count(it => it != null && it.Name == itemName);
                            description += itemCount == 1 ?
                                "\n    " + itemName :
                                "\n    " + itemName + " x" + itemCount;
                        }
                    }

                    string colorStr = XMLExtensions.ColorToString(!item.AllowStealing ? GUI.Style.Red : Color.White);

                    toolTip = $"‖color:{colorStr}‖{name}‖color:end‖";
                    if (item.GetComponent<Quality>() != null)
                    {
                        // substring by to get rid of the empty space at start, text file should be adjusted
                        toolTip += $"\n{TextManager.GetWithVariable("itemname.quality" + item.Quality, "[itemname]", "", fallBackTag: "itemname.quality3")?.Substring(1)}";
                    }

                    if (itemsInSlot.All(it => it.NonInteractable || it.NonPlayerTeamInteractable))
                    {
                        toolTip += " " + TextManager.Get("connectionlocked");
                    }
                    if (!item.IsFullCondition && !item.Prefab.HideConditionInTooltip)
                    {
                        string conditionColorStr = XMLExtensions.ColorToString(ToolBox.GradientLerp(item.Condition / item.MaxCondition, GUI.Style.ColorInventoryEmpty, GUI.Style.ColorInventoryHalf, GUI.Style.ColorInventoryFull));
                        toolTip += $"‖color:{conditionColorStr}‖ ({(int)item.ConditionPercentage} %)‖color:end‖";
                    }
                    if (!string.IsNullOrEmpty(description)) { toolTip += '\n' + description; }
                    if (item.prefab.ContentPackage != GameMain.VanillaContent && item.prefab.ContentPackage != null)
                    {
                        colorStr = XMLExtensions.ColorToString(Color.MediumPurple);
                        toolTip += $"\n‖color:{colorStr}‖{item.prefab.ContentPackage.Name}‖color:end‖";
                    }
                }
                if (itemsInSlot.Count() > 1)
                {
                    string colorStr = XMLExtensions.ColorToString(GUI.Style.Blue);
                    toolTip += $"\n‖color:{colorStr}‖[{GameMain.Config.KeyBindText(InputType.TakeOneFromInventorySlot)}] {TextManager.Get("inputtype.takeonefrominventoryslot")}‖color:end‖";
                    colorStr = XMLExtensions.ColorToString(GUI.Style.Blue);
                    toolTip += $"\n‖color:{colorStr}‖[{GameMain.Config.KeyBindText(InputType.TakeHalfFromInventorySlot)}] {TextManager.Get("inputtype.takehalffrominventoryslot")}‖color:end‖";
                }
                return toolTip;
            }
        }

        public static VisualSlot DraggingSlot;
        public static readonly List<Item> DraggingItems = new List<Item>();
        public static bool DraggingItemToWorld
        {
            get
            {
                return Character.Controlled != null &&
                  Character.Controlled.SelectedConstruction == null &&
                  CharacterHealth.OpenHealthWindow == null &&
                  DraggingItems.Any();
            }
        }

        public static readonly List<Item> doubleClickedItems = new List<Item>();

        protected Vector4 padding;

        private int slotsPerRow;
        public int SlotsPerRow
        {
            set { slotsPerRow = Math.Max(1, value); }
        }

        protected static HashSet<SlotReference> highlightedSubInventorySlots = new HashSet<SlotReference>();
        private static readonly List<SlotReference> subInventorySlotsToDraw = new List<SlotReference>();

        protected static SlotReference selectedSlot;

        public VisualSlot[] visualSlots;

        private Rectangle prevRect;
        /// <summary>
        /// If set, the inventory is automatically positioned inside the rect
        /// </summary>
        public RectTransform RectTransform;

        public static SlotReference SelectedSlot
        {
            get
            {
                if (selectedSlot?.ParentInventory?.Owner == null || selectedSlot.ParentInventory.Owner.Removed)
                {
                    return null;
                }
                return selectedSlot;
            }
        }

        public Inventory GetReplacementOrThiS()
        {
            return ReplacedBy?.GetReplacementOrThiS() ?? this;
        }

        public virtual void CreateSlots()
        {
            visualSlots = new VisualSlot[capacity];

            int rows = (int)Math.Ceiling((double)capacity / slotsPerRow);
            int columns = Math.Min(slotsPerRow, capacity);

            Vector2 spacing = new Vector2(5.0f * UIScale);
            spacing.Y += (this is CharacterInventory) ? UnequippedIndicator.size.Y * UIScale : ContainedIndicatorHeight;
            Vector2 rectSize = new Vector2(60.0f * UIScale);

            padding = new Vector4(spacing.X, spacing.Y, spacing.X, spacing.X);

            Vector2 slotAreaSize = new Vector2(
                columns * rectSize.X + (columns - 1) * spacing.X,
                rows * rectSize.Y + (rows - 1) * spacing.Y);
            slotAreaSize.X += padding.X + padding.Z;
            slotAreaSize.Y += padding.Y + padding.W;

            Vector2 topLeft = new Vector2(
                GameMain.GraphicsWidth / 2 - slotAreaSize.X / 2,
                GameMain.GraphicsHeight / 2 - slotAreaSize.Y / 2);

            if (RectTransform != null)
            {
                Vector2 scale = new Vector2(
                    RectTransform.Rect.Width / slotAreaSize.X,
                    RectTransform.Rect.Height / slotAreaSize.Y);

                spacing *= scale;
                rectSize *= scale;
                padding.X *= scale.X; padding.Z *= scale.X;
                padding.Y *= scale.Y; padding.W *= scale.Y;

                topLeft = RectTransform.TopLeft.ToVector2() + new Vector2(padding.X, padding.Y);
                prevRect = RectTransform.Rect;
            }

            Rectangle slotRect = new Rectangle((int)topLeft.X, (int)topLeft.Y, (int)rectSize.X, (int)rectSize.Y);
            for (int i = 0; i < capacity; i++)
            {
                slotRect.X = (int)(topLeft.X + (rectSize.X + spacing.X) * (i % slotsPerRow));
                slotRect.Y = (int)(topLeft.Y + (rectSize.Y + spacing.Y) * ((int)Math.Floor((double)i / slotsPerRow)));
                visualSlots[i] = new VisualSlot(slotRect);
                visualSlots[i].InteractRect = new Rectangle(
                    (int)(visualSlots[i].Rect.X - spacing.X / 2 - 1), (int)(visualSlots[i].Rect.Y - spacing.Y / 2 - 1),
                    (int)(visualSlots[i].Rect.Width + spacing.X + 2), (int)(visualSlots[i].Rect.Height + spacing.Y + 2));

                if (visualSlots[i].Rect.Width > visualSlots[i].Rect.Height)
                {
                    visualSlots[i].Rect.Inflate((visualSlots[i].Rect.Height - visualSlots[i].Rect.Width) / 2, 0);
                }
                else
                {
                    visualSlots[i].Rect.Inflate(0, (visualSlots[i].Rect.Width - visualSlots[i].Rect.Height) / 2);
                }
            }

            if (selectedSlot != null && selectedSlot.ParentInventory == this)
            {
                selectedSlot = new SlotReference(this, visualSlots[selectedSlot.SlotIndex], selectedSlot.SlotIndex, selectedSlot.IsSubSlot, selectedSlot.Inventory);
            }
            CalculateBackgroundFrame();
        }

        protected virtual void CalculateBackgroundFrame()
        {
        }

        public bool Movable()
        {
            return movableFrameRect.Size != Point.Zero;
        }

        public bool IsInventoryHoverAvailable(Character owner, ItemContainer container)
        {
            if (container == null && this is ItemInventory)
            {
                container = (this as ItemInventory).Container;
            }

            if (container == null) { return false; }
            return owner.SelectedCharacter != null|| (!(owner is Character character)) || !container.KeepOpenWhenEquippedBy(character) || !owner.HasEquippedItem(container.Item);
        }

        protected virtual bool HideSlot(int i)
        {
            return visualSlots[i].Disabled || (slots[i].HideIfEmpty && slots[i].Empty());
        }

        public virtual void Update(float deltaTime, Camera cam, bool subInventory = false)
        {
            if (visualSlots == null || isSubInventory != subInventory ||
                (RectTransform != null && RectTransform.Rect != prevRect))
            {
                CreateSlots();
                isSubInventory = subInventory;
            }

            if (!subInventory || (OpenState >= 0.99f || OpenState < 0.01f))
            {
                for (int i = 0; i < capacity; i++)
                {
                    if (HideSlot(i)) { continue; }
                    UpdateSlot(visualSlots[i], i, slots[i].Items.FirstOrDefault(), subInventory);
                }
                if (!isSubInventory)
                {
                    ControlInput(cam);
                }
            }
        }

        protected virtual void ControlInput(Camera cam)
        {
            // Note that these targets are static. Therefore the outcome is the same if this method is called multiple times or only once.
            if (selectedSlot != null && !DraggingItemToWorld && cam.GetZoomAmountFromPrevious() <= 0.25f)
            {
                cam.Freeze = true;
            }
        }

        protected void UpdateSlot(VisualSlot slot, int slotIndex, Item item, bool isSubSlot)
        {
            Rectangle interactRect = slot.InteractRect;
            interactRect.Location += slot.DrawOffset.ToPoint();

            bool mouseOnGUI = false;
            /*if (GUI.MouseOn != null)
            {
                //block usage if the mouse is on a GUIComponent that's not related to this inventory
                if (RectTransform == null || (RectTransform != GUI.MouseOn.RectTransform && !GUI.MouseOn.IsParentOf(RectTransform.GUIComponent)))
                {
                    mouseOnGUI = true;
                }
            }*/

            bool mouseOn = interactRect.Contains(PlayerInput.MousePosition) && !Locked && !mouseOnGUI && !slot.Disabled && IsMouseOnInventory;

            // Delete item from container in sub editor
            if (SubEditorScreen.IsSubEditor() && PlayerInput.IsCtrlDown())
            {
                DraggingItems.Clear();
                var mouseDrag = SubEditorScreen.MouseDragStart != Vector2.Zero && Vector2.Distance(PlayerInput.MousePosition, SubEditorScreen.MouseDragStart) >= GUI.Scale * 20;
                if (mouseOn && (PlayerInput.PrimaryMouseButtonClicked() || mouseDrag))
                {
                    if (item != null)
                    {
                        slot.ShowBorderHighlight(GUI.Style.Red, 0.1f, 0.4f);
                        if (!mouseDrag)
                        {
                            SoundPlayer.PlayUISound(GUISoundType.PickItem);
                        }

                        if (!item.Removed)
                        {
                            SubEditorScreen.BulkItemBufferInUse = SubEditorScreen.ItemRemoveMutex;
                            SubEditorScreen.BulkItemBuffer.Add(new AddOrDeleteCommand(new List<MapEntity> { item }, true));
                        }

                        item.OwnInventory?.DeleteAllItems();
                        item.Remove();
                    }
                }
            }
            
            if (PlayerInput.LeftButtonHeld() && PlayerInput.RightButtonHeld())
            {
                mouseOn = false;
            }
            
            if (selectedSlot != null && selectedSlot.Slot != slot)
            {
                //subinventory slot highlighted -> don't allow highlighting this one
                if (selectedSlot.IsSubSlot && !isSubSlot)
                {
                    mouseOn = false;
                }
                else if (!selectedSlot.IsSubSlot && isSubSlot && mouseOn)
                {
                    selectedSlot = null;
                }
            }


            slot.State = GUIComponent.ComponentState.None;

            if (mouseOn && (DraggingItems.Any() || selectedSlot == null || selectedSlot.Slot == slot) && DraggingInventory == null)
            // &&
            //(highlightedSubInventories.Count == 0 || highlightedSubInventories.Contains(this) || highlightedSubInventorySlot?.Slot == slot || highlightedSubInventory.Owner == item))
            {
                
                slot.State = GUIComponent.ComponentState.Hover;

                if (selectedSlot == null || (!selectedSlot.IsSubSlot && isSubSlot))
                {
                    var slotRef = new SlotReference(this, slot, slotIndex, isSubSlot, slots[slotIndex].FirstOrDefault()?.GetComponent<ItemContainer>()?.Inventory);
                    if (Screen.Selected is SubEditorScreen editor && !editor.WiringMode && slotRef.ParentInventory is CharacterInventory) { return; }
                    if (CanSelectSlot(slotRef))
                    {
                        selectedSlot = slotRef;
                    }
                }

                if (!DraggingItems.Any())
                {
                    var interactableItems = Screen.Selected == GameMain.GameScreen ? slots[slotIndex].Items.Where(it => !it.NonInteractable && !it.NonPlayerTeamInteractable) : slots[slotIndex].Items;
                    if (PlayerInput.PrimaryMouseButtonDown() && interactableItems.Any())
                    {
                        if (PlayerInput.KeyDown(InputType.TakeHalfFromInventorySlot))
                        {
                            DraggingItems.AddRange(interactableItems.Skip(interactableItems.Count() / 2));
                        }
                        else if (PlayerInput.KeyDown(InputType.TakeOneFromInventorySlot))
                        {
                            DraggingItems.Add(interactableItems.First());
                        }
                        else
                        {
                            DraggingItems.AddRange(interactableItems);
                        }
                        DraggingSlot = slot;
                    }
                }
                else if (PlayerInput.PrimaryMouseButtonReleased())
                {
                    var interactableItems = Screen.Selected == GameMain.GameScreen ? slots[slotIndex].Items.Where(it => !it.NonInteractable && !it.NonPlayerTeamInteractable) : slots[slotIndex].Items;
                    if (PlayerInput.DoubleClicked() && interactableItems.Any())
                    {
                        doubleClickedItems.Clear();
                        if (PlayerInput.KeyDown(InputType.TakeHalfFromInventorySlot))
                        {
                            doubleClickedItems.AddRange(interactableItems.Skip(interactableItems.Count() / 2));
                        }
                        else if (PlayerInput.KeyDown(InputType.TakeOneFromInventorySlot))
                        {
                            doubleClickedItems.Add(interactableItems.First());
                        }
                        else
                        {
                            doubleClickedItems.AddRange(interactableItems);
                        }
                    }
                }
            }
        }

        protected Inventory GetSubInventory(int slotIndex)
        {
            var container = slots[slotIndex].FirstOrDefault()?.GetComponent<ItemContainer>();
            if (container == null) { return null; }

            return container.Inventory;
        }

        protected virtual ItemInventory GetActiveEquippedSubInventory(int slotIndex)
        {
            return null;
        }

        public float OpenState;

        public void UpdateSubInventory(float deltaTime, int slotIndex, Camera cam)
        {
            var item = slots[slotIndex].FirstOrDefault();
            if (item == null) { return; }

            var container = item.GetComponent<ItemContainer>();
            if (container == null || !container.DrawInventory) { return; }

            var subInventory = container.Inventory;
            if (subInventory.visualSlots == null) { subInventory.CreateSlots(); }

            canMove = container.MovableFrame && !subInventory.IsInventoryHoverAvailable(Owner as Character, container) && subInventory.originalPos != Point.Zero;
            if (this is CharacterInventory characterInventory && characterInventory.CurrentLayout != CharacterInventory.Layout.Default)
            {
                canMove = false;
            }

            if (canMove)
            {
                subInventory.HideTimer = 1.0f;
                subInventory.OpenState = 1.0f;
                if (subInventory.movableFrameRect.Contains(PlayerInput.MousePosition) && PlayerInput.SecondaryMouseButtonClicked())
                {
                    container.Inventory.savedPosition = container.Inventory.originalPos;
                }
                if (subInventory.movableFrameRect.Contains(PlayerInput.MousePosition) || (DraggingInventory != null && DraggingInventory == subInventory))
                {
                    if (DraggingInventory == null)
                    {
                        if (PlayerInput.PrimaryMouseButtonDown())
                        {
                            // Prevent us from dragging an item
                            DraggingItems.Clear();
                            DraggingSlot = null;
                            DraggingInventory = subInventory;
                        }
                    }
                    else if (PlayerInput.PrimaryMouseButtonReleased())
                    {
                        DraggingInventory = null;
                        subInventory.savedPosition = PlayerInput.MousePosition.ToPoint();
                    }
                    else
                    {
                        subInventory.savedPosition = PlayerInput.MousePosition.ToPoint();
                    }
                }
            }

            int itemCapacity = subInventory.slots.Length;
            var slot = visualSlots[slotIndex];
            int dir = slot.SubInventoryDir;
            Rectangle subRect = slot.Rect;
            Vector2 spacing;

            if (GUI.IsFourByThree())
            {
                spacing = new Vector2(5 * UIScale, (5 + UnequippedIndicator.size.Y) * UIScale);
            }
            else
            {
                spacing = new Vector2(10 * UIScale, (10 + UnequippedIndicator.size.Y) * UIScale);
            }

            int columns = MathHelper.Clamp((int)Math.Floor(Math.Sqrt(itemCapacity)), 1, container.SlotsPerRow);
            while (itemCapacity / columns * (subRect.Height + spacing.Y) > GameMain.GraphicsHeight * 0.5f)
            {
                columns++;
            }

            int width = (int)(subRect.Width * columns + spacing.X * (columns - 1));
            int startX = slot.Rect.Center.X - (int)(width / 2.0f);
            int startY = dir < 0 ?
                slot.EquipButtonRect.Y - subRect.Height - (int)(35 * UIScale) :
                slot.EquipButtonRect.Bottom + (int)(10 * UIScale);

            if (canMove)
            {
                startX += subInventory.savedPosition.X - subInventory.originalPos.X;
                startY += subInventory.savedPosition.Y - subInventory.originalPos.Y;
            }

            float totalHeight = itemCapacity / columns * (subRect.Height + spacing.Y);
            int padding = (int)(20 * UIScale);

            //prevent the inventory from extending outside the left side of the screen
            startX = Math.Max(startX, padding);
            //same for the right side of the screen
            startX -= Math.Max(startX + width - GameMain.GraphicsWidth + padding, 0);

            //prevent the inventory from extending outside the top of the screen
            startY = Math.Max(startY, (int)totalHeight - padding / 2);
            //same for the bottom side of the screen
            startY -= Math.Max(startY - GameMain.GraphicsHeight + padding * 2 + (canMove ? (int)(movableFrameRectHeight * UIScale) : 0), 0);               

            subRect.X = startX;
            subRect.Y = startY;

            subInventory.OpenState = subInventory.HideTimer >= 0.5f ?
                Math.Min(subInventory.OpenState + deltaTime * 8.0f, 1.0f) :
                Math.Max(subInventory.OpenState - deltaTime * 5.0f, 0.0f);

            for (int i = 0; i < itemCapacity; i++)
            {
                subInventory.visualSlots[i].Rect = subRect;
                subInventory.visualSlots[i].Rect.Location += new Point(0, (int)totalHeight * -dir);

                subInventory.visualSlots[i].DrawOffset = Vector2.SmoothStep(new Vector2(0, -50 * dir), new Vector2(0, totalHeight * dir), subInventory.OpenState);

                subInventory.visualSlots[i].InteractRect = new Rectangle(
                    (int)(subInventory.visualSlots[i].Rect.X - spacing.X / 2 - 1), (int)(subInventory.visualSlots[i].Rect.Y - spacing.Y / 2 - 1),
                    (int)(subInventory.visualSlots[i].Rect.Width + spacing.X + 2), (int)(subInventory.visualSlots[i].Rect.Height + spacing.Y + 2));

                if ((i + 1) % columns == 0)
                {
                    subRect.X = startX;
                    subRect.Y += subRect.Height * dir;
                    subRect.Y += (int)(spacing.Y * dir);
                }
                else
                {
                    subRect.X = (int)(subInventory.visualSlots[i].Rect.Right + spacing.X);
                }
            }

            if (canMove)
            {
                subInventory.movableFrameRect.X = subRect.X - (int)spacing.X;
                subInventory.movableFrameRect.Y = subRect.Y + (int)(spacing.Y);
            }
            visualSlots[slotIndex].State = GUIComponent.ComponentState.Hover;
            
            subInventory.isSubInventory = true;
            subInventory.Update(deltaTime, cam, true);
        }

        public void ClearSubInventories()
        {
            if (highlightedSubInventorySlots.Count == 0) { return; }

            foreach (SlotReference highlightedSubInventorySlot in highlightedSubInventorySlots)
            {
                highlightedSubInventorySlot.Inventory.HideTimer = 0.0f;
            }

            highlightedSubInventorySlots.Clear();
        }

        public virtual void Draw(SpriteBatch spriteBatch, bool subInventory = false)
        {
            if (visualSlots == null || isSubInventory != subInventory) { return; }

            for (int i = 0; i < capacity; i++)
            {
                if (HideSlot(i)) { continue; }

                //don't draw the item if it's being dragged out of the slot
                bool drawItem = !DraggingItems.Any() || !slots[i].Items.All(it => DraggingItems.Contains(it)) || visualSlots[i].MouseOn();

                DrawSlot(spriteBatch, this, visualSlots[i], slots[i].FirstOrDefault(), i, drawItem);
            }
        }
        
        /// <summary>
        /// Check if the mouse is hovering on top of the slot
        /// </summary>
        /// <param name="slot">The desired slot we want to check</param>
        /// <returns>True if our mouse is hover on the slot, false otherwise</returns>
        public static bool IsMouseOnSlot(VisualSlot slot)
        {
            var rect = new Rectangle(slot.InteractRect.X, slot.InteractRect.Y, slot.InteractRect.Width, slot.InteractRect.Height);
            rect.Offset(slot.DrawOffset);
            return rect.Contains(PlayerInput.MousePosition);
        }

        public static bool IsMouseOnInventory
        {
            get; private set;
        }

        /// <summary>
        /// Refresh the value of IsMouseOnInventory
        /// </summary>
        public static void RefreshMouseOnInventory()
        {
            IsMouseOnInventory = DetermineMouseOnInventory();
        }

        /// <summary>
        /// Is the mouse on any inventory element (slot, equip button, subinventory...)
        /// </summary>
        private static bool DetermineMouseOnInventory(bool ignoreDraggedItem = false)
        {
            if (GameMain.GameSession?.Campaign != null &&
                (GameMain.GameSession.Campaign.ShowCampaignUI || GameMain.GameSession.Campaign.ForceMapUI))
            {
                return false;
            }
            if (GameSession.IsTabMenuOpen) { return false; }
            if (CrewManager.IsCommandInterfaceOpen) { return false; }

            if (Character.Controlled == null) { return false; }

            if (!ignoreDraggedItem)
            {
                if (DraggingItems.Any() || DraggingInventory != null) { return true; }
            }

            var isSubEditor = Screen.Selected is SubEditorScreen editor && !editor.WiringMode;

            if (Character.Controlled.Inventory != null && !isSubEditor)
            {
                var inv = Character.Controlled.Inventory;
                for (var i = 0; i < inv.visualSlots.Length; i++)
                {
                    var slot = inv.visualSlots[i];
                    if (slot.InteractRect.Contains(PlayerInput.MousePosition))
                    {
                        return true;
                    }

                    // check if the equip button actually exists
                    if (slot.EquipButtonRect.Contains(PlayerInput.MousePosition) && 
                        i >= 0 && inv.slots.Length > i &&
                        !inv.slots[i].Empty())
                    {
                        return true;
                    }
                }
            }
            
            if (Character.Controlled.SelectedCharacter?.Inventory != null && !isSubEditor)
            {
                var inv = Character.Controlled.SelectedCharacter.Inventory;
                for (var i = 0; i < inv.visualSlots.Length; i++)
                {
                    var slot = inv.visualSlots[i];
                    if (slot.InteractRect.Contains(PlayerInput.MousePosition))
                    {
                        return true;
                    }
                    
                    // check if the equip button actually exists
                    if (slot.EquipButtonRect.Contains(PlayerInput.MousePosition) && 
                        i >= 0 && inv.slots.Length > i &&
                        !inv.slots[i].Empty())
                    {
                        return true;
                    }
                }
            }

            if (Character.Controlled.SelectedConstruction != null)
            {
                foreach (var ic in Character.Controlled.SelectedConstruction.ActiveHUDs)
                {
                    var itemContainer = ic as ItemContainer;
                    if (itemContainer?.Inventory?.visualSlots == null) { continue; }

                    foreach (VisualSlot slot in itemContainer.Inventory.visualSlots)
                    {
                        if (slot.InteractRect.Contains(PlayerInput.MousePosition) ||
                            slot.EquipButtonRect.Contains(PlayerInput.MousePosition))
                        {
                            return true;
                        }
                    }
                }
            }

            foreach (SlotReference highlightedSubInventorySlot in highlightedSubInventorySlots)
            {
                if (GetSubInventoryHoverArea(highlightedSubInventorySlot).Contains(PlayerInput.MousePosition)) { return true; }
            }

            return false;
        }
        
        public static CursorState GetInventoryMouseCursor()
        {
            var character = Character.Controlled;
            if (character == null) { return CursorState.Default; }
            if (DraggingItems.Any() || DraggingInventory != null) { return CursorState.Dragging; }
            
            var inv = character.Inventory;
            var selInv = character.SelectedCharacter?.Inventory;
            
            if (inv == null) { return CursorState.Default; }

            foreach (var item in inv.AllItems)
            {
                var container = item?.GetComponent<ItemContainer>();
                if (container == null) { continue; }

                if (container.Inventory.visualSlots != null)
                {
                    if (container.Inventory.visualSlots.Any(slot => slot.IsHighlighted))
                    {
                        return CursorState.Hand;
                    }
                }

                if (container.Inventory.movableFrameRect.Contains(PlayerInput.MousePosition))
                {
                    return CursorState.Move;
                }
            }            
            
            if (selInv != null)
            {
                for (int i = 0; i < selInv.visualSlots.Length; i++)
                {
                    VisualSlot slot = selInv.visualSlots[i];
                    Item item = selInv.slots[i].FirstOrDefault();
                    if (slot.InteractRect.Contains(PlayerInput.MousePosition) || 
                        (slot.EquipButtonRect.Contains(PlayerInput.MousePosition) && item != null && item.AllowedSlots.Contains(InvSlotType.Any)))
                    {
                        return CursorState.Hand;
                    }
                    var container = item?.GetComponent<ItemContainer>();
                    if (container == null) { continue; }
                    if (container.Inventory.visualSlots != null)
                    {
                        if (container.Inventory.visualSlots.Any(slot => slot.IsHighlighted))
                        {
                            return CursorState.Hand;
                        }
                    }
                }
            }
            
            if (character.SelectedConstruction != null)
            {
                foreach (var ic in character.SelectedConstruction.ActiveHUDs)
                {
                    var itemContainer = ic as ItemContainer;
                    if (itemContainer?.Inventory?.visualSlots == null) { continue; }
                    if (!ic.Item.IsInteractable(character)) { continue; }

                    foreach (var slot in itemContainer.Inventory.visualSlots)
                    {
                        if (slot.InteractRect.Contains(PlayerInput.MousePosition) ||
                            slot.EquipButtonRect.Contains(PlayerInput.MousePosition))
                        {
                            return CursorState.Hand;
                        }
                    }
                }
            }

            for (int i = 0; i < inv.visualSlots.Length; i++)
            {
                VisualSlot slot = inv.visualSlots[i];
                Item item = inv.slots[i].FirstOrDefault();
                if (slot.EquipButtonRect.Contains(PlayerInput.MousePosition) && item != null && item.AllowedSlots.Contains(InvSlotType.Any))
                {
                    return CursorState.Hand;
                }
                
                // This is the only place we double check this because if we have a inventory container
                // highlighting any area within that container registers as highlighting the
                // original slot the item is in thus giving us a false hand cursor.
                if (slot.InteractRect.Contains(PlayerInput.MousePosition))
                {
                    if (slot.IsHighlighted)
                    {
                        return CursorState.Hand;
                    }
                }
            }
            return CursorState.Default;
        }

        protected static void DrawToolTip(SpriteBatch spriteBatch, string toolTip, Rectangle highlightedSlot, List<RichTextData> richTextData = null)
        {           
            GUIComponent.DrawToolTip(spriteBatch, toolTip, highlightedSlot, richTextData);
        }

        public void DrawSubInventory(SpriteBatch spriteBatch, int slotIndex)
        {
            var item = slots[slotIndex].FirstOrDefault();
            if (item == null) { return; }

            var container = item.GetComponent<ItemContainer>();
            if (container == null || !container.DrawInventory) { return; }

            if (container.Inventory.visualSlots == null || !container.Inventory.isSubInventory) { return; }

            int itemCapacity = container.Capacity;

#if DEBUG
            System.Diagnostics.Debug.Assert(slotIndex >= 0 && slotIndex < slots.Length);
#else
            if (slotIndex < 0 || slotIndex >= capacity) { return; }
#endif

            if (!canMove)
            {
                Rectangle prevScissorRect = spriteBatch.GraphicsDevice.ScissorRectangle;
                spriteBatch.End();
                spriteBatch.Begin(SpriteSortMode.Deferred, rasterizerState: GameMain.ScissorTestEnable);
                if (visualSlots[slotIndex].SubInventoryDir > 0)
                {
                    spriteBatch.GraphicsDevice.ScissorRectangle = new Rectangle(
                        new Point(0, visualSlots[slotIndex].Rect.Bottom),
                        new Point(GameMain.GraphicsWidth, (int)Math.Max(GameMain.GraphicsHeight - visualSlots[slotIndex].Rect.Bottom, 0)));
                }
                else
                {
                    spriteBatch.GraphicsDevice.ScissorRectangle = new Rectangle(
                        new Point(0, 0),
                        new Point(GameMain.GraphicsWidth, visualSlots[slotIndex].Rect.Y));
                }
                container.Inventory.Draw(spriteBatch, true);
                spriteBatch.End();
                spriteBatch.GraphicsDevice.ScissorRectangle = prevScissorRect;
                spriteBatch.Begin(SpriteSortMode.Deferred);
            }
            else
            {
                container.Inventory.Draw(spriteBatch, true);
            }

            container.InventoryBottomSprite?.Draw(spriteBatch,
                new Vector2(visualSlots[slotIndex].Rect.Center.X, visualSlots[slotIndex].Rect.Y) + visualSlots[slotIndex].DrawOffset,
                0.0f, UIScale);

            container.InventoryTopSprite?.Draw(spriteBatch,
                new Vector2(
                    visualSlots[slotIndex].Rect.Center.X,
                    container.Inventory.visualSlots[container.Inventory.visualSlots.Length - 1].Rect.Y) + container.Inventory.visualSlots[container.Inventory.visualSlots.Length - 1].DrawOffset,
                0.0f, UIScale);

            if (container.MovableFrame && !IsInventoryHoverAvailable(Owner as Character, container))
            {
                if (positionUpdateQueued) // Wait a frame before updating the positioning of the container after a resolution change to have everything working
                {
                    int height = (int)(movableFrameRectHeight * UIScale);
                    CreateSlots();
                    container.Inventory.movableFrameRect = new Rectangle(container.Inventory.BackgroundFrame.X, container.Inventory.BackgroundFrame.Y - height, container.Inventory.BackgroundFrame.Width, height);
                    draggableIndicatorScale = 1.25f * UIScale;
                    draggableIndicatorOffset = DraggableIndicator.size * draggableIndicatorScale / 2f;
                    draggableIndicatorOffset += new Vector2(height / 2f - draggableIndicatorOffset.Y);
                    container.Inventory.originalPos = container.Inventory.savedPosition = container.Inventory.movableFrameRect.Center;
                    positionUpdateQueued = false;
                }

                if (container.Inventory.movableFrameRect.Size == Point.Zero || GUI.HasSizeChanged(prevScreenResolution, prevUIScale, prevHUDScale))
                {
                    // Reset position
                    container.Inventory.savedPosition = container.Inventory.originalPos;

                    prevScreenResolution = new Point(GameMain.GraphicsWidth, GameMain.GraphicsHeight);
                    prevUIScale = UIScale;
                    prevHUDScale = GUI.Scale;
                    positionUpdateQueued = true;
                }
                else
                {
                    GUI.DrawRectangle(spriteBatch, container.Inventory.movableFrameRect, movableFrameRectColor, true);
                    DraggableIndicator.Draw(spriteBatch, container.Inventory.movableFrameRect.Location.ToVector2() + draggableIndicatorOffset, 0, draggableIndicatorScale);
                }             
            }
        }

        public static void UpdateDragging()
        {
            if (Screen.Selected == GameMain.GameScreen)
            {
                DraggingItems.RemoveAll(it => !Character.Controlled.CanInteractWith(it));
            }
            if (DraggingItems.Any() && PlayerInput.PrimaryMouseButtonReleased())
            {
                Character.Controlled.ClearInputs();

                if (!DetermineMouseOnInventory(ignoreDraggedItem: true) &&
                    CharacterHealth.OpenHealthWindow != null)
                {
                    bool dropSuccessful = false;
                    foreach (Item item in DraggingItems)
                    {
                        var inventory = item.ParentInventory;
                        var indices = inventory?.FindIndices(item);
                        dropSuccessful |= CharacterHealth.OpenHealthWindow.OnItemDropped(item, false);
                        if (dropSuccessful)
                        {
                            if (indices != null && inventory.visualSlots != null)
                            {
                                foreach (int i in indices)
                                {
                                    inventory.visualSlots[i]?.ShowBorderHighlight(GUI.Style.Green, 0.1f, 0.4f);
                                }
                            }
                            break;
                        }

                    }
                    if (dropSuccessful)
                    {
                        DraggingItems.Clear();
                        return;
                    }
                }

                if (selectedSlot == null)
                {
                    if (DraggingItemToWorld &&
                        Character.Controlled.FocusedItem is { OwnInventory: { } inventory } item && item.GetComponent<ItemContainer>() is { } container && 
                        container.HasRequiredItems(Character.Controlled, addMessage: false) && 
                        container.AllowDragAndDrop && 
                        inventory.CanBePut(DraggingItems.FirstOrDefault()))
                    {
                        bool anySuccess = false;
                        foreach (Item it in DraggingItems)
                        {
                            bool success = Character.Controlled.FocusedItem.OwnInventory.TryPutItem(it, Character.Controlled);
                            if (!success) { break; }
                            anySuccess |= success;
                        }
                        if (anySuccess) { SoundPlayer.PlayUISound(GUISoundType.PickItem); }                       
                    }
                    else
                    {
                        if (Screen.Selected is SubEditorScreen)
                        {
                            if (DraggingItems.First()?.ParentInventory != null)
                            {
                                SubEditorScreen.StoreCommand(new InventoryPlaceCommand(DraggingItems.First().ParentInventory, new List<Item>(DraggingItems), true));
                            }
                        }
                        
                        SoundPlayer.PlayUISound(GUISoundType.DropItem);
                        bool removed = false;
                        if (Screen.Selected is SubEditorScreen editor)
                        {
                            if (editor.EntityMenu.Rect.Contains(PlayerInput.MousePosition))
                            {
                                DraggingItems.ForEachMod(it => it.Remove());
                                removed = true;
                            }
                            else
                            {
                                if (editor.WiringMode)
                                {
                                    DraggingItems.ForEachMod(it => it.Remove());
                                    removed = true;
                                }
                                else
                                {
                                    DraggingItems.ForEachMod(it => it.Drop(Character.Controlled));
                                }
                            }
                        }
                        else
                        {
                            DraggingItems.ForEachMod(it => it.Drop(Character.Controlled));
                        }

                        SoundPlayer.PlayUISound(removed ? GUISoundType.PickItem : GUISoundType.DropItem);
                    }
                }
                else if (!DraggingItems.Any(it => selectedSlot.ParentInventory.slots[selectedSlot.SlotIndex].Contains(it)))
                {
                    Inventory oldInventory = DraggingItems.First().ParentInventory;
                    Inventory selectedInventory = selectedSlot.ParentInventory;
                    int slotIndex = selectedSlot.SlotIndex;
                    int oldSlot = oldInventory == null ? 0 : Array.IndexOf(oldInventory.slots, DraggingItems);

                    //if attempting to drop into an invalid slot in the same inventory, try to move to the correct slot
                    if (selectedInventory.slots[slotIndex].Empty() &&
                        selectedInventory == Character.Controlled.Inventory &&
                        !DraggingItems.First().AllowedSlots.Any(a => a.HasFlag(Character.Controlled.Inventory.SlotTypes[slotIndex])) &&
                        DraggingItems.Any(it => selectedInventory.TryPutItem(it, Character.Controlled, it.AllowedSlots)))
                    {
                        if (selectedInventory.visualSlots != null)
                        {
                            for (int i = 0; i < selectedInventory.visualSlots.Length; i++)
                            {
                                if (DraggingItems.Any(it => selectedInventory.slots[i].Contains(it)))
                                {
                                    selectedInventory.visualSlots[slotIndex].ShowBorderHighlight(Color.White, 0.1f, 0.4f);
                                }
                            }
                            selectedInventory.visualSlots[slotIndex].ShowBorderHighlight(GUI.Style.Red, 0.1f, 0.9f);
                        }
                        SoundPlayer.PlayUISound(GUISoundType.PickItem);
                    }
                    else
                    {
                        bool anySuccess = false;
                        bool allowCombine = true;
                        //if we're dragging a stack of partial items or trying to drag to a stack of partial items
                        //(which should not normally exist, but can happen when e.g. fire damages a stack of items)
                        //don't allow combining because it leads to weird behavior (stack of items of mixed quality)
                        if (DraggingItems.Count(it => !it.IsFullCondition && it.Condition > 0.0f) > 1 || 
                            selectedInventory.GetItemsAt(slotIndex).Count(it => !it.IsFullCondition && it.Condition > 0.0f) > 1) 
                        { 
                            allowCombine = false; 
                        }
                        foreach (Item item in DraggingItems)
                        {
                            bool success =  selectedInventory.TryPutItem(item, slotIndex, allowSwapping: !anySuccess, allowCombine, Character.Controlled);
                            anySuccess |= success;
                            if (!success) { break; }
                        }

                        if (anySuccess)
                        {
                            highlightedSubInventorySlots.RemoveWhere(s => s.ParentInventory == oldInventory || s.ParentInventory == selectedInventory);
                            if (SubEditorScreen.IsSubEditor())
                            {
                                foreach (Item draggingItem in DraggingItems)
                                {
                                    if (selectedInventory.slots[slotIndex].Contains(draggingItem))
                                    {
                                        SubEditorScreen.StoreCommand(new InventoryMoveCommand(oldInventory, selectedInventory, draggingItem, oldSlot, slotIndex));
                                    }
                                }
                            }
                            if (selectedInventory.visualSlots != null) { selectedInventory.visualSlots[slotIndex].ShowBorderHighlight(Color.White, 0.1f, 0.4f); }
                            SoundPlayer.PlayUISound(GUISoundType.PickItem);
                        }
                        else
                        {
                            if (selectedInventory.visualSlots != null){ selectedInventory.visualSlots[slotIndex].ShowBorderHighlight(GUI.Style.Red, 0.1f, 0.9f); }
                            SoundPlayer.PlayUISound(GUISoundType.PickItemFail);
                        }
                    }

                    selectedInventory.HideTimer = 2.0f;
                    if (selectedSlot.ParentInventory?.Owner is Item parentItem && parentItem.ParentInventory != null)
                    {
                        for (int i = 0; i < parentItem.ParentInventory.capacity; i++)
                        {
                            if (parentItem.ParentInventory.HideSlot(i)) { continue; }
                            if (parentItem.ParentInventory.slots[i].FirstOrDefault() != parentItem) { continue; }

                            highlightedSubInventorySlots.Add(new SlotReference(
                                parentItem.ParentInventory, parentItem.ParentInventory.visualSlots[i],
                                i, false, selectedSlot.ParentInventory));
                            break;
                        }

                    }
                    DraggingItems.Clear();
                    DraggingSlot = null;
                }

                DraggingItems.Clear();
            }

            if (selectedSlot != null && !CanSelectSlot(selectedSlot))
            {
                selectedSlot = null;
            }            
        }

        private static bool CanSelectSlot(SlotReference selectedSlot)
        {
            if (!IsMouseOnInventory)
            {
                return false;
            }
            if (!selectedSlot.Slot.MouseOn())
            {
                return false;
            }
            else
            {
                var rootOwner = (selectedSlot.ParentInventory?.Owner as Item)?.GetRootInventoryOwner();
                if (selectedSlot.ParentInventory?.Owner != Character.Controlled &&
                   selectedSlot.ParentInventory?.Owner != Character.Controlled.SelectedCharacter &&
                   selectedSlot.ParentInventory?.Owner != Character.Controlled.SelectedConstruction &&
                   !(Character.Controlled.SelectedConstruction?.linkedTo.Contains(selectedSlot.ParentInventory?.Owner) ?? false) &&
                   rootOwner != Character.Controlled &&
                   rootOwner != Character.Controlled.SelectedCharacter &&
                   rootOwner != Character.Controlled.SelectedConstruction &&
                   !(Character.Controlled.SelectedConstruction?.linkedTo.Contains(rootOwner) ?? false))
                {
                    return false;
                }
                var parentItem = (selectedSlot?.ParentInventory?.Owner as Item) ?? selectedSlot?.Item;
                if ((parentItem?.GetRootInventoryOwner() is Character ownerCharacter) &&
                    ownerCharacter == Character.Controlled &&
                    CharacterHealth.OpenHealthWindow?.Character != ownerCharacter &&
                    ownerCharacter.Inventory.IsInLimbSlot(parentItem, InvSlotType.HealthInterface) &&
                    Screen.Selected != GameMain.SubEditorScreen)
                {
                    highlightedSubInventorySlots.RemoveWhere(s => s.Item == parentItem);
                    return false;
                }
            }
            return true;
        }


        protected static Rectangle GetSubInventoryHoverArea(SlotReference subSlot)
        {
            Rectangle hoverArea;
            if (!subSlot.Inventory.Movable() || 
                (Character.Controlled?.Inventory == subSlot.ParentInventory && !Character.Controlled.HasEquippedItem(subSlot.Item)) ||
                (subSlot.ParentInventory is CharacterInventory characterInventory && characterInventory.CurrentLayout != CharacterInventory.Layout.Default))
            {
                hoverArea = subSlot.Slot.Rect;
                hoverArea.Location += subSlot.Slot.DrawOffset.ToPoint();
                hoverArea = Rectangle.Union(hoverArea, subSlot.Slot.EquipButtonRect);
            }
            else
            {
                hoverArea = subSlot.Inventory.BackgroundFrame;
                hoverArea.Location += subSlot.Slot.DrawOffset.ToPoint();
                hoverArea = Rectangle.Union(hoverArea, subSlot.Inventory.movableFrameRect);
            }

            if (subSlot.Inventory?.visualSlots != null)
            {
                foreach (VisualSlot slot in subSlot.Inventory.visualSlots)
                {
                    Rectangle subSlotRect = slot.InteractRect;
                    subSlotRect.Location += slot.DrawOffset.ToPoint();
                    hoverArea = Rectangle.Union(hoverArea, subSlotRect);
                }
                if (subSlot.Slot.SubInventoryDir < 0)
                {
                    // 24/2/2020 - the below statement makes the sub inventory extend all the way to the bottom of the screen because of a double negative
                    // Not sure if it's intentional or not but it was causing hover issues and disabling it seems to have no detrimental effects.
                    // hoverArea.Height -= hoverArea.Bottom - subSlot.Slot.Rect.Bottom;
                }
                else
                {
                    int over = subSlot.Slot.Rect.Y - hoverArea.Y;
                    hoverArea.Y += over;
                    hoverArea.Height -= over;
                }
            }

            float inflateAmount = 10 * UIScale;

            hoverArea.Inflate(inflateAmount, inflateAmount);
            return hoverArea;
        }

        public static void DrawFront(SpriteBatch spriteBatch)
        {
            if (GUI.PauseMenuOpen || GUI.SettingsMenuOpen) { return; }
            if (GameMain.GameSession?.Campaign != null &&
                (GameMain.GameSession.Campaign.ShowCampaignUI || GameMain.GameSession.Campaign.ForceMapUI)) { return; }

            subInventorySlotsToDraw.Clear();
            subInventorySlotsToDraw.AddRange(highlightedSubInventorySlots);
            foreach (var slot in subInventorySlotsToDraw)
            {
                int slotIndex = Array.IndexOf(slot.ParentInventory.visualSlots, slot.Slot);
                if (slotIndex > -1 && slotIndex < slot.ParentInventory.visualSlots.Length &&
                    (slot.Item?.GetComponent<ItemContainer>()?.HasRequiredItems(Character.Controlled, addMessage: false) ?? true))
                {
                    slot.ParentInventory.DrawSubInventory(spriteBatch, slotIndex);
                }
            }  

            if (DraggingItems.Any())
            {
                if (DraggingSlot == null || (!DraggingSlot.MouseOn()))
                {
                    Sprite sprite = DraggingItems.First().Prefab.InventoryIcon ?? DraggingItems.First().Sprite;

                    int iconSize = (int)(64 * GUI.Scale);
                    float scale = Math.Min(Math.Min(iconSize / sprite.size.X, iconSize / sprite.size.Y), 1.5f);
                    Vector2 itemPos = PlayerInput.MousePosition;

                    bool mouseOnHealthInterface = CharacterHealth.OpenHealthWindow != null && CharacterHealth.OpenHealthWindow.MouseOnElement && DraggingItems.Any(it => it.UseInHealthInterface);

                    if ((GUI.MouseOn == null || mouseOnHealthInterface) && selectedSlot == null)
                    {
                        var shadowSprite = GUI.Style.GetComponentStyle("OuterGlow").Sprites[GUIComponent.ComponentState.None][0];
                        string toolTip = mouseOnHealthInterface ? TextManager.Get("QuickUseAction.UseTreatment") :
                            Character.Controlled.FocusedItem != null ?
                                TextManager.GetWithVariable("PutItemIn", "[itemname]", Character.Controlled.FocusedItem.Name, true) :
                                TextManager.Get(Screen.Selected is SubEditorScreen editor && editor.EntityMenu.Rect.Contains(PlayerInput.MousePosition) ? "Delete" : "DropItem");
                        int textWidth = (int)Math.Max(GUI.Font.MeasureString(DraggingItems.First().Name).X, GUI.SmallFont.MeasureString(toolTip).X);
                        int textSpacing = (int)(15 * GUI.Scale);
                        Point shadowBorders = (new Point(40, 10)).Multiply(GUI.Scale);
                        shadowSprite.Draw(spriteBatch,
                            new Rectangle(itemPos.ToPoint() - new Point(iconSize / 2) - shadowBorders, new Point(iconSize + textWidth + textSpacing, iconSize) + shadowBorders.Multiply(2)), Color.Black * 0.8f);
                        GUI.DrawString(spriteBatch, new Vector2(itemPos.X + iconSize / 2 + textSpacing, itemPos.Y - iconSize / 2), DraggingItems.First().Name, Color.White);
                        GUI.DrawString(spriteBatch, new Vector2(itemPos.X + iconSize / 2 + textSpacing, itemPos.Y), toolTip,
                            color: Character.Controlled.FocusedItem == null && !mouseOnHealthInterface ? GUI.Style.Red : Color.LightGreen,
                            font: GUI.SmallFont);
                    }
                    sprite.Draw(spriteBatch, itemPos + Vector2.One * 2, Color.Black, scale: scale);
                    sprite.Draw(spriteBatch,
                        itemPos,
                        sprite == DraggingItems.First().Sprite ? DraggingItems.First().GetSpriteColor() : DraggingItems.First().GetInventoryIconColor(),
                        scale: scale);

                    if (DraggingItems.First().Prefab.MaxStackSize > 1)
                    {
                        Vector2 stackCountPos = itemPos + Vector2.One * iconSize * 0.25f;
                        string stackCountText = "x" + DraggingItems.Count;
                        GUI.SmallFont.DrawString(spriteBatch, stackCountText, stackCountPos + Vector2.One, Color.Black);
                        GUI.SmallFont.DrawString(spriteBatch, stackCountText, stackCountPos, Color.White);
                    }
                }
            }

            if (selectedSlot != null && selectedSlot.Item != null)
            {
                Rectangle slotRect = selectedSlot.Slot.Rect;
                slotRect.Location += selectedSlot.Slot.DrawOffset.ToPoint();
                if (selectedSlot.TooltipNeedsRefresh())
                {
                    selectedSlot.RefreshTooltip();
                }
                DrawToolTip(spriteBatch, selectedSlot.Tooltip, slotRect, selectedSlot.TooltipRichTextData);
            }
        }

        public static void DrawSlot(SpriteBatch spriteBatch, Inventory inventory, VisualSlot slot, Item item, int slotIndex, bool drawItem = true, InvSlotType type = InvSlotType.Any)
        {
            Rectangle rect = slot.Rect;
            rect.Location += slot.DrawOffset.ToPoint();

            if (slot.HighlightColor.A > 0)
            {
                float inflateAmount = (slot.HighlightColor.A / 255.0f) * slot.HighlightScaleUpAmount * 0.5f;
                rect.Inflate(rect.Width * inflateAmount, rect.Height * inflateAmount);
            }

            Color slotColor = Color.White;
            Item parentItem = inventory?.Owner as Item;
            if (parentItem != null && !parentItem.IsPlayerTeamInteractable) { slotColor = Color.Gray; }
            var itemContainer = item?.GetComponent<ItemContainer>();
            if (itemContainer != null && (itemContainer.InventoryTopSprite != null || itemContainer.InventoryBottomSprite != null))
            {
                if (!highlightedSubInventorySlots.Any(s => s.Slot == slot))
                {
                    itemContainer.InventoryBottomSprite?.Draw(spriteBatch, new Vector2(rect.Center.X, rect.Y), 0, UIScale);
                    itemContainer.InventoryTopSprite?.Draw(spriteBatch, new Vector2(rect.Center.X, rect.Y), 0, UIScale);
                }

                drawItem = false;
            }
            else
            {
                Sprite slotSprite = slot.SlotSprite ?? SlotSpriteSmall;

                /*if (inventory != null && (CharacterInventory.PersonalSlots.HasFlag(type) || (inventory.isSubInventory && (inventory.Owner as Item) != null 
                    && (inventory.Owner as Item).AllowedSlots.Any(a => CharacterInventory.PersonalSlots.HasFlag(a)))))
                {
                    slotColor = slot.IsHighlighted ? GUI.Style.EquipmentSlotColor : GUI.Style.EquipmentSlotColor * 0.8f;
                }
                else
                {
                    slotColor = slot.IsHighlighted ? GUI.Style.InventorySlotColor : GUI.Style.InventorySlotColor * 0.8f;
                }*/

                if (inventory != null && inventory.Locked) { slotColor = Color.Gray * 0.5f; }
                spriteBatch.Draw(slotSprite.Texture, rect, slotSprite.SourceRect, slotColor);
                
                if (SubEditorScreen.IsSubEditor() && PlayerInput.IsCtrlDown() && selectedSlot?.Slot == slot)
                {
                    GUI.DrawRectangle(spriteBatch, rect, GUI.Style.Red * 0.3f, isFilled: true);
                }

                bool canBePut = false;

                if (DraggingItems.Any() && inventory != null && slotIndex > -1 && slotIndex < inventory.visualSlots.Length)
                {
                    if (inventory.CanBePutInSlot(DraggingItems.First(), slotIndex))
                    {
                        canBePut = true;
                    }
                    else if (inventory.slots[slotIndex].FirstOrDefault()?.OwnInventory?.CanBePut(DraggingItems.First()) ?? false)
                    {
                        canBePut = true;
                    }
                    else if (inventory.slots[slotIndex] == null && inventory == Character.Controlled.Inventory && 
                        !DraggingItems.First().AllowedSlots.Any(a => a.HasFlag(Character.Controlled.Inventory.SlotTypes[slotIndex])) &&
                        Character.Controlled.Inventory.CanBeAutoMovedToCorrectSlots(DraggingItems.First()))
                    {
                        canBePut = true;
                    }
                }
                if (slot.MouseOn() && canBePut && selectedSlot?.Slot == slot)
                {
                    GUI.UIGlow.Draw(spriteBatch, rect, GUI.Style.Green);
                }

                if (item != null && drawItem)
                {
                    if (!item.IsFullCondition && !item.Prefab.HideConditionBar && (itemContainer == null || !itemContainer.ShowConditionInContainedStateIndicator))
                    {
                        int dir = slot.SubInventoryDir;
                        Rectangle conditionIndicatorArea;
                        if (itemContainer != null && itemContainer.ShowContainedStateIndicator)
                        {
                            conditionIndicatorArea = new Rectangle(rect.X, rect.Bottom - (int)(10 * GUI.Scale), rect.Width, (int)(10 * GUI.Scale));
                        }
                        else
                        {
                            conditionIndicatorArea = new Rectangle(
                                rect.X, dir < 0 ? rect.Bottom + HUDLayoutSettings.Padding / 2 : rect.Y - HUDLayoutSettings.Padding / 2 - ContainedIndicatorHeight, 
                                rect.Width, ContainedIndicatorHeight);
                            conditionIndicatorArea.Inflate(-4, 0);
                        }

                        var indicatorStyle = GUI.Style.GetComponentStyle("ContainedStateIndicator.Default");
                        Sprite indicatorSprite = indicatorStyle?.GetDefaultSprite();
                        Sprite emptyIndicatorSprite = indicatorStyle?.GetSprite(GUIComponent.ComponentState.Hover);
                        DrawItemStateIndicator(spriteBatch, inventory, indicatorSprite, emptyIndicatorSprite, conditionIndicatorArea, item.Condition / item.MaxCondition);
                    }

                    if (itemContainer != null && itemContainer.ShowContainedStateIndicator)
                    {
                        float containedState = 0.0f;
                        if (itemContainer.ShowConditionInContainedStateIndicator)
                        {
                            containedState = item.Condition / item.MaxCondition;
                        }
                        else
                        {
                            var containedItem = itemContainer.Inventory.slots[Math.Max(itemContainer.ContainedStateIndicatorSlot, 0)].FirstOrDefault();
                            containedState = itemContainer.Inventory.Capacity == 1 || itemContainer.ContainedStateIndicatorSlot > -1 ?
                                (containedItem == null ? 0.0f : containedItem.Condition / containedItem.MaxCondition) :
                                itemContainer.Inventory.slots.Count(i => !i.Empty()) / (float)itemContainer.Inventory.capacity;
                            if (containedItem != null && itemContainer.Inventory.Capacity == 1)
                            {
                                int maxStackSize = Math.Min(containedItem.Prefab.MaxStackSize, itemContainer.GetMaxStackSize(0));
                                if (maxStackSize > 1 || containedItem.Prefab.HideConditionBar)
                                {
                                    containedState = itemContainer.Inventory.slots[0].ItemCount / (float)maxStackSize;
                                }
                            }
                        }

                        int dir = slot.SubInventoryDir;
                        Rectangle containedIndicatorArea = new Rectangle(rect.X,
                            dir < 0 ? rect.Bottom + HUDLayoutSettings.Padding / 2 : rect.Y - HUDLayoutSettings.Padding / 2 - ContainedIndicatorHeight, rect.Width, ContainedIndicatorHeight);
                        containedIndicatorArea.Inflate(-4, 0);

                        Sprite indicatorSprite = 
                            itemContainer.ContainedStateIndicator ??
                            itemContainer.IndicatorStyle?.GetDefaultSprite();
                        Sprite emptyIndicatorSprite =
                            itemContainer.ContainedStateIndicatorEmpty ??
                            itemContainer.IndicatorStyle?.GetSprite(GUIComponent.ComponentState.Hover);

                        bool usingDefaultSprite = itemContainer.IndicatorStyle?.Name == "ContainedStateIndicator.Default";

                        DrawItemStateIndicator(spriteBatch, inventory, indicatorSprite, emptyIndicatorSprite, containedIndicatorArea, containedState, 
                            pulsate: !usingDefaultSprite && containedState >= 0.0f && containedState < 0.25f && inventory == Character.Controlled?.Inventory && Character.Controlled.HasEquippedItem(item));
                    }

                    if (item.Quality != 0)
                    {
                        var style = GUI.Style.GetComponentStyle("InnerGlowSmall");
                        if (style == null)
                        {
                            GUI.DrawRectangle(spriteBatch, rect, GUI.Style.GetQualityColor(item.Quality) * 0.7f);
                        }
                        else
                        {
                            style.Sprites[GUIComponent.ComponentState.None].FirstOrDefault()?.Draw(spriteBatch, rect, GUI.Style.GetQualityColor(item.Quality) * 0.5f);
                        }
                    }
                }
                else
                {
                    var slotIcon = parentItem?.GetComponent<ItemContainer>()?.GetSlotIcon(slotIndex);
                    if (slotIcon != null)
                    {
                        slotIcon.Draw(spriteBatch, rect.Center.ToVector2(), GUI.Style.EquipmentSlotIconColor, scale: Math.Min(rect.Width / slotIcon.size.X, rect.Height / slotIcon.size.Y) * 0.8f);
                    }
                }
            }

            if (GameMain.DebugDraw)
            {
                GUI.DrawRectangle(spriteBatch, rect, Color.White, false, 0, 1);
                GUI.DrawRectangle(spriteBatch, slot.EquipButtonRect, Color.White, false, 0, 1);
            }

            if (slot.HighlightColor != Color.Transparent)
            {
                GUI.UIGlow.Draw(spriteBatch, rect, slot.HighlightColor);
            }

            if (item != null && drawItem)
            {
                Sprite sprite = item.Prefab.InventoryIcon ?? item.Sprite;
                float scale = Math.Min(Math.Min((rect.Width - 10) / sprite.size.X, (rect.Height - 10) / sprite.size.Y), 2.0f);
                Vector2 itemPos = rect.Center.ToVector2();
                if (itemPos.Y > GameMain.GraphicsHeight)
                {
                    itemPos.Y -= Math.Min(
                        (itemPos.Y + sprite.size.Y / 2 * scale) - GameMain.GraphicsHeight,
                        (itemPos.Y - sprite.size.Y / 2 * scale) - rect.Y);
                }

                float rotation = 0.0f;
                if (slot.HighlightColor.A > 0)
                {
                    rotation = (float)Math.Sin(slot.HighlightTimer * MathHelper.TwoPi) * slot.HighlightTimer * 0.3f;
                }

                Color spriteColor = sprite == item.Sprite ? item.GetSpriteColor() : item.GetInventoryIconColor();
                if (inventory != null && (inventory.Locked || inventory.slots[slotIndex].Items.All(it => it.NonInteractable || it.NonPlayerTeamInteractable))) { spriteColor *= 0.5f; }
                if (CharacterHealth.OpenHealthWindow != null && !item.UseInHealthInterface && !item.AllowedSlots.Contains(InvSlotType.HealthInterface) && item.GetComponent<GeneticMaterial>() == null)
                {
                    spriteColor = Color.Lerp(spriteColor, Color.TransparentBlack, 0.5f);
                }
                else
                {
                    sprite.Draw(spriteBatch, itemPos + Vector2.One * 2, Color.Black * 0.6f, rotate: rotation, scale: scale);
                }
                sprite.Draw(spriteBatch, itemPos, spriteColor, rotation, scale);

                if (((item.SpawnedInCurrentOutpost && !item.AllowStealing) || (inventory != null && inventory.slots[slotIndex].Items.Any(it => it.SpawnedInCurrentOutpost && !it.AllowStealing))) && CharacterInventory.LimbSlotIcons.ContainsKey(InvSlotType.LeftHand))
                {
                    var stealIcon = CharacterInventory.LimbSlotIcons[InvSlotType.LeftHand];
                    Vector2 iconSize = new Vector2(25 * GUI.Scale);
                    stealIcon.Draw(
                        spriteBatch,
                        new Vector2(rect.X + iconSize.X * 0.2f, rect.Bottom - iconSize.Y * 1.2f),
                        color: GUI.Style.Red,
                        scale: iconSize.X / stealIcon.size.X);
                }
                int maxStackSize = item.Prefab.MaxStackSize;
                if (inventory is ItemInventory itemInventory)
                {
                    maxStackSize = Math.Min(maxStackSize, itemInventory.Container.GetMaxStackSize(slotIndex));
                }
                if (maxStackSize > 1 && inventory != null)
                {
                    int itemCount = slot.MouseOn() ? inventory.slots[slotIndex].ItemCount : inventory.slots[slotIndex].Items.Where(it => !DraggingItems.Contains(it)).Count();
                    if (item.IsFullCondition || MathUtils.NearlyEqual(item.Condition, 0.0f) || itemCount > 1)
                    {
                        Vector2 stackCountPos = new Vector2(rect.Right, rect.Bottom);
                        string stackCountText = "x" + itemCount;
                        stackCountPos -= GUI.SmallFont.MeasureString(stackCountText) + new Vector2(4, 2);
                        GUI.SmallFont.DrawString(spriteBatch, stackCountText, stackCountPos + Vector2.One, Color.Black);
                        GUI.SmallFont.DrawString(spriteBatch, stackCountText, stackCountPos, Color.White);
                    }
                }
            }

            if (inventory != null &&
                !inventory.Locked &&
                Character.Controlled?.Inventory == inventory &&
                slot.InventoryKeyIndex != -1)
            {
                spriteBatch.Draw(slotHotkeySprite.Texture, rect.ScaleSize(1.15f), slotHotkeySprite.SourceRect, slotColor);
                GUI.DrawString(spriteBatch, rect.Location.ToVector2() + new Vector2((int)(4.25f * UIScale), (int)Math.Ceiling(-1.5f * UIScale)), GameMain.Config.InventoryKeyBind(slot.InventoryKeyIndex).Name, Color.Black, font: GUI.HotkeyFont);
            }
        }

        private static void DrawItemStateIndicator(
            SpriteBatch spriteBatch, Inventory inventory, 
            Sprite indicatorSprite, Sprite emptyIndicatorSprite, Rectangle containedIndicatorArea, float containedState,
            bool pulsate = false)
        {
            Color backgroundColor = GUI.Style.ColorInventoryBackground;

            if (indicatorSprite == null)
            {
                containedIndicatorArea.Inflate(0, -2);
                GUI.DrawRectangle(spriteBatch, containedIndicatorArea, backgroundColor, true);
                GUI.DrawRectangle(spriteBatch,
                    new Rectangle(containedIndicatorArea.X, containedIndicatorArea.Y, (int)(containedIndicatorArea.Width * containedState), containedIndicatorArea.Height),
                    ToolBox.GradientLerp(containedState, GUI.Style.ColorInventoryEmpty, GUI.Style.ColorInventoryHalf, GUI.Style.ColorInventoryFull) * 0.8f, true);
                GUI.DrawLine(spriteBatch,
                    new Vector2(containedIndicatorArea.X + (int)(containedIndicatorArea.Width * containedState), containedIndicatorArea.Y),
                    new Vector2(containedIndicatorArea.X + (int)(containedIndicatorArea.Width * containedState), containedIndicatorArea.Bottom),
                    Color.Black * 0.8f);
            }
            else
            {
                float indicatorScale = Math.Min(
                    containedIndicatorArea.Width / (float)indicatorSprite.SourceRect.Width,
                    containedIndicatorArea.Height / (float)indicatorSprite.SourceRect.Height);

                if (pulsate)
                {
                    indicatorScale += ((float)Math.Sin(Timing.TotalTime * 5.0f) + 1.0f) * 0.2f;
                }

                indicatorSprite.Draw(spriteBatch, containedIndicatorArea.Center.ToVector2(),
                    (inventory != null && inventory.Locked) ? backgroundColor * 0.5f : backgroundColor,
                    origin: indicatorSprite.size / 2,
                    rotate: 0.0f,
                    scale: indicatorScale);

                if (containedState > 0.0f)
                {
                    Color indicatorColor = ToolBox.GradientLerp(containedState, GUI.Style.ColorInventoryEmpty, GUI.Style.ColorInventoryHalf, GUI.Style.ColorInventoryFull);
                    if (inventory != null && inventory.Locked) { indicatorColor *= 0.5f; }

                    spriteBatch.Draw(indicatorSprite.Texture, containedIndicatorArea.Center.ToVector2(),
                        sourceRectangle: new Rectangle(indicatorSprite.SourceRect.Location, new Point((int)(indicatorSprite.SourceRect.Width * containedState), indicatorSprite.SourceRect.Height)),
                        color: indicatorColor,
                        rotation: 0.0f,
                        origin: indicatorSprite.size / 2,
                        scale: indicatorScale,
                        effects: SpriteEffects.None, layerDepth: 0.0f);

                    spriteBatch.Draw(indicatorSprite.Texture, containedIndicatorArea.Center.ToVector2(),
                        sourceRectangle: new Rectangle(indicatorSprite.SourceRect.X - 1 + (int)(indicatorSprite.SourceRect.Width * containedState), indicatorSprite.SourceRect.Y, Math.Max((int)Math.Ceiling(1 / indicatorScale), 2), indicatorSprite.SourceRect.Height),
                        color: Color.Black,
                        rotation: 0.0f,
                        origin: new Vector2(indicatorSprite.size.X * (0.5f - containedState), indicatorSprite.size.Y * 0.5f),
                        scale: indicatorScale,
                        effects: SpriteEffects.None, layerDepth: 0.0f);
                }
                else if (emptyIndicatorSprite != null)
                {
                    Color indicatorColor = GUI.Style.ColorInventoryEmptyOverlay;
                    if (inventory != null && inventory.Locked) { indicatorColor *= 0.5f; }

                    emptyIndicatorSprite.Draw(spriteBatch, containedIndicatorArea.Center.ToVector2(),
                        indicatorColor,
                        origin: emptyIndicatorSprite.size / 2,
                        rotate: 0.0f,
                        scale: indicatorScale);
                }
            }
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            UInt16 lastEventID = msg.ReadUInt16();
            byte slotCount = msg.ReadByte();
            receivedItemIDs = new List<ushort>[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                receivedItemIDs[i] = new List<ushort>();
                int itemCount = msg.ReadRangedInteger(0, MaxStackSize);
                for (int j = 0; j < itemCount; j++)
                {
                    receivedItemIDs[i].Add(msg.ReadUInt16());
                }
            }

            //delay applying the new state if less than 1 second has passed since this client last sent a state to the server
            //prevents the inventory from briefly reverting to an old state if items are moved around in quick succession

            //also delay if we're still midround syncing, some of the items in the inventory may not exist yet
            if (syncItemsDelay > 0.0f || GameMain.Client.MidRoundSyncing || NetIdUtils.IdMoreRecent(lastEventID, GameMain.Client.EntityEventManager.LastReceivedID))
            {
                if (syncItemsCoroutine != null) CoroutineManager.StopCoroutines(syncItemsCoroutine);
                syncItemsCoroutine = CoroutineManager.StartCoroutine(SyncItemsAfterDelay(lastEventID));
            }
            else
            {
                if (syncItemsCoroutine != null)
                {
                    CoroutineManager.StopCoroutines(syncItemsCoroutine);
                    syncItemsCoroutine = null;
                }
                ApplyReceivedState();
            }
        }

        private IEnumerable<CoroutineStatus> SyncItemsAfterDelay(UInt16 lastEventID)
        {
            while (syncItemsDelay > 0.0f || 
                //don't apply inventory updates until 
                //  1. MidRound syncing is done AND
                //  2. We've received all the events created before the update was written (otherwise we may not yet know about some items the server has spawned in the inventory)
                (GameMain.Client != null && (GameMain.Client.MidRoundSyncing || NetIdUtils.IdMoreRecent(lastEventID, GameMain.Client.EntityEventManager.LastReceivedID))))
            {
                if (GameMain.GameSession == null || Level.Loaded == null) 
                {
                    yield return CoroutineStatus.Success;
                }
                syncItemsDelay = Math.Max((float)(syncItemsDelay - Timing.Step), 0.0f);
                yield return CoroutineStatus.Running;
            }

            if (Owner.Removed || GameMain.Client == null)
            {
                yield return CoroutineStatus.Success;
            }

            ApplyReceivedState();

            yield return CoroutineStatus.Success;
        }

        private void ApplyReceivedState()
        {
            if (receivedItemIDs == null || (Owner != null && Owner.Removed)) { return; }

            for (int i = 0; i < capacity; i++)
            {
                foreach (Item item in slots[i].Items.ToList())
                {
                    if (!receivedItemIDs[i].Contains(item.ID))
                    {
                        item.Drop(null);
                    }
                }
            }

            //iterate backwards to get the item to the Any slots first
            for (int i = capacity - 1; i >= 0; i--)
            {
                if (!receivedItemIDs[i].Any()) { continue; }
                foreach (UInt16 id in receivedItemIDs[i])
                {
                    if (!(Entity.FindEntityByID(id) is Item item) || slots[i].Contains(item)) { continue; }
                    if (!TryPutItem(item, i, false, false, null, false))
                    {
                        ForceToSlot(item, i);
                    }
                    for (int j = 0; j < capacity; j++)
                    {
                        if (slots[j].Contains(item) && !receivedItemIDs[j].Contains(item.ID))
                        {
                            slots[j].RemoveItem(item);
                        }
                    }
                }
            }

            receivedItemIDs = null;
        }

        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            SharedWrite(msg, extraData);
            syncItemsDelay = 1.0f;
        }
    }
}
