using System;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionLocation : AbilityConditionData
    {
        private readonly bool? hasOutpost;
        private readonly Identifier[] locationIdentifiers;

        public AbilityConditionLocation(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            if (conditionElement.Attribute("hasoutpost") != null)
            {
                hasOutpost = conditionElement.GetAttributeBool("hasoutpost", false);
            }
            locationIdentifiers = conditionElement.GetAttributeIdentifierArray("locationtype", Array.Empty<Identifier>());
        }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if (abilityObject is IAbilityLocation abilityLocation)
            {
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
