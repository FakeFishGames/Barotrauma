using System.Collections.Generic;
using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Plugins;
using Microsoft.Xna.Framework;

namespace HomeAndHarbor;

/// <summary>
/// Enables or disables Descendants spreading independently of shortcuts, and optionally configures
/// parameters for the spreading (frequency, shortcut search depth, zone outpost count targets).
/// Can be called again with different parameters.
/// The class also handles Descendants faction spreading and related debug commands.
/// </summary>
class EnableDescendantsSpreadingAction(ScriptedEvent parentEvent, ContentXElement element) : EventAction(parentEvent, element)
{
    public const int DefaultWorldStepsPerSpreadAttempt = 2;
    public const int DefaultMaxDistanceFromShortcut = 2;
    public const string DefaultMinOutpostsPerZone = "1,2,3,4,5";
    public const string DefaultMaxOutpostsPerZone = "2,4,6,6,6";

    public const string FieldSpreadingEnabled = "DescendantsSpreadingEnabled";
    public const string FieldWorldStepsPerSpreadAttempt = "DescendantsWorldStepsPerSpreadAttempt";
    public const string FieldMaxDistanceFromShortcut = "DescendantsMaxDistanceFromShortcut";
    public const string FieldDescendantsToAdd = "DescendantsToAdd";
    public const string FieldStepCounter = "DescendantSpreadStepCounter";

    private static readonly Identifier DescendantsIdentifier = "descendants".ToIdentifier();
    private static readonly Identifier DescendantOutpostIdentifier = "DescendantOutpost".ToIdentifier();

    #region Event Action

    [Serialize(true, IsPropertySaveable.Yes, description: "Whether Descendants spreading should be enabled.")]
    public bool Enabled { get; set; }

    [Serialize(DefaultWorldStepsPerSpreadAttempt, IsPropertySaveable.Yes, description: "How many world steps between each spreading attempt.")]
    public int WorldStepsPerSpreadAttempt { get; set; }

    [Serialize(DefaultMaxDistanceFromShortcut, IsPropertySaveable.Yes, description: "Maximum search distance (in map graph edges) from a shortcut for eligible locations.")]
    public int MaxDistanceFromShortcut { get; set; }

    [Serialize(DefaultMinOutpostsPerZone, IsPropertySaveable.Yes, description: "Comma-separated per-zone minimum target counts for Descendants outposts (eg. '1,2,3,4,5,0'). Actual targets are randomized between the min and max per zone.")]
    public string MinOutpostsPerZone { get; private set; } = DefaultMinOutpostsPerZone;

    [Serialize(DefaultMaxOutpostsPerZone, IsPropertySaveable.Yes, description: "Comma-separated per-zone maximum target counts for Descendants outposts (eg. '1,2,3,4,5,0'). Actual targets are randomized between the min and max per zone.")]
    public string MaxOutpostsPerZone { get; private set; } = DefaultMaxOutpostsPerZone;

    private bool isFinished = false;

    public override bool IsFinished(ref string goTo) => isFinished;

    public override void Reset(bool shouldEndConversations = true) => isFinished = false;

    public override void Update(float deltaTime)
    {
        if (isFinished) { return; }

        if (GameMain.GameSession?.Campaign is not CampaignMode campaign)
        {
            DebugConsole.ThrowError($"Error in event {ParentEvent.Prefab.Identifier}: cannot use {nameof(EnableDescendantsSpreadingAction)} when a campaign is not active.");
            return;
        }

        campaign.SetExtraField(FieldSpreadingEnabled, Enabled);
        campaign.SetExtraField(FieldWorldStepsPerSpreadAttempt, WorldStepsPerSpreadAttempt);
        campaign.SetExtraField(FieldMaxDistanceFromShortcut, MaxDistanceFromShortcut);
        InitDescendantsZoneTargets(campaign, campaign.Map, MinOutpostsPerZone, MaxOutpostsPerZone);

        // Reset the step counter so repeated calls start fresh
        campaign.SetExtraField(FieldStepCounter, 0);

#if DEBUG
        DebugConsole.NewMessage($"EnableDescendantsSpreadingAction triggered, DescendantsSpreadingEnabled: {Enabled}");
#endif

        isFinished = true;
    }

