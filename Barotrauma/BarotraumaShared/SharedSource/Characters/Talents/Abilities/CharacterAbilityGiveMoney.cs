using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGiveMoney : CharacterAbility
    {
        public override bool AppliesEffectOnIntervalUpdate => true;

        private readonly int amount;
        private readonly string scalingStatIdentifier;

        public CharacterAbilityGiveMoney(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            amount = abilityElement.GetAttributeInt("amount", 0);
            scalingStatIdentifier = abilityElement.GetAttributeString("scalingstatidentifier", string.Empty);
        }

        private void ApplyEffectSpecific(Character targetCharacter)
        {
            float multiplier = 1f;
            if (!string.IsNullOrEmpty(scalingStatIdentifier))
            {
                multiplier = 0 + Character.Info.GetSavedStatValue(StatTypes.None, scalingStatIdentifier);
            }

            int totalAmount = (int)(multiplier * amount);
            targetCharacter.GiveMoney(totalAmount);
            GameAnalyticsManager.AddMoneyGainedEvent(totalAmount, GameAnalyticsManager.MoneySource.Ability, CharacterTalent.Prefab.Identifier);
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
