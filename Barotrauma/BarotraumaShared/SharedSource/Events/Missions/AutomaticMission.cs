#nullable enable

using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public class AutomaticMission
    {
        public readonly Identifier MissionTag;
        public readonly LevelData.LevelType LevelType;
        public readonly float MinReputation, MaxReputation;
        public readonly float MinProbability, MaxProbability;
        /// <summary>
        /// How far from a faction outpost can the mission be triggered in?
        /// </summary>
        public readonly int MaxDistanceFromFactionOutpost;
        /// <summary>
        /// How far from a the player's current location can the mission be triggered in? Defaults to 0 (triggers in the same location).
        /// </summary>
        public readonly int MaxDistanceFromCurrentLocation;

        public readonly bool DisallowBetweenOtherFactionOutposts;

        public AutomaticMission(ContentXElement element, string parentDebugName)
        {
            MissionTag = element.GetAttributeIdentifier("missiontag", Identifier.Empty);
            LevelType = element.GetAttributeEnum("leveltype", LevelData.LevelType.LocationConnection);
            MinReputation = element.GetAttributeFloat("minreputation", 0.0f);
            MaxReputation = element.GetAttributeFloat("maxreputation", 0.0f);
            if (MinReputation > MaxReputation)
            {
                DebugConsole.ThrowError($"Error in faction prefab \"{parentDebugName}\": MinReputation cannot be larger than MaxReputation.");
            }
            float probability = element.GetAttributeFloat("probability", 0.0f);
            MinProbability = element.GetAttributeFloat("minprobability", probability);
            MaxProbability = element.GetAttributeFloat("maxprobability", probability);
            MaxDistanceFromFactionOutpost = element.GetAttributeInt(nameof(MaxDistanceFromFactionOutpost), int.MaxValue);
            MaxDistanceFromCurrentLocation = element.GetAttributeInt(nameof(MaxDistanceFromCurrentLocation), 0);
            DisallowBetweenOtherFactionOutposts = element.GetAttributeBool(nameof(DisallowBetweenOtherFactionOutposts), false);
        }

        public static List<Mission> CreateForLevel(CampaignMode campaignMode, Location location)
        {
            return CreateForLevel(campaignMode, (Either<Location, LocationConnection>)location);
        }

        public static List<Mission> CreateForLevel(CampaignMode campaignMode, LocationConnection locationConnection)
        {
            return CreateForLevel(campaignMode, (Either<Location, LocationConnection>)locationConnection);
        }

        public static void CreateAcrossMap(CampaignMode campaignMode, Map map)
        {
            foreach (Faction faction in campaignMode.Factions.OrderBy(f => f.Prefab.MenuOrder))
            {
                //only handle missions taking place somewhere further away, missions that should trigger in the current level are handled in CreateForLevel
                var shuffledMissions = faction.Prefab.AutomaticMissions.Where(m => m.MaxDistanceFromCurrentLocation > 0).ToList();
                //shuffle the missions, so we don't always tend to choose the missions defined first when missions are mutually exclusive
                shuffledMissions.Shuffle(Rand.RandSync.Unsynced);

                foreach (var automaticMission in shuffledMissions)
                {
                    if (!automaticMission.IsValidForFaction(faction)) { continue; }

                    foreach (var location in map.Locations)
                    {
                        if (location == campaignMode.CurrentLocation || !location.Discovered) { continue; }
                        if (!automaticMission.IsValidForLocation(campaignMode, location, faction)) { continue; }
                        Mission? newMission = automaticMission.TryCreateRandomMission(campaignMode, location.LevelData, faction, location);
                        if (newMission != null)
                        {
                            location.AddMission(newMission);
                        }
                    }

                    foreach (var locationConnection in map.Connections)
                    {
                        if (locationConnection.Locations.None(l => l.Discovered)) { continue; }
                        if (!automaticMission.IsValidForLocationConnection(campaignMode, locationConnection, faction)) { continue; }
                        Mission? newMission = automaticMission.TryCreateRandomMission(campaignMode, locationConnection.LevelData, faction, locationConnection);
                        if (newMission != null)
                        {
                            newMission.Locations[0].AddMission(newMission);
                        }
                    }
                }
            }
        }

        private bool IsValidForFaction(Faction faction)
        {
            return faction.Reputation.Value >= MinReputation && faction.Reputation.Value <= MaxReputation;
        }

        private bool IsValidForLocation(CampaignMode campaignMode, Location location, Faction faction)
        {
            if (LevelType != LevelData.LevelType.Outpost) { return false; }

            if (MaxDistanceFromFactionOutpost < int.MaxValue)
            {
                if (!Map.LocationOrConnectionWithinDistance(location, MaxDistanceFromFactionOutpost, loc => loc.Faction == faction))
                {
                    return false;
                }
            }

            int distanceToCurrentLocation = Map.GetDistanceToClosestLocationOrConnection(campaignMode.CurrentLocation, MaxDistanceFromCurrentLocation,
                (Location l) => l == location);
            if (distanceToCurrentLocation > MaxDistanceFromCurrentLocation) { return false; }

            return true;
        }

        private bool IsValidForLocationConnection(CampaignMode campaignMode, LocationConnection locationConnection, Faction faction)
        {
            if (LevelType != LevelData.LevelType.LocationConnection) { return false; }

            if (DisallowBetweenOtherFactionOutposts)
            {
                if (locationConnection.Locations.Any(l => l.Faction != faction))
                {
                    return false;
                }
            }
            if (MaxDistanceFromFactionOutpost < int.MaxValue)
            {
                if (!Map.LocationOrConnectionWithinDistance(locationConnection.Locations[0], MaxDistanceFromFactionOutpost, loc => loc.Faction == faction) &&
                    !Map.LocationOrConnectionWithinDistance(locationConnection.Locations[1], MaxDistanceFromFactionOutpost, loc => loc.Faction == faction))
                {
                    return false;
                }
            }

            if (MaxDistanceFromCurrentLocation <= 0)
            {
                if (campaignMode.CurrentLocationConnection != locationConnection) { return false; }
            }

            int distanceToCurrentLocation = Map.GetDistanceToClosestLocationOrConnection(campaignMode.CurrentLocation, MaxDistanceFromCurrentLocation,
                criteria: (Location l) => true,
                connectionCriteria: (LocationConnection lc) => lc == locationConnection);
            if (distanceToCurrentLocation > MaxDistanceFromCurrentLocation) { return false; }
            
            return true;
        }
        private static List<Mission> CreateForLevel(CampaignMode campaignMode, Either<Location, LocationConnection> locationOrConnection)
        {
            LevelData levelData;
            Location? location = null;
            LocationConnection? locationConnection = null;
            if (locationOrConnection.TryGet(out location))
            {
                levelData = location.LevelData;
            }
            else if (locationOrConnection.TryGet(out locationConnection))
            {
                levelData = locationConnection.LevelData;
            }
            else
            {
                throw new ArgumentException($"Invalid {locationOrConnection}.");
            }

            Random rand = new MTRandom(ToolBox.StringToInt(levelData.Seed + campaignMode.TotalPassedLevels));

            List<Mission> extraMissions = [];
            foreach (Faction faction in campaignMode.Factions.OrderBy(f => f.Prefab.MenuOrder))
            {
                //only choose missions that take place in the current location/level here, missions triggering elsewhere are handled in CreateAcrossMap
                var shuffledMissions = faction.Prefab.AutomaticMissions.Where(m => m.MaxDistanceFromCurrentLocation == 0).ToList();
                //shuffle the missions, so we don't always tend to choose the missions defined first when missions are mutually exclusive
                shuffledMissions.Shuffle(rand);

                foreach (var automaticMission in shuffledMissions)
                {
                    if (!automaticMission.IsValidForFaction(faction)) { continue; }
                    if (location != null && !automaticMission.IsValidForLocation(campaignMode, location, faction)) { continue; }
                    if (locationConnection != null && !automaticMission.IsValidForLocationConnection(campaignMode, locationConnection, faction)) { continue; }
                    Mission? newMission = automaticMission.TryCreateRandomMission(campaignMode, levelData, faction, locationOrConnection, alreadyCreatedMissions: extraMissions);
                    if (newMission != null)
                    {
                        extraMissions.Add(newMission);
                    }
                }
            }
            return extraMissions;
        }

        private Mission? TryCreateRandomMission(CampaignMode campaignMode, LevelData levelData, Faction faction, Either<Location, LocationConnection> locationOrConnection,
            List<Mission>? alreadyCreatedMissions = null)
        {
            Random rand = new MTRandom(ToolBox.StringToInt(levelData.Seed + campaignMode.TotalPassedLevels));

            float probability =
                MathHelper.Lerp(
                    MinProbability,
                    MaxProbability,
                    MathUtils.InverseLerp(MinReputation, MaxReputation, faction.Reputation.Value));
            if (rand.NextDouble() > probability) { return null; }
            
            var missionPrefabs = MissionPrefab.Prefabs.Where(m => m.Tags.Contains(MissionTag)).OrderBy(m => m.UintIdentifier);
            if (missionPrefabs.Any())
            {
                var missionPrefab = ToolBox.SelectWeightedRandom(missionPrefabs, p => p.Commonness, rand);
                if (missionPrefab.DisallowMissionsOfSameType &&
                    alreadyCreatedMissions != null && alreadyCreatedMissions.Any(m => m.Prefab.Type == missionPrefab.Type))
                {
                    return null;
                }

                if (locationOrConnection.TryGet(out Location location))
                {
                    if (missionPrefab.DisallowMissionsOfSameType &&
                        location.AvailableMissions.Any(m => m.Prefab.Type == missionPrefab.Type))
                    {
                        return null;
                    }
                    return missionPrefab.Instantiate([location, location], Submarine.MainSub);
                }
                else if (locationOrConnection.TryGet(out LocationConnection locationConnection))
                {
                    if (missionPrefab.DisallowMissionsOfSameType &&
                        locationConnection.AvailableMissions.Any(m => m.Prefab.Type == missionPrefab.Type))
                    {
                        return null;
                    }

                    var locations = locationConnection.Locations.ToArray();

                    bool missionSuitableForFirstLocation = (faction == null || locations[0].Faction == faction) && missionPrefab.IsAllowed(locations[0], locations[1]);
                    bool missionSuitableForSecondLocation = (faction == null || locations[1].Faction == faction) && missionPrefab.IsAllowed(locations[1], locations[0]);

                    //if the connection is "the wrong way around" (2nd location belongs to the faction that triggers this mission or if the 2nd location has been explicitly set as the origin),
                    //flip the locations around because missions treat the 1st location as the "origin"
                    if (!missionSuitableForFirstLocation && missionSuitableForSecondLocation)
                    {
                        (locations[0], locations[1]) = (locations[1], locations[0]);
                    }
                    else if (!missionSuitableForFirstLocation && !missionSuitableForSecondLocation)
                    {
                        return null;
                    } 
                    //mission suitable for either location: choose the direction randomly
                    else if (missionSuitableForFirstLocation && missionSuitableForSecondLocation && rand.NextDouble() < 0.5f)
                    {
                        (locations[0], locations[1]) = (locations[1], locations[0]);
                    }
                    return missionPrefab.Instantiate(locations, Submarine.MainSub);
                }
            }
            return null;
        }
    }
}