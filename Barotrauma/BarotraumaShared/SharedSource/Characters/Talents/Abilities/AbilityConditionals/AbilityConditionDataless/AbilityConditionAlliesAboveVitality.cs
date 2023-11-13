using System.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionAlliesAboveVitality : AbilityConditionDataless
    {
        readonly float vitalityPercentage;

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
