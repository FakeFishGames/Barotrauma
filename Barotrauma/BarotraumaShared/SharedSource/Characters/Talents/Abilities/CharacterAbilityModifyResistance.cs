using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityModifyResistance : CharacterAbility
    {
        private string resistanceId;
        private float resistance;
        bool lastState;

        // should probably be split to different classes
        public CharacterAbilityModifyResistance(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            resistanceId = abilityElement.GetAttributeString("resistanceid", "");
            resistance = abilityElement.GetAttributeFloat("resistance", 1f);
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
