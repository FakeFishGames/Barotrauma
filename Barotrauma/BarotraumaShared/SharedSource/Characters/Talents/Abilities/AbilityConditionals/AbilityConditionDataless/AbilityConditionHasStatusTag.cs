using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class AbilityConditionHasStatusTag : AbilityConditionDataless
    {
        private readonly string tag;


        public AbilityConditionHasStatusTag(CharacterTalent characterTalent, ContentXElement conditionElement) : base(characterTalent, conditionElement)
        {
            tag = conditionElement.GetAttributeString("tag", "");
            if (string.IsNullOrEmpty(tag))
            {
                DebugConsole.AddWarning($"Error in talent \"{characterTalent.Prefab.OriginalName}\" - tag not defined in AbilityConditionHasStatusTag.");
            }
        }

        protected override bool MatchesConditionSpecific()
        {
            if (!string.IsNullOrEmpty(tag))
            {
                return 
                    StatusEffect.DurationList.Any(d => d.Targets.Contains(character) && d.Parent.HasTag(tag)) || 
                    DelayedEffect.DelayList.Any(d => d.Targets.Contains(character) && d.Parent.HasTag(tag));
            }
            return false;
        }
    }
}
