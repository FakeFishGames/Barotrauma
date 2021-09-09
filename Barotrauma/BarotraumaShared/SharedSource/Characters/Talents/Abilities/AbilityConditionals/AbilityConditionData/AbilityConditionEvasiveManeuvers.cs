using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionEvasiveManeuvers : AbilityConditionData
    {
        public AbilityConditionEvasiveManeuvers(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement) { }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilitySubmarine)?.Submarine is Submarine submarine && (abilityObject as IAbilityCharacter)?.Character is Character attackingCharacter)
            {
                return submarine.TeamID == character.TeamID && character.Submarine == submarine && attackingCharacter.TeamID != character.TeamID;
            }
            else
            {
                LogAbilityConditionError(abilityObject, typeof(IAbilitySubmarine));
                return false;
            }
        }
    }
}
