using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionHasPermanentStat : AbilityConditionDataless
    {
        private readonly StatTypes statType;
        private readonly float min;

        public AbilityConditionHasPermanentStat(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement)
        {
            statType = CharacterAbilityGroup.ParseStatType(conditionElement.GetAttributeString("stattype", ""), characterTalent.DebugIdentifier);
            min = conditionElement.GetAttributeFloat("min", 0f);
        }

        protected override bool MatchesConditionSpecific()
        {
            // should consider decoupling this from stat values entirely
            return character.Info.GetSavedStatValue(statType) >= min;
        }
    }
}
