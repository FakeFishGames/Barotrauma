using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityApplyStatusEffects : CharacterAbility
    {
        public override bool AppliesEffectOnIntervalUpdate => true;
        public override bool AllowClientSimulation => true;

        protected readonly List<StatusEffect> statusEffects;

        public CharacterAbilityApplyStatusEffects(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            statusEffects = CharacterAbilityGroup.ParseStatusEffects(CharacterTalent, abilityElement.GetChildElement("statuseffects"));
        }

        protected void ApplyEffectSpecific(Character targetCharacter)
        {
            foreach (var statusEffect in statusEffects)
            {
                statusEffect.Apply(ActionType.OnAbility, EffectDeltaTime, Character, targetCharacter);
            }
        }
        
        protected override void ApplyEffect()
        {
            ApplyEffectSpecific(Character);
        }

        protected override void ApplyEffect(object abilityData)
        {
            if (abilityData is Character targetCharacter)
            {
                ApplyEffectSpecific(targetCharacter);
            }
            else 
            {
            	ApplyEffect();
            }
        }
    }
}
