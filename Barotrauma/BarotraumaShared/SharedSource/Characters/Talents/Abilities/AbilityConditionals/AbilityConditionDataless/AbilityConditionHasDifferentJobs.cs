﻿using System.Collections.Generic;
using System.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionHasDifferentJobs : AbilityConditionDataless
    {
        private readonly int amount;
        public AbilityConditionHasDifferentJobs(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            amount = conditionElement.GetAttributeInt("amount", 0);
        }

        protected override bool MatchesConditionSpecific()
        {
            IEnumerable<Character> crewMembers = Character.GetFriendlyCrew(character);
            int differentCrewAmount = crewMembers.Select(c => c.Info.Job?.Prefab.Identifier).Distinct().Count();
            return differentCrewAmount >= amount;
        }
    }
}
