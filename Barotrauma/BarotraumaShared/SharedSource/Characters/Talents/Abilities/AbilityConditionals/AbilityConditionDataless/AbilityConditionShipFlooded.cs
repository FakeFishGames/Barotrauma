namespace Barotrauma.Abilities
{
    class AbilityConditionShipFlooded : AbilityConditionDataless
    {
        private readonly float floodPercentage;
        public AbilityConditionShipFlooded(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            floodPercentage = conditionElement.GetAttributeFloat("floodpercentage", 0f);
        }

        protected override bool MatchesConditionSpecific()
        {
            if (!character.IsInFriendlySub) { return false; }
            float waterVolume = 0.0f, totalVolume = 0.0f;
            foreach (Hull hull in Hull.HullList)
            {
                if (hull.Submarine is not { } hullSubmarine) { continue; }
                if (hullSubmarine != character.Submarine || hullSubmarine.TeamID != character.TeamID) { continue; }
                waterVolume += hull.WaterVolume;
                totalVolume += hull.Volume;
            }
            return (waterVolume / totalVolume) > floodPercentage;
        }
    }
}