    public override string ToDebugString()
    {
        return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(EnableDescendantsSpreadingAction)} -> Enabled={Enabled}, WorldStepsPerSpreadAttempt={WorldStepsPerSpreadAttempt}, MaxDistanceFromShortcut={MaxDistanceFromShortcut}, MinOutpostsPerZone={MinOutpostsPerZone}, MaxOutpostsPerZone={MaxOutpostsPerZone}";
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Parses comma-separated per-zone min and max target counts for Descendants outposts, randomizes
    /// a value between the min and max for each zone, subtracts any Descendants outposts already
    /// present on the map, and stores the results in the campaign.
    /// Counts for missing zones are padded with zeroes.
    /// </summary>
    public static void InitDescendantsZoneTargets(CampaignMode campaign, Map map, string? zoneMinString, string? zoneMaxString)
    {
        string[] minParts = zoneMinString?.Split(',') ?? Array.Empty<string>();
        string[] maxParts = zoneMaxString?.Split(',') ?? Array.Empty<string>();
        int zoneCount = MapGenerationParams.Instance.DifficultyZones;

        // Count potential existing Descendants outposts per zone so we don't overshoot the targets
        int[] existingCounts = new int[zoneCount];
        foreach (Location loc in map.Locations)
        {
            if (loc.Type.HasOutpost && loc.Faction?.Prefab.Identifier == DescendantsIdentifier)
            {
                int zoneIdx = map.GetZoneIndex(loc.MapPosition.X) - 1;
                if (zoneIdx >= 0 && zoneIdx < zoneCount)
                {
                    existingCounts[zoneIdx]++;
                }
            }
        }

        // Use a salted seed so the random sequence doesn't correlate with map generation
        var rand = new MTRandom(ToolBox.StringToInt(map.Seed ?? "") + 0x4321);

        int[] targetCounts = new int[zoneCount];
        for (int i = 0; i < zoneCount; i++)
        {
            int minValue = (i < minParts.Length && int.TryParse(minParts[i].Trim(), out int parsedMin)) ? parsedMin : 0;
            int maxValue = (i < maxParts.Length && int.TryParse(maxParts[i].Trim(), out int parsedMax)) ? parsedMax : 0;

            // Ensure max >= min
            maxValue = Math.Max(maxValue, minValue);

            targetCounts[i] = Math.Max(0, rand.Next(minValue, maxValue + 1) - existingCounts[i]);
        }
        campaign.SetExtraField(FieldDescendantsToAdd, targetCounts);
    }

    /// <summary>
    /// Disables spreading and resets its parameters. Also clears the faction from the locations the Descendants have taken over,
    /// so a new faction gets assigned when the location reverts to its original type (see <see cref="Location.Reset"/>).
    /// Used when the end of the campaign is reached.
    /// </summary>
    public static void ResetSpreading(CampaignMode campaign, Map map)
    {
        campaign.SetExtraField(FieldSpreadingEnabled, false);
        campaign.SetExtraField(FieldWorldStepsPerSpreadAttempt, DefaultWorldStepsPerSpreadAttempt);
        campaign.SetExtraField(FieldMaxDistanceFromShortcut, DefaultMaxDistanceFromShortcut);
        campaign.SetExtraField<int[]?>(FieldDescendantsToAdd, null);
        campaign.SetExtraField(FieldStepCounter, 0);

        foreach (Location location in map.Locations)
        {
            //same conditions as in Location.Reset: only locations that are going to revert to their original type
            if (location.Type == location.OriginalType || location.DisallowLocationTypeChanges) { continue; }
            if (location.Faction?.Prefab.Identifier != DescendantsIdentifier) { continue; }

            location.Faction = null;
            //set to Descendants when the location was taken over
            location.OriginalFaction = null;
        }
    }

    #endregion

    #region Spreading Logic

    /// <summary>
    /// Spreads Descendants outposts near shortcuts. Called via PluginOnProgressWorldDelegate after all
    /// normal location type changes.
    /// Finds at most one suitable location near a shortcut and converts it to a Descendants outpost.
    /// </summary>
    public static void SpreadDescendants(Map map, CampaignMode campaign)
    {
        if (!campaign.GetExtraField<bool>(FieldSpreadingEnabled)) { return; }

        // Only attempt spreading every N world steps
        int stepsPerAttempt = campaign.GetExtraField<int>(FieldWorldStepsPerSpreadAttempt);
        int stepCounter = campaign.GetExtraField<int>(FieldStepCounter);
        campaign.SetExtraField(FieldStepCounter, stepCounter + 1);
        if (stepCounter % stepsPerAttempt != 0)
        {
            return;
        }

        if (campaign.GetExtraField<int[]>(FieldDescendantsToAdd) is not int[] remainingOutpostsToAdd)
        {
            DebugConsole.AddWarning("SpreadDescendants: DescendantsToAdd was empty?");
            return;
        }

        // Early exit if we don't need any more Descendants (for any zone)
        if (!remainingOutpostsToAdd.Any(r => r > 0))
        {
            return;
        }

        if (!LocationType.Prefabs.TryGet(DescendantOutpostIdentifier, out var descendantOutpostType))
        {
            DebugConsole.AddWarning("DescendantOutpost location type not found, can't spawn Descendants outpost");
            return;
        }

        // First look at only empty locations, if none are left then accept converting existing outposts
        int maxDistance = campaign.GetExtraField<int>(FieldMaxDistanceFromShortcut);
        for (int pass = 0; pass < 2; pass++)
        {
            bool allowOutpostConversion = pass > 0;
            if (TrySpreadToOneLocation(map, campaign, remainingOutpostsToAdd, descendantOutpostType, allowOutpostConversion, maxDistance))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Attempts to find and convert a single location near a shortcut to a Descendants outpost.
    /// Uses lazy BFS starting from shortcut locations, returning as soon as a result is found.
    /// </summary>
    private static bool TrySpreadToOneLocation(Map map, CampaignMode campaign, int[] remaining,
        LocationType descendantOutpostType, bool allowOutpostConversion, int maxDistance)
    {
        // Start the BFS "frontier" with all shortcut locations
        var visited = new HashSet<Location>();
        var currentRing = new List<Location>();
        foreach (Location location in map.Locations)
        {
            if (PersistentOutpostData.GetPersistentOutpostData(location) is { HasShortcut: true })
            {
                visited.Add(location);
                currentRing.Add(location);
            }
        }

        // Check each "ring" of distance, starting from 0 (shortcut locations)
        for (int distance = 0; distance <= maxDistance; distance++)
        {
            currentRing.Shuffle(Rand.RandSync.Unsynced);

            // Check each zone in order
            // (empty shortcut locations are sparse in the early map so we want to prioritize those to avoid wasting them)
            for (int zoneIdx = 0; zoneIdx < remaining.Length; zoneIdx++)
            {
                // Skip zones that don't need more Descendants
                if (remaining[zoneIdx] <= 0)
                {
                    continue;
                }

                foreach (Location loc in currentRing)
                {
                    if (map.GetZoneIndex(loc.MapPosition.X) - 1 != zoneIdx)
                    {
                        continue;
                    }
                    if (!IsEligibleForDescendantsConversion(map, loc, remaining, allowOutpostConversion))
                    {
                        continue;
                    }

                    if (map.ChangeLocationType(campaign, loc, descendantOutpostType))
                    {
                        remaining[zoneIdx]--;
                        var msg = TextManager.GetWithVariable("map.locationchange.descendantsestablished", "[name]",
                            $"‖color:gui.yellow‖{loc.DisplayName}‖end‖");
                        // Store the message so it gets serialized and clients can show it after receiving the map update in MP
                        loc.LastTypeChangeMessage = msg.Value;
#if CLIENT
                        map.AddMapNotification(msg, loc);
#endif
                        // Normally set in map generation, but for Descendants, we'll have to set it when the location is converted.
                        loc.OriginalFaction =  campaign.GetFaction(DescendantsIdentifier);
                        // Early exit as soon as one result is found
                        return true;
                    }
                }
            }

            // Expand to next distance ring only if no candidate was found at the current distance
            if (distance < maxDistance)
            {
                var nextRing = new List<Location>();
                foreach (Location loc in currentRing)
                {
                    foreach (var connection in loc.Connections)
                    {
                        var neighbor = connection.OtherLocation(loc);
                        if (visited.Add(neighbor))
                        {
                            nextRing.Add(neighbor);
                        }
                    }
                }
                currentRing = nextRing;
            }
        }

        return false;
    }

    private static bool IsEligibleForDescendantsConversion(Map map, Location location, int[] remaining, bool allowOutpostConversion)
    {
        if (location.Biome.IsEndBiome || location.IsGateBetweenBiomes) { return false; }
        if (location == map.CurrentLocation || location == map.SelectedLocation) { return false; }
        if (location.LocationTypeChangesBlocked) { return false; }
        if (location.Type.HasOutpost && location.Faction?.Prefab.Identifier == DescendantsIdentifier) { return false; }
        if (!allowOutpostConversion && location.Type.HasOutpost) { return false; } // Togglable
        if (PersistentOutpostData.GetPersistentOutpostData(location) is { IsPlayerOwned: true }) { return false; }
        int zoneArrayIndex = map.GetZoneIndex(location.MapPosition.X) - 1;
        if (zoneArrayIndex < 0 || zoneArrayIndex >= remaining.Length) { return false; }
        return remaining[zoneArrayIndex] > 0;
    }

    #endregion

    #region Debug Commands

    public static void RegisterDebugCommands()
    {
        DebugConsole.RegisterCommand(
            command: "testmapsdlc",
            helpMessage: "testmapsdlc [stepsBeforeShortcutsUnlocked] [mapcount] [stepsAfterShortcuts=200]: " +
                "generates campaign maps and simulates the spreading of the Descendants faction. " +
                "stepsBeforeShortcutsUnlocked = world steps before the shortcuts network is unlocked. " +
                "If mapcount is omitted, runs until cancelled (by pressing C).",
            flags: CommandFlags.None,
            onCommandExecuted: (string[] args) =>
            {
                if (args.Length == 0 || !int.TryParse(args[0], out int stepsBeforeShortcutsUnlocked))
                {
                    DebugConsole.ThrowError("Usage: testmapsdlc [stepsBeforeShortcutsUnlocked] [mapcount] [stepsAfterShortcuts=200]");
                    return;
                }
                int? mapcount = args.Length > 1 && int.TryParse(args[1], out int a) ? a : null;
                int stepsAfterShortcuts = args.Length > 2 && int.TryParse(args[2], out int p) ? p : 200;

                var results = new List<string>();
                DebugConsole.StartTestMaps(
                    amount: mapcount,
                    onMapGenerated: (seed, map, campaign) =>
                        SimulateDescendantsSpreading(seed, map, campaign, stepsBeforeShortcutsUnlocked, stepsAfterShortcuts, results),
                    onBatchFinished: () => OutputTestResults(results));
            });

        DebugConsole.RegisterCommand(
            command: "testmapdlc",
            helpMessage: "testmapdlc [seed] [stepsBeforeShortcutsUnlocked] [stepsAfterShortcuts=200]: " +
                "generates a single campaign map and simulates the spreading of the Descendants faction.",
            flags: CommandFlags.None,
            onCommandExecuted: (string[] args) =>
            {
                if (args.Length < 2 || !int.TryParse(args[1], out int stepsBeforeShortcutsUnlocked))
                {
                    DebugConsole.ThrowError("Usage: testmapdlc [seed] [stepsBeforeShortcutsUnlocked] [stepsAfterShortcuts=200]");
                    return;
                }
                int stepsAfterShortcuts = args.Length > 2 && int.TryParse(args[2], out int p) ? p : 200;

                var results = new List<string>();
                DebugConsole.StartTestMaps(
                    fixedSeed: args[0],
                    amount: 1,
                    onMapGenerated: (seed, map, campaign) => SimulateDescendantsSpreading(seed, map, campaign, stepsBeforeShortcutsUnlocked, stepsAfterShortcuts, results),
                    onBatchFinished: () => OutputTestResults(results));
            });
    }

    private static void SimulateDescendantsSpreading(string seed, Map map, CampaignMode campaign, int stepsBeforeShortcutsUnlocked, int stepsAfterShortcuts, List<string> results)
    {
        int totalSteps = stepsBeforeShortcutsUnlocked + stepsAfterShortcuts;

        for (int i = 0; i < totalSteps; i++)
        {
            if (i == stepsBeforeShortcutsUnlocked)
            {
                LocationShortcutManager.SetShortcutsUnlocked(campaign, true, createNetworkEvent: false);
                campaign.SetExtraField(FieldSpreadingEnabled, true);
                campaign.SetExtraField(FieldWorldStepsPerSpreadAttempt, DefaultWorldStepsPerSpreadAttempt);
                campaign.SetExtraField(FieldMaxDistanceFromShortcut, DefaultMaxDistanceFromShortcut);
                InitDescendantsZoneTargets(campaign, map, DefaultMinOutpostsPerZone, DefaultMaxOutpostsPerZone);
            }

            map.ProgressWorld(campaign);
        }

        // Count results
        int totalOutposts = 0;
        int descendantsOutposts = 0;
        int totalShortcuts = 0;
        int totalLocations = map.Locations.Count;
        int[] descendantsPerZone = new int[MapGenerationParams.Instance.DifficultyZones - 1];
        foreach (Location location in map.Locations)
        {
            if (location.Type.HasOutpost)
            {
                totalOutposts++;
                if (location.Faction?.Prefab.Identifier == DescendantsIdentifier)
                {
                    descendantsOutposts++;
                    int zone = map.GetZoneIndex(location.MapPosition.X);
                    descendantsPerZone[zone - 1]++;
                }
            }
            if (PersistentOutpostData.GetPersistentOutpostData(location) is { HasShortcut: true })
            {
                totalShortcuts++;
            }
        }

        string descendantsPerZoneStr = "[" + string.Join(",", descendantsPerZone) + "]";
        results.Add(
            $"[{seed}] {totalShortcuts}/{totalLocations} shortcuts/total locations, {descendantsOutposts}/{totalOutposts} Descendant/total outposts, Descendants per zone: {descendantsPerZoneStr}");
    }

    // Flush the results at once so that they're easily readable in one place and not sprinkled among the debug spam
    private static void OutputTestResults(List<string> results)
    {
        foreach (string result in results)
        {
            DebugConsole.NewMessage(result, Color.Cyan);
        }
    }

    #endregion
}
