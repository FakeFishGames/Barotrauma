namespace Barotrauma.Abilities
{
    class AbilityConditionHasAffliction : AbilityConditionDataless
    {
        private readonly Identifier afflictionIdentifier;
        private readonly float minimumPercentage;

        public AbilityConditionHasAffliction(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            afflictionIdentifier = conditionElement.GetAttributeIdentifier("afflictionidentifier", Identifier.Empty);
            minimumPercentage = conditionElement.GetAttributeFloat("minimumpercentage", 0f);
        }

        protected override bool MatchesConditionSpecific()
        {
            if (!afflictionIdentifier.IsEmpty)
            {
                var affliction = character.CharacterHealth.GetAffliction(afflictionIdentifier);
                if (affliction == null) { return false; }
                return affliction.Strength >= affliction.Prefab.ActivationThreshold && minimumPercentage <= affliction.Strength / affliction.Prefab.MaxStrength;
            }
            return false;
        }
    }
}
