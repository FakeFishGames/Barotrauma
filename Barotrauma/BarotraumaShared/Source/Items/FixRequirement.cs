using System.Collections.Generic;
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
            name = element.GetAttributeString("name", "");

            requiredSkills = new List<Skill>();
            requiredItems = new List<string>();

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "skill":
                        string skillName = subElement.GetAttributeString("name", "");
                        int level = subElement.GetAttributeInt("level", 1);

                        requiredSkills.Add(new Skill(skillName, level));
                        break;
                    case "item":
                        string itemName = subElement.GetAttributeString("name", "");

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
