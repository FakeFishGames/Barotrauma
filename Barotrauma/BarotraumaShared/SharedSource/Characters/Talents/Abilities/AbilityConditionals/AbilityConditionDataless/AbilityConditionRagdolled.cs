using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionRagdolled : AbilityConditionDataless
    {

        public AbilityConditionRagdolled(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement)
        {
        }

        protected override bool MatchesConditionSpecific()
        {
            return character.IsRagdolled || character.Stun > 0f || character.IsIncapacitated;
        }
    }
}
