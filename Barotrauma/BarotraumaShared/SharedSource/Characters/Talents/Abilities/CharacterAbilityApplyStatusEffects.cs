using System.Collections.Generic;

namespace Barotrauma.Abilities
{
    class CharacterAbilityApplyStatusEffects : CharacterAbility
    {
        public override bool AppliesEffectOnIntervalUpdate => true;
        public override bool AllowClientSimulation => true;

        protected readonly List<StatusEffect> statusEffects;

        private readonly bool applyToSelf;
        private readonly bool nearbyCharactersAppliesToSelf;
        private readonly bool nearbyCharactersAppliesToAllies;
        private readonly bool nearbyCharactersAppliesToEnemies;
        private readonly bool applyToSelected;

        readonly List<ISerializableEntity> targets = new List<ISerializableEntity>();

        private bool effectBeingApplied;

        /// <summary>
        /// Should the character who has the ability be marked as the "user" of the status effect? 
        /// Means that e.g. enemies will consider damage from the effect to be coming from the character with the ability, and that the character will gain skills if the effect e.g. heals someone.
        /// </summary>

        private readonly bool setUser;

        public CharacterAbilityApplyStatusEffects(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
        {
            statusEffects = CharacterAbilityGroup.ParseStatusEffects(CharacterTalent, abilityElement.GetChildElement("statuseffects"));
            applyToSelf = abilityElement.GetAttributeBool("applytoself", false);
            applyToSelected = abilityElement.GetAttributeBool("applytoselected", false);
            nearbyCharactersAppliesToSelf = abilityElement.GetAttributeBool("nearbycharactersappliestoself", true);
            nearbyCharactersAppliesToAllies = abilityElement.GetAttributeBool("nearbycharactersappliestoallies", true);
            nearbyCharactersAppliesToEnemies = abilityElement.GetAttributeBool("nearbycharactersappliestoenemies", true);
            setUser = abilityElement.GetAttributeBool("setuser", true);
        }

        protected void ApplyEffectSpecific(Character targetCharacter, Limb targetLimb = null)
        {
            //prevent an infinite loop if an effect triggers itself
            //(e.g. a talent that triggers when an affliction is applied, and applies that same affliction)
            if (effectBeingApplied) { return; }

            effectBeingApplied = true;

            try
            {
                foreach (var statusEffect in statusEffects)
                {
                    if (statusEffect.HasTargetType(StatusEffect.TargetType.UseTarget))
                    {
                        // currently used to spawn items on the targeted character
                        if (setUser) { statusEffect.SetUser(targetCharacter); }
                        statusEffect.Apply(ActionType.OnAbility, EffectDeltaTime, targetCharacter, targetCharacter);
                    }
                    else if (statusEffect.HasTargetType(StatusEffect.TargetType.NearbyCharacters))
                    {
                        targets.Clear();
                        statusEffect.AddNearbyTargets(targetCharacter.WorldPosition, targets);
                        if (!nearbyCharactersAppliesToSelf)
                        {
                            targets.RemoveAll(c => c == Character);
                        }
                        if (!nearbyCharactersAppliesToAllies)
                        {
                            targets.RemoveAll(c => c is Character otherCharacter && HumanAIController.IsFriendly(otherCharacter, Character));
                        }
                        if (!nearbyCharactersAppliesToEnemies)
                        {
                            targets.RemoveAll(c => c is Character otherCharacter && !HumanAIController.IsFriendly(otherCharacter, Character));
                        }
                        if (setUser) { statusEffect.SetUser(Character); }
                        statusEffect.Apply(ActionType.OnAbility, EffectDeltaTime, targetCharacter, targets);
                    }
                    else if (statusEffect.HasTargetType(StatusEffect.TargetType.Limb) && targetLimb != null)
                    {
                        if (setUser) { statusEffect.SetUser(Character); }
                        statusEffect.Apply(ActionType.OnAbility, EffectDeltaTime, Character, targetLimb);
                    }
                    else if (statusEffect.HasTargetType(StatusEffect.TargetType.Character))
                    {
                        if (setUser) { statusEffect.SetUser(Character); }
                        statusEffect.Apply(ActionType.OnAbility, EffectDeltaTime, Character, targetCharacter);
                    }
                    else
                    {
                        if (setUser) { statusEffect.SetUser(Character); }
                        statusEffect.Apply(ActionType.OnAbility, EffectDeltaTime, Character, Character);
                    }
                }
            }
            finally
            {
                effectBeingApplied = false;
            }
        }
        protected override void ApplyEffect()
        {
            if (applyToSelected && Character.SelectedCharacter is Character selectedCharacter)
            {
                ApplyEffectSpecific(selectedCharacter);
            }
            else
            {
                ApplyEffectSpecific(Character);
            }
        }

        protected override void ApplyEffect(AbilityObject abilityObject)
        {
            if ((abilityObject as IAbilityCharacter)?.Character is Character targetCharacter && !applyToSelf)
            {
                ApplyEffectSpecific(targetCharacter, targetLimb: (abilityObject as AbilityApplyTreatment)?.TargetLimb);
            }
            else
            {
                ApplyEffect();
            }
        }
    }
}
