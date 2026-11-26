using Barotrauma;
using Barotrauma.Items.Components;
using System.Xml.Linq;

namespace HomeAndHarbor.Resources;

/// <summary>
/// A resource that calculates the percentage of hulls that are "sufficiently lit". 
/// The "sufficiency" is calculated based on the range and brightness of the lights relative to the size of the hull, and the threshold can be adjusted in XML.
/// </summary>
internal class LightingResource : ReadonlyResource
{
    /// <summary>
    /// The intensity of the lights (calculated as range * brightness) divided by the sum of the hull's width and height must be larger than this for the hull to be considered sufficiently lit.
    /// I.e. an ad-hoc formula that's supposed to roughly approximate whether the hull looks sufficiently lit - finding the appropriate threshold is a matter of testing and adjusting what looks good.
    /// </summary>
    private readonly float sufficientLightingThreshold;

    public LightingResource(ResourcePrefab prefab, ResourceManager manager, XElement? element = null) : base(prefab, manager, element)
    {
        sufficientLightingThreshold = element.GetAttributeFloat(nameof(sufficientLightingThreshold), 0.1f);
    }

    protected override void OnOutpostEnteredResourceSpecific(Submarine outpost, PersistentOutpostData outpostData)
    {
        RefreshAmount(outpost, outpostData);
    }

    protected override void OnOutpostExitedResourceSpecific(Submarine outpost, PersistentOutpostData outpostData, bool shouldCreateLogs)
    {
        RefreshAmount(outpost, outpostData);
    }

    protected override float GetAmountResourceSpecific()
    {
        return CalculatedAmount;
    }

    /// <summary>
    /// The amount calculated the last time the outpost was entered or exited. Saved so the value persists when the outpost isn't loaded.
    /// Negative = not calculated yet, in which case the simulated value is used.
    /// </summary>
    [Serialize(-1f, IsPropertySaveable.Yes, description: "The lighting amount calculated the last time the outpost was entered or exited. Negative if it hasn't been calculated yet.")]
    public float CalculatedAmount { get; private set; }

    private void RefreshAmount(Submarine outpost, PersistentOutpostData outpostData)
    {
        List<LightComponent> lights = [];
        foreach (var item in outpost.GetItems(alsoFromConnectedSubs: false))
        {
            if (item.ParentInventory != null) { continue; }
            if (item.GetComponent<LightComponent>() is LightComponent light)
            {
                lights.Add(light);
            }
        }

        int hullCount = 0;
        int litHullCount = 0;
        foreach (Hull hull in outpost.GetHulls(alsoFromConnectedSubs: false))
        {
            if (hull.IsWetRoom || hull.IsAirlock || hull.AvoidStaying) { continue; }
            float lightAmount = lights.Where(light => light.Item.CurrentHull == hull).Sum(light => light.Range * light.LightColor.A / 255.0f);
            if (lightAmount / (hull.RectWidth + hull.RectHeight) > sufficientLightingThreshold)
            {
                litHullCount++;
            }
            hullCount++;
        }

        float litHullPercentage = litHullCount == 0 ? 0 : (float)litHullCount / hullCount;

        CalculatedAmount = litHullPercentage * 100;
        System.Diagnostics.Debug.Assert(CalculatedAmount >= 0 && CalculatedAmount <= 100);
    }
}