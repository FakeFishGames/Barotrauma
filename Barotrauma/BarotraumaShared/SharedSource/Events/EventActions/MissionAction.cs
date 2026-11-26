using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    /// <summary>
    /// Unlocks a mission in a nearby level or location.
    /// </summary>
    public partial class MissionAction : EventAction
    {
        public enum Target
        {
            /// <summary>
            /// Default behavior: the mission is unlocked in the current location, or a location that matches specific criteria that's within a certain distance from the current location.
            /// </summary>
            Default,
            /// <summary>
            /// Only valid for actions executed by a mission. The mission is unlocked in the start location of the triggering mission, or a location that matches specific criteria that's within a certain distance from that location.
            /// </summary>
            MissionStartLocation,
            /// <summary>
            /// Only valid for actions executed by a mission. The mission is unlocked in the end location of the triggering mission, or a location that matches specific criteria that's within a certain distance from that location.
            /// </summary>
            MissionEndLocation
        }

        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the mission to unlock.")]
        public Identifier MissionIdentifier { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "Tag of the mission to unlock. If there are multiple missions with the tag, one is chosen randomly.")]
        public Identifier MissionTag { get; set; }

        [Serialize(Target.Default, IsPropertySaveable.Yes)]
        public Target TargetLocation { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "The mission can only be unlocked in a location that's occupied by this faction.")]
        public Identifier RequiredFaction { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "The mission can only be unlocked in a location that's in the specified biomes.")]
        public Identifier[] AllowedBiomes { get; set; }

        public ImmutableArray<Identifier> LocationTypes { get; }

        [Serialize(0, IsPropertySaveable.Yes, description: "Minimum distance from the current location to the location the mission is unlocked in (1 = one path between locations).")]
        public int MinLocationDistance { get; set; }

        [Serialize(int.MaxValue, IsPropertySaveable.Yes, description: "Maximum distance from the current location to the location the mission is unlocked in (1 = one path between locations). Unlimited by default. If CreateLocationIfNotFound is enabled and no location can be found or created within this distance, the limit is ignored as a last resort.")]
        public int MaxLocationDistance { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "If true, the mission has to be unlocked in a location further on the campaign map.")]
        public bool UnlockFurtherOnMap { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "Only relevant if UnlockFurtherOnMap is enabled. If true, the location will be marked as discovered and will become visible on the campaign map, even if it's hidden by the fog of war.")]
        public bool MarkLocationAsDiscovered { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "If true, a suitable location is forced on the map if one isn't found.")]
        public bool CreateLocationIfNotFound { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "If a location gets created, should the game attempt to unlock the initial missions on it?")]
        public bool UnlockInitialMission { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "Should the \"mission unlocked\" notification pop up when the mission is unlocked?")]
        public bool CreateUnlockNotification { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "The mission can only be unlocked in a location that isn't a biome game.")]
        public bool DisallowBiomeGates { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "The mission can only be unlocked in a location with a difficulty equal to or higher than this.")]
        public float MinLevelDifficulty { get; set; }

        [Serialize(float.MaxValue, IsPropertySaveable.Yes, description: "The mission can only be unlocked in a location with a difficulty equal to or less than this.")]
        public float MaxLevelDifficulty { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "The mission can only be unlocked in a location that's at least this far into the campaign map, as a percentage of the width of the map. 0.16 would mean the mission can be placed at the end of cold caverns at the earliest (there's 6 biomes, so 1/6).")]
        public float MinMapPosition { get; set; }

        [Serialize(1.0f, IsPropertySaveable.Yes, description: "The mission can only be unlocked in a location that's at most this far into the campaign map, as a percentage of the width of the map. 0.16 would mean the mission must be placed in the cold caverns (there's 6 biomes, so 1/6).")]
        public float MaxMapPosition { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "If enabled, the mission is unlocked in a level that leads to the chosen location, rather than in that location or leading out from it.")]
        public bool UnlockInLevelLeadingToLocation { get; set; }

        [Serialize(-1, IsPropertySaveable.Yes, description: "When set to a positive value, the mission will expire after the entered amount of world steps has passed if the mission has no time limit already. No failure conditions will be applied upon expiry. The steps don't count while the mission is available in the players' current location or being played.")]
        public int ExpireAfter { get; set; }

        /// <summary>
        /// Can be used to filter the locations where the mission can be unlocked.
        /// </summary>
        public Func<Location, bool> LocationPredicate;

        /// <summary>
        /// Triggers after the mission has been unlocked.
        /// </summary>
        public Action<Location, Mission> OnMissionUnlocked;

        private bool isFinished;

        private readonly Random random;

        public MissionAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element)
        {
            if (MissionIdentifier.IsEmpty && MissionTag.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\": neither MissionIdentifier or MissionTag has been configured.",
                    contentPackage: element.ContentPackage);
            }
            if (!MissionIdentifier.IsEmpty && !MissionTag.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\": both MissionIdentifier or MissionTag have been configured. The tag will be ignored.",
                    contentPackage: element.ContentPackage);
            }
            if (MaxLocationDistance < MinLocationDistance)
            {
                DebugConsole.ThrowError($"Error in event \"{parentEvent.Prefab.Identifier}\": MaxLocationDistance ({MaxLocationDistance}) is lower than MinLocationDistance ({MinLocationDistance}).",
                    contentPackage: element.ContentPackage);
            }
            LocationTypes = element.GetAttributeIdentifierArray("locationtype", Array.Empty<Identifier>()).ToImmutableArray();
            if (parentEvent != null)
            {
                //the action chooses the same mission if
                // 1. event seed is the same (based on level seed, changes when events are completed)
                // 2. event is the same (two different events shouldn't choose the same mission)
                // 3. the MissionAction is the same (two different actions in the same event shouldn't choose the same mission)
                random = new MTRandom(
                    parentEvent.RandomSeed +
                    ToolBox.StringToInt(parentEvent.Prefab.Identifier.Value) +
                    ParentEvent.Actions.Count);
            }
            else
            {
                random = new MTRandom();
            }

            //default to marking the location as discovered if there's a notification about the mission
            MarkLocationAsDiscovered = element.GetAttributeBool(nameof(MarkLocationAsDiscovered), CreateUnlockNotification);
        }

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }
        public override void Reset(bool shouldEndConversations = true)
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            //clients should never try to trigger missions by themselves
            //(Probably a theoretical issue. Clients don't run events, but missions can run actions when they fail/succeed, which could cause some problems -
            //although, since missions succeed/fail at the end of the round, any mismatches should get corrected by the clients receiving an up-to-date campaign save)
            if (GameMain.NetworkMember is { IsClient: true })
            {
                isFinished = true;
                return;
            }

            Identifier missionDebugId = (MissionIdentifier.IsEmpty ? MissionTag : MissionIdentifier);

            if (GameMain.GameSession.GameMode is CampaignMode campaign)
            {
                var unlockLocation = FindOrCreateUnlockLocation(campaign, missionDebugId);
                if (unlockLocation == null && CreateLocationIfNotFound && MaxLocationDistance < int.MaxValue)
                {
                    DebugConsole.NewMessage($"Failed to find or create a suitable location to unlock the mission \"{missionDebugId}\" within {MaxLocationDistance} steps. Attempting to remove the maximum distance requirement of the location...");
                    int originalMaxLocationDistance = MaxLocationDistance;
                    MaxLocationDistance = int.MaxValue;
                    unlockLocation = FindOrCreateUnlockLocation(campaign, missionDebugId);
                    MaxLocationDistance = originalMaxLocationDistance;
                }

                if (unlockLocation != null)
                {
                    Mission unlockedMission;
                    if (UnlockInLevelLeadingToLocation)
                    {
                        LocationConnection randomConnection = unlockLocation.Connections.GetRandomUnsynced();
                        Location otherLocation = randomConnection.OtherLocation(unlockLocation);
                        unlockedMission = UnlockMission(campaign, otherLocation, MissionIdentifier, MissionTag,
                            createMessageBox: CreateUnlockNotification, markLocationAsDiscovered: MarkLocationAsDiscovered,
                            forceConnection: randomConnection,
                            random: random, invokingContentPackage: ParentEvent?.Prefab.ContentPackage);
                        unlockedMission.OriginLocation = otherLocation;
                    }
                    else
                    {
                        unlockedMission = UnlockMission(campaign, unlockLocation, MissionIdentifier, MissionTag,
                            createMessageBox: CreateUnlockNotification, markLocationAsDiscovered: MarkLocationAsDiscovered,
                            random: random, invokingContentPackage: ParentEvent?.Prefab.ContentPackage);
                    }

                    if (unlockedMission != null)
                    {
                        //if this is a mission unlocking another mission, consider the origin of this mission as the origin of the followup-mission
                        if (ParentMission != null)
                        {
                            unlockedMission.OriginLocation = ParentMission.OriginLocation;
                        }

                        if (ExpireAfter > 0 && unlockedMission.Prefab.TimeLimitWorldSteps < 0)
                        {
                            unlockedMission.WorldStepsUntilExpiry = ExpireAfter;
                        }

                        OnMissionUnlocked?.Invoke(unlockLocation, unlockedMission);
                    }
                }
                else
                {
                    DebugConsole.AddWarning($"Failed to find a suitable location to unlock the mission \"{missionDebugId}\" "+
                        $"(LocationType: {string.Join(", ", LocationTypes)}, MinLocationDistance: {MinLocationDistance}, MaxLocationDistance: {MaxLocationDistance}, UnlockFurtherOnMap: {UnlockFurtherOnMap}, MinLevelDifficulty: {MinLevelDifficulty}, MaxLevelDifficulty: {MaxLevelDifficulty}, MinMapPosition: {MinMapPosition}, MaxMapPosition: {MaxMapPosition})",
                        ParentEvent.Prefab.ContentPackage);
                }
            }
            isFinished = true;
        }

        /// <summary>
        /// Finds a location to unlock the mission in. If there isn't one and <see cref="CreateLocationIfNotFound"/> is enabled, attempts to change the type of another location to make it suitable.
        /// </summary>
        private Location FindOrCreateUnlockLocation(CampaignMode campaign, Identifier missionDebugId)
        {
            var unlockLocation = FindUnlockLocation(MinLocationDistance, UnlockFurtherOnMap, LocationTypes, mustAllowLocationTypeChanges: false, disallowBiomeGates: DisallowBiomeGates);
            if (unlockLocation == null && UnlockFurtherOnMap)
            {
                DebugConsole.NewMessage($"Failed to find a suitable location to unlock the mission \"{missionDebugId}\" further on the map. Attempting to find a location earlier on the map...");
                unlockLocation = FindUnlockLocation(MinLocationDistance, unlockFurtherOnMap: false, LocationTypes, mustAllowLocationTypeChanges: false, disallowBiomeGates: DisallowBiomeGates);
            }

            if (unlockLocation == null && CreateLocationIfNotFound)
            {
                //MaxLocationDistance can be lower than the 3 steps we'd normally want
                int minDistance = Math.Min(Math.Max(MinLocationDistance, 3), MaxLocationDistance);

                DebugConsole.NewMessage($"Failed to find a suitable location to unlock the mission \"{missionDebugId}\". Attempting to change the type of an empty location to create a suitable location...");
                //find an empty location at least 3 steps away, further on the map
                var emptyLocation = FindUnlockLocation(minDistance, unlockFurtherOnMap: true, "none".ToIdentifier().ToEnumerable(),
                    mustAllowLocationTypeChanges: true,
                    requireCorrectFaction: false,
                    disallowBiomeGates: DisallowBiomeGates);
                if (emptyLocation == null)
                {
                    DebugConsole.NewMessage($"Failed to find a suitable empty location further on the map. Attempting to find a location earlier on the map...");
                    emptyLocation = FindUnlockLocation(minDistance, unlockFurtherOnMap: false, "none".ToIdentifier().ToEnumerable(),
                        mustAllowLocationTypeChanges: true,
                        requireCorrectFaction: false,
                        disallowBiomeGates: DisallowBiomeGates);
                    if (emptyLocation == null)
                    {
                        DebugConsole.NewMessage($"Failed to find a suitable empty location earlier on the map. Attempting to find any outpost we could convert...");
                        emptyLocation = FindUnlockLocation(minDistance, unlockFurtherOnMap: false, Tags.AnyOutpost.ToEnumerable(),
                            mustAllowLocationTypeChanges: true,
                            requireCorrectFaction: false,
                            disallowBiomeGates: DisallowBiomeGates);
                        if (emptyLocation == null && (MaxLevelDifficulty < float.MaxValue || MaxMapPosition < 1.0f))
                        {
                            DebugConsole.NewMessage($"Failed to find an outpost we could convert. Attempting to remove the maximum level difficulty and map position requirement of the location...");
                            float originalMaxLevelDifficulty = MaxLevelDifficulty;
                            MaxLevelDifficulty = float.MaxValue;
                            float originalMaxMapPosition = MaxMapPosition;
                            MaxMapPosition = 1.0f;
                            emptyLocation = FindUnlockLocation(minDistance, unlockFurtherOnMap: false, "none".ToIdentifier().ToEnumerable(),
                                mustAllowLocationTypeChanges: true,
                                requireCorrectFaction: false,
                                disallowBiomeGates: DisallowBiomeGates);
                            MaxLevelDifficulty = originalMaxLevelDifficulty;
                            MaxMapPosition = originalMaxMapPosition;
                        }
                    }
                }
                if (emptyLocation != null)
                {
                    System.Diagnostics.Debug.Assert(!emptyLocation.LocationTypeChangesBlocked);
                    emptyLocation.ChangeType(campaign, LocationType.Prefabs[LocationTypes[0]], unlockInitialMissions: UnlockInitialMission, createNetworkEvent: true);
                    unlockLocation = emptyLocation;
                    if (!RequiredFaction.IsEmpty)
                    {
                        emptyLocation.Faction = campaign.GetFaction(RequiredFaction);
                    }
                }
            }
            return unlockLocation;
        }

        public static Mission UnlockMission(CampaignMode campaign, Location unlockLocation, Identifier MissionIdentifier, Identifier MissionTag, bool createMessageBox, bool markLocationAsDiscovered,
            LocationConnection forceConnection = null, Random random = null, ContentPackage invokingContentPackage = null, float? overrideLevelDifficulty = null, bool createNetworkEvent = true)
        {
            Mission unlockedMission = null;
            if (!MissionIdentifier.IsEmpty)
            {
                unlockedMission = unlockLocation.UnlockMissionByIdentifier(MissionIdentifier, forceConnection,
                    invokingContentPackage: invokingContentPackage);
            }
            else if (!MissionTag.IsEmpty)
            {
                unlockedMission = unlockLocation.UnlockMissionByTag(MissionTag, random, forceConnection,
                    invokingContentPackage: invokingContentPackage, overrideLevelDifficulty: overrideLevelDifficulty);
            }
            if (campaign is MultiPlayerCampaign mpCampaign)
            {
                mpCampaign.IncrementLastUpdateIdForFlag(MultiPlayerCampaign.NetFlags.MapAndMissions);
            }
            if (unlockedMission != null)
            {
                unlockedMission.OriginLocation = campaign.Map.CurrentLocation;
                if (markLocationAsDiscovered)
                {
                    campaign.Map.Discover(unlockLocation, checkTalents: false);
                }
                bool missionTakesPlaceInLocation = unlockedMission.Locations[0] == unlockedMission.Locations[1] || unlockedMission.Locations[1] == null;
                if (missionTakesPlaceInLocation)
                {
                    DebugConsole.Log($"Unlocked mission \"{unlockedMission.Name}\" in the location \"{unlockLocation.DisplayName}\".");
                }
                else
                {
                    DebugConsole.Log($"Unlocked mission \"{unlockedMission.Name}\" in the connection from \"{unlockedMission.Locations[0].DisplayName}\" to \"{unlockedMission.Locations[1].DisplayName}\".");
                }

                if (createMessageBox)
                {
#if CLIENT
                    new GUIMessageBox(string.Empty, RichString.Rich(TextManager.GetWithVariable("missionunlocked", "[missionname]", unlockedMission.NameWithContentPackage)),
                        Array.Empty<LocalizedString>(), type: GUIMessageBox.Type.InGame, icon: unlockedMission.Prefab.Icon, relativeSize: new Vector2(0.3f, 0.15f), minSize: new Point(512, 128))
                    {
                        IconColor = unlockedMission.Prefab.IconColor
                    };
#endif
                }
#if SERVER
                missionsUnlockedThisRound.Add(new MissionUnlock(unlockedMission, createMessageBox));
                if (createNetworkEvent)
                {
                    WriteMissionUnlock(unlockedMission, createMessageBox);
                }
#endif
            }

            return unlockedMission;
        }

        private Location FindUnlockLocation(int minDistance, bool unlockFurtherOnMap, IEnumerable<Identifier> locationTypes, bool mustAllowLocationTypeChanges, bool requireCorrectFaction = true, bool disallowBiomeGates = false)
        {
            var campaign = GameMain.GameSession.GameMode as CampaignMode;
            Location targetLocation = TargetLocation switch
            {
                Target.Default => campaign.CurrentLocation,
                Target.MissionStartLocation => ParentMission?.Locations[0] ?? campaign.CurrentLocation,
                Target.MissionEndLocation => ParentMission?.Locations[1] ?? campaign.CurrentLocation,
                _ => throw new ArgumentOutOfRangeException(nameof(TargetLocation), TargetLocation, null)
            };

            if (LocationTypes.Length == 0 && minDistance <= 1)
            {
                return targetLocation;
            }

            var currentLocation = targetLocation;
            int distance = 0;
            HashSet<Location> checkedLocations = new HashSet<Location>();
            HashSet<Location> pendingLocations = new HashSet<Location>() { currentLocation };
            do
            {
                List<Location> currentLocations = pendingLocations.ToList();
                pendingLocations.Clear();
                foreach (var location in currentLocations)
                {
                    checkedLocations.Add(location);
                    if (IsLocationValid(currentLocation, location, unlockFurtherOnMap, distance, minDistance, locationTypes, mustAllowLocationTypeChanges, requireCorrectFaction, disallowBiomeGates)) 
                    {
                        return location;
                    }
                    else
                    {
                        foreach (LocationConnection connection in location.Connections)
                        {
                            var otherLocation = connection.OtherLocation(location);
                            if (checkedLocations.Contains(otherLocation)) { continue; }
                            pendingLocations.Add(otherLocation);
                        }
                    }
                }
                distance++;
            } while (pendingLocations.Any() && distance <= MaxLocationDistance);

            return null;
        }

        private bool IsLocationValid(Location currLocation, Location location, bool unlockFurtherOnMap, int distance, int minDistance, IEnumerable<Identifier> locationTypes, bool mustAllowLocationTypeChanges, bool requireCorrectFaction, bool disallowBiomeGates)
        {
            if (disallowBiomeGates && location.IsGateBetweenBiomes)
            {
                return false;
            }
            if (mustAllowLocationTypeChanges && location.LocationTypeChangesBlocked)
            {
                return false;
            }
            if (requireCorrectFaction && !RequiredFaction.IsEmpty)
            {
                if (location.Faction?.Prefab.Identifier != RequiredFaction &&
                    location.SecondaryFaction?.Prefab.Identifier != RequiredFaction)
                {
                    return false;
                }
            }
            if (location.LevelData.Difficulty < MinLevelDifficulty || location.LevelData.Difficulty > MaxLevelDifficulty)
            {
                return false;
            }

            float relativeMapPosition = GameMain.GameSession?.Map is { } map ? location.MapPosition.X / map.Width : 0f;
            if (relativeMapPosition < MinMapPosition || relativeMapPosition > MaxMapPosition)
            {
                return false;
            }
            if (AllowedBiomes.Length > 0 && !AllowedBiomes.Contains(location.Biome.Identifier))
            {
                return false;
            }
            if (!locationTypes.Contains(location.Type.Identifier) && !(location.HasOutpost() && locationTypes.Contains(Tags.AnyOutpost)))
            {
                return false;
            }
            if (distance < minDistance) 
            { 
                return false; 
            }
            if (unlockFurtherOnMap && location.MapPosition.X < currLocation.MapPosition.X)
            {
                return false;
            }
            if (LocationPredicate != null && !LocationPredicate(location))
            {
                return false;
            }
            return true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(MissionAction)} -> ({(MissionIdentifier.IsEmpty ? MissionTag : MissionIdentifier)})";
        }
    }
}