using System.Collections.Immutable;
using Barotrauma.PerkBehaviors;

namespace Barotrauma
{
    internal sealed class DisembarkPerkPrefab : PrefabWithUintIdentifier
    {
        public static readonly PrefabCollection<DisembarkPerkPrefab> Prefabs = new PrefabCollection<DisembarkPerkPrefab>();

        public LocalizedString Name { get; }
        public LocalizedString Description { get; }
        public Identifier SortCategory { get; }

        /// <summary>
        /// After the perks have been sorted by category and cost, they are sorted using this key.
        /// Use if you want the perks to be arranged in specific order when their cost are the same.
        /// </summary>
        public int SortKey { get; }

        /// <summary>
        /// When set to an identifier of another perk, this perk cannot be selected unless the prerequisite perk is selected.
        /// </summary>
        public Identifier Prerequisite { get; }

        /// <summary>
        /// When this perk is selected, the perks in this set cannot be selected at the same time.
        /// </summary>
        public ImmutableHashSet<Identifier> MutuallyExclusivePerks { get; }

        public int Cost { get; }

        public ImmutableArray<PerkBase> PerkBehaviors { get; }

        public DisembarkPerkPrefab(ContentXElement element, DisembarkPerkFile prefabFile) : base(prefabFile, element.GetAttributeIdentifier("identifier", ""))
        {
            Name = TextManager.Get($"disembarkperk.{Identifier}").Fallback(Identifier.ToString());
            Description = TextManager.Get($"disembarkperkdescription.{Identifier}").Fallback("");
            Cost = element.GetAttributeInt("cost", 0);
            SortCategory = element.GetAttributeIdentifier("sortcategory", Identifier);
            Prerequisite = element.GetAttributeIdentifier("prerequisite", Identifier.Empty);
            MutuallyExclusivePerks = element.GetAttributeIdentifierImmutableHashSet("mutuallyexclusiveperks", ImmutableHashSet<Identifier>.Empty);
            SortKey = element.GetAttributeInt("sortkey", ToolBox.IdentifierToInt(Identifier));

            var builder = ImmutableArray.CreateBuilder<PerkBase>();
            foreach (var child in element.Elements())
            {
                if (PerkBase.TryLoadFromXml(child, this, out var perk))
                {
                    builder.Add(perk);
                }
            }

            PerkBehaviors = builder.ToImmutable();
        }

        public override void Dispose() {  }
    }
}