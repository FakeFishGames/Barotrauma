using Barotrauma;
using System.Collections.Immutable;

namespace HomeAndHarbor
{
    public sealed class ResourcePrefab : Prefab
    {
        public readonly LocalizedString Name;
        public readonly LocalizedString Description;

        public readonly ContentXElement OriginalElement;

        public static readonly PrefabCollection<ResourcePrefab> Prefabs = [];

        /// <summary>
        /// The amounts of Readonly properties cannot be modified (even if the Resource in question is not a <see cref="ReadonlyResource"></see>
        /// </summary>
        public readonly bool Readonly;

        /// <summary>
        /// Should the resource be hidden from the player in menus etc?
        /// Note that this can be overriden by <see cref="Resource.Hidden"/>, and that should generally be used over this 
        /// (unless you're specifically interested in the prefab, or, whether the resource is hidden by default)
        /// </summary>
        public readonly bool Hidden;

        public readonly ImmutableHashSet<Identifier> LocationTypes;
        public readonly ImmutableHashSet<Identifier> Factions;

        public readonly int MenuOrder;

        public readonly bool ShowInResourcesUI;
        /// <summary>
        /// If set, this resource will be displayed in the resource UI using the name/icon of the specified resource.
        /// </summary>
        public readonly Identifier ShowInResourcesUIAs;

        /// <summary>
        /// Is this resource considered to "consist of its subresources", i.e. should changes to the subresources be logged as changes to this resource?
        /// Defaults to true, but can be disabled on resources such as Happiness (which is calculated based on various things, including population, but changes to population should not be considered changes to happiness)
        /// </summary>
        public readonly bool ConsistsOfSubResources;

        public readonly bool ShowItemsInResourcesUI;

        public readonly bool LogEvents;

        /// <summary>
        /// The order in which the resources spawn items (only relevant for resources that spawn items).
        /// </summary>
        public readonly int SpawnOrder;

        public readonly LocalizedString? DefaultAmountText;
        public readonly ImmutableArray<(float minAmount, LocalizedString text)> AmountTexts;

        /// <summary>
        /// How much should the resource decrease when an NPC dies? Dying in this context means an actual character instance dying during the round, not the population resource going down.
        /// </summary>
        public readonly float DecreaseOnNPCDeath;

        /// <summary>
        /// How long the decrease from NPC deaths should last. Only supported on for <see cref="VariableResource"/>.
        /// </summary>
        public readonly int DecreaseDurationOnNPCDeath;

#if CLIENT
        public readonly Sprite? Sprite;
#endif

        /// <summary>
        /// If this resource is shown on a tooltip this can be used to set
        /// the maximum value of the bar shown without affecting the generation.
        /// Can be either a constant value or a Identifier in which case the identifier
        /// will be used to fetch a resource and the value of that resource will be used as the max.
        /// </summary>
        public readonly Either<Identifier, float> SoftMaxValueInTooltip = Identifier.Empty;
        public readonly bool HasSoftMaxValueInTooltip;

        public ResourcePrefab(ContentFile file, ContentXElement element) : base(file, element.GetAttributeIdentifier("identifier", Identifier.Empty))
        {
            OriginalElement = element;
            string? name = element.GetAttributeString("name", null);
            if (string.IsNullOrWhiteSpace(name))
            {
                Name = TextManager
                       .Get($"resource.{Identifier}")
                       .Fallback(Identifier.Value);
            }
            else
            {
                Name = name;
            }

            if (element.GetAttribute(nameof(SoftMaxValueInTooltip)) is { } attribute)
            {
                HasSoftMaxValueInTooltip = true;
                SoftMaxValueInTooltip = attribute.GetAttributeFloatOrIdentifier();
            }

            string? description = element.GetAttributeString("description", null);

            LogEvents = element.GetAttributeBool("logevents", false);

            if (string.IsNullOrWhiteSpace(description))
            {
                Description = TextManager
                              .Get($"resource.{Identifier}.description")
                              .Fallback(string.Empty);
            }
            else
            {
                Description = description;
            }

            Readonly = element.GetAttributeBool(nameof(Readonly), false);
            Hidden = element.GetAttributeBool(nameof(Hidden), false);

            MenuOrder = element.GetAttributeInt(nameof(MenuOrder), 0);

            ShowInResourcesUIAs = element.GetAttributeIdentifier(nameof(ShowInResourcesUIAs), Identifier.Empty);
            ShowInResourcesUI = element.GetAttributeBool(nameof(ShowInResourcesUI), def: !ShowInResourcesUIAs.IsEmpty);
            ShowItemsInResourcesUI = element.GetAttributeBool(nameof(ShowItemsInResourcesUI), false);
            ConsistsOfSubResources = element.GetAttributeBool(nameof(ConsistsOfSubResources), true);

            SpawnOrder = element.GetAttributeInt(nameof(SpawnOrder), 0);

            LocationTypes = element.GetAttributeIdentifierImmutableHashSet(nameof(LocationTypes),
                def: element.GetAttributeIdentifierImmutableHashSet("locationtype", []));
            Factions = element.GetAttributeIdentifierImmutableHashSet("faction", []);

            DecreaseOnNPCDeath = element.GetAttributeFloat(nameof(DecreaseOnNPCDeath), 0f);
            DecreaseDurationOnNPCDeath = element.GetAttributeInt(nameof(DecreaseDurationOnNPCDeath), 0);

            List<(float minAmount, LocalizedString text)> amountTexts = [];
            foreach (string amountTextsString in element.GetAttributeStringArray(nameof(AmountTexts), def: []))
            {
                string[] splitAmountTextsString = amountTextsString.Split(':');
                switch (splitAmountTextsString.Length)
                {
                    case 1 when DefaultAmountText is null:
                        DefaultAmountText = splitAmountTextsString[0];
                        break;
                    case 2 when float.TryParse(splitAmountTextsString[0], out float minAmount):
                        string amountTextString = splitAmountTextsString[1];
                        amountTexts.Add((minAmount, TextManager.Get(amountTextString).Fallback(amountTextString)));
                        break;
                }
            }
            AmountTexts = [.. amountTexts.OrderByDescending(static pair => pair.minAmount)];

#if CLIENT
            foreach (ContentXElement subElement in element.Elements())
            {
                if (subElement.NameAsIdentifier() == "icon")
                {
                    Sprite = new Sprite(subElement);
                }
            }
#endif
        }

        public static ResourcePrefab? FindPrefab(Identifier identifier)
        {
            if (identifier.IsEmpty) { return null; }

            if (Prefabs.TryGet(identifier, out ResourcePrefab? prefab))
            {
                return prefab;
            }

            return null;
        }

        public bool AppliesToLocation(Location location)
        {
            if (LocationTypes.Count > 0 && !LocationTypes.Contains(location.Type.Identifier)) { return false; }
            if (Factions.Count > 0 && !Factions.Contains(location.Faction?.Prefab.Identifier ?? Identifier.Empty)) { return false; }
            return true;
        }

        public override void Dispose() { }
    }
}