using Barotrauma.Extensions;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGiveTalentPoints : CharacterAbility
    {
        private readonly int amount;

        public CharacterAbilityGiveTalentPoints(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            amount = abilityElement.GetAttributeInt("amount", 0);
        }

        public override void InitializeAbility(bool addingFirstTime)
        {
            if (addingFirstTime && Character.Info != null)
            {
                Character.Info.AdditionalTalentPoints += amount;
            }
        }
    }
}
