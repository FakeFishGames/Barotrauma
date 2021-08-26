using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityPsychoClown : CharacterAbility
    {
        private StatTypes statType;
        private float value;
        private string afflictionIdentifier;
        private float lastValue = 0f;

        public CharacterAbilityPsychoClown(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            statType = CharacterAbilityGroup.ParseStatType(abilityElement.GetAttributeString("stattype", ""), CharacterTalent.DebugIdentifier);
            value = abilityElement.GetAttributeFloat("value", 0f);
            afflictionIdentifier = abilityElement.GetAttributeString("afflictionidentifier", "");
        }

        protected override void VerifyState(bool conditionsMatched, float timeSinceLastUpdate)
        {
            // managing state this way seems liable to cause bugs, maybe instead create abstraction to reset these values more safely
            // talents cannot be removed while in active play because of the lack of this, for example
            Character.ChangeStat(statType, -lastValue);

            if (conditionsMatched)
            {
                var affliction = Character.CharacterHealth.GetAffliction(afflictionIdentifier);

                float afflictionStrength = 0f;
                if (affliction != null)
                {
                    afflictionStrength = affliction.Strength / affliction.Prefab.MaxStrength;
                }

                lastValue = afflictionStrength * value;
                Character.ChangeStat(statType, lastValue);
            }
            else
            {
                lastValue = 0f;
            }
        }
    }
}
