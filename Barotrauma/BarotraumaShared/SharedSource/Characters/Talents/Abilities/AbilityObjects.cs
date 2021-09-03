using System.Collections.Generic;

namespace Barotrauma.Abilities
{

    class AbilityValue : IAbilityValue
    {
        public AbilityValue(float value)
        {
            Value = value;
        }
        public float Value { get; set; }
    }

    class AbilityValueItem : IAbilityValue, IAbilityItemPrefab
    {
        public AbilityValueItem(float value, ItemPrefab itemPrefab)
        {
            Value = value;
            ItemPrefab = itemPrefab;
        }
        public float Value { get; set; }
        public ItemPrefab ItemPrefab { get; set; }
    }

    class AbilityValueString : IAbilityValue, IAbilityString
    {
        public AbilityValueString(float value, string abilityString)
        {
            Value = value;
            String = abilityString;
        }
        public float Value { get; set; }
        public string String { get; set; }
    }

    class AbilityStringCharacter : IAbilityCharacter, IAbilityString
    {
        public AbilityStringCharacter(string abilityString, Character character)
        {
            String = abilityString;
            Character = character;
        }
        public Character Character { get; set; }
        public string String { get; set; }
    }

    class AbilityValueAffliction : IAbilityValue, IAbilityAffliction
    {
        public AbilityValueAffliction(float value, Affliction affliction)
        {
            Value = value;
            Affliction = affliction;
        }
        public float Value { get; set; }
        public Affliction Affliction { get; set; }
    }

    class AbilityValueMission : IAbilityValue, IAbilityMission
    {
        public AbilityValueMission(float value, Mission mission)
        {
            Value = value;
            Mission = mission;
        }
        public float Value { get; set; }
        public Mission Mission { get; set; }
    }

    class AbilityAttackData : IAbilityCharacter
    {
        public float DamageMultiplier { get; set; } = 1f;
        public float AddedPenetration { get; set; } = 0f;
        public List<Affliction> Afflictions { get; set; }
        public Attack SourceAttack { get; }
        public Character Character { get; set; }
        public Character Attacker { get; set; }

        public AbilityAttackData(Attack sourceAttack, Character character)
        {
            SourceAttack = sourceAttack;
            Character = character;
        }
    }
}
