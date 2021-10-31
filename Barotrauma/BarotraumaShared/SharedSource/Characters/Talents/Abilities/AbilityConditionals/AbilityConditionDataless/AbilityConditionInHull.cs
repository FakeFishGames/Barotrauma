
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionInHull : AbilityConditionDataless
    {
        public AbilityConditionInHull(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement) { }

        protected override bool MatchesConditionSpecific()
        {
            return character.CurrentHull != null;
        }
    }
}
