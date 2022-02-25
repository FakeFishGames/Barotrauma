using System;
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
        public AbilityConditionData(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement) { }

        protected void LogAbilityConditionError(AbilityObject abilityObject, Type expectedData)
        {
            DebugConsole.ThrowError($"Used data-reliant ability condition when data is incompatible! Expected {expectedData}, but received {abilityObject} in talent {characterTalent.DebugIdentifier}");
        }

        protected abstract bool MatchesConditionSpecific(AbilityObject abilityObject);
        public override bool MatchesCondition()
        {
            DebugConsole.ThrowError($"Used data-reliant ability condition in a state-based ability in talent {characterTalent.DebugIdentifier}! This is not allowed.");
            return false;
        }
        public override bool MatchesCondition(AbilityObject abilityObject)
        {
            if (abilityObject is null) { return invert; }
            return invert ? !MatchesConditionSpecific(abilityObject) : MatchesConditionSpecific(abilityObject);
        }
    }
}
