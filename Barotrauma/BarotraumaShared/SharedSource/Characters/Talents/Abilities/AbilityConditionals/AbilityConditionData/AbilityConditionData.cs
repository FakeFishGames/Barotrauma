using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    abstract class AbilityConditionData : AbilityCondition
    {
        /// <summary>
        /// Some conditions rely on specific ability data that is integrally connected to the AbilityEffectType.
        /// This is done in order to avoid having to create duplicate ability behavior, such as if an ability needs to trigger
        /// a common ability effect but in specific circumstances. These conditions could also be partially replaced by
        /// more explicit AbilityEffectType enums, but this would introduce bloat and overhead to integral game logic
        /// when instead said logic can be made to only run when required using these conditions.
        /// 
        /// These conditions will return an error if used outside their limited intended use.
        /// </summary>
        public AbilityConditionData(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement) { }

        protected void LogAbilityConditionError<T>(T abilityData, Type expectedData)
        {
            DebugConsole.ThrowError($"Used data-reliant ability condition when data is incompatible! Expected {expectedData}, but received {abilityData}");
        }

        protected abstract bool MatchesConditionSpecific(object abilityData);
        public override bool MatchesCondition()
        {
            DebugConsole.ThrowError("Used data-reliant ability condition in a state-based ability! This is not allowed.");
            return false;
        }
        public override bool MatchesCondition(object abilityData)
        {
            if (abilityData is null) { return invert; }
            return invert ? !MatchesConditionSpecific(abilityData) : MatchesConditionSpecific(abilityData);
        }
    }
}
