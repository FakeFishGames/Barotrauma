#nullable enable

using System;

namespace Barotrauma.Abilities
{
    internal sealed class AbilityConditionHasLevel : AbilityConditionDataless
    {
        private readonly Option<int> matchedLevel;
        private readonly Option<int> minLevel;

        public AbilityConditionHasLevel(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            matchedLevel = conditionElement.GetAttributeInt("levelequals", 0) is var match and not 0
                ? Option<int>.Some(match)
                : Option<int>.None();

            minLevel = conditionElement.GetAttributeInt("minlevel", 0) is var min and not 0
                ? Option<int>.Some(min)
                : Option<int>.None();

            if (matchedLevel.IsNone() && minLevel.IsNone())
            {
                throw new Exception($"{nameof(AbilityConditionHasLevel)} must have either \"levelequals\" or \"minlevel\" attribute.");
            }
        }

        protected override bool MatchesConditionSpecific()
        {
            if (matchedLevel.TryUnwrap(out int match))
            {
                return character.Info.GetCurrentLevel() == match;
            }

            if (minLevel.TryUnwrap(out int min))
            {
                return character.Info.GetCurrentLevel() >= min;
            }

            return false;
        }
    }
}