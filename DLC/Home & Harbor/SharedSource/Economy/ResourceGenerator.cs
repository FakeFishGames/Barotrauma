using Barotrauma;
using Barotrauma.Items.Components;
using HomeAndHarbor.Resources;
using Microsoft.Xna.Framework;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace HomeAndHarbor
{
    internal class ResourceGenerator : ISerializableEntity
    {
        /// <summary>
        /// A conversion from one or more items in <see cref="ResourceIdentifier"/> to one or more items in <see cref="OutputResource"/>.
        /// All consumed items must belong to the same input resource, and all produced items to the same output resource.
        /// </summary>
        public readonly struct ConvertedItem
        {
            /// <param name="Item">Identifier of the item.</param>
            /// <param name="Amount">Base amount consumed/produced.</param>
            /// <param name="AmountAtMinSkill">Amount used instead of <see cref="Amount"/> at skill level 0.
            /// If set together with <see cref="AmountAtMaxSkill"/>, overrides <see cref="Amount"/> with an interpolated value.</param>
            /// <param name="AmountAtMaxSkill">Amount used instead of <see cref="Amount"/> at skill level 100. NOTE: Linear scaling continues beyond this if skill is higher than 100!
            /// If set together with <see cref="AmountAtMinSkill"/>, overrides <see cref="Amount"/> with an interpolated value.</param>
            /// <param name="DisplayNameTag">Optional text tag used as the display name in the UI instead of the item's/tag's name.
            /// Useful when <see cref="Item"/> is a tag that doesn't have a name of its own.</param>
            public readonly record struct ItemProduction(Identifier Item, float Amount, float? AmountAtMinSkill, float? AmountAtMaxSkill, string? DisplayNameTag)
            {
                /// <summary>
                /// Returns <see cref="Amount"/>, or a value interpolated between <see cref="AmountAtMinSkill"/> and <see cref="AmountAtMaxSkill"/> if both are defined.
                /// </summary>
                public float GetScaledAmount(float skillLevel)
                {
                    return AmountAtMinSkill.HasValue && AmountAtMaxSkill.HasValue
                        ? MathHelper.Lerp(AmountAtMinSkill.Value, AmountAtMaxSkill.Value, skillLevel / 100f)
                        : Amount;
                }

                public static ItemProduction FromXml(XElement element, string parentDebugName)
                {
                    Identifier item = element.GetAttributeIdentifier("item", Identifier.Empty);
                    float amount = element.GetAttributeFloat("amount", 1f);

                    float? amountAtMinSkill = element.GetAttributeNullableFloat("AmountAtMinSkill");
                    float? amountAtMaxSkill = element.GetAttributeNullableFloat("AmountAtMaxSkill");
                    string? displayNameTag = element.GetAttributeString(nameof(DisplayNameTag), null);

                    if (amountAtMinSkill.HasValue != amountAtMaxSkill.HasValue)
                    {
                        DebugConsole.AddWarning($"Invalid {nameof(ItemProduction)} in {parentDebugName}: both '{nameof(AmountAtMinSkill)}' and '{nameof(AmountAtMaxSkill)}' must be defined together.");
                    }
                    if (item.IsEmpty)
                    {
                        DebugConsole.ThrowError($"Invalid {nameof(ItemProduction)} in {parentDebugName}: no 'item' identifier specified.");
                    }

                    return new ItemProduction(item, amount, amountAtMinSkill, amountAtMaxSkill, displayNameTag);
                }
            }

            public readonly record struct Requirement(Identifier Item, PropertyConditional.ComparisonOperatorType Op, float Comparison);

            /// <summary>The resource the consumed items are taken from.</summary>
            public readonly Identifier ResourceIdentifier;

            /// <summary>The resource the produced items are added to. If empty, defaults to <see cref="ResourceIdentifier"/>.</summary>
            public readonly Identifier OutputResource;

            /// <summary>Items consumed by this conversion. Must all belong to <see cref="ResourceIdentifier"/>.</summary>
            public readonly ImmutableArray<ItemProduction> ConsumedItems;

            /// <summary>Items produced by this conversion. Must all belong to <see cref="OutputResource"/>.</summary>
            public readonly ImmutableArray<ItemProduction> ProducedItems;

            /// <summary>Requirements based on the target ItemResource's accumulated items.</summary>
            public readonly ImmutableArray<Requirement> Requirements;

            /// <summary>Whether the conversion runs when the player hasn't explicitly enabled or disabled it, see <see cref="ConversionState.Default"/>. Defaults to true.</summary>
            public readonly bool EnabledByDefault;

            public ConvertedItem(
                Identifier resourceIdentifier,
                Identifier outputResource,
                ImmutableArray<ItemProduction> consumedItems,
                ImmutableArray<ItemProduction> producedItems,
                ImmutableArray<Requirement> requirements,
                bool enabledByDefault)
            {
                ResourceIdentifier = resourceIdentifier;
                OutputResource = outputResource;
                ConsumedItems = consumedItems;
                ProducedItems = producedItems;
                Requirements = requirements;
                EnabledByDefault = enabledByDefault;
            }

            public bool HasRequiredConsumedItems(ResourceManager? resourceManager, float skillLevel)
            {
                return resourceManager?.GetResource(ResourceIdentifier) is ItemResource inputItemResource &&
                       HasRequiredConsumedItems(inputItemResource, skillLevel);
            }

            public bool HasRequiredConsumedItems(ItemResource? resource, float skillLevel)
            {
                return TryConsumeItems(resource, skillLevel, isDryRun: true);
            }

            /// <summary>
            /// Checks whether the resource has all the required consumed items, and consumes them unless <paramref name="isDryRun"/> is true.
            /// </summary>
            /// <returns>True if there were enough items (i.e. the items were or would be consumed).</returns>
            public bool TryConsumeItems(ItemResource? resource, float skillLevel, bool isDryRun)
            {
                if (resource is null) { return false; }

                Dictionary<ItemPrefab, float> reservedAmounts = [];
                foreach (ItemProduction consumed in ConsumedItems)
                {
                    if (consumed.Item.IsEmpty) { return false; }
                    float remaining = consumed.GetScaledAmount(skillLevel);
                    foreach (ItemPrefab prefab in ItemResource.ResolveItemPrefab(consumed.Item, excludedIdentifierOrTag: Identifier.Empty))
                    {
                        if (remaining <= 0f) { break; }
                        float amountToReserve = Math.Min(resource.CountItems(prefab.Identifier) - reservedAmounts.GetValueOrDefault(prefab), remaining);
                        if (amountToReserve <= 0f) { continue; }
                        reservedAmounts[prefab] = reservedAmounts.GetValueOrDefault(prefab) + amountToReserve;
                        remaining -= amountToReserve;
                    }
                    if (remaining > 0f) { return false; }
                }

                if (!isDryRun)
                {
                    // Modify the specific prefabs instead of the consumed identifiers/tags, so the consumption matches what was reserved
                    foreach (var (prefab, amount) in reservedAmounts)
                    {
                        resource.Modify([prefab], -amount);
                    }
                }
                return true;
            }

            public bool MeetsRequirement(ResourceManager? resourceManager)
            {
                if (resourceManager?.GetResource(ResourceIdentifier) is not ItemResource inputItemResource) { return false; }
                return MeetsRequirement(inputItemResource);
            }

            public bool MeetsRequirement(ItemResource? resource)
            {
                if (resource is null) { return false; }

                foreach (Requirement req in Requirements)
                {
                    float count = resource.CountItems(req.Item);
                    if (!PropertyConditional.Compare(req.Comparison, count, req.Op))
                    {
                        return false;
                    }
                }

                return true;
            }

            public static ConvertedItem FromXml(XElement subElement, string parentDebugName)
            {
                var requirementBuilder = ImmutableArray.CreateBuilder<Requirement>();
                var consumedBuilder = ImmutableArray.CreateBuilder<ItemProduction>();
                var producedBuilder = ImmutableArray.CreateBuilder<ItemProduction>();

                foreach (XElement element in subElement.Elements())
                {
                    Identifier elementName = element.NameAsIdentifier();
                    if (elementName == RequirementElementName)
                    {
                        Identifier itemIdentifier = element.GetAttributeIdentifier("item", Identifier.Empty);
                        string condition = element.GetAttributeString("condition", string.Empty);
                        var (op, conditionStr) = PropertyConditional.ExtractComparisonOperatorFromConditionString(condition);
                        if (!float.TryParse(conditionStr, out float conditionValue))
                        {
                            DebugConsole.ThrowError($"Error in {nameof(ConvertedItem)} in '{parentDebugName}': Requirement condition '{conditionStr}' is not valid.");
                        }

                        requirementBuilder.Add(new Requirement(itemIdentifier, op, conditionValue));
                    }
                    else if (elementName == ConsumedItemElementName)
                    {
                        consumedBuilder.Add(ItemProduction.FromXml(element, parentDebugName));
                    }
                    else if (elementName == ProducedItemElementName)
                    {
                        producedBuilder.Add(ItemProduction.FromXml(element, parentDebugName));
                    }
                }

                ConvertedItem item = new ConvertedItem(
                    resourceIdentifier: subElement.GetAttributeIdentifier("resource", Identifier.Empty),
                    outputResource: subElement.GetAttributeIdentifier(nameof(OutputResource), Identifier.Empty),
                    consumedItems: consumedBuilder.ToImmutable(),
                    producedItems: producedBuilder.ToImmutable(),
                    requirements: requirementBuilder.ToImmutable(),
                    enabledByDefault: subElement.GetAttributeBool(nameof(EnabledByDefault), true));

                if (item.ResourceIdentifier.IsEmpty)
                {
                    DebugConsole.ThrowError($"Error in {nameof(ConvertedItem)} in '{parentDebugName}': No resource identifier specified.");
                }

                if (item.ConsumedItems.IsEmpty)
                {
                    DebugConsole.ThrowError($"Error in {nameof(ConvertedItem)} in '{parentDebugName}': No consumed items specified.");
                }

                if (item.ProducedItems.IsEmpty)
                {
                    DebugConsole.ThrowError($"Error in {nameof(ConvertedItem)} in '{parentDebugName}': No produced items specified.");
                }
                return item;
            }

            public uint GetHash()
            {
                if (HashCache.TryGetValue(this, out uint cache))
                {
                    return cache;
                }

                StringBuilder builder = new();
                builder.Append(ResourceIdentifier).Append('|').Append(OutputResource).Append('|');
                foreach (ItemProduction consumed in ConsumedItems)
                {
                    builder.Append($"c:{consumed.Item}|{consumed.Amount}|");
                }
                foreach (ItemProduction produced in ProducedItems)
                {
                    builder.Append($"p:{produced.Item}|{produced.Amount}|");
                }
                foreach (Requirement r in Requirements)
                {
                    builder.Append($"{r.Item}|{r.Op}|{r.Comparison}");
                }

                using var md5 = MD5.Create();
                uint hash = ToolBoxCore.StringToUInt32Hash(builder.ToString(), md5);
                HashCache.Add(this, hash);
                return hash;
            }

            // a simple lookup to map GetHashCode to the MD5 hash of the recipe
            // GetHashCode is very fast to compute but can't be used for networking
            // or storing in save file
            private static readonly Dictionary<ConvertedItem, uint> HashCache = [];
        }

        /// <param name="Identifier">Which resource to produce.</param>
        /// <param name="Amount">Flat base amount of the resource produced when the workstation is active.
        /// Used as the base unless skill-based amounts are defined, resource-based scaling (if any) multiplies this base, see <see cref="ScaleByResource"/>.</param>
        /// <param name="AmountAtMinSkill">Base amount produced at skill level 0.
        /// If set together with AmountAtMaxSkill, overrides Amount as the base with an interpolated value.</param>
        /// <param name="AmountAtMaxSkill">Base amount produced at skill level 100. NOTE: Linear scaling continues beyond this if skill is higher than 100 (with items etc.)! 
        /// If set together with AmountAtMinSkill, overrides Amount as the base with an interpolated value.</param>
        /// <param name="ItemResourceAddedItems">Items and their ratio that are added to the resource when produced if the resource is an ItemResource.</param>
        /// <param name="AllowedBiomes">Which biomes can this item be produced in. If empty, there are no biome restrictions.</param>
        /// <param name="ScaleByResource">Identifier of another resource to scale the base amount by.
        /// Note that the other resource's amount is used directly as a scaling multiplier, not normalized, so this might be best to use in combination with <see cref="ResourceScalingMultiplier"/> and <see cref="AmountCap"/>.</param>
        /// <param name="ResourceScalingMultiplier">Optional multiplier for scaling the <see cref="ScaleByResource"/> value. Defaults to 1.</param>
        /// <param name="AmountCap">Optional upper limit for the amount applied after any scaling.</param>
        /// <param name="UpcomingAmountsSchedule">Optional schedule set at runtime by the owning component: every world step tick, <see cref="Amount"/> moves on to the next one. Once it runs out, the last amount stays.</param>
        public record struct ProducedResource(
            Identifier Identifier,
            float Amount,
            float? AmountAtMinSkill,
            float? AmountAtMaxSkill,
            Dictionary<Identifier, float> ItemResourceAddedItems,
            string? DisplayNameTag,
            ImmutableHashSet<Identifier> AllowedBiomes,
            Identifier ScaleByResource = default,
            float ResourceScalingMultiplier = 1f,
            float? AmountCap = null,
            float[]? UpcomingAmountsSchedule = null)
        {
            public static ProducedResource FromXml(XElement element, string parentDebugName)
            {
                string? displayNameTag = element.GetAttributeString("displaynametag", null);
                Identifier resourceIdentifier = element.GetAttributeIdentifier("identifier", Identifier.Empty);
                float resourceAmount = element.GetAttributeFloat("amount", 0f);

                if (resourceIdentifier.IsEmpty)
                {
                    DebugConsole.ThrowError($"Invalid {nameof(ProducedResource)} in {parentDebugName}: no identifier given.");
                }

                XAttribute? minSkillAttr = element.Attribute("AmountAtMinSkill");
                XAttribute? maxSkillAttr = element.Attribute("AmountAtMaxSkill");
                float? AmountAtMinSkill = minSkillAttr != null ? element.GetAttributeFloat("AmountAtMinSkill", 0f) : null;
                float? AmountAtMaxSkill = maxSkillAttr != null ? element.GetAttributeFloat("AmountAtMaxSkill", 0f) : null;

                if ((AmountAtMinSkill.HasValue) != (AmountAtMaxSkill.HasValue))
                {
                    DebugConsole.AddWarning($"Invalid {nameof(ProducedResource)} in {parentDebugName}: both 'AmountAtMinSkill' and 'AmountAtMaxSkill' must be defined together.");
                }

                Identifier scaleByResource = element.GetAttributeIdentifier("ScaleByResource", Identifier.Empty);
                float resourceScalingMultiplier = element.GetAttributeFloat("ResourceScalingMultiplier", 1f);
                float? resultAmountCap = element.Attribute("AmountCap") != null ? element.GetAttributeFloat("AmountCap", 0f) : null;

                if (scaleByResource.IsEmpty && element.Attribute("ResourceScalingMultiplier") != null)
                {
                    DebugConsole.AddWarning($"Invalid {nameof(ProducedResource)} in {parentDebugName}: 'ScaleByResource' must be set when using 'ResourceScalingMultiplier'.");
                }

                Dictionary<Identifier, float> items = new();
                foreach (XElement itemElement in element.Elements())
                {
                    Identifier itemIdentifier = itemElement.GetAttributeIdentifier("item", Identifier.Empty);
                    if (itemIdentifier.IsEmpty)
                    {
                        DebugConsole.ThrowError($"Invalid {nameof(ProducedResource)} in {parentDebugName}: child item \"{itemIdentifier}\" does not define the item to spawn using the \"item\" attribute.");
                    }
                    float weight = itemElement.GetAttributeFloat("weight", 1f);
                    if (itemIdentifier != Identifier.Empty)
                    {
                        items[itemIdentifier] = weight;
                    }
                    if (itemElement.GetAttribute("ratio") != null)
                    {
                        DebugConsole.ThrowError($"Invalid {nameof(ProducedResource)} in {parentDebugName}: child item \"{itemIdentifier}\" defines a ratio instead of a weight.");
                    }
                }

                var allowedBiomes = element.GetAttributeIdentifierImmutableHashSet("biomes", ImmutableHashSet<Identifier>.Empty);
                float[]? upcomingAmounts = element.Attribute(nameof(UpcomingAmountsSchedule)) != null ? element.GetAttributeFloatArray(nameof(UpcomingAmountsSchedule), []) : null;
                return new ProducedResource(resourceIdentifier, resourceAmount, AmountAtMinSkill, AmountAtMaxSkill, items, displayNameTag, allowedBiomes,
                    scaleByResource, resourceScalingMultiplier, resultAmountCap, upcomingAmounts);
            }

            public bool MatchesLocation(Location location)
            {
                return AllowedBiomes.IsEmpty || AllowedBiomes.Contains(location.Biome.Identifier);
            }

            /// <summary>
            /// Returns the scaled production amount, combining the optional skill-based and resource-based scaling factors and their limits.
            /// </summary>
            public float GetScaledAmount(float skillLevel, ResourceManager? manager)
            {
                float scaleResourceValue = 0f;
                if (!ScaleByResource.IsEmpty && manager != null)
                {
                    if (manager.GetResource(ScaleByResource) is Resource scaleResource)
                    {
                        scaleResourceValue = scaleResource.Amount;
                    }
                    else
                    {
                        DebugConsole.AddWarning($"Error with resource scaling in {nameof(ResourceGenerator)}: scaling resource \"{ScaleByResource}\" not found.");
                    }
                }

                // Skill-bases scaling is used if both skill amounts are defined, otherwise defaults to the flat Amount
                float amount = AmountAtMinSkill.HasValue && AmountAtMaxSkill.HasValue
                    ? MathHelper.Lerp(AmountAtMinSkill.Value, AmountAtMaxSkill.Value, skillLevel / 100f)
                    : Amount;

                // Resource scaling multiplies the result directly, NOT by a normalized value
                if (!ScaleByResource.IsEmpty)
                {
                    amount *= scaleResourceValue * ResourceScalingMultiplier;
                }

                if (AmountCap.HasValue)
                {
                    amount = Math.Min(amount, AmountCap.Value);
                }

                return amount;
            }

            public XElement Serialize()
            {
                XElement element = new XElement(ProducedResourceElementName,
                    new XAttribute("identifier", Identifier),
                    new XAttribute("amount", Amount));

                if (AmountAtMinSkill.HasValue) { element.Add(new XAttribute("AmountAtMinSkill", AmountAtMinSkill.Value)); }
                if (AmountAtMaxSkill.HasValue) { element.Add(new XAttribute("AmountAtMaxSkill", AmountAtMaxSkill.Value)); }

                if (!ScaleByResource.IsEmpty) { element.Add(new XAttribute("ScaleByResource", ScaleByResource)); }
                if (!MathUtils.NearlyEqual(ResourceScalingMultiplier, 1f)) { element.Add(new XAttribute("ResourceScalingMultiplier", ResourceScalingMultiplier)); }
                if (AmountCap.HasValue) { element.Add(new XAttribute("AmountCap", AmountCap.Value)); }
                if (UpcomingAmountsSchedule is { Length: > 0 }) { element.Add(new XAttribute(nameof(UpcomingAmountsSchedule), string.Join(',', UpcomingAmountsSchedule.Select(static a => a.ToString("G", CultureInfo.InvariantCulture))))); }

                foreach (var (itemIdentifier, weight) in ItemResourceAddedItems)
                {
                    XElement itemElement = new XElement(ItemResourceItemElementName,
                        new XAttribute("item", itemIdentifier),
                        new XAttribute("weight", weight));
                    element.Add(itemElement);
                }

                return element;
            }
        }

        public readonly List<ProducedResource> ProducedResourcesList = [];
        public readonly List<Requirement> RequiredResources = [];
        public readonly List<ConvertedItem> ConvertedItemsList = [];

        /// <summary>
        /// Whether the player has explicitly enabled or disabled a <see cref="ConvertedItem"/> in this generator.
        /// </summary>
        public enum ConversionState
        {
            /// <summary>Not set by the player, <see cref="ConvertedItem.EnabledByDefault"/> determines whether the conversion runs.</summary>
            Default,
            Enabled,
            Disabled
        }

        /// <summary>
        /// Conversions explicitly enabled or disabled by the player, keyed by <see cref="ConvertedItem.GetHash"/>.
        /// Conversions not included here are in the <see cref="ConversionState.Default"/> state (which is never stored here).
        /// </summary>
        private readonly Dictionary<uint, ConversionState> conversionStates = [];

        [Serialize(0, IsPropertySaveable.Yes, "Unique ID of the ResourceGeneratorBaseComponent this generator belongs to. Cached here so offline worldstep ticking can match generators with dweller assignments without the outpost being loaded.")]
        public int UniqueId { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, "Factors that affect the production of the workstation.")]
        public bool IsActive { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, "Are the produced resources' amounts and items set at runtime by the owning component, so when the outpost isn't loaded, they're kept from the save data instead of the item prefab? Everything else is still loaded from the prefab.")]
        public bool ProducedResourcesSetByComponent { get; set; }

        /// <summary>
        /// Whether the owning component is a <see cref="WorkstationComponent"/> (as opposed to e.g. a <see cref="CreatureResourceGeneratorComponent"/>),
        /// i.e. whether NPCs can be assigned to it. Persisted so the outpost overview can tell which generators are unassigned workstations while away.
        /// </summary>
        [Serialize(false, IsPropertySaveable.Yes)]
        public bool IsWorkstation { get; set; }

        /// <summary>
        /// Mirrors <see cref="ResourceGeneratorBaseComponent.ShowOnInterfaceIdentifier"/>, persisted so UI elements that key off it
        /// can be evaluated without the owning item being loaded.
        /// </summary>
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier ShowOnInterfaceIdentifier { get; set; }

        /// <summary>
        /// The average skill level (0-100) of characters currently assigned to the workstation, if this is one.
        /// Updated by WorkstationComponent when characters are assigned or unassigned.
        /// Persisted so that it is available for resource generation when away.
        /// </summary>
        [Serialize(0f, IsPropertySaveable.Yes, "Average skill level of assigned NPCs, used for skill-dependent scaling of resource production.")]
        public float AssignedSkillLevel { get; private set; }

        [Serialize(-1f, IsPropertySaveable.Yes, "Maximum amount of production or conversions that can be generated in one tick.")]
        public float MaximumRecipesPerTick { get; private set; }

        /// <summary>
        /// Research tiers this generator makes available while active.
        /// </summary>
        public ImmutableHashSet<Identifier> UnlockResearchTiers { get; private set; } = ImmutableHashSet<Identifier>.Empty;

        /// <summary>
        /// Which skill affects production amount, interpolating between <see cref="ProducedResource.AmountAtMinSkill"/> and <see cref="ProducedResource.AmountAtMaxSkill"/>.
        /// Empty means no skill scaling and only <see cref="ProducedResource.Amount"/> is used.
        /// Set from <see cref="WorkstationComponent.SkillIdentifier"/>, and persisted so that <see cref="AssignedSkillLevel"/>
        /// can be refreshed correctly during world steps even without the outpost being loaded.
        /// </summary>
        [Serialize("", IsPropertySaveable.Yes, "Skill identifier used for skill-scaled resource production.")]
        public Identifier SkillIdentifier { get; private set; }

        /// <summary>
        /// Percentage (0.0f - 1.0f) how much the next workstation with the same
        /// DiminishingReturnIdentifier gets its produced amount reduced by.
        /// For example, setting this to 0.2 means that the next workstation
        /// will have its production reduced by 20%, stacks additively,
        /// so it will eventually go to 0.
        /// </summary>
        public readonly float DiminishingReturn;

        /// <summary>
        /// Unique ID used to identify if a diminishing return should apply to this resource generator.
        /// </summary>
        public readonly Identifier DiminishingReturnIdentifier;

        /// <summary>
        /// If enabled, the generator doesn't run on the world steps spent in the outpost (= the steps processed when leaving the outpost at the end of a round),
        /// because its effect has already been simulated during the round. E.g. the reactor burns the fuel rods during the round, and the burnt fuel is counted when leaving the outpost.
        /// </summary>
        public readonly bool DisableGenerationWhileInOutpost;

        /// <summary>
        /// Identifier of the item this resource generator was created by.
        /// </summary>
        [Serialize("", IsPropertySaveable.Yes)]
        public Identifier ItemPrefabIdentifier { get; private set; }

        /// <summary>
        /// Name of the ItemComponent this resource generator was created by.
        /// </summary>
        [Serialize("", IsPropertySaveable.Yes)]
        public string ItemComponentName { get; private set; } = string.Empty;

        /// <summary>
        /// Index of the ItemComponent this resource generator was created by.
        /// </summary>
        [Serialize(-1, IsPropertySaveable.Yes)]
        public int ItemComponentIndex { get; private set; }

        [Serialize("", IsPropertySaveable.Yes, "Optional custom localization tag to use when writing resource changes to the economy log.")]
        public Identifier LogTextTag { get; set; }

        public string Name
            => nameof(ResourceGenerator);

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; }

        public const string ProducedResourceElementName = "ProducedResource",
                             ConvertItemElementName = "ConvertItem",
                             RequirementElementName = "Requirement",
                             ItemResourceItemElementName = "ItemResourceItem",
                             ConsumedItemElementName = "ConsumedItem",
                             ProducedItemElementName = "ProducedItem";

        private const string EnabledConversionsAttributeName = "EnabledConversions",
                             DisabledConversionsAttributeName = "DisabledConversions";

        public ResourceGenerator(XElement? element, ItemComponent? parentComponent, ResourceManager? resourceManager, string parentDebugName)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);

            //the conversion states set by the player only exist in the save data,
            //so they must be loaded before the element potentially gets replaced with the item prefab's element below
            if (element != null)
            {
                SetConversionHashes(ConversionState.Enabled, element.GetAttributeUIntArray(EnabledConversionsAttributeName, []));
                SetConversionHashes(ConversionState.Disabled, element.GetAttributeUIntArray(DisabledConversionsAttributeName, []));
            }
            XElement? savedElement = element;

            if (parentComponent != null)
            {
                ItemComponentName = parentComponent.Name;
                ItemPrefabIdentifier = parentComponent.Item.Prefab.Identifier;

                var matchingComponents = parentComponent.Item.GetComponents<ItemComponent>()
                    .Where(ic => ic.GetType() == parentComponent.GetType())
                    .ToList();
                ItemComponentIndex = matchingComponents.IndexOf(parentComponent);
                //if the parentComponent wasn't yet in the item's list of components,
                //it must mean we're in the process of instantiating it
                if (ItemComponentIndex == -1)
                {
                    ItemComponentIndex = matchingComponents.Count;
                }
            }
            else if (!ItemComponentName.IsNullOrEmpty())
            {
                //if we're not instantiating from an existing component (but just loading from XML when the player is away from the outpost)
                //dig up the XML element from the ItemPrefab the ResourceGenerator is associated with
                // - otherwise changes we do to the ResourceGenerators would not carry over to existing saves
                if (ItemPrefab.Prefabs.TryGet(ItemPrefabIdentifier, out ItemPrefab? itemPrefab) && itemPrefab != null &&
                    itemPrefab.ConfigElement.Elements()
                        //components added through item extensions aren't part of the ConfigElement, so those need to be checked too!
                        .Concat(itemPrefab.GetPluginComponentElements())
                        .Where(e => string.Equals(ItemComponentName, e.Name.LocalName, StringComparison.OrdinalIgnoreCase))
                        .ElementAtOrDefault(ItemComponentIndex) is ContentXElement componentElement)
                {
                    foreach (ContentXElement subElement in componentElement.Elements())
                    {
                        if (subElement.NameAsIdentifier() == nameof(ResourceGenerator))
                        {
                            element = subElement;
                            break;
                        }
                    }
                }
            }

            if (element == null) { return; }

            UnlockResearchTiers = element.GetAttributeIdentifierImmutableHashSet(nameof(UnlockResearchTiers), ImmutableHashSet<Identifier>.Empty);
            DiminishingReturn = element.GetAttributeFloat(nameof(DiminishingReturn), 0f);
            DiminishingReturnIdentifier = element.GetAttributeIdentifier("diminishingreturnid", Identifier.Empty);
            DisableGenerationWhileInOutpost = element.GetAttributeBool(nameof(DisableGenerationWhileInOutpost), false);

            if (DiminishingReturn > 0f && DiminishingReturnIdentifier.IsEmpty)
            {
                DebugConsole.ThrowError($"Error in {nameof(ResourceGenerator)} '{parentDebugName}': A 'diminishingreturnid' must be provided when setting diminishing returns.");
            }

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.NameAsIdentifier() == ProducedResourceElementName)
                {
                    var producedResource = ProducedResource.FromXml(subElement, parentDebugName: parentDebugName);
                    ProducedResourcesList.Add(producedResource);
                    if (resourceManager != null && 
                        resourceManager.GetResource(producedResource.Identifier) is not Resource resource)
                    {
                        DebugConsole.ThrowError($"Error in {nameof(ResourceGenerator)} {ItemPrefabIdentifier}: resource \"{producedResource.Identifier}\" not found.");
                    }
                }
                else if (subElement.NameAsIdentifier() == ConvertItemElementName)
                {
                    var convertedItem = ConvertedItem.FromXml(subElement, parentDebugName: parentDebugName);
                    ConvertedItemsList.Add(convertedItem);
                    if (resourceManager?.GetResource(convertedItem.ResourceIdentifier) is ItemResource inputItemResource)
                    {
                        WarnAboutInapplicableConsumedItems(convertedItem, inputItemResource, parentDebugName);
                    }
                }
                else if (subElement.NameAsIdentifier() == RequirementElementName)
                {
                    if (!Requirement.TryCreate(subElement, out Requirement? requirement))
                    {
                        DebugConsole.ThrowError($"Failed to create requirement from element: {subElement}");
                        continue;
                    }
                    RequiredResources.Add(requirement);
                }
                else
                {
                    DebugConsole.ThrowError($"Error in {nameof(ResourceGenerator)} '{parentDebugName}': unrecognized element {subElement.Name}.");
                }
            }

            if (ProducedResourcesSetByComponent && savedElement != null && savedElement != element)
            {
                OverlaySavedProducedResources(savedElement, parentDebugName);
            }
        }

        /// <summary>
        /// Overlays the instance-specific fields (amounts and items) of the saved ProducedResources over the ones loaded from the item prefab.
        /// Matched by index and identifier, so if the prefab has changed, the mismatching ProducedResources from it just keep the prefab values.
        /// </summary>
        private void OverlaySavedProducedResources(XElement savedElement, string parentDebugName)
        {
            var savedResources = savedElement.Elements()
                .Where(static e => e.NameAsIdentifier() == ProducedResourceElementName)
                .Select(e => ProducedResource.FromXml(e, parentDebugName))
                .ToList();

            for (int i = 0; i < ProducedResourcesList.Count && i < savedResources.Count; i++)
            {
                ProducedResource saved = savedResources[i];
                if (saved.Identifier != ProducedResourcesList[i].Identifier) { continue; }

                ProducedResourcesList[i] = ProducedResourcesList[i] with
                {
                    Amount = saved.Amount,
                    UpcomingAmountsSchedule = saved.UpcomingAmountsSchedule,
                    ItemResourceAddedItems = saved.ItemResourceAddedItems
                };
            }
        }

        /// <summary>
        /// Warns if a consumed item can never be part of the input resource (e.g. not in its Allow/GeneratedItem list),
        /// because the conversion could then never run.
        /// </summary>
        private void WarnAboutInapplicableConsumedItems(ConvertedItem convertedItem, ItemResource inputItemResource, string parentDebugName)
        {
            foreach (ConvertedItem.ItemProduction consumed in convertedItem.ConsumedItems)
            {
                if (consumed.Item.IsEmpty) { continue; }
                var prefabs = ItemResource.ResolveItemPrefab(consumed.Item, excludedIdentifierOrTag: Identifier.Empty);
                if (!inputItemResource.GetAllowedPrefabs(prefabs).Any())
                {
                    DebugConsole.ThrowError(
                        $"Potential error in {nameof(ResourceGenerator)} '{parentDebugName}' ({ItemPrefabIdentifier}): " +
                        $"{ConvertItemElementName} consumes \"{consumed.Item}\" from the resource \"{inputItemResource.Identifier}\", " +
                        $"but no item matching it can be part of that resource. The conversion can never run.");
                }
            }
        }

        public void Save(XElement saveElement)
        {
            XElement element = new XElement(nameof(ResourceGenerator));
            SerializableProperty.SerializeProperties(this, element);

            //note that these are ignored if we're loading a ResourceGenerator related to an item,
            //and load these from the item XML instead (except the amounts and items if ProducedResourcesSetByComponent=true)
            foreach (ProducedResource produced in ProducedResourcesList)
            {
                element.Add(produced.Serialize());
            }

            foreach (Requirement req in RequiredResources)
            {
                XElement requirementElement = new XElement(RequirementElementName);
                req.Save(requirementElement);
                element.Add(requirementElement);
            }

            element.Add(new XAttribute(EnabledConversionsAttributeName, string.Join(',', GetConversionHashes(ConversionState.Enabled))));
            element.Add(new XAttribute(DisabledConversionsAttributeName, string.Join(',', GetConversionHashes(ConversionState.Disabled))));

            saveElement.Add(element);
        }

        public ConversionState GetConversionState(uint conversionHash)
            => conversionStates.GetValueOrDefault(conversionHash, ConversionState.Default);

        /// <summary>
        /// Sets the state of the conversion. <see cref="ConversionState.Default"/> resets it back to <see cref="ConvertedItem.EnabledByDefault"/>.
        /// </summary>
        /// <returns>True if the state changed.</returns>
        public bool SetConversionState(uint conversionHash, ConversionState state)
        {
            if (GetConversionState(conversionHash) == state) { return false; }
            if (state == ConversionState.Default)
            {
                conversionStates.Remove(conversionHash);
            }
            else
            {
                conversionStates[conversionHash] = state;
            }
            return true;
        }

        public bool IsConversionEnabled(ConvertedItem conversion)
        {
            return GetConversionState(conversion.GetHash()) switch
            {
                ConversionState.Enabled => true,
                ConversionState.Disabled => false,
                _ => conversion.EnabledByDefault
            };
        }

        /// <summary>
        /// Hashes of the conversions set to the given state.
        /// </summary>
        public IEnumerable<uint> GetConversionHashes(ConversionState state)
            => conversionStates.Where(kvp => kvp.Value == state).Select(kvp => kvp.Key);

        /// <summary>
        /// Replaces the conversions explicitly set to the given state with the given ones.
        /// Conversions in other states are left as-is, unless included in <paramref name="conversionHashes"/>.
        /// </summary>
        public void SetConversionHashes(ConversionState state, IEnumerable<uint> conversionHashes)
        {
            foreach (uint hash in GetConversionHashes(state).ToList())
            {
                conversionStates.Remove(hash);
            }
            foreach (uint hash in conversionHashes)
            {
                SetConversionState(hash, state);
            }
        }

        /// <summary>
        /// Sets the skill identifier used for skill-scaled production.
        /// Should only be called by the workstation system.
        /// </summary>
        public void SetSkillIdentifier(Identifier identifier)
        {
            SkillIdentifier = identifier;
        }

        /// <summary>
        /// Updates the average skill level of assigned characters used for skill-based production scaling.
        /// Should only be called by the workstation system.
        /// </summary>
        public void SetAssignedSkillLevel(float level)
        {
            AssignedSkillLevel = level;
        }

        /// <summary>
        /// Would this generator currently produce at least one <see cref="ProducedResource"/>? Used to estimate
        /// the diminishing returns it applies to later generators, without actually ticking it.
        /// </summary>
        private bool ProducesAnyResource(PersistentOutpostData outpostData)
        {
            if (!IsActive || outpostData.ResourceManager == null) { return false; }
            if (!Requirement.RequirementsMatch(outpostData.ResourceManager, RequiredResources, PropertyConditional.LogicalOperatorType.And, debugLogName: $"{nameof(ResourceGenerator)} {ItemPrefabIdentifier}")) { return false; }
            return ProducedResourcesList.Any(p => p.MatchesLocation(outpostData.Location) && outpostData.ResourceManager.GetResource(p.Identifier) != null);
        }

        /// <summary>
        /// Current multiplier (0 - 1) that diminishing returns apply to this generator's production,
        /// based on the generators before it in <see cref="PersistentOutpostData.ResourceGenerators"/> sharing its <see cref="DiminishingReturnIdentifier"/>.
        /// Mirrors what <see cref="Tick"/> does, for displaying the actual amounts in the UI.
        /// </summary>
        public float GetDiminishingReturnMultiplier(PersistentOutpostData outpostData)
        {
            if (DiminishingReturnIdentifier.IsEmpty) { return 1f; }

            float penalty = 0f;
            foreach (ResourceGenerator other in outpostData.ResourceGenerators)
            {
                if (other == this) { break; }
                if (other.DiminishingReturnIdentifier != DiminishingReturnIdentifier) { continue; }
                if (!other.ProducesAnyResource(outpostData)) { continue; }
                penalty = MathF.Min(penalty + other.DiminishingReturn, 1f);
            }
            return 1f - penalty;
        }

        public void Tick(PersistentOutpostData outpostData, ref Dictionary<Identifier, float> diminishingReturns)
        {
            if (!IsActive || outpostData.ResourceManager == null) { return; }

            AdvanceAmountSchedules();

            bool matchesRequirements = Requirement.RequirementsMatch(outpostData.ResourceManager, RequiredResources, PropertyConditional.LogicalOperatorType.And, debugLogName: $"{nameof(ResourceGenerator)} {ItemPrefabIdentifier}");
            // Requirements aren't met, do not produce resources.
            if (!matchesRequirements) { return; }

            bool hasMax = MaximumRecipesPerTick > 0;
            float generated = 0;
            bool producedAny = false;

            float generationMultiplier = 1f;
            if (!DiminishingReturnIdentifier.IsEmpty && diminishingReturns.TryGetValue(DiminishingReturnIdentifier, out float diminishingReturn))
            {
                generationMultiplier -= diminishingReturn;
            }

            foreach (ConvertedItem conversion in ConvertedItemsList)
            {
                if (!IsConversionEnabled(conversion)) { continue; }

                Resource? resource = outpostData.ResourceManager.GetResource(conversion.ResourceIdentifier);
                Resource? outputResource = conversion.OutputResource.IsEmpty
                                               ? resource
                                               : outpostData.ResourceManager.GetResource(conversion.OutputResource);

                if (resource is not ItemResource inputItemResource)
                {
                    DebugConsole.AddWarning($"Error when attempting to convert a resource to another in {nameof(ResourceGenerator)}: item resource \"{conversion.ResourceIdentifier}\" not found.");
                    continue;
                }

                if (outputResource is not ItemResource outputItemResource)
                {
                    DebugConsole.AddWarning($"Error when attempting to convert a resource to another in {nameof(ResourceGenerator)}: item resource \"{conversion.OutputResource}\" not found.");
                    continue;
                }

                if (!conversion.MeetsRequirement(inputItemResource)) { continue; }

                float oldAmount = (outputItemResource.GetResourceToLogAs() ?? outputItemResource).Amount;

                if (!conversion.TryConsumeItems(inputItemResource, AssignedSkillLevel, isDryRun: false)) { continue; }

                float totalProduced = 0f;
                foreach (ConvertedItem.ItemProduction produced in conversion.ProducedItems)
                {
                    float scaledProducedAmount = produced.GetScaledAmount(AssignedSkillLevel);
                    outputItemResource.Modify(produced.Item, scaledProducedAmount);
                    totalProduced += scaledProducedAmount;
                }

                CreateAndStoreLogEntry(LogType.WorkstationGeneration, outpostData, outputItemResource, oldAmount);
                if (CheckMax(totalProduced)) { return; }
            }

            foreach (var produced in ProducedResourcesList.Where(resource => resource.MatchesLocation(outpostData.Location)))
            {
                if (outpostData.ResourceManager.GetResource(produced.Identifier) is not Resource resource) { continue; }

                float producedAmount = produced.GetScaledAmount(AssignedSkillLevel, resource.Manager) * generationMultiplier;
                float oldAmount = (resource.GetResourceToLogAs() ?? resource).Amount;

                switch (resource)
                {
                    // item resource can handle distributing in the default case if specific items aren't defined here
                    case ItemResource itemResource when produced.ItemResourceAddedItems.Count > 0:
                    {
                        //distribute in chunks of 1 item instead of "splitting the increase up" over multiple items
                        float amountRemainingToDistribute = producedAmount;
                        while (amountRemainingToDistribute > 0)
                        {
                            //select the item to produce based on its weight
                            var producedItem = ToolBox.SelectWeightedRandom(
                                produced.ItemResourceAddedItems,
                                (p) => p.Value,
                                Rand.RandSync.Unsynced);
                            itemResource.Modify(producedItem.Key, Math.Min(1, amountRemainingToDistribute));
                            amountRemainingToDistribute -= 1;
                        }
                        CreateAndStoreLogEntry(LogType.WorkstationGeneration, outpostData, itemResource, oldAmount);
                        break;
                    }
                    case ResearchPointResource researchResource:
                        // Since research points are actually global and not per outpost,
                        // we don't want NPC outposts to be generating research points towards the player's global pool
                        if (!outpostData.IsPlayerOwned) { break; }
                        researchResource.AddResearchPoints((int)producedAmount);
                        CreateAndStoreLogEntry(LogType.ResearchGeneration, outpostData, researchResource, oldAmount);
                        break;
                    default:
                        resource.Amount += producedAmount;
                        CreateAndStoreLogEntry(LogType.WorkstationGeneration, outpostData, resource, oldAmount);
                        break;
                }

                producedAny = true;
                if (CheckMax(producedAmount))
                {
                    ApplyDiminishingReturn(diminishingReturns);
                    return;
                }
            }
            ApplyDiminishingReturn(diminishingReturns);
            return;

            // Steps once per generator, no matter how many ProducedResources it has.
            // Takes the dictionary as a parameter because local functions can't capture the ref parameter.
            void ApplyDiminishingReturn(Dictionary<Identifier, float> returns)
            {
                if (!producedAny || DiminishingReturnIdentifier.IsEmpty) { return; }
                float old = returns.GetValueOrDefault(DiminishingReturnIdentifier);
                returns[DiminishingReturnIdentifier] = MathF.Min(old + DiminishingReturn, 1f);
            }

            // We probably don't need this to be weighted/randomized? I don't think it has a big impact
            bool CheckMax(float added)
            {
                if (!hasMax) { return false; }
                if (MathUtils.NearlyEqual(added, 0f)) { return false; }
                generated += 1;
                return generated >= MaximumRecipesPerTick;
            }
        }

        /// <summary>
        /// Moves the ProducedResource Amount to the next one in the <see cref="ProducedResource.UpcomingAmountsSchedule"/>.
        /// </summary>
        private void AdvanceAmountSchedules()
        {
            for (int i = 0; i < ProducedResourcesList.Count; i++)
            {
                if (ProducedResourcesList[i] is { UpcomingAmountsSchedule: [float next, .. var rest] } produced)
                {
                    ProducedResourcesList[i] = produced with { Amount = next, UpcomingAmountsSchedule = rest };
                }
            }
        }

        private void CreateAndStoreLogEntry(LogType logType, PersistentOutpostData outpostData, Resource resource, float oldValue)
        {
            Resource resourceToLog = resource.GetResourceToLogAs() ?? resource;
            outpostData.StoreResourceChangeEvent(LogTextTag.IsEmpty
                ? EconomyLogEntryGeneric.Create(logType, resourceToLog, oldValue, resourceToLog.Amount, outpostData.Location)
                : EconomyLogEntrySpecial.Create(LogTextTag, resourceToLog, oldValue, resourceToLog.Amount, outpostData.Location)
            );
        }
    }
}