using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityInsurancePolicy : CharacterAbility
    {
        public override bool AppliesEffectOnIntervalUpdate => true;

        private readonly int moneyPerMission;

        public CharacterAbilityInsurancePolicy(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            moneyPerMission = abilityElement.GetAttributeInt("moneypermission", 0);
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if (Character?.Info is CharacterInfo info)
            {
                int totalAmount = moneyPerMission * info.MissionsCompletedSinceDeath;
                Character.GiveMoney(totalAmount);
                GameAnalyticsManager.AddMoneyGainedEvent(totalAmount, GameAnalyticsManager.MoneySource.Ability, CharacterTalent.Prefab.Identifier);
            }
        }
    }
}
