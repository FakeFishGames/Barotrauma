using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityBountyHunter : CharacterAbility
    {
        private float vitalityPercentage;

        public CharacterAbilityBountyHunter(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            vitalityPercentage = abilityElement.GetAttributeFloat("vitalitypercentage", 0f);
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilityCharacter)?.Character is Character character)
            {
                int totalAmount = (int)(vitalityPercentage * character.MaxVitality);
                Character.GiveMoney(totalAmount);
                GameAnalyticsManager.AddMoneyGainedEvent(totalAmount, GameAnalyticsManager.MoneySource.Ability, CharacterTalent.Prefab.Identifier);
            }
        }
    }
}
