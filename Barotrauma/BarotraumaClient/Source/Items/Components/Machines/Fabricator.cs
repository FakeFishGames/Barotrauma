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

        partial void InitProjSpecific()
        {
            var paddedFrame = new GUIFrame(new RectTransform(new Vector2(0.9f, 0.9f), GuiFrame.RectTransform, Anchor.Center), style: null);

            itemList = new GUIListBox(new RectTransform(new Vector2(0.47f, 1.0f), paddedFrame.RectTransform))
            {
                OnSelected = SelectItem
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
                    fi.TargetItem.Name)
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
        
        private bool SelectItem(GUIComponent component, object obj)
        {
            FabricableItem targetItem = obj as FabricableItem;
            if (targetItem == null) return false;

            if (selectedItemFrame != null) GuiFrame.RemoveChild(selectedItemFrame);
            
            selectedItemFrame = new GUIFrame(new RectTransform(new Vector2(0.47f, 0.8f), GuiFrame.Children[0].RectTransform, Anchor.CenterRight),
                style: "InnerFrame");
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), selectedItemFrame.RectTransform, Anchor.Center)) { RelativeSpacing = 0.05f };

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
                inadequateSkills = targetItem.RequiredSkills.FindAll(skill => Character.Controlled.GetSkillLevel(skill.Name) < skill.Level);
            }

            Color textColor = Color.White;
            string text;
            if (!inadequateSkills.Any())
            {
                text = TextManager.Get("FabricatorRequiredItems")+ ":\n";
                foreach (Tuple<ItemPrefab, int, float, bool> ip in targetItem.RequiredItems)
                {
                    text += "   - " + ip.Item1.Name + " x" + ip.Item2 + (ip.Item3 < 1.0f ? ", " + ip.Item3 * 100 + "% " + TextManager.Get("FabricatorRequiredCondition") + "\n" : "\n");
                }
                text += '\n' + TextManager.Get("FabricatorRequiredTime") + ": " + targetItem.RequiredTime + " s";
            }
            else
            {
                text = TextManager.Get("FabricatorRequiredSkills") + ":\n";
                foreach (Skill skill in inadequateSkills)
                {
                    text += "   - " + skill.Name + " " + TextManager.Get("Lvl").ToLower() + " " + skill.Level + "\n";
                }

                textColor = Color.Red;
            }
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.0f), paddedFrame.RectTransform), text, textColor: textColor, font: GUI.SmallFont);

            progressBar = new GUIProgressBar(new RectTransform(new Point(paddedFrame.Rect.Width, 20), paddedFrame.RectTransform),
                barSize: 0.0f, color: Color.Green)
            {
                IsHorizontal = true
            };

            activateButton = new GUIButton(new RectTransform(new Point(100, 20), paddedFrame.RectTransform, Anchor.BottomCenter),
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
        
        public override void AddToGUIUpdateList()
        {
            GuiFrame.AddToGUIUpdateList();
        }

        public override void UpdateHUD(Character character, float deltaTime)
        {
            FabricableItem targetItem = itemList.SelectedData as FabricableItem;
            if (targetItem != null)
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