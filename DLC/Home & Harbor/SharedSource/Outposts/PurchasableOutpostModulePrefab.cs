#nullable enable
using Barotrauma;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Collections.Immutable;
using System.Xml.Linq;

namespace HomeAndHarbor;

partial class PurchasableOutpostModulePrefab : Prefab
{
    public class GapPositionInfo(Rectangle rect, OutpostModuleInfo.GapPosition position, ushort gapId)
    {
        public readonly Rectangle Rect = rect;
        public readonly OutpostModuleInfo.GapPosition Position = position;
        public readonly ushort GapId = gapId;

        public bool IsHorizontal => Position is OutpostModuleInfo.GapPosition.Right or OutpostModuleInfo.GapPosition.Left;
    }

    public static readonly PrefabCollection<PurchasableOutpostModulePrefab> Prefabs = [];

    /// <summary>
    /// Can be used to override the name defined for the module file in the sub editor.
    /// </summary>
    public LocalizedString OverrideName { get; private set; }

    /// <summary>
    /// Can be used to override the description defined for the module file in the sub editor.
    /// </summary>
    public LocalizedString OverrideDescription { get; private set; }

    public LocalizedString DisplayName => OverrideName.Loaded && !OverrideName.Value.IsNullOrEmpty() ? OverrideName : GetDefaultDisplayName(OutpostInfo);

    public LocalizedString DisplayDescription => OverrideDescription.Loaded && !OverrideDescription.Value.IsNullOrEmpty() ? OverrideDescription : OutpostInfo.Description;

    [Serialize(defaultValue: 0, isSaveable: IsPropertySaveable.Yes)]
    public int Price { get; set; }

    [Serialize(defaultValue: "", isSaveable: IsPropertySaveable.Yes)]
    public Identifier Category { get; set; }
    
    [Serialize(defaultValue: int.MaxValue, isSaveable: IsPropertySaveable.Yes)]
    public int MaxCountPerCategory { get; set; }
    
    [Serialize(defaultValue: 4, isSaveable: IsPropertySaveable.Yes, description: "Amount of world steps it takes for the module to be built.")]
    public int TimeStepsToBuild { get; set; }

    [Serialize(defaultValue: true, isSaveable: IsPropertySaveable.Yes, description: 
        "Is the module sold / possible to purchase for your outpost?"+
        " Useful for configuring information about pre-made NPC outpost modules for showing in the outpost building UI, without making the module purchaseable.")]
    public bool Sold { get; set; }

    [Serialize(defaultValue: "", isSaveable: IsPropertySaveable.Yes, description:
        "If set, the outpost's secondary faction is changed to this faction when the module is added to the outpost (e.g. to unlock the stores and hires of that faction)."+
        " The secondary faction is cleared when the module is removed (unless some other module in the outpost also sets a secondary faction).")]
    public Identifier OutpostSecondaryFaction { get; set; }

    [Serialize(defaultValue: false, isSaveable: IsPropertySaveable.Yes, description:
        "If enabled, no other modules can be placed or moved above this module (e.g. a rocket module that needs a clear path upwards).")]
    public bool DisallowModulesAbove { get; set; }

    public Rectangle HullBounds { get; private set; }

    public Rectangle Bounds { get; private set; }

    private readonly ImmutableList<UpgradeResourceCost> resourceCosts;
    public IEnumerable<UpgradeResourceCost> ResourceCosts => resourceCosts;

    public readonly List<GapPositionInfo> Gaps = [];

    private readonly ImmutableList<ItemPrefab> swappableItems;
    public IEnumerable<ItemPrefab> SwappableItems => swappableItems;

    private readonly ImmutableList<ItemPrefab> workstations;
    public IEnumerable<ItemPrefab> Workstations => workstations;

    public Option<OutpostFacilityManager> OutpostFacilities;

    public readonly SubmarineInfo OutpostInfo;
    public readonly Identifier RequiredResearch;

