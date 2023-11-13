#nullable enable

namespace Barotrauma.Abilities
{
    internal sealed class AbilityConditionLowestLevel : AbilityConditionDataless
    {
        public AbilityConditionLowestLevel(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement) { }

        protected override bool MatchesConditionSpecific()
        {
            int ownLevel = character.Info.GetCurrentLevel();

            foreach (Character crew in GameSession.GetSessionCrewCharacters(CharacterType.Both))
            {
                if (crew == character) { continue; }

                if (crew.Info.GetCurrentLevel() < ownLevel) { return false; }
            }

            return true;
        }
    }
}