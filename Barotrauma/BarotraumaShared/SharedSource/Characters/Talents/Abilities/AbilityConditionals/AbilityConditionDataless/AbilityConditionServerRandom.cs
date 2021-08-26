using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionServerRandom : AbilityConditionDataless
    {
        private float randomChance = 0f;
        public override bool AllowClientSimulation => false;

        public AbilityConditionServerRandom(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement)
        {
            randomChance = conditionElement.GetAttributeFloat("randomchance", 1f);
        }

        protected override bool MatchesConditionSpecific()
        {
            return randomChance >= Rand.Range(0f, 1f, Rand.RandSync.Unsynced);
        }
    }
}
