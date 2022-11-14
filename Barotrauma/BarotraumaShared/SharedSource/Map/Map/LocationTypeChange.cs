using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class LocationTypeChange
    {
        public class Requirement
        {
            public enum FunctionType
            {
                Add,
                Multiply
            }

            public readonly FunctionType Function;

            /// <summary>
            /// The change can only happen if there's at least one of the given types of locations near this one
            /// </summary>
            public readonly ImmutableArray<Identifier> RequiredLocations;

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
            /// Does there need to be a beacon station within RequiredProximity
            /// </summary>
            public readonly bool RequireBeaconStation;

            /// <summary>
            /// Does there need to be hunting grounds within RequiredProximity
            /// </summary>
            public readonly bool RequireHuntingGrounds;

            public Requirement(XElement element, LocationTypeChange change)
            {
                RequiredLocations = element.GetAttributeIdentifierArray("requiredlocations", element.GetAttributeIdentifierArray("requiredadjacentlocations", Array.Empty<Identifier>())).ToImmutableArray();
                RequiredProximity = Math.Max(element.GetAttributeInt("requiredproximity", 1), 1);
                ProximityProbabilityIncrease = element.GetAttributeFloat("proximityprobabilityincrease", 0.0f);
                RequiredProximityForProbabilityIncrease = element.GetAttributeInt("requiredproximityforprobabilityincrease", -1);
                RequireBeaconStation = element.GetAttributeBool("requirebeaconstation", false);
                RequireHuntingGrounds = element.GetAttributeBool("requirehuntinggrounds", false);

                string functionStr = element.GetAttributeString("function", "Add");
                if (!Enum.TryParse(functionStr, ignoreCase: true, out Function))
                {
                    DebugConsole.ThrowError(
                        $"Invalid location type change in location type \"{change.CurrentType}\". " +
                        $"\"{functionStr}\" is not a valid function.");
                }

                Probability = element.GetAttributeFloat("probability", 1.0f);

                if (RequiredProximityForProbabilityIncrease > 0 || ProximityProbabilityIncrease > 0.0f)
                {
                    if (!RequiredLocations.Any() && !RequireBeaconStation && !RequireHuntingGrounds)
                    {
                        DebugConsole.AddWarning(
                            $"Invalid location type change in location type \"{change.CurrentType}\". " +
                            "Probability is configured to increase when near some other type of location, but the RequiredLocations attribute is not set.");
                    }
                    if (Probability >= 1.0f)
                    {
                        DebugConsole.AddWarning(
                            $"Invalid location type change in location type \"{change.CurrentType}\". " +
                            "Probability is configured to increase when near some other type of location, but the base probability is already 100%");
                    }
                }
            }

            public bool MatchesLocation(Location location)
            {
                return RequiredLocations.Contains(location.Type.Identifier) && !location.IsCriticallyRadiated();
            }

            public bool AnyWithinDistance(Location location, int maxDistance, int currentDistance = 0, HashSet<Location> checkedLocations = null)
            {
                if (currentDistance > maxDistance) { return false; }
                if (currentDistance > 0 && MatchesLocation(location)) { return true; }

                checkedLocations ??= new HashSet<Location>();
                checkedLocations.Add(location);

                foreach (var connection in location.Connections)
                {
                    if (RequireBeaconStation && connection.LevelData.HasBeaconStation && connection.LevelData.IsBeaconActive)
                    {
                        return true;
                    }
                    if (RequireHuntingGrounds && connection.LevelData.HasHuntingGrounds)
                    {
                        return true;
                    }

                    var otherLocation = connection.OtherLocation(location);
                    if (!checkedLocations.Contains(otherLocation))
                    {
                        if (AnyWithinDistance(otherLocation, maxDistance, currentDistance + 1, checkedLocations)) { return true; }
                    }
                }

                return false;
            }
        }

        public readonly Identifier CurrentType;

        public readonly Identifier ChangeToType;

        /// <summary>
        /// Base probability per turn for the location to change if near one of the RequiredLocations
        /// </summary>
        public readonly float Probability;

        public readonly bool RequireDiscovered;

        public List<Requirement> Requirements = new List<Requirement>();

        private readonly bool requireChangeMessages;
        private readonly string messageTag;

        public IReadOnlyList<string> GetMessages(Faction faction)
        {
            if (faction != null && TextManager.ContainsTag(messageTag + "." + faction.Prefab.Identifier))
            {
                return TextManager.GetAll(messageTag + "." + faction.Prefab.Identifier).ToImmutableArray();
            }

            if (TextManager.ContainsTag(messageTag))
            {
                return TextManager.GetAll(messageTag).ToImmutableArray();
            }
            else
            {
                if (requireChangeMessages)
                {
                    DebugConsole.ThrowError($"No messages defined for the location type change {CurrentType} -> {ChangeToType}");
                }
                return Enumerable.Empty<string>().ToImmutableArray();
            }
        }

        /// <summary>
        /// The change can't happen if there's one or more of the given types of locations near this one
        /// </summary>
        public readonly ImmutableArray<Identifier> DisallowedAdjacentLocations;

        /// <summary>
        /// How close the location needs to be to one of the DisallowedAdjacentLocations for the change to be disabled
        /// </summary>
        public readonly int DisallowedProximity;

        /// <summary>
        /// The location can't change it's type for this many turns after this location type changes occurs
        /// </summary>
        public readonly int CooldownAfterChange;

        public readonly Point RequiredDurationRange;

        public LocationTypeChange(Identifier currentType, XElement element, bool requireChangeMessages, float defaultProbability = 0.0f)
        {
            CurrentType = currentType;
            ChangeToType = element.GetAttributeIdentifier("type", element.GetAttributeIdentifier("to", ""));

            RequireDiscovered = element.GetAttributeBool("requirediscovered", false);

            DisallowedAdjacentLocations = element.GetAttributeIdentifierArray("disallowedadjacentlocations", Array.Empty<Identifier>()).ToImmutableArray();
            DisallowedProximity = Math.Max(element.GetAttributeInt("disallowedproximity", 1), 1);

            RequiredDurationRange = element.GetAttributePoint("requireddurationrange", Point.Zero);

            Probability = element.GetAttributeFloat("probability", defaultProbability);

            CooldownAfterChange = Math.Max(element.GetAttributeInt("cooldownafterchange", 0), 0);

            //backwards compatibility
            if (element.Attribute("requiredlocations") != null)
            {
                Requirements.Add(new Requirement(element, this));
            }

            //backwards compatibility
            if (element.Attribute("requiredduration") != null)
            {
                RequiredDurationRange = new Point(element.GetAttributeInt("requiredduration", 0));
            }

            this.requireChangeMessages = requireChangeMessages;
            messageTag = element.GetAttributeString("messagetag", "LocationChange." + currentType + ".ChangeTo." + ChangeToType);

            foreach (var subElement in element.Elements())
            {
                if (subElement.Name.ToString().Equals("requirement", StringComparison.OrdinalIgnoreCase))
                {
                    Requirements.Add(new Requirement(subElement, this));
                }
            }
        }

        public float DetermineProbability(Location location)
        {
            if (RequireDiscovered && !location.Discovered) { return 0.0f; }
            if (location.IsCriticallyRadiated()) { return 0.0f; }
            if (location.LocationTypeChangeCooldown > 0) { return 0.0f; }
            if (location.IsGateBetweenBiomes) { return 0.0f; }
         
            if (DisallowedAdjacentLocations.Any() && 
                AnyWithinDistance(location, DisallowedProximity, (otherLocation) => { return DisallowedAdjacentLocations.Contains(otherLocation.Type.Identifier); }))
            {
                return 0.0f;
            }

            float probability = Probability;
            foreach (Requirement requirement in Requirements)
            {
                if (requirement.AnyWithinDistance(location, requirement.RequiredProximity))
                {
                    if (requirement.Function == Requirement.FunctionType.Add)
                    {
                        probability += requirement.Probability;
                    }
                    else
                    {
                        probability *= requirement.Probability;
                    }
                }

                if (location.ProximityTimer.ContainsKey(requirement))
                {
                    if (requirement.AnyWithinDistance(location, requirement.RequiredProximityForProbabilityIncrease))
                    {
                        if (requirement.Function == Requirement.FunctionType.Add)
                        {
                            probability += requirement.ProximityProbabilityIncrease * location.ProximityTimer[requirement];
                        }
                        else
                        {
                            probability *= requirement.ProximityProbabilityIncrease * location.ProximityTimer[requirement];
                        }
                    }
                }
            }

            return probability;
        }

        private bool AnyWithinDistance(Location location, int maxDistance, Func<Location, bool> predicate, int currentDistance = 0, HashSet<Location> checkedLocations = null)
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
    }
}
