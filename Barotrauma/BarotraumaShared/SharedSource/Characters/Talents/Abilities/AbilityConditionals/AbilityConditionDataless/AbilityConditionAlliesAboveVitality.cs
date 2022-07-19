using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionAlliesAboveVitality : AbilityConditionDataless
    {
        float vitalityPercentage;

        public AbilityConditionAlliesAboveVitality(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            vitalityPercentage = conditionElement.GetAttributeFloat("vitalitypercentage", 0f);
        }
        protected override bool MatchesConditionSpecific()
        {
            return Character.GetFriendlyCrew(character).All(c => c.HealthPercentage / 100f >= vitalityPercentage);
        }
    }
}
