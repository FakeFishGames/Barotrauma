using System.Collections.Immutable;
using Barotrauma;
using Microsoft.Xna.Framework;

namespace HomeAndHarbor;

public class TechnologyTreePrefab : Prefab
{
    public static readonly PrefabCollection<TechnologyTreePrefab> Prefabs = [];

    public readonly ImmutableArray<TechnologyCategory> Categories;
    public readonly ImmutableArray<TechnologyTier> Tiers;

    /// <summary>
    /// Which prefabs should be grouped together when drawing dependency lines.
    /// This is useful for prefabs that are adjacent that have shared prerequisite causing
    /// a lot of prerequisite lines. Grouping these prefabs will draw a box around them
    /// and only produce 1 line into the shared prerequisite.
    /// Grouped prefabs should be adjacent in the UI and have the same prerequisite for the best results.
    /// </summary>
    public readonly ImmutableArray<TechnologyPrefabGroup> Groups;

    /// <summary>
    /// How many pixels (adjusted by GUI.Scale) the dependency lines should be offset.
    /// Used to manually offset them a bit in the case of overlapping lines to make them look cleaner.
    /// Resolving overlapping lines automatically would be a lot of work so these are defined manually.
    /// </summary>
    public readonly ImmutableDictionary<Identifier, Vector2> FineTuneLineOffsets;

    /// <summary>
    /// Dependency lines (in the same "from->to" format as <see cref="FineTuneLineOffsets"/>) that should prefer going right
    /// instead of left when there are multiple equally short routes. Used to keep lines from crossing over each other.
    /// </summary>
    public readonly ImmutableHashSet<Identifier> PreferRightLines;

    public TechnologyTreePrefab(ContentFile file, ContentXElement element) : base(file, element.GetAttributeIdentifier("identifier", Identifier.Empty))
    {
        var categoryBuilder = ImmutableArray.CreateBuilder<TechnologyCategory>();
        var tierBuilder = ImmutableArray.CreateBuilder<TechnologyTier>();
        var groupBuilder = ImmutableArray.CreateBuilder<TechnologyPrefabGroup>();
        var fineTuneBuilder  = ImmutableDictionary.CreateBuilder<Identifier, Vector2>();
        var preferRightBuilder = ImmutableHashSet.CreateBuilder<Identifier>();
        foreach (ContentXElement subElement in element.Elements())
        {
            Identifier name = subElement.NameAsIdentifier();

            if (name == "Group")
            {
                var prefabs = subElement.GetAttributeIdentifierImmutableHashSet("prefabs", ImmutableHashSet<Identifier>.Empty);
                groupBuilder.Add(new TechnologyPrefabGroup(prefabs));
                continue;
            }

            Identifier identifier = subElement.GetAttributeIdentifier("identifier", Identifier.Empty);

            if (identifier.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in {file.Path}: Missing or empty identifier for technology tree prefab element '{subElement.Name}'.");
                continue;
            }

            if (name == "Category")
            {
                Color color = subElement.GetAttributeColor("color", Color.White);

                TechnologyCategory category = new TechnologyCategory(identifier, color);
#if CLIENT
                if (subElement.GetChildElement("Portrait") is { } portrait)
                {
                    category = category with
                    {
                        Portrait = new Sprite(portrait, lazyLoad: true)
                    };
                }
#endif
                categoryBuilder.Add(category);
            }
            else if (name == "Tier")
            {
                tierBuilder.Add(TechnologyTier.FromXML(identifier, subElement));
            }
            else if (name == "FineTune")
            {
                Vector2 offset = subElement.GetAttributeVector2("offset", Vector2.Zero);
                fineTuneBuilder.Add(identifier, offset);
                if (subElement.GetAttributeBool("preferright", false))
                {
                    preferRightBuilder.Add(identifier);
                }
            }
            else
            {
                DebugConsole.ThrowError($"Error in {file.Path}: Unknown technology tree prefab type '{name}'.");
            }
        }
        Categories = categoryBuilder.ToImmutable();
        Tiers = tierBuilder.ToImmutable();
        Groups  = groupBuilder.ToImmutable();
        FineTuneLineOffsets = fineTuneBuilder.ToImmutable();
        PreferRightLines = preferRightBuilder.ToImmutable();
    }

    public override void Dispose()
    {
#if CLIENT
        foreach (TechnologyCategory category in Categories)
        {
            category.Portrait?.Remove();
        }
#endif
    }

    public bool Belongs(TechnologyPrefab technologyPrefab)
    {
        if (Categories.All(c => c.Identifier != technologyPrefab.Category)) { return false; }
        foreach (TechnologyTier tier in Tiers)
        {
            foreach (TierRow row in tier.Rows)
            {
                if (row.Prefabs.Contains(technologyPrefab.Identifier))
                {
                    return true;
                }
            }
        }

        return false;
    }
}

public readonly record struct TierRow(int Index, ImmutableHashSet<Identifier> Prefabs);

public readonly record struct TechnologyPrefabGroup(ImmutableHashSet<Identifier> Prefabs);

public readonly record struct TechnologyTier(Identifier Identifier, Color Color, LocalizedString UnlockText, ImmutableArray<TierRow> Rows)
{
    public static TechnologyTier FromXML(Identifier identifier, ContentXElement element)
    {
        Color color = element.GetAttributeColor("color", Color.White);
        string unlockTextStr = element.GetAttributeString("unlocktext", string.Empty);
        LocalizedString unlockText = TextManager.Get(unlockTextStr).Fallback(unlockTextStr);
        var rowBuilder = ImmutableArray.CreateBuilder<TierRow>();
        int row = 0;
        foreach (ContentXElement subElement in element.Elements())
        {
            if (subElement.NameAsIdentifier() == "Row")
            {
                rowBuilder.Add(new TierRow(row, subElement.GetAttributeIdentifierImmutableHashSet("prefabs", ImmutableHashSet<Identifier>.Empty)));
                row++;
            }
        }

        return new TechnologyTier(identifier, color, unlockText, rowBuilder.ToImmutable());
    }

    public int GetRow(TechnologyPrefab prefab)
    {
        foreach (TierRow row in Rows)
        {
            if (row.Prefabs.Contains(prefab.Identifier))
            {
                return row.Index;
            }
        }

        return -1;
    }
}

public readonly record struct TechnologyCategory(Identifier Identifier, Color Color)
{
#if CLIENT
    public Sprite? Portrait { get; init; }
#endif
}