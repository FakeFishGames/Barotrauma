using Barotrauma;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.PerkBehaviors;
using Microsoft.Xna.Framework;
using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;

namespace HomeAndHarbor.Resources
{
    internal sealed class ItemResource : Resource
    {
        /// <summary>
        /// Defines parameters for items that are generated/removed as the resource amount changes,
        /// such as specific food items that appear in an outpost when the food resource goes up.
        /// </summary>
        /// <param name="Identifier">Can either be an identifier or a tag of the item.</param>
        /// <param name="ExcludedItem">Identifier or tag of items that should be excluded, even if they have the tag/id specified by Identifier.</param>
        /// <param name="Ratio">Affects how much of this item generates relative to the others. Arbitrary value, defaults to 1.</param>
        /// <param name="Threshold">The amount of the resource must be equal to or larger than this value for this item to generate.</param>
        /// <param name="MaxCount">A maximum number of the item in the outpost. I.e. if there's already this many or more of the item, new ones won't spawn when the resource increases.
        /// Does not apply to player outposts, as it would easily lead to unintuitive behavior when the players items inexplicably disappear.</param>
        /// <param name="Factions">The outpost must belong to one of these factions for the item to spawn. Use "None" if the outpost must not belong to any faction.</param>
        /// <param name="ExcludedFactions">The item doesn't spawn if the outpost belongs to one of these factions. Use "None" to exclude outposts that don't belong to any faction.
        /// Useful for defining fallback items that also apply to custom factions, e.g. excluding the factions that have their own specific items defined.</param>
        /// <param name="Biome">If not empty, the item will only spawn in this biome</param>
        /// <param name="SpawnPriority">Determines the order in which the items are spawned, items with a higher value spawning first. Useful to prioritize spawning certain items when SpawnIfNoSpaceInContainer is set to false.</param>
        /// <param name="SpawnIfNoSpaceInContainer">If set to false, the number of these items that can spawn is limited by the number of available containers (e.g. an outpost won't spawn diving suits if there's no space in cabinets)</param>
        private readonly record struct GeneratedItemParams(Identifier Identifier,
                                                           Identifier ExcludedItem,
                                                           float Ratio,
                                                           float Threshold,
                                                           int MaxCount,
                                                           ImmutableHashSet<Identifier> Factions,
                                                           ImmutableHashSet<Identifier> ExcludedFactions,
                                                           Identifier Biome,
                                                           int SpawnPriority,
                                                           bool SpawnIfNoSpaceInContainer)
        {
            public GeneratedItemParams(ContentXElement element) : this(
                 Identifier: element.GetAttributeIdentifier("item", Identifier.Empty),
                 ExcludedItem: element.GetAttributeIdentifier(nameof(ExcludedItem), Identifier.Empty),
                 Ratio: element.GetAttributeFloat(nameof(Ratio), 1f),
                 Threshold: element.GetAttributeFloat(nameof(Threshold), 0f),
                 MaxCount: element.GetAttributeInt(nameof(MaxCount), int.MaxValue),
                 Factions: element.GetAttributeIdentifierImmutableHashSet("faction", []),
                 ExcludedFactions: element.GetAttributeIdentifierImmutableHashSet(nameof(ExcludedFactions), []),
                 Biome: element.GetAttributeIdentifier(nameof(Biome), Identifier.Empty),
                 SpawnPriority: element.GetAttributeInt(nameof(SpawnPriority), 0),
                 SpawnIfNoSpaceInContainer: element.GetAttributeBool(nameof(SpawnIfNoSpaceInContainer), true))
            { 
            }

            public bool MatchesItemPrefab(ItemPrefab itemPrefab)
            {
                if (!ExcludedItem.IsEmpty)
                {
                    if (itemPrefab.Identifier == ExcludedItem || itemPrefab.Tags.Contains(ExcludedItem))
                    {
                        return false;
                    }
                }
                return itemPrefab.Identifier == Identifier || itemPrefab.Tags.Contains(Identifier);
            }
        }

        /// <summary>
        /// Defines items that are considered part of this resource if they appear in the outpost (e.g. produced by a workstation or a fabricator, brought to the outpost by the player)
        /// but which don't spotaneously appear when the resource goes up.
        /// </summary>
        /// <param name="Identifier">Can either be an identifier or a tag of the item.</param>
        /// <param name="ExcludedItem">Identifier or tag of items that should be excluded, even if they have the tag/id specified by Identifier.</param>
        private readonly record struct AllowedItemParams(Identifier Identifier, Identifier ExcludedItem)
        {
            public AllowedItemParams(ContentXElement element) : this(
                 Identifier: element.GetAttributeIdentifier("item", Identifier.Empty),
                 ExcludedItem: element.GetAttributeIdentifier(nameof(ExcludedItem), Identifier.Empty))
            {
            }

            public bool MatchesItemPrefab(ItemPrefab itemPrefab)
            {
                if (!ExcludedItem.IsEmpty)
                {
                    if (itemPrefab.Identifier == ExcludedItem || itemPrefab.Tags.Contains(ExcludedItem))
                    {
                        return false;
                    }
                }
                return itemPrefab.Identifier == Identifier || itemPrefab.Tags.Contains(Identifier);
            }
        }

        [Serialize(1f, IsPropertySaveable.No, description: "The ratio of the resource to the items found in the outpost. For example value of 0.1 means one item will correspond to 10 of the resource amount.")]
        public float ItemRatio { get; set; }

        [Serialize("", IsPropertySaveable.No, description: "If set, the resource avoids removing items from a container with this tag, and if it must remove them, it tries to replace them with another suitable item. Useful for e.g. making sure reactors don't get emptied when the fuel resources are reduced.")]
        public Identifier AvoidEmptyingContainer { get; set; }

        [Serialize(false, IsPropertySaveable.No, description: "If enabled, items consumed when the resource goes down are set to 0% condition instead of being removed (e.g. depleted fuel rods). Items at 0% condition don't count towards the resource.")]
        public bool DepleteItemsInsteadOfRemoving { get; set; }

        [Serialize(false, IsPropertySaveable.No, description: "If enabled, items are depleted gradually by reducing their condition instead of setting them to 0% at once (e.g. 0.8 fuel rods = one rod at 80% condition), " +
            "and partially depleted items count towards the resource based on their condition. Requires DepleteItemsInsteadOfRemoving to be enabled.")]
        public bool DepleteItemsGradually { get; set; }

        private bool DepletesGradually => DepleteItemsInsteadOfRemoving && DepleteItemsGradually;

        /// <summary>
        /// When depleting items gradually, items left with less than this condition (as a fraction of the max condition) are depleted fully,
        /// and partially depleted items smaller than this aren't spawned.
        /// </summary>
        private const float MinPartialItemCondition = 0.01f;

        /// <summary>
        /// Allowed items can count towards the resource, but don't appear normally as a result of the resource amount increasing.
        /// I.e. a work station for example might produce some special food items that increase the "food" resource, even if those food items don't normally appear if the food resource goes up.
        /// </summary>
        private readonly HashSet<AllowedItemParams> allowedItems = [];

        /// <summary>
        /// Items that can generate when the amount of the resource goes up.
        /// E.g. specific food items that appear in an outpost when the food resource goes up.
        /// </summary>
        private readonly List<GeneratedItemParams> generatedItems = [];

        private readonly Dictionary<Identifier, float> itemAmountsByIdentifier = [];

        private const string AccumulatedItemElementName = "AccumulatedItem";

        private readonly List<Item> ItemsThatDidntFitInContainers = [];

        /// <summary>
        /// The amount of the resource that must be reached to spawn one item
        /// </summary>
        private float FullItemAmount => 1.0f / ItemRatio;

