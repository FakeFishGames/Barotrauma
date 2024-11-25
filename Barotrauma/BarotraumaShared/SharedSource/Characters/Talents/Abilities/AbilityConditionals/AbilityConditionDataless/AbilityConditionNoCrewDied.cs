namespace Barotrauma.Abilities
{
    class AbilityConditionNoCrewDied : AbilityConditionDataless
    {
        public bool assistantsDontCount;

        public AbilityConditionNoCrewDied(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            assistantsDontCount = conditionElement.GetAttributeBool(nameof(assistantsDontCount), true);
        }

        protected override bool MatchesConditionSpecific()
        {
            if (GameMain.GameSession == null) { return false; }

            foreach (Character deadCharacter in GameMain.GameSession.Casualties)
            {
                if (deadCharacter.TeamID != character.TeamID) { continue; }

                if (assistantsDontCount && deadCharacter.Info?.Job?.Prefab.Identifier == "assistant")
                {
                    continue;
                }
                if (deadCharacter.CauseOfDeath != null && deadCharacter.CauseOfDeath.Type != CauseOfDeathType.Disconnected)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
