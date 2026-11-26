using Barotrauma;
using Microsoft.Xna.Framework;
using System.Collections.Immutable;

namespace HomeAndHarbor;

/// <summary>
/// Indicator on the status of some resource, e.g. starvation, out of power, on strike...
/// The status indicators can be displayed in the UI, and can be checked in resource conditionals to make them affect other resources.
/// </summary>
public class ResourceStatusIndicator
{
    public record class PriceMultiplier(ImmutableArray<Identifier> ItemTags, float Multiplier, bool IncreaseAvailability);

    public Identifier Identifier;

    public readonly LocalizedString Name;
    public readonly LocalizedString Description;

    public readonly ContentXElement OriginalElement;

    public readonly Color IconColor;

    public Sprite? Icon;

    /// <summary>
    /// Should the indicator be hidden from the player in menus etc?
    /// </summary>
    public readonly bool Hidden;

    private readonly ImmutableArray<Requirement> requirements;

    private PropertyConditional.LogicalOperatorType conditionalComparison;

    /// <summary>
    /// Which location types can this indicator appear in? If empty, it can appear in any.
    /// </summary>
    private readonly ImmutableHashSet<Identifier> locationTypes;

    public readonly ImmutableArray<PriceMultiplier> PriceMultipliers;

    public bool IsActive { get; private set; }

    public ResourceStatusIndicator(ContentXElement element, string debugLogName)
    {
        Identifier = element.GetAttributeIdentifier("identifier", Identifier.Empty);
        if (Identifier.IsEmpty)
        {
            DebugConsole.ThrowError($"Error in {debugLogName}: {nameof(ResourceStatusIndicator)} has no identifier!");
        }

        OriginalElement = element;
        string? name = element.GetAttributeString("name", null);
        if (string.IsNullOrWhiteSpace(name))
        {
            Name = TextManager
                   .Get($"ResourceStatusIndicator.{Identifier}")
                   .Fallback(Identifier.Value);
        }
        else
        {
            Name = name;
        }

        string? description = element.GetAttributeString("description", null);
        if (string.IsNullOrWhiteSpace(description))
        {
            Description = TextManager
                          .Get($"ResourceStatusIndicator.{Identifier}.description")
                          .Fallback(string.Empty);
        }
        else
        {
            Description = description;
        }

        Hidden = element.GetAttributeBool(nameof(Hidden), false);

        locationTypes = element.GetAttributeIdentifierImmutableHashSet(nameof(locationTypes), ImmutableHashSet<Identifier>.Empty);

        conditionalComparison = element.GetAttributeEnum(nameof(conditionalComparison), PropertyConditional.LogicalOperatorType.And);

        var priceMultipliersBuilder = ImmutableArray.CreateBuilder<PriceMultiplier>();
        var requirementsBuilder = ImmutableArray.CreateBuilder<Requirement>();
        foreach (ContentXElement subElement in element.Elements())
        {
            Identifier elementName = subElement.NameAsIdentifier();
            if (elementName == "Conditional")
            {
                if (Requirement.TryCreate(subElement, out Requirement? requirement) && requirement != null)
                {
                    requirementsBuilder.Add(requirement);
                }
            }
            else if (elementName == nameof(PriceMultiplier))
            {
                priceMultipliersBuilder.Add(new PriceMultiplier(
                    ItemTags: subElement.GetAttributeIdentifierArray(nameof(PriceMultiplier.ItemTags), []).ToImmutableArray(),
                    Multiplier: subElement.GetAttributeFloat(nameof(PriceMultiplier.Multiplier), 1f),
                    IncreaseAvailability: subElement.GetAttributeBool(nameof(PriceMultiplier.IncreaseAvailability), false)));
            }
            else if (elementName == nameof(Icon))
            {
                Icon = new Sprite(subElement);
                IconColor = subElement.GetAttributeColor("color", Color.Red);
            }
            else
            {
                DebugConsole.ThrowError($"Error in {debugLogName}: Unrecognized element \"{subElement.Name}\" in {nameof(ResourceStatusIndicator)} \"{Identifier}\".");
            }
        }
        requirements = requirementsBuilder.ToImmutable();
        PriceMultipliers = priceMultipliersBuilder.ToImmutable();
    }

    internal void UpdateIsActive(ResourceManager manager)
    {
        if (!locationTypes.IsEmpty)
        {
            if (!locationTypes.Contains(manager.Location.Type.Identifier))
            {
                IsActive = false;
                return;
            }
        }
        IsActive = Requirement.RequirementsMatch(manager, requirements, conditionalComparison, $"{nameof(ResourceStatusIndicator)} ({Identifier})");
    }
}