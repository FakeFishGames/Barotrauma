using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionHasStatusTag : AbilityConditionDataless
    {
        private readonly Identifier tag;


        public AbilityConditionHasStatusTag(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            tag = conditionElement.GetAttributeIdentifier("tag", Identifier.Empty);
            if (tag.IsEmpty)
            {
                DebugConsole.AddWarning($"Error in talent \"{characterTalent.Prefab.OriginalName}\" - tag not defined in AbilityConditionHasStatusTag.",
                    characterTalent.Prefab.ContentPackage);
            }
        }

        protected override bool MatchesConditionSpecific()
        {
            if (!tag.IsEmpty)
            {
                return 
                    StatusEffect.DurationList.Any(d => d.Targets.Contains(character) && d.Parent.HasTag(tag)) || 
                    DelayedEffect.DelayList.Any(d => d.Targets.Contains(character) && d.Parent.HasTag(tag));
            }
            return false;
        }
    }
}
