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
            // TODO: Should we only check whether the target is ragdolling here?
            // Or should we use character.IsKnockedDown instead?
            return (character.IsRagdolled && !character.AnimController.IsHangingWithRope) || character.Stun > 0f || character.IsIncapacitated;
        }
    }
}
