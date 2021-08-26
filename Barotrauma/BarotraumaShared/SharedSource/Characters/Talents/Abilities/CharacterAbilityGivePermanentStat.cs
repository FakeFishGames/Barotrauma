using Barotrauma.Extensions;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGivePermanentStat : CharacterAbility
    {
        private readonly string statIdentifier;
        private readonly StatTypes statType;
        private readonly float value;
        private readonly bool targetAllies;
        private readonly bool removeOnDeath;
        //private readonly float maximumValue;

        public override bool AppliesEffectOnIntervalUpdate => true;

        public CharacterAbilityGivePermanentStat(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            statIdentifier = abilityElement.GetAttributeString("statidentifier", "").ToLowerInvariant();
            statType = CharacterAbilityGroup.ParseStatType(abilityElement.GetAttributeString("stattype", ""), CharacterTalent.DebugIdentifier);
            value = abilityElement.GetAttributeFloat("value", 0f);
            targetAllies = abilityElement.GetAttributeBool("targetallies", false);
            removeOnDeath = abilityElement.GetAttributeBool("removeondeath", true);
            //maximumValue = abilityElement.GetAttributeFloat("maximumvalue", float.MaxValue);
        }

        protected override void ApplyEffect(object abilityData)
        {
            ApplyEffectSpecific();
        }

        protected override void ApplyEffect()
        {
            ApplyEffectSpecific();
        }

        private void ApplyEffectSpecific()
        {
            if (targetAllies)
            {
                Character.GetFriendlyCrew(Character).ForEach(c => c?.Info.ChangeSavedStatValue(statType, value, statIdentifier, removeOnDeath));
            }
            else
            {
                Character?.Info.ChangeSavedStatValue(statType, value, statIdentifier, removeOnDeath);
            }
        }
    }
}
