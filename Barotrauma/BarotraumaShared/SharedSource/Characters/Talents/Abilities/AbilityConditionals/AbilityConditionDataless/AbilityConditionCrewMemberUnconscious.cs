#nullable enable

namespace Barotrauma.Abilities
{
    internal sealed class AbilityConditionCrewMemberUnconscious : AbilityConditionDataless
    {
        public AbilityConditionCrewMemberUnconscious(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement) { }

        protected override bool MatchesConditionSpecific()
        {
            foreach (Character c in Character.GetFriendlyCrew(character))
            {
                if (!c.IsDead && c.IsUnconscious)
                {
                    return true;
                }
            }

            return false;
        }
    }
}