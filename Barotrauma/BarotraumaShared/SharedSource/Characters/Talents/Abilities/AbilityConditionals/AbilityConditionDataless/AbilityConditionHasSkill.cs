using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionHasSkill : AbilityConditionDataless
    {
        private readonly string skillIdentifier;
        private readonly float minValue;

        public AbilityConditionHasSkill(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement)
        {
            skillIdentifier = conditionElement.GetAttributeString("skillidentifier", string.Empty);
            minValue = conditionElement.GetAttributeFloat("minvalue", 0f);
        }

        protected override bool MatchesConditionSpecific()
        {
            return character.GetSkillLevel(skillIdentifier) >= minValue;
        }
    }
}