    public PurchasableOutpostModulePrefab(ContentFile file, ContentXElement element) : 
        base(file, element.GetAttributeIdentifier("identifier", Identifier.Empty))
    {
        SerializableProperty.DeserializeProperties(this, element);

        var nameTag = element.GetAttributeIdentifier("name", Identifier.Empty);
        OverrideName = nameTag.IsEmpty ? LocalizedString.EmptyString : TextManager.Get(nameTag);
        if (!nameTag.IsEmpty && OverrideName.Value.IsNullOrEmpty())
        {
            DebugConsole.AddWarning($"Potential error in {nameof(PurchasableOutpostModulePrefab)}: name defined as {nameTag} but a matching text could not be found.");
        }
        
        var descriptionTag = element.GetAttributeIdentifier("description", Identifier.Empty);
        OverrideDescription = descriptionTag.IsEmpty ? LocalizedString.EmptyString : TextManager.Get(descriptionTag); 
        if (!descriptionTag.IsEmpty && OverrideDescription.Value.IsNullOrEmpty())
        {
            DebugConsole.AddWarning($"Potential error in {nameof(PurchasableOutpostModulePrefab)}: description defined as {descriptionTag} but a matching text could not be found.");
        }

        ContentPath? moduleFilePath = element.GetAttributeContentPath("modulefile");
        if (moduleFilePath is null || string.IsNullOrEmpty(moduleFilePath.Value))
        {
            throw new Exception($"Error in content file {file.Path} (content package {ContentPackage?.Name ?? "null"}). Outpost module path not set.");
        }
        else
        {
            OutpostInfo = new SubmarineInfo(moduleFilePath.Value);
            if (OutpostInfo.SubmarineElement == null)
            {
                throw new Exception($"Error in content file {file.Path} (could not find the submarine file {moduleFilePath.Value}).");
            }
            FindGaps(OutpostInfo.SubmarineElement);
        }

        if (Price == 0)
        {
            //default to the price defined in the sub file if not set
            Price = OutpostInfo.Price;
        }

        List<ItemPrefab> newSwappableItems = [];
        List<ItemPrefab> newWorkstations = [];
        foreach (XElement itemElement in OutpostInfo.SubmarineElement.Elements())
        {
            if (itemElement.NameAsIdentifier() == nameof(Item))
            {
                Identifier itemIdentifier = itemElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                if (ItemPrefab.Prefabs.TryGet(itemIdentifier, out ItemPrefab? itemPrefab))
                {
                    if (itemElement.GetAttributeBool(nameof(Item.NonInteractable), false) ||
                        itemElement.GetAttributeBool(nameof(Item.HiddenInGame), false))
                    {
                        continue;
                    }
                    if (itemPrefab.SwappableItem != null)
                    {
                        newSwappableItems.Add(itemPrefab);
                    }
                    if (itemPrefab.Tags.Contains("workstation".ToIdentifier()))
                    {
                        newWorkstations.Add(itemPrefab);
                    }
                }
            }
        }
        swappableItems = newSwappableItems.ToImmutableList();
        workstations = newWorkstations.ToImmutableList();

        List<UpgradeResourceCost> newResourceCosts = [];
        foreach (var subElement in element.GetChildElements("ResourceCost"))
        {
            newResourceCosts.Add(new UpgradeResourceCost(subElement));
        }
        resourceCosts = newResourceCosts.ToImmutableList();

        foreach (var subElement in element.GetChildElements("RequiredResearch"))
        {
            RequiredResearch = subElement.GetAttributeIdentifier("Identifier", Identifier.Empty);
        }

        InitProjSpecific();
    }

    /// <summary>
    /// Initializes the <see cref="OutpostFacilities"/>, i.e. determines what kind of devices/installations are present on the module. 
    /// Must be done after all content has been loaded, so all the item prefabs that might be present in the module have been loaded and are available for use.
    /// </summary>
    public void InitializeFacilities()
    {
        OutpostFacilities = Option.Some(OutpostFacilityManager.ConstructFromOutpostElement(OutpostInfo.SubmarineElement, debugLogName: ToString()));
    }

