#nullable enable

using System.Collections.Generic;
using Barotrauma;
using Barotrauma.Networking;
using Barotrauma.Plugins;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace HomeAndHarbor;

public static partial class LocationShortcutManager
{
    private const int MinDistanceBetweenShortcuts = 200;
    private const float ShortcutProbability = 0.66f;
    private const int MinimumShortcutsPerBiome = 2;

    /// <summary>
    /// Whether the shortcut network feature itself has been unlocked in the given campaign.
    /// Shortcuts are pre-generated but functionally ignored until this returns true.
    /// </summary>
    public static bool AreShortcutsUnlocked(CampaignMode campaign)
        => campaign.GetExtraField<bool>(nameof(AreShortcutsUnlocked));

    public static void SetShortcutsUnlocked(CampaignMode campaign, bool unlocked, bool createNetworkEvent)
    {
        if (AreShortcutsUnlocked(campaign) == unlocked) { return; }
        campaign.SetExtraField(nameof(AreShortcutsUnlocked), unlocked);

#if SERVER
        if (createNetworkEvent)
        {
            ServerSendShortcutsUnlocked();
        }
#endif
    }

#if SERVER
    static partial void ServerSendShortcutsUnlocked();
#endif

    [NetworkSerialize]
    public readonly record struct ShortcutsUnlockedNetworkData(bool Unlocked) : INetSerializableStruct;

    public static void GenerateShortcuts(CampaignMode campaign, Map map, List<Location> locations)
    {
        const int MaxAttempts = 100;

        // Locations adjacent to the start location should be skipped
        HashSet<Location> adjacentToStart = map.StartLocation.Connections
            .Select(c => c.OtherLocation(map.StartLocation))
            .ToHashSet();

        // Group suitable shortcut location candidates by biome.
        // Note: Empty nodes can have shortcuts, they just won't be visible until there's an outpost.
        // Hence we exclude non-outpost locations that can never become outposts (DisallowLocationTypeChanges)
        // to avoid "wasting" shortcuts.
        var candidateLocations = locations.Where(l =>
            !l.IsGateBetweenBiomes &&
            // Empty nodes can have shortcuts, they just won't be visible until there's an outpost.
            // Hence we exclude non-outpost locations that can never become outposts, to avoid "wasting" shortcuts.
            !l.DisallowLocationTypeChanges && !l.Type.DisallowLocationTypeChanges &&
            l != map.StartLocation &&
            !adjacentToStart.Contains(l) &&
            !map.EndLocations.Contains(l) &&
            !l.Biome.IsEndBiome).ToList();

        var locationsByBiome = candidateLocations
            .GroupBy(l => l.Biome.Identifier)
            .ToDictionary(g => g.Key, g => g.ToList());

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (TryAssignShortcuts(candidateLocations, locationsByBiome.Keys)) { return; }
        }
        DebugConsole.ThrowError($"Could not generate enough shortcuts for the map after {MaxAttempts} attempts, check the parameters!");
    }

    /// <summary>
    /// Randomly assigns shortcuts to the candidate locations.
    /// Returns false if some biome didn't get at least <see cref="MinimumShortcutsPerBiome"/> shortcuts.
    /// </summary>
    private static bool TryAssignShortcuts(List<Location> candidateLocations, IEnumerable<Identifier> biomeIds)
    {
        // Clear flags left over from previous attempts
        foreach (Location location in candidateLocations)
        {
            PersistentOutpostData.GetPersistentOutpostData(location).SetHasShortCut(hasShortcut: false, createNetworkEvent: false);
        }

        // Track shortcut positions per biome
        var shortcutPositionsByBiome = new Dictionary<Identifier, List<Vector2>>();
        foreach (Identifier biomeId in biomeIds)
        {
            shortcutPositionsByBiome[biomeId] = new List<Vector2>();
        }

        foreach (Location currentLocation in candidateLocations)
        {
            var biomeId = currentLocation.Biome.Identifier;
            var biomeShortcutPositions = shortcutPositionsByBiome[biomeId];

            // Skip if too close to an existing shortcut in the same biome
            if (biomeShortcutPositions.Any(pos =>
                Vector2.Distance(pos, currentLocation.MapPosition) < MinDistanceBetweenShortcuts))
            {
                continue;
            }

            if (Rand.Value(Rand.RandSync.ServerAndClient) <= ShortcutProbability)
            {
                PersistentOutpostData.GetPersistentOutpostData(currentLocation).SetHasShortCut(hasShortcut: true, createNetworkEvent: false);
                biomeShortcutPositions.Add(currentLocation.MapPosition);
            }
        }

        // Check if each biome has enough shortcuts
        return shortcutPositionsByBiome.All(kvp => kvp.Value.Count >= MinimumShortcutsPerBiome);
    }
    
    /// <summary>
    /// Check if a shortcut is available (visible and usable) at a location.
    /// </summary>
    internal static bool IsShortcutAvailable(Location? location, PersistentOutpostData? outpostData = null)
    {
        if (location is null) { return false; }
        // Shortcuts can exist at empty nodes too, but aren't visible until there's an outpost there.
        if (!location.Type.HasOutpost) { return false; }
        if (GameMain.GameSession?.Campaign is not CampaignMode campaign) { return false; }
        if (!AreShortcutsUnlocked(campaign)) { return false; }
        outpostData ??= PersistentOutpostData.GetPersistentOutpostData(location);
        return outpostData.HasShortcut;
    }

    public static void AddShortcut(Location location)
    {
        var outpostData = PersistentOutpostData.GetPersistentOutpostData(location);
        outpostData.SetHasShortCut(hasShortcut: true, createNetworkEvent: true);
    }

}
