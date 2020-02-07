using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;

namespace Barotrauma.Items.Components
{
    partial class Fabricator : Powered, IServerSerializable, IClientSerializable
    {
        private GUIListBox itemList;

        private GUIFrame selectedItemFrame;
        private GUIFrame selectedItemReqsFrame;
        
        public GUIButton ActivateButton
        {
            get { return activateButton; }
        }
        private GUIButton activateButton;

        private GUITextBox itemFilterBox;

        private GUIComponent outputSlot;
        private GUIComponent inputInventoryHolder, outputInventoryHolder;

        public FabricationRecipe SelectedItem
        {
            get { return selectedItem; }
        }
        private FabricationRecipe selectedItem;

        private GUIComponent inSufficientPowerWarning;

        private FabricationRecipe pendingFabricatedItem;

        private Pair<Rectangle, string> tooltip;

        partial void InitProjSpecific()
        {
            CreateGUI();
            GameMain.Instance.OnResolutionChanged += () =>
            {
                GuiFrame.ClearChildren();
                CreateGUI();
                OnItemLoadedProjSpecific();
            };
        }

        private void CreateGUI()
        {
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), GuiFrame.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter);

            // === LABEL === //
            new GUITextBlock(new RectTransform(new Vector2(1f, 0.05f), paddedFrame.RectTransform), item.Name, font: GUI.SubHeadingFont)
            {
                TextAlignment = Alignment.Center,
                AutoScaleVertical = true
            };

