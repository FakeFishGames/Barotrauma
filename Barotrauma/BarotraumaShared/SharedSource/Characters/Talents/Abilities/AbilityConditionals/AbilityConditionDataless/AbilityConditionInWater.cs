
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionInWater : AbilityConditionDataless
    {
        public AbilityConditionInWater(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement) { }

        protected override bool MatchesConditionSpecific()
        {
            return character.InWater;
        }
    }
}
