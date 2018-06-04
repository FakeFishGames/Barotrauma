using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;

namespace Barotrauma
{
    partial class FixRequirement
    {
        private static GUIFrame frame;
        private static GUIComponent requirementContainer;
        private static GUIComponent itemTextContainer;
        private static GUIComponent skillTextContainer;

        public bool CanBeFixed(Character character, GUIComponent reqFrame = null)
        {
            foreach (string itemName in requiredItems)
            {
                Item item = character.Inventory.FindItem(itemName);
                bool itemFound = (item != null);

                if (reqFrame != null)
                {
                    GUIComponent component = reqFrame.GetChildByUserData(itemName);
                    if (component is GUITextBlock text) text.TextColor = itemFound ? Color.LightGreen : Color.Red;
                }
            }

            foreach (Skill skill in RequiredSkills)
            {
                float characterSkill = character.GetSkillLevel(skill.Name);
                bool sufficientSkill = characterSkill >= skill.Level;

                if (reqFrame != null)
                {
                    GUIComponent component = reqFrame.GetChildByUserData(skill);
                    GUITextBlock text = component as GUITextBlock;
                    if (text != null) text.TextColor = sufficientSkill ? Color.LightGreen : Color.Orange;
                }
            }

            return CanBeFixed(character);
        }

        private static void CreateGUIFrame(Item item)
        {
            int width = 400, height = 80;
            foreach (FixRequirement requirement in item.FixRequirements)
            {
                height += 60 + Math.Max(requirement.requiredItems.Count, requirement.RequiredSkills.Count) * 15;
            }

            frame = new GUIFrame(new RectTransform(new Point(width, height), GUI.Canvas, Anchor.Center))
            {
                UserData = item
            };
            var paddedFrame = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.8f), frame.RectTransform, Anchor.Center))
            {
                Stretch = true
            };
            
            new GUITextBlock(new RectTransform(new Vector2(1.0f, 0.15f), paddedFrame.RectTransform),
                TextManager.Get("FixHeader").Replace("[itemname]", item.Name));
            
            foreach (FixRequirement requirement in item.FixRequirements)
            {
                int maxRequirementCount = Math.Max(requirement.requiredItems.Count, requirement.RequiredSkills.Count);
                GUIFrame reqFrame = new GUIFrame(new RectTransform(new Point(paddedFrame.Rect.Width, 60 + maxRequirementCount * 15), paddedFrame.RectTransform), style: null, color: Color.Black)
                {
                    UserData = requirement
                };

                GUIFrame paddedReqFrame = new GUIFrame(new RectTransform(new Vector2(0.95f, 0.9f), reqFrame.RectTransform), style: null)
                {
                    UserData = requirement
                };
                itemTextContainer = paddedReqFrame;
                skillTextContainer = paddedReqFrame;

                var tickBox = new GUITickBox(new RectTransform(new Point(20), paddedReqFrame.RectTransform), requirement.name)
                {
                    CanBeFocused = false,
                    Enabled = false
                };

                int y2 = 20;
                foreach (string itemName in requirement.requiredItems)
                {
                    var itemBlock = new GUITextBlock(new RectTransform(new Point(200, 15), paddedReqFrame.RectTransform) { AbsoluteOffset = new Point(30, y2) },
                        itemName, font: GUI.SmallFont)
                    {
                        UserData = itemName
                    };
                    y2 += 15;
                }

                y2 = 20;
                foreach (Skill skill in requirement.RequiredSkills)
                {
                    var skillBlock = new GUITextBlock(new RectTransform(new Point(200, 15), paddedReqFrame.RectTransform, Anchor.TopRight) { AbsoluteOffset = new Point(0, y2)},
                        skill.Name + " - " + skill.Level, font: GUI.SmallFont)
                    {
                        Font = GUI.SmallFont,
                        UserData = skill
                    };
                    y2 += 15;
                }

                var progressBarArea = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.2f), paddedReqFrame.RectTransform, Anchor.BottomCenter), isHorizontal: true)
                {
                    Stretch = true,
                    RelativeSpacing = 0.05f
                };

                var fixButton = new GUIButton(new RectTransform(new Vector2(0.2f, 1.0f), progressBarArea.RectTransform),
                    TextManager.Get("FixButton"))
                {
                    OnClicked = FixButtonPressed,
                    UserData = requirement
                };

                var fixProgressBar = new GUIProgressBar(new RectTransform(new Vector2(0.8f, 1.0f), progressBarArea.RectTransform),
                    color: Color.Green, barSize: 0.0f)
                {
                    IsHorizontal = true,
                    ProgressGetter = () => { return requirement.fixProgress; }
                };
            }

            requirementContainer = paddedFrame;
        }

        private static bool FixButtonPressed(GUIButton button, object obj)
        {
            FixRequirement requirement = obj as FixRequirement;
            if (requirement == null) return true;

            Item item = frame.UserData as Item;
            if (item == null) return true;

            if (!requirement.CanBeFixed(Character.Controlled, button.Parent)) return true;

            if (GameMain.Client != null)
            {
                GameMain.Client.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.Repair, item.FixRequirements.IndexOf(requirement) });
            }
            else if (GameMain.Server != null)
            {
                GameMain.Server.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.Status });
                requirement.CurrentFixer = Character.Controlled;
            }
            else
            {
                requirement.CurrentFixer = Character.Controlled;
            }
            
            return true;
        }

        private static void UpdateGUIFrame(Item item, Character character)
        {
            if (frame == null) return;

            foreach (GUIComponent child in requirementContainer.Children)
            {
                FixRequirement requirement = child.UserData as FixRequirement;
                if (requirement == null) continue;

                if (requirement.Fixed)
                {
                    child.Color = Color.LightGreen * 0.3f;
                    child.Children.First().GetChild<GUITickBox>().Selected = true;
                }
                else
                {
                    foreach (string itemName in requirement.requiredItems)
                    {
                        bool itemFound = (character.Inventory.FindItem(itemName) != null);
                        
                        GUIComponent component = itemTextContainer.GetChildByUserData(itemName);
                        if (component is GUITextBlock text) text.TextColor = itemFound ? Color.LightGreen : Color.Red;
                    }

                    foreach (Skill skill in requirement.RequiredSkills)
                    {
                        float characterSkill = character.GetSkillLevel(skill.Name);
                        bool sufficientSkill = characterSkill >= skill.Level;

                        GUIComponent component = skillTextContainer.GetChildByUserData(skill);
                        if (component is GUITextBlock text) text.TextColor = sufficientSkill ? Color.LightGreen : Color.Red;
                    }
                    child.Color = Color.Red * 0.2f;
                }
            }
        }
        
        public static void AddToGUIUpdateList()
        {
            frame?.AddToGUIUpdateList();
        }

        public static void UpdateHud(Item item, Character character)
        {
            if (frame == null || frame.UserData != item)
            {
                CreateGUIFrame(item);
            }
            UpdateGUIFrame(item, character);
        }
    }
}
