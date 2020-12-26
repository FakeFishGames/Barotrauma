using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class LocationTypeChange
    {
        public readonly string ChangeToType;

        public readonly float Probability;
        public readonly int RequiredDuration;

        public readonly float ProximityProbabilityIncrease;
        public readonly int RequiredProximityForProbabilityIncrease;

        public readonly bool RequireDiscovered;

        public List<string> Messages = new List<string>();

        //the change can't happen if there's a location of the given type next to this one
        public readonly List<string> DisallowedAdjacentLocations;

        //the change can only happen if there's at least one of the given types of locations next to this one
        public readonly List<string> RequiredAdjacentLocations;

        public LocationTypeChange(string currentType, XElement element)
        {
            ChangeToType = element.GetAttributeString("type", "");
            Probability = element.GetAttributeFloat("probability", 1.0f);
            RequiredDuration = element.GetAttributeInt("requiredduration", 0);

            RequireDiscovered = element.GetAttributeBool("requirediscovered", false);

            ProximityProbabilityIncrease = element.GetAttributeFloat("proximityprobabilityincrease", 0.0f);
            RequiredProximityForProbabilityIncrease = element.GetAttributeInt("requiredproximityforprobabilityincrease", -1);

            DisallowedAdjacentLocations = element.GetAttributeStringArray("disallowedadjacentlocations", new string[0]).ToList();
            RequiredAdjacentLocations = element.GetAttributeStringArray("requiredadjacentlocations", new string[0]).ToList();

            string messageTag = element.GetAttributeString("messagetag", "LocationChange." + currentType + ".ChangeTo." + ChangeToType);

            Messages = TextManager.GetAll(messageTag);
            if (Messages == null)
            {
                DebugConsole.ThrowError("No messages defined for the location type change " + currentType + " -> " + ChangeToType);
            }
        }

        public float DetermineProbability(Location location)
        {
            float totalProbability = Probability;
            if (AnyWithinRequiredProximity(location)) { totalProbability += ProximityProbabilityIncrease; }
            return totalProbability;
        }

        private bool AnyWithinRequiredProximity(Location location, int currentDistance = 0, HashSet<Location> checkedLocations = null)
        {
            if (currentDistance > RequiredProximityForProbabilityIncrease) { return false; }
            if (currentDistance > 0 && RequiredAdjacentLocations.Contains(location.Type.Identifier)) { return true; }

            checkedLocations ??= new HashSet<Location>();
            checkedLocations.Add(location);

            foreach (var connection in location.Connections)
            {
                var otherLocation = connection.OtherLocation(location);
                if (!checkedLocations.Contains(otherLocation)) 
                {
                    if (AnyWithinRequiredProximity(otherLocation, currentDistance + 1, checkedLocations)) { return true; }
                }
            }

            return false;
        }

        private int CountWithinRequiredProximity(Location location, int currentDistance = 0, HashSet<Location> checkedLocations = null)
        {
            if (currentDistance > RequiredProximityForProbabilityIncrease) { return 0; }
            int count = currentDistance > 0 && RequiredAdjacentLocations.Contains(location.Type.Identifier) ? 1 : 0;

            checkedLocations ??= new HashSet<Location>();
            checkedLocations.Add(location);

            foreach (var connection in location.Connections)
            {
                var otherLocation = connection.OtherLocation(location);
                if (!checkedLocations.Contains(otherLocation)) { count += CountWithinRequiredProximity(otherLocation, currentDistance+1, checkedLocations); }
            }

            return count;
        }
    }
}
