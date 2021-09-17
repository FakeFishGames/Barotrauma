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
        private readonly bool removeAfterRound;
        private readonly bool giveOnAddingFirstTime;

        //private readonly float maximumValue;

        public override bool AppliesEffectOnIntervalUpdate => true;

        public CharacterAbilityGivePermanentStat(CharacterAbilityGroup characterAbilityGroup, XElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            statIdentifier = abilityElement.GetAttributeString("statidentifier", "").ToLowerInvariant();
            statType = CharacterAbilityGroup.ParseStatType(abilityElement.GetAttributeString("stattype", ""), CharacterTalent.DebugIdentifier);
            value = abilityElement.GetAttributeFloat("value", 0f);
            maxValue = abilityElement.GetAttributeFloat("maxvalue", float.MaxValue);
            targetAllies = abilityElement.GetAttributeBool("targetallies", false);
            removeOnDeath = abilityElement.GetAttributeBool("removeondeath", true);
            removeAfterRound = abilityElement.GetAttributeBool("removeafterround", false);
            giveOnAddingFirstTime = abilityElement.GetAttributeBool("giveonaddingfirsttime", characterAbilityGroup.AbilityEffectType == AbilityEffectType.None);
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
                Character.GetFriendlyCrew(Character).ForEach(c => c?.Info.ChangeSavedStatValue(statType, value, statIdentifier, removeOnDeath, removeAfterRound, maxValue));
            }
            else
            {
                Character?.Info.ChangeSavedStatValue(statType, value, statIdentifier, removeOnDeath, removeAfterRound, maxValue);
            }
        }
    }
}
