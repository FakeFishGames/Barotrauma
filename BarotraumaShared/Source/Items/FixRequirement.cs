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
    partial class FixRequirement
    {
        string name;

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
                switch (subElement.Name.ToString().ToLowerInvariant())
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

        public bool CanBeFixed(Character character)
        {
            if (character == null) return false;

            bool success = true;
            foreach (string itemName in requiredItems)
            {
                Item item = character.Inventory.FindItem(itemName);
                bool itemFound = (item != null);
               
                if (!itemFound) success = false;
            }

            foreach (Skill skill in requiredSkills)
            {
                float characterSkill = character.GetSkillLevel(skill.Name);
                bool sufficientSkill = characterSkill >= skill.Level;

                if (!sufficientSkill) success = false;
            }

            return success;
        }
    }
}
