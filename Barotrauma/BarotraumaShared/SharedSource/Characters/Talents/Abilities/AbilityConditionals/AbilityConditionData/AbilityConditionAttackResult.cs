using System;
using Barotrauma.Items.Components;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionAttackResult : AbilityConditionData
    {
        private readonly List<TargetType> targetTypes;
        private readonly Identifier[] afflictions;
        public AbilityConditionAttackResult(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            targetTypes = ParseTargetTypes(conditionElement.GetAttributeStringArray("targettypes", Array.Empty<string>()));
            afflictions = conditionElement.GetAttributeIdentifierArray("afflictions", Array.Empty<Identifier>());
        }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilityAttackResult)?.AttackResult is AttackResult attackResult)
            {
                if (!IsViableTarget(targetTypes, attackResult.HitLimb?.character)) { return false; }

                if (afflictions.Any())
                {
                    if (attackResult.Afflictions == null || !afflictions.Any(a => attackResult.Afflictions.Select(c => c.Identifier).Contains(a))) { return false; }
                }

                return true;
            }
            else
            {
                LogAbilityConditionError(abilityObject, typeof(IAbilityAttackResult));
                return false;
            }
        }
    }
}
