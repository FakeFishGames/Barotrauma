using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class LocationTypeChange
    {
        public readonly string ChangeToType;

        public readonly bool RequireDiscovered;

        public List<string> Messages = new List<string>();

        /// <summary>
        /// The change can only happen if there's at least one of the given types of locations near this one
        /// </summary>
        public readonly List<string> RequiredLocations;

        /// <summary>
        /// How close the location needs to be to one of the RequiredLocations for the change to occur
        /// </summary>
        public readonly int RequiredProximity;

        /// <summary>
        /// Base probability per turn for the location to change if near one of the RequiredLocations
        /// </summary>
        public readonly float Probability;

        /// <summary>
        /// How close the location needs to be to one of the RequiredLocations for the probability to increase
        /// </summary>
        public readonly int RequiredProximityForProbabilityIncrease;

        /// <summary>
        /// How much the probability increases per turn if within RequiredProximityForProbabilityIncrease steps of RequiredLocations
        /// </summary>
        public readonly float ProximityProbabilityIncrease;

        /// <summary>
        /// The change can't happen if there's one or more of the given types of locations near this one
        /// </summary>
        public readonly List<string> DisallowedAdjacentLocations;

        /// <summary>
        /// How close the location needs to be to one of the DisallowedAdjacentLocations for the change to be disabled
        /// </summary>
        public readonly int DisallowedProximity;

        public readonly Point RequiredDurationRange;

        public LocationTypeChange(string currentType, XElement element)
        {
            ChangeToType = element.GetAttributeString("type", "");
            Probability = element.GetAttributeFloat("probability", 1.0f);

            RequireDiscovered = element.GetAttributeBool("requirediscovered", false);

            RequiredLocations = element.GetAttributeStringArray("requiredlocations", element.GetAttributeStringArray("requiredadjacentlocations", new string[0])).ToList();
            RequiredProximity = Math.Max(element.GetAttributeInt("requiredproximity", 1), 1);
            ProximityProbabilityIncrease = element.GetAttributeFloat("proximityprobabilityincrease", 0.0f);
            RequiredProximityForProbabilityIncrease = element.GetAttributeInt("requiredproximityforprobabilityincrease", -1);


            if (RequiredProximityForProbabilityIncrease > 0 || ProximityProbabilityIncrease > 0.0f)
            {
                if (!RequiredLocations.Any())
                {
                    DebugConsole.AddWarning(
                        $"Invalid location type change in location type \"{currentType}\". "+
                        "Probability is configured to increase when near some other type of location, but the RequiredLocations attribute is not set.");
                }
                if (Probability >= 1.0f)
                {
                    DebugConsole.AddWarning(
                        $"Invalid location type change in location type \"{currentType}\". " +
                        "Probability is configured to increase when near some other type of location, but the base probability is already 100%");
                }
            }

            DisallowedAdjacentLocations = element.GetAttributeStringArray("disallowedadjacentlocations", new string[0]).ToList();
            DisallowedProximity = Math.Max(element.GetAttributeInt("disallowedproximity", 1), 1);

            RequiredDurationRange = element.GetAttributePoint("requireddurationrange", Point.Zero);
            //backwards compatibility
            if (element.Attribute("requiredduration") != null)
            {
                RequiredDurationRange = new Point(element.GetAttributeInt("requiredduration", 0));
            }

            string messageTag = element.GetAttributeString("messagetag", "LocationChange." + currentType + ".ChangeTo." + ChangeToType);

            Messages = TextManager.GetAll(messageTag);
            if (Messages == null)
            {
                DebugConsole.ThrowError("No messages defined for the location type change " + currentType + " -> " + ChangeToType);
            }
        }

        public float DetermineProbability(Location location)
        {
            if (RequireDiscovered && !location.Discovered) { return 0.0f; }
         
            if (RequiredLocations.Any() && !AnyWithinDistance(location, RequiredProximity, (otherLocation) => { return RequiredLocations.Contains(otherLocation.Type.Identifier); })) 
            {
                return 0.0f;
            }
            if (DisallowedAdjacentLocations.Any() && AnyWithinDistance(location, DisallowedProximity, (otherLocation) => { return DisallowedAdjacentLocations.Contains(otherLocation.Type.Identifier); }))
            {
                return 0.0f;
            }
            float probability = Probability;
            if (location.ProximityTimer.ContainsKey(this))
            {
                if (AnyWithinDistance(location, RequiredProximityForProbabilityIncrease, (otherLocation) => { return RequiredLocations.Contains(otherLocation.Type.Identifier); }))
                {
                    return probability += ProximityProbabilityIncrease * location.ProximityTimer[this];
                }
            }
            return probability;
        }

        public bool AnyWithinDistance(Location location, int maxDistance, Func<Location, bool> predicate, int currentDistance = 0, HashSet<Location> checkedLocations = null)
        {
            if (currentDistance > maxDistance) { return false; }
            if (currentDistance > 0 && predicate(location)) { return true; }

            checkedLocations ??= new HashSet<Location>();
            checkedLocations.Add(location);

            foreach (var connection in location.Connections)
            {
                var otherLocation = connection.OtherLocation(location);
                if (!checkedLocations.Contains(otherLocation)) 
                {
                    if (AnyWithinDistance(otherLocation, maxDistance, predicate, currentDistance + 1, checkedLocations)) { return true; }
                }
            }

            return false;
        }

        private int CountWithinRequiredProximity(Location location, int currentDistance = 0, HashSet<Location> checkedLocations = null)
        {
            if (currentDistance > RequiredProximityForProbabilityIncrease) { return 0; }
            int count = currentDistance > 0 && RequiredLocations.Contains(location.Type.Identifier) ? 1 : 0;

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
