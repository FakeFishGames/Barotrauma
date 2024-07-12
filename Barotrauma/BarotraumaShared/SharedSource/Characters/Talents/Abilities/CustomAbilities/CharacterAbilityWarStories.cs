namespace Barotrauma.Abilities;

/// <summary>
/// Hardcoded ability for the "War Stories" talent.
/// Spawns an item and sets the health multiplier to the target stat value.
///
/// The item spawned should have a default health of 1 because we set the multiplier.
/// This is because we already had existing Item.HealthMultiplier that gets synced and
/// everything but not one for setting the max health directly to some value and I didn't
/// want to add a new one just for this.
/// </summary>
internal class CharacterAbilityWarStories : CharacterAbility
{
    private readonly Identifier targetStat;
    private readonly float minCondition;

    private readonly ItemPrefab prefab;

    public CharacterAbilityWarStories(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
    {
        targetStat = abilityElement.GetAttributeIdentifier("target", Identifier.Empty);
        minCondition = abilityElement.GetAttributeFloat("mincondition", 1);

        if (targetStat.IsEmpty)
        {
            DebugConsole.ThrowError($"{nameof(CharacterAbilityWarStories)}: target stat is not defined", contentPackage: abilityElement.ContentPackage);
        }

        Identifier spawnedItem = abilityElement.GetAttributeIdentifier("item", Identifier.Empty);
        if (!ItemPrefab.Prefabs.TryGet(spawnedItem, out prefab))
        {
            DebugConsole.ThrowError($"{nameof(CharacterAbilityWarStories)}: spawned item \"{spawnedItem}\" could not be found.", contentPackage: abilityElement.ContentPackage);
        }
    }

    protected override void ApplyEffect()
    {
        if (prefab is null || Character is null) { return; }
        
        float condition = Character.Info?.GetSavedStatValue(StatTypes.None, targetStat) ?? 0;
        if (condition < minCondition) { return; }

        if (GameMain.GameSession?.RoundEnding ?? true)
        {
            Item item = new(prefab, Character.WorldPosition, Character.Submarine)
            {
                Condition = condition,
                HealthMultiplier = condition
            };
            Character.Inventory.TryPutItem(item, Character, item.AllowedSlots);
        }
        else
        {
            Entity.Spawner?.AddItemToSpawnQueue(prefab, Character.Inventory, condition: condition, onSpawned: item =>
            {
                item.HealthMultiplier = condition;
            });
        }
    }

    protected override void ApplyEffect(AbilityObject abilityObject)
        => ApplyEffect();
}