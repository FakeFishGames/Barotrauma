﻿namespace Barotrauma.Abilities;

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
    private readonly float normalQualityThreshold;
    private readonly float goodQualityThreshold;
    private readonly float excellentQualityThreshold;
    private readonly float masterworkQualityThreshold;

    private readonly ItemPrefab prefab;

    public CharacterAbilityWarStories(CharacterAbilityGroup characterAbilityGroup, ContentXElement abilityElement) : base(characterAbilityGroup, abilityElement)
    {
        targetStat = abilityElement.GetAttributeIdentifier("target", Identifier.Empty);
        normalQualityThreshold = abilityElement.GetAttributeFloat("normalqualitythreshold", 4);
        goodQualityThreshold = abilityElement.GetAttributeFloat("goodqualitythreshold", 10);
        excellentQualityThreshold = abilityElement.GetAttributeFloat("excellentqualitythreshold", 20);
        masterworkQualityThreshold = abilityElement.GetAttributeFloat("masterworkqualitythreshold", 30);

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
        
        float statValue = Character.Info?.GetSavedStatValue(StatTypes.None, targetStat) ?? 0;

        if (statValue < normalQualityThreshold) { return; }

        int quality = 0;
        if (statValue >= masterworkQualityThreshold) { quality = 3; }
        else if (statValue >= excellentQualityThreshold) { quality = 2; }
        else if (statValue >= goodQualityThreshold) { quality = 1; }

        if (GameMain.GameSession?.RoundEnding ?? true)
        {
            Item item = new(prefab, Character.WorldPosition, Character.Submarine)
            {
                Quality = quality,
            };
            Character.Inventory.TryPutItem(item, Character, item.AllowedSlots);
        }
        else
        {
            Entity.Spawner?.AddItemToSpawnQueue(prefab, Character.Inventory, quality: quality, onSpawned: item =>
            {
                item.Quality = quality;
            });
        }
    }

    protected override void ApplyEffect(AbilityObject abilityObject)
        => ApplyEffect();
}