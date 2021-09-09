using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityTaskmaster : CharacterAbility
    {
        private readonly List<StatusEffect> statusEffects;
        private readonly List<StatusEffect> statusEffectsRemove;

        private Character lastCharacter;

        public CharacterAbilityTaskmaster(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            statusEffects = CharacterAbilityGroup.ParseStatusEffects(CharacterTalent, abilityElement.GetChildElement("statuseffects"));
            statusEffectsRemove = CharacterAbilityGroup.ParseStatusEffects(CharacterTalent, abilityElement.GetChildElement("statuseffectsremove"));
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilityCharacter)?.Character is Character targetCharacter)
            {
                if (targetCharacter == Character) { return; }

                foreach (var statusEffect in statusEffectsRemove)
                {
                    statusEffect.Apply(ActionType.OnAbility, EffectDeltaTime, Character, lastCharacter);
                }

                foreach (var statusEffect in statusEffects)
                {
                    statusEffect.Apply(ActionType.OnAbility, EffectDeltaTime, Character, targetCharacter);
                }

                lastCharacter = targetCharacter;
            }
        }
    }
}
