namespace Barotrauma.Abilities
{
    interface IAbilityItemPrefab
    {
        public ItemPrefab ItemPrefab { get; set; }
    }

    interface IAbilityItem
    {
        public Item Item { get; set; }
    }

    interface IAbilityValue
    {
        public float Value { get; set; }
    }

    interface IAbilityMission
    {
        public Mission Mission { get; set; }
    }

    interface IAbilityCharacter
    {
        public Character Character { get; set; }
    }

    interface IAbilityString
    {
        public string String { get; set; }
    }

    interface IAbilityAffliction
    {
        public Affliction Affliction { get; set; }
    }

    interface IAbilityAttackResult
    {
        public AttackResult AttackResult { get; set; }
    }

    interface IAbilitySubmarine
    {
        public Submarine Submarine { get; set; }
    }
}
