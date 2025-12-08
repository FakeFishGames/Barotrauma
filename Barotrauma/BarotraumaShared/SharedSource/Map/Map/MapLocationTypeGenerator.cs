#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal class MapLocationTypeGenerator
    {
        internal class LocationTypeCount
        {
            public int AmountToAssign;
            public int? DifficultyZone;
            public Identifier? BiomeId;

            public LocationTypeCount(int amountToAssign, int difficultyZone)
            {
                AmountToAssign = amountToAssign;
                DifficultyZone = difficultyZone;
            }

            public LocationTypeCount(int amountToAssign, Identifier biomeId)
            {
                AmountToAssign = amountToAssign;
                BiomeId = biomeId;
            }

            public string ToDebugString()
            {
                if (DifficultyZone.HasValue)
                {
                    return $"x{AmountToAssign} in (zone {DifficultyZone.Value})";
                }
                else if (BiomeId.HasValue)
                {
                    return $"x{AmountToAssign} in (biome {BiomeId.Value})";
                }
                return $"x{AmountToAssign}";
            }                
        }

        /// <summary>
        /// Actual amounts of location types that need to be assigned to locations, after resolving random variation from min/max counts.
        /// </summary>
        private Dictionary<LocationType, List<LocationTypeCount>> locationTypeAmountsToAssign;
        private readonly Map map;

        private readonly CampaignMode campaign;
        /// <summary>
        /// List of locations that have been filled from specific requirements and should not be overwritten by some subsequent generation pass (e.g. the start outpost).
        /// </summary>
        private readonly List<Location> filledLocations;
        private readonly Dictionary<int, List<Location>> locationsPerZone = new Dictionary<int, List<Location>>();

        private readonly IOrderedEnumerable<LocationType> orderedLocationTypes = LocationType.Prefabs.GetOrdered();
        
        private bool IsEveryLocationTypeAssigned
        {
            get 
            { 
                return locationTypeAmountsToAssign.SelectMany(kvp => kvp.Value)
                                               .None(locationTypeCount => locationTypeCount.AmountToAssign > 0); 
            }
        }
        
        public MapLocationTypeGenerator(CampaignMode campaign, Map map)
        {
            filledLocations = new List<Location>();
            this.map = map;
            this.campaign = campaign;
            locationTypeAmountsToAssign = new Dictionary<LocationType, List<LocationTypeCount>>();
            GenerateTotalAmountsToAssign();
        }

        private void GenerateTotalAmountsToAssign()
        {            
            foreach (var locationTypePrefab in orderedLocationTypes)
            {
                foreach (var areaSetting in locationTypePrefab.AreaSettings)
                {
                    if (!areaSetting.HasCounts) { continue; }

                    if (!areaSetting.HasValidData)
                    {
                        // the only case for invalid data right now is the biome id
                        DebugConsole.AddWarning($"Biome ID is invalid for AreaSetting in locationType '{locationTypePrefab.Identifier}'. Skipping invalid setting.", locationTypePrefab.ContentPackage);
                        continue;
                    }
                    
                    int amountToAdd = Rand.GetRNG(Rand.RandSync.ServerAndClient).Next(areaSetting.MinCount ?? 0, areaSetting.MaxCount ?? 0 + 1);
                    if (amountToAdd <= 0) { continue; }
                    
                    // data is either for a biome or a zone, but not both
                    var existingCount = GetExistingCount(locationTypePrefab, areaSetting);
                    
                    if (existingCount != null)
                    {
                        existingCount.AmountToAssign += amountToAdd;
                    }
                    else
                    {
                        if (!locationTypeAmountsToAssign.ContainsKey(locationTypePrefab))
                        {
                            locationTypeAmountsToAssign[locationTypePrefab] = new List<LocationTypeCount>();
                        }
                        locationTypeAmountsToAssign[locationTypePrefab].Add(CreateNewCount(areaSetting, amountToAdd));
                    }
                }
            }

            LocationTypeCount CreateNewCount(LocationType.AreaSettingData areaSettingData, int amountToAdd)
            {
                if (areaSettingData is LocationType.BiomeSettingData biomeSettingData)
                {
                    return new LocationTypeCount(amountToAdd, biomeSettingData.BiomeIdentifier);
                }
                else if (areaSettingData is LocationType.DifficultyZoneSettingData difficultyZoneSettingData)
                {
                    return new LocationTypeCount(amountToAdd, difficultyZoneSettingData.DifficultyZone);
                }
                else
                {
                    throw new ArgumentException("Unrecognized areaSettingData");
                }
            }
            
            LocationTypeCount? GetExistingCount(LocationType locationTypePrefab, LocationType.AreaSettingData areaSettingData)
            {
                if (!locationTypeAmountsToAssign.TryGetValue(locationTypePrefab, out List<LocationTypeCount>? value)) { return null; }                
                return value.FirstOrDefault(areaSettingData.MatchesRemainingCount);
            }
        }
        
        public void AddToLocationsPerZone(int zone, Location location)
        {
            if (!locationsPerZone.ContainsKey(zone))
            {
                locationsPerZone[zone] = new List<Location>();
            }
            locationsPerZone[zone].Add(location);
        }
        
        public void AddToFilled(Location location)
        {
            if (filledLocations.Contains(location)) { return; }
            filledLocations.Add(location);
        }
        
        public bool IsFilled(Location location)
        {
            return filledLocations.Contains(location);
        }
        
        public void ChangeLocationTypeAndName(CampaignMode campaign, Location location, LocationType suitableLocationType)
        {
            location.ChangeType(campaign, suitableLocationType, createStores: false, unlockInitialMissions: false);
            if (!suitableLocationType.ForceLocationName.IsEmpty)
            {
                location.ForceName(suitableLocationType.ForceLocationName);
            }
            else
            {
                location.AssignRandomName(location.Type, Rand.GetRNG(Rand.RandSync.ServerAndClient), existingLocations: map.Locations);
            }
        }
        
        public void AssignForcedBiomeGateTypes(IEnumerable<Location> gateLocations)
        {
            foreach (Location gateLocation in gateLocations)
            {
                foreach (LocationType locationType in orderedLocationTypes)
                {
                    if (locationType.BiomeGate != LocationType.BiomeGateSetting.Force) { continue; }
                
                    int zone = map.GetZoneIndex(gateLocation.MapPosition.X);
                    
                    // if there are no counts left for this location type, skip it
                    if (locationType.HasCounts() && !TypeHasRemainingCountForLocation(locationType, gateLocation)) { continue; }
                    
                    // wrong faction, can't place here
                    if (!locationType.Faction.IsEmpty && locationType.Faction != gateLocation.Faction?.Prefab.Identifier) 
                    { 
                        continue; 
                    }
                    
                    // if the location already happens to be of the type we want to assign, skip and remove from totals
                    if (gateLocation.Type == locationType)
                    {
                        AddToFilled(gateLocation);
                        RemoveOneFromTotals(locationType, gateLocation);
                        break;
                    }

                    if (!IsFilled(gateLocation) && 
                        locationType.IsValidForZoneOrBiome(zone, gateLocation.Biome.Identifier))
                    {
                        AddToFilled(gateLocation);
                        ChangeLocationTypeAndName(campaign, gateLocation, locationType);
                        RemoveOneFromTotals(locationType, gateLocation);
                        break;
                    }
                }
            }
        }

        private bool TypeHasRemainingCountForLocation(LocationType countLocationType, Location location)
        {
            if (!locationTypeAmountsToAssign.TryGetValue(countLocationType, out List<LocationTypeCount>? locationTypeCounts))
            {
                return false;
            }
            
            bool hasZoneCount = locationTypeCounts.Any(ltc => ltc.DifficultyZone == map.GetZoneIndex(location.MapPosition.X) && ltc.AmountToAssign > 0);
            bool hasBiomeCount = locationTypeCounts.Any(ltc => ltc.BiomeId == location.Biome.Identifier && ltc.AmountToAssign > 0);

            return hasZoneCount || hasBiomeCount;
        }
        
        private int GetRemainingCount(LocationType locationType, LocationType.AreaSettingData areaSetting)
        {
            locationTypeAmountsToAssign.TryGetValue(locationType, out List<LocationTypeCount>? locationTypeCounts);
            if (locationTypeCounts == null || locationTypeCounts.None()) { return 0; }
            
            var match = locationTypeCounts.FirstOrDefault(ltc => areaSetting.MatchesRemainingCount(ltc));
            return match?.AmountToAssign ?? 0;
        }

        public void RemoveOneFromTotals(LocationType locationType, Location location)
        {
            if (!locationTypeAmountsToAssign.TryGetValue(locationType, out List<LocationTypeCount>? locationTypeCounts))
            {
                return;
            }
            
            var zoneMatch = locationTypeCounts.FirstOrDefault(ltc => ltc.AmountToAssign > 0 && ltc.DifficultyZone == map.GetZoneIndex(location.MapPosition.X));
            if (zoneMatch != null)
            {
                zoneMatch.AmountToAssign--;
            }
            var biomeMatch = locationTypeCounts.FirstOrDefault(ltc => ltc.AmountToAssign > 0 && ltc.BiomeId == location.Biome.Identifier);
            if (biomeMatch != null)
            {
                biomeMatch.AmountToAssign--;
            }
        }
        
        /// <summary>
        /// Assign the location types that should be placed in some specific part of a biome/zone <see cref="LocationType.AreaSettingData.DesiredPosition"/>.
        /// </summary>
        public void AssignLocationTypesBasedOnDesiredPosition(IEnumerable<Location> gateLocations)
        {
            foreach (LocationType locationType in LocationType.Prefabs)
            {
                foreach (var areaSetting in locationType.AreaSettings)
                {
                    if (!areaSetting.DesiredPosition.HasValue) { continue; }

                    int remainingCount = GetRemainingCount(locationType, areaSetting);
                    if (remainingCount == 0) { continue; }

                    var locations = map.Locations.Where(location => areaSetting.MatchesLocation(map, location)).Where(location => !gateLocations.Contains(location));
                    if (locations.None()) { continue; }
                    
                    FillLocations(locations, areaSetting.DesiredPosition.Value, remainingCount, locationType);
                }
            }

            void FillLocations(IEnumerable<Location> locations, float desiredPosition, int locationCount, LocationType locationType)
            {
                float areaStart = locations.Min(l => l.MapPosition.X);
                float areaEnd = locations.Max(l => l.MapPosition.X);

                float desiredMapPosition = MathHelper.Lerp(areaStart, areaEnd, desiredPosition);

                List<Location> sortedLocations = locations.Where(location => !IsFilled(location)).ToList();
                sortedLocations.Sort((firstLocation, secondLocation) => Math.Abs(firstLocation.MapPosition.X - desiredMapPosition).CompareTo(secondLocation.MapPosition.X - desiredMapPosition));

                for (int i = 0; i < locationCount; i++)
                {
                    if (sortedLocations.None()) { break; }
                    var closestLocation = sortedLocations.First();
                    ChangeLocationTypeAndName(campaign, closestLocation, locationType);
                    AddToFilled(closestLocation);
                    RemoveOneFromTotals(closestLocation.Type, closestLocation);
                    sortedLocations.Remove(closestLocation);
                }
            }
        }
        
        public void AssignLocationTypesBasedOnCount(IEnumerable<Location> gateLocations, IEnumerable<Location> locations)
        {
            // generate lists of all the instances of location types that we are supposed to try and fit into the available locations
            List<Location> shuffledLocations = locations.ToList();
            shuffledLocations.Shuffle(Rand.GetRNG(Rand.RandSync.ServerAndClient));

            foreach (Location location in shuffledLocations)
            {
                if (IsFilled(location)) { continue; }

                bool isBiomeGate = gateLocations.Contains(location);
                if (isBiomeGate && location.Type.BiomeGate == LocationType.BiomeGateSetting.Force)
                {
                    //forced as a biome gate, let's not touch this location
                    continue;
                }
                if (IsEveryLocationTypeAssigned) { break; }

                var suitableLocationType = TryPickSuitableLocationTypeFromTotals(location, isBiomeGate);

                // if we found something suitable, change the location type and name, and add to filled locations
                // (otherwise we will honor the initial random location type)
                if (suitableLocationType != null)
                {
                    ChangeLocationTypeAndName(campaign, location, suitableLocationType);
                    AddToFilled(location);
                }
            }

            // warn if we couldn't fill in all the desired counts
            if (!IsEveryLocationTypeAssigned)
            {
                ContentPackage? nonVanillaContentPackage = null;
                StringBuilder sb = new StringBuilder("Following location types could not be assigned to locations:\n");
                foreach ((LocationType locationType, List<LocationTypeCount> locationTypeCounts) in locationTypeAmountsToAssign)
                {
                    foreach (var locationTypeCount in locationTypeCounts)
                    {
                        if (locationTypeCount.AmountToAssign > 0)
                        {
                            if (locationType.ContentPackage != ContentPackageManager.VanillaCorePackage)
                            {
                                nonVanillaContentPackage = locationType.ContentPackage;
                            }
                            sb.AppendLine($"- {locationType.Identifier} - {locationType.Name} ({locationTypeCount.ToDebugString()})");
                        }
                    }
                }
                DebugConsole.AddWarning(sb.ToString(), 
                    //blame the mod where one of the problematic location types is defined in
                    contentPackage: nonVanillaContentPackage);
            }
        }
        
        private LocationType? TryPickSuitableLocationTypeFromTotals(Location location, bool isBiomeGate)
        {
            int locationZone = map.GetZoneIndex(location.MapPosition.X);
            Identifier locationBiomeId = location.Biome.Identifier;
            LocationType? suitableLocationType = null;

            //find location type counts that haven't been fully assigned yet
            List<(LocationType LocationType, LocationTypeCount Count)> potentialLocationTypeCounts = [];
            foreach ((LocationType locationType, List<LocationTypeCount> countList) in locationTypeAmountsToAssign)
            {
                //if we're picking a potential new type for a biome gate, it must be a location type that's allowed as a biome gate
                if (isBiomeGate && locationType.BiomeGate == LocationType.BiomeGateSetting.Deny) { continue; }
                foreach (var locationTypeCount in countList)
                {
                    if (locationTypeCount.AmountToAssign > 0)
                    {
                        potentialLocationTypeCounts.Add((locationType, locationTypeCount));
                    }
                }
            }

            var zoneMatches = potentialLocationTypeCounts.Where(locationTypeCount => locationTypeCount.Count.DifficultyZone == locationZone);
            var biomeMatches = potentialLocationTypeCounts.Where(locationTypeCount => locationTypeCount.Count.BiomeId == locationBiomeId);            
            if (zoneMatches.None() && biomeMatches.None())
            {
                return null;
            }
            
            // if both lists have something, we will try to find a location type that is in both lists
            if (zoneMatches.Any() && biomeMatches.Any())
            {
                var dualMatch = zoneMatches.FirstOrDefault(zoneCount => biomeMatches.Any(biomeCount => biomeCount.LocationType == zoneCount.LocationType));
                if (dualMatch.LocationType != null)
                {
                    suitableLocationType = dualMatch.LocationType;
                }
            }

            // no dual match, find individual match
            if (suitableLocationType == null)
            {
                suitableLocationType = zoneMatches.Any() ? 
                    zoneMatches.First().LocationType : biomeMatches.First().LocationType;
            }
            
            if (suitableLocationType != null)
            {
                RemoveOneFromTotals(suitableLocationType, location);
            }

            return suitableLocationType;
        }
    }
}