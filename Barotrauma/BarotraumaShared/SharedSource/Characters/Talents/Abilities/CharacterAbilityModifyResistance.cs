using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityModifyResistance : CharacterAbility
    {
        private readonly Identifier resistanceId;
        private readonly float resistance;
        bool lastState;
        public override bool AllowClientSimulation => true;

        // should probably be split to different classes
        public CharacterAbilityModifyResistance(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            resistanceId = abilityElement.GetAttributeIdentifier("resistanceid", "");
            resistance = abilityElement.GetAttributeFloat("resistance", 1f);

            if (resistanceId.IsEmpty)
            {
                DebugConsole.ThrowError("Error in CharacterAbilityModifyResistance - resistance identifier not set.");
            }
        }

        public override void UpdateCharacterAbility(bool conditionsMatched, float timeSinceLastUpdate)
        {
            if (conditionsMatched != lastState)
            {
                Character.ChangeAbilityResistance(resistanceId, conditionsMatched ? resistance : 1 / resistance);
                lastState = conditionsMatched;
            }
        }
    }
}
