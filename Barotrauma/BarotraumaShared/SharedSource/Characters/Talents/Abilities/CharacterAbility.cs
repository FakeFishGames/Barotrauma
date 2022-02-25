using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Barotrauma.Abilities
{
    abstract class CharacterAbility
    {
        public CharacterAbilityGroup CharacterAbilityGroup { get; }
        public CharacterTalent CharacterTalent { get; }
        public Character Character { get; }

        public bool RequiresAlive { get; }

        public virtual bool AllowClientSimulation => false;
        public virtual bool AppliesEffectOnIntervalUpdate => false;

        private const float DefaultEffectTime = 1.0f;

        // currently resets if the character dies. would need to be stored in a dictionary of sorts to maintain through death


        /// <summary>
        /// Used primarily for StatusEffects. Default to constant outside interval abilities.
        /// </summary>
        protected float EffectDeltaTime => CharacterAbilityGroup is CharacterAbilityGroupInterval abilityGroupInterval ? abilityGroupInterval.TimeSinceLastUpdate : DefaultEffectTime;

        public CharacterAbility(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement)
        {
            CharacterAbilityGroup = characterAbilityGroup;
            CharacterTalent = characterAbilityGroup.CharacterTalent;
            Character = CharacterTalent.Character;
            RequiresAlive = abilityElement.GetAttributeBool("requiresalive", true);
        }

        public bool IsViable()
        {
            if (!AllowClientSimulation && GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return false; }
            if (RequiresAlive && Character.IsDead) { return false; }
            return true;
        }

        public virtual void InitializeAbility(bool addingFirstTime) { }

        public virtual void UpdateCharacterAbility(bool conditionsMatched, float timeSinceLastUpdate) 
        {
            // may need a separate Update for changing state on non-interval-based abilities
            if (AppliesEffectOnIntervalUpdate)
            {
                if (conditionsMatched)
                {
                    ApplyEffect();
                }
            }
            else
            {
                VerifyState(conditionsMatched, timeSinceLastUpdate);
            }
        }

        protected virtual void VerifyState(bool conditionsMatched, float timeSinceLastUpdate)
        {
            DebugConsole.ThrowError($"Error in talent {CharacterTalent.DebugIdentifier}: Ability {this} does not have an implementation for VerifyState! This ability does not work in interval ability groups.");
        }

        public void ApplyAbilityEffect(AbilityObject abilityObject)
        {
            if (abilityObject is null)
            {
                ApplyEffect();
            } 
            else
            {
                ApplyEffect(abilityObject);
            }
        }

        protected virtual void ApplyEffect()
        {
            DebugConsole.AddWarning($"Ability {this} used improperly! This ability does not have a definition for ApplyEffect in talent {CharacterTalent.DebugIdentifier}");
        }

        protected virtual void ApplyEffect(AbilityObject abilityObject)
        {
            DebugConsole.AddWarning($"Ability {this} used improperly! This ability does not take a parameter for ApplyEffect in talent {CharacterTalent.DebugIdentifier}");
        }

        protected void LogAbilityObjectMismatch()
        {
            DebugConsole.ThrowError($"Incompatible ability! Ability {this} is incompatitible with this type of ability effect type in talent {CharacterTalent.DebugIdentifier}");
        }

        // XML
        public static CharacterAbility Load(ContentXElement abilityElement, CharacterAbilityGroup characterAbilityGroup, bool errorMessages = true)
        {
            Type abilityType;
            string type = abilityElement.Name.ToString().ToLowerInvariant();
            try
            {
                abilityType = Type.GetType("Barotrauma.Abilities." + type + "", false, true);
                if (abilityType == null)
                {
                    if (errorMessages) DebugConsole.ThrowError("Could not find the CharacterAbility \"" + type + "\" (" + characterAbilityGroup.CharacterTalent.DebugIdentifier + ")");
                    return null;
                }
            }
            catch (Exception e)
            {
                if (errorMessages) DebugConsole.ThrowError("Could not find the CharacterAbility \"" + type + "\" (" + characterAbilityGroup.CharacterTalent.DebugIdentifier + ")", e);
                return null;
            }

            object[] args = { characterAbilityGroup, abilityElement };
            CharacterAbility characterAbility;

            try
            {
                characterAbility = (CharacterAbility)Activator.CreateInstance(abilityType, args);
            }
            catch (TargetInvocationException e)
            {
                DebugConsole.ThrowError("Error while creating an instance of a CharacterAbility of the type " + abilityType + ".", e.InnerException);
                return null;
            }

            return characterAbility;
        }
    }
}
