using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionCharacter : AbilityConditionData
    {
        private readonly List<TargetType> targetTypes;

        private List<PropertyConditional> conditionals = new List<PropertyConditional>();

        public AbilityConditionCharacter(CharacterTalent characterTalent, XElement conditionElement) : base(characterTalent, conditionElement)
        {
            targetTypes = ParseTargetTypes(conditionElement.GetAttributeStringArray("targettypes", new string[0], convertToLowerInvariant: true));

            foreach (XElement subElement in conditionElement.Elements())
            {
                if (subElement.Name.ToString().Equals("conditional", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (XAttribute attribute in subElement.Attributes())
                    {
                        if (PropertyConditional.IsValid(attribute))
                        {
                            conditionals.Add(new PropertyConditional(attribute));
                        }
                    }
                    break;
                }
            }
        }

        protected override bool MatchesConditionSpecific(AbilityObject abilityObject)
        {
            if (abilityObject is IAbilityCharacter abilityCharacter)
            {
                if (!(abilityCharacter.Character is Character character)) { return false; }
                if (!IsViableTarget(targetTypes, character)) { return false; }
                foreach (var conditional in conditionals)
                {
                    if (!conditional.Matches(character)) { return false; }
                }
                return true;
            }
            else
            {
                LogAbilityConditionError(abilityObject, typeof(IAbilityCharacter));
                return false;
            }
        }
    }
}
