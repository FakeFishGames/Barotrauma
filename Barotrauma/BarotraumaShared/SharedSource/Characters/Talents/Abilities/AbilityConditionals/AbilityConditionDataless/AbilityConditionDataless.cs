using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    abstract class AbilityConditionDataless : AbilityCondition
    {
        public AbilityConditionDataless(CharacterTalent characterTalent, ContentXElement conditionElement) : base (characterTalent, conditionElement) { }

        protected abstract bool MatchesConditionSpecific();
        public override bool MatchesCondition()
        {
            return invert ? !MatchesConditionSpecific() : MatchesConditionSpecific();
        }

        public override bool MatchesCondition(AbilityObject abilityObject)
        {
            return invert ? !MatchesConditionSpecific() : MatchesConditionSpecific();
        }
    }
}
