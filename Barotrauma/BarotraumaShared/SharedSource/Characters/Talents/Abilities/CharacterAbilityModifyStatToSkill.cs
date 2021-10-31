using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityModifyStatToSkill : CharacterAbility
    {
        private readonly StatTypes statType;
        private readonly float maxValue;
        private readonly string skillIdentifier;
        private readonly bool useAll;
        private float lastValue = 0f;

        public CharacterAbilityModifyStatToSkill(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            statType = CharacterAbilityGroup.ParseStatType(abilityElement.GetAttributeString("stattype", ""), CharacterTalent.DebugIdentifier);
            maxValue = abilityElement.GetAttributeFloat("maxvalue", 0f);
            skillIdentifier = abilityElement.GetAttributeString("skillidentifier", string.Empty);
            useAll = skillIdentifier == "all";
        }

        protected override void VerifyState(bool conditionsMatched, float timeSinceLastUpdate)
        {
            Character.ChangeStat(statType, -lastValue);

            if (conditionsMatched)
            {
                float skillTotal = 0f;

                if (useAll && Character.Info?.Job != null)
                {
                    foreach (Skill skill in Character.Info.Job.Skills)
                    {
                        skillTotal += Character.GetSkillLevel(skill.Identifier);
                    }
                    skillTotal /= Character.Info.Job.Skills.Count;
                }
                else
                {
                    skillTotal = Character.GetSkillLevel(skillIdentifier);
                }

                lastValue = skillTotal / 100f * maxValue;
                Character.ChangeStat(statType, lastValue);
            }
            else
            {
                lastValue = 0f;
            }
        }
    }
}
