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

            foreach (Character character in GameMain.GameSession.Casualties)
            {
                if (assistantsDontCount && character.Info?.Job?.Prefab.Identifier == "assistant")
                {
                    continue;
                }
                if (character.CauseOfDeath != null && character.CauseOfDeath.Type != CauseOfDeathType.Disconnected)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
