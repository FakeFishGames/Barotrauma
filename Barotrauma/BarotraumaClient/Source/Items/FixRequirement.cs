using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Barotrauma
{
    partial class FixRequirement
    {
        private static GUIFrame frame;

        public bool CanBeFixed(Character character, GUIComponent reqFrame = null)
        {
            foreach (string itemName in requiredItems)
            {
                Item item = character.Inventory.FindItem(itemName);
                bool itemFound = (item != null);

                if (reqFrame != null)
                {
                    GUIComponent component = reqFrame.children.Find(c => c.UserData as string == itemName);
                    GUITextBlock text = component as GUITextBlock;
                    if (text != null) text.TextColor = itemFound ? Color.LightGreen : Color.Red;
                }
            }

            foreach (Skill skill in RequiredSkills)
            {
                float characterSkill = character.GetSkillLevel(skill.Name);
                bool sufficientSkill = characterSkill >= skill.Level;

                if (reqFrame != null)
                {
                    GUIComponent component = reqFrame.children.Find(c => c.UserData as Skill == skill);
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
            int y = 0;

            frame = new GUIFrame(new Rectangle(0, 0, width, height), null, Alignment.Center, "");
            frame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);
            frame.UserData = item;

            new GUITextBlock(new Rectangle(0, 0, 200, 20), TextManager.Get("FixHeader").Replace("[itemname]", item.Name), "", frame);

            y = y + 40;
            foreach (FixRequirement requirement in item.FixRequirements)
            {
                int maxRequirementCount = Math.Max(requirement.requiredItems.Count, requirement.RequiredSkills.Count);
                GUIFrame reqFrame = new GUIFrame(
                    new Rectangle(0, y, 0, 60 + maxRequirementCount * 15),
                    Color.Transparent, null, frame);
                reqFrame.UserData = requirement;

                var tickBox = new GUITickBox(new Rectangle(0, 0, 20, 20), requirement.name, Alignment.Left, reqFrame);
                tickBox.CanBeFocused = false;
                tickBox.Enabled = false;

                int y2 = 20;
                foreach (string itemName in requirement.requiredItems)
                {
                    var itemBlock = new GUITextBlock(new Rectangle(30, y2, 200, 15), itemName, "", reqFrame);
                    itemBlock.Font = GUI.SmallFont;
                    itemBlock.UserData = itemName;

                    y2 += 15;
                }

                y2 = 20;
                foreach (Skill skill in requirement.RequiredSkills)
                {
                    var skillBlock = new GUITextBlock(new Rectangle(0, y2, 200, 15), skill.Name + " - " + skill.Level, "", Alignment.Right, Alignment.TopLeft, reqFrame);
                    skillBlock.Font = GUI.SmallFont;
                    skillBlock.UserData = skill;


                    y2 += 15;
                }
                                
                var fixButton = new GUIButton(new Rectangle(0, 0, 50, 20), TextManager.Get("FixButton"), Alignment.BottomLeft, "", reqFrame);
                fixButton.OnClicked = FixButtonPressed;
                fixButton.UserData = requirement;

                var fixProgressBar = new GUIProgressBar(new Rectangle(60,0,0,20), Color.Green, 0.0f, Alignment.BottomLeft, reqFrame);
                fixProgressBar.IsHorizontal = true;
                fixProgressBar.ProgressGetter += () => { return requirement.fixProgress; };

                y += reqFrame.Rect.Height;
            }
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
            
            foreach (GUIComponent child in frame.children)
            {
                FixRequirement requirement = child.UserData as FixRequirement;
                if (requirement == null) continue;

                if (requirement.Fixed)
                {
                    child.Color = Color.LightGreen * 0.3f;
                    child.GetChild<GUITickBox>().Selected = true;
                }
                else
                {
                    foreach (string itemName in requirement.requiredItems)
                    {
                        bool itemFound = (character.Inventory.FindItem(itemName) != null);
                        
                        GUIComponent component = child.children.Find(c => c.UserData as string == itemName);
                        GUITextBlock text = component as GUITextBlock;
                        if (text != null) text.TextColor = itemFound ? Color.LightGreen : Color.Red;                        
                    }

                    foreach (Skill skill in requirement.RequiredSkills)
                    {
                        float characterSkill = character.GetSkillLevel(skill.Name);
                        bool sufficientSkill = characterSkill >= skill.Level;

                        GUIComponent component = child.children.Find(c => c.UserData as Skill == skill);
                        GUITextBlock text = component as GUITextBlock;
                        if (text != null) text.TextColor = sufficientSkill ? Color.LightGreen : Color.Red;                        
                    }
                    child.Color = Color.Red * 0.2f;
                }
            }
        }

        public static void DrawHud(SpriteBatch spriteBatch, Item item, Character character)
        {
            if (frame == null || frame.UserData != item) return;

            frame.Draw(spriteBatch);
        }

        public static void AddToGUIUpdateList()
        {
            if (frame == null) return;

            frame.AddToGUIUpdateList();
        }

        public static void UpdateHud(Item item, Character character)
        {
            if (frame == null || frame.UserData != item)
            {
                CreateGUIFrame(item);
            }
            UpdateGUIFrame(item, character);

            if (frame == null) return;

            frame.Update((float)Timing.Step);
        }
    }
}
