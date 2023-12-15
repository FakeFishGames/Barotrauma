using System;

namespace Barotrauma.Abilities
{
    public enum PermanentStatPlaceholder
    {
        None,
        LocationName,
        LocationIndex
    }
    
    class CharacterAbilityGivePermanentStat : CharacterAbility
    {
        private readonly Identifier statIdentifier;
        private readonly StatTypes statType;
        private readonly float value;
        private readonly float maxValue;
        private readonly bool targetAllies;
        private readonly bool removeOnDeath;
        private readonly bool giveOnAddingFirstTime;
        private readonly bool setValue;
        private readonly PermanentStatPlaceholder placeholder;

        /// <summary>
        /// If enabled, the effect is applied on the target of the ability (e.g. the character that was killed if the effect type is OnKillCharacter).
        /// Defaults to false (= targets the character who has the talent).
        /// </summary>
        private readonly bool targetAbilityTarget = false;

        public override bool AllowClientSimulation => true;
        public override bool AppliesEffectOnIntervalUpdate => true;

        public CharacterAbilityGivePermanentStat(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            statIdentifier = abilityElement.GetAttributeIdentifier("statidentifier", Identifier.Empty);
            if (statIdentifier.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in talent \"{CharacterTalent.DebugIdentifier}\" - stat identifier not defined.",
                    contentPackage: abilityElement.ContentPackage);
            }
            string statTypeName = abilityElement.GetAttributeString("stattype", string.Empty);
            statType = string.IsNullOrEmpty(statTypeName) ? StatTypes.None : CharacterAbilityGroup.ParseStatType(statTypeName, CharacterTalent.DebugIdentifier);
            value = abilityElement.GetAttributeFloat("value", 0f);
            maxValue = abilityElement.GetAttributeFloat("maxvalue", float.MaxValue);
            targetAllies = abilityElement.GetAttributeBool("targetallies", false);
            removeOnDeath = abilityElement.GetAttributeBool("removeondeath", false);
            giveOnAddingFirstTime = abilityElement.GetAttributeBool("giveonaddingfirsttime", characterAbilityGroup.AbilityEffectType == AbilityEffectType.None);
            setValue = abilityElement.GetAttributeBool("setvalue", false);
            placeholder = abilityElement.GetAttributeEnum("placeholder", PermanentStatPlaceholder.None);
            targetAbilityTarget = abilityElement.GetAttributeBool(nameof(targetAbilityTarget), false);
        }

        public override void InitializeAbility(bool addingFirstTime)
        {
            if (giveOnAddingFirstTime && addingFirstTime)
            {
                ApplyEffectSpecific(abilityObject: null);
            }
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            ApplyEffectSpecific(abilityObject);
        }

        protected override void ApplyEffect()
        {
            ApplyEffectSpecific(abilityObject: null);
        }

        private void ApplyEffectSpecific(AbilityObject abilityObject)
        {
            Identifier identifier = HandlePlaceholders(placeholder, statIdentifier);
            if (targetAllies)
            {
                foreach (Character c in Character.GetFriendlyCrew(Character))
                {
                    c?.Info?.ChangeSavedStatValue(statType, value, identifier, removeOnDeath, maxValue: maxValue, setValue: setValue);
                }
            }
            else
            {
                Character targetCharacter =
                    targetAbilityTarget ? 
                    (abilityObject as IAbilityCharacter)?.Character ?? Character :
                    Character;
                if (targetCharacter == null)
                {
                    DebugConsole.ThrowError($"Error in {nameof(CharacterAbilityGivePermanentStat.ApplyEffectSpecific)}: character was null.\n{Environment.StackTrace.CleanupStackTrace()}");
                    return;
                }
                if (targetCharacter?.Info == null)
                {
                    DebugConsole.AddWarning($"Error in {nameof(CharacterAbilityGivePermanentStat.ApplyEffectSpecific)}: character {targetCharacter} has no CharacterInfo. Are you trying to use the condition on a non-player character?\n{Environment.StackTrace.CleanupStackTrace()}");
                    return;
                }
                targetCharacter.Info.ChangeSavedStatValue(statType, value, identifier, removeOnDeath, maxValue: maxValue, setValue: setValue);
            }
        }

        public static Identifier HandlePlaceholders(PermanentStatPlaceholder placeholder, Identifier original)
        {
            if (GameMain.GameSession?.Campaign?.Map is not { } map) { return original; }

            switch (placeholder)
            {
                case PermanentStatPlaceholder.LocationName when map.CurrentLocation is { } location:
                    return original.Replace("[placeholder]", location.NameIdentifier.Value);
                case PermanentStatPlaceholder.LocationIndex:
                    return original.Replace("[placeholder]", map.CurrentLocationIndex.ToString());
            }

            return original;
        }
    }
}
