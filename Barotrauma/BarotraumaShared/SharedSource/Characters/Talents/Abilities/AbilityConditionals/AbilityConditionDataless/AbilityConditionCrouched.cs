using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionCrouched : AbilityConditionDataless
    {

        public AbilityConditionCrouched(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement)
        {
        }

        protected override bool MatchesConditionSpecific()
        {
            return character.AnimController is HumanoidAnimController humanoidAnimController && humanoidAnimController.Crouching;
        }
    }
}