    /// <summary>
    /// Finds gaps in the outpost module's XML file, and inserts them into the <see cref="Gaps"/> list.
    /// </summary>
    private void FindGaps(XElement element)
    {
        //the entities a sub consists of are automatically moved so the origin is at the center of the hulls' bounding box
        //we need to take that into account here when determining the positions of the gaps relative to the module
        var hullRects = new List<Rectangle>();
        var entityRects = new List<Rectangle>();
        foreach (var subElement in element.Elements())
        {
            Rectangle rect = subElement.GetAttributeRect("rect", Rectangle.Empty);
            if (rect == Rectangle.Empty) { continue; }
            if (subElement.NameAsIdentifier() == "Hull") 
            { 
                hullRects.Add(rect);
            }
            entityRects.Add(rect);
        }

        int minX = entityRects.Min(r => r.X);
        int minY = entityRects.Min(r => r.Y - r.Height);
        int maxX = entityRects.Max(r => r.Right);
        int maxY = entityRects.Max(r => r.Y);

        Bounds = new Rectangle(minX, minY, maxX - minX, maxY - minY);

        int minHullX = hullRects.Min(r => r.X);
        int minHullY = hullRects.Min(r => r.Y - r.Height);
        int maxHullX = hullRects.Max(r => r.Right);
        int maxHullY = hullRects.Max(r => r.Y);

        HullBounds = new Rectangle(minHullX, minHullY, maxHullX - minHullX, maxHullY - minHullY);        

        Vector2 center = new Vector2(minHullX + maxHullX, minHullY + maxHullY) / 2.0f;
        center.X -= center.X % Submarine.GridSize.X;
        center.Y -= center.Y % Submarine.GridSize.Y;

        foreach (var subElement in element.Elements())
        {
            if (subElement.NameAsIdentifier() != "Gap") { continue; }

            Rectangle gapRect = subElement.GetAttributeRect("rect", Rectangle.Empty);
            bool isHorizontal = gapRect.Width < gapRect.Height;
            const int tolerance = 1;

            Rectangle hull1, hull2;

            if (isHorizontal)
            {
                hull1 = hullRects.FirstOrDefault(r => Submarine.RectContains(r, new Vector2(gapRect.X - tolerance, gapRect.Y - gapRect.Height / 2)));
                hull2 = hullRects.FirstOrDefault(r => Submarine.RectContains(r, new Vector2(gapRect.Right + tolerance, gapRect.Y - gapRect.Height / 2)));
            }
            else
            {
                hull1 = hullRects.FirstOrDefault(r => Submarine.RectContains(r, new Vector2(gapRect.Center.X, gapRect.Y + tolerance)));
                hull2 = hullRects.FirstOrDefault(r => Submarine.RectContains(r, new Vector2(gapRect.Center.X, gapRect.Y - gapRect.Height - tolerance)));
            }

            //if there were no hulls on either side, or if there's hulls on both sides, this isn't a gap at the edge of the module
            if ((hull1 == default) == (hull2 == default)) { continue; }

            PlacedModule.GapInfo gap = new PlacedModule.GapInfo(subElement);
            Gaps.Add(new GapPositionInfo(
                new Rectangle(gapRect.Location - center.ToPoint(), gapRect.Size), 
                GetGapPosition(gapRect, hull1 != default ? hull1 : hull2),
                subElement.GetAttributeUInt16("id", Entity.NullEntityID)));
        }

        //remove the gaps that are linked to a door configured not to be used between outpost modules
        foreach (var subElement in element.Elements())
        {
            if (subElement.NameAsIdentifier() != "Item") { continue; }
            var doorElement = subElement.Element("Door");
            if (doorElement == null) { continue; }

            bool useBetweenOutpostModules = doorElement.GetAttributeBool(nameof(Door.UseBetweenOutpostModules), true);

            if (!useBetweenOutpostModules)
            {
                var linkedIds = subElement.GetAttributeUIntArray("linked", []);
                Gaps.RemoveAll(g => linkedIds.Contains(g.GapId));
            }
        }
    }

    public static OutpostModuleInfo.GapPosition GetGapPosition(Rectangle gapRect, Rectangle hullRect)
    {
        bool isHorizontal = gapRect.Width < gapRect.Height;
        if (isHorizontal)
        {
            return gapRect.Center.X > hullRect.Center.X / 2 ? OutpostModuleInfo.GapPosition.Right : OutpostModuleInfo.GapPosition.Left;
        }
        else
        {
            return gapRect.Y - gapRect.Height / 2 > hullRect.Y - hullRect.Height / 2 ? OutpostModuleInfo.GapPosition.Top : OutpostModuleInfo.GapPosition.Bottom;
        }
    }

    /// <summary>
    /// The <see cref="SubmarineInfo.DisplayName"/> of the module if a name has been defined for it in the text files,
    /// otherwise a generic "outpost module" text (instead of the file name <see cref="SubmarineInfo.DisplayName"/> would fall back to).
    /// </summary>
    public static LocalizedString GetDefaultDisplayName(SubmarineInfo outpostInfo)
    {
        if (TextManager.ContainsTag($"Submarine.Name.{outpostInfo.Name}"))
        {
            return outpostInfo.DisplayName;
        }
        return TextManager.Get("submarinetype.outpostmodule");
    }

    partial void InitProjSpecific();

    public override void Dispose() { }

    public override string ToString() => $"{nameof(PurchasableOutpostModulePrefab)} ({OutpostInfo?.Name ?? "null"})";
}
