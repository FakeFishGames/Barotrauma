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

        private GUIProgressBar progressBar;
        private GUIButton activateButton;

        private GUIComponent inputInventoryHolder, outputInventoryHolder;

        partial void InitProjSpecific()
        {
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), GuiFrame.RectTransform, Anchor.Center), childAnchor: Anchor.TopCenter)
            {
                Stretch = true,
                RelativeSpacing = 0.05f
            };

            itemList = new GUIListBox(new RectTransform(new Vector2(1.0f, 1.2f), paddedFrame.RectTransform))
            {
                OnSelected = SelectItem
            };

            inputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(0.7f, 1.0f), paddedFrame.RectTransform), style: null);

            selectedItemFrame = new GUIFrame(new RectTransform(new Vector2(1.0f, 1.5f), paddedFrame.RectTransform), style: "InnerFrame");

            outputInventoryHolder = new GUIFrame(new RectTransform(new Vector2(0.2f, 1.0f), paddedFrame.RectTransform), style: null);
            
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

                if (fi.TargetItem.sprite != null)
                {
                    GUIImage img = new GUIImage(new RectTransform(new Point(40, 40), frame.RectTransform, Anchor.CenterLeft) { AbsoluteOffset = new Point(3, 0) },
                        fi.TargetItem.sprite, scaleToFit: true)
                    {
                        Color = fi.TargetItem.SpriteColor,
                        ToolTip = fi.TargetItem.Description
                    };
                }
            }
        }

        public override void OnItemLoaded()
        {
            var itemContainers = item.GetComponents<ItemContainer>().ToList();
            for (int i = 0; i < 2 && i < itemContainers.Count; i++)
            {
                itemContainers[i].AllowUIOverlap = true;
                itemContainers[i].Inventory.RectTransform = i == 0 ? inputInventoryHolder.RectTransform : outputInventoryHolder.RectTransform;
            }
        }

        private bool SelectItem(GUIComponent component, object obj)
        {
            FabricableItem targetItem = obj as FabricableItem;
            if (targetItem == null) return false;

            selectedItemFrame.ClearChildren();
            
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.85f), selectedItemFrame.RectTransform, Anchor.Center) { RelativeOffset = new Vector2(0.0f, -0.05f) }) { RelativeSpacing = 0.03f, Stretch = true };

            if (targetItem.TargetItem.sprite != null)
            {
                GUIImage img = new GUIImage(new RectTransform(new Point(40, 40), paddedFrame.RectTransform),
                    targetItem.TargetItem.sprite, scaleToFit: true)
                {
                    Color = targetItem.TargetItem.SpriteColor
                };
            }
            new GUITextBlock(new RectTransform(new Point(paddedFrame.Rect.Width - 70, 40), paddedFrame.RectTransform) { AbsoluteOffset = new Point(60, 0) },
                targetItem.TargetItem.Name, textAlignment: Alignment.CenterLeft, wrap: true)
            {
                IgnoreLayoutGroups = true
            };

            if (!string.IsNullOrWhiteSpace(targetItem.TargetItem.Description))
            {
                var description = new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform),
                    targetItem.TargetItem.Description,
                    font: GUI.SmallFont, wrap: true);
            }
            
            List<Skill> inadequateSkills = new List<Skill>();
            if (Character.Controlled != null)
            {
                inadequateSkills = targetItem.RequiredSkills.FindAll(skill => Character.Controlled.GetSkillLevel(skill.Identifier) < skill.Level);
            }
            
            string text;
            text = TextManager.Get("FabricatorRequiredItems")+ ":\n";
            foreach (FabricableItem.RequiredItem requiredItem in targetItem.RequiredItems)
            {
                text += "   - " + requiredItem.ItemPrefab.Name + " x" + requiredItem.Amount + (requiredItem.MinCondition < 1.0f ? ", " + requiredItem.MinCondition * 100 + "% " + TextManager.Get("FabricatorRequiredCondition") + "\n" : "\n");
            }
            text += '\n' + TextManager.Get("FabricatorRequiredTime") + ": " + targetItem.RequiredTime + " s";

            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform), text, textColor: Color.White, font: GUI.SmallFont);
            
            if (targetItem.RequiredSkills.Any())
            {
                text = TextManager.Get("FabricatorRequiredSkills") + ":\n";
                foreach (Skill skill in inadequateSkills)
                {
                    text += "   - " + TextManager.Get("SkillName." + skill.Identifier) + " " + TextManager.Get("Lvl").ToLower() + " " + skill.Level + "\n";
                }
                new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform), text, 
                    textColor: inadequateSkills.Any() ? Color.Red : Color.White, font: GUI.SmallFont);
            }
                
            progressBar = new GUIProgressBar(new RectTransform(new Point(paddedFrame.Rect.Width, 20), paddedFrame.RectTransform),
                barSize: 0.0f, color: Color.Green)
            {
                IsHorizontal = true
            };

            activateButton = new GUIButton(new RectTransform(new Vector2(0.4f, 0.08f), selectedItemFrame.RectTransform, Anchor.BottomCenter) { RelativeOffset = new Vector2(0.0f, 0.03f) },
                TextManager.Get("FabricatorCreate"))
            {
                OnClicked = StartButtonClicked,
                IgnoreLayoutGroups = true,
                UserData = targetItem,
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
                activateButton.Enabled = CanBeFabricated(targetItem, character);
            }

            if (character != null)
            {
                bool itemsChanged = false;
                if (prevContainedItems == null)
                {
                    itemsChanged = true;
                }
                else
                {
                    var itemContainer = item.GetComponent<ItemContainer>();
                    for (int i = 0; i < itemContainer.Inventory.Items.Length; i++)
                    {
                        if (prevContainedItems[i] != itemContainer.Inventory.Items[i])
                        {
                            itemsChanged = true;
                            break;
                        }
                    }
                }

                if (itemsChanged) CheckFabricableItems(character);
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