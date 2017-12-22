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
            GuiFrame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);

            itemList = new GUIListBox(new Rectangle(0, 0, GuiFrame.Rect.Width / 2 - 20, 0), "", GuiFrame);
            itemList.OnSelected = SelectItem;

            foreach (FabricableItem fi in fabricableItems)
            {
                GUIFrame frame = new GUIFrame(new Rectangle(0, 0, 0, 50), Color.Transparent, null, itemList)
                {
                    UserData = fi,
                    Padding = new Vector4(5.0f, 5.0f, 5.0f, 5.0f),
                    HoverColor = Color.Gold * 0.2f,
                    SelectedColor = Color.Gold * 0.5f,
                    ToolTip = fi.TargetItem.Description
                };

                GUITextBlock textBlock = new GUITextBlock(
                    new Rectangle(40, 0, 0, 25),
                    fi.TargetItem.Name,
                    Color.Transparent, Color.White,
                    Alignment.Left, Alignment.Left,
                    null, frame);
                textBlock.ToolTip = fi.TargetItem.Description;
                textBlock.Padding = new Vector4(5.0f, 0.0f, 5.0f, 0.0f);

                if (fi.TargetItem.sprite != null)
                {
                    GUIImage img = new GUIImage(new Rectangle(0, 0, 40, 40), fi.TargetItem.sprite, Alignment.Left, frame);
                    img.Scale = Math.Min(Math.Min(40.0f / img.SourceRect.Width, 40.0f / img.SourceRect.Height), 1.0f);
                    img.Color = fi.TargetItem.SpriteColor;
                    img.ToolTip = fi.TargetItem.Description;
                }
            }
        }
        
        private bool SelectItem(GUIComponent component, object obj)
        {
            FabricableItem targetItem = obj as FabricableItem;
            if (targetItem == null) return false;

            if (selectedItemFrame != null) GuiFrame.RemoveChild(selectedItemFrame);

            //int width = 200, height = 150;
            selectedItemFrame = new GUIFrame(new Rectangle(0, 0, (int)(GuiFrame.Rect.Width * 0.4f), 300), Color.Black * 0.8f, Alignment.CenterY | Alignment.Right, null, GuiFrame);

            selectedItemFrame.Padding = new Vector4(10.0f, 10.0f, 10.0f, 10.0f);

            progressBar = new GUIProgressBar(new Rectangle(0, 0, 0, 20), Color.Green, "", 0.0f, Alignment.BottomCenter, selectedItemFrame);
            progressBar.IsHorizontal = true;

            if (targetItem.TargetItem.sprite != null)
            {
                int y = 0;

                GUIImage img = new GUIImage(new Rectangle(10, 0, 40, 40), targetItem.TargetItem.sprite, Alignment.TopLeft, selectedItemFrame);
                img.Scale = Math.Min(Math.Min(40.0f / img.SourceRect.Width, 40.0f / img.SourceRect.Height), 1.0f);
                img.Color = targetItem.TargetItem.SpriteColor;

                new GUITextBlock(
                    new Rectangle(60, 0, 0, 25),
                    targetItem.TargetItem.Name,
                    Color.Transparent, Color.White,
                    Alignment.TopLeft,
                    Alignment.TopLeft, null,
                    selectedItemFrame, true);

                y += 40;

                if (!string.IsNullOrWhiteSpace(targetItem.TargetItem.Description))
                {
                    var description = new GUITextBlock(
                        new Rectangle(0, y, 0, 0),
                        targetItem.TargetItem.Description,
                        "", Alignment.TopLeft, Alignment.TopLeft,
                        selectedItemFrame, true, GUI.SmallFont);

                    y += description.Rect.Height + 10;
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
                    text = "Required items:\n";
                    foreach (Tuple<ItemPrefab, int, float, bool> ip in targetItem.RequiredItems)
                    {
                        text += "   - " + ip.Item1.Name + " x" + ip.Item2 + (ip.Item3 < 1.0f ? ", " + ip.Item3 * 100 + "% condition\n" : "\n");
                    }
                    text += "Required time: " + targetItem.RequiredTime + " s";
                }
                else
                {
                    text = "Skills required to calibrate:\n";
                    foreach (Skill skill in inadequateSkills)
                    {
                        text += "   - " + skill.Name + " lvl " + skill.Level + "\n";
                    }

                    textColor = Color.Red;
                }

                new GUITextBlock(
                    new Rectangle(0, y, 0, 25),
                    text,
                    Color.Transparent, textColor,
                    Alignment.TopLeft,
                    Alignment.TopLeft, null,
                    selectedItemFrame);

                activateButton = new GUIButton(new Rectangle(0, -30, 100, 20), "Create", Color.White, Alignment.CenterX | Alignment.Bottom, "", selectedItemFrame);
                activateButton.OnClicked = StartButtonClicked;
                activateButton.UserData = targetItem;
                activateButton.Enabled = false;
            }

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

        public override void DrawHUD(SpriteBatch spriteBatch, Character character)
        {
            GuiFrame.Draw(spriteBatch);
        }

        public override void AddToGUIUpdateList()
        {
            GuiFrame.AddToGUIUpdateList();
        }

        public override void UpdateHUD(Character character)
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


            GuiFrame.Update((float)Timing.Step);
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