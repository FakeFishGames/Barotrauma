using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionGeneHarvester : AbilityConditionData
    {

        public AbilityConditionGeneHarvester(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement) { }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if (abilityObject is AbilityCharacterKill abilityCharacterKill)
            {
                return abilityCharacterKill.Killer.Submarine == null || abilityCharacterKill.Killer.TeamID != abilityCharacterKill.Killer.Submarine.TeamID;
            }
            else
            {
                LogAbilityConditionError(abilityObject, typeof(AbilityCharacterKill));
                return false;
            }
        }
    }
}
