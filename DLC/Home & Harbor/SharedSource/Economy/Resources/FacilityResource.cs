using System.Xml.Linq;
using Barotrauma;
using Microsoft.Xna.Framework;

namespace HomeAndHarbor.Resources
{
    /// <summary>
    /// Resource that corresponds to amount of static items in the outpost.
    /// </summary>
    internal class FacilityResource : ReadonlyResource
    {
        [Serialize("", IsPropertySaveable.No, description: "The item identifier or tag of the facility.")]
        public Identifier FacilityTag { get; set; }

        [Serialize(1f, IsPropertySaveable.Yes, description: "Multiplier for the amount of items to value. A value of 1 means one item corresponds to one unit of the resource amount while a value of 0.1 means one item corresponds to ten units of the resource amount.")]
        public float ItemsToValueMultiplier { get; set; }

        private readonly Dictionary<Identifier, int> facilityTagsAndRatio = [];

        public FacilityResource(ResourcePrefab prefab, ResourceManager manager, XElement? element = null) : base(prefab, manager, element)
        {
            foreach (XElement subElement in prefab.OriginalElement.Elements())
            {
                if (subElement.NameAsIdentifier() == "Item")
                {
                    Identifier tag = subElement.GetAttributeIdentifier("tag", Identifier.Empty);
                    if (tag.IsEmpty) { continue; }

                    facilityTagsAndRatio[tag] = subElement.GetAttributeInt("ratio", 1);
                    if (subElement.GetAttribute("weight") != null)
                    {
                        DebugConsole.ThrowError(
                            $"Error in {nameof(FacilityResource)} \"{Identifier}\": an item configured with a weight rather than a ratio."+
                            " The ratio determines how much each matching item contributes to the resource.");
                    }
                }
            }
        }

        protected override float GetAmountResourceSpecific()
        {
            if (Manager.OutpostData.OutpostFacilities.TryUnwrap(out var facility))
            {
                return GetAmount(facility);
            }
            return -1;
        }

        public float GetAmount(OutpostFacilityManager facilityManager)
        {
            float amount = 0;
            if (!FacilityTag.IsEmpty)
            {
                amount = facilityManager.GetCountByTag(FacilityTag) * ItemsToValueMultiplier;
            }
            foreach (var (tag, ratio) in facilityTagsAndRatio)
            {
                amount += facilityManager.GetCountByTag(tag) * ratio;
            }
            return amount;
        }

        public bool MatchesItemPrefab(ItemPrefab itemPrefab)
        {
            if (!FacilityTag.IsEmpty)
            {
                if (itemPrefab.Identifier == FacilityTag || itemPrefab.Tags.Contains(FacilityTag))
                {
                    return true;
                }
            }
            if (facilityTagsAndRatio.Any(t => t.Key == itemPrefab.Identifier || itemPrefab.Tags.Contains(t.Key)))
            {
                return true;
            }
            return false;
        }
    }
}