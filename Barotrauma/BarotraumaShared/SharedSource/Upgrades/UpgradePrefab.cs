#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
    internal readonly struct UpgradePrice
    {
        public readonly int BasePrice;

        public readonly int IncreaseLow;

        public readonly int IncreaseHigh;

        public readonly UpgradePrefab Prefab;

        public UpgradePrice(UpgradePrefab prefab, XElement element)
        {
            Prefab = prefab;

            IncreaseLow = UpgradePrefab.ParsePercentage(element.GetAttributeString("increaselow", string.Empty),
                "IncreaseLow", element, suppressWarnings: prefab.SupressWarnings);

            IncreaseHigh = UpgradePrefab.ParsePercentage(element.GetAttributeString("increasehigh", string.Empty),
                "IncreaseHigh", element, suppressWarnings: prefab.SupressWarnings);

            BasePrice = element.GetAttributeInt("baseprice", -1);

            if (BasePrice == -1)
            {
                if (prefab.SupressWarnings)
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
                price += (int)(price * MathHelper.Lerp( IncreaseLow, IncreaseHigh, i / (float)Prefab.MaxLevel) / 100);
            }
            return location?.GetAdjustedMechanicalCost(price) ?? price;
        }
    }

    internal class UpgradeCategory
    {
        public static readonly List<UpgradeCategory> Categories = new List<UpgradeCategory>();

        public readonly string[] ItemTags;
        public readonly string Identifier;
        public readonly bool IsWallUpgrade;
        public readonly string Name;

        public UpgradeCategory(XElement element)
        {
            ItemTags = element.GetAttributeStringArray("items", new string[] { });
            Identifier = element.GetAttributeString("identifier", string.Empty);
            Name = element.GetAttributeString("name", string.Empty);
            IsWallUpgrade = element.GetAttributeBool("wallupgrade", false);

            if (string.IsNullOrWhiteSpace(Name))
            {
                Name = TextManager.Get($"UpgradeCategory.{Identifier}", true) ?? string.Empty;
            }

            foreach (ItemPrefab itemPrefab in ItemPrefab.Prefabs)
            {
                string[] identifierArray = itemPrefab.AllowedUpgrades.Split(",");
                if (identifierArray.Contains(Identifier))
                {
                    ItemTags = ItemTags.Concat(new[] { itemPrefab.Identifier }).ToArray();
                }
            }

            Categories.Add(this);
        }

        public bool CanBeApplied(Item item, UpgradePrefab? upgradePrefab = null)
        {
            if (IsWallUpgrade) return false;

            if (upgradePrefab != null && item.disallowedUpgrades.Contains(upgradePrefab.Identifier)) return false;

            return item.prefab.GetAllowedUpgrades().Contains(Identifier) ||
                   ItemTags.Any(tag => item.Prefab.Tags.HasTag(tag) || item.Prefab.MapEntityIdentifier == tag);
        }
        
        public bool CanBeApplied(XElement element)
        {
            if (string.Equals("Structure", element.Name.ToString(), StringComparison.OrdinalIgnoreCase)) return IsWallUpgrade;

            string identifier = element.GetAttributeString("identifier", string.Empty);
            if (string.IsNullOrWhiteSpace(identifier)) return false;

            ItemPrefab? item = ItemPrefab.Find(null, identifier);
            if (item == null) return false;

            return item.GetAllowedUpgrades().Contains(Identifier) || 
                   ItemTags.Any(tag => item.Tags.HasTag(tag) || item.MapEntityIdentifier == tag);
        }

        public static UpgradeCategory? Find(string idenfitier)
        {
            return !string.IsNullOrWhiteSpace(idenfitier) ? Categories.Find(category => string.Equals(category.Identifier, idenfitier, StringComparison.OrdinalIgnoreCase)) : null;
        }
    }

    internal partial class UpgradePrefab : IPrefab, IDisposable
    {
        public static readonly PrefabCollection<UpgradePrefab> Prefabs = new PrefabCollection<UpgradePrefab>();

        public int MaxLevel { get; }

        public string OriginalName { get; }

        public string Name { get; }

        public string Description { get; }

        public string Identifier { get; }

        public string FilePath { get; }

        public UpgradeCategory[] UpgradeCategories { get; }

        public UpgradePrice Price { get; }

        public ContentPackage? ContentPackage { get; private set; }

        private bool IsOverride { get; }

        public XElement SourceElement { get; }

        private bool Disposed { get; set; }

        public bool SupressWarnings { get; }

        public bool HideInMenus { get; }

        public IEnumerable<string> TargetItems => UpgradeCategories.SelectMany(u => u.ItemTags);

        public bool IsWallUpgrade => UpgradeCategories.All(u => u.IsWallUpgrade);

        private Dictionary<string, string[]> TargetProperties { get; }

        private UpgradePrefab(XElement element, string filePath, bool isOverride)
        {
            Name = element.GetAttributeString("name", string.Empty);
            Description = element.GetAttributeString("description", string.Empty);
            MaxLevel = element.GetAttributeInt("maxlevel", 1);
            Identifier = element.GetAttributeString("identifier", "");
            SupressWarnings = element.GetAttributeBool("supresswarnings", false);
            HideInMenus = element.GetAttributeBool("hideinmenus", false);
            FilePath = filePath;
            SourceElement = element;
            IsOverride = isOverride;
            OriginalName = Name;

            var targetProperties = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(Name))
            {
                Name = TextManager.Get($"UpgradeName.{Identifier}", returnNull: true) ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(Description))
            {
                Description = TextManager.Get($"UpgradeDescription.{Identifier}", returnNull: true) ?? string.Empty;
            }

            DebugConsole.Log("    " + Name);

            foreach (XElement subElement in element.Elements())
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
                        DecorativeSprites.Add(new DecorativeSprite(subElement));
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

            TargetProperties = targetProperties;

            string[] categories = element.GetAttributeStringArray("categories", new string[] { });
            UpgradeCategories = (from category in UpgradeCategory.Categories from identifier in categories where string.Equals(category.Identifier, identifier) select category).ToArray();

            if (!SupressWarnings && !IsOverride)
            {
                foreach (UpgradePrefab matchingPrefab in Prefabs.Where(prefab => prefab.TargetItems.Any(s => TargetItems.Contains(s))))
                {
                    if (matchingPrefab.IsOverride) { continue; }

                    var upgradePrefab = matchingPrefab.TargetProperties;
                    string key = string.Empty;

                    if (upgradePrefab.Keys.Any(s => TargetProperties.Keys.Any(s1 => s == (key = s1))))
                    {
                        if (upgradePrefab.ContainsKey(key) && upgradePrefab[key].Any(s => TargetProperties[key].Contains(s)))
                        {
                            DebugConsole.AddWarning($"Upgrade \"{Identifier}\" is affecting a property that is also being affected by \"{matchingPrefab.Identifier}\".\n" +
                                                    "This is unsupported and might yield unexpected results if both upgrades are applied at the same time to the same item.\n" +
                                                    "Add the attribute suppresswarnings=\"true\" to your XML element to disable this warning if you know what you're doing.");
                        }
                    }
                }
            }

            Prefabs.Add(this, isOverride);
        }

        public static UpgradePrefab? Find(string idenfitier)
        {
            return !string.IsNullOrWhiteSpace(idenfitier) ? Prefabs.Find(prefab => prefab.Identifier == idenfitier) : null;
        }

        public static void LoadAll(IEnumerable<ContentFile> files)
        {
            DebugConsole.Log("Loading upgrade module prefabs: ");

            foreach (ContentFile file in files) { LoadFromFile(file); }
        }

        private static void LoadFromFile(ContentFile file)
        {
            XDocument doc = XMLExtensions.TryLoadXml(file.Path);

            var rootElement = doc?.Root;
            if (rootElement == null) { return; }

            switch (rootElement.Name.ToString().ToLowerInvariant())
            {
                case "upgrademodule":
                {
                    new UpgradePrefab(rootElement, file.Path, false) { ContentPackage = file.ContentPackage };
                    break;
                }
                case "upgradecategory":
                {
                    new UpgradeCategory(rootElement);
                    break;
                }
                case "upgrademodules":
                {
                    foreach (var element in rootElement.Elements())
                    {
                        if (element.IsOverride())
                        {
                            var upgradeElement = element.GetChildElement("upgradeprefab");
                            if (upgradeElement != null)
                            {
                                new UpgradePrefab(upgradeElement, file.Path, true) { ContentPackage = file.ContentPackage };
                            }
                            else
                            {
                                DebugConsole.ThrowError($"Cannot find an upgrade element from the children of the override element defined in {file.Path}");
                            }
                        }
                        else
                        {
                            switch (element.Name.ToString().ToLowerInvariant())
                            {
                                case "upgrademodule":
                                {
                                    new UpgradePrefab(element, file.Path, false) { ContentPackage = file.ContentPackage };
                                    break;
                                }
                                case "upgradecategory":
                                {
                                    new UpgradeCategory(element);
                                    break;
                                }
                            }
                        }
                    }

                    break;
                }
                case "override":
                {
                    var upgrades = rootElement.GetChildElement("upgrademodules");
                    if (upgrades != null)
                    {
                        foreach (var element in upgrades.Elements())
                        {
                            new UpgradePrefab(element, file.Path, true) { ContentPackage = file.ContentPackage };
                        }
                    }

                    foreach (var element in rootElement.GetChildElements("upgrademodule"))
                    {
                        new UpgradePrefab(element, file.Path, true) { ContentPackage = file.ContentPackage };
                    }

                    break;
                }
                default:
                    DebugConsole.ThrowError($"Invalid XML root element: '{rootElement.Name}' in {file.Path}\n " +
                                            "Valid elements are: \"UpgradeModule\", \"UpgradeModules\" and \"Override\".");
                    break;
            }
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
        public static int ParsePercentage(string value, string? attribute = null, XElement? sourceElement = null, bool suppressWarnings = false)
        {
            string? line = sourceElement?.ToString().Split('\n')[0].Trim();
            bool doWarnings = !suppressWarnings && attribute != null && sourceElement != null && line != null;

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
            if (!Disposed)
            {
                if (disposing)
                {
                    Prefabs.Remove(this);
#if CLIENT
                    Sprite.Remove();
                    Sprite = null;
                    DecorativeSprites.ForEach(sprite => sprite.Remove());
                    DecorativeSprites.Clear();
                    TargetProperties.Clear();
#endif
                }
            }

            Disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}