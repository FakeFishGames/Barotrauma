using System.Xml.Linq;
using static Barotrauma.StatusEffect;

namespace Barotrauma.Abilities
{
    class AbilityConditionStatusEffectIdentifier : AbilityConditionData
    {
        private string effectIdentifier;

        public AbilityConditionStatusEffectIdentifier(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            effectIdentifier = conditionElement.GetAttributeString("effectidentifier", "").ToLowerInvariant();
        }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if (abilityObject is AbilityStatusEffectIdentifier abilityStatusEffectIdentifier)
            {
                return abilityStatusEffectIdentifier.EffectIdentifier == effectIdentifier;
            }
            else
            {
                LogAbilityConditionError(abilityObject, typeof(AbilityStatusEffectIdentifier));
                return false;
            }
        }
    }
}
