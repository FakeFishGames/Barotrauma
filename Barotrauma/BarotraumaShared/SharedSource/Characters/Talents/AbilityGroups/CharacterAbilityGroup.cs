using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Barotrauma.Abilities
{
    abstract class CharacterAbilityGroup
    {
        public CharacterTalent CharacterTalent { get; }
        public Character Character { get; }

        // currently only used to turn off simulation if random conditions are in use
        public bool IsActive { get; private set; } = true;

        public readonly AbilityEffectType AbilityEffectType;

        protected int maxTriggerCount { get; }
        protected int timesTriggered = 0;


        // add support for OR conditions? 
        protected readonly List<AbilityCondition> abilityConditions = new List<AbilityCondition>();

        // separate dictionaries for each type of characterability?
        protected readonly List<CharacterAbility> characterAbilities = new List<CharacterAbility>();

        public CharacterAbilityGroup(AbilityEffectType abilityEffectType, CharacterTalent characterTalent, ContentXElement abilityElementGroup)
        {
            AbilityEffectType = abilityEffectType;
            CharacterTalent = characterTalent;
            Character = CharacterTalent.Character;
            maxTriggerCount = abilityElementGroup.GetAttributeInt("maxtriggercount", int.MaxValue);
            foreach (var subElement in abilityElementGroup.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "abilities":
                        LoadAbilities(subElement);
                        break;
                    case "conditions":
                        LoadConditions(subElement);
                        break;
                }
            }
        }

        public void ActivateAbilityGroup(bool addingFirstTime)
        {
            foreach (var characterAbility in characterAbilities)
            {
                characterAbility.InitializeAbility(addingFirstTime);
            }
        }

        public void LoadConditions(ContentXElement conditionElements)
        {
            foreach (ContentXElement conditionElement in conditionElements.Elements())
            {
                AbilityCondition newCondition = ConstructCondition(CharacterTalent, conditionElement);

                if (newCondition == null)
                {
                    DebugConsole.ThrowError($"AbilityCondition was not found in talent {CharacterTalent.DebugIdentifier}!");
                    return;
                }

                if (!newCondition.AllowClientSimulation && GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
                {
                    IsActive = false;
                }

                abilityConditions.Add(newCondition);
            }
        }

        public void AddAbility(CharacterAbility characterAbility)
        {
            if (characterAbility == null)
            {
                DebugConsole.ThrowError($"Trying to add null ability for talent {CharacterTalent.DebugIdentifier}!");
                return;
            }

            characterAbilities.Add(characterAbility);
        }

        // XML
        private AbilityCondition ConstructCondition(CharacterTalent characterTalent, ContentXElement conditionElement, bool errorMessages = true)
        {
            AbilityCondition newCondition = null;

            Type conditionType;
            string type = conditionElement.Name.ToString().ToLowerInvariant();
            try
            {
                conditionType = Type.GetType("Barotrauma.Abilities." + type + "", false, true);
                if (conditionType == null)
                {
                    if (errorMessages) DebugConsole.ThrowError("Could not find the component \"" + type + "\" (" + characterTalent.DebugIdentifier + ")");
                    return null;
                }
            }
            catch (Exception e)
            {
                if (errorMessages) DebugConsole.ThrowError("Could not find the component \"" + type + "\" (" + characterTalent.DebugIdentifier + ")", e);
                return null;
            }

            object[] args = { characterTalent, conditionElement };

            try
            {
                newCondition = (AbilityCondition)Activator.CreateInstance(conditionType, args);
            }
            catch (TargetInvocationException e)
            {
                DebugConsole.ThrowError("Error while creating an instance of an ability condition of the type " + conditionType + ".", e.InnerException);
                return null;
            }

            if (newCondition == null)
            {
                DebugConsole.ThrowError("Error while creating an instance of an ability condition of the type " + conditionType + ", instance was null");
                return null;
            }

            return newCondition;
        }

        private void LoadAbilities(ContentXElement abilityElements)
        {
            foreach (var abilityElementGroup in abilityElements.Elements())
            {
                AddAbility(ConstructAbility(abilityElementGroup, CharacterTalent));
            }
        }

        private CharacterAbility ConstructAbility(ContentXElement abilityElement, CharacterTalent characterTalent)
        {
            CharacterAbility newAbility = CharacterAbility.Load(abilityElement, this);

            if (newAbility == null)
            {
                DebugConsole.ThrowError($"Unable to create an ability for {characterTalent.DebugIdentifier}!");
                return null;
            }

            return newAbility;
        }

        public static List<StatusEffect> ParseStatusEffects(CharacterTalent characterTalent, ContentXElement statusEffectElements)
        {
            if (statusEffectElements == null)
            {
                DebugConsole.ThrowError("StatusEffect list was not found in talent " + characterTalent.DebugIdentifier);
                return null;
            }

            List<StatusEffect> statusEffects = new List<StatusEffect>();

            foreach (var statusEffectElement in statusEffectElements.Elements())
            {
                var statusEffect = StatusEffect.Load(statusEffectElement, characterTalent.DebugIdentifier);
                statusEffects.Add(statusEffect);
            }

            return statusEffects;
        }

        public static StatTypes ParseStatType(string statTypeString, string debugIdentifier)
        {
            if (!Enum.TryParse(statTypeString, true, out StatTypes statType))
            {
                DebugConsole.ThrowError("Invalid stat type type \"" + statTypeString + "\" in CharacterTalent (" + debugIdentifier + ")");
            }
            return statType;
        }

        public static List<Affliction> ParseAfflictions(CharacterTalent characterTalent, ContentXElement afflictionElements)
        {
            if (afflictionElements == null)
            {
                DebugConsole.ThrowError("Affliction list was not found in talent " + characterTalent.DebugIdentifier);
                return null;
            }

            List<Affliction> afflictions = new List<Affliction>();

            // similar logic to affliction creation in statuseffects
            // might be worth unifying

            foreach (var afflictionElement in afflictionElements.Elements())
            {
                Identifier afflictionIdentifier = afflictionElement.GetAttributeIdentifier("identifier", "");
                AfflictionPrefab afflictionPrefab = AfflictionPrefab.List.FirstOrDefault(ap => ap.Identifier == afflictionIdentifier);
                if (afflictionPrefab == null)
                {
                    DebugConsole.ThrowError("Error in CharacterTalent (" + characterTalent.DebugIdentifier + ") - Affliction prefab with the identifier \"" + afflictionIdentifier + "\" not found.");
                    continue;
                }

                Affliction afflictionInstance = afflictionPrefab.Instantiate(afflictionElement.GetAttributeFloat(1.0f, "amount", "strength"));
                afflictionInstance.Probability = afflictionElement.GetAttributeFloat(1.0f, "probability");
                afflictions.Add(afflictionInstance);
            }

            return afflictions;
        }

        public static AbilityFlags ParseFlagType(string flagTypeString, string debugIdentifier)
        {
            AbilityFlags flagType = AbilityFlags.None;
            if (!Enum.TryParse(flagTypeString, true, out flagType))
            {
                DebugConsole.ThrowError("Invalid flag type type \"" + flagTypeString + "\" in CharacterTalent (" + debugIdentifier + ")");
            }
            return flagType;
        }
    }
}
