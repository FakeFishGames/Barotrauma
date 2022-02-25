using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityApplyStatusEffectsToAttacker : CharacterAbilityApplyStatusEffects
    {
        public CharacterAbilityApplyStatusEffectsToAttacker(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if ((abilityObject as AbilityAttackData)?.Attacker is Character attacker)
            {
                ApplyEffectSpecific(attacker);
            }
        }
    }
}
