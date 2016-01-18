using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class FixRequirement
    {
        string name;

        private static GUIFrame frame;

        List<Skill> requiredSkills;
        List<string> requiredItems;

        public bool Fixed;

        public FixRequirement(XElement element)
        {
            name = ToolBox.GetAttributeString(element, "name", "");

            requiredSkills = new List<Skill>();
            requiredItems = new List<string>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLower())
                {
                    case "skill":
                        string skillName = ToolBox.GetAttributeString(subElement, "name", "");
                        int level = ToolBox.GetAttributeInt(subElement, "level", 1);

                        requiredSkills.Add(new Skill(skillName, level));
                        break;
                    case "item":
                        string itemName = ToolBox.GetAttributeString(subElement, "name", "");

                        requiredItems.Add(itemName);
                        break;
                }
            }
        }

        public bool CanBeFixed(Character character, GUIComponent reqFrame)
        {
            bool success = true;
            foreach (string itemName in requiredItems)
            {
                GUIComponent component = reqFrame.children.Find(c => c.UserData as string == itemName);
                
                GUITextBlock text = component as GUITextBlock;
                Item item = character.Inventory.FindItem(itemName);
                bool itemFound = (item != null);
               
                if (!itemFound) success = false;
                
                if (text != null) text.TextColor = itemFound ? Color.LightGreen : Color.Red;
            }

            foreach (Skill skill in requiredSkills)
            {
                GUIComponent component = reqFrame.children.Find(c => c.UserData as Skill == skill);
                GUITextBlock text = component as GUITextBlock;

                float characterSkill = character.GetSkillLevel(skill.Name);
                bool sufficientSkill = characterSkill >= skill.Level;

                if (!sufficientSkill) success = false;

                if (text != null) text.TextColor = sufficientSkill ? Color.LightGreen : Color.Red;
            }

            return success;
        }
               
        private static void CreateGUIFrame(Item item)
        {
            int width = 400, height = 500;
            int y = 0;

            frame = new GUIFrame(new Rectangle(0, 0, width, height), null, Alignment.Center, GUI.Style);
            frame.Padding = new Vector4(20.0f, 20.0f, 20.0f, 20.0f);
            frame.UserData = item;

            new GUITextBlock(new Rectangle(0,0,200,20), "Attempting to fix " + item.Name, GUI.Style, frame);

            y = y + 40;
            foreach (FixRequirement requirement in item.FixRequirements)
            {
                GUIFrame reqFrame = new GUIFrame(
                    new Rectangle(0, y, 0, 20 + Math.Max(requirement.requiredItems.Count, requirement.requiredSkills.Count) * 15), 
                    Color.Transparent, null, frame);
                reqFrame.UserData = requirement;


                var fixButton = new GUIButton(new Rectangle(0, 0, 50, 20), "Fix", GUI.Style, reqFrame);
                fixButton.OnClicked = FixButtonPressed;
                fixButton.UserData = requirement;

                var tickBox = new GUITickBox(new Rectangle(70, 0, 20,20), requirement.name, Alignment.Left, reqFrame);
                tickBox.Enabled = false;

                int y2 = 20;
                foreach (string itemName in requirement.requiredItems)
                {
                    var itemBlock = new GUITextBlock(new Rectangle(30, y2, 200, 15), itemName, GUI.Style, reqFrame);
                    itemBlock.Font = GUI.SmallFont;
                    itemBlock.UserData = itemName;
                    
                    y2 += 15;
                }
                
                y2 = 20;
                foreach (Skill skill in requirement.requiredSkills)
                {
                    var skillBlock = new GUITextBlock(new Rectangle(0, y2, 200, 15), skill.Name + " - " + skill.Level, GUI.Style, Alignment.Right, Alignment.TopLeft, reqFrame);
                    skillBlock.Font = GUI.SmallFont;
                    skillBlock.UserData = skill;


                    y2 += 15;
                }

                y += reqFrame.Rect.Height;
            }
        }

        private static bool FixButtonPressed(GUIButton button, object obj)
        {
            FixRequirement requirement = obj as FixRequirement;
            if (requirement == null) return false;

            if (!requirement.CanBeFixed(Character.Controlled, button.Parent)) return true;

            requirement.Fixed = true;

            Item item = frame.UserData as Item;
            if (item == null) return true;

            new NetworkEvent(NetworkEventType.ItemFixed, item.ID, true, (byte)item.FixRequirements.IndexOf(requirement) );

            return true;
        }

        private static void UpdateGUIFrame(Item item, Character character)
        {
            if (frame == null) return;

            bool unfixedFound = false;
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
                    bool canBeFixed = requirement.CanBeFixed(character, child);
                    unfixedFound = true;
                    //child.GetChild<GUITickBox>().Selected = canBeFixed;
                    GUITickBox tickBox = child.GetChild<GUITickBox>();
                    if (tickBox.Selected)
                    {
                        tickBox.Selected = canBeFixed;
                        requirement.Fixed = canBeFixed;

                    }
                    child.Color = Color.Red * 0.2f;
                    //tickBox.State = GUIComponent.ComponentState.None;
                }
            }
            if (!unfixedFound)
            {
                item.Condition = 100.0f;
                frame = null;
            }
        }

        public static void DrawHud(SpriteBatch spriteBatch, Item item, Character character)
        {
            if (frame == null || frame.UserData != item)
            {
                CreateGUIFrame(item);
                
            }
            UpdateGUIFrame(item, character);

            if (frame == null) return;

            frame.Update((float)Physics.step);
            frame.Draw(spriteBatch);
        }
    }
}
