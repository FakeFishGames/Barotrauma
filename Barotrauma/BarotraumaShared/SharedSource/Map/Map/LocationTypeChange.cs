using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    public class LocationTypeChange
    {
        /// <summary>
        /// Can be used to modify the probability of the location type occurring based on some criteria.
        /// </summary>
        public class Modifier
        {
            public enum FunctionType
            {
                Add,
                Multiply
            }

            /// <summary>
            /// The modifier only takes effect if there's at least one of the given types of locations near this one.
            /// </summary>
            public readonly ImmutableArray<Identifier> RequiredLocations;

            /// <summary>
            /// How close the location needs to be to one of the RequiredLocations for the modifier to take effect.
            /// </summary>
            public readonly int RequiredProximity;

            /// <summary>
            /// How much the base probability changes when the conditions of this modifier are met. Can either be added to or multiplied by the base probability of the location type change.
            /// Defaults to 0 if the <see cref="Function"/> is <see cref="FunctionType.Add"/>, and 1 if it's <see cref="FunctionType.Multiply"/> (both meaning no change to the base probability).
            /// </summary>
            public readonly float ProbabilityModifier;

            /// <summary>
            /// How does the <see cref="ProbabilityModifier"/> affect the base probability of the location type change? Defaults to adding to the base probability.
            /// </summary>
            public readonly FunctionType Function;

            /// <summary>
            /// How close the location needs to be to one of the RequiredLocations for the modifier to take effect.
            /// </summary>
            public readonly int RequiredProximityForProbabilityIncrease;

            /// <summary>
            /// How much the probability increases per world step if within RequiredProximityForProbabilityIncrease steps of RequiredLocations.
            /// </summary>
            public readonly float ProximityProbabilityIncrease;

            /// <summary>
            /// Does there need to be a beacon station within RequiredProximity?
            /// </summary>
            public readonly bool RequireBeaconStation;

            /// <summary>
            /// Does there need to be hunting grounds within RequiredProximity?
            /// </summary>
            public readonly bool RequireHuntingGrounds;

            /// <summary>
            /// Minimum population in this location for the modifier to take effect. A percentage of the initial population, between 0-1. Defaults to 0.
            /// </summary>
            public readonly float MinPopulation;

            /// <summary>
            /// Maximum population in this location for the modifier to take effect. A percentage of the initial population, between 0-1. Defaults to 1.
            /// </summary>
            public readonly float MaxPopulation;

            public Modifier(ContentXElement element, LocationTypeChange change, string parentDebugName, bool isRootElement = false)
            {
                RequiredLocations = element.GetAttributeIdentifierArray(nameof(RequiredLocations), 
                    element.GetAttributeIdentifierArray("requiredadjacentlocations", Array.Empty<Identifier>())).ToImmutableArray();
                RequiredProximity = Math.Max(element.GetAttributeInt(nameof(RequiredProximity), 1), 0);
                ProximityProbabilityIncrease = element.GetAttributeFloat(nameof(ProximityProbabilityIncrease), 0.0f);
                RequiredProximityForProbabilityIncrease = element.GetAttributeInt(nameof(RequiredProximityForProbabilityIncrease), -1);
                RequireBeaconStation = element.GetAttributeBool(nameof(RequireBeaconStation), false);
                RequireHuntingGrounds = element.GetAttributeBool(nameof(RequireHuntingGrounds), false);
                MinPopulation = element.GetAttributeFloat(nameof(MinPopulation), 0.0f);
                MaxPopulation = element.GetAttributeFloat(nameof(MaxPopulation), 1.0f);

                if (MinPopulation > MaxPopulation)
                {
                    DebugConsole.ThrowError($"Error in location type change in {parentDebugName}: required minimum population larger than maximum population.",
                        contentPackage: element.ContentPackage);
                }
                if (MinPopulation < 0 || MinPopulation > 1.0f)
                {
                    DebugConsole.ThrowError($"Error in location type change in {parentDebugName}: minimum population set to {MinPopulation}, should be between 0 and 1.",
                        contentPackage: element.ContentPackage);
                }
                if (MaxPopulation < 0 || MaxPopulation > 1.0f)
                {
                    DebugConsole.ThrowError($"Error in location type change in {parentDebugName}: maximum population set to {MaxPopulation}, should be between 0 and 1.",
                        contentPackage: element.ContentPackage);
                }

                string functionStr = element.GetAttributeString("function", "Add");
                if (!Enum.TryParse(functionStr, ignoreCase: true, out Function))
                {
                    DebugConsole.ThrowError(
                        $"Invalid location type change in location type \"{change.CurrentType}\". " +
                        $"\"{functionStr}\" is not a valid function.");
                }

                if (Function == FunctionType.Add)
                {
                    //the base probability of the change and the probabilities in the modifiers are added together,
                    //which is why we default to a probability of 0 here: by default, adding a modifier doesn't increase the probability
                    ProbabilityModifier = element.GetAttributeFloat(0.0f, "probability", nameof(ProbabilityModifier));
                    if (!isRootElement && !HasProbabilityAttribute(element))
                    {
                        DebugConsole.AddWarning(
                            $"Potentially invalid location type change in location type \"{change.CurrentType}\". " +
                            $"{nameof(ProbabilityModifier)} is not set for a modifier with Function=\"Add\", which means the modifier does nothing. " +
                            $"Please specify the {nameof(ProbabilityModifier)} to add. Defaulting to 0.0f (no change to the probability).",
                            element.ContentPackage);
                    }
                }
                else if (Function == FunctionType.Multiply)
                {
                    //default to 1 if multiplying, meaning adding this modifier doesn't change the probability
                    ProbabilityModifier = element.GetAttributeFloat(1.0f, "probability", nameof(ProbabilityModifier));
                    if (!isRootElement && !HasProbabilityAttribute(element))
                    {
                        DebugConsole.AddWarning(
                            $"Potentially invalid location type change in location type \"{change.CurrentType}\". " +
                            $"{nameof(ProbabilityModifier)} is not set for a modifier with Function=\"Multiply\". " +
                            $"Please specify the {nameof(ProbabilityModifier)} to multiply with. Defaulting to 1.0f (no change to the probability).",
                            element.ContentPackage);
                    }
                }

                if (RequiredProximityForProbabilityIncrease > 0 || ProximityProbabilityIncrease > 0.0f)
                {
                    if (!RequiredLocations.Any() && !RequireBeaconStation && !RequireHuntingGrounds)
                    {
                        DebugConsole.AddWarning(
                            $"Invalid location type change in location type \"{change.CurrentType}\". " +
                            "Probability is configured to increase when near some other type of location, but the RequiredLocations attribute is not set.",
                            element.ContentPackage);
                    }
                    if (ProbabilityModifier >= 1.0f)
                    {
                        DebugConsole.AddWarning(
                            $"Invalid location type change in location type \"{change.CurrentType}\". " +
                            "Probability is configured to increase when near some other type of location, but the base probability is already 100%",
                            element.ContentPackage);
                    }
                }
            }

            /// <summary>
            /// Does the element have the probability modifier, either with the current attribute name or legacy?
            /// </summary>
            private static bool HasProbabilityAttribute(ContentXElement element)
            {
                return
                    element.GetAttribute(nameof(ProbabilityModifier)) != null ||
                    element.GetAttribute("probability") != null;
            }

            public bool HasAnyNearbyLocationRequirements()
            {
                return RequireBeaconStation || RequireHuntingGrounds || RequiredLocations.Any();
            }

            public bool AnyWithinDistance(Location startLocation, int distance)
            {
                return Map.LocationOrConnectionWithinDistance(
                    startLocation,
                    maxDistance: distance,
                    criteria: MatchesLocation,
                    connectionCriteria: MatchesConnection);
            }

            public bool MatchesLocation(Location location)
            {
                return RequiredLocations.Contains(location.Type.Identifier) && !location.IsCriticallyRadiated();
            }

            public bool MatchesConnection(LocationConnection connection)
            {
                if (RequireBeaconStation && connection.LevelData.HasBeaconStation && connection.LevelData.IsBeaconActive)
                {
                    return true;
                }
                if (RequireHuntingGrounds && connection.LevelData.HasHuntingGrounds)
                {
                    return true;
                }
                return false;
            }
        }

        public readonly Identifier CurrentType;

        public readonly Identifier ChangeToType;

        /// <summary>
        /// Base probability per world step for the location to change. Can be further modified by child <see cref="Modifier"/> elements.
        /// </summary>
        public readonly float Probability;

        public readonly bool RequireDiscovered;

        /// <summary>
        /// Hard requirements for the location type change to occur.
        /// </summary>
        private readonly Modifier requirements;

        /// <summary>
        /// Modifiers that adjust the base <see cref="Probability"/> of the ChangeTo.
        /// NOTE: Applied in the order they're defined in the XML, which matters especially when mixing <see cref="Modifier.FunctionType.Add"/> and <see cref="Modifier.FunctionType.Multiply"/>
        /// </summary>
        public readonly IReadOnlyList<Modifier> Modifiers;

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

        /// <summary>
        /// If a type change is triggered, a value is randomly chosen within this range and the change is delayed for that many world steps.
        /// </summary>
        public readonly Point RequiredDurationRange;

        public LocationTypeChange(Identifier currentType, ContentXElement element, bool requireChangeMessages, string parentDebugName, float defaultProbability = 0.0f)
        {
            CurrentType = currentType;
            ChangeToType = element.GetAttributeIdentifier("type", element.GetAttributeIdentifier("to", ""));

            RequireDiscovered = element.GetAttributeBool("requirediscovered", false);

            DisallowedAdjacentLocations = element.GetAttributeIdentifierArray("disallowedadjacentlocations", Array.Empty<Identifier>()).ToImmutableArray();
            DisallowedProximity = Math.Max(element.GetAttributeInt("disallowedproximity", 1), 1);

            RequiredDurationRange = element.GetAttributePoint("requireddurationrange", Point.Zero);
            //backwards compatibility (previously duration was a fixed value, now it's a range)
            if (element.GetAttribute("requiredduration") != null)
            {
                RequiredDurationRange = new Point(element.GetAttributeInt("requiredduration", 0));
            }

            Probability = element.GetAttributeFloat("probability", defaultProbability);

            CooldownAfterChange = Math.Max(element.GetAttributeInt("cooldownafterchange", 0), 0);
            
            // Here we reuse the Modifier parsing for the root ChangeTo as well, but they have slightly different requirements
            // for the attributes so we need to forward that information to generate proper warnings
            requirements = new Modifier(element, this, parentDebugName, isRootElement: true);

            // Check all the attributes that would only work in Modifier at once
            var attributesOnlyForModifier = new[]
                {
                    nameof(Modifier.ProbabilityModifier),
                    nameof(Modifier.Function),
                    nameof(Modifier.RequiredProximityForProbabilityIncrease),
                    nameof(Modifier.ProximityProbabilityIncrease)
                }
                .Where(attributeName => element.GetAttribute(attributeName) != null)
                .ToArray();
            if (attributesOnlyForModifier.Any())
            {
                DebugConsole.AddWarning(
                    $"Potentially invalid location type change in location type \"{CurrentType}\" ({parentDebugName}). " +
                    $"The attribute(s) {string.Join(", ", attributesOnlyForModifier)} can't be used in the root ChangeTo element itself, only in a {nameof(Modifier)} element. " +
                    $"Consider moving them to a {nameof(Modifier)} element instead.",
                    element.ContentPackage);
            }

            this.requireChangeMessages = requireChangeMessages;
            messageTag = element.GetAttributeString("messagetag", "LocationChange." + currentType + ".ChangeTo." + ChangeToType);

            List<Modifier> modifiers = new List<Modifier>();
            foreach (var subElement in element.Elements())
            {
                var nameAsId = subElement.NameAsIdentifier();
                if (nameAsId == nameof(Modifier) ||
                    nameAsId == "requirement")
                {
                    modifiers.Add(new Modifier(subElement, this, parentDebugName));
                }
            }
            Modifiers = modifiers.ToImmutableArray();
        }

        public float DetermineProbability(Location location)
        {
            if (RequireDiscovered && !location.Discovered) { return 0.0f; }
            if (location.IsCriticallyRadiated()) { return 0.0f; }
            if (location.LocationTypeChangeCooldown > 0) { return 0.0f; }
            if (location.IsGateBetweenBiomes) { return 0.0f; }
         
            if (DisallowedAdjacentLocations.Any() &&
                Map.LocationOrConnectionWithinDistance(location, DisallowedProximity, 
                    (otherLocation) => { return DisallowedAdjacentLocations.Contains(otherLocation.Type.Identifier); }))
            {
                return 0.0f;
            }

            if (location.PopulationPercentage < requirements.MinPopulation ||
                location.PopulationPercentage > requirements.MaxPopulation ||
                (requirements.HasAnyNearbyLocationRequirements() && !requirements.AnyWithinDistance(location, requirements.RequiredProximity)))
            {
                // Hard requirements not met, we stop here regardless of what the modifiers below would do to the probability
                return 0.0f;
            }

            float modifiedProbability = Probability;
            foreach (Modifier modifier in Modifiers)
            {
                if (location.PopulationPercentage < modifier.MinPopulation ||
                    location.PopulationPercentage > modifier.MaxPopulation)
                {
                    continue;
                }
                if (!modifier.HasAnyNearbyLocationRequirements() ||
                    modifier.AnyWithinDistance(location, modifier.RequiredProximity))
                {
                    if (modifier.Function == Modifier.FunctionType.Add)
                    {
                        modifiedProbability += modifier.ProbabilityModifier;
                    }
                    else
                    {
                        modifiedProbability *= modifier.ProbabilityModifier;
                    }
                }
                if (location.ProximityTimer.ContainsKey(modifier))
                {
                    if (modifier.AnyWithinDistance(location, modifier.RequiredProximityForProbabilityIncrease))
                    {
                        if (modifier.Function == Modifier.FunctionType.Add)
                        {
                            modifiedProbability += modifier.ProximityProbabilityIncrease * location.ProximityTimer[modifier];
                        }
                        else
                        {
                            // The increase is relative to the current probability here, ie. an increase of 0.1 means multiplying with x1.1 (not x0.1)
                            modifiedProbability *= 1.0f + modifier.ProximityProbabilityIncrease * location.ProximityTimer[modifier];
                        }
                    }
                }
            }

            return modifiedProbability;
        }
    }
}
