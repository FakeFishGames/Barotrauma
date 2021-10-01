using Barotrauma.Extensions;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    class CharacterAbilityGivePermanentStat : CharacterAbility
    {
        private readonly string statIdentifier;
        private readonly StatTypes statType;
        private readonly float value;
        private readonly float maxValue;
        private readonly bool targetAllies;
        private readonly bool removeOnDeath;
        private readonly bool giveOnAddingFirstTime;
        private readonly bool setValue;

        //private readonly float maximumValue;

        public override bool AppliesEffectOnIntervalUpdate => true;

        public CharacterAbilityGivePermanentStat(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            statIdentifier = abilityElement.GetAttributeString("statidentifier", "").ToLowerInvariant();
            string statTypeName = abilityElement.GetAttributeString("stattype", string.Empty);
            statType = string.IsNullOrEmpty(statTypeName) ? StatTypes.None : CharacterAbilityGroup.ParseStatType(statTypeName, CharacterTalent.DebugIdentifier);
            value = abilityElement.GetAttributeFloat("value", 0f);
            maxValue = abilityElement.GetAttributeFloat("maxvalue", float.MaxValue);
            targetAllies = abilityElement.GetAttributeBool("targetallies", false);
            removeOnDeath = abilityElement.GetAttributeBool("removeondeath", true);
            giveOnAddingFirstTime = abilityElement.GetAttributeBool("giveonaddingfirsttime", characterAbilityGroup.AbilityEffectType == AbilityEffectType.None);
            setValue = abilityElement.GetAttributeBool("setvalue", false);
        }

        public override void InitializeAbility(bool addingFirstTime)
        {
            if (giveOnAddingFirstTime && addingFirstTime)
            {
                ApplyEffectSpecific();
            }
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
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
                Character.GetFriendlyCrew(Character).ForEach(c => c?.Info.ChangeSavedStatValue(statType, value, statIdentifier, removeOnDeath, maxValue: maxValue, setValue: setValue));
            }
            else
            {
                Character?.Info.ChangeSavedStatValue(statType, value, statIdentifier, removeOnDeath, maxValue: maxValue, setValue: setValue);
            }
        }
    }
}
