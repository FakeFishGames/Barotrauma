using System;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

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

        /// <summary>
        /// Depth at which the contained sprites are drawn. If not set, the original depth of the item sprites is used.
        /// </summary>
        [Serialize(-1.0f, false, description: "Depth at which the contained sprites are drawn. If not set, the original depth of the item sprites is used.")]
        public float ContainedSpriteDepth { get; set; }

        [Serialize(null, false, description: "An optional text displayed above the item's inventory.")]
        public string UILabel { get; set; }

        public GUIComponentStyle IndicatorStyle { get; set; }

        [Serialize(null, false)]
        public string ContainedStateIndicatorStyle { get; set; }

        [Serialize(-1, false, description: "Can be used to make the contained state indicator display the condition of the item in a specific slot even when the container's capacity is more than 1.")]
        public int ContainedStateIndicatorSlot { get; set; }

        [Serialize(true, false, description: "Should an indicator displaying the state of the contained items be displayed on this item's inventory slot. "+
            "If this item can only contain one item, the indicator will display the condition of the contained item, otherwise it will indicate how full the item is.")]
        public bool ShowContainedStateIndicator { get; set; }

        [Serialize(false, false, description: "If enabled, the condition of this item is displayed in the indicator that would normally show the state of the contained items." +
            " May be useful for items such as ammo boxes and magazines that spawn projectiles as needed," +
            " and use the condition to determine how many projectiles can be spawned in total.")]
        public bool ShowConditionInContainedStateIndicator
        {
            get;
            set;
        }

        [Serialize(false, false, description: "Should the inventory of this item be kept open when the item is equipped by a character.")]
        public bool KeepOpenWhenEquipped { get; set; }

        [Serialize(false, false, description: "Can the inventory of this item be moved around on the screen by the player.")]
        public bool MovableFrame { get; set; }

        public Vector2 DrawSize
        {
            //use the extents of the item as the draw size
            get { return Vector2.Zero; }
        }

        partial void InitProjSpecific(XElement element)
        {
            slotIcons = new Sprite[capacity];
            foreach (XElement subElement in element.Elements())
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
                }
            }

            if (string.IsNullOrEmpty(ContainedStateIndicatorStyle))
            {
                //if neither a style or a custom sprite is defined, use default style
                if (ContainedStateIndicator == null)
                {
                    IndicatorStyle = GUI.Style.GetComponentStyle("ContainedStateIndicator.Default");
                }
            }
            else
            {
                IndicatorStyle = GUI.Style.GetComponentStyle("ContainedStateIndicator." + ContainedStateIndicatorStyle);
                if (ContainedStateIndicator != null || ContainedStateIndicatorEmpty != null)
                {
                    DebugConsole.AddWarning($"Item \"{item.Name}\" defines both a contained state indicator style and a custom indicator sprite. Will use the custom sprite...");
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

            containedSpriteDepths = element.GetAttributeFloatArray("containedspritedepths", new float[0]);
        }

        protected override void CreateGUI()
        {
            var content = new GUIFrame(new RectTransform(GuiFrame.Rect.Size - GUIStyle.ItemFrameMargin, GuiFrame.RectTransform, Anchor.Center) { AbsoluteOffset = GUIStyle.ItemFrameOffset },
                style: null)
            {
                CanBeFocused = false
            };

            string labelText = GetUILabel();
            GUITextBlock label = null;
            if (!string.IsNullOrEmpty(labelText))
            {
                label = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), content.RectTransform, Anchor.TopCenter), 
                    labelText, font: GUI.SubHeadingFont, textAlignment: Alignment.Center, wrap: true);
            }

            float minInventoryAreaSize = 0.5f;
            guiCustomComponent = new GUICustomComponent(
                new RectTransform(new Vector2(1.0f, label == null ? 1.0f : Math.Max(1.0f - label.RectTransform.RelativeSize.Y, minInventoryAreaSize)), content.RectTransform, Anchor.BottomCenter),
                onDraw: (SpriteBatch spriteBatch, GUICustomComponent component) => { Inventory.Draw(spriteBatch); },
                onUpdate: null)
            {
                CanBeFocused = false
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

        public string GetUILabel()
        {
            if (UILabel == string.Empty) { return string.Empty; }
            if (UILabel != null)
            {
                return TextManager.Get("UILabel." + UILabel);
            }
            else
            {
                return item?.Name;
            }            
        }

        public Sprite GetSlotIcon(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= slotIcons.Length) { return null; }
            return slotIcons[slotIndex];
        }

        public bool KeepOpenWhenEquippedBy(Character character)
        {
            if (!character.CanAccessInventory(Inventory) ||
                !KeepOpenWhenEquipped ||
                !character.HasEquippedItem(Item))
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

        public void Draw(SpriteBatch spriteBatch, bool editing = false, float itemDepth = -1)
        {
            if (hideItems || (item.body != null && !item.body.Enabled)) { return; }
            DrawContainedItems(spriteBatch, itemDepth);
        }

        public void DrawContainedItems(SpriteBatch spriteBatch, float itemDepth)
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

                if (Math.Abs(item.Rotation) > 0.01f)
                {
                    Matrix transform = Matrix.CreateRotationZ(MathHelper.ToRadians(-item.Rotation));
                    transformedItemPos = Vector2.Transform(transformedItemPos - item.DrawPosition, transform) + item.DrawPosition;
                    transformedItemInterval = Vector2.Transform(transformedItemInterval, transform);
                    transformedItemIntervalHorizontal = Vector2.Transform(transformedItemIntervalHorizontal, transform);
                    transformedItemIntervalVertical = Vector2.Transform(transformedItemIntervalVertical, transform);
                }
            }
            else
            {
                Matrix transform = Matrix.CreateRotationZ(item.body.Rotation);
                if (item.body.Dir == -1.0f)
                {
                    transformedItemPos.X = -transformedItemPos.X;
                    transformedItemInterval.X = -transformedItemInterval.X;
                    transformedItemIntervalHorizontal.X = -transformedItemIntervalHorizontal.X;
                }

                transformedItemPos = Vector2.Transform(transformedItemPos, transform);
                transformedItemInterval = Vector2.Transform(transformedItemInterval, transform);
                transformedItemIntervalHorizontal = Vector2.Transform(transformedItemIntervalHorizontal, transform);
                transformedItemPos += item.DrawPosition;
            }

            Vector2 currentItemPos = transformedItemPos;

            SpriteEffects spriteEffects = SpriteEffects.None;
            if ((item.body != null && item.body.Dir == -1) || item.FlippedX) 
            { 
                spriteEffects |= MathUtils.NearlyEqual(ItemRotation % 180, 90.0f) ? SpriteEffects.FlipVertically : SpriteEffects.FlipHorizontally;
            }
            if (item.FlippedY)
            {
                spriteEffects |= MathUtils.NearlyEqual(ItemRotation % 180, 90.0f) ? SpriteEffects.FlipHorizontally : SpriteEffects.FlipVertically;
            }

            bool isWiringMode = SubEditorScreen.TransparentWiringMode && SubEditorScreen.IsWiringMode();

            int i = 0;
            foreach (Item containedItem in Inventory.AllItems)
            {
                if (AutoInteractWithContained)
                {
                    containedItem.IsHighlighted = item.IsHighlighted;
                    item.IsHighlighted = false;
                }

                Vector2 origin = containedItem.Sprite.Origin;
                if (item.FlippedX) { origin.X = containedItem.Sprite.SourceRect.Width - origin.X; }
                if (item.FlippedY) { origin.Y = containedItem.Sprite.SourceRect.Height - origin.Y; }

                float containedSpriteDepth = ContainedSpriteDepth < 0.0f ? containedItem.Sprite.Depth : ContainedSpriteDepth;
                if (i < containedSpriteDepths.Length)
                {
                    containedSpriteDepth = containedSpriteDepths[i];
                }
                containedSpriteDepth = itemDepth + (containedSpriteDepth - (item.Sprite?.Depth ?? item.SpriteDepth)) / 10000.0f;

                containedItem.Sprite.Draw(
                    spriteBatch,
                    new Vector2(currentItemPos.X, -currentItemPos.Y),
                    isWiringMode ? containedItem.GetSpriteColor() * 0.15f : containedItem.GetSpriteColor(),
                    origin,
                    -(containedItem.body == null ? 0.0f : containedItem.body.DrawRotation + MathHelper.ToRadians(-item.Rotation)),
                    containedItem.Scale,
                    spriteEffects,
                    depth: containedSpriteDepth);

                foreach (ItemContainer ic in containedItem.GetComponents<ItemContainer>())
                {
                    if (ic.hideItems) continue;
                    ic.DrawContainedItems(spriteBatch, containedSpriteDepth);
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

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            if (!item.IsInteractable(character)) { return; }
            if (Inventory.RectTransform != null)
            {
                guiCustomComponent.RectTransform.Parent = Inventory.RectTransform;
            }

            //if the item is in the character's inventory, no need to update the item's inventory 
            //because the player can see it by hovering the cursor over the item
            guiCustomComponent.Visible = item.ParentInventory?.Owner != character && DrawInventory;
            if (!guiCustomComponent.Visible) { return; }

            Inventory.Update(deltaTime, cam);
        }

        /*public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            //if the item is in the character's inventory, no need to draw the item's inventory 
            //because the player can see it by hovering the cursor over the item
            if (item.ParentInventory?.Owner == character || !DrawInventory) return;
            
            Inventory.Draw(spriteBatch);            
        }*/
    }
}
