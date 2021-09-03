using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    abstract class AbilityConditionDataless : AbilityCondition
    {
        public AbilityConditionDataless(CharacterTalent characterTalent, XElement conditionElement) : base (characterTalent, conditionElement) { }

        protected abstract bool MatchesConditionSpecific();
        public override bool MatchesCondition()
        {
            return invert ? !MatchesConditionSpecific() : MatchesConditionSpecific();
        }

        public override bool MatchesCondition(object abilityData)
        {
            return invert ? !MatchesConditionSpecific() : MatchesConditionSpecific();
        }
    }
}
