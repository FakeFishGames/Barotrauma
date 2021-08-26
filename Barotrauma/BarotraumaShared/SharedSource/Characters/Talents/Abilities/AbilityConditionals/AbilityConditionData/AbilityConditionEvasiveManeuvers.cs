using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionEvasiveManeuvers : AbilityConditionData
    {
        public AbilityConditionEvasiveManeuvers(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement) { }

        protected override bool MatchesConditionSpecific(object abilityData)
        {
            if (abilityData is Submarine submarine)
            {
                return submarine.TeamID == character.TeamID && character.Submarine == submarine;
            }
            else
            {
                LogAbilityConditionError(abilityData, typeof(Submarine));
                return false;
            }
        }
    }
}
