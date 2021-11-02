using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityModifyResistance : CharacterAbility
    {
        private readonly string resistanceId;
        private readonly float resistance;
        bool lastState;
        public override bool AllowClientSimulation => true;

        // should probably be split to different classes
        public CharacterAbilityModifyResistance(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            resistanceId = abilityElement.GetAttributeString("resistanceid", "");
            resistance = abilityElement.GetAttributeFloat("resistance", 1f);

            if (string.IsNullOrEmpty(resistanceId))
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
