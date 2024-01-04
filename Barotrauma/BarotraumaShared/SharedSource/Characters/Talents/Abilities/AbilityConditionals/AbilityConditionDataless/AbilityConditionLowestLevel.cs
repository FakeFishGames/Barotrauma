#nullable enable

namespace Barotrauma.Abilities
{
    internal sealed class AbilityConditionLowestLevel : AbilityConditionCharacter
    {
        public AbilityConditionLowestLevel(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement) { }

        protected override bool MatchesCharacter(Character character)
        {
            int ownLevel = character.Info.GetCurrentLevel();
            foreach (Character otherCharacter in GameSession.GetSessionCrewCharacters(CharacterType.Both))
            {
                if (otherCharacter == character) { continue; }
                if (otherCharacter.Info.GetCurrentLevel() < ownLevel) { return false; }
            }
            return true;
        }
    }
}