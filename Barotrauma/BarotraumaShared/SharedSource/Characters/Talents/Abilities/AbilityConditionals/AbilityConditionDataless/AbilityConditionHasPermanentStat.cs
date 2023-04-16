namespace Barotrauma.Abilities
{
    class AbilityConditionHasPermanentStat : AbilityConditionDataless
    {
        private readonly Identifier statIdentifier;
        private readonly StatTypes statType;
        private readonly float min;
        private readonly PermanentStatPlaceholder placeholder;

        public AbilityConditionHasPermanentStat(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            statIdentifier = conditionElement.GetAttributeIdentifier("statidentifier", Identifier.Empty);
            if (statIdentifier.IsEmpty)
            {
                DebugConsole.ThrowError($"No stat identifier defined for {this} in talent {characterTalent.DebugIdentifier}!");
            }
            string statTypeName = conditionElement.GetAttributeString("stattype", string.Empty);
            statType = string.IsNullOrEmpty(statTypeName) ? StatTypes.None : CharacterAbilityGroup.ParseStatType(statTypeName, characterTalent.DebugIdentifier);
            min = conditionElement.GetAttributeFloat("min", 0f);
            placeholder = conditionElement.GetAttributeEnum("placeholder", PermanentStatPlaceholder.None);
        }

        protected override bool MatchesConditionSpecific()
        {
            Identifier identifier = CharacterAbilityGivePermanentStat.HandlePlaceholders(placeholder, statIdentifier);
            return character.Info.GetSavedStatValue(statType, identifier) >= min;
        }
    }
}
