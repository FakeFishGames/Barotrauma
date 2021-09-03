using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityApplyStatusEffects : CharacterAbility
    {
        public override bool AppliesEffectOnIntervalUpdate => true;
        public override bool AllowClientSimulation => true;

        protected readonly List<StatusEffect> statusEffects;

        private readonly bool applyToSelected;

        readonly List<ISerializableEntity> targets = new List<ISerializableEntity>();

        public CharacterAbilityApplyStatusEffects(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            statusEffects = CharacterAbilityGroup.ParseStatusEffects(CharacterTalent, abilityElement.GetChildElement("statuseffects"));
            applyToSelected = abilityElement.GetAttributeBool("applytoselected", false);
        }

        protected void ApplyEffectSpecific(Character targetCharacter)
        {
            foreach (var statusEffect in statusEffects)
            {
                if (statusEffect.HasTargetType(StatusEffect.TargetType.UseTarget))
                {
                    // currently used this to spawn items on the targeted character
                    statusEffect.SetUser(targetCharacter);
                    statusEffect.Apply(ActionType.OnAbility, EffectDeltaTime, targetCharacter, targetCharacter);
                }
                else if (statusEffect.HasTargetType(StatusEffect.TargetType.NearbyCharacters))
                {
                    targets.Clear();
                    targets.AddRange(statusEffect.GetNearbyTargets(targetCharacter.WorldPosition, targets));
                    statusEffect.Apply(ActionType.OnAbility, EffectDeltaTime, targetCharacter, targets);
                }
                else if (statusEffect.HasTargetType(StatusEffect.TargetType.This))
                {
                    statusEffect.SetUser(Character);
                    statusEffect.Apply(ActionType.OnAbility, EffectDeltaTime, Character, Character);
                }
                else
                {
                    statusEffect.Apply(ActionType.OnAbility, EffectDeltaTime, Character, targetCharacter);
                }
            }
        }
        
        protected override void ApplyEffect()
        {
            ApplyEffectSpecific(Character);
        }

        protected override void ApplyEffect(object abilityData)
        {
            if (applyToSelected && Character.SelectedCharacter is Character selectedCharacter)
            {
                ApplyEffectSpecific(selectedCharacter);
            }
            else if ((abilityData as Character ?? (abilityData as IAbilityCharacter)?.Character) is Character targetCharacter)
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
