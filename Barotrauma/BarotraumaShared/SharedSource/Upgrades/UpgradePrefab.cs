#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal readonly struct UpgradePrice
    {
        public readonly int BasePrice;

        public readonly int IncreaseLow;

        public readonly int IncreaseHigh;

        public readonly UpgradePrefab Prefab;

        public UpgradePrice(UpgradePrefab prefab, ContentXElement element)
        {
            Prefab = prefab;

            IncreaseLow = UpgradePrefab.ParsePercentage(element.GetAttributeString("increaselow", string.Empty)!,
                "IncreaseLow".ToIdentifier(), element, suppressWarnings: prefab.SuppressWarnings);

            IncreaseHigh = UpgradePrefab.ParsePercentage(element.GetAttributeString("increasehigh", string.Empty)!,
                "IncreaseHigh".ToIdentifier(), element, suppressWarnings: prefab.SuppressWarnings);

            BasePrice = element.GetAttributeInt("baseprice", -1);

            if (BasePrice == -1)
            {
                if (prefab.SuppressWarnings)
                {
                    DebugConsole.AddWarning($"Price attribute \"baseprice\" is not defined for {prefab?.Identifier}.\n " +
                                            "The value has been assumed to be '1000'.");
                    BasePrice = 1000;
                }
            }
        }

        public int GetBuyprice(int level, Location? location = null)
        {
            int price = BasePrice;
            for (int i = 1; i <= level; i++)
            {
                price += (int)(price * MathHelper.Lerp(IncreaseLow, IncreaseHigh, i / (float)Prefab.MaxLevel) / 100);
            }
            return location?.GetAdjustedMechanicalCost(price) ?? price;
        }
    }

    abstract class UpgradeContentPrefab : Prefab
    {
        public static readonly PrefabCollection<UpgradeContentPrefab> PrefabsAndCategories = new PrefabCollection<UpgradeContentPrefab>(
            onAdd: (prefab, isOverride) =>
            {
                if (prefab is UpgradePrefab upgradePrefab)
                {
                    UpgradePrefab.Prefabs.Add(upgradePrefab, isOverride);
                }
                else if (prefab is UpgradeCategory upgradeCategory)
                {
                    UpgradeCategory.Categories.Add(upgradeCategory, isOverride);
                }
            },
            onRemove: (prefab) =>
            {
                if (prefab is UpgradePrefab upgradePrefab)
                {
                    UpgradePrefab.Prefabs.Remove(upgradePrefab);
                }
                else if (prefab is UpgradeCategory upgradeCategory)
                {
                    UpgradeCategory.Categories.Remove(upgradeCategory);
                }
            },
            onSort: () =>
            {
                UpgradePrefab.Prefabs.SortAll();
                UpgradeCategory.Categories.SortAll();
            },
            onAddOverrideFile: (file) =>
            {
                UpgradePrefab.Prefabs.AddOverrideFile(file);
                UpgradeCategory.Categories.AddOverrideFile(file);
            },
            onRemoveOverrideFile: (file) =>
            {
                UpgradePrefab.Prefabs.RemoveOverrideFile(file);
                UpgradeCategory.Categories.RemoveOverrideFile(file);
            });

        public UpgradeContentPrefab(ContentXElement element, UpgradeModulesFile file) : base(file, element) { }
    }

    internal class UpgradeCategory : UpgradeContentPrefab
    {
        public static readonly PrefabCollection<UpgradeCategory> Categories = new PrefabCollection<UpgradeCategory>();

        private readonly ImmutableHashSet<Identifier> selfItemTags;
        private readonly HashSet<Identifier> prefabsThatAllowUpgrades = new HashSet<Identifier>();
        public readonly bool IsWallUpgrade;
        public readonly LocalizedString Name;

        public readonly IEnumerable<Identifier> ItemTags;
        
        public UpgradeCategory(ContentXElement element, UpgradeModulesFile file) : base(element, file)
        {
            selfItemTags = element.GetAttributeIdentifierArray("items", Array.Empty<Identifier>())?.ToImmutableHashSet() ?? ImmutableHashSet<Identifier>.Empty;
            Name = element.GetAttributeString("name", string.Empty)!;
            IsWallUpgrade = element.GetAttributeBool("wallupgrade", false);

            ItemTags = selfItemTags.CollectionConcat(prefabsThatAllowUpgrades);

            Identifier nameIdentifier = element.GetAttributeIdentifier("nameidentifier", Identifier.Empty);

            if (!nameIdentifier.IsEmpty)
            {
                Name = TextManager.Get($"{nameIdentifier}");
            }
            else if (Name.IsNullOrWhiteSpace())
            {
                Name = TextManager.Get($"UpgradeCategory.{Identifier}");
            }
        }

        public void DeterminePrefabsThatAllowUpgrades()
        {
            prefabsThatAllowUpgrades.Clear();
            prefabsThatAllowUpgrades.UnionWith(ItemPrefab.Prefabs
                .Where(it => it.GetAllowedUpgrades().Contains(Identifier))
                .Select(it => it.Identifier));
        }

        public bool CanBeApplied(Item item, UpgradePrefab? upgradePrefab)
        {
            if (IsWallUpgrade) { return false; }

            if (upgradePrefab != null && upgradePrefab.IsDisallowed(item)) { return false; }

            return ((MapEntity)item).Prefab.GetAllowedUpgrades().Contains(Identifier) ||
                   ItemTags.Any(tag => item.Prefab.Tags.Contains(tag) || item.Prefab.Identifier == tag);
        }

        public bool CanBeApplied(XElement element, UpgradePrefab prefab)
        {
            if ("Structure" == element.NameAsIdentifier()) { return IsWallUpgrade; }

            Identifier identifier = element.GetAttributeIdentifier("identifier", Identifier.Empty);
            if (identifier.IsEmpty) { return false; }

            ItemPrefab? item = ItemPrefab.Find(null, identifier);
            if (item == null) { return false; }

            Identifier[] disallowedUpgrades = element.GetAttributeIdentifierArray("disallowedupgrades", Array.Empty<Identifier>());

            if (disallowedUpgrades.Any(s => s == Identifier || s == prefab.Identifier)) { return false; }

            return item.GetAllowedUpgrades().Contains(Identifier) || 
                   ItemTags.Any(tag => item.Tags.Contains(tag) || item.Identifier == tag);
        }

        public static UpgradeCategory? Find(Identifier identifier)
        {
            return !identifier.IsEmpty ? Categories.Find(category => category.Identifier == identifier) : null;
        }

        public override void Dispose() { }
    }

    internal partial class UpgradePrefab : UpgradeContentPrefab
    {
        public static readonly PrefabCollection<UpgradePrefab> Prefabs = new PrefabCollection<UpgradePrefab>(
            onAdd: (prefab, isOverride) =>
            {
                if (!prefab.SuppressWarnings && !isOverride)
                {
                    foreach (UpgradePrefab matchingPrefab in Prefabs?.Where(p => p != prefab && p.TargetItems.Any(s => prefab.TargetItems.Contains(s))) ?? throw new NullReferenceException("Honestly I have no clue why this could be null..."))
                    {
                        if (matchingPrefab.isOverride) { continue; }

                        var upgradePrefab = matchingPrefab.targetProperties;
                        string key = string.Empty;

                        if (upgradePrefab.Keys.Any(s => prefab.targetProperties.Keys.Any(s1 => s == (key = s1))))
                        {
                            if (upgradePrefab.ContainsKey(key) && upgradePrefab[key].Any(s => prefab.targetProperties[key].Contains(s)))
                            {
                                DebugConsole.AddWarning($"Upgrade \"{prefab.Identifier}\" is affecting a property that is also being affected by \"{matchingPrefab.Identifier}\".\n" +
                                                        "This is unsupported and might yield unexpected results if both upgrades are applied at the same time to the same item.\n" +
                                                        "Add the attribute suppresswarnings=\"true\" to your XML element to disable this warning if you know what you're doing.");
                            }
                        }
                    }
                }
            },
            onRemove: null,
            onSort: null,
            onAddOverrideFile: null,
            onRemoveOverrideFile: null
        );

        public int MaxLevel { get; }

        public LocalizedString Name { get; }
        
        public LocalizedString Description { get; }

        public float IncreaseOnTooltip { get; }

        private readonly ImmutableHashSet<Identifier> upgradeCategoryIdentifiers;

        public IEnumerable<UpgradeCategory> UpgradeCategories
        {
            get
            {
                foreach (var id in upgradeCategoryIdentifiers)
                {
                    if (UpgradeCategory.Categories.TryGet(id, out var category)) { yield return category!; }
                }
            }
        }

        public UpgradePrice Price { get; }

        private bool isOverride => Prefabs.IsOverride(this);

        public ContentXElement SourceElement { get; }

        private bool disposed;

        public bool SuppressWarnings { get; }

        public bool HideInMenus { get; }

        public IEnumerable<Identifier> TargetItems => UpgradeCategories.SelectMany(u => u.ItemTags);

        public bool IsWallUpgrade => UpgradeCategories.All(u => u.IsWallUpgrade);

        private Dictionary<string, string[]> targetProperties { get; }

        public UpgradePrefab(ContentXElement element, UpgradeModulesFile file) : base(element, file)
        {
            Name = element.GetAttributeString("name", string.Empty)!;
            Description = element.GetAttributeString("description", string.Empty)!;
            MaxLevel = element.GetAttributeInt("maxlevel", 1);
            SuppressWarnings = element.GetAttributeBool("supresswarnings", false);
            HideInMenus = element.GetAttributeBool("hideinmenus", false);
            SourceElement = element;

            var targetProperties = new Dictionary<string, string[]>();

            Identifier nameIdentifier = element.GetAttributeIdentifier("nameidentifier", "");
            if (!nameIdentifier.IsEmpty)
            {
                Name = TextManager.Get($"UpgradeName.{nameIdentifier}");
            }
            else if (Name.IsNullOrWhiteSpace())
            {
                Name = TextManager.Get($"UpgradeName.{Identifier}");
            }

            Identifier descriptionIdentifier = element.GetAttributeIdentifier("descriptionidentifier", "");
            if (!descriptionIdentifier.IsEmpty)
            {
                Description = TextManager.Get($"UpgradeDescription.{descriptionIdentifier}");
            }
            else if (Description.IsNullOrWhiteSpace())
            {
                Description = TextManager.Get($"UpgradeDescription.{Identifier}");
            }

            IncreaseOnTooltip = element.GetAttributeFloat("increaseontooltip", 0f);

            DebugConsole.Log("    " + Name);

#if CLIENT
            var decorativeSprites = new List<DecorativeSprite>();
#endif
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "price":
                    {
                        Price = new UpgradePrice(this, subElement);
                        break;
                    }
#if CLIENT
                    case "decorativesprite":
                    {
                        decorativeSprites.Add(new DecorativeSprite(subElement));
                        break;
                    }
                    case "sprite":
                    {
                        Sprite = new Sprite(subElement);
                        break;
                    }
#else
                    case "decorativesprite":
                    case "sprite":
                        break;
#endif
                    default:
                    {
                        IEnumerable<string> properties = subElement.Attributes().Select(attribute => attribute.Name.ToString());
                        targetProperties.Add(subElement.Name.ToString(), properties.ToArray());
                        break;
                    }
                }
            }

