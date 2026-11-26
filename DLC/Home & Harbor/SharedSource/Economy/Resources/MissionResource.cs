using Barotrauma;
using System.Xml.Linq;

namespace HomeAndHarbor.Resources;

/// <summary>
/// Resource that corresponds to the number of active (not completed or failed) missions with a specific tag in the location.
/// Can be used e.g. to check if there's an uncleared nest near the outpost.
/// </summary>
internal class MissionResource(ResourcePrefab prefab, ResourceManager manager, XElement? element = null)
    : ReadonlyResource(prefab, manager, element)
{
    [Serialize("", IsPropertySaveable.No, description: "Tag of the missions that add up to this resource.")]
    public Identifier MissionTag { get; set; }

    protected override float GetAmountResourceSpecific()
    {
        return Location.AvailableMissions.Count(m => m.Prefab.Tags.Contains(MissionTag) && !m.ShouldBeRemovedFromLocation());
    }
}
