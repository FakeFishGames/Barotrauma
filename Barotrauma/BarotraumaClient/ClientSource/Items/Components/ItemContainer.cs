using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class ItemContainer : ItemComponent, IDrawableComponent
    {
        private Sprite inventoryTopSprite;
        private Sprite inventoryBackSprite;
        private Sprite inventoryBottomSprite;

        private GUICustomComponent guiCustomComponent;

        /// <summary>
        /// Can be used to set the sprite depth individually for each contained item
        /// </summary>
        private float[] containedSpriteDepths;

        private Sprite[] slotIcons;

        public Sprite InventoryTopSprite
        {
            get { return inventoryTopSprite; }
        }
        public Sprite InventoryBackSprite
        {
            get { return inventoryBackSprite; }
        }
        public Sprite InventoryBottomSprite
        {
            get { return inventoryBottomSprite; }
        }

        public Sprite ContainedStateIndicator
        {
            get;
            private set;
        }

        public Sprite ContainedStateIndicatorEmpty
        {
            get;
            private set;
        }

        public override bool RecreateGUIOnResolutionChange => true;

        /// <summary>
        /// Depth at which the contained sprites are drawn. If not set, the original depth of the item sprites is used.
        /// </summary>
        [Serialize(-1.0f, IsPropertySaveable.No, description: "Depth at which the contained sprites are drawn. If not set, the original depth of the item sprites is used.")]
        public float ContainedSpriteDepth { get; set; }

        [Serialize(null, IsPropertySaveable.No, description: "An optional text displayed above the item's inventory.")]
        public string UILabel { get; set; }

        public GUIComponentStyle IndicatorStyle { get; set; }

        [Serialize(null, IsPropertySaveable.No)]
        public string ContainedStateIndicatorStyle { get; set; }

        [Serialize(-1, IsPropertySaveable.No, description: "Can be used to make the contained state indicator display the condition of the item in a specific slot even when the container's capacity is more than 1.")]
        public int ContainedStateIndicatorSlot { get; set; }

        [Serialize(true, IsPropertySaveable.No, description: "Should an indicator displaying the state of the contained items be displayed on this item's inventory slot. "+
                                                             "If this item can only contain one item, the indicator will display the condition of the contained item, otherwise it will indicate how full the item is.")]
        public bool ShowContainedStateIndicator { get; set; }

        [Serialize(false, IsPropertySaveable.No, description: "If enabled, the condition of this item is displayed in the indicator that would normally show the state of the contained items." +
            " May be useful for items such as ammo boxes and magazines that spawn projectiles as needed," +
            " and use the condition to determine how many projectiles can be spawned in total.")]
        public bool ShowConditionInContainedStateIndicator
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.No, description: "If true, the contained state indicator calculates how full the item is based on the total amount of items that can be stacked inside it, as opposed to how many of the inventory slots are occupied." +
                                                              " Note that only items in the main container or in the subcontainer are counted, depending on which container the first containable item match is found in. The item determining this can be defined with ContainedStateIndicatorSlot")]
        public bool ShowTotalStackCapacityInContainedStateIndicator { get; set; }

        [Serialize(false, IsPropertySaveable.No, description: "Should the inventory of this item be kept open when the item is equipped by a character.")]
        public bool KeepOpenWhenEquipped { get; set; }

        [Serialize(false, IsPropertySaveable.No, description: "Can the inventory of this item be moved around on the screen by the player.")]
        public bool MovableFrame { get; set; }

        public Vector2 DrawSize
        {
            //use the extents of the item as the draw size
            get { return Vector2.Zero; }
        }

        partial void InitProjSpecific(ContentXElement element)
        {
            slotIcons = new Sprite[capacity];

            int currCapacity = MainContainerCapacity;
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "topsprite":
                        inventoryTopSprite = new Sprite(subElement);
                        break;
                    case "backsprite":
                        inventoryBackSprite = new Sprite(subElement);
                        break;
                    case "bottomsprite":
                        inventoryBottomSprite = new Sprite(subElement);
                        break;
                    case "containedstateindicator":
                        ContainedStateIndicator = new Sprite(subElement);
                        break;
                    case "containedstateindicatorempty":
                        ContainedStateIndicatorEmpty = new Sprite(subElement);
                        break;
                    case "sloticon":
                        int index = subElement.GetAttributeInt("slotindex", -1);
                        Sprite icon = new Sprite(subElement);
                        for (int i = 0; i < capacity; i++)
                        {
                            if (i == index || index == -1)
                            {
                                slotIcons[i] = icon;
                            }
                        }
                        break;
                    case "subcontainer":
                        int subContainerCapacity = subElement.GetAttributeInt("capacity", 1);
                        var slotIconElement = subElement.GetChildElement("sloticon");
                        if (slotIconElement != null)
                        {
                            var slotIcon = new Sprite(slotIconElement);
                            for (int i = currCapacity; i < currCapacity + subContainerCapacity; i++)
                            {
                                slotIcons[i] = slotIcon;                                
                            }
                        }
                        currCapacity += subContainerCapacity;
                        break;
                }
            }

            if (string.IsNullOrEmpty(ContainedStateIndicatorStyle))
            {
                //if neither a style or a custom sprite is defined, use default style
                if (ContainedStateIndicator == null)
                {
                    IndicatorStyle = GUIStyle.GetComponentStyle("ContainedStateIndicator.Default");
                }
            }
            else
            {
                IndicatorStyle = GUIStyle.GetComponentStyle("ContainedStateIndicator." + ContainedStateIndicatorStyle);
                if (ContainedStateIndicator != null || ContainedStateIndicatorEmpty != null)
                {
                    DebugConsole.AddWarning($"Item \"{item.Name}\" defines both a contained state indicator style and a custom indicator sprite. Will use the custom sprite...",
                        contentPackage: item.Prefab.ContentPackage);
                }
            }
            if (GuiFrame == null)
            {
                //if a GUIFrame is not defined in the xml, 
                //we create a full-screen frame and let the inventory position itself on it
                GuiFrame = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas), style: null)
                {
                    CanBeFocused = false
                };
                guiCustomComponent = new GUICustomComponent(new RectTransform(Vector2.One, GuiFrame.RectTransform),
                    onDraw: (SpriteBatch spriteBatch, GUICustomComponent component) => { Inventory.Draw(spriteBatch); },
                    onUpdate: null)
                {
                    CanBeFocused = false
                };
                GuiFrame.RectTransform.ParentChanged += OnGUIParentChanged;
            }
            else
            {
                //if a GUIFrame has been defined, draw the inventory inside it
                CreateGUI();
            }

            containedSpriteDepths = element.GetAttributeFloatArray("containedspritedepths", Array.Empty<float>());
        }

        protected override void CreateGUI()
        {
            var content = new GUIFrame(new RectTransform(GuiFrame.Rect.Size - GUIStyle.ItemFrameMargin, GuiFrame.RectTransform, Anchor.Center) { AbsoluteOffset = GUIStyle.ItemFrameOffset },
                style: null)
            {
                CanBeFocused = false
            };

            LocalizedString labelText = GetUILabel();
            GUITextBlock label = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform, Anchor.TopCenter),
                labelText, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.CenterLeft, wrap: true)
                {
                    IgnoreLayoutGroups = true
                };
            
            int buttonSize = GUIStyle.ItemFrameTopBarHeight;
            Point margin = new Point(buttonSize / 4, buttonSize / 6);

            GUILayoutGroup buttonArea = new GUILayoutGroup(new RectTransform(new Point(content.Rect.Width, buttonSize - margin.Y * 2), content.RectTransform, Anchor.TopRight) { AbsoluteOffset = new Point(0, margin.Y) }, 
                isHorizontal: true, childAnchor: Anchor.TopRight)
            {
                AbsoluteSpacing = margin.X / 2
            };
            if (Inventory.Capacity > 1)
            {
                new GUIButton(new RectTransform(Vector2.One, buttonArea.RectTransform, scaleBasis: ScaleBasis.Smallest), style: "SortItemsButton")
                {
                    ToolTip = TextManager.Get("SortItemsAlphabetically"),
                    OnClicked = (btn, userdata) =>
                    {
                        SortItems();
                        return true;
                    }
                };
                new GUIButton(new RectTransform(Vector2.One, buttonArea.RectTransform, scaleBasis: ScaleBasis.Smallest), style: "MergeStacksButton")
                {
                    ToolTip = TextManager.Get("MergeItemStacks"),
                    OnClicked = (btn, userdata) =>
                    {
                        MergeStacks();
                        return true;
                    }
                };
            }

            float minInventoryAreaSize = 0.5f;
            guiCustomComponent = new GUICustomComponent(
                new RectTransform(new Vector2(1.0f, label == null ? 1.0f : Math.Max(1.0f - label.RectTransform.RelativeSize.Y, minInventoryAreaSize)), content.RectTransform, Anchor.BottomCenter),
                onDraw: (SpriteBatch spriteBatch, GUICustomComponent component) => { Inventory.Draw(spriteBatch); },
                onUpdate: null)
            {
                CanBeFocused = true
            };

            // Expand the frame vertically if it's too small to fit the text
            if (label != null && label.RectTransform.RelativeSize.Y > 0.5f)
            {
                int newHeight = (int)(GuiFrame.Rect.Height + (2 * (label.RectTransform.RelativeSize.Y - 0.5f) * content.Rect.Height));
                if (newHeight > GuiFrame.RectTransform.MaxSize.Y)
                {
                    Point newMaxSize = GuiFrame.RectTransform.MaxSize;
                    newMaxSize.Y = newHeight;
                    GuiFrame.RectTransform.MaxSize = newMaxSize;
                }
                GuiFrame.RectTransform.Resize(new Point(GuiFrame.Rect.Width, newHeight));
                content.RectTransform.Resize(GuiFrame.Rect.Size - GUIStyle.ItemFrameMargin);
                label.CalculateHeightFromText();
                guiCustomComponent.RectTransform.Resize(new Vector2(1.0f, Math.Max(1.0f - label.RectTransform.RelativeSize.Y, minInventoryAreaSize)));
            }

            Inventory.RectTransform = guiCustomComponent.RectTransform;
        }

        private void SortItems()
        {
            List<List<Item>> itemsPerSlot = new List<List<Item>>();

            for (int i = 0; i < Inventory.Capacity; i++)
            {
                var items = Inventory.GetItemsAt(i).ToList();
                if (items.Any()) 
                { 
                    itemsPerSlot.Add(items);
                    items.ForEach(it => it.Drop(dropper: null, createNetworkEvent: false, setTransform: false));
                }
            }

            var sortedItems = itemsPerSlot
                .OrderBy(i => i.First().Name)
                //if there's multiple items with the same name, sort largest stacks first
                .ThenByDescending(i => i.Count)
                //same name and stack size, sort items with most items inside first
                .ThenByDescending(i => i.First().ContainedItems.Count());

            foreach (var items in sortedItems)
            {
                int firstFreeSlot = -1;
                for (int i = 0; i < Inventory.Capacity; i++)
                {
                    if (Inventory.GetItemAt(i) == null && Inventory.CanBePut(items.First()))
                    {
                        firstFreeSlot = i;
                        break;
                    }
                }
                if (firstFreeSlot == -1) 
                { 
                    items.ForEach(it => it.Drop(dropper: null));
                    continue; 
                }
                foreach (var item in items)
                {
                   if (!Inventory.TryPutItem(item, firstFreeSlot, allowSwapping: false, allowCombine: false, user: null, createNetworkEvent: false))
                    {
                        //if putting in the specific slot fails (prevented by containable restrictions?), just put in the first free slot
                        if (!Inventory.TryPutItem(item, user: null, createNetworkEvent: false))
                        {
                            item.Drop(dropper: null);
                        }
                    }
                }
            }
            Inventory.CreateNetworkEvent();
        }

        private void MergeStacks()
        {
            for (int i = Inventory.Capacity - 1; i >= 0; i--)
            {
                var items = Inventory.GetItemsAt(i).ToList();
                if (items.None()) { continue; }
                //find the first stack we can put the item in
                for (int j = 0; j < i; j++)
                {
                    if (Inventory.GetItemsAt(j).Any() && Inventory.CanBePutInSlot(items.First(), j))
                    {
                        items.ForEach(it => Inventory.TryPutItem(it, j, allowSwapping: false, allowCombine: false, user: null, createNetworkEvent: false));
                        break;
                    }
                }
            }
            Inventory.CreateNetworkEvent();
        }

        public LocalizedString GetUILabel()
        {
            if (UILabel == string.Empty) { return string.Empty; }
            if (UILabel != null)
            {
                return TextManager.Get("UILabel." + UILabel).Fallback(TextManager.Get(UILabel));
            }
            else
            {
                return item?.Prefab.Name;
            }            
        }

        public Sprite GetSlotIcon(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= slotIcons.Length) { return null; }
            return slotIcons[slotIndex];
        }

        public bool KeepOpenWhenEquippedBy(Character character)
        {
            if (!KeepOpenWhenEquipped ||
                !character.HasEquippedItem(Item) ||
                !character.CanAccessInventory(Inventory))
            {
                return false;
            }

            //if holding 2 different "always open" items in different hands, don't force them to stay open
            if (character.HeldItems.Count() > 1 && character.HeldItems.All(it => it.GetComponent<ItemContainer>()?.KeepOpenWhenEquipped ?? false))
            {
                return false;
            }

            return true;
        }


        public float GetContainedIndicatorState()
        {
            if (ShowConditionInContainedStateIndicator)
            {
                return item.Condition / item.MaxCondition;
            }

            int targetSlot = Math.Max(ContainedStateIndicatorSlot, 0);
            if (targetSlot >= Inventory.Capacity) { return 0.0f; }

            var containedItems = Inventory.GetItemsAt(targetSlot);            
            if (containedItems == null) { return 0.0f; }
            
            Item containedItem = containedItems.FirstOrDefault();
            if (ShowTotalStackCapacityInContainedStateIndicator)
            {
                // No item on the defined slot, check if the items on other slots can be used.
                containedItem ??= 
                    containedItems.FirstOrDefault() ?? 
                    Inventory.AllItems.FirstOrDefault(it => CanBeContained(it, targetSlot));
                if (containedItem == null) { return 0.0f; }
                
                int ignoredItemCount = 0;
                var subContainableItems = AllSubContainableItems;
                float targetSlotCapacity = Math.Min(containedItem.Prefab.MaxStackSize, GetMaxStackSize(targetSlot));
                float capacity = targetSlotCapacity * MainContainerCapacity;
                if (subContainableItems != null)
                {
                    bool useMainContainerCapacity = true;
                    foreach (Item it in Inventory.AllItems)
                    {
                        // Ignore all items in the sub containers.
                        foreach (RelatedItem ri in subContainableItems)
                        {
                            if (ri.MatchesItem(containedItem))
                            {
                                // The target item is in a subcontainer -> inverse the logic.
                                useMainContainerCapacity = false;
                                break;
                            }
                            if (ri.MatchesItem(it))
                            {
                                ignoredItemCount++;
                            }
                        }
                        if (!useMainContainerCapacity) { break; }
                    }
                    if (!useMainContainerCapacity)
                    {
                        // Ignore all items in the main container.
                        ignoredItemCount = Inventory.AllItems.Count(it => subContainableItems.Any(ri => !ri.MatchesItem(it)));
                        capacity = targetSlotCapacity * (Capacity - MainContainerCapacity);
                    }
                }
                int itemCount = Inventory.AllItems.Count() - ignoredItemCount;
                return Math.Min(itemCount / Math.Max(capacity, 1), 1);                
            }

            //display the state of an item in a specific slot
            if (Inventory.Capacity == 1 || ContainedStateIndicatorSlot > -1)
            {
                if (containedItem == null) { return 0.0f; }
                //if the contained item has some contained state indicator, show that
                if (containedItem.GetComponent<ItemContainer>() is { ShowContainedStateIndicator: true } containedItemContainer)
                {
                    return containedItemContainer.GetContainedIndicatorState();
                }
                int maxStackSize = Math.Min(containedItem.Prefab.GetMaxStackSize(Inventory), GetMaxStackSize(targetSlot));
                if (maxStackSize == 1)
                {
                    return containedItem.Condition / containedItem.MaxCondition;
                }
                return containedItems.Count() / (float)maxStackSize;                    
            }
            else
            {
                return Inventory.EmptySlotCount / (float)Inventory.Capacity;
            }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing = false, float itemDepth = -1, Color? overrideColor = null)
        {
            if (hideItems || (item.body != null && !item.body.Enabled)) { return; }
            DrawContainedItems(spriteBatch, itemDepth, overrideColor);
        }

        public void DrawContainedItems(SpriteBatch spriteBatch, float itemDepth, Color? overrideColor = null)
        {
            Vector2 transformedItemPos = ItemPos * item.Scale;
            Vector2 transformedItemInterval = ItemInterval * item.Scale;
            Vector2 transformedItemIntervalHorizontal = new Vector2(transformedItemInterval.X, 0.0f);
            Vector2 transformedItemIntervalVertical = new Vector2(0.0f, transformedItemInterval.Y);

            if (item.body == null)
            {
                if (item.FlippedX)
                {
                    transformedItemPos.X = -transformedItemPos.X;
                    transformedItemPos.X += item.Rect.Width;
                    transformedItemInterval.X = -transformedItemInterval.X;
                    transformedItemIntervalHorizontal.X = -transformedItemIntervalHorizontal.X;
                }
                if (item.FlippedY)
                {
                    transformedItemPos.Y = -transformedItemPos.Y;
                    transformedItemPos.Y -= item.Rect.Height;
                    transformedItemInterval.Y = -transformedItemInterval.Y;
                    transformedItemIntervalVertical.Y = -transformedItemIntervalVertical.Y;
                }
                transformedItemPos += new Vector2(item.Rect.X, item.Rect.Y);
                if (item.Submarine != null) { transformedItemPos += item.Submarine.DrawPosition; }

                if (Math.Abs(item.RotationRad) > 0.01f)
                {
                    Matrix transform = Matrix.CreateRotationZ(-item.RotationRad);
                    transformedItemPos = Vector2.Transform(transformedItemPos - item.DrawPosition, transform) + item.DrawPosition;
                    transformedItemInterval = Vector2.Transform(transformedItemInterval, transform);
                    transformedItemIntervalHorizontal = Vector2.Transform(transformedItemIntervalHorizontal, transform);
                    transformedItemIntervalVertical = Vector2.Transform(transformedItemIntervalVertical, transform);
                }
            }
            else
            {
                Matrix transform = Matrix.CreateRotationZ(item.body.DrawRotation);
                if (item.body.Dir == -1.0f)
                {
                    transformedItemPos.X = -transformedItemPos.X;
                    transformedItemInterval.X = -transformedItemInterval.X;
                    transformedItemIntervalHorizontal.X = -transformedItemIntervalHorizontal.X;
                }

                transformedItemPos = Vector2.Transform(transformedItemPos, transform);
                transformedItemInterval = Vector2.Transform(transformedItemInterval, transform);
                transformedItemIntervalHorizontal = Vector2.Transform(transformedItemIntervalHorizontal, transform);
                transformedItemPos += item.body.DrawPosition;
            }

            Vector2 currentItemPos = transformedItemPos;

            bool isWiringMode = SubEditorScreen.TransparentWiringMode && SubEditorScreen.IsWiringMode();

            int i = 0;
            foreach (ContainedItem contained in containedItems)
            {
                Vector2 itemPos = currentItemPos;

                if (contained.Item?.Sprite == null) { continue; }

                if (contained.Hide) { continue; }
                if (contained.ItemPos.HasValue)
                {
                    Vector2 pos = contained.ItemPos.Value;
                    if (item.body != null)
                    {
                        Matrix transform = Matrix.CreateRotationZ(item.body.DrawRotation);
                        pos.X *= item.body.Dir;
                        itemPos = Vector2.Transform(pos, transform) + item.body.DrawPosition;
                    }
                    else
                    {
                        itemPos = pos;
                        // This code is aped based on above. Not tested.
                        if (item.FlippedX)
                        {
                            itemPos.X = -itemPos.X;
                            itemPos.X += item.Rect.Width;
                        }
                        if (item.FlippedY)
                        {
                            itemPos.Y = -itemPos.Y;
                            itemPos.Y -= item.Rect.Height;
                        }
                        itemPos += new Vector2(item.Rect.X, item.Rect.Y);
                        if (item.Submarine != null)
                        {
                            itemPos += item.Submarine.DrawPosition;
                        }
                        if (Math.Abs(item.RotationRad) > 0.01f)
                        {
                            Matrix transform = Matrix.CreateRotationZ(-item.RotationRad);
                            itemPos = Vector2.Transform(itemPos - item.DrawPosition, transform) + item.DrawPosition;
                        }
                    }
                }
                
                if (AutoInteractWithContained)
                {
                    contained.Item.IsHighlighted = item.IsHighlighted;
                    item.IsHighlighted = false;
                }

                Vector2 origin = contained.Item.Sprite.Origin;
                if (item.FlippedX) { origin.X = contained.Item.Sprite.SourceRect.Width - origin.X; }
                if (item.FlippedY) { origin.Y = contained.Item.Sprite.SourceRect.Height - origin.Y; }

                float containedSpriteDepth = ContainedSpriteDepth < 0.0f ? contained.Item.Sprite.Depth : ContainedSpriteDepth;
                if (i < containedSpriteDepths.Length)
                {
                    containedSpriteDepth = containedSpriteDepths[i];
                }
                containedSpriteDepth = itemDepth + (containedSpriteDepth - (item.Sprite?.Depth ?? item.SpriteDepth)) / 10000.0f;

                SpriteEffects spriteEffects = SpriteEffects.None;
                float spriteRotation = ItemRotation;
                if (contained.Rotation != 0)
                {
                    spriteRotation = contained.Rotation;
                }
                bool flipX = (item.body != null && item.body.Dir == -1) || item.FlippedX;
                if (flipX)
                {
                    spriteEffects |= MathUtils.NearlyEqual(spriteRotation % 180, 90.0f) ? SpriteEffects.FlipVertically : SpriteEffects.FlipHorizontally;
                }
                bool flipY = item.FlippedY;
                if (flipY)
                {
                    spriteEffects |= MathUtils.NearlyEqual(spriteRotation % 180, 90.0f) ? SpriteEffects.FlipHorizontally : SpriteEffects.FlipVertically;
                }

                contained.Item.Sprite.Draw(
                    spriteBatch,
                    new Vector2(itemPos.X, -itemPos.Y),
                    overrideColor ?? (isWiringMode ? contained.Item.GetSpriteColor(withHighlight: true) * 0.15f : contained.Item.GetSpriteColor(withHighlight: true)),
                    origin,
                    -(contained.Item.body == null ? 0.0f : contained.Item.body.DrawRotation),
                    contained.Item.Scale,
                    spriteEffects,
                    depth: containedSpriteDepth);
                contained.Item.DrawDecorativeSprites(spriteBatch, itemPos, flipX,flipY, (contained.Item.body == null ? 0.0f : contained.Item.body.DrawRotation), 
                    containedSpriteDepth, overrideColor);

                foreach (ItemContainer ic in contained.Item.GetComponents<ItemContainer>())
                {
                    if (ic.hideItems) { continue; }
                    ic.DrawContainedItems(spriteBatch, containedSpriteDepth, overrideColor);
                }

                i++;
                if (Math.Abs(ItemInterval.X) > 0.001f && Math.Abs(ItemInterval.Y) > 0.001f)
                {
                    //interval set on both axes -> use a grid layout
                    currentItemPos += transformedItemIntervalHorizontal;
                    if (i % ItemsPerRow == 0)
                    {
                        currentItemPos = transformedItemPos;
                        currentItemPos += transformedItemIntervalVertical * (i / ItemsPerRow);
                    }
                }
                else
                {
                    currentItemPos += transformedItemInterval;
                }
            }
        }

        public override void UpdateHUDComponentSpecific(Character character, float deltaTime, Camera cam)
        {
            if (!item.IsInteractable(character)) { return; }
            if (Inventory.RectTransform != null)
            {
                guiCustomComponent.RectTransform.Parent = Inventory.RectTransform;
            }

            if (item.ParentInventory?.Owner == character && character.SelectedItem == item)
            {
                character.SelectedItem = null;
            }

            //if the item is in the character's inventory, no need to update the item's inventory 
            //because the player can see it by hovering the cursor over the item        
            guiCustomComponent.Visible = DrawInventory && (item.ParentInventory?.Owner != character || Inventory.DrawWhenEquipped);
            if (!guiCustomComponent.Visible) { return; }           

            Inventory.Update(deltaTime, cam);
        }
    }
}
