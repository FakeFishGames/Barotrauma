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

        public int GetBuyPrice(int level, Location? location = null, ImmutableHashSet<Character>? characterList = null)
        {
            int maxLevel = Prefab.GetMaxLevelForCurrentSub();

            if (level > maxLevel) { maxLevel = level; }

            float price = BasePrice;
            price += price * MathHelper.Lerp(IncreaseLow, IncreaseHigh, level / (float)maxLevel) / 100f;
            price = location?.GetAdjustedMechanicalCost((int)price) ?? price;

            characterList ??= GameSession.GetSessionCrewCharacters(CharacterType.Both);

            if (characterList.Any())
            {
                if (location?.Reputation is { } reputation && Faction.GetPlayerAffiliationStatus(reputation.Identifier, characterList) is FactionAffiliation.Positive)
                {
                    price *= 1f - characterList.Max(static c => c.GetStatValue(StatTypes.ShipyardBuyMultiplierAffiliated));
                }
                price *= 1f - characterList.Max(static c => c.GetStatValue(StatTypes.ShipyardBuyMultiplier));
            }

            return (int)price;
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

        private readonly object mutex = new object();

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
            lock (mutex)
            {
                prefabsThatAllowUpgrades.Clear();
                prefabsThatAllowUpgrades.UnionWith(ItemPrefab.Prefabs
                    .Where(it => it.GetAllowedUpgrades().Contains(Identifier))
                    .Select(it => it.Identifier));
            }
        }

        public bool CanBeApplied(MapEntity item, UpgradePrefab? upgradePrefab)
        {
            if (upgradePrefab != null && item.Submarine is { Info: var info } && !upgradePrefab.IsApplicable(info)) { return false; }

            bool isStructure = item is Structure;
            switch (IsWallUpgrade)
            {
                case true:
                    return isStructure;
                case false when isStructure:
                    return false;
            }

            if (upgradePrefab != null && upgradePrefab.IsDisallowed(item)) { return false; }

            lock (mutex)
            {
                return item.Prefab.GetAllowedUpgrades().Contains(Identifier) ||
                       ItemTags.Any(tag => item.Prefab.Tags.Contains(tag) || item.Prefab.Identifier == tag);
            }
        }

        public static UpgradeCategory? Find(Identifier identifier)
        {
            return !identifier.IsEmpty ? Categories.Find(category => category.Identifier == identifier) : null;
        }

        public override void Dispose() { }
    }

    internal readonly struct UpgradeMaxLevelMod
    {
        private enum MaxLevelModType
        {
            Invalid,
            Increase,
            Set
        }

        private readonly Either<SubmarineClass, int> tierOrClass;
        private readonly int value;
        private readonly MaxLevelModType type;

        public int GetLevelAfter(int level) =>
            type switch
            {
                MaxLevelModType.Invalid => level,
                MaxLevelModType.Increase => level + value,
                MaxLevelModType.Set => value,
                _ => throw new ArgumentOutOfRangeException()
            };

        public bool AppliesTo(SubmarineInfo sub)
        {
            if (type is MaxLevelModType.Invalid) { return false; }

            int subTier = sub.Tier;
            if (GameMain.GameSession?.Campaign?.CampaignMetadata is { } metadata)
            {
                int modifier = metadata.GetInt(new Identifier("tiermodifieroverride"), 0);

                subTier = Math.Max(modifier, subTier);
            }

            if (tierOrClass.TryGet(out int tier))
            {
                return subTier == tier;
            }

            if (tierOrClass.TryGet(out SubmarineClass subClass))
            {
                return sub.SubmarineClass == subClass;
            }

            return false;
        }

        public UpgradeMaxLevelMod(ContentXElement element)
        {
            bool isValid = true;

            SubmarineClass subClass = element.GetAttributeEnum("class", SubmarineClass.Undefined);
            int tier = element.GetAttributeInt("tier", 0);
            if (subClass != SubmarineClass.Undefined)
            {
                tierOrClass = subClass;
            }
            else
            {
                tierOrClass = tier;
            }

            string stringValue = element.GetAttributeString("level", null) ?? string.Empty;
            value = 0;

            if (string.IsNullOrWhiteSpace(stringValue)) { isValid = false; }

            char firstChar = stringValue[0];

            if (!int.TryParse(stringValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var intValue)) { isValid = false; }
            value = intValue;

            if (firstChar.Equals('+') || firstChar.Equals('-'))
            {
                type = MaxLevelModType.Increase;
            }
            else
            {
                type = MaxLevelModType.Set;
            }

            if (!isValid) { type = MaxLevelModType.Invalid; }
        }
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

        /// <summary>
        /// Maximum upgrade level without taking submarine tier or class restrictions into account
        /// </summary>
        public readonly int MaxLevel;

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

        public bool SuppressWarnings { get; }

        public bool HideInMenus { get; }

        public IEnumerable<Identifier> TargetItems => UpgradeCategories.SelectMany(u => u.ItemTags);

        public bool IsWallUpgrade => UpgradeCategories.All(u => u.IsWallUpgrade);

        private Dictionary<string, string[]> targetProperties { get; }
        private readonly ImmutableArray<UpgradeMaxLevelMod> MaxLevelsMods;

        public UpgradePrefab(ContentXElement element, UpgradeModulesFile file) : base(element, file)
        {
            Name = element.GetAttributeString("name", string.Empty)!;
            Description = element.GetAttributeString("description", string.Empty)!;
            MaxLevel = element.GetAttributeInt("maxlevel", 1);
            SuppressWarnings = element.GetAttributeBool("supresswarnings", false);
            HideInMenus = element.GetAttributeBool("hideinmenus", false);
            SourceElement = element;

            var targetProperties = new Dictionary<string, string[]>();
            var maxLevels = new List<UpgradeMaxLevelMod>();

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
                    case "maxlevel":
                    {
                        maxLevels.Add(new UpgradeMaxLevelMod(subElement));
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
            MaxLevelsMods = maxLevels.ToImmutableArray();

            upgradeCategoryIdentifiers = element.GetAttributeIdentifierArray("categories", Array.Empty<Identifier>())?
                .ToImmutableHashSet() ?? ImmutableHashSet<Identifier>.Empty;
        }

        /// <summary>
        /// Returns the maximum upgrade level for the current sub, taking tier and class restrictions into account
        /// </summary>
        public int GetMaxLevelForCurrentSub()
        {
            Submarine? sub = GameMain.GameSession?.Submarine ?? Submarine.MainSub;
            return sub is { Info: var info } ? GetMaxLevel(info) : MaxLevel;
        }

        /// <summary>
        /// Returns the maximum upgrade level for the specified sub, taking tier and class restrictions into account
        /// </summary>
        public int GetMaxLevel(SubmarineInfo info)
        {
            int level = MaxLevel;

            foreach (UpgradeMaxLevelMod mod in MaxLevelsMods)
            {
                if (mod.AppliesTo(info)) { level = mod.GetLevelAfter(level); }
            }

            if (GameMain.GameSession?.Campaign?.CampaignMetadata is { } metadata)
            {
                int modifier = metadata.GetInt(new Identifier($"tiermodifiers.{Identifier}"), 0);
                level += modifier;
            }

            return level;
        }

        public bool IsApplicable(SubmarineInfo? info)
        {
            if (info is null) { return false; }

            return GetMaxLevel(info) > 0;
        }

        public bool IsDisallowed(MapEntity item)
        {
            return item.DisallowedUpgradeSet.Contains(Identifier)
                   || UpgradeCategories.Any(c => item.DisallowedUpgradeSet.Contains(c.Identifier));
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

        public override void Dispose()
        {
#if CLIENT
            Sprite?.Remove();
            Sprite = null;
            DecorativeSprites.ForEach(sprite => sprite.Remove());
            targetProperties.Clear();
#endif
        }
    }
}