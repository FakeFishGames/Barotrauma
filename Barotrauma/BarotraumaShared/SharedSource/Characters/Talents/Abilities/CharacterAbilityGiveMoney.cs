using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGiveMoney : CharacterAbility
    {
        public override bool AppliesEffectOnIntervalUpdate => true;

        private readonly int amount;
        private StatTypes scalingStatType;

        public CharacterAbilityGiveMoney(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            amount = abilityElement.GetAttributeInt("amount", 0);
            scalingStatType = CharacterAbilityGroup.ParseStatType(abilityElement.GetAttributeString("scalingstattype", "None"), CharacterTalent.DebugIdentifier);
        }

        private void ApplyEffectSpecific(Character targetCharacter)
        {
            float multiplier = 1f;
            if (scalingStatType != StatTypes.None)
            {
                multiplier = 0 + Character.Info.GetSavedStatValue(scalingStatType);
            }

            targetCharacter.GiveMoney((int)(multiplier * amount));
        }

        protected override void ApplyEffect(object abilityData)
        {
            if ((abilityData as Character ?? (abilityData as IAbilityCharacter)?.Character) is Character targetCharacter)
            {
                ApplyEffectSpecific(targetCharacter);
            }
            else
            {
                ApplyEffectSpecific(Character);
            }
        }

        protected override void ApplyEffect()
        {
            ApplyEffectSpecific(Character);
        }
    }
}