#if CLIENT
            DecorativeSprites = decorativeSprites.ToImmutableArray();
#endif

            this.targetProperties = targetProperties;

            upgradeCategoryIdentifiers = element.GetAttributeIdentifierArray("categories", Array.Empty<Identifier>())?
                .ToImmutableHashSet() ?? ImmutableHashSet<Identifier>.Empty;
        }

        public bool IsDisallowed(Item item)
        {
            return item.DisallowedUpgradeSet.Contains(Identifier) || UpgradeCategories.Any(c => item.DisallowedUpgradeSet.Contains(c.Identifier));
        }

        public static UpgradePrefab? Find(Identifier identifier)
        {
            return identifier != Identifier.Empty ? Prefabs.Find(prefab => prefab.Identifier == identifier) : null;
        }

        /// <summary>
        /// Parse a integer value from a string that is formatted like a percentage increase / decrease.
        /// </summary>
        /// <param name="value">String to parse</param>
        /// <param name="attribute">What XML attribute the value originates from, only used for warning formatting.</param>
        /// <param name="sourceElement">What XMLElement the value originates from, only used for warning formatting.</param>
        /// <param name="suppressWarnings">Whether or not to suppress warnings if both "attribute" and "sourceElement" are defined.</param>
        /// <returns></returns>
        /// <example>
        /// This sample returns -15 as an integer.
        /// <code>
        /// XElement element = new XElement("change", new XAttribute("increase", "-15%"));
        /// ParsePercentage(element.GetAttributeString("increase", string.Empty));
        /// </code>
        /// </example>
        public static int ParsePercentage(string value, Identifier attribute = default, XElement? sourceElement = null, bool suppressWarnings = false)
        {
            string? line = sourceElement?.ToString().Split('\n')[0].Trim();
            bool doWarnings = !suppressWarnings && !attribute.IsEmpty && sourceElement != null && line != null;

            if (string.IsNullOrWhiteSpace(value))
            {
                if (doWarnings)
                {
                    DebugConsole.AddWarning($"Attribute \"{attribute}\" not found at {sourceElement!.Document?.ParseContentPathFromUri()} @ '{line}'.\n " +
                                            "Value has been assumed to be '0'.");
                }

                return 1;
            }

            if (!int.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            {
                string str = value;

                if (str.Length > 1 && str[0] == '+') { str = str.Substring(1); }

                if (str.Length > 1 && str[^1] == '%') { str = str.Substring(0, str.Length - 1); }

                if (int.TryParse(str, out price))
                {
                    return price;
                }
            }
            else
            {
                return price;
            }

            if (doWarnings)
            {
                DebugConsole.AddWarning($"Value in attribute \"{attribute}\" is not formatted correctly\n " +
                                        $"at {sourceElement!.Document?.ParseContentPathFromUri()} @ '{line}'.\n " +
                                        "It should be an integer with optionally a '+' or '-' at the front and/or '%' at the end.\n" +
                                        "The value has been assumed to be '0'.");
            }

            return 1;
        }

        private void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    Prefabs.Remove(this);
#if CLIENT
                    Sprite?.Remove();
                    Sprite = null;
                    DecorativeSprites.ForEach(sprite => sprite.Remove());
                    targetProperties.Clear();
#endif
                }
            }

            disposed = true;
        }

        public override void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}