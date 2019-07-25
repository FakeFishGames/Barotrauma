using Barotrauma.Extensions;
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
        
        public GUIButton ActivateButton
        {
            get { return activateButton; }
        }
        private GUIButton activateButton;

        private GUITextBox itemFilterBox;

        private GUIComponent inputInventoryHolder, outputInventoryHolder;
        private GUICustomComponent inputInventoryOverlay, outputInventoryOverlay;

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
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), GuiFrame.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            var filterArea = new GUILayoutGroup(new RectTransform(new Vector2(1.0f, 0.06f), paddedFrame.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                UserData = "filterarea"
            };
            new GUITextBlock(new RectTransform(new Vector2(0.25f, 1.0f), filterArea.RectTransform), TextManager.Get("serverlog.filter"), font: GUI.Font);
            itemFilterBox = new GUITextBox(new RectTransform(new Vector2(0.8f, 1.0f), filterArea.RectTransform), font: GUI.Font);
            itemFilterBox.OnTextChanged += (textBox, text) => { FilterEntities(text); return true; };
            var clearButton = new GUIButton(new RectTransform(new Vector2(0.1f, 1.0f), filterArea.RectTransform), "x")
            {
                OnClicked = (btn, userdata) => { ClearFilter(); itemFilterBox.Flash(Color.White); return true; }
            };

            itemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 0.5f), paddedFrame.RectTransform))
            {
                OnSelected = (GUIComponent component, object userdata) =>
                {
                    selectedItem = userdata as FabricationRecipe;
                    if (selectedItem != null) { SelectItem(Character.Controlled, selectedItem); }
                    return true;
                }
            };

            inputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(0.7f, 0.15f), paddedFrame.RectTransform), style: null);
            inputInventoryOverlay = new GUICustomComponent(new RectTransform(Vector2.One, inputInventoryHolder.RectTransform), DrawInputOverLay, null)
            {
                CanBeFocused = false
            };

            var outputArea = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.25f), paddedFrame.RectTransform), isHorizontal: true);

            selectedItemFrame = new GUIFrame(new RectTransform(new Vector2(0.75f, 1.0f), outputArea.RectTransform), style: "InnerFrame");
            outputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(0.25f, 1.0f), outputArea.RectTransform), style: null);
            outputInventoryOverlay = new GUICustomComponent(new RectTransform(Vector2.One, outputArea.RectTransform), DrawOutputOverLay, null)
            {
                CanBeFocused = false
            };

            CreateRecipes();

            activateButton = new GUIButton(new RectTransform(new Vector2(0.8f, 0.07f), paddedFrame.RectTransform),
                TextManager.Get("FabricatorCreate"), style: "GUIButtonLarge")
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

        partial void CreateRecipes()
        {
            itemList.Content.RectTransform.ClearChildren();

            foreach (FabricationRecipe fi in fabricationRecipes)
            {
                GUIFrame frame = new GUIFrame(new RectTransform(new Point(itemList.Rect.Width, (int)(30 * GUI.yScale)), itemList.Content.RectTransform), style: null)
                {
                    UserData = fi,
                    HoverColor = Color.Gold * 0.2f,
                    SelectedColor = Color.Gold * 0.5f,
                    ToolTip = fi.TargetItem.Description
                };

                GUITextBlock textBlock = new GUITextBlock(new RectTransform(Vector2.Zero, frame.RectTransform, Anchor.CenterLeft) { AbsoluteOffset = new Point((int)(50 * GUI.xScale), 0) },
                    fi.DisplayName)
                {
                    ToolTip = fi.TargetItem.Description
                };

                var itemIcon = fi.TargetItem.InventoryIcon ?? fi.TargetItem.sprite;
                if (itemIcon != null)
                {
                    GUIImage img = new GUIImage(new RectTransform(new Point((int)(30 * GUI.Scale)), frame.RectTransform, Anchor.CenterLeft) { AbsoluteOffset = new Point((int)(3 * GUI.xScale), 0) },
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

        partial void SelectProjSpecific(Character character)
        {
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
                TextManager.Get("fabricatorsufficientskills", returnNull: true) ?? "Sufficient skills to fabricate", textColor: Color.LightGreen)
            {
                CanBeFocused = false
            };
            sufficientSkillsText.RectTransform.SetAsFirstChild();

            var insufficientSkillsText = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), itemList.Content.RectTransform),
                TextManager.Get("fabricatorinsufficientskills", returnNull: true) ?? "Insufficient skills to fabricate", textColor: Color.Orange)
            {
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
                                    item.ParentInventory.slots[availableSlotIndex].ShowBorderHighlight(Color.LightGreen * 0.5f, 0.5f, 0.5f);
                                }
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
            
            FabricationRecipe targetItem = fabricatedItem ?? selectedItem;
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
            if (user != null)
            {
                inadequateSkills = selectedItem.RequiredSkills.FindAll(skill => user.GetSkillLevel(skill.Identifier) < skill.Level);
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

            float degreeOfSuccess = user == null ? 0.0f : DegreeOfSuccess(user, selectedItem.RequiredSkills);
            if (degreeOfSuccess > 0.5f) { degreeOfSuccess = 1.0f; }

            float requiredTime = user == null ? selectedItem.RequiredTime : GetRequiredTime(selectedItem, user);
            string requiredTimeText = TextManager.AddPunctuation(':', TextManager.Get("FabricatorRequiredTime"), ToolBox.SecondsToReadableTime(requiredTime));
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform),
                requiredTimeText, textColor: ToolBox.GradientLerp(degreeOfSuccess, Color.Red, Color.Yellow, Color.LightGreen), font: GUI.SmallFont);
                        
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
                outputInventoryHolder.Flash(Color.Red);
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

                    child.GetChild<GUITextBlock>().TextColor = Color.White * (canBeFabricated ? 1.0f : 0.5f);
                    child.GetChild<GUIImage>().Color = itemPrefab.TargetItem.InventoryIconColor * (canBeFabricated ? 1.0f : 0.5f);
                }
            }
        }

        public void ClientWrite(NetBuffer msg, object[] extraData = null)
        {
            int itemIndex = pendingFabricatedItem == null ? -1 : fabricationRecipes.IndexOf(pendingFabricatedItem);
            msg.WriteRangedInteger(-1, fabricationRecipes.Count - 1, itemIndex);
        }

        public void ClientRead(ServerNetObject type, NetBuffer msg, float sendingTime)
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