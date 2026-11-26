using Barotrauma;
using System.Collections.Immutable;
using System.Xml.Linq;

namespace HomeAndHarbor;

public sealed class TechnologyPrefab : Prefab
{
    public class TechnologyPrerequisite(XElement element)
    {
        public readonly PropertyConditional.LogicalOperatorType Comparison = element.GetAttributeEnum("comparison", PropertyConditional.LogicalOperatorType.And);
        public ImmutableArray<Identifier> Identifiers = [..element.GetAttributeIdentifierArray("identifiers", [])];

        public bool Matches(ResearchManager manager)
        {
            if (Comparison is PropertyConditional.LogicalOperatorType.And)
            {
                foreach (Identifier identifier in Identifiers)
                {
                    if (!manager.IsFinished(identifier)) { return false; }
                }

                return true;
            }
            else
            {
                foreach (Identifier identifier in Identifiers)
                {
                    if (manager.IsFinished(identifier)) { return true; }
                }

                return false;
            }
        }
    }

    public static readonly PrefabCollection<TechnologyPrefab> Prefabs = [];

    /// <summary>
    /// Shown in place of the name, description, unlocks and requirements of hidden technologies (see <see cref="HiddenUntilMetadataRequirementMet"/>)
    /// </summary>
    public static readonly LocalizedString HiddenText = TextManager.Get("technology.hidden").Fallback("???");

    public readonly LocalizedString Name;
    public readonly LocalizedString Description;

    /// <summary>
    /// List of unlock texts to show in the UI.
    /// Does not correspond to the actual unlocks of this technology but rather
    /// a brief description shown in the UI.
    /// Supports the same formatting as talent descriptions in the XML.
    /// </summary>
    public readonly ImmutableArray<LocalizedString> UnlockTexts;

    public readonly int Cost;
    public readonly bool UnlockedByDefault;
    public readonly ImmutableArray<TechnologyPrerequisite> Prerequisites;
    /// <summary>
    /// Makes the technology require a campaign metadata to be set to true for it to be researchable.
    /// </summary>
    public readonly Identifier MetadataRequirement;
    public readonly LocalizedString MetadataRequirementName;
    public readonly LocalizedString MetadataRequirementTooltip;
    /// <summary>
    /// Hides the name, description, unlocks and requirements of the technology in the UI until <see cref="MetadataRequirement"/> is met (e.g. to avoid spoiling the story).
    /// The metadata requirement itself is still shown, so the player knows how to reveal the technology.
    /// Technologies that have a hidden technology as a prerequisite are hidden too.
    /// </summary>
    public readonly bool HiddenUntilMetadataRequirementMet;
    public readonly Identifier Tier;
    public readonly Identifier Category;

    public TechnologyPrefab(ContentFile file, ContentXElement element) : base(file, element.GetAttributeIdentifier("identifier", Identifier.Empty))
    {
        Name = TextManager.Get($"TechnologyName.{Identifier}").Fallback(element.GetAttributeString(nameof(Name), Identifier.Value));
        Description = TextManager.Get($"TechnologyDescription.{Identifier}").Fallback(element.GetAttributeString(nameof(Description), ""));

        string metadataRequirementNameAttribute = element.GetAttributeString(nameof(MetadataRequirementName), "");
        MetadataRequirementName = TextManager.Get(metadataRequirementNameAttribute).Fallback(metadataRequirementNameAttribute);

        string metadataRequirementTooltipAttribute = element.GetAttributeString(nameof(MetadataRequirementTooltip), "");
        MetadataRequirementTooltip = TextManager.Get(metadataRequirementTooltipAttribute).Fallback(metadataRequirementTooltipAttribute);

        Cost = element.GetAttributeInt(nameof(Cost), 1);
        UnlockedByDefault = element.GetAttributeBool(nameof(UnlockedByDefault), false);
        Tier = element.GetAttributeIdentifier(nameof(Tier), Identifier.Empty);
        Category = element.GetAttributeIdentifier(nameof(Category), Identifier.Empty);
        MetadataRequirement = element.GetAttributeIdentifier(nameof(MetadataRequirement), Identifier.Empty);
        HiddenUntilMetadataRequirementMet = element.GetAttributeBool(nameof(HiddenUntilMetadataRequirementMet), false);

        if (HiddenUntilMetadataRequirementMet && MetadataRequirement.IsEmpty)
        {
            DebugConsole.AddWarning($"{nameof(TechnologyPrefab)} \"{Identifier}\" is set to be hidden until the metadata requirement is met, but no metadata requirement is defined. The technology will never be hidden.");
        }

        if (Tier.IsEmpty)
        {
            DebugConsole.ThrowError($"Error in {nameof(TechnologyPrefab)} \"{Identifier}\" tier is not defined.");
        }

        if (Category.IsEmpty)
        {
            DebugConsole.ThrowError($"Error in {nameof(TechnologyPrefab)} \"{Identifier}\" category is not defined.");
        }

        var prerequisiteBuilder = ImmutableArray.CreateBuilder<TechnologyPrerequisite>();
        var unlockBuilder = ImmutableArray.CreateBuilder<LocalizedString>();

        foreach (XElement subElement in element.Elements())
        {
            switch (subElement.Name.LocalName.ToLowerInvariant())
            {
                case "prerequisite":
                    prerequisiteBuilder.Add(new TechnologyPrerequisite(subElement));
                    break;
                case "unlock":
                    LocalizedString unlockText = string.Empty;
                    TextManager.ConstructDescription(ref unlockText, subElement);
                    unlockBuilder.Add(unlockText);
                    break;
            }
        }

        Prerequisites = prerequisiteBuilder.ToImmutable();
        UnlockTexts = unlockBuilder.ToImmutable();
    }

    public bool CheckMetadataRequirement()
    {
        if (GameMain.GameSession?.Campaign is not { } campaign)
        {
            return true;
        }

        if (MetadataRequirement.IsEmpty) { return true; }

        return campaign.CampaignMetadata.GetBoolean(MetadataRequirement, false);
    }

    /// <summary>
    /// Is the technology hidden because of its own unmet metadata requirement (as opposed to being hidden because of a hidden prerequisite)?
    /// </summary>
    public bool IsHiddenByOwnMetadataRequirement
        => HiddenUntilMetadataRequirementMet && !CheckMetadataRequirement();

    /// <summary>
    /// Should the details of the technology be hidden in the UI? See <see cref="HiddenUntilMetadataRequirementMet"/>.
    /// </summary>
    public bool IsHidden()
        => IsHidden(checkedPrefabs: []);

    /// <summary>
    /// The name to show in the UI: <see cref="HiddenText"/> if the technology is hidden, <see cref="Name"/> otherwise.
    /// </summary>
    public LocalizedString GetDisplayName()
        => IsHidden() ? HiddenText : Name;

    private bool IsHidden(HashSet<TechnologyPrefab> checkedPrefabs)
    {
        // Prevent infinite recursion if the prerequisites are circular
        if (!checkedPrefabs.Add(this)) { return false; }
        if (IsHiddenByOwnMetadataRequirement) { return true; }

        foreach (TechnologyPrerequisite prerequisite in Prerequisites)
        {
            foreach (Identifier identifier in prerequisite.Identifiers)
            {
                if (Prefabs.TryGet(identifier, out TechnologyPrefab? prerequisitePrefab) && prerequisitePrefab.IsHidden(checkedPrefabs))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public override void Dispose() { }
}