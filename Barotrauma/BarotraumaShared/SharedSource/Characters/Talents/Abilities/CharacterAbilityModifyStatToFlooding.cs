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
                float waterVolume = 0.0f, totalVolume = 0.0f;
                foreach (Hull hull in Hull.HullList)
                {
                    if (hull.Submarine != Character.Submarine) { continue; }
                    waterVolume += hull.WaterVolume;
                    totalVolume += hull.Volume;
                }
                lastValue = (totalVolume == 0.0f ? 1.0f : waterVolume / totalVolume) * maxValue;
                Character.ChangeStat(statType, lastValue);
            }
            else
            {
                lastValue = 0f;
            }
        }
    }
}
