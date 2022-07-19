using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGiveResistance : CharacterAbility
    {
        private readonly Identifier resistanceId;
        private readonly float multiplier;

        public CharacterAbilityGiveResistance(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            resistanceId = abilityElement.GetAttributeIdentifier("resistanceid", abilityElement.GetAttributeIdentifier("resistance", Identifier.Empty));
            multiplier = abilityElement.GetAttributeFloat("multiplier", 1f); // rename this to resistance for consistency

            if (resistanceId.IsEmpty)
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
