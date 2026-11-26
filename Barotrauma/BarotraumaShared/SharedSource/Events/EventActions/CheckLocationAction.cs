#nullable enable
using System;

namespace Barotrauma
{
    /// <summary>
    /// Check whether the reputation of the crew for a specific faction meets some criteria (e.g. equal to, larger than or less than some value).
    /// </summary>
    class CheckLocationAction : BinaryOptionAction
    {
        public enum Target
        {
            /// <summary>
            /// Default behavior: modifies the current location.
            /// </summary>
            Default,
            /// <summary>
            /// Only valid for actions executed by a mission. Modifies the start location of the triggering mission.
            /// </summary>
            MissionStartLocation,
            /// <summary>
            /// Only valid for actions executed by a mission. Modifies the end location (destination) of the triggering mission.
            /// </summary>
            MissionEndLocation
        }

        [Serialize(Target.Default, IsPropertySaveable.Yes)]
        public Target TargetLocation { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "If set, the faction of the current location needs to match this value.")]
        public Identifier RequiredFaction { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "If set, the type of the location needs to match this value.")]
        public Identifier RequiredLocationType { get; set; }

        [Serialize(-1.0f, IsPropertySaveable.Yes, description: "If set, the population of the current location needs to be above this value for the check to succeed.")]
        public float MinPopulationPercentage { get; set; }

        [Serialize(1.0f, IsPropertySaveable.Yes, description: "If set, the population of the current location needs to be below or equal to this value for the check to succeed.")]
        public float MaxPopulationPercentage { get; set; }

        public CheckLocationAction(ScriptedEvent parentEvent, ContentXElement element) : base(parentEvent, element) { }

        protected override bool? DetermineSuccess()
        {
            Location? location = TargetLocation switch
            {
                Target.Default => Level.Loaded?.StartLocation,
                Target.MissionStartLocation => ParentMission?.Locations[0] ?? Level.Loaded?.StartLocation,
                Target.MissionEndLocation => ParentMission?.Locations[1] ?? Level.Loaded?.EndLocation,
                _ => throw new ArgumentOutOfRangeException(nameof(TargetLocation), TargetLocation, null)
            };

            if (location == null) { return false; }

            if (RequiredFaction != Identifier.Empty && location.Faction?.Prefab.Identifier != RequiredFaction)
            {
                return false;
            }
            if (RequiredLocationType != Identifier.Empty && location.Type.Identifier != RequiredLocationType)
            {
                return false;
            }
            if (location.PopulationPercentage <= MinPopulationPercentage)
            {
                return false;
            }
            if (location.PopulationPercentage > MaxPopulationPercentage)
            {
                return false;
            }
            return true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(succeeded.HasValue)} {nameof(CheckLocationAction)} -> (" +
                   $"{(RequiredFaction.IsEmpty ? string.Empty : $"Required Faction: {RequiredFaction.ColorizeObject()}, ")}" +
                   $"{(RequiredLocationType.IsEmpty ? string.Empty : $"Required location type: {RequiredLocationType.ColorizeObject()}, ")}" +
                   $"{(MinPopulationPercentage < 0 ? string.Empty : $"min population: {(MinPopulationPercentage * 100).ColorizeObject()}, ")}" +
                   $"{(MaxPopulationPercentage >= 1 ? string.Empty : $"max population: {(MaxPopulationPercentage * 100).ColorizeObject()}")}" +
                   ")";
        }
    }
}