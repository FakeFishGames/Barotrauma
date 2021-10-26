using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGiveResistance : CharacterAbility
    {
        private readonly string resistanceId;
        private readonly float multiplier;

        public CharacterAbilityGiveResistance(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            resistanceId = abilityElement.GetAttributeString("resistanceid", abilityElement.GetAttributeString("resistance", string.Empty));
            multiplier = abilityElement.GetAttributeFloat("multiplier", 1f); // rename this to resistance for consistency

            if (string.IsNullOrEmpty(resistanceId))
            {
                DebugConsole.ThrowError("Error in CharacterAbilityGiveResistance - resistance identifier not set.");
            }
        }

        public override void InitializeAbility(bool addingFirstTime)
        {
            Character.ChangeAbilityResistance(resistanceId, multiplier);
        }
    }
}