            var mainFrame = new GUILayoutGroup(new RectTransform(new Vector2(1f, 1f), paddedFrame.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                RelativeSpacing = 0.02f
            };
            
            // === TOP AREA ===
            var topFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 0.65f), mainFrame.RectTransform), style: "InnerFrameDark");

                // === ITEM LIST ===
                var itemListFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1.0f), topFrame.RectTransform), childAnchor: Anchor.Center);
                    var paddedItemFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), itemListFrame.RectTransform))
                    {
                        Stretch = true, 
                        RelativeSpacing = 0.03f
                    };
                        var filterArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.15f), paddedItemFrame.RectTransform), isHorizontal: true)
                        {
                            Stretch = true, 
                            RelativeSpacing = 0.03f, 
                            UserData = "filterarea"
                        };
                            new GUITextBlock(new RectTransform(new Vector2(0.2f, 1f), filterArea.RectTransform), TextManager.Get("serverlog.filter"), font: GUI.SubHeadingFont)
                            {
                                Padding = Vector4.Zero, 
                                AutoScaleVertical = true
                            };
                            itemFilterBox = new GUITextBox(new RectTransform(new Vector2(0.8f, 1.0f), filterArea.RectTransform), createClearButton: true);
                            itemFilterBox.OnTextChanged += (textBox, text) =>
                            {
                                FilterEntities(text); 
                                return true;
                            };
                            filterArea.RectTransform.MaxSize = new Point(int.MaxValue, itemFilterBox.Rect.Height);

                        itemList = new GUIListBox(new RectTransform(new Vector2(1f, 0.9f), paddedItemFrame.RectTransform), style: null)
                        {
                            OnSelected = (component, userdata) =>
                            {
                                selectedItem = userdata as FabricationRecipe;
                                if (selectedItem != null) SelectItem(Character.Controlled, selectedItem);
                                return true;
                            }
                        };

                // === SEPARATOR === //
                new GUIFrame(new RectTransform(new Vector2(0.01f, 0.9f), topFrame.RectTransform, Anchor.Center), style: "VerticalLine");

                // === OUTPUT AREA === //
                var outputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.5f, 1f), topFrame.RectTransform, Anchor.TopRight), childAnchor: Anchor.Center);
                    var paddedOutputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), outputArea.RectTransform));
                        var outputTopArea = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.5F), paddedOutputArea.RectTransform, Anchor.Center), isHorizontal: true);
                            // === OUTPUT SLOT === //
                            outputSlot = new GUIFrame(new RectTransform(new Vector2(0.4f, 1f), outputTopArea.RectTransform), style: null);
                                outputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(1f, 1.2f), outputSlot.RectTransform, Anchor.BottomCenter), style: null);
                                    new GUICustomComponent(new RectTransform(Vector2.One, outputInventoryHolder.RectTransform), DrawOutputOverLay) { CanBeFocused = false };
                            // === DESCRIPTION === //
                            selectedItemFrame = new GUIFrame(new RectTransform(new Vector2(0.6f, 1f), outputTopArea.RectTransform), style: null);
                        // === REQUIREMENTS === //
                        selectedItemReqsFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.5f), paddedOutputArea.RectTransform), style: null);

            // === BOTTOM AREA === //
            var bottomFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.3f), mainFrame.RectTransform), style: null);

                // === SEPARATOR === //
                var separatorArea = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.15f), bottomFrame.RectTransform, Anchor.TopCenter), childAnchor: Anchor.CenterLeft, isHorizontal: true)
                {
                    Stretch = true, 
                    RelativeSpacing = 0.03f
                };
                    var inputLabel = new GUITextBlock(new RectTransform(Vector2.One, separatorArea.RectTransform), TextManager.Get("uilabel.input"), font: GUI.SubHeadingFont) { Padding = Vector4.Zero };
                    inputLabel.RectTransform.Resize(new Point((int) inputLabel.Font.MeasureString(inputLabel.Text).X, inputLabel.RectTransform.Rect.Height));
                    new GUIFrame(new RectTransform(Vector2.One, separatorArea.RectTransform), style: "HorizontalLine");

                // === INPUT AREA === //
                var inputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 1f), bottomFrame.RectTransform, Anchor.BottomCenter), isHorizontal: true, childAnchor: Anchor.BottomLeft);
                    
                    // === INPUT SLOTS === //
                    inputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(0.8f, 1f), inputArea.RectTransform), style: null);
                        new GUICustomComponent(new RectTransform(Vector2.One, inputInventoryHolder.RectTransform), DrawInputOverLay) { CanBeFocused = false };

                    // === ACTIVATE BUTTON === //
                    var buttonFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.2f, 0.8f), inputArea.RectTransform), childAnchor: Anchor.CenterRight);
                        activateButton = new GUIButton(new RectTransform(new Vector2(1f, 0.6f), buttonFrame.RectTransform),
                            TextManager.Get("FabricatorCreate"), style: "DeviceButton")
                        {
                            OnClicked = StartButtonClicked,
                            UserData = selectedItem,
                            Enabled = false
                        };
                            // === POWER WARNING === //
                            inSufficientPowerWarning = new GUITextBlock(new RectTransform(Vector2.One, activateButton.RectTransform),
                                TextManager.Get("FabricatorNoPower"), textColor: GUI.Style.Orange, textAlignment: Alignment.Center, color: Color.Black, style: "OuterGlow")
                            {
                                HoverColor = Color.Black,
                                IgnoreLayoutGroups = true,
                                Visible = false,
                                CanBeFocused = false
                            };
            CreateRecipes();
        }

        partial void CreateRecipes()
        {
            itemList.Content.RectTransform.ClearChildren();

            foreach (FabricationRecipe fi in fabricationRecipes)
            {
                var frame = new GUIFrame(new RectTransform(new Point(itemList.Rect.Width, (int)(40 * GUI.yScale)), itemList.Content.RectTransform), style: null)
                {
                    UserData = fi,
                    HoverColor = Color.Gold * 0.2f,
                    SelectedColor = Color.Gold * 0.5f,
                    ToolTip = fi.TargetItem.Description
                };
                
                    var container = new GUILayoutGroup(new RectTransform(Vector2.One, frame.RectTransform),
                        childAnchor: Anchor.CenterLeft, isHorizontal: true) { RelativeSpacing = 0.02f };
                    
                        var itemIcon = fi.TargetItem.InventoryIcon ?? fi.TargetItem.sprite;
                        if (itemIcon != null)
                        {
                            new GUIImage(new RectTransform(new Point(frame.Rect.Height,frame.Rect.Height), container.RectTransform),
                                itemIcon, scaleToFit: true)
                            {
                                Color = fi.TargetItem.InventoryIconColor,
                                ToolTip = fi.TargetItem.Description
                            };
                        }

                        new GUITextBlock(new RectTransform(new Vector2(0.85f, 1f), container.RectTransform), fi.DisplayName)
                        {
                            Padding = Vector4.Zero,
                            AutoScaleVertical = true,
                            ToolTip = fi.TargetItem.Description
                        };
            }
        }

        partial void OnItemLoadedProjSpecific()
        {
            inputContainer.AllowUIOverlap = true;
            inputContainer.Inventory.RectTransform = inputInventoryHolder.RectTransform;
            outputContainer.AllowUIOverlap = true;
            outputContainer.Inventory.RectTransform = outputInventoryHolder.RectTransform;
        }

        partial void SelectProjSpecific(Character character)
        {
            // TODO, This works fine as of now but if GUI.PreventElementOverlap ever gets fixed this block of code may become obsolete or detrimental.
            // Only do this if there's only one linked component. If you link more containers then may
            // GUI.PreventElementOverlap have mercy on your HUD layout
            if (item.linkedTo.Count(entity => entity is Item item && item.DisplaySideBySideWhenLinked) == 1)
            {
                foreach (MapEntity linkedTo in item.linkedTo)
                {
                    if (!(linkedTo is Item linkedItem)) continue;
                    if (!linkedItem.Components.Any()) continue;
                
                    var itemContainer = linkedItem.Components.First();
                    if (itemContainer == null) { continue; }

                    if (!itemContainer.Item.DisplaySideBySideWhenLinked) continue;

                    // how much spacing do we want between the components
                    var padding = (int) (8 * GUI.Scale);
                    // Move the linked container to the right and move the fabricator to the left
                    itemContainer.GuiFrame.RectTransform.AbsoluteOffset = new Point(GuiFrame.Rect.Width / -2 - padding, 0);
                    GuiFrame.RectTransform.AbsoluteOffset = new Point(itemContainer.GuiFrame.Rect.Width / 2 + padding, 0);
                }
            }
            
            var nonItems = itemList.Content.Children.Where(c => !(c.UserData is FabricationRecipe)).ToList();
            nonItems.ForEach(i => itemList.Content.RemoveChild(i));

            itemList.Content.RectTransform.SortChildren((c1, c2) =>
            {
                var item1 = c1.GUIComponent.UserData as FabricationRecipe;
                var item2 = c2.GUIComponent.UserData as FabricationRecipe;

                bool hasSkills1 = DegreeOfSuccess(character, item1.RequiredSkills) >= 0.5f;
                bool hasSkills2 = DegreeOfSuccess(character, item2.RequiredSkills) >= 0.5f;

                if (hasSkills1 != hasSkills2)
                {
                    return hasSkills1 ? -1 : 1;
                }

                return string.Compare(item1.DisplayName, item2.DisplayName);
            });

            var sufficientSkillsText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), itemList.Content.RectTransform),
                TextManager.Get("fabricatorsufficientskills", returnNull: true) ?? "Sufficient skills to fabricate", textColor: GUI.Style.Green, font: GUI.SubHeadingFont)
            {
                AutoScaleHorizontal = true,
                CanBeFocused = false
            };
            sufficientSkillsText.RectTransform.SetAsFirstChild();

            var insufficientSkillsText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), itemList.Content.RectTransform),
                TextManager.Get("fabricatorinsufficientskills", returnNull: true) ?? "Insufficient skills to fabricate", textColor: Color.Orange, font: GUI.SubHeadingFont)
            {
                AutoScaleHorizontal = true,
                CanBeFocused = false
            };
            var firstinSufficient = itemList.Content.Children.FirstOrDefault(c => c.UserData is FabricationRecipe fabricableItem && DegreeOfSuccess(character, fabricableItem.RequiredSkills) < 0.5f);
            if (firstinSufficient != null)
            {
                insufficientSkillsText.RectTransform.RepositionChildInHierarchy(itemList.Content.RectTransform.GetChildIndex(firstinSufficient.RectTransform));
            }
        }

        private void DrawInputOverLay(SpriteBatch spriteBatch, GUICustomComponent overlayComponent)
        {
            overlayComponent.RectTransform.SetAsLastChild();

            FabricationRecipe targetItem = fabricatedItem ?? selectedItem;
            if (targetItem != null)
            {
                int slotIndex = 0;

                var missingItems = new List<FabricationRecipe.RequiredItem>();
                foreach (FabricationRecipe.RequiredItem requiredItem in targetItem.RequiredItems)
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

                foreach (FabricationRecipe.RequiredItem requiredItem in missingItems)
                {
                    while (slotIndex < inputContainer.Capacity && inputContainer.Inventory.Items[slotIndex] != null)
                    {
                        slotIndex++;
                    }

                    //highlight suitable ingredients in linked inventories
                    foreach (Item item in availableIngredients)
                    {
                        if (item.ParentInventory != inputContainer.Inventory && IsItemValidIngredient(item, requiredItem))
                        {
                            int availableSlotIndex = Array.IndexOf(item.ParentInventory.Items, item);
                            //slots are null if the inventory has never been displayed 
                            //(linked item, but the UI is not set to be displayed at the same time)
                            if (item.ParentInventory.slots != null)
                            {
                                if (item.ParentInventory.slots[availableSlotIndex].HighlightTimer <= 0.0f)
                                {
                                    item.ParentInventory.slots[availableSlotIndex].ShowBorderHighlight(GUI.Style.Green, 0.5f, 0.5f);
                                    if (slotIndex < inputContainer.Capacity)
                                    {
                                        inputContainer.Inventory.slots[slotIndex].ShowBorderHighlight(GUI.Style.Green, 0.5f, 0.5f);
                                    }
                                }
                            }
                        }
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
            
            FabricationRecipe targetItem = fabricatedItem ?? selectedItem;
            if (targetItem != null)
            {
                var itemIcon = targetItem.TargetItem.InventoryIcon ?? targetItem.TargetItem.sprite;

                Rectangle slotRect = outputContainer.Inventory.slots[0].Rect;

                if (fabricatedItem != null)
                {
                    GUI.DrawRectangle(spriteBatch,
                        new Rectangle(
                            slotRect.X, slotRect.Y + (int)(slotRect.Height * (1.0f - progressState)),
                            slotRect.Width, (int)(slotRect.Height * progressState)),
                        GUI.Style.Green * 0.5f, isFilled: true);
                }

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

        private bool FilterEntities(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                itemList.Content.Children.ForEach(c => c.Visible = true);
                return true;
            }

            filter = filter.ToLower();
            foreach (GUIComponent child in itemList.Content.Children)
            {
                FabricationRecipe recipe = child.UserData as FabricationRecipe;
                if (recipe?.DisplayName == null) { continue; }
                child.Visible = recipe.DisplayName.ToLower().Contains(filter);
            }
            itemList.UpdateScrollBarSize();
            itemList.BarScroll = 0.0f;

            return true;
        }

        public bool ClearFilter()
        {
            FilterEntities("");
            itemList.UpdateScrollBarSize();
            itemList.BarScroll = 0.0f;
            itemFilterBox.Text = "";
            return true;
        }

        private bool SelectItem(Character user, FabricationRecipe selectedItem)
        {
            selectedItemFrame.ClearChildren();
            selectedItemReqsFrame.ClearChildren();
            
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.9f), selectedItemFrame.RectTransform, Anchor.Center)) { RelativeSpacing = 0.03f };
            var paddedReqFrame = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.9f), selectedItemReqsFrame.RectTransform, Anchor.Center)) { RelativeSpacing = 0.03f };

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
                selectedItem.TargetItem.Name, textAlignment: Alignment.CenterLeft, textColor: Color.Aqua, font: GUI.SubHeadingFont)
            {
                AutoScaleHorizontal = true
            };
            
            nameBlock.Padding = new Vector4(0, nameBlock.Padding.Y, nameBlock.Padding.Z, nameBlock.Padding.W);
            
            if (!string.IsNullOrWhiteSpace(selectedItem.TargetItem.Description))
            {
                var description = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform),
                    selectedItem.TargetItem.Description,
                    font: GUI.SmallFont, wrap: true);
                description.Padding = new Vector4(0, description.Padding.Y, description.Padding.Z, description.Padding.W);
            
                while (description.Rect.Height + nameBlock.Rect.Height > paddedFrame.Rect.Height)
                {
                    var lines = description.WrappedText.Split('\n');
                    var newString = string.Join('\n', lines.Take(lines.Length - 1));
                    description.Text = newString.Substring(0, newString.Length - 4) + "...";
                    description.CalculateHeightFromText();
                    description.ToolTip = selectedItem.TargetItem.Description;
                }
            }
            
            List<Skill> inadequateSkills = new List<Skill>();
            if (user != null)
            {
                inadequateSkills = selectedItem.RequiredSkills.FindAll(skill => user.GetSkillLevel(skill.Identifier) < skill.Level);
            }
            
            if (selectedItem.RequiredSkills.Any())
            {
                string text = "";
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedReqFrame.RectTransform), 
                    TextManager.Get("FabricatorRequiredSkills"), textColor: inadequateSkills.Any() ? GUI.Style.Red : GUI.Style.Green, font: GUI.SubHeadingFont)
                {
                    AutoScaleHorizontal = true,
                };
                foreach (Skill skill in selectedItem.RequiredSkills)
                {
                    text += TextManager.Get("SkillName." + skill.Identifier) + " " + TextManager.Get("Lvl").ToLower() + " " + skill.Level;
                    if (skill != selectedItem.RequiredSkills.Last()) { text += "\n"; }
                }
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedReqFrame.RectTransform), text, font: GUI.SmallFont);
            }

            float degreeOfSuccess = user == null ? 0.0f : DegreeOfSuccess(user, selectedItem.RequiredSkills);
            if (degreeOfSuccess > 0.5f) { degreeOfSuccess = 1.0f; }

            float requiredTime = user == null ? selectedItem.RequiredTime : GetRequiredTime(selectedItem, user);
            
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedReqFrame.RectTransform), 
                TextManager.Get("FabricatorRequiredTime") , textColor: ToolBox.GradientLerp(degreeOfSuccess, GUI.Style.Red, Color.Yellow, GUI.Style.Green), font: GUI.SubHeadingFont)
            {
                AutoScaleHorizontal = true,
            };
                
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedReqFrame.RectTransform), ToolBox.SecondsToReadableTime(requiredTime), 
                font: GUI.SmallFont);
            return true;
        }

        public void HighlightRecipe(string identifier, Color color)
        {
            foreach (GUIComponent child in itemList.Content.Children)
            {
                FabricationRecipe recipe = child.UserData as FabricationRecipe;
                if (recipe?.DisplayName == null) { continue; }
                if (recipe.TargetItem.Identifier == identifier)
                {
                    if (child.FlashTimer > 0.0f) return;
                    child.Flash(color, 1.5f, false);

                    for (int i = 0; i < child.CountChildren; i++)
                    {
                        var grandChild = child.GetChild(i);
                        if (grandChild is GUITextBlock) continue;
                        grandChild.Flash(color, 1.5f, false);
                    }

                    return;
                }
            }
        }
        
        private bool StartButtonClicked(GUIButton button, object obj)
        {
            if (selectedItem == null) { return false; }
            if (!outputContainer.Inventory.IsEmpty())
            {
                outputSlot.Flash(GUI.Style.Red);
                return false;
            }
            
            if (GameMain.Client != null)
            {
                pendingFabricatedItem = fabricatedItem != null ? null : selectedItem;
                item.CreateClientEvent(this);
            }
            else
            {
                if (fabricatedItem == null)
                {
                    StartFabricating(selectedItem, Character.Controlled);
                }
                else
                {
                    CancelFabricating(Character.Controlled);
                }
            }

            return true;
        }

        public override void UpdateHUD(Character character, float deltaTime, Camera cam)
        {
            activateButton.Enabled = false;
            inSufficientPowerWarning.Visible = currPowerConsumption > 0 && !hasPower;

            var availableIngredients = GetAvailableIngredients();
            if (character != null)
            {
                foreach (GUIComponent child in itemList.Content.Children)
                {
                    var itemPrefab = child.UserData as FabricationRecipe;
                    if (itemPrefab == null) continue;

                    bool canBeFabricated = CanBeFabricated(itemPrefab, availableIngredients);
                    if (itemPrefab == selectedItem)
                    {
                        activateButton.Enabled = canBeFabricated;
                    }

                    var childContainer = child.GetChild<GUILayoutGroup>();

                    childContainer.GetChild<GUITextBlock>().TextColor = Color.White * (canBeFabricated ? 1.0f : 0.5f);
                    childContainer.GetChild<GUIImage>().Color = itemPrefab.TargetItem.InventoryIconColor * (canBeFabricated ? 1.0f : 0.5f);
                }
            }
        }

        public void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            int itemIndex = pendingFabricatedItem == null ? -1 : fabricationRecipes.IndexOf(pendingFabricatedItem);
            msg.WriteRangedInteger(itemIndex, -1, fabricationRecipes.Count - 1);
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            int itemIndex = msg.ReadRangedInteger(-1, fabricationRecipes.Count - 1);
            UInt16 userID = msg.ReadUInt16();
            Character user = Entity.FindEntityByID(userID) as Character;

            if (itemIndex == -1 || user == null)
            {
                CancelFabricating();
            }
            else
            {
                //if already fabricating the selected item, return
                if (fabricatedItem != null && fabricationRecipes.IndexOf(fabricatedItem) == itemIndex) { return; }
                if (itemIndex < 0 || itemIndex >= fabricationRecipes.Count) { return; }

                SelectItem(user, fabricationRecipes[itemIndex]);
                StartFabricating(fabricationRecipes[itemIndex], user);
            }
        }
    }
}
