using System;

namespace Barotrauma.Abilities
{
    class AbilityConditionHasPermanentStat : AbilityConditionCharacter
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
                DebugConsole.ThrowError($"No stat identifier defined for {this} in talent {characterTalent.DebugIdentifier}!",
                    contentPackage: conditionElement.ContentPackage);
            }
            string statTypeName = conditionElement.GetAttributeString("stattype", string.Empty);
            statType = string.IsNullOrEmpty(statTypeName) ? StatTypes.None : CharacterAbilityGroup.ParseStatType(statTypeName, characterTalent.DebugIdentifier);
            min = conditionElement.GetAttributeFloat("min", 0f);
            placeholder = conditionElement.GetAttributeEnum("placeholder", PermanentStatPlaceholder.None);
        }

        protected override bool MatchesCharacter(Character character)
        {
            if (character?.Info == null)
            {
                DebugConsole.AddWarning($"Error in {nameof(AbilityConditionHasPermanentStat.MatchesCharacter)}: character {character} has no CharacterInfo. Are you trying to use the condition on a non-player character?\n{Environment.StackTrace.CleanupStackTrace()}");
                return false;
            }
            Identifier identifier = CharacterAbilityGivePermanentStat.HandlePlaceholders(placeholder, statIdentifier);
            return character.Info.GetSavedStatValue(statType, identifier) >= min;
        }
    }
}
