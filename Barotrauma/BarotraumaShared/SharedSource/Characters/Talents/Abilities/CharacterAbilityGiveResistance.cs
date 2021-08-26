using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGiveResistance : CharacterAbility
    {
        private string resistanceId;
        private float resistance;

        public CharacterAbilityGiveResistance(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            resistanceId = abilityElement.GetAttributeString("resistanceid", "");
            resistance = abilityElement.GetAttributeFloat("resistance", 1f);
        }

        public override void InitializeAbility(bool addingFirstTime)
        {
            Character.ChangeAbilityResistance(resistanceId, resistance);
        }
    }
}
