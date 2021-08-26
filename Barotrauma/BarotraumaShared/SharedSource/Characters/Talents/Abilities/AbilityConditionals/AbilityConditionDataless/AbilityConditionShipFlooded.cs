using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionShipFlooded : AbilityConditionDataless
    {
        private readonly float floodPercentage;
        public AbilityConditionShipFlooded(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement)
        {
            floodPercentage = conditionElement.GetAttributeFloat("floodpercentage", 0f);
        }

        protected override bool MatchesConditionSpecific()
        {
            if (character.Submarine == null || character.Submarine.TeamID != character.TeamID) { return false; }
            float currentFloodPercentage = character.Submarine.GetHulls(false).Average(h => h.WaterPercentage);
            return currentFloodPercentage / 100 > floodPercentage;
        }
    }
}
