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

        private GUIComponent inSufficientPowerWarning;

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

            activateButton = new GUIButton(new RectTransform(new Vector2(0.8f, 0.07f), paddedFrame.RectTransform),
                TextManager.Get("FabricatorCreate"))
            {
                OnClicked = StartButtonClicked,
                UserData = selectedItem,
                Enabled = false
            };

            inSufficientPowerWarning = new GUITextBlock(new RectTransform(Vector2.One, activateButton.RectTransform), TextManager.Get("FabricatorNoPower"),
                textColor: Color.Orange, textAlignment: Alignment.Center, color: Color.Black, style: "OuterGlow")
            {
                HoverColor = Color.Black,
                IgnoreLayoutGroups = true,
                Visible = false,
                CanBeFocused = false
            };
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

                var availableIngredients = GetAvailableIngredients();

                foreach (FabricableItem.RequiredItem requiredItem in missingItems)
                {
                    //highlight suitable ingredients in linked inventories
                    foreach (Item item in availableIngredients)
                    {
                        if (item.ParentInventory != inputContainer.Inventory && IsItemValidIngredient(item, requiredItem))
                        {
                            int availableSlotIndex = Array.IndexOf(item.ParentInventory.Items, item);
                            if (item.ParentInventory.slots[availableSlotIndex].HighlightTimer <= 0.0f)
                            {
                                item.ParentInventory.slots[availableSlotIndex].ShowBorderHighlight(Color.LightGreen * 0.5f, 0.5f, 0.5f);
                            }
                        }
                    }

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
                        color: requiredItem.ItemPrefab.InventoryIconColor * 0.3f,
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
                    scale: Math.Min(slotRect.Width / itemIcon.size.X, slotRect.Height / itemIcon.size.Y) * 0.9f);
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
            
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), selectedItemFrame.RectTransform, Anchor.Center)) { RelativeSpacing = 0.03f, Stretch = true };

            /*var itemIcon = selectedItem.TargetItem.InventoryIcon ?? selectedItem.TargetItem.sprite;
            if (itemIcon != null)
            {
                GUIImage img = new GUIImage(new RectTransform(new Point(40, 40), paddedFrame.RectTransform),
                    itemIcon, scaleToFit: true)
                {
                    Color = selectedItem.TargetItem.InventoryIconColor
                };
            }*/
            var nameBlock = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform),
                selectedItem.TargetItem.Name, textAlignment: Alignment.CenterLeft);

            if (!string.IsNullOrWhiteSpace(selectedItem.TargetItem.Description))
            {
                var description = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform),
                    selectedItem.TargetItem.Description,
                    font: GUI.SmallFont, wrap: true);
                if (description.Rect.Height > paddedFrame.Rect.Height * 0.4f)
                {
                    description.Wrap = false;
                    description.Text = description.WrappedText.Split('\n').First()+"...";
                    nameBlock.ToolTip = description.ToolTip = selectedItem.TargetItem.Description;
                    description.RectTransform.MaxSize = new Point(int.MaxValue, (int)description.Font.MeasureString(description.Text).Y);
                }
            }
            
            List<Skill> inadequateSkills = new List<Skill>();
            if (Character.Controlled != null)
            {
                inadequateSkills = selectedItem.RequiredSkills.FindAll(skill => Character.Controlled.GetSkillLevel(skill.Identifier) < skill.Level);
            }
            
            if (selectedItem.RequiredSkills.Any())
            {
                string text = TextManager.Get("FabricatorRequiredSkills") + ":\n";
                foreach (Skill skill in selectedItem.RequiredSkills)
                {
                    text += "   - " + TextManager.Get("SkillName." + skill.Identifier) + " " + TextManager.Get("Lvl").ToLower() + " " + skill.Level;
                    if (skill != selectedItem.RequiredSkills.Last()) { text += "\n"; }
                }
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform), text,
                    textColor: inadequateSkills.Any() ? Color.Red : Color.LightGreen, font: GUI.SmallFont);
            }

            float degreeOfSuccess = DegreeOfSuccess(Character.Controlled, selectedItem.RequiredSkills);
            if (degreeOfSuccess > 0.5f) { degreeOfSuccess = 1.0f; }

            float requiredTime = GetRequiredTime(selectedItem, Character.Controlled);
            string requiredTimeText = TextManager.Get("FabricatorRequiredTime") + ": " + ToolBox.SecondsToReadableTime(requiredTime);
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform),
                requiredTimeText, textColor: ToolBox.GradientLerp(degreeOfSuccess, Color.Red, Color.Yellow, Color.LightGreen), font: GUI.SmallFont);
                        
            return true;
        }

        private bool StartButtonClicked(GUIButton button, object obj)
        {
            if (selectedItem == null) { return false; }
            if (fabricatedItem == null)
            {
                StartFabricating(selectedItem, Character.Controlled);
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
            activateButton.Enabled = false;
            inSufficientPowerWarning.Visible = powerConsumption > 0 && voltage < minVoltage;

            var availableIngredients = GetAvailableIngredients();
            if (character != null)
            {
                foreach (GUIComponent child in itemList.Content.Children)
                {
                    var itemPrefab = child.UserData as FabricableItem;
                    if (itemPrefab == null) continue;

                    bool canBeFabricated = CanBeFabricated(itemPrefab, availableIngredients);
                    if (itemPrefab == selectedItem)
                    {
                        activateButton.Enabled = canBeFabricated;
                    }

                    child.GetChild<GUITextBlock>().TextColor = Color.White * (canBeFabricated ? 1.0f : 0.5f);
                    child.GetChild<GUIImage>().Color = itemPrefab.TargetItem.InventoryIconColor * (canBeFabricated ? 1.0f : 0.5f);
                }
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
            UInt16 userID = msg.ReadUInt16();
            Character user = Entity.FindEntityByID(userID) as Character;

            if (itemIndex == -1 || user == null)
            {
                CancelFabricating();
            }
            else
            {
                //if already fabricating the selected item, return
                if (fabricatedItem != null && fabricableItems.IndexOf(fabricatedItem) == itemIndex) return;
                if (itemIndex < 0 || itemIndex >= fabricableItems.Count) return;

                SelectItem(null, fabricableItems[itemIndex]);
                StartFabricating(fabricableItems[itemIndex], user);
            }
        }
    }
}