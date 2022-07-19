using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionRagdolled : AbilityConditionDataless
    {

        public AbilityConditionRagdolled(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
        }

        protected override bool MatchesConditionSpecific()
        {
            return character.IsRagdolled || character.Stun > 0f || character.IsIncapacitated;
        }
    }
}
