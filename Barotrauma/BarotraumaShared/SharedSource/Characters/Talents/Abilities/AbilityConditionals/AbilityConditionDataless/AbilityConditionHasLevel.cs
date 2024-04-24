#nullable enable

using System;

namespace Barotrauma.Abilities
{
    internal sealed class AbilityConditionHasLevel : AbilityConditionDataless
    {
        private readonly Option<int> matchedLevel;
        private readonly Option<int> minLevel;
        private readonly Option<int> maxLevel;

        public AbilityConditionHasLevel(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            matchedLevel = conditionElement.GetAttributeInt("levelequals", 0) is var match and not 0
                ? Option<int>.Some(match)
                : Option<int>.None();

            minLevel = conditionElement.GetAttributeInt("minlevel", 0) is var min and not 0
                ? Option<int>.Some(min)
                : Option<int>.None();
            
            maxLevel = conditionElement.GetAttributeInt("maxlevel", 0) is var max and not 0
                ? Option<int>.Some(max)
                : Option<int>.None();

            if (matchedLevel.IsNone() && minLevel.IsNone() && maxLevel.IsNone())
            {
                throw new Exception($"{nameof(AbilityConditionHasLevel)} must have either \"levelequals\", \"minlevel\" or \"maxlevel\" attribute.");
            }
        }

        protected override bool MatchesConditionSpecific()
        {
            var currentLevel = character.Info.GetCurrentLevel();
            if (matchedLevel.TryUnwrap(out int match))
            {
                return currentLevel == match;
            }

            if (minLevel.TryUnwrap(out int min))
            {
                return currentLevel >= min;
            }

            if (maxLevel.TryUnwrap(out int max))
            {
                return currentLevel <= max;
            }

            return false;
        }
    }
}