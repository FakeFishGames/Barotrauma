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
        public readonly float MaxDifficulty;

        public readonly ImmutableHashSet<int> AllowedZones;

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
            MaxDifficulty = element.GetAttributeFloat("MaxDifficulty", 100);
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

        public override void Dispose() { }
    }
}