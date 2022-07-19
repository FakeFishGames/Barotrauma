using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGiveMoney : CharacterAbility
    {
        public override bool AppliesEffectOnIntervalUpdate => true;

        private readonly int amount;
        private readonly Identifier scalingStatIdentifier;

        public CharacterAbilityGiveMoney(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            amount = abilityElement.GetAttributeInt("amount", 0);
            scalingStatIdentifier = abilityElement.GetAttributeIdentifier("scalingstatidentifier", Identifier.Empty);
        }

        private void ApplyEffectSpecific(Character targetCharacter)
        {
            float multiplier = 1f;
            if (!scalingStatIdentifier.IsEmpty)
            {
                multiplier = 0 + Character.Info.GetSavedStatValue(StatTypes.None, scalingStatIdentifier);
            }

            int totalAmount = (int)(multiplier * amount);
            targetCharacter.GiveMoney(totalAmount);
            GameAnalyticsManager.AddMoneyGainedEvent(totalAmount, GameAnalyticsManager.MoneySource.Ability, CharacterTalent.Prefab.Identifier.Value);
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilityCharacter)?.Character is Character targetCharacter)
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
