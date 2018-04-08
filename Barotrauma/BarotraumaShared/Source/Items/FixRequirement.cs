using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    partial class FixRequirement
    {
        public static float SkillIncreaseMultiplier = 0.1f;

        private string name;

        private readonly Item item;

        private readonly List<Skill> RequiredSkills;
        private readonly List<string> requiredItems;
        
        public bool Fixed
        {
            get { return fixProgress >= 1.0f; }
        }
        
        private float fixDurationLowSkill, fixDurationHighSkill;

        private float fixProgress;
        public float FixProgress
        {
            get { return fixProgress; }
            set
            {
                fixProgress = MathHelper.Clamp(value, 0.0f, 1.0f);
                if (fixProgress >= 1.0f && currentFixer != null) currentFixer.AnimController.Anim = AnimController.Animation.None;
            }
        }

        public List<string> RequiredItems
        {
            get { return requiredItems; }
        }

        private Character currentFixer;
        public Character CurrentFixer
        {
            get { return currentFixer; }
            set
            {
                if (currentFixer == value || item.Condition >= 100.0f) return;
                if (currentFixer != null) currentFixer.AnimController.Anim = AnimController.Animation.None;
                currentFixer = value;
            }
        }

        public FixRequirement(XElement element, Item item)
        {
            this.item = item;
            name = element.GetAttributeString("name", "");
            fixDurationLowSkill = element.GetAttributeFloat("fixdurationlowskill", 100.0f);
            fixDurationHighSkill = element.GetAttributeFloat("fixdurationhighskill", 5.0f);

            RequiredSkills = new List<Skill>();
            requiredItems = new List<string>();
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "skill":
                        string skillName = subElement.GetAttributeString("name", "");
                        int level = subElement.GetAttributeInt("level", 1);
                        RequiredSkills.Add(new Skill(skillName, level));
                        break;
                    case "item":
                        string itemName = subElement.GetAttributeString("name", "");
                        requiredItems.Add(itemName);
                        break;
                }
            }
        }

        public bool HasRequiredSkills(Character character)
        {
            foreach (Skill skill in RequiredSkills)
            {
                if (character.GetSkillLevel(skill.Name) < skill.Level) return false;
            }
            return true;
        }

        public bool HasRequiredItems(Character character)
        {
            foreach (string itemName in requiredItems)
            {
                if (character.Inventory.FindItem(itemName) == null) return false;
            }
            return true;
        }

        public bool CanBeFixed(Character character)
        {
            return character != null && HasRequiredItems(character);
        }

        public void StartFixing(Character character)
        {
            CurrentFixer = character;
        }

        public void Update(float deltaTime)
        {
            if (CurrentFixer == null || !HasRequiredItems(CurrentFixer)) return;
            
            if (CurrentFixer.SelectedConstruction != item || !currentFixer.CanInteractWith(item))
            {
                currentFixer.AnimController.Anim = AnimController.Animation.None;
                CurrentFixer = null;
                return;
            }

            UpdateFixAnimation(CurrentFixer);

            if (GameMain.Client != null) return;

            float successFactor = RequiredSkills.Count == 0 ? 1.0f : 0.0f;
            foreach (Skill skill in RequiredSkills)
            {
                float characterSkillLevel = CurrentFixer.GetSkillLevel(skill.Name);
                if (characterSkillLevel >= skill.Level) successFactor += 1.0f / RequiredSkills.Count;
                CurrentFixer.Info.IncreaseSkillLevel(skill.Name, SkillIncreaseMultiplier * deltaTime / Math.Max(characterSkillLevel, 1.0f));
            }

            float fixDuration = MathHelper.Lerp(fixDurationLowSkill, fixDurationHighSkill, successFactor);
            if (fixDuration <= 0.0f)
            {
                fixProgress = 1.0f;
            }
            else
            {
                FixProgress += deltaTime / fixDuration;
            }            
        }

        private void UpdateFixAnimation(Character character)
        {
            character.AnimController.UpdateUseItem(false, item.SimPosition + Vector2.UnitY * (fixProgress % 0.1f));
        }
    }
}
