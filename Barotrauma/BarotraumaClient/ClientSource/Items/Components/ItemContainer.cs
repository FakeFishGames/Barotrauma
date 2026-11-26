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

        // The container type text, the tags version and the tag editing permission the current GUI was created with, see RefreshGUIIfOutdated
        private string containerTypeTextInGUI = string.Empty;
        private int tagsVersionInGUI = -1;
        private bool canChangeTagsInGUI;

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
            GUITextBlock label = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform, Anchor.TopLeft),
                labelText, font: GUIStyle.SubHeadingFont, textAlignment: Alignment.TopLeft, wrap: true)
                {
                    IgnoreLayoutGroups = true
                };
            
            //show what kind of container this is (based on the container tag) underneath the label
            //(the text is fixed until RefreshGUIIfOutdated rebuilds the GUI if any of the tags change)
            LocalizedString containerTypeText = GetContainerTypeText();
            containerTypeTextInGUI = containerTypeText?.Value ?? string.Empty;
            tagsVersionInGUI = item.TagsVersion;
            GUITextBlock containerTypeBlock = null;
            if (!containerTypeText.IsNullOrEmpty())
            {
                containerTypeBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), label.RectTransform, Anchor.BottomLeft, Pivot.TopLeft),
                    containerTypeText, font: GUIStyle.SmallFont, textColor: GUIStyle.TextColorDim, textAlignment: Alignment.TopLeft, wrap: true)
                {
                    IgnoreLayoutGroups = true,
                    CanBeFocused = false
                };
                
                RichString containerTypeDescription = null;
                var matchingTags = ContainerTagPrefab.GetDistinctDisplayedTags(Item);
                // If there's just one description, show it without the tag heading because it's displayed on the window already
                if (matchingTags.Count == 1)
                {
                    if (!matchingTags[0].Description.IsNullOrEmpty())
                    {
                        
                        containerTypeDescription = matchingTags[0].Description;
                    }
                }
                // If there's more than one description, show them as a list with the tags as subheadings
                if (matchingTags.Count > 1)
                {
                    // Note: Tags without a description text are listed by name only
                    var entries = matchingTags.Select(t => LocalizedString.JoinIfNotNullOrEmpty("\n",
                        "‖color:gui.textcolordim‖" + t.BaseName + "‖end‖",
                        t.Description));
                    containerTypeDescription = RichString.Rich(LocalizedString.Join("\n", entries));
                }
                if (!containerTypeDescription.IsNullOrEmpty())
                {
                    containerTypeBlock.CanBeFocused = true;
                    containerTypeBlock.ToolTip = containerTypeDescription;
                }
            }

            // If the words of the text would get broken up, make the frame wider instead
            float typeTextLongestWordWidth = containerTypeBlock == null ?
                0.0f :
                ToolBox.GetLongestWordWidth(containerTypeBlock.Text.SanitizedString.Value, containerTypeBlock.Font);
            int longestWordWidth = (int)Math.Floor(Math.Max(
                ToolBox.GetLongestWordWidth(label.Text.SanitizedString.Value, label.Font),
                typeTextLongestWordWidth))
                                   + 1; // it needs to be wider, not just matching, to not break
            int availableTextWidth = label.Rect.Width - (int)Math.Ceiling(label.Padding.X + label.Padding.Z);
            if (longestWordWidth > availableTextWidth)
            {
                int newWidth = GuiFrame.Rect.Width + longestWordWidth - availableTextWidth;
                if (newWidth > GuiFrame.RectTransform.MaxSize.X)
                {
                    Point newMaxSize = GuiFrame.RectTransform.MaxSize;
                    newMaxSize.X = newWidth;
                    GuiFrame.RectTransform.MaxSize = newMaxSize;
                }
                GuiFrame.RectTransform.MinSize = new Point(Math.Max(GuiFrame.RectTransform.MinSize.X, newWidth), GuiFrame.RectTransform.MinSize.Y);
                GuiFrame.RectTransform.Resize(new Point(newWidth, GuiFrame.Rect.Height));
                content.RectTransform.Resize(GuiFrame.Rect.Size - GUIStyle.ItemFrameMargin);
                label.CalculateHeightFromText();
                containerTypeBlock?.CalculateHeightFromText();
            }

            //the container type text is drawn below the label, so it needs to be included in the height of the label area
            float GetLabelAreaRelativeHeight()
            {
                float height = label.RectTransform.RelativeSize.Y;
                // The label can be offset below the buttons
                height += label.RectTransform.AbsoluteOffset.Y / (float)Math.Max(content.Rect.Height, 1);
                if (containerTypeBlock != null)
                {
                    height += containerTypeBlock.Rect.Height / (float)Math.Max(content.Rect.Height, 1);
                }
                return height;
            }

            int buttonSize = GUIStyle.ItemFrameTopBarHeight;
            Point margin = new Point(buttonSize / 4, buttonSize / 6);

            int buttonCount = 0;

            GUILayoutGroup buttonArea = new GUILayoutGroup(new RectTransform(new Point(content.Rect.Width, buttonSize - margin.Y * 2), content.RectTransform, Anchor.TopRight) { AbsoluteOffset = new Point(0, margin.Y) }, 
                isHorizontal: true, childAnchor: Anchor.TopRight)
            {
                AbsoluteSpacing = margin.X / 2
            };
            if (Inventory.Capacity > 1)
            {
                if (ShowSortButton)
                {
                    buttonCount++;
                    new GUIButton(new RectTransform(Vector2.One, buttonArea.RectTransform, scaleBasis: ScaleBasis.Smallest), style: "SortItemsButton")
                    {
                        ToolTip = TextManager.Get("SortItemsAlphabetically"),
                        OnClicked = (btn, userdata) =>
                        {
                            SortItems();
                            return true;
                        }
                    };
                }
                if (ShowMergeButton)
                {
                    buttonCount++;
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
                // Only created when the tags can be changed, so the button doesn't take up space from the label otherwise
                // (that depends on the sub the item is in, which is not known yet when the GUI is first created, see RefreshGUIIfOutdated)
                if (ShowContainerType && canChangeTagsInGUI)
                {
                    buttonCount++;
                    new GUIButton(new RectTransform(Vector2.One, buttonArea.RectTransform, scaleBasis: ScaleBasis.Smallest),
                        style: "EditContainerTagsButton")
                    {
                        ToolTip = TextManager.Get("containertags.edit"),
                        OnClicked = (btn, userdata) =>
                        {
                            CreateTagSelectionPopup();
                            return true;
                        }
                    };
                }
            }

            if (buttonCount > 0)
            {
                int widthBesideButtons = label.Parent.Rect.Width - buttonCount * buttonSize;
                bool labelFitsOnOneLineBesideButtons =
                    !label.WrappedText.Value.Contains('\n') &&
                    label.TextSize.X + label.Padding.X + label.Padding.Z < widthBesideButtons;
                // The container type text is as wide as the label, so its words need to fit beside the buttons too
                bool typeTextFitsBesideButtons =
                    containerTypeBlock == null ||
                    typeTextLongestWordWidth + containerTypeBlock.Padding.X + containerTypeBlock.Padding.Z < widthBesideButtons;
                if (labelFitsOnOneLineBesideButtons && typeTextFitsBesideButtons)
                {
                    label.RectTransform.MaxSize = new Point(widthBesideButtons, int.MaxValue);
                }
                else
                {
                    // If the label doesn't fit neatly on the same line with the buttons, move it below them.
                    // This looks better especially with single-column containers, rather than just pushing them wider to fit everything. 
                    label.RectTransform.AbsoluteOffset = new Point(0, buttonSize);
                }
                containerTypeBlock?.CalculateHeightFromText();
            }

            float minInventoryAreaSize = 0.5f;
            guiCustomComponent = new GUICustomComponent(
                new RectTransform(new Vector2(1.0f, label == null ? 1.0f : Math.Max(1.0f - GetLabelAreaRelativeHeight(), minInventoryAreaSize)), content.RectTransform, Anchor.BottomCenter),
                onDraw: (SpriteBatch spriteBatch, GUICustomComponent component) => { Inventory.Draw(spriteBatch); },
                onUpdate: null)
            {
                CanBeFocused = true
            };

            // Expand the frame vertically if it's too small to fit the text
            if (label != null && GetLabelAreaRelativeHeight() > 0.5f)
            {
                int newHeight = (int)(GuiFrame.Rect.Height + (2 * (GetLabelAreaRelativeHeight() - 0.5f) * content.Rect.Height));
                if (newHeight > GuiFrame.RectTransform.MaxSize.Y)
                {
                    Point newMaxSize = GuiFrame.RectTransform.MaxSize;
                    newMaxSize.Y = newHeight;
                    GuiFrame.RectTransform.MaxSize = newMaxSize;
                }
                GuiFrame.RectTransform.MinSize = new Point(GuiFrame.RectTransform.MinSize.X, Math.Max(GuiFrame.RectTransform.MinSize.Y, newHeight));
                GuiFrame.RectTransform.Resize(new Point(GuiFrame.Rect.Width, newHeight));
                content.RectTransform.Resize(GuiFrame.Rect.Size - GUIStyle.ItemFrameMargin);
                label.CalculateHeightFromText();
                containerTypeBlock?.CalculateHeightFromText();
                guiCustomComponent.RectTransform.Resize(new Vector2(1.0f, Math.Max(1.0f - GetLabelAreaRelativeHeight(), minInventoryAreaSize)));
            }

            Inventory.RectTransform = guiCustomComponent.RectTransform;
        }

        /// <summary>
        /// Lets the player pick the container tags of the item among the tags available for the sub/outpost it's in.
        /// </summary>
        private void CreateTagSelectionPopup()
        {
            var selectableTags = ContainerTagPrefab.GetSelectableTags(item).ToList();
            if (selectableTags.Count == 0) { return; }

            // Tags the item has that can't be changed here (ie. tags defined in the item prefab, tags of other categories etc.)
            var lockedTags = ContainerTagPrefab.GetDisplayedTags(item)
                .Where(t => item.Prefab.Tags.Contains(t.Identifier) || !selectableTags.Contains(t))
                .ToList();
            // One row per tag, including locked tags (a tag can be both selectable and locked, eg. if it's defined in the item prefab)
            var tagRows = selectableTags
                .Concat(lockedTags)
                .Distinct()
                .OrderBy(t => t.BaseName.Value)
                .ToList();

            var msgBox = new GUIMessageBox(
                headerText: TextManager.Get("containertags.edit"),
                text: TextManager.Get("containertags.edit.description"),
                buttons: new LocalizedString[] { TextManager.Get("Ok"), TextManager.Get("Cancel") },
                relativeSize: new Vector2(0.25f, 0.5f), minSize: new Point(400, 400));

            //the list takes up whatever height is left after the header, the description and the buttons
            //(the button container is drawn over the bottom of the content instead of being part of the layout)
            int buttonAreaHeight = msgBox.Buttons[0].Parent.Rect.Height;
            int descriptionHeight = msgBox.Text == null ? 0 : msgBox.Text.Rect.Height + msgBox.Content.AbsoluteSpacing;
            int listHeight = msgBox.Content.Rect.Height - msgBox.Header.Rect.Height - descriptionHeight - buttonAreaHeight - msgBox.Content.AbsoluteSpacing * 2;
            var list = new GUIListBox(new RectTransform(new Point(msgBox.Content.Rect.Width, listHeight), msgBox.Content.RectTransform));
            var selectedTags = new HashSet<Identifier>();
            foreach (ContainerTagPrefab tag in tagRows)
            {
                bool hasTag = item.HasTag(tag.Identifier);
                if (hasTag) { selectedTags.Add(tag.Identifier); }
                Identifier tagIdentifier = tag.Identifier;
                bool isLocked = lockedTags.Contains(tag);
                var tagTickBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.1f), list.Content.RectTransform),
                    tag.BaseName,
                    font: GUIStyle.SmallFont)
                {
                    Selected = hasTag || isLocked,
                    Enabled = !isLocked,
                    ToolTip = tag.Description,
                    OnSelected = (tickBox) =>
                    {
                        if (tickBox.Selected) { selectedTags.Add(tagIdentifier); } else { selectedTags.Remove(tagIdentifier); }
                        return true;
                    }
                };
                // Rare, but tags can share a name (or same translation for two tags), in those cases show the tag identifier to tell them apart
                if (tagRows.Count(t => t.BaseName.Value == tag.BaseName.Value) > 1)
                {
                    string dimmedIdentifier = $"‖color:gui.textcolordim‖({tag.Identifier})‖end‖";
                    tagTickBox.TextBlock.Text = RichString.Rich($"{tag.BaseName} {dimmedIdentifier}");
                    tagTickBox.ToolTip = RichString.Rich(tag.Description.IsNullOrEmpty() ? dimmedIdentifier : $"{tag.Description}\n{dimmedIdentifier}");
                }
            }

            new GUICustomComponent(new RectTransform(Vector2.Zero, msgBox.InnerFrame.RectTransform),
                onUpdate: (_, _) =>
                {
                    // Close the popup if the character dies, walks away etc.
                    if (!ContainerTagPrefab.CanChangeTags(item))
                    {
                        msgBox.Close();
                    }
                })
            {
                IgnoreLayoutGroups = true,
                CanBeFocused = false
            };

            msgBox.Buttons[0].OnClicked = (btn, userdata) =>
            {
                // Rechecked so changes the server would reject don't get applied locally
                if (ContainerTagPrefab.CanChangeTags(item))
                {
                    ContainerTagPrefab.SetTags(item, selectedTags);
                }
                msgBox.Close();
                return true;
            };
            msgBox.Buttons[1].OnClicked = (btn, userdata) =>
            {
                msgBox.Close();
                return true;
            };
        }

        /// <summary>
        /// Text describing what kind of container this is, shown under the label. Null if it shouldn't be shown.
        /// </summary>
        private LocalizedString GetContainerTypeText()
        {
            if (!ShowContainerType || GetUILabel().IsNullOrEmpty()) { return null; }
            return ContainerTagPrefab.GetContainerTypeText(this);
        }

        /// <summary>
        /// Recreates the GUI if the item's tags have changed the container type text, or if the character's permission
        /// to change the tags has changed, since the GUI was created.
        /// The layout depends on the height of the text and the number of buttons, so they can't just be swapped in place.
        /// Needed e.g. because the tags and the sub of an item loaded from a submarine file are only set after the GUI has been created.
        /// </summary>
        private void RefreshGUIIfOutdated(Character character)
        {
            // No GUIFrame defined in the XML = CreateGUI isn't used and there's no label to show the text under
            if (GuiFrameSource == null) { return; }

            bool canChangeTags = ContainerTagPrefab.CanChangeTags(item);
            if (item.TagsVersion == tagsVersionInGUI &&
                canChangeTags == canChangeTagsInGUI) { return; }

            tagsVersionInGUI = item.TagsVersion;
            if ((GetContainerTypeText()?.Value ?? string.Empty) == containerTypeTextInGUI &&
                canChangeTags == canChangeTagsInGUI) { return; }

            canChangeTagsInGUI = canChangeTags;
            ReloadGuiFrame();
            CreateGUI();
            // The new frame starts from the default layout, so position it again like when the item's HUDs are opened
            if (UseAlternativeLayout) { AlternativeLayout?.ApplyTo(GuiFrame.RectTransform); }
            (character?.SelectedItem ?? item).SetHUDLayout();
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
            var rootBody = item.RootContainer?.body ?? item.body;

            Vector2 transformedItemPos = GetContainedPosition(
                drawPosition: true,
                out Vector2 transformedItemIntervalHorizontal,
                out Vector2 transformedItemIntervalVertical,
                out bool flippedX,
                out bool flippedY);

            Vector2 currentItemPos = transformedItemPos;

            bool isWiringMode = SubEditorScreen.TransparentWiringMode && SubEditorScreen.IsWiringMode();

            int i = 0;
            foreach (ContainedItem contained in containedItems)
            {
                if (contained.Item?.Sprite == null) { continue; }
                if (contained.Hide) { continue; }

                Vector2 itemPos = transformedItemPos;
                int targetSlotIndex = ItemsUseInventoryPlacement ? Inventory.FindIndex(contained.Item) : i;
                //interval set on both axes -> use a grid layout
                if (Math.Abs(ItemInterval.X) > 0.001f && Math.Abs(ItemInterval.Y) > 0.001f)
                {
                    itemPos += transformedItemIntervalHorizontal * (targetSlotIndex % ItemsPerRow);
                    itemPos += transformedItemIntervalVertical * (targetSlotIndex / ItemsPerRow);                    
                }
                else
                {
                    itemPos += (transformedItemIntervalHorizontal + transformedItemIntervalVertical) * targetSlotIndex;
                }

                if (contained.ItemPos.HasValue)
                {
                    Vector2 pos = contained.ItemPos.Value;
                    if (item.body != null)
                    {
                        Matrix transform = Matrix.CreateRotationZ(item.body.DrawRotation);
                        pos.X *= rootBody.Dir;
                        itemPos = Vector2.Transform(pos, transform) + item.body.DrawPosition;
                    }
                    else
                    {
                        itemPos = pos;
                        // This code is aped based on above. Not tested.
                        if (flippedX)
                        {
                            itemPos.X = -itemPos.X;
                            itemPos.X += item.Rect.Width;
                        }
                        if (flippedY)
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
                
                if (CanAutoInteractWithContained(contained.Item) && Screen.Selected is not { IsEditor: true })
                {
                    contained.Item.IsHighlighted = item.IsHighlighted;
                    item.IsHighlighted = false;
                }

                Vector2 origin = contained.Item.Sprite.Origin;
                if (flippedX) { origin.X = contained.Item.Sprite.SourceRect.Width - origin.X; }
                if (flippedY) { origin.Y = contained.Item.Sprite.SourceRect.Height - origin.Y; }

                float containedSpriteDepth = ContainedSpriteDepth < 0.0f ? contained.Item.Sprite.Depth : ContainedSpriteDepth;
                if (targetSlotIndex < containedSpriteDepths.Length)
                {
                    containedSpriteDepth = containedSpriteDepths[targetSlotIndex];
                }
                containedSpriteDepth = itemDepth + (containedSpriteDepth - (item.Sprite?.Depth ?? item.SpriteDepth)) / 10000.0f;
                
                SpriteEffects spriteEffects = SpriteEffects.None;
                float spriteRotation = ItemRotation;
                if (contained.Rotation != 0)
                {
                    spriteRotation = contained.Rotation;
                }

                bool flipX = rootBody is { Dir: -1 } || flippedX;
                if (flipX)
                {
                    spriteEffects |= SpriteEffects.FlipHorizontally;
                }
                bool flipY = flippedY;
                if (flipY)
                {
                    spriteEffects |= SpriteEffects.FlipVertically;
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

                contained.Item.DrawDecorativeSprites(spriteBatch, itemPos, flipX, flipY, (contained.Item.body == null ? 0.0f : contained.Item.body.DrawRotation), 
                    containedSpriteDepth, overrideColor);

                foreach (ItemContainer ic in contained.Item.GetComponents<ItemContainer>())
                {
                    if (ic.hideItems) { continue; }
                    ic.DrawContainedItems(spriteBatch, containedSpriteDepth, overrideColor);
                }

                i++;
            }
        }

        public override void UpdateHUDComponentSpecific(Character character, float deltaTime, Camera cam)
        {
            if (!item.IsInteractable(character)) { return; }
            RefreshGUIIfOutdated(character);
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
