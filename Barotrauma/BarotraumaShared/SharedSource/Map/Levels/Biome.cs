using Barotrauma.Extensions;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Barotrauma
{
    class Biome : PrefabWithUintIdentifier
    {
        public readonly static PrefabCollection<Biome> Prefabs = new PrefabCollection<Biome>();

        public readonly Identifier OldIdentifier;
        public readonly LocalizedString DisplayName;
        public readonly LocalizedString Description;

        public readonly bool IsEndBiome;
        public readonly float MinDifficulty;
        private readonly float maxDifficulty;
        public float ActualMaxDifficulty => maxDifficulty;
        public float AdjustedMaxDifficulty => maxDifficulty - 0.1f;

        public readonly ImmutableHashSet<int> AllowedZones;

        private readonly SubmarineAvailability? submarineAvailability;
        private readonly ImmutableHashSet<SubmarineAvailability> submarineAvailabilityOverrides;

        public readonly record struct SubmarineAvailability(
            Identifier LocationType,
            SubmarineClass Class = SubmarineClass.Undefined,
            int MaxTier = 0);

        public Biome(ContentXElement element, LevelGenerationParametersFile file) : base(file, ParseIdentifier(element))
        {
            OldIdentifier = element.GetAttributeIdentifier("oldidentifier", Identifier.Empty);

            DisplayName =
                TextManager.Get("biomename." + Identifier).Fallback(
                element.GetAttributeString("name", "Biome"));

            Description =
                TextManager.Get("biomedescription." + Identifier).Fallback(
                element.GetAttributeString("description", ""));

            IsEndBiome = element.GetAttributeBool("endbiome", false);
            AllowedZones = element.GetAttributeIntArray("AllowedZones", new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }).ToImmutableHashSet();
            MinDifficulty = element.GetAttributeFloat("MinDifficulty", 0);
            maxDifficulty = element.GetAttributeFloat("MaxDifficulty", 100);

            var submarineAvailabilityOverrides = new HashSet<SubmarineAvailability>();
            if (element.GetChildElement("submarines") is ContentXElement availabilityElement)
            {
                submarineAvailability = GetAvailability(availabilityElement);
                foreach (var overrideElement in availabilityElement.GetChildElements("override"))
                {
                    var availabilityOverride = GetAvailability(overrideElement);
                    submarineAvailabilityOverrides.Add(availabilityOverride);
                }
            }
            this.submarineAvailabilityOverrides = submarineAvailabilityOverrides.ToImmutableHashSet();

            static SubmarineAvailability GetAvailability(ContentXElement element)
            {
                return new SubmarineAvailability(
                    LocationType: element.GetAttributeIdentifier("locationtype", Identifier.Empty),
                    Class: element.GetAttributeEnum("class", SubmarineClass.Undefined),
                    MaxTier: element.GetAttributeInt("maxtier", 0));
            }
        }

        public static Identifier ParseIdentifier(ContentXElement element)
        {
            Identifier identifier = element.GetAttributeIdentifier("identifier", "");
            if (identifier.IsEmpty)
            {
                identifier = element.GetAttributeIdentifier("name", "");
                DebugConsole.ThrowError("Error in biome \"" + identifier + "\": identifier missing, using name as the identifier.");
            }
            return identifier;
        }

        public int HighestSubmarineTierAvailable(SubmarineClass subClass, Identifier locationType)
        {
            if (!submarineAvailability.HasValue)
            {
                // If the availability is not explicitly defined, make all subs available
                return SubmarineInfo.HighestTier;
            }
            int maxTier = submarineAvailability.Value.MaxTier;
            if (submarineAvailabilityOverrides.FirstOrNull(a => a.LocationType == locationType && a.Class == subClass) is SubmarineAvailability locationAndClassOverride)
            {
                maxTier = locationAndClassOverride.MaxTier;
            }
            else if (submarineAvailabilityOverrides.FirstOrNull(a => a.LocationType == locationType && a.Class == SubmarineClass.Undefined) is SubmarineAvailability locationOverride)
            {
                maxTier = locationOverride.MaxTier;
            }
            else if (submarineAvailabilityOverrides.FirstOrNull(a => a.LocationType == Identifier.Empty && a.Class == subClass) is SubmarineAvailability classOverride)
            {
                maxTier = classOverride.MaxTier;
            }
            return maxTier;
        }

        public bool IsSubmarineAvailable(SubmarineInfo info, Identifier locationType) => info.Tier <= HighestSubmarineTierAvailable(info.SubmarineClass, locationType);

        public override void Dispose() { }
    }
}