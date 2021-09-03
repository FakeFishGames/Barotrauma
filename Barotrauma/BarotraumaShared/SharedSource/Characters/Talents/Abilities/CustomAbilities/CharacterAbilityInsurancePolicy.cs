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

        private static List<Client> clientsAlreadyUsed = new List<Client>();

        public CharacterAbilityInsurancePolicy(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            moneyPerMission = abilityElement.GetAttributeInt("moneypermission", 0);
        }

        protected override void ApplyEffect()
        {
            if (Character?.Info is CharacterInfo info)
            {

                Character.GiveMoney(moneyPerMission * info.MissionsCompletedSinceDeath);
            }
        }
    }
}
