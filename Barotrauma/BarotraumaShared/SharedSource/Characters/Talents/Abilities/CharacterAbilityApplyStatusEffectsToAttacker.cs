using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityApplyStatusEffectsToAttacker : CharacterAbilityApplyStatusEffects
    {
        public CharacterAbilityApplyStatusEffectsToAttacker(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
        }

        protected override void ApplyEffect(object abilityData)
        {
            if ((abilityData as AbilityAttackData)?.Attacker is Character attacker)
            {
                ApplyEffectSpecific(attacker);
            }
        }
    }
}