        public ItemResource(ResourcePrefab prefab, ResourceManager manager, XElement? element) : base(prefab, manager, element)
        {
            foreach (ContentXElement subElement in prefab.OriginalElement.Elements())
            {
                if (subElement.Name.ToIdentifier() == "Allow")
                {
                    Identifier identifier = subElement.GetAttributeIdentifier("item", Identifier.Empty);
                    if (!identifier.IsEmpty)
                    {
                        allowedItems.Add(new AllowedItemParams(subElement));
                    }
                    else
                    {
                        DebugConsole.ThrowError($"Error in resource {this.Identifier}: AllowedItem does not define which item to generate ({subElement}).");
                    }
                }
                else if (subElement.Name.ToIdentifier() == "GeneratedItem")
                {
                    if (subElement.GetAttribute("weight") != null)
                    {
                        DebugConsole.ThrowError(
                            $"Error in {nameof(ItemResource)} \"{Identifier}\": GeneratedItem defined using weight instead of ratio."
                            + " The ratio determines how likely a given item is to be spawned as the resource, i.e. how common the items are relative to each other.");
                    }

                    Identifier identifier = subElement.GetAttributeIdentifier("item", Identifier.Empty);
                    if (!identifier.IsEmpty)
                    {
                        var newGeneratedItem = new GeneratedItemParams(subElement);
                        if (newGeneratedItem.Threshold > MaxAmount)
                        {
                            DebugConsole.ThrowError(
                                $"Error in {nameof(ItemResource)} \"{Identifier}\": GeneratedItem {newGeneratedItem.Identifier} has a spawn threshold " +
                                $"higher than the maximum value of the resource ({newGeneratedItem.Threshold} vs {MaxAmount}). The item will never spawn.");
                        }
                        generatedItems.Add(newGeneratedItem);
                        allowedItems.Add(new AllowedItemParams(subElement));
                    }
                    else
                    {
                        DebugConsole.ThrowError($"Error in resource {this.Identifier}: GeneratedItem does not define which item to generate ({subElement}).");
                    }
                }
            }

            if (DepleteItemsGradually && !DepleteItemsInsteadOfRemoving)
            {
                DebugConsole.AddWarning($"Potential error in the resource \"{Identifier}\": {nameof(DepleteItemsGradually)} has no effect unless {nameof(DepleteItemsInsteadOfRemoving)} is enabled.");
            }

            if (element is null) { return; }

            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToIdentifier() == AccumulatedItemElementName)
                {
                    Identifier itemIdentifier = subElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                    float amount = subElement.GetAttributeFloat("amount", 1f);
                    if (itemIdentifier.IsEmpty) { continue; }

                    ImmutableHashSet<ItemPrefab> prefabs = ResolveItemPrefab(itemIdentifier, excludedIdentifierOrTag: Identifier.Empty);
                    if (GetAllowedPrefabs(prefabs).None())
                    {
                        DebugConsole.AddWarning($"The item {itemIdentifier} is no longer applicable to the resource {Identifier}. Removing it from the resource.");
                    }
                    else
                    {
                        itemAmountsByIdentifier.TryGetValue(itemIdentifier, out float existingAmount);
                        itemAmountsByIdentifier[itemIdentifier] = existingAmount + amount;
                    }
                }
            }
        }

        /// <summary>
        /// Total amount of the given item in this resource. The identifier can be either an item identifier or a tag,
        /// in which case the amounts of all items with the tag are summed (consistent with <see cref="Modify(Identifier, float)"/>).
        /// </summary>
        public float CountItems(Identifier identifierOrTag)
        {
            if (identifierOrTag.IsEmpty) { return 0.0f; }

            float total = itemAmountsByIdentifier.GetValueOrDefault(identifierOrTag);
            foreach (ItemPrefab prefab in ResolveItemPrefab(identifierOrTag, excludedIdentifierOrTag: Identifier.Empty))
            {
                // The prefab with the exact identifier was already counted above
                if (prefab.Identifier == identifierOrTag) { continue; }
                total += itemAmountsByIdentifier.GetValueOrDefault(prefab.Identifier);
            }
            return total;
        }

        /// <summary>
        /// Same as <see cref="CountItems(Identifier)"/>, but if this location's outpost is currently loaded, counts the actual items in the outpost instead.
        /// The stored item amounts only get updated to match the items when leaving the outpost, and clients only receive the total amount of the resource.
        /// The items are counted the same way as in <see cref="SetResourceToMatchItems"/>, and the amount is returned in resource units.
        /// </summary>
        public float CountItemsInLoadedOutpost(Identifier identifierOrTag)
        {
            if (identifierOrTag.IsEmpty) { return 0.0f; }
            if (Manager.GetLoadedOutpost() is not Submarine outpost) { return CountItems(identifierOrTag); }

            ImmutableHashSet<ItemPrefab> prefabs = ResolveItemPrefab(identifierOrTag, excludedIdentifierOrTag: Identifier.Empty);
            bool allowsAnyItem = allowedItems.None() && generatedItems.None();

            int itemCount = 0;
            foreach (Item item in GetOutpostItems(outpost).Union(ItemsThatDidntFitInContainers))
            {
                if (!prefabs.Contains(item.Prefab)) { continue; }
                if (DepleteItemsInsteadOfRemoving && item.Condition <= 0.0f) { continue; }
                if (generatedItems.Any(gi => gi.MatchesItemPrefab(item.Prefab)) ||
                    allowedItems.Any(allowed => allowed.MatchesItemPrefab(item.Prefab)) ||
                    (allowsAnyItem && itemAmountsByIdentifier.ContainsKey(item.Prefab.Identifier)))
                {
                    itemCount++;
                }
            }
            return itemCount / ItemRatio;
        }

        protected override float GetAmountResourceSpecific()
        {
            float totalAmount = 0;
            foreach (KeyValuePair<Identifier, float> kvp in itemAmountsByIdentifier)
            {
                totalAmount += kvp.Value;
            }
            return totalAmount;
        }

        // Comparing by zero was causing the methods to get into a loop due to inaccuracies
        // that then caused it to eventually start comparing with -Infinity due to precision
        // (and everything math'd by -Infinity is still -Infinity causing an infinite loop)
        private const float ZeroEpsilon = 0.001f;
        private const int MaxIterations = 100;

        /// <summary>
        /// If there are no items that can be generated by the resource, we cannot sync its value directly (we need to know what specific items have been manually added to it, can't just set the amount)
        /// </summary>
        public override bool IsSyncable => generatedItems.Any();

        /// <summary>
        /// Generated items applicable to the current faction and biome of the outpost.
        /// </summary>
        private IEnumerable<GeneratedItemParams> GetApplicableGeneratedItems()
        {
            return generatedItems.Where(IsApplicable);
        }

        /// <summary>
        /// Used in <see cref="GeneratedItemParams.Factions"/> and <see cref="GeneratedItemParams.ExcludedFactions"/> to refer to outposts that don't belong to any faction.
        /// </summary>
        private static readonly Identifier NoFactionIdentifier = "None".ToIdentifier();

        private bool IsApplicable(GeneratedItemParams item)
        {
            Identifier faction = Manager.FactionIdentifier.IsEmpty ? NoFactionIdentifier : Manager.FactionIdentifier;
            return
                (item.Factions.IsEmpty || item.Factions.Contains(faction)) &&
                !item.ExcludedFactions.Contains(faction) &&
                (item.Biome.IsEmpty || item.Biome == Manager.Location?.Biome?.Identifier);
        }

        /// <summary>
        /// Finds the generation params matching the predicate, preferring ones applicable to the current faction and biome
        /// (the same item can be listed for multiple factions with different thresholds, max counts etc).
        /// Falls back to any matching params if none are applicable, so the item is still recognized as part of the resource.
        /// Use <see cref="IsApplicable"/> to check if the limits (max count, threshold) of the result should be respected.
        /// </summary>
        private GeneratedItemParams? FindGeneratedItemParams(Func<GeneratedItemParams, bool> predicate)
        {
            return GetApplicableGeneratedItems().FirstOrNull(predicate) ?? generatedItems.FirstOrNull(predicate);
        }

        /// <summary>
        /// Max counts don't apply to player outposts, see <see cref="GeneratedItemParams.MaxCount"/>.
        /// </summary>
        private int GetMaxCount(GeneratedItemParams item)
        {
            return Manager.OutpostData.IsPlayerOwned ? int.MaxValue : item.MaxCount;
        }

        protected override void SetAmount(float value)
        {
            float currentAmount = GetAmount();
            float difference = value - currentAmount;
            if (MathUtils.NearlyEqual(difference, 0)) { return; }

            bool isIncreasing = difference > 0;

            // First try to distribute the change among items specifically generated by this resource.
            // (e.g. if an increase in food is configured to spawn protein bars, do that)
            var applicableGeneratedItems = GetApplicableGeneratedItems()
                .Where(item => value >= item.Threshold)
                .ToImmutableArray();

            if (applicableGeneratedItems.None() && difference > 0)
            {
                DebugConsole.AddWarning(
                    $"Potential error in the resource \"{Identifier}\": no generated items applicable for the location {Manager.Location.DisplayName}." +
                    " Cannot increase the value of the resource because there are no valid items to add.\n" + Environment.StackTrace);
            }

            float totalRatio = applicableGeneratedItems.Sum(static gis => gis.Ratio);
            if (totalRatio > 0)
            {
                //distribute the amount in "chunks" of "how much it takes to reach one item" between the different generated items
                //so we prefer finishing "partially generated" items instead of distributing a small amount between a bunch of different items
                float chunkSize = FullItemAmount;
                int chunksToDistribute = (int)Math.Ceiling(Math.Max(Math.Abs(difference) / chunkSize, 1));
                float remainingAmountToDistribute = Math.Abs(difference);
                for (int i = 0; i < chunksToDistribute; i++)
                {
                    var candidateItems = applicableGeneratedItems;
                    if (DepletesGradually && !isIncreasing && applicableGeneratedItems.Any(IsPartiallyDepleted))
                    {
                        //when depleting gradually, finish using up partially depleted items before starting on new ones (e.g. burn one fuel rod at a time)
                        candidateItems = applicableGeneratedItems.Where(IsPartiallyDepleted).ToImmutableArray();
                    }

                    var mostPartialGeneratedItem = candidateItems.GetRandomByWeight((GeneratedItemParams p) =>
                    {
                        //the identifier can be a tag, CountItems handles both
                        float existingAmount = CountItems(p.Identifier);

                        float progressToNextFullItem =
                            isIncreasing ?
                            MathF.Floor(existingAmount) % FullItemAmount :
                            MathUtils.PositiveModulo(MathF.Floor(existingAmount), FullItemAmount);
                        
                        float desiredAmount = Math.Min(Amount / totalRatio * p.Ratio, GetMaxCount(p));

                        float weight =
                            //ratio affects which items are more likely to spawn
                            p.Ratio
                            //finishing up partial items is preferred
                            * MathHelper.Lerp(0.1f, 1.0f, progressToNextFullItem / FullItemAmount);

                        if (desiredAmount > 0.0f)
                        {
                            float ratioOfDesiredAmount = MathHelper.Clamp(existingAmount / desiredAmount, 0.0f, 1.0f);
                            if (isIncreasing)
                            {
                                //give a higher weight to spawning items far away from the desired amount
                                weight *= MathHelper.Lerp(1.0f, 0.01f, ratioOfDesiredAmount);
                            }
                            else
                            {
                                //other way around: high preference for removing items that we have many of
                                weight *= MathHelper.Lerp(0.01f, 1.0f, ratioOfDesiredAmount);
                            }
                        }
                        return weight;
                    },
                    Rand.RandSync.Unsynced);

                    if (mostPartialGeneratedItem.Identifier.IsEmpty)
                    {
                        DebugConsole.ThrowError(
                            $"Something went wrong when modifying {nameof(ItemResource)} {Identifier}: could not find an applicable item to modify. Applicable items: "
                            + string.Join(", ", applicableGeneratedItems.Select(a => $"{a.Identifier}")));
                        break;
                    }
                    else
                    {
                        chunkSize = Math.Min(chunkSize, remainingAmountToDistribute);
                        float amountToModify = chunkSize;
                        if (!isIncreasing)
                        {
                            //can't reduce more than there is of the item, the excess pass below reduces the rest from other items
                            amountToModify = Math.Min(amountToModify, CountItems(mostPartialGeneratedItem.Identifier));
                        }
                        Modify(mostPartialGeneratedItem.Identifier, isIncreasing ? amountToModify : -amountToModify);
                        remainingAmountToDistribute -= amountToModify;
                    }
                }
            }

            // If we still have an excess, it means there are other items contributing to this resource
            // that aren't in the 'generated items' list. Reduce all of them.
            float excess = GetAmount() - value;
            if (excess > ZeroEpsilon)
            {
                // We might need multiple passes because Modify() may not be able to reduce
                // the full requested amount if an item's amount hits zero.
                for (int iteration = 0; iteration < MaxIterations; iteration++)
                {
                    float totalResourceAmount = GetAmount();
                    if (totalResourceAmount <= ZeroEpsilon) { break; }
                    foreach (var (identifier, itemAmount) in itemAmountsByIdentifier)
                    {
                        if (itemAmount <= 0) { continue; }
                        Modify(identifier, -excess * itemAmount / totalResourceAmount);
                    }
                    excess = GetAmount() - value;
                    if (excess <= ZeroEpsilon) { break; }
                }
            }

            RemoveEmptyItems();

            bool IsPartiallyDepleted(GeneratedItemParams p)
            {
                //the identifier can be a tag, CountItems handles both
                float partialAmount = MathUtils.PositiveModulo(CountItems(p.Identifier), FullItemAmount);
                return partialAmount > ZeroEpsilon && partialAmount < FullItemAmount - ZeroEpsilon;
            }
        }

        /// <summary>
        /// Filter the prefabs based on which of them are considered applicable to this resource (<see cref="allowedItems"/>).
        /// </summary>
        public IEnumerable<ItemPrefab> GetAllowedPrefabs(IEnumerable<ItemPrefab> prefabs)
        {
            if (allowedItems.Count > 0)
            {
                // Only allow items that are specifically allowed for this resource. Assuming we have an allow list
                return prefabs.Where(prefab => allowedItems.Any(allowed => allowed.MatchesItemPrefab(prefab)));
            }
            else
            {
                return prefabs;
            }
        }

        /// <summary>
        /// Modify the amount of a given item that contributes to this resource. 
        /// The identifier can be either an identifier or a tag (however, if no matching item is configured to be generated or allowed for this resource, the method will do nothing).
        /// </summary>
        public void Modify(Identifier identifier, float amountToAdd)
        {
            // Resolve the identifier (which could be a specific item identifier or a tag)
            ImmutableHashSet<ItemPrefab> prefabs = ResolveItemPrefab(identifier,
                //no need to exclude at this point, we do it in Modify
                excludedIdentifierOrTag: Identifier.Empty);
            Modify(prefabs, amountToAdd);
        }

        /// <summary>
        /// Modify the amount of some specific items that contribute to this resource. 
        /// Prefabs in the list that aren't configured to be generated or allowed for this resource will be ignored.
        /// </summary>
        public void Modify(IEnumerable<ItemPrefab> prefabs, float amountToAdd)
        {
            if (MathUtils.NearlyEqual(amountToAdd, 0)) { return; }

            IEnumerable<ItemPrefab> allowedPrefabs = GetAllowedPrefabs(prefabs);

            if (allowedPrefabs.None())
            {
                DebugConsole.ThrowError($"Failed to modify resource \"{Identifier}\": items {string.Join(" ,", prefabs.Select(p => p.Name))} are not applicable to the resource.");
                return;
            }

            if (allowedPrefabs.Count() == 1)
            {
                UpdateItemAmount(allowedPrefabs.First(), amountToAdd);
                return;
            }

            float remainder = amountToAdd;
            remainder = DistributeToFinishOffPartialItems(allowedPrefabs, remainder);            

            if (remainder is > ZeroEpsilon or < -ZeroEpsilon)
            {
                DistributeRemainder(allowedPrefabs, remainder);
            }

            RemoveEmptyItems();
        }

        private void RemoveEmptyItems()
        {
            foreach (var kvp in itemAmountsByIdentifier.Where(kvp => kvp.Value < ZeroEpsilon).ToList())
            {
                itemAmountsByIdentifier.Remove(kvp.Key);
            }
        }

        private void UpdateItemAmount(ItemPrefab prefab, float amountToAdd)
        {
            UpdateItemAmount(prefab.Identifier, amountToAdd);
        }

        private void UpdateItemAmount(Identifier prefabIdentifier, float amountToAdd)
        {
            if (prefabIdentifier.IsEmpty)
            {
                DebugConsole.ThrowError($"Failed to update item amount for resource {Identifier}: item prefab identifier is empty.\n" + Environment.StackTrace);
                return;
            }
            itemAmountsByIdentifier.TryGetValue(prefabIdentifier, out float existingAmount);
            float newAmount = Math.Max(existingAmount + amountToAdd, 0f);
            itemAmountsByIdentifier[prefabIdentifier] = newAmount;

            // The same item can count towards multiple resources, and the amounts should stay in sync,
            // ie. 15 oxygen tanks in the outpost means 15 tanks in _every_ resource that contains oxygen tanks.
            // The amounts are stored as amounts of the resource (instead of numbers of items), so we convert them
            // to a number of items and back using the ItemRatio of each resource.
            float itemCount = newAmount * ItemRatio;
            ItemPrefab? prefab = null;
            foreach (Resource otherResource in Manager.Resources)
            {
                if (otherResource == this || otherResource is not ItemResource otherItemResource) { continue; }

                if (!otherItemResource.itemAmountsByIdentifier.ContainsKey(prefabIdentifier))
                {
                    //The other resource doesn't track this item yet (or the entry was removed when the item ran out), but it accepts it:
                    //add the entry, otherwise e.g. plants grown while the processor has none left wouldn't count towards its resource.
                    //Don't create entries when the item is gone, there's nothing to track.
                    if (newAmount <= 0.0f) { continue; }
                    if (prefab == null && !ItemPrefab.Prefabs.TryGet(prefabIdentifier, out prefab)) { continue; }
                    if (!otherItemResource.BelongsIn(prefab)) { continue; }
                }
                otherItemResource.itemAmountsByIdentifier[prefabIdentifier] = itemCount / otherItemResource.ItemRatio;
            }
        }

        /// <summary>
        /// Distributes items so items with a partial amount get topped off first to the nearest integer
        /// </summary>
        /// <param name="prefabs">List of prefabs to distribute</param>
        /// <param name="amount">How much to distribute</param>
        /// <returns>Possible leftover amount if there's nothing to 'finish off'</returns>
        private float DistributeToFinishOffPartialItems(IEnumerable<ItemPrefab> prefabs, float amount)
        {
            bool isIncreasing = amount > 0;

            // Find items that have a partial amount to reach the next whole integer
            List<(ItemPrefab Prefab, float Needed)> priorityItems = [];
            foreach (ItemPrefab prefab in prefabs)
            {
                if (itemAmountsByIdentifier.TryGetValue(prefab.Identifier, out float existingAmount))
                {
                    float progressToNextFullItem =
                        isIncreasing ?
                        MathF.Floor(existingAmount) % FullItemAmount :
                        MathUtils.PositiveModulo(MathF.Floor(existingAmount), FullItemAmount);
                    if (MathUtils.NearlyEqual(progressToNextFullItem, 0)) { continue; }
                    float missingAmount = FullItemAmount - progressToNextFullItem;
                    if (missingAmount > ZeroEpsilon)
                    {
                        float needed = isIncreasing
                            ? missingAmount
                            : FullItemAmount - missingAmount;
                        priorityItems.Add((prefab, needed));
                    }
                }
            }

            if (priorityItems.Count is 0) { return amount; }

            var sortedItems = isIncreasing
                ? priorityItems.OrderByDescending(static item => item.Needed)
                : priorityItems.OrderBy(static item => item.Needed);

            foreach (var (prefab, needed) in sortedItems)
            {
                float toChange = isIncreasing
                    ? MathF.Min(amount, needed)
                    : MathF.Max(amount, -needed);

                UpdateItemAmount(prefab, toChange);
                amount -= toChange;

                if (isIncreasing)
                {
                    if (amount <= ZeroEpsilon) { break; }
                }
                else
                {
                    if (amount >= -ZeroEpsilon) { break; }
                }
            }

            return amount;
        }

        private void DistributeRemainder(IEnumerable<ItemPrefab> prefabs, float amount)
        {
            bool isIncreasing = amount > 0;
            if (isIncreasing)
            {
                var prefabList = prefabs.ToImmutableArray();
                if (prefabs.Count() is 1)
                {
                    UpdateItemAmount(prefabList.GetRandomUnsynced()!, amount);
                    return;
                }

                while (amount > ZeroEpsilon)
                {
                    float toAdd = Math.Min(amount, 1.0f / ItemRatio);
                    UpdateItemAmount(prefabList.GetRandomUnsynced()!, toAdd);
                    amount -= toAdd;
                }
            }
            else
            {
                int iteration = 0;
                // Only reduce the requested items (important when consuming by tag, we must not eat unrelated items)
                var prefabIdentifiers = prefabs.Select(p => p.Identifier).ToImmutableHashSet();
                while (amount < -ZeroEpsilon && itemAmountsByIdentifier.Any(kvp => prefabIdentifiers.Contains(kvp.Key)))
                {
                    float toReduce = Math.Min(amount, 1.0f / ItemRatio);
                    Identifier mostPartialItem = itemAmountsByIdentifier
                        .Where(kvp => prefabIdentifiers.Contains(kvp.Key))
                        .MinBy(kvp => MathUtils.PositiveModulo(kvp.Value, FullItemAmount)).Key;
                    if (mostPartialItem.IsEmpty)
                    {
                        DebugConsole.AddWarning($"Something went wrong when trying to reduce ItemResource {Identifier}: the item to reduce has no identifier?");
                        break;
                    }
                    float existingAmount = itemAmountsByIdentifier[mostPartialItem];
                    toReduce = Math.Max(-existingAmount, toReduce);
                    UpdateItemAmount(mostPartialItem, toReduce);
                    if (itemAmountsByIdentifier[mostPartialItem] < ZeroEpsilon)
                    {
                        itemAmountsByIdentifier.Remove(mostPartialItem);
                    }

                    amount -= toReduce;
                    iteration++;
                    if (iteration >= MaxIterations)
                    {
                        DebugConsole.ThrowError($"Failed to reduce ItemResource {Identifier} after {MaxIterations} iterations in location {Manager.Location.DisplayName}. Remaining amount to reduce: {amount}");
                    }
                }

                if (amount < -ZeroEpsilon)
                {
                    DebugConsole.AddWarning($"Could not reduce ItemResource {Identifier} by the full amount ({amount}) in location {Manager.Location.DisplayName}: ran out of items to reduce, calculations are leaking somewhere.");
                }
            }
        }

        public override XElement Save()
        {
            XElement saveElement = base.Save();
            foreach (var (identifier, amount) in itemAmountsByIdentifier)
            {
                XElement itemElement = new XElement(AccumulatedItemElementName,
                                                    new XAttribute("identifier", identifier),
                                                    new XAttribute("amount", amount));
                saveElement.Add(itemElement);
            }

            return saveElement;
        }

        private static Md5Hash? prevContentPackagesHash;
        private static readonly Dictionary<Identifier, ImmutableHashSet<ItemPrefab>> ItemPrefabCache = [];

        /// <summary>
        /// Find the item prefabs that match the specified identifiers/tags. Also handles caching the results.
        /// </summary>
        /// <param name="identifierOrTag">Identifier or tag the item must have.</param>
        /// <param name="excludedIdentifierOrTag">Identifier or tag the item is not allowed to have. Can be empty if there's no need to exclude items.</param>
        public static ImmutableHashSet<ItemPrefab> ResolveItemPrefab(Identifier identifierOrTag, Identifier excludedIdentifierOrTag)
        {
            if (identifierOrTag.IsEmpty)
            {
                throw new ArgumentException("Identifier or tag cannot be empty.", nameof(identifierOrTag));
            }

            if (prevContentPackagesHash == null ||
                !prevContentPackagesHash.Equals(ContentPackageManager.EnabledPackages.MergedHash))
            {
                ItemPrefabCache.Clear();
                prevContentPackagesHash = ContentPackageManager.EnabledPackages.MergedHash;
            }

            Identifier cacheId = (identifierOrTag.Value + excludedIdentifierOrTag.Value).ToIdentifier();

            if (ItemPrefabCache.TryGetValue(cacheId, out ImmutableHashSet<ItemPrefab>? cachedItemPrefabs))
            {
                return cachedItemPrefabs;
            }

            HashSet<ItemPrefab> builder = [];

            if (ItemPrefab.Prefabs.TryGet(identifierOrTag, out ItemPrefab? itemPrefab))
            {

                builder.Add(itemPrefab);
            }

            foreach (ItemPrefab prefab in ItemPrefab.Prefabs)
            {
                if (!excludedIdentifierOrTag.IsEmpty)
                {
                    if (prefab.Identifier == excludedIdentifierOrTag || prefab.Tags.Contains(excludedIdentifierOrTag))
                    {
                        continue;
                    }
                }
                if (prefab.Identifier == identifierOrTag || prefab.Tags.Contains(identifierOrTag))
                {
                    builder.Add(prefab);
                }
            }

            var cache = builder.ToImmutableHashSet();
            ItemPrefabCache[cacheId] = cache;

            return cache;
        }

        protected override void OnOutpostEnteredResourceSpecific(Submarine outpost, PersistentOutpostData outpostData)
        {
            if (GameMain.NetworkMember is { IsClient: true }) { return; }
            SetItemsToMatchResources(outpost, outpostData);
        }

        /// <summary>
        /// Makes the items in the outpost reflect the current amount of the resource, so if the resource has increased/decreased, items will be spawned/removed to match the new amount.
        /// </summary>
        private void SetItemsToMatchResources(Submarine outpost, PersistentOutpostData outpostData)
        {
            ItemsThatDidntFitInContainers.Clear();

            List<Item> outpostItems = GetOutpostItems(outpost).ToList();
            Dictionary<Identifier, int> existingItemCounts = [];

#if DEBUG
            StringBuilder sb = new($"\nItems in {Identifier} (amount: {GetAmount()}):");
            if (itemAmountsByIdentifier.Any())
            {
                sb.Append("\n");
                foreach (var (identifier, amount) in itemAmountsByIdentifier)
                {
                    if (MathUtils.NearlyEqual(amount, 0f)) { continue; }
                    float adjustedAmount = amount * ItemRatio;
                    sb.AppendLine($"‖color:{XMLExtensions.ToStringHex(Color.Green)}‖  - {identifier} {adjustedAmount:0.#}‖color:end‖");
                }
            }
            else
            {
                sb.Append(" none");
            }
#endif

            //first create a dictionary of all items in the outpost that can contribute to the resource (both generated and allowed),
            //so we know to remove items that there should be 0 of (= items that aren't currently in itemAmountsByIdentifier)
            Dictionary<Identifier, float> itemAmountsByIdentifierWithEmptyEntries = 
                itemAmountsByIdentifier.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            //add the entries per item prefab, not per tag: if we e.g. added an empty entry for "ore" only when none of the ores are in itemAmountsByIdentifier,
            //the ores that have dropped to 0 would never get removed as long as some other ore is still left
            foreach (ItemPrefab prefab in outpostItems.Select(static it => it.Prefab).Distinct())
            {
                if (itemAmountsByIdentifierWithEmptyEntries.ContainsKey(prefab.Identifier)) { continue; }
                if (!BelongsIn(prefab)) { continue; }
                //the item is already covered by some entry in the resource (in case some entry is a tag instead of a specific item)
                if (itemAmountsByIdentifier.Keys.Any(prefab.Tags.Contains)) { continue; }
                itemAmountsByIdentifierWithEmptyEntries[prefab.Identifier] = 0.0f;
            }

            //amounts of existing items when depleting gradually (partially depleted items count as a fraction of an item)
            Dictionary<Identifier, float> existingGradualItemAmounts = [];

            //count existing items in the outpost, remove ones that there's too many of based on the resource amount
            foreach (var (identifier, amount) in itemAmountsByIdentifierWithEmptyEntries)
            {
                ImmutableHashSet<ItemPrefab> itemPrefabs = ResolveItemPrefab(identifier, excludedIdentifierOrTag: Identifier.Empty);
                var matchingItems = outpostItems.Where(it => itemPrefabs.Contains(it.Prefab) && IsValidResourceItem(it)).OrderByDescending(GetItemRemovalPriority);

                if (DepletesGradually)
                {
                    float targetAmount = GetTargetItemAmount(identifier, amount, countOtherResources: true, ignoreLimits: outpostData.IsPlayerOwned, allowPartialItems: true);
                    float existingAmount = matchingItems.Sum(GetItemFraction);
                    if (existingAmount > targetAmount + ZeroEpsilon)
                    {
                        float depletedAmount = DepleteItems(matchingItems.ToList(), existingAmount - targetAmount, outpostItems);
                        existingAmount -= depletedAmount;
#if DEBUG
                        sb.AppendLine($"  ‖color:{XMLExtensions.ToStringHex(Color.Red)}‖Depleted x{depletedAmount:0.##} {identifier} in the outpost‖color:end‖");
#endif
                    }
                    existingGradualItemAmounts[identifier] = existingAmount;
                    continue;
                }

                int existingCount = matchingItems.Count();
                int adjustedAmount = AdjustAmountBasedOnGenerationParameters(identifier, amount, countOtherResources: true,
                    /*ignore the spawn thresholds when removing items from player outposts: if a player has obtained high-tier weapons,
                     * we should not remove them just because the value of the resource isn't high enough to spawn them naturally
                     * same with max counts: they only limit how many of the item spawns, items the player has brought in shouldn't be removed*/
                    ignoreLimits: outpostData.IsPlayerOwned);
                if (existingCount > adjustedAmount)
                {
                    float amountToRemove = existingCount - adjustedAmount;
                    int itemsToRemove = (int)MathF.Ceiling(amountToRemove);
                    int removedItemCount = 0;
                    for (int i = 0; i < itemsToRemove; i++)
                    {
                        Item? itemToRemove = matchingItems.FirstOrDefault();
                        if (itemToRemove is null) { break; }

                        if (DepleteItemsInsteadOfRemoving)
                        {
                            //leave the item in place at 0% condition (no longer counted towards the resource)
                            itemToRemove.Condition = 0.0f;
                            outpostItems.Remove(itemToRemove);
                            removedItemCount++;
                            continue;
                        }

                        foreach (var containedItem in itemToRemove.ContainedItems.ToList())
                        {
                            containedItem.Drop(dropper: null);
                            if (itemToRemove.ParentInventory == null || 
                                !itemToRemove.ParentInventory.TryPutItem(containedItem, user: null, allowedSlots: containedItem.AllowedSlots))
                            {
                                //putting the item in the parent inventory failed, try finding a suitable container somewhere in the sub
                                if (itemToRemove.Submarine.FindContainerFor(containedItem, onlyPrimary: true) is Item replacementContainer)
                                {
                                    replacementContainer.GetComponent<ItemContainer>()?.Inventory.TryPutItem(containedItem, user: null);
                                }
                            }
                        }

                        outpostItems.Remove(itemToRemove);
                        itemToRemove.Remove();
                        removedItemCount++;
#if SERVER
                        Entity.Spawner?.CreateNetworkEvent(new EntitySpawner.RemoveEntity(itemToRemove));
#endif
                    }
#if DEBUG
                    sb.AppendLine($"  ‖color:{XMLExtensions.ToStringHex(Color.Red)}‖Removed x{removedItemCount} {identifier} from the outpost‖color:end‖");
#endif
                }                             
                existingItemCounts[identifier] = existingCount;
            }

            List<(ItemPrefab prefab, GeneratedItemParams generatedItem, int amount, float existingAmount, float lastItemCondition)> itemsToSpawn = [];
            foreach (var (identifier, amount) in itemAmountsByIdentifier)
            {
                int adjustedAmount;
                float existingAmount;
                //condition of the last spawned item (as a fraction of the max condition), used to spawn partially depleted items when depleting gradually
                float lastItemCondition = 1.0f;
                if (DepletesGradually)
                {
                    float targetAmount = GetTargetItemAmount(identifier, amount, countOtherResources: true, ignoreLimits: false, allowPartialItems: true);
                    existingGradualItemAmounts.TryGetValue(identifier, out existingAmount);
                    float missingAmount = Math.Max(targetAmount - existingAmount, 0.0f);
                    adjustedAmount = (int)MathF.Floor(missingAmount + ZeroEpsilon);
                    float partialAmount = missingAmount - adjustedAmount;
                    if (partialAmount >= MinPartialItemCondition)
                    {
                        //top up existing partially depleted items first, so we don't end up with lots of partially depleted items
                        ImmutableHashSet<ItemPrefab> itemPrefabs = ResolveItemPrefab(identifier, excludedIdentifierOrTag: Identifier.Empty);
                        partialAmount -= TopUpPartialItems(outpostItems.Where(it => itemPrefabs.Contains(it.Prefab) && IsValidResourceItem(it)), partialAmount);
                    }
                    if (partialAmount >= MinPartialItemCondition)
                    {
                        adjustedAmount++;
                        lastItemCondition = partialAmount;
                    }
                }
                else
                {
                    adjustedAmount = AdjustAmountBasedOnGenerationParameters(identifier, amount, countOtherResources: true, ignoreLimits: false);
                    if (existingItemCounts.TryGetValue(identifier, out int existingCount))
                    {
                        adjustedAmount = (int)MathF.Floor(adjustedAmount - existingCount);
                    }
                    existingAmount = existingCount;
                }
                if (adjustedAmount <= 0) { continue; }

                if (!ItemPrefab.Prefabs.TryGet(identifier, out ItemPrefab? itemPrefab))
                {
                    DebugConsole.ThrowError($"Failed to find item prefab with identifier \"{identifier}\" for resource {Prefab.Identifier}. Cannot spawn items.");
                    continue;
                }
                GeneratedItemParams generatedItem =
                    FindGeneratedItemParams(gi => gi.MatchesItemPrefab(itemPrefab)) ?? default;

                itemsToSpawn.Add((itemPrefab, generatedItem, adjustedAmount, existingAmount, lastItemCondition));
            }
            itemsToSpawn.Sort((a, b) =>
            {
                return b.generatedItem.SpawnPriority.CompareTo(a.generatedItem.SpawnPriority);
            });

            List<Item> spawnedItems = [];
            //spawn more items there's too few of based on the resource amount
            foreach ((ItemPrefab prefab, GeneratedItemParams generatedItem, int amount, float existingAmount, float lastItemCondition) in itemsToSpawn)
            {
                ImmutableArray<Item> items = SpawnItemPerk.SpawnItem(
                    prefab: prefab,
                    amount: amount,
                    submarine: outpost,
                    priorityContainerTag: Identifier.Empty,
                    fallbackContainerTag: Tags.FallbackLocker,
                    /*not that the items only spawn in crates if no suitable items are found*/
                    spawnInCrates: true,
                    showCargoIcon: false);

                if (lastItemCondition < 1.0f && items.Any())
                {
                    Item partialItem = items.Last();
                    partialItem.Condition = partialItem.MaxCondition * lastItemCondition;
                }

#if DEBUG
                sb.AppendLine($"  ‖color:{XMLExtensions.ToStringHex(Color.Orange)}‖Spawned x{amount} {prefab.Name} on the outpost‖color:end‖ (existing: x{existingAmount:0.##})");
#endif
                int itemsThatDidntFitInContainersCount = 0;
                foreach (var item in items)
                {
                    if (item.ParentInventory == null)
                    {
                        if (generatedItem is { SpawnIfNoSpaceInContainer: false })
                        {
                            ItemsThatDidntFitInContainers.Add(item);
                            Entity.Spawner?.AddEntityToRemoveQueue(item);
                            itemsThatDidntFitInContainersCount++;
                        }
                    }
                }
#if DEBUG
                if (itemsThatDidntFitInContainersCount > 0)
                {
                    sb.AppendLine($"  ‖color:{XMLExtensions.ToStringHex(Color.Red)}‖x{itemsThatDidntFitInContainersCount} of {prefab.Name} did not fit in containers, and were not spawned.‖color:end‖");
                }
#endif
                spawnedItems.AddRange(items);
                foreach (Item it in items)
                {
                    it.SpawnedInNonPlayerOutpostOrSubmarine = true;
                    if (!outpostData.IsPlayerOwned)
                    {
                        it.AllowStealing = outpost.Info.OutpostGenerationParams.AllowStealing;
                    }
                    else
                    {
                        // Sellable, like the items loaded with the player outpost (see OutpostGenerator.SetAllowSellingOutpostItems)
                        it.AllowSellingIfSpawnedInCurrentOutpost = true;
                    }
                }
            }

            //if some spawned items can be contained inside each other (e.g. tanks in suits/tools), contain them
            foreach (var spawnedItem in spawnedItems)
            {
                foreach (var otherItem in spawnedItems)
                {
                    if (spawnedItem == otherItem) { continue; }
                    ItemContainer container = otherItem.GetComponent<ItemContainer>();
                    if (container == null) { continue; }
                    if (spawnedItem.IsContainerPreferred(container, out bool _, out bool isSecondary) && !isSecondary)
                    {
                        if (container.Inventory.TryPutItem(spawnedItem, user: null))
                        {
                            break;
                        }
                    }
                }
            }
#if DEBUG
            DebugLog(sb.ToString().TrimEnd('\n'));
#endif
        }

        /// <summary>
        /// Get the number of full items that should be present on the outpost based on the resource. See <see cref="GetTargetItemAmount"/>.
        /// </summary>
        private int AdjustAmountBasedOnGenerationParameters(Identifier itemIdentifier, float baseAmount, bool countOtherResources, bool ignoreLimits)
            => (int)GetTargetItemAmount(itemIdentifier, baseAmount, countOtherResources, ignoreLimits, allowPartialItems: false);

        /// <summary>
        /// Get the number of items that should be present on the outpost based on the resource,
        /// taking into account the <see cref="ItemRatio"/>, spawn thresholds etc.
        /// </summary>
        /// <param name="countOtherResources">If true, other resources that include the item are also taken into account
        /// (meaning, if there's e.g. 15 tanks in some other resource, there should be at least 15 tanks in the outpost even if this resource has fewer of those)</param>
        /// <param name="ignoreLimits">If true, the spawn thresholds and max counts of the generated items are ignored.</param>
        /// <param name="allowPartialItems">If false, the amount is rounded down to full items. If true, the amount can include a partial item (e.g. 2.5 = 2 full items and one at 50% condition, see <see cref="DepleteItemsGradually"/>).</param>
        private float GetTargetItemAmount(Identifier itemIdentifier, float baseAmount, bool countOtherResources, bool ignoreLimits, bool allowPartialItems)
        {
            float itemAmount = baseAmount * ItemRatio;
            if (!allowPartialItems)
            {
                itemAmount = MathF.Floor(itemAmount);
            }
            //player outposts have no faction: don't apply the limits (max count, threshold) of some other faction's entry to items the player has brought in
            //(the item is still counted as part of the resource though, see FindGeneratedItemParams)
            if (FindGeneratedItemParams(p => p.Identifier == itemIdentifier) is { } giParams &&
                (IsApplicable(giParams) || !Manager.OutpostData.IsPlayerOwned))
            {
                if (!ignoreLimits)
                {
                    if (Amount < giParams.Threshold) { return 0; }
                    itemAmount = Math.Min(itemAmount, GetMaxCount(giParams));
                }
            }
            if (countOtherResources)
            {
                //count the items from other resources too:
                //we shouldn't be e.g. removing all oxygen tanks from the outpost if there's 15 of them in goods_essentials but 0 in workshop_goods
                //the identifier may be a tag (e.g. "floraresearchitem") while other resources track specific items (e.g. "fiberplant"),
                //so check how many of each matching item the other resources need
                float otherResourcesItemAmount = 0;
                foreach (ItemPrefab prefab in ResolveItemPrefab(itemIdentifier, excludedIdentifierOrTag: Identifier.Empty))
                {
                    float maxAmountForPrefab = 0;
                    foreach (Resource otherResource in Manager.Resources)
                    {
                        if (otherResource == this) { continue; }
                        if (otherResource is ItemResource otherItemResource &&
                            otherItemResource.itemAmountsByIdentifier.TryGetValue(prefab.Identifier, out float otherAmount))
                        {
                            float otherAdjustedAmount = otherItemResource.GetTargetItemAmount(prefab.Identifier, otherAmount, countOtherResources: false, ignoreLimits: ignoreLimits, allowPartialItems: allowPartialItems);
                            maxAmountForPrefab = Math.Max(maxAmountForPrefab, otherAdjustedAmount);
                        }
                    }
                    otherResourcesItemAmount += maxAmountForPrefab;
                }
                itemAmount = Math.Max(itemAmount, otherResourcesItemAmount);
            }
            return itemAmount;
        }

        /// <summary>
        /// How much of a full item the item counts as when depleting items gradually (0-1, based on the condition). See <see cref="DepleteItemsGradually"/>.
        /// </summary>
        private static float GetItemFraction(Item item)
        {
            if (item.MaxCondition <= 0.0f) { return 1.0f; }
            return MathHelper.Clamp(item.Condition / item.MaxCondition, 0.0f, 1.0f);
        }

        /// <summary>
        /// Reduces the condition of the items by the given amount (in items, e.g. 1.5 = one and a half full items). See <see cref="DepleteItemsGradually"/>.
        /// Items are depleted in the same order they'd be removed in (items in reactors and such last), and the most depleted ones first,
        /// so we don't end up with lots of partially depleted items.
        /// </summary>
        /// <param name="outpostItems">Fully depleted items are removed from this list.</param>
        /// <returns>The amount that was depleted (in items)</returns>
        private float DepleteItems(IEnumerable<Item> items, float amountToDeplete, List<Item> outpostItems)
        {
            float depletedAmount = 0.0f;
            foreach (Item item in items.OrderByDescending(GetItemRemovalPriority).ThenBy(GetItemFraction).ToList())
            {
                float remainingAmountToDeplete = amountToDeplete - depletedAmount;
                if (remainingAmountToDeplete <= ZeroEpsilon) { break; }

                float itemFraction = GetItemFraction(item);
                float newItemFraction = itemFraction - Math.Min(itemFraction, remainingAmountToDeplete);
                if (newItemFraction < MinPartialItemCondition)
                {
                    newItemFraction = 0.0f;
                    outpostItems.Remove(item);
                }
                item.Condition = item.MaxCondition * newItemFraction;
                depletedAmount += itemFraction - newItemFraction;
            }
            return depletedAmount;
        }

        /// <summary>
        /// Increases the condition of partially depleted items by the given amount (in items). See <see cref="DepleteItemsGradually"/>.
        /// The least depleted items are topped up first.
        /// </summary>
        /// <returns>The amount that was added (in items)</returns>
        private static float TopUpPartialItems(IEnumerable<Item> items, float amountToAdd)
        {
            float addedAmount = 0.0f;
            foreach (Item item in items.Where(it => GetItemFraction(it) < 1.0f).OrderByDescending(GetItemFraction).ToList())
            {
                float remainingAmountToAdd = amountToAdd - addedAmount;
                if (remainingAmountToAdd <= ZeroEpsilon) { break; }

                float itemFraction = GetItemFraction(item);
                float newItemFraction = Math.Min(itemFraction + remainingAmountToAdd, 1.0f);
                item.Condition = item.MaxCondition * newItemFraction;
                addedAmount += newItemFraction - itemFraction;
            }
            return addedAmount;
        }

        /// <summary>
        /// Handles refilling containers that are configured to be kept non-empty (e.g. placing rods back in the reactor if they've been removed).
        /// 
        /// Note that this must be handled after we've handled removing/adding items based on all the resources (see SetResourceToMatchItems).
        /// Otherwise we would need some extra logic for handling cases where e.g. advancedfuel runs out and a rod gets removed from the reactor,
        /// which would require finding more suitable fuel from some other resource like basicfuel.
        /// </summary>
        public void FixEmptiedContainers(Submarine outpost)
        {
            if (GameMain.NetworkMember is { IsClient: true }) { return; }
            if (AvoidEmptyingContainer.IsEmpty) { return; }

            List<Item> outpostItems = GetOutpostItems(outpost).ToList();
            foreach (var potentialContainer in outpostItems.Where(it => it.HasTag(AvoidEmptyingContainer)))
            {
                ItemContainer container = potentialContainer.GetComponent<ItemContainer>();
                if (container == null || !container.IsEmpty(user: null)) { continue; }
                //replace with the item that has the highest removal priority (item that's in the "least appropriate" container)
                var suitableReplacements = outpostItems.Where(ResourceMatchesItem).OrderByDescending(GetItemRemovalPriority);
                if (suitableReplacements.Any())
                {
                    container.Inventory.TryPutItem(suitableReplacements.First(), user: null);
                }
            }
        }


        private int GetItemRemovalPriority(Item item)
        {
            //how to prioritize which items to remove when the amount of the resource has decreased
            // 5: items that aren't in any inventory/container
            // 4: items that aren't in a preferred container
            // 3: items in a secondary container
            // 2: items in a preferred container
            // 0: items in reactors or containers bots aren't allowed to take items from, these should be the last thing to remove

            // 1: everything else

            if (item.ParentInventory == null)
            {
                return 5;
            }
            else if (item.Container != null)
            {
                if ((!AvoidEmptyingContainer.IsEmpty && item.Container.HasTag(AvoidEmptyingContainer)) || 
                    item.Container.HasTag(Tags.DontTakeItems) || 
                    item.Container.HasTag(Tags.DontTakeItemsUnlessEmergency)) 
                { 
                    return 0; 
                }
                else if (!item.IsContainerPreferred(item.Container.GetComponent<ItemContainer>(), out bool isPreferencesDefined, out bool isSecondary))
                {
                    return 4;
                }
                else
                {
                    return isSecondary ? 3 : 2;
                }
            }
            return 1;
        }

        private bool ResourceMatchesItem(Item item)
        {
            if (!IsValidResourceItem(item)) { return false; }
            if (generatedItems.Any(gi => gi.MatchesItemPrefab(item.Prefab))) { return true; }
            if (allowedItems.Any(allowed => allowed.MatchesItemPrefab(item.Prefab))) { return true; }
            return false;
        }

        protected override void OnOutpostExitedResourceSpecific(Submarine outpost, PersistentOutpostData outpostData, bool shouldCreateLogs)
        {
            if (GameMain.NetworkMember is { IsClient: true }) { return; }
            SetResourceToMatchItems(outpost, shouldCreateLogs);
            //the items have been counted, no need to keep to keep references to removed items after leaving the outpost
            ItemsThatDidntFitInContainers.Clear();
        }

        /// <summary>
        /// Updates the resource to match the items in the outpost mid-round, without considering the outpost exited.
        /// </summary>
        public void SyncWithLoadedOutpostItems(Submarine outpost)
        {
            if (GameMain.NetworkMember is { IsClient: true }) { return; }
            SetResourceToMatchItems(outpost, shouldCreateLogs: false);
            UpdateResourceStatusIndicators();
        }

        /// <summary>
        /// Makes the resource amounts reflect the items currently in the outpost, 
        /// so if the player has brought items to the outpost or taken items from it, 
        /// the resource amounts will be updated to match.
        /// </summary>
        private void SetResourceToMatchItems(Submarine outpost, bool shouldCreateLogs)
        {
            List<string> debugLogs = [];
            float prevAmount = Amount;
            Resource resourceToLog = GetResourceToLogAs() ?? this;
            float oldAmountToLog = resourceToLog.Amount;
            List<Item> outpostItems = 
                GetOutpostItems(outpost)
                .Union(ItemsThatDidntFitInContainers)
                .Where(it => !DepleteItemsInsteadOfRemoving || it.Condition > 0.0f)
                .ToList();

            Dictionary<Identifier, float> existingItemAmountsByIdentifier = [];
            //set zero as the initial amount before counting how many there actually are
            foreach ((Identifier identifier, float newAmount) in itemAmountsByIdentifier)
            {
                existingItemAmountsByIdentifier[identifier] = 0.0f;
            }
            foreach (var outpostItem in outpostItems)
            {
                Identifier itemIdentifier = outpostItem.Prefab.Identifier;

                GeneratedItemParams generatedItem =
                    FindGeneratedItemParams(gi => gi.MatchesItemPrefab(outpostItem.Prefab)) ?? default;

                bool allowsAnyItem = allowedItems.None() && generatedItems.None();

                float amountToAdd = 0.0f;
                if (!generatedItem.Identifier.IsEmpty || 
                    allowedItems.Any(allowedItem => allowedItem.MatchesItemPrefab(outpostItem.Prefab)) ||
                    (allowsAnyItem && existingItemAmountsByIdentifier.ContainsKey(itemIdentifier)))
                {
                    amountToAdd = 1.0f / ItemRatio;
                    if (DepletesGradually)
                    {
                        amountToAdd *= GetItemFraction(outpostItem);
                    }
                }
                if (amountToAdd > 0.0f)
                {
                    if (!existingItemAmountsByIdentifier.ContainsKey(itemIdentifier))
                    {
                        existingItemAmountsByIdentifier[itemIdentifier] = 0;
                    }
                    existingItemAmountsByIdentifier[itemIdentifier] += amountToAdd;
                }
            }

            foreach ((Identifier identifier, float newAmount) in existingItemAmountsByIdentifier)
            {
                itemAmountsByIdentifier.TryGetValue(identifier, out float previousAmount);
                bool amountChanged = DepletesGradually ?
                    //partially depleted items count towards the resource, so the resource should match the items exactly
                    //(otherwise we could be left with e.g. 0.99 of an item that's been depleted)
                    Math.Abs(previousAmount - newAmount) > ZeroEpsilon :
                    (int)Math.Floor(previousAmount * ItemRatio) != (int)Math.Floor(newAmount * ItemRatio);
                if (amountChanged)
                {
                    debugLogs.Add($"The amount of items matching {identifier} has changed during the round, modifying the resource {previousAmount} -> {newAmount}");
                    itemAmountsByIdentifier[identifier] = newAmount;
                }
            }
#if CLIENT && DEBUG
            if (debugLogs.Any())
            {
                DebugConsole.NewMessage(
                    $"Resource {Identifier} changed from {prevAmount} -> {Amount}.\n" +
                    string.Join("\n", debugLogs.Select(line => $"  - ‖color:{XMLExtensions.ToStringHex(GUIStyle.Orange)}‖{line}‖color:end‖")));
            }
#endif
            if (shouldCreateLogs)
            {
                Manager.OutpostData.StoreResourceChangeEvent(EconomyLogEntryGeneric.Create(LogType.PlayerResourceTransfer, resourceToLog, oldAmountToLog, resourceToLog.Amount, Manager.Location));
            }
        }

        private IEnumerable<Item> GetOutpostItems(Submarine outpost)
        {
            //note that even though we're calling with alsoFromConnectedSubs: true,
            //items from subs of a different type aren't included (e.g. items in the docked sub)
            return outpost.GetItems(alsoFromConnectedSubs: true).Where(it => IsValidResourceItem(it));
        }

        /// <summary>
        /// Is the item a depleted (0% condition) item that no longer counts towards this resource? See <see cref="DepleteItemsInsteadOfRemoving"/>.
        /// </summary>
        public bool IsDepleted(Item item)
            => DepleteItemsInsteadOfRemoving && item.Condition <= 0.0f && BelongsIn(item.Prefab);

        private bool IsValidResourceItem(Item item)
        {
            if (item.IsHidden || item.Removed || item.NonInteractable) { return false; }
            if (DepleteItemsInsteadOfRemoving && item.Condition <= 0.0f) { return false; }
            if (item.GetComponent<Wire>() is Wire wire)
            {
                //wires connected to something are ignored (we don't want to rip out wiring due to some resource going down!)
                if (wire.Connections.Any(c => c != null)) { return false; }
            }
            if (item.GetComponent<Holdable>() is Holdable { Attached: true })
            {
                //ignore attached items too, we don't want to rip out signal components etc either
                return false;
            }
            if (item.ParentInventory?.Owner is Item parentItem && parentItem.GetComponent<Planter>() != null)
            {
                //don't consider planted seeds/mushroom and such valid for the item resource - we don't want some "rawfood" resource going down uprooting our plants!
                //and a planted seed shouldn't count as edible food any longer anyway
                return false;
            }

            if (item.GetRootInventoryOwner() is Character ownerCharacter)
            {
                return false;
            }

            return true;
        }

        public bool BelongsIn(ItemPrefab prefab)
        {
            foreach (AllowedItemParams allowedItem in allowedItems)
            {
                if (allowedItem.MatchesItemPrefab(prefab))
                {
                    return true;
                }
            }

            foreach (GeneratedItemParams item in generatedItems)
            {
                if (item.MatchesItemPrefab(prefab))
                {
                    return true;
                }
            }

            return false;
        }

        private static void DebugLog(string msg)
        {
#if CLIENT
            DebugConsole.NewMessage(msg);
#else
            DebugConsole.NewMessage(RichString.Rich(msg).SanitizedValue);
#endif
        }
    }
}