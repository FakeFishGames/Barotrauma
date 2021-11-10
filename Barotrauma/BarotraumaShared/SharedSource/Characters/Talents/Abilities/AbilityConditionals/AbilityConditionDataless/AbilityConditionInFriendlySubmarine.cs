
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionInFriendlySubmarine : AbilityConditionDataless
    {
        public AbilityConditionInFriendlySubmarine(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement) { }

        protected override bool MatchesConditionSpecific()
        {
            return character.Submarine?.TeamID == character.TeamID;
        }
    }
}
