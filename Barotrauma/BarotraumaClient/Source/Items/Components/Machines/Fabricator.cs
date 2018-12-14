using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Items.Components
{
    partial class Fabricator : Powered, IServerSerializable, IClientSerializable
    {
        private GUIListBox itemList;

        private GUIFrame selectedItemFrame;
        
        private GUIButton activateButton;

        private GUIComponent inputInventoryHolder, outputInventoryHolder;
        private GUICustomComponent inputInventoryOverlay, outputInventoryOverlay;

        private FabricableItem selectedItem;

        private Pair<Rectangle, string> tooltip;

        partial void InitProjSpecific()
        {
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), GuiFrame.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            itemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.5f), paddedFrame.RectTransform))
            {
                OnSelected = SelectItem
            };

            inputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(0.7f, 0.15f), paddedFrame.RectTransform), style: null);
            inputInventoryOverlay = new GUICustomComponent(new RectTransform(Vector2.One, inputInventoryHolder.RectTransform), DrawInputOverLay, null)
            {
                CanBeFocused = false
            };

            var outputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.3f), paddedFrame.RectTransform), isHorizontal: true);

            selectedItemFrame = new GUIFrame(new RectTransform(new Vector2(0.75f, 1.0f), outputArea.RectTransform), style: "InnerFrame");
            outputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(0.25f, 1.0f), outputArea.RectTransform), style: null);
            outputInventoryOverlay = new GUICustomComponent(new RectTransform(Vector2.One, outputArea.RectTransform), DrawOutputOverLay, null)
            {
                CanBeFocused = false
            };

            foreach (FabricableItem fi in fabricableItems)
            {
                GUIFrame frame = new GUIFrame(new RectTransform(new Point(itemList.Rect.Width, 50), itemList.Content.RectTransform), style: null)
                {
                    UserData = fi,
                    HoverColor = Color.Gold * 0.2f,
                    SelectedColor = Color.Gold * 0.5f,
                    ToolTip = fi.TargetItem.Description
                };

                GUITextBlock textBlock = new GUITextBlock(new RectTransform(Vector2.Zero, frame.RectTransform, Anchor.CenterLeft) { AbsoluteOffset = new Point(50, 0) },
                    fi.DisplayName)
                {
                    ToolTip = fi.TargetItem.Description
                };

                var itemIcon = fi.TargetItem.InventoryIcon ?? fi.TargetItem.sprite;
                if (itemIcon != null)
                {
                    GUIImage img = new GUIImage(new RectTransform(new Point(40, 40), frame.RectTransform, Anchor.CenterLeft) { AbsoluteOffset = new Point(3, 0) },
                        itemIcon, scaleToFit: true)
                    {
                        Color = fi.TargetItem.InventoryIconColor,
                        ToolTip = fi.TargetItem.Description
                    };
                }
            }
        }

        partial void OnItemLoadedProjSpecific()
        {
            inputContainer.AllowUIOverlap = true;
            inputContainer.Inventory.RectTransform = inputInventoryHolder.RectTransform;
            outputContainer.AllowUIOverlap = true;
            outputContainer.Inventory.RectTransform = outputInventoryHolder.RectTransform;
        }
        
        private void DrawInputOverLay(SpriteBatch spriteBatch, GUICustomComponent overlayComponent)
        {
            overlayComponent.RectTransform.SetAsLastChild();

            FabricableItem targetItem = fabricatedItem ?? selectedItem;
            if (targetItem != null)
            {
                int slotIndex = 0;

                var missingItems = new List<FabricableItem.RequiredItem>();
                foreach (FabricableItem.RequiredItem requiredItem in targetItem.RequiredItems)
                {
                    for (int i = 0; i < requiredItem.Amount; i++)
                    {
                        missingItems.Add(requiredItem);
                    }
                }
                foreach (Item item in inputContainer.Inventory.Items)
                {
                    if (item == null) { continue; }
                    missingItems.Remove(missingItems.FirstOrDefault(mi => mi.ItemPrefab == item.prefab));
                }

                foreach (FabricableItem.RequiredItem requiredItem in missingItems)
                {
                    while (slotIndex < inputContainer.Capacity && inputContainer.Inventory.Items[slotIndex] != null)
                    {
                        slotIndex++;
                    }
                    if (slotIndex >= inputContainer.Capacity) { break; }

                    var itemIcon = requiredItem.ItemPrefab.InventoryIcon ?? requiredItem.ItemPrefab.sprite;

                    Rectangle slotRect = inputContainer.Inventory.slots[slotIndex].Rect;

                    itemIcon.Draw(
                        spriteBatch,
                        slotRect.Center.ToVector2(),
                        color: requiredItem.ItemPrefab.InventoryIconColor * 0.4f,
                        scale: Math.Min(slotRect.Width / itemIcon.size.X, slotRect.Height / itemIcon.size.Y));
                    
                    if (slotRect.Contains(PlayerInput.MousePosition))
                    {
                        string toolTipText = requiredItem.ItemPrefab.Name;
                        if (!string.IsNullOrEmpty(requiredItem.ItemPrefab.Description))
                        {
                            toolTipText += '\n' + requiredItem.ItemPrefab.Description;
                        }
                        tooltip = new Pair<Rectangle, string>(slotRect, toolTipText);
                    }

                    slotIndex++;
                }
            }
        }
        private void DrawOutputOverLay(SpriteBatch spriteBatch, GUICustomComponent overlayComponent)
        {
            overlayComponent.RectTransform.SetAsLastChild();

            FabricableItem targetItem = fabricatedItem ?? selectedItem;
            if (targetItem != null)
            {
                var itemIcon = targetItem.TargetItem.InventoryIcon ?? targetItem.TargetItem.sprite;

                Rectangle slotRect = outputContainer.Inventory.slots[0].Rect;

                GUI.DrawRectangle(spriteBatch,
                new Rectangle(
                    slotRect.X, slotRect.Y + (int)(slotRect.Height * (1.0f - progressState)),
                    slotRect.Width, (int)(slotRect.Height * progressState)),
                Color.Green * 0.5f, isFilled: true);

                itemIcon.Draw(
                    spriteBatch,
                    slotRect.Center.ToVector2(),
                    color: targetItem.TargetItem.InventoryIconColor * 0.4f,
                    scale: Math.Min(slotRect.Width / itemIcon.size.X, slotRect.Height / itemIcon.size.Y));
            }
            
            if (tooltip != null)
            {
                GUIComponent.DrawToolTip(spriteBatch, tooltip.Second, tooltip.First);
                tooltip = null;
            }
        }

        private bool SelectItem(GUIComponent component, object obj)
        {
            selectedItem = obj as FabricableItem;
            if (selectedItem == null) return false;

            selectedItemFrame.ClearChildren();
            
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.85f), selectedItemFrame.RectTransform, Anchor.Center) { RelativeOffset = new Vector2(0.0f, -0.05f) }) { RelativeSpacing = 0.03f, Stretch = true };

            /*var itemIcon = selectedItem.TargetItem.InventoryIcon ?? selectedItem.TargetItem.sprite;
            if (itemIcon != null)
            {
                GUIImage img = new GUIImage(new RectTransform(new Point(40, 40), paddedFrame.RectTransform),
                    itemIcon, scaleToFit: true)
                {
                    Color = selectedItem.TargetItem.InventoryIconColor
                };
            }*/
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform),
                selectedItem.TargetItem.Name, textAlignment: Alignment.CenterLeft);

            if (!string.IsNullOrWhiteSpace(selectedItem.TargetItem.Description))
            {
                var description = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform),
                    selectedItem.TargetItem.Description,
                    font: GUI.SmallFont, wrap: true);
            }
            
            List<Skill> inadequateSkills = new List<Skill>();
            if (Character.Controlled != null)
            {
                inadequateSkills = selectedItem.RequiredSkills.FindAll(skill => Character.Controlled.GetSkillLevel(skill.Identifier) < skill.Level);
            }
            
            if (selectedItem.RequiredSkills.Any())
            {
                string text = TextManager.Get("FabricatorRequiredSkills") + ":\n";
                foreach (Skill skill in inadequateSkills)
                {
                    text += "   - " + TextManager.Get("SkillName." + skill.Identifier) + " " + TextManager.Get("Lvl").ToLower() + " " + skill.Level;
                    if (skill != inadequateSkills.Last()) { text += "\n"; }
                }
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform), text,
                    textColor: inadequateSkills.Any() ? Color.Red : Color.White, font: GUI.SmallFont);
            }

            activateButton = new GUIButton(new RectTransform(new Vector2(0.4f, 0.1f), selectedItemFrame.RectTransform, Anchor.BottomCenter) { RelativeOffset = new Vector2(0.0f, 0.03f) },
                TextManager.Get("FabricatorCreate"))
            {
                OnClicked = StartButtonClicked,
                IgnoreLayoutGroups = true,
                UserData = selectedItem,
                Enabled = false
            };

            return true;
        }

        private bool StartButtonClicked(GUIButton button, object obj)
        {
            if (fabricatedItem == null)
            {
                StartFabricating(obj as FabricableItem, Character.Controlled);
            }
            else
            {
                CancelFabricating(Character.Controlled);
            }

            if (GameMain.Server != null)
            {
                item.CreateServerEvent(this);
            }
            else if (GameMain.Client != null)
            {
                item.CreateClientEvent(this);
            }

            return true;
        }

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            if (itemList.SelectedData is FabricableItem targetItem)
            {
                activateButton.Enabled = CanBeFabricated(targetItem);
            }

            if (character != null)
            {
                CheckFabricableItems();
            }
        }

        public void ClientWrite(NetBuffer msg, object[] extraData = null)
        {
            int itemIndex = fabricatedItem == null ? -1 : fabricableItems.IndexOf(fabricatedItem);
            msg.WriteRangedInteger(-1, fabricableItems.Count - 1, itemIndex);
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
        {
            int itemIndex = msg.ReadRangedInteger(-1, fabricableItems.Count - 1);

            if (itemIndex == -1)
            {
                CancelFabricating();
            }
            else
            {
                //if already fabricating the selected item, return
                if (fabricatedItem != null && fabricableItems.IndexOf(fabricatedItem) == itemIndex) return;
                if (itemIndex < 0 || itemIndex >= fabricableItems.Count) return;

                SelectItem(null, fabricableItems[itemIndex]);
                StartFabricating(fabricableItems[itemIndex]);
            }
        }
    }
}