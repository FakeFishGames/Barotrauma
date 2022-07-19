using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionLevelsBehindHighest : AbilityConditionDataless
    {
        private readonly int levelsBehind;
        public AbilityConditionLevelsBehindHighest(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            levelsBehind = conditionElement.GetAttributeInt("levelsbehind", 0);
        }

        protected override bool MatchesConditionSpecific()
        {
            return Character.GetFriendlyCrew(character).Where(c => c.Info != null && (c.Info.GetCurrentLevel() - character.Info.GetCurrentLevel() >= levelsBehind)).Any();
        }
    }
}
