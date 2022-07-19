using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionHasPermanentStat : AbilityConditionDataless
    {
        private readonly Identifier statIdentifier;
        private readonly StatTypes statType;
        private readonly float min;

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
        }

        protected override bool MatchesConditionSpecific()
        {
            return character.Info.GetSavedStatValue(statType, statIdentifier) >= min;
        }
    }
}
