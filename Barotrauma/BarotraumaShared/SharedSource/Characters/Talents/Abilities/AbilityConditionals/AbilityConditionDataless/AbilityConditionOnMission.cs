using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionOnMission : AbilityConditionDataless
    {
        public AbilityConditionOnMission(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement)
        {
        }

        protected override bool MatchesConditionSpecific()
        {
            return Level.Loaded?.Type != LevelData.LevelType.Outpost;
        }
    }
}
