using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityModifyStatToFlooding : CharacterAbility
    {
        private readonly StatTypes statType;
        private readonly float maxValue;
        private float lastValue = 0f;
        public override bool AllowClientSimulation => true;

        public CharacterAbilityModifyStatToFlooding(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            statType = CharacterAbilityGroup.ParseStatType(abilityElement.GetAttributeString("stattype", ""), CharacterTalent.DebugIdentifier);
            maxValue = abilityElement.GetAttributeFloat("maxvalue", 0f);
        }

        protected override void VerifyState(bool conditionsMatched, float timeSinceLastUpdate)
        {
            Character.ChangeStat(statType, -lastValue);

            if (conditionsMatched && Character.IsInFriendlySub)
            {
                float currentFloodPercentage = Character.Submarine.GetHulls(false).Average(h => h.WaterPercentage);
                lastValue = currentFloodPercentage / 100f * maxValue;
                Character.ChangeStat(statType, lastValue);
            }
            else
            {
                lastValue = 0f;
            }
        }
    }
}
