using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionLocation : AbilityConditionData
    {
        private readonly bool? hasOutpost;
        private readonly Identifier[] locationIdentifiers;
        private readonly bool isPositiveReputation;

        public AbilityConditionLocation(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            if (conditionElement.GetAttribute("hasoutpost") != null)
            {
                hasOutpost = conditionElement.GetAttributeBool("hasoutpost", false);
            }
            locationIdentifiers = conditionElement.GetAttributeIdentifierArray("locationtype", Array.Empty<Identifier>());

            isPositiveReputation = conditionElement.GetAttributeBool("ispositivereputation", false);
        }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if (abilityObject is IAbilityLocation abilityLocation)
            {
                if (isPositiveReputation)
                {
                    if (abilityLocation.Location?.Reputation is not { } reputation) { return false; }
                    if (reputation.Value <= 0) { return false; }
                }

                if (locationIdentifiers.Any())
                {
                    if (!locationIdentifiers.Contains(abilityLocation.Location.Type.Identifier)) { return false; }
                }
                if (hasOutpost.HasValue)
                {
                    if (hasOutpost.Value != abilityLocation.Location.HasOutpost()) { return false; }
                }
                return true;
            }
            else
            {
                LogAbilityConditionError(abilityObject, typeof(IAbilityItemPrefab));
                return false;
            }
        }
    }
}
