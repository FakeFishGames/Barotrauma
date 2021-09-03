namespace Barotrauma.Abilities
{
    interface IAbilityItemPrefab
    {
        public ItemPrefab ItemPrefab { get; set; }
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
}
