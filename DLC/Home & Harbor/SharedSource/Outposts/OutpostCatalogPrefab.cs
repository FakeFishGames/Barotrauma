using Barotrauma;

namespace HomeAndHarbor;


public class OutpostCatalogPrefab : Prefab
{
    public static readonly PrefabCollection<OutpostCatalogPrefab> Prefabs = new();

    public readonly LocalizedString DisplayName;
    public readonly LocalizedString ToolTip;

    public readonly bool RequiresOutpostManagement;

    public readonly Identifier CatalogItemIdentifier;
    public readonly Identifier RequiredTechnologyIdentifier;

    /// <summary>
    /// UI style of the catalog button in the outpost building UI. If empty, a generic text button is used instead.
    /// </summary>
    public readonly Identifier ButtonStyle;
    /// <summary>
    /// UI style swapped in when the catalog button is disabled (there's no disabled sprite state in UI styles).
    /// </summary>
    public readonly Identifier DisabledButtonStyle;

    public OutpostCatalogPrefab(ContentFile file, ContentXElement element) : base(file, element.GetAttributeIdentifier("identifier", Identifier.Empty))
    {
        string displayName = element.GetAttributeString(nameof(DisplayName), "");
        DisplayName = TextManager.Get(displayName).Fallback(displayName);

        string toolTip = element.GetAttributeString(nameof(ToolTip), "");
        ToolTip = TextManager.Get(toolTip).Fallback(toolTip);

        RequiresOutpostManagement = element.GetAttributeBool(nameof(RequiresOutpostManagement), false);
        CatalogItemIdentifier = element.GetAttributeIdentifier(nameof(CatalogItemIdentifier), "");
        RequiredTechnologyIdentifier = element.GetAttributeIdentifier(nameof(RequiredTechnologyIdentifier), "");
        ButtonStyle = element.GetAttributeIdentifier(nameof(ButtonStyle), "");
        DisabledButtonStyle = element.GetAttributeIdentifier(nameof(DisabledButtonStyle), "");
    }

    public override void Dispose() { }
}