using System.Collections.Generic;

namespace Barotrauma.Abilities
{
    class AbilityObject
    {
        // kept as blank for now, as we are using a composition and only using this object to enforce parameter types
    }

    class AbilityCharacter : AbilityObject, IAbilityCharacter
    {
        public AbilityCharacter(Character character)
        {
            Character = character;
        }
        public Character Character { get; set; }
    }

    class AbilityItem : AbilityObject, IAbilityItem
    {
        public AbilityItem(Item item)
        {
            Item = item;
        }
        public Item Item { get; set; }
    }

    class AbilityValue : AbilityObject, IAbilityValue
    {
        public AbilityValue(float value)
        {
            Value = value;
        }
        public float Value { get; set; }
    }

    class AbilityAffliction : AbilityObject, IAbilityAffliction
    {
        public AbilityAffliction(Affliction affliction)
        {
            Affliction = affliction;
        }
        public Affliction Affliction { get; set; }
    }

    class AbilityValueItem : AbilityObject, IAbilityValue, IAbilityItemPrefab
    {
        public AbilityValueItem(float value, ItemPrefab itemPrefab)
        {
            Value = value;
            ItemPrefab = itemPrefab;
        }
        public float Value { get; set; }
        public ItemPrefab ItemPrefab { get; set; }
    }

    class AbilityValueString : AbilityObject, IAbilityValue, IAbilityString
    {
        public AbilityValueString(float value, string abilityString)
        {
            Value = value;
            String = abilityString;
        }
        public float Value { get; set; }
        public string String { get; set; }
    }

    class AbilityValueStringCharacter : AbilityObject, IAbilityValue, IAbilityString
    {
        public AbilityValueStringCharacter(float value, string abilityString, Character character)
        {
            Value = value;
            String = abilityString;
            Character = character;
        }
        public Character Character { get; set; }
        public float Value { get; set; }
        public string String { get; set; }
    }

    class AbilityStringCharacter : AbilityObject, IAbilityCharacter, IAbilityString
    {
        public AbilityStringCharacter(string abilityString, Character character)
        {
            String = abilityString;
            Character = character;
        }
        public Character Character { get; set; }
        public string String { get; set; }
    }

    class AbilityValueAffliction : AbilityObject, IAbilityValue, IAbilityAffliction
    {
        public AbilityValueAffliction(float value, Affliction affliction)
        {
            Value = value;
            Affliction = affliction;
        }
        public float Value { get; set; }
        public Affliction Affliction { get; set; }
    }

    class AbilityValueMission : AbilityObject, IAbilityValue, IAbilityMission
    {
        public AbilityValueMission(float value, Mission mission)
        {
            Value = value;
            Mission = mission;
        }
        public float Value { get; set; }
        public Mission Mission { get; set; }
    }

    // this is an exception class that should only be passed in this form, so classes that use it should cast into it directly
    class AbilityAttackData : AbilityObject, IAbilityCharacter
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

    class AbilityAttackResult : AbilityObject, IAbilityAttackResult
    {
        public AttackResult AttackResult { get; set; }

        public AbilityAttackResult(AttackResult attackResult)
        {
            AttackResult = attackResult;
        }
    }

    class AbilityCharacterSubmarine : AbilityObject, IAbilityCharacter, IAbilitySubmarine
    {
        public AbilityCharacterSubmarine(Character character, Submarine submarine)
        {
            Character = character;
            Submarine = submarine;
        }
        public Character Character { get; set; }
        public Submarine Submarine { get; set; }
    }

}
