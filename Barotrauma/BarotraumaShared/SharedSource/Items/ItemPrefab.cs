using Barotrauma.IO;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Barotrauma.Extensions;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace Barotrauma
{
    readonly struct DeconstructItem
    {
        public readonly Identifier ItemIdentifier;
        //number of items to output
        public readonly int Amount;
        //minCondition does <= check, meaning that below or equal to min condition will be skipped.
        public readonly float MinCondition;
        //maxCondition does > check, meaning that above this max the deconstruct item will be skipped.
        public readonly float MaxCondition;
        //Condition of item on creation
        public readonly float OutConditionMin, OutConditionMax;
        //should the condition of the deconstructed item be copied to the output items
        public readonly bool CopyCondition;

        //tag/identifier of the deconstructor(s) that can be used to deconstruct the item into this
        public readonly string[] RequiredDeconstructor;
        //tag/identifier of other item(s) that that need to be present in the deconstructor to deconstruct the item into this
        public readonly string[] RequiredOtherItem;
        //text to display on the deconstructor's activate button when this output is available
        public readonly string ActivateButtonText;
        public readonly string InfoText;
        public readonly string InfoTextOnOtherItemMissing;

        public readonly float Commonness;

        public DeconstructItem(XElement element, Identifier parentDebugName)
        {
            ItemIdentifier = element.GetAttributeIdentifier("identifier", "");
            Amount = element.GetAttributeInt("amount", 1);
            MinCondition = element.GetAttributeFloat("mincondition", -0.1f);
            MaxCondition = element.GetAttributeFloat("maxcondition", 1.0f);
            OutConditionMin = element.GetAttributeFloat("outconditionmin", element.GetAttributeFloat("outcondition", 1.0f));
            OutConditionMax = element.GetAttributeFloat("outconditionmax", element.GetAttributeFloat("outcondition", 1.0f));
            CopyCondition = element.GetAttributeBool("copycondition", false);
            Commonness = element.GetAttributeFloat("commonness", 1.0f);
            RequiredDeconstructor = element.GetAttributeStringArray("requireddeconstructor", 
                element.Parent?.GetAttributeStringArray("requireddeconstructor", new string[0]) ?? new string[0]);
            RequiredOtherItem = element.GetAttributeStringArray("requiredotheritem", new string[0]);
            ActivateButtonText = element.GetAttributeString("activatebuttontext", string.Empty);
            InfoText = element.GetAttributeString("infotext", string.Empty);
            InfoTextOnOtherItemMissing = element.GetAttributeString("infotextonotheritemmissing", string.Empty);
        }

        public bool IsValidDeconstructor(Item deconstructor)
        {
            return RequiredDeconstructor.Length == 0 || RequiredDeconstructor.Any(r => deconstructor.HasTag(r) || deconstructor.Prefab.Identifier == r);
        }
    }

    class FabricationRecipe
    {
        public abstract class RequiredItem
        {
            public abstract IEnumerable<ItemPrefab> ItemPrefabs { get; }
            public abstract UInt32 UintIdentifier { get; }

            public abstract bool MatchesItem(Item item);

            public abstract ItemPrefab FirstMatchingPrefab { get; }

            public RequiredItem(int amount, float minCondition, float maxCondition, bool useCondition)
            {
                Amount = amount;
                MinCondition = minCondition;
                MaxCondition = maxCondition;
                UseCondition = useCondition;
            }
            public readonly int Amount;
            public readonly float MinCondition;
            public readonly float MaxCondition;
            public readonly bool UseCondition;

            public bool IsConditionSuitable(float conditionPercentage)
            {
                float normalizedCondition = conditionPercentage / 100.0f;
                if (MathUtils.NearlyEqual(normalizedCondition, MinCondition) || MathUtils.NearlyEqual(normalizedCondition, MaxCondition))
                {
                    return true;
                }
                else if (normalizedCondition >= MinCondition && normalizedCondition <= MaxCondition)
                {
                    return true;
                }
                return false;
            }
        }

        public class RequiredItemByIdentifier : RequiredItem
        {
            public readonly Identifier ItemPrefabIdentifier;

            public ItemPrefab ItemPrefab => ItemPrefab.Prefabs.TryGet(ItemPrefabIdentifier, out var prefab) ? prefab
                : MapEntityPrefab.FindByName(ItemPrefabIdentifier.Value) as ItemPrefab ?? throw new Exception($"No ItemPrefab with identifier or name \"{ItemPrefabIdentifier}\"");
            
            public override UInt32 UintIdentifier { get; }

            public override IEnumerable<ItemPrefab> ItemPrefabs => ItemPrefab.ToEnumerable();

            public override ItemPrefab FirstMatchingPrefab => ItemPrefab;

            public override bool MatchesItem(Item item)
            {
                return item?.Prefab.Identifier == ItemPrefabIdentifier;
            }

            public RequiredItemByIdentifier(Identifier itemPrefab, int amount, float minCondition, float maxCondition, bool useCondition) : base(amount, minCondition, maxCondition, useCondition)
            {
                ItemPrefabIdentifier = itemPrefab;
                using MD5 md5 = MD5.Create();
                UintIdentifier = ToolBox.IdentifierToUint32Hash(itemPrefab, md5);
            }
        }

        public class RequiredItemByTag : RequiredItem
        {
            public readonly Identifier Tag;

            public override UInt32 UintIdentifier { get; }

            public override IEnumerable<ItemPrefab> ItemPrefabs => ItemPrefab.Prefabs.Where(p => p.Tags.Contains(Tag));

            public override ItemPrefab FirstMatchingPrefab => ItemPrefab.Prefabs.FirstOrDefault(p => p.Tags.Contains(Tag));

            public override bool MatchesItem(Item item)
            {
                if (item == null) { return false; }
                return item.HasTag(Tag);
            }

            public RequiredItemByTag(Identifier tag, int amount, float minCondition, float maxCondition, bool useCondition) : base(amount, minCondition, maxCondition, useCondition)
            {
                Tag = tag;
                using MD5 md5 = MD5.Create();
                UintIdentifier = ToolBox.IdentifierToUint32Hash(tag, md5);
            }
        }

        public readonly Identifier TargetItemPrefabIdentifier;
        public ItemPrefab TargetItem => ItemPrefab.Prefabs[TargetItemPrefabIdentifier];

        private readonly Lazy<LocalizedString> displayName;
        public LocalizedString DisplayName
            => ItemPrefab.Prefabs.ContainsKey(TargetItemPrefabIdentifier) ? displayName.Value : "";
        public readonly ImmutableArray<RequiredItem> RequiredItems;
        public readonly ImmutableArray<Identifier> SuitableFabricatorIdentifiers;
        public readonly float RequiredTime;
        public readonly int RequiredMoney;
        public readonly bool RequiresRecipe;
        public readonly float OutCondition; //Percentage-based from 0 to 1
        public readonly ImmutableArray<Skill> RequiredSkills;
        public readonly uint RecipeHash;
        public readonly int Amount;
        public readonly int? Quality;

        /// <summary>
        /// How many of this item the fabricator can create (< 0 = unlimited)
        /// </summary>
        public readonly int FabricationLimitMin, FabricationLimitMax;

        public FabricationRecipe(XElement element, Identifier itemPrefab)
        {
            TargetItemPrefabIdentifier = itemPrefab;
            var displayNameIdentifier = element.GetAttributeIdentifier("displayname", "");
            displayName = new Lazy<LocalizedString>(() => displayNameIdentifier.IsEmpty
                ? TargetItem.Name
                : TextManager.GetWithVariable($"DisplayName.{displayNameIdentifier}", "[itemname]", TargetItem.Name));

            SuitableFabricatorIdentifiers = element.GetAttributeIdentifierArray("suitablefabricators", Array.Empty<Identifier>()).ToImmutableArray();

            var requiredSkills = new List<Skill>();
            RequiredTime = element.GetAttributeFloat("requiredtime", 1.0f);
            RequiredMoney = element.GetAttributeInt("requiredmoney", 0);
            OutCondition = element.GetAttributeFloat("outcondition", 1.0f);
            if (OutCondition > 1.0f)
            {
                DebugConsole.AddWarning($"Error in \"{itemPrefab}\"'s fabrication recipe: out condition is above 100% ({OutCondition * 100}).");
            }
            var requiredItems = new List<RequiredItem>();
            RequiresRecipe = element.GetAttributeBool("requiresrecipe", false);
            Amount = element.GetAttributeInt("amount", 1);

            int limitDefault = element.GetAttributeInt("fabricationlimit", -1);
            FabricationLimitMin = element.GetAttributeInt(nameof(FabricationLimitMin), limitDefault);
            FabricationLimitMax = element.GetAttributeInt(nameof(FabricationLimitMax), limitDefault);

            if (element.GetAttribute(nameof(Quality)) != null)
            {
                Quality = element.GetAttributeInt(nameof(Quality), 0);
            }

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "requiredskill":
                        if (subElement.Attribute("name") != null)
                        {
                            DebugConsole.ThrowError("Error in fabricable item " + itemPrefab + "! Use skill identifiers instead of names.");
                            continue;
                        }

                        requiredSkills.Add(new Skill(
                            subElement.GetAttributeIdentifier("identifier", ""),
                            subElement.GetAttributeInt("level", 0)));
                        break;
                    case "item":
                    case "requireditem":
                        Identifier requiredItemIdentifier = subElement.GetAttributeIdentifier("identifier", Identifier.Empty);
                        Identifier requiredItemTag = subElement.GetAttributeIdentifier("tag", Identifier.Empty);
                        if (requiredItemIdentifier == Identifier.Empty && requiredItemTag == Identifier.Empty)
                        {
                            DebugConsole.ThrowError("Error in fabricable item " + itemPrefab + "! One of the required items has no identifier or tag.");
                            continue;
                        }

                        float minCondition = subElement.GetAttributeFloat("mincondition", 1.0f);
                        float maxCondition = subElement.GetAttributeFloat("maxcondition", 1.0f);
                        //Substract mincondition from required item's condition or delete it regardless?
                        bool useCondition = subElement.GetAttributeBool("usecondition", true);
                        int amount = subElement.GetAttributeInt("count", subElement.GetAttributeInt("amount", 1));

                        if (requiredItemIdentifier != Identifier.Empty)
                        {
                            var existing = requiredItems.FindIndex(r =>
                                r is RequiredItemByIdentifier ri &&
                                ri.ItemPrefabIdentifier == requiredItemIdentifier &&
                                MathUtils.NearlyEqual(r.MinCondition, minCondition) &&
                                MathUtils.NearlyEqual(r.MaxCondition, maxCondition));
                            if (existing >= 0)
                            {
                                amount += requiredItems[existing].Amount;
                                requiredItems.RemoveAt(existing);
                            }
                            requiredItems.Add(new RequiredItemByIdentifier(requiredItemIdentifier, amount, minCondition, maxCondition, useCondition));
                        }
                        else
                        {
                            var existing = requiredItems.FindIndex(r =>
                                r is RequiredItemByTag rt &&
                                rt.Tag == requiredItemTag &&
                                MathUtils.NearlyEqual(r.MinCondition, minCondition) &&
                                MathUtils.NearlyEqual(r.MaxCondition, maxCondition));
                            if (existing >= 0)
                            {
                                amount += requiredItems[existing].Amount;
                                requiredItems.RemoveAt(existing);
                            }
                            requiredItems.Add(new RequiredItemByTag(requiredItemTag, amount, minCondition, maxCondition, useCondition));
                        }
                        break;
                }
            }

            this.RequiredSkills = requiredSkills.ToImmutableArray();
            this.RequiredItems = requiredItems.ToImmutableArray();

            RecipeHash = GenerateHash();
        }

        private uint GenerateHash()
        {
            using var md5 = MD5.Create();
            uint outputId = ToolBox.IdentifierToUint32Hash(TargetItemPrefabIdentifier, md5);

            var requiredItems = string.Join(':', RequiredItems
                .Select(i => i.UintIdentifier)
                .Select(i => string.Join(',', i)));

            var requiredSkills = string.Join(':', RequiredSkills.Select(s => $"{s.Identifier}:{s.Level}"));

            uint retVal = ToolBox.StringToUInt32Hash($"{Amount}|{outputId}|{RequiredTime}|{requiredItems}|{requiredSkills}", md5);
            if (retVal == 0) { retVal = 1; }
            return retVal;
        }
    }

    class PreferredContainer
    {
        public readonly ImmutableHashSet<Identifier> Primary;
        public readonly ImmutableHashSet<Identifier> Secondary;

        public readonly float SpawnProbability;
        public readonly float MaxCondition;
        public readonly float MinCondition;
        public readonly int MinAmount;
        public readonly int MaxAmount;
        // Overrides min and max, if defined.
        public readonly int Amount;
        public readonly bool CampaignOnly;
        public readonly bool NotCampaign;
        public readonly bool TransferOnlyOnePerContainer;
        public readonly bool AllowTransfersHere = true;

        public PreferredContainer(XElement element)
        {
            Primary = XMLExtensions.GetAttributeIdentifierArray(element, "primary", Array.Empty<Identifier>()).ToImmutableHashSet();
            Secondary = XMLExtensions.GetAttributeIdentifierArray(element, "secondary", Array.Empty<Identifier>()).ToImmutableHashSet();
            SpawnProbability = element.GetAttributeFloat("spawnprobability", 0.0f);
            MinAmount = element.GetAttributeInt("minamount", 0);
            MaxAmount = Math.Max(MinAmount, element.GetAttributeInt("maxamount", 0));
            Amount = element.GetAttributeInt("amount", 0);
            MaxCondition = element.GetAttributeFloat("maxcondition", 100f);
            MinCondition = element.GetAttributeFloat("mincondition", 0f);
            CampaignOnly = element.GetAttributeBool("campaignonly", CampaignOnly);
            NotCampaign = element.GetAttributeBool("notcampaign", NotCampaign);
            TransferOnlyOnePerContainer = element.GetAttributeBool("TransferOnlyOnePerContainer", TransferOnlyOnePerContainer);
            AllowTransfersHere = element.GetAttributeBool("AllowTransfersHere", AllowTransfersHere);

            if (element.GetAttribute("spawnprobability") == null)
            {
                //if spawn probability is not defined but amount is, assume the probability is 1
                if (MaxAmount > 0 || Amount > 0)
                {
                    SpawnProbability = 1.0f;
                } 
            }
            else if (element.GetAttribute("minamount") == null && element.GetAttribute("maxamount") == null && element.GetAttribute("amount") == null)
            {
                //spawn probability defined but amount isn't, assume amount is 1
                MinAmount = MaxAmount = Amount = 1;
                SpawnProbability = element.GetAttributeFloat("spawnprobability", 0.0f);
            }
        }
    }

    class SwappableItem
    {
        public int BasePrice { get; }

        public readonly bool CanBeBought;

        public readonly Identifier ReplacementOnUninstall;

        public string SpawnWithId;

        public string SwapIdentifier;

        public readonly Vector2 SwapOrigin;

        public List<(Identifier requiredTag, Identifier swapTo)> ConnectedItemsToSwap = new List<(Identifier requiredTag, Identifier swapTo)>();

        public readonly Sprite SchematicSprite;

        public int GetPrice(Location location = null)
        {
            int price = BasePrice;
            return location?.GetAdjustedMechanicalCost(price) ?? price;
        }

        public SwappableItem(ContentXElement element)
        {
            BasePrice = Math.Max(element.GetAttributeInt("price", 0), 0);
            SwapIdentifier = element.GetAttributeString("swapidentifier", string.Empty);
            CanBeBought = element.GetAttributeBool("canbebought", BasePrice != 0);
            ReplacementOnUninstall = element.GetAttributeIdentifier("replacementonuninstall", "");
            SwapOrigin = element.GetAttributeVector2("origin", Vector2.One);
            SpawnWithId = element.GetAttributeString("spawnwithid", string.Empty);

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "schematicsprite":
                        SchematicSprite = new Sprite(subElement);
                        break;
                    case "swapconnecteditem":
                        ConnectedItemsToSwap.Add(
                            (subElement.GetAttributeIdentifier("tag", ""), 
                            subElement.GetAttributeIdentifier("swapto", "")));
                        break;
                }
            }
        }
    }

    partial class ItemPrefab : MapEntityPrefab, IImplementsVariants<ItemPrefab>
    {
        public static readonly PrefabCollection<ItemPrefab> Prefabs = new PrefabCollection<ItemPrefab>();

        //default size
        public Vector2 Size { get; private set; }

        private PriceInfo defaultPrice;
        public PriceInfo DefaultPrice => defaultPrice;
        private ImmutableDictionary<Identifier, PriceInfo> StorePrices { get; set; }
        public bool CanBeBought => (DefaultPrice != null && DefaultPrice.CanBeBought) ||
            (StorePrices != null && StorePrices.Any(p => p.Value.CanBeBought));
        /// <summary>
        /// Any item with a Price element in the definition can be sold everywhere.
        /// </summary>
        public bool CanBeSold => DefaultPrice != null;

        /// <summary>
        /// Defines areas where the item can be interacted with. If RequireBodyInsideTrigger is set to true, the character
        /// has to be within the trigger to interact. If it's set to false, having the cursor within the trigger is enough.
        /// </summary>
        public ImmutableArray<Rectangle> Triggers { get; private set; }

        private ImmutableDictionary<Identifier, float> treatmentSuitability;
        private readonly List<XElement> fabricationRecipeElements = new List<XElement>();

        /// <summary>
        /// Is this prefab overriding a prefab in another content package
        /// </summary>
        public bool IsOverride => Prefabs.IsOverride(this);

        public XElement originalElement { get; }
        public ContentXElement ConfigElement { get; private set; }

        public ImmutableArray<DeconstructItem> DeconstructItems { get; private set; }

        public ImmutableDictionary<uint, FabricationRecipe> FabricationRecipes { get; private set; }

        public float DeconstructTime { get; private set; }

        public bool AllowDeconstruct { get; private set; }

        //Containers (by identifiers or tags) that this item should be placed in. These are preferences, which are not enforced.
        public ImmutableArray<PreferredContainer> PreferredContainers { get; private set; }

        public SwappableItem SwappableItem
        {
            get;
            private set;
        }

        public readonly struct CommonnessInfo
        {
            public float Commonness
            {
                get
                {
                    return commonness;
                }
            }
            public float AbyssCommonness
            {
                get
                {
                    return abyssCommonness ?? 0.0f;
                }
            }
            public float CaveCommonness
            {
                get
                {
                    return caveCommonness ?? Commonness;
                }
            }
            public bool CanAppear
            {
                get
                {
                    if (Commonness > 0.0f) { return true; }
                    if (AbyssCommonness > 0.0f) { return true; }
                    if (CaveCommonness > 0.0f) { return true; }
                    return false;
                }
            }

            public readonly float commonness;
            public readonly float? abyssCommonness;
            public readonly float? caveCommonness;

            public CommonnessInfo(XElement element)
            {
                this.commonness = Math.Max(element?.GetAttributeFloat("commonness", 0.0f) ?? 0.0f, 0.0f);

                float? abyssCommonness = null;
                XAttribute abyssCommonnessAttribute = element?.GetAttribute("abysscommonness") ?? element?.GetAttribute("abyss");
                if (abyssCommonnessAttribute != null)
                {
                    abyssCommonness = Math.Max(abyssCommonnessAttribute.GetAttributeFloat(0.0f), 0.0f);
                }
                this.abyssCommonness = abyssCommonness;

                float? caveCommonness = null;
                XAttribute caveCommonnessAttribute = element?.GetAttribute("cavecommonness") ?? element?.GetAttribute("cave");
                if (caveCommonnessAttribute != null)
                {
                    caveCommonness =  Math.Max(caveCommonnessAttribute.GetAttributeFloat(0.0f), 0.0f);
                }
                this.caveCommonness = caveCommonness;
            }

            public CommonnessInfo(float commonness, float? abyssCommonness, float? caveCommonness)
            {
                this.commonness = commonness;
                this.abyssCommonness = abyssCommonness != null ? (float?)Math.Max(abyssCommonness.Value, 0.0f) : null;
                this.caveCommonness = caveCommonness != null ? (float?)Math.Max(caveCommonness.Value, 0.0f) : null;
            }

            public CommonnessInfo WithInheritedCommonness(CommonnessInfo? parentInfo)
            {
                return new CommonnessInfo(commonness,
                    abyssCommonness ?? parentInfo?.abyssCommonness,
                    caveCommonness ?? parentInfo?.caveCommonness);
            }

            public CommonnessInfo WithInheritedCommonness(params CommonnessInfo?[] parentInfos)
            {
                CommonnessInfo info = this;
                foreach (var parentInfo in parentInfos)
                {
                    info = info.WithInheritedCommonness(parentInfo);
                }
                return info;
            }

            public float GetCommonness(Level.TunnelType tunnelType)
            {
                if (tunnelType == Level.TunnelType.Cave)
                {
                    return CaveCommonness;
                }
                else
                {
                    return Commonness;
                }
            }
        }

        /// <summary>
        /// How likely it is for the item to spawn in a level of a given type.
        /// </summary>
        private ImmutableDictionary<Identifier, CommonnessInfo> LevelCommonness { get; set; }

        public readonly struct FixedQuantityResourceInfo
        {
            public readonly int ClusterQuantity;
            public readonly int ClusterSize;
            public readonly bool IsIslandSpecific;
            public readonly bool AllowAtStart;

            public FixedQuantityResourceInfo(int clusterQuantity, int clusterSize, bool isIslandSpecific, bool allowAtStart)
            {
                ClusterQuantity = clusterQuantity;
                ClusterSize = clusterSize;
                IsIslandSpecific = isIslandSpecific;
                AllowAtStart = allowAtStart;
            }
        }

        public ImmutableDictionary<Identifier, FixedQuantityResourceInfo> LevelQuantity { get; private set; }

        public bool CanSpriteFlipX { get; private set; }

        public bool CanSpriteFlipY { get; private set; }

        /// <summary>
        /// Can the item be chosen as extra cargo in multiplayer. If not set, the item is available if it can be bought from outposts in the campaign.
        /// </summary>
        public bool? AllowAsExtraCargo { get; private set; }

        public bool RandomDeconstructionOutput { get; private set; }

        public int RandomDeconstructionOutputAmount { get; private set; }

        private Sprite sprite;
        public override Sprite Sprite => sprite;

        public override string OriginalName { get; }

        private LocalizedString name;
        public override LocalizedString Name => name;

        private ImmutableHashSet<Identifier> tags;
        public override ImmutableHashSet<Identifier> Tags => tags;

        private ImmutableHashSet<Identifier> allowedLinks;
        public override ImmutableHashSet<Identifier> AllowedLinks => allowedLinks;

        private MapEntityCategory category;
        public override MapEntityCategory Category => category;

        private ImmutableHashSet<string> aliases;
        public override ImmutableHashSet<string> Aliases => aliases;

        //how close the Character has to be to the item to pick it up
        [Serialize(120.0f, IsPropertySaveable.No)]
        public float InteractDistance { get; private set; }

        // this can be used to allow items which are behind other items tp
        [Serialize(0.0f, IsPropertySaveable.No)]
        public float InteractPriority { get; private set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool InteractThroughWalls { get; private set; }

        [Serialize(false, IsPropertySaveable.No, description: "Hides the condition bar displayed at the bottom of the inventory slot the item is in.")]
        public bool HideConditionBar { get; set; }

        [Serialize(false, IsPropertySaveable.No, description: "Hides the condition displayed in the item's tooltip.")]
        public bool HideConditionInTooltip { get; set; }

        //if true and the item has trigger areas defined, characters need to be within the trigger to interact with the item
        //if false, trigger areas define areas that can be used to highlight the item
        [Serialize(true, IsPropertySaveable.No)]
        public bool RequireBodyInsideTrigger { get; private set; }

        //if true and the item has trigger areas defined, players can only highlight the item when the cursor is on the trigger
        [Serialize(false, IsPropertySaveable.No)]
        public bool RequireCursorInsideTrigger { get; private set; }

        //if true then players can only highlight the item if its targeted for interaction by a campaign event
        [Serialize(false, IsPropertySaveable.No)]
        public bool RequireCampaignInteract
        {
            get;
            private set;
        }

        //should the camera focus on the item when selected
        [Serialize(false, IsPropertySaveable.No)]
        public bool FocusOnSelected { get; private set; }

        //the amount of "camera offset" when selecting the construction
        [Serialize(0.0f, IsPropertySaveable.No)]
        public float OffsetOnSelected { get; private set; }

        [Serialize(100.0f, IsPropertySaveable.No)]
        public float Health { get; private set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool AllowSellingWhenBroken { get; private set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool Indestructible { get; private set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool DamagedByExplosions { get; private set; }

        [Serialize(1f, IsPropertySaveable.No)]
        public float ExplosionDamageMultiplier { get; private set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool DamagedByProjectiles { get; private set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool DamagedByMeleeWeapons { get; private set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool DamagedByRepairTools { get; private set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool DamagedByMonsters { get; private set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool FireProof { get; private set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool WaterProof { get; private set; }

        private float impactTolerance;
        [Serialize(0.0f, IsPropertySaveable.No)]
        public float ImpactTolerance
        {
            get { return impactTolerance; }
            set { impactTolerance = Math.Max(value, 0.0f); }
        }

        [Serialize(0.0f, IsPropertySaveable.No)]
        public float OnDamagedThreshold { get; set; }

        [Serialize(0.0f, IsPropertySaveable.No)]
        public float SonarSize
        {
            get;
            private set;
        }

        [Serialize(false, IsPropertySaveable.No)]
        public bool UseInHealthInterface { get; private set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool DisableItemUsageWhenSelected { get; private set; }

        [Serialize("", IsPropertySaveable.No)]        
        public string CargoContainerIdentifier { get; private set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool UseContainedSpriteColor { get; private set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool UseContainedInventoryIconColor { get; private set; }
        
        [Serialize(0.0f, IsPropertySaveable.No)]
        public float AddedRepairSpeedMultiplier
        {
            get;
            private set;
        }

        [Serialize(0.0f, IsPropertySaveable.No)]
        public float AddedPickingSpeedMultiplier
        {
            get;
            private set;
        }

        [Serialize(false, IsPropertySaveable.No)]
        public bool CannotRepairFail
        {
            get;
            private set;
        }

        [Serialize(null, IsPropertySaveable.No)]
        public string EquipConfirmationText { get; set; }

        [Serialize(true, IsPropertySaveable.No, description: "Can the item be rotated in the submarine editor.")]
        public bool AllowRotatingInEditor { get; set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool ShowContentsInTooltip { get; private set; }

        [Serialize(true, IsPropertySaveable.No)]
        public bool CanFlipX { get; private set; }
        
        [Serialize(true, IsPropertySaveable.No)]
        public bool CanFlipY { get; private set; }
        
        [Serialize(false, IsPropertySaveable.No)]
        public bool IsDangerous { get; private set; }

        private int maxStackSize;
        [Serialize(1, IsPropertySaveable.No)]
        public int MaxStackSize
        {
            get { return maxStackSize; }
            private set { maxStackSize = MathHelper.Clamp(value, 1, Inventory.MaxStackSize); }
        }

        [Serialize(false, IsPropertySaveable.No)]
        public bool AllowDroppingOnSwap { get; private set; }

        public ImmutableHashSet<Identifier> AllowDroppingOnSwapWith { get; private set; }

        [Serialize(false, IsPropertySaveable.No)]
        public bool DontTransferBetweenSubs { get; private set; }

        protected override Identifier DetermineIdentifier(XElement element)
        {
            Identifier identifier = base.DetermineIdentifier(element);
            string originalName = element.GetAttributeString("name", "");
            if (identifier.IsEmpty && !string.IsNullOrEmpty(originalName))
            {
                string categoryStr = element.GetAttributeString("category", "Misc");
                if (Enum.TryParse(categoryStr, true, out MapEntityCategory category) && category.HasFlag(MapEntityCategory.Legacy))
                {
                    identifier = GenerateLegacyIdentifier(originalName);
                }
            }
            return identifier;
        }

        public static Identifier GenerateLegacyIdentifier(string name)
        {
            return ($"legacyitem_{name.Replace(" ", "")}").ToIdentifier();
        }

        public ItemPrefab(ContentXElement element, ItemFile file) : base(element, file)
        {
            originalElement = element;
            ConfigElement = element;

            OriginalName = element.GetAttributeString("name", "");
            name = OriginalName;
            
            if (!element.InheritParent().id.IsEmpty) { return; } //don't even attempt to read the XML until the PrefabCollection readies up the parent to inherit from

            ParseConfigElement(variantOf: null);
        }

        private string GetTexturePath(ContentXElement subElement, ItemPrefab variantOf)
            => subElement.DoesAttributeReferenceFileNameAlone("texture")
                ? Path.GetDirectoryName(ContentFile.Path.IsNullOrEmpty()?variantOf?.ContentFile.Path : ContentFile.Path)
                : "";

        private void ParseConfigElement(ItemPrefab variantOf)
        {
            string categoryStr = ConfigElement.GetAttributeString("category", "Misc");
            this.category = Enum.TryParse(categoryStr, true, out MapEntityCategory category)
                ? category
                : MapEntityCategory.Misc;

            var parentType = ConfigElement.Parent?.GetAttributeIdentifier("itemtype", "");

            //nameidentifier can be used to make multiple items use the same names and descriptions
            Identifier nameIdentifier = ConfigElement.GetAttributeIdentifier("nameidentifier", "");

            //only used if the item doesn't have a name/description defined in the currently selected language
            string fallbackNameIdentifier = ConfigElement.GetAttributeString("fallbacknameidentifier", "");

            name = TextManager.Get(nameIdentifier.IsEmpty
                    ? $"EntityName.{Identifier}"
                    : $"EntityName.{nameIdentifier}",
                $"EntityName.{fallbackNameIdentifier}");
            if (!string.IsNullOrEmpty(OriginalName))
            {
                name = name.Fallback(OriginalName);
            }

            if (parentType == "wrecked")
            {
                name = TextManager.GetWithVariable("wreckeditemformat", "[name]", name);
            }

            name = GeneticMaterial.TryCreateName(this, ConfigElement);

            this.aliases =
                (ConfigElement.GetAttributeStringArray("aliases", null, convertToLowerInvariant: true) ??
                 ConfigElement.GetAttributeStringArray("Aliases", Array.Empty<string>(), convertToLowerInvariant: true))
                    .ToImmutableHashSet()
                    .Add(OriginalName.ToLowerInvariant());

            var triggers = new List<Rectangle>();
            var deconstructItems = new List<DeconstructItem>();
            var fabricationRecipes = new Dictionary<uint, FabricationRecipe>();
            var treatmentSuitability = new Dictionary<Identifier, float>();
            var storePrices = new Dictionary<Identifier, PriceInfo>();
            var preferredContainers = new List<PreferredContainer>();
            DeconstructTime = 1.0f;

            if (ConfigElement.GetAttribute("allowasextracargo") != null)
            {
                AllowAsExtraCargo = ConfigElement.GetAttributeBool("allowasextracargo", false);
            }

            this.tags = ConfigElement.GetAttributeIdentifierArray("tags", Array.Empty<Identifier>()).ToImmutableHashSet();
            if (!Tags.Any())
            {
                this.tags = ConfigElement.GetAttributeIdentifierArray("Tags", Array.Empty<Identifier>()).ToImmutableHashSet();
            }

            if (ConfigElement.GetAttribute("cargocontainername") != null)
            {
                DebugConsole.ThrowError($"Error in item prefab \"{ToString()}\" - cargo container should be configured using the item's identifier, not the name.");
            }

            SerializableProperty.DeserializeProperties(this, ConfigElement);

            LoadDescription(ConfigElement);

            var allowDroppingOnSwapWith = ConfigElement.GetAttributeIdentifierArray("allowdroppingonswapwith", Array.Empty<Identifier>());
            AllowDroppingOnSwapWith = allowDroppingOnSwapWith.ToImmutableHashSet();
            AllowDroppingOnSwap = allowDroppingOnSwapWith.Any();

            var levelCommonness = new Dictionary<Identifier, CommonnessInfo>();
            var levelQuantity = new Dictionary<Identifier, FixedQuantityResourceInfo>();

            foreach (ContentXElement subElement in ConfigElement.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        string spriteFolder = GetTexturePath(subElement, variantOf);

                        CanSpriteFlipX = subElement.GetAttributeBool("canflipx", true);
                        CanSpriteFlipY = subElement.GetAttributeBool("canflipy", true);

                        sprite = new Sprite(subElement, spriteFolder, lazyLoad: true);
                        if (subElement.GetAttribute("sourcerect") == null &&
                            subElement.GetAttribute("sheetindex") == null)
                        {
                            DebugConsole.ThrowError($"Warning - sprite sourcerect not configured for item \"{ToString()}\"!");
                        }
                        Size = Sprite.size;

                        if (subElement.GetAttribute("name") == null && !Name.IsNullOrWhiteSpace())
                        {
                            Sprite.Name = Name.Value;
                        }
                        Sprite.EntityIdentifier = Identifier;
                        break;
                    case "price":
                        if (subElement.GetAttribute("baseprice") != null)
                        {
                            foreach (var priceInfo in PriceInfo.CreatePriceInfos(subElement, out defaultPrice))
                            {
                                if (priceInfo.StoreIdentifier.IsEmpty) { continue; }
                                if (storePrices.ContainsKey(priceInfo.StoreIdentifier))
                                {
                                    DebugConsole.AddWarning($"Error in item prefab \"{this}\": price for the store \"{priceInfo.StoreIdentifier}\" defined more than once.");
                                    storePrices[priceInfo.StoreIdentifier] = priceInfo;
                                }
                                else
                                {
                                    storePrices.Add(priceInfo.StoreIdentifier, priceInfo);
                                }
                            }
                        }
                        else if (subElement.GetAttribute("buyprice") != null && subElement.GetAttributeIdentifier("locationtype", "") is { IsEmpty: false } locationType) // Backwards compatibility
                        {
                            if (storePrices.ContainsKey(locationType))
                            {
                                DebugConsole.AddWarning($"Error in item prefab \"{this}\": price for the location type \"{locationType}\" defined more than once.");
                                storePrices[locationType] = new PriceInfo(subElement);
                            }
                            else
                            {
                                storePrices.Add(locationType, new PriceInfo(subElement));
                            }
                        }
                        break;
                    case "deconstruct":
                        DeconstructTime = subElement.GetAttributeFloat("time", 1.0f);
                        AllowDeconstruct = true;
                        RandomDeconstructionOutput = subElement.GetAttributeBool("chooserandom", false);
                        RandomDeconstructionOutputAmount = subElement.GetAttributeInt("amount", 1);
                        foreach (XElement deconstructItem in subElement.Elements())
                        {
                            if (deconstructItem.Attribute("name") != null)
                            {
                                DebugConsole.ThrowError($"Error in item config \"{ToString()}\" - use item identifiers instead of names to configure the deconstruct items.");
                                continue;
                            }
                            deconstructItems.Add(new DeconstructItem(deconstructItem, Identifier));
                        }
                        RandomDeconstructionOutputAmount = Math.Min(RandomDeconstructionOutputAmount, deconstructItems.Count);
                        break;
                    case "fabricate":
                    case "fabricable":
                    case "fabricableitem":
                        var newRecipe = new FabricationRecipe(subElement, Identifier);
                        if (fabricationRecipes.TryGetValue(newRecipe.RecipeHash, out var prevRecipe))
                        {
                            DebugConsole.ThrowError(
                                $"Error in item prefab \"{ToString()}\": " +
                                $"{prevRecipe.DisplayName} has the same hash as {newRecipe.DisplayName}. " +
                                $"This will cause issues with fabrication."
                            );
                        }
                        else
                        {
                            fabricationRecipes.Add(newRecipe.RecipeHash, newRecipe);
                        }
                        break;
                    case "preferredcontainer":
                        var preferredContainer = new PreferredContainer(subElement);
                        if (preferredContainer.Primary.Count == 0 && preferredContainer.Secondary.Count == 0)
                        {
                            DebugConsole.ThrowError($"Error in item prefab \"{ToString()}\": preferred container has no preferences defined ({subElement}).");
                        }
                        else
                        {
                            preferredContainers.Add(preferredContainer);
                        }
                        break;
                    case "swappableitem":
                        SwappableItem = new SwappableItem(subElement);
                        break;
                    case "trigger":
                        Rectangle trigger = new Rectangle(0, 0, 10, 10)
                        {
                            X = subElement.GetAttributeInt("x", 0),
                            Y = subElement.GetAttributeInt("y", 0),
                            Width = subElement.GetAttributeInt("width", 0),
                            Height = subElement.GetAttributeInt("height", 0)
                        };

                        triggers.Add(trigger);

                        break;
                    case "levelresource":
                        foreach (XElement levelCommonnessElement in subElement.GetChildElements("commonness"))
                        {
                            Identifier levelName = levelCommonnessElement.GetAttributeIdentifier("leveltype", "");
                            if (!levelCommonnessElement.GetAttributeBool("fixedquantity", false))
                            {
                                if (!levelCommonness.ContainsKey(levelName))
                                {
                                    levelCommonness.Add(levelName, new CommonnessInfo(levelCommonnessElement));
                                }
                            }
                            else
                            {
                                if (!levelQuantity.ContainsKey(levelName))
                                {
                                    levelQuantity.Add(levelName, new FixedQuantityResourceInfo(
                                        levelCommonnessElement.GetAttributeInt("clusterquantity", 0),
                                        levelCommonnessElement.GetAttributeInt("clustersize", 0),
                                        levelCommonnessElement.GetAttributeBool("isislandspecific", false),
                                        levelCommonnessElement.GetAttributeBool("allowatstart", true)));
                                }
                            }
                        }
                        break;
                    case "suitabletreatment":
                        if (subElement.GetAttribute("name") != null)
                        {
                            DebugConsole.ThrowError($"Error in item prefab \"{ToString()}\" - suitable treatments should be defined using item identifiers, not item names.");
                        }
                        Identifier treatmentIdentifier = subElement.GetAttributeIdentifier("identifier", subElement.GetAttributeIdentifier("type", Identifier.Empty));
                        float suitability = subElement.GetAttributeFloat("suitability", 0.0f);
                        treatmentSuitability.Add(treatmentIdentifier, suitability);
                        break;
                }
            }

#if CLIENT
            ParseSubElementsClient(ConfigElement, variantOf);
#endif

            this.Triggers = triggers.ToImmutableArray();
            this.DeconstructItems = deconstructItems.ToImmutableArray();
            this.FabricationRecipes = fabricationRecipes.ToImmutableDictionary();
            this.treatmentSuitability = treatmentSuitability.ToImmutableDictionary();
            StorePrices = storePrices.ToImmutableDictionary();
            this.PreferredContainers = preferredContainers.ToImmutableArray();
            this.LevelCommonness = levelCommonness.ToImmutableDictionary();
            this.LevelQuantity = levelQuantity.ToImmutableDictionary();

            // Backwards compatibility
            if (storePrices.Any())
            {
                defaultPrice ??= new PriceInfo(GetMinPrice() ?? 0, false);
            }

            HideConditionInTooltip = ConfigElement.GetAttributeBool("hideconditionintooltip", HideConditionBar);

            //backwards compatibility
            if (categoryStr.Equals("Thalamus", StringComparison.OrdinalIgnoreCase))
            {
                this.category = MapEntityCategory.Wrecked;
                Subcategory = "Thalamus";
            }

            if (Sprite == null)
            {
                DebugConsole.ThrowError($"Item \"{ToString()}\" has no sprite!");
#if SERVER
                this.sprite = new Sprite("", Vector2.Zero);
                this.sprite.SourceRect = new Rectangle(0, 0, 32, 32);
#else
                this.sprite = new Sprite(TextureLoader.PlaceHolderTexture, null, null)
                {
                    Origin = TextureLoader.PlaceHolderTexture.Bounds.Size.ToVector2() / 2
                };
#endif
                Size = Sprite.size;
                Sprite.EntityIdentifier = Identifier;
            }

            if (Identifier == Identifier.Empty)
            {
                DebugConsole.ThrowError(
                    $"Item prefab \"{ToString()}\" has no identifier. All item prefabs have a unique identifier string that's used to differentiate between items during saving and loading.");
            }

#if DEBUG
            if (!Category.HasFlag(MapEntityCategory.Legacy) && !HideInMenus)
            {
                if (!string.IsNullOrEmpty(OriginalName))
                {
                    DebugConsole.AddWarning($"Item \"{(Identifier == Identifier.Empty ? Name : Identifier.Value)}\" has a hard-coded name, and won't be localized to other languages.");
                }
            }
#endif

            this.allowedLinks = ConfigElement.GetAttributeIdentifierArray("allowedlinks", Array.Empty<Identifier>()).ToImmutableHashSet();
        }

        public CommonnessInfo? GetCommonnessInfo(Level level)
        {
            CommonnessInfo? levelCommonnessInfo = GetValueOrNull(level.GenerationParams.Identifier);
            CommonnessInfo? biomeCommonnessInfo = GetValueOrNull(level.LevelData.Biome.Identifier);
            CommonnessInfo? defaultCommonnessInfo = GetValueOrNull(Identifier.Empty);

            if (levelCommonnessInfo.HasValue)
            {
                return levelCommonnessInfo?.WithInheritedCommonness(biomeCommonnessInfo, defaultCommonnessInfo);
            }
            else if (biomeCommonnessInfo.HasValue)
            {
                return biomeCommonnessInfo?.WithInheritedCommonness(defaultCommonnessInfo);
            }
            else if (defaultCommonnessInfo.HasValue)
            {
                return defaultCommonnessInfo;
            }

            return null;

            CommonnessInfo? GetValueOrNull(Identifier identifier)
            {
                if (LevelCommonness.TryGetValue(identifier, out CommonnessInfo info))
                {
                    return info;
                }
                else
                {
                    return null;
                }
            }
        }

        public float GetTreatmentSuitability(Identifier treatmentIdentifier)
        {
            return treatmentSuitability.TryGetValue(treatmentIdentifier, out float suitability) ? suitability : 0.0f;
        }

        #region Pricing

        public PriceInfo GetPriceInfo(Location.StoreInfo store)
        {
            if (store == null)
            {
                string message = $"Tried to get price info for \"{Identifier}\" with a null store parameter!\n{Environment.StackTrace.CleanupStackTrace()}";
#if DEBUG
                DebugConsole.LogError(message);
#else
                DebugConsole.AddWarning(message);
                GameAnalyticsManager.AddErrorEventOnce("ItemPrefab.GetPriceInfo:StoreParameterNull", GameAnalyticsManager.ErrorSeverity.Error, message);
#endif
                return null;
            }
            else if (!store.Identifier.IsEmpty && StorePrices != null && StorePrices.TryGetValue(store.Identifier, out var storePriceInfo))
            {
                return storePriceInfo;
            }
            else
            {
                return DefaultPrice;
            }
        }
        
        public bool CanBeBoughtFrom(Location.StoreInfo store, out PriceInfo priceInfo)
        {
            priceInfo = GetPriceInfo(store);
            return priceInfo != null && priceInfo.CanBeBought && (store.Location?.LevelData?.Difficulty ?? 0) >= priceInfo.MinLevelDifficulty;
        }

        public bool CanBeBoughtFrom(Location location)
        {
            if (location?.Stores == null) { return false; }
            foreach (var store in location.Stores)
            {
                var priceInfo = GetPriceInfo(store.Value);
                if (priceInfo == null) { continue; }
                if (!priceInfo.CanBeBought) { continue; }
                if ((location.LevelData?.Difficulty ?? 0) < priceInfo.MinLevelDifficulty) { continue; }
                return true;
            }
            return false;
        }

        public int? GetMinPrice()
        {
            int? minPrice = null;
            if (StorePrices != null && StorePrices.Any())
            {
                minPrice = StorePrices.Values.Min(p => p.Price);
            }
            if (minPrice.HasValue)
            {
                if (DefaultPrice != null)
                {
                    return minPrice < DefaultPrice.Price ? minPrice : DefaultPrice.Price;
                }
                else
                {
                    return minPrice.Value;
                }
            }
            else
            {
                return DefaultPrice?.Price;
            }
        }

        public ImmutableDictionary<Identifier, PriceInfo> GetBuyPricesUnder(int maxCost = 0)
        {
            var prices = new Dictionary<Identifier, PriceInfo>();
            if (StorePrices != null)
            {
                foreach (var storePrice in StorePrices)
                {
                    var priceInfo = storePrice.Value;
                    if (priceInfo == null)
                    {
                        continue;
                    }
                    if (!priceInfo.CanBeBought)
                    {
                        continue;
                    }
                    if (priceInfo.Price < maxCost || maxCost == 0)
                    {
                        prices.Add(storePrice.Key, priceInfo);
                    }
                }
            }
            return prices.ToImmutableDictionary();
        }

        public ImmutableDictionary<Identifier, PriceInfo> GetSellPricesOver(int minCost = 0, bool sellingImportant = true)
        {
            var prices = new Dictionary<Identifier, PriceInfo>();
            if (!CanBeSold && sellingImportant)
            {
                return prices.ToImmutableDictionary();
            }
            foreach (var storePrice in StorePrices)
            {
                var priceInfo = storePrice.Value;
                if (priceInfo == null)
                {
                    continue;
                }
                if (priceInfo.Price > minCost)
                {
                    prices.Add(storePrice.Key, priceInfo);
                }
            }
            return prices.ToImmutableDictionary();
        }

        #endregion

        public static ItemPrefab Find(string name, Identifier identifier)
        {
            if (string.IsNullOrEmpty(name) && identifier.IsEmpty)
            {
                throw new ArgumentException("Both name and identifier cannot be null.");
            }

            ItemPrefab prefab;
            if (identifier.IsEmpty)
            {
                //legacy support
                identifier = GenerateLegacyIdentifier(name);
            }
            Prefabs.TryGet(identifier, out prefab);

            //not found, see if we can find a prefab with a matching alias
            if (prefab == null && !string.IsNullOrEmpty(name))
            {
                string lowerCaseName = name.ToLowerInvariant();
                prefab = Prefabs.Find(me => me.Aliases != null && me.Aliases.Contains(lowerCaseName));
            }
            if (prefab == null)
            {
                prefab = Prefabs.Find(me => me.Aliases != null && me.Aliases.Contains(identifier.Value));
            }

            if (prefab == null)
            {
                DebugConsole.ThrowError($"Error loading item - item prefab \"{name}\" (identifier \"{identifier}\") not found.");
            }
            return prefab;
        }

        public bool IsContainerPreferred(Item item, ItemContainer targetContainer, out bool isPreferencesDefined, out bool isSecondary, bool requireConditionRequirement = false, bool checkTransferConditions = false)
        {
            isPreferencesDefined = PreferredContainers.Any();
            isSecondary = false;
            if (!isPreferencesDefined) { return true; }
            if (PreferredContainers.Any(pc => (!requireConditionRequirement || HasConditionRequirement(pc)) && IsItemConditionAcceptable(item, pc) && 
                IsContainerPreferred(pc.Primary, targetContainer) && (!checkTransferConditions || CanBeTransferred(item.Prefab.Identifier, pc, targetContainer))))
            {
                return true;
            }
            isSecondary = true;
            return PreferredContainers.Any(pc => (!requireConditionRequirement || HasConditionRequirement(pc)) && IsItemConditionAcceptable(item, pc) && IsContainerPreferred(pc.Secondary, targetContainer));
            static bool HasConditionRequirement(PreferredContainer pc) => pc.MinCondition > 0 || pc.MaxCondition < 100;
        }

        public bool IsContainerPreferred(Item item, Identifier[] identifiersOrTags, out bool isPreferencesDefined, out bool isSecondary)
        {
            isPreferencesDefined = PreferredContainers.Any();
            isSecondary = false;
            if (!isPreferencesDefined) { return true; }
            if (PreferredContainers.Any(pc => IsItemConditionAcceptable(item, pc) && IsContainerPreferred(pc.Primary, identifiersOrTags)))
            {
                return true;
            }
            isSecondary = true;
            return PreferredContainers.Any(pc => IsItemConditionAcceptable(item, pc) && IsContainerPreferred(pc.Secondary, identifiersOrTags));
        }

        private bool IsItemConditionAcceptable(Item item, PreferredContainer pc) => item.ConditionPercentage >= pc.MinCondition && item.ConditionPercentage <= pc.MaxCondition;
        private bool CanBeTransferred(Identifier item, PreferredContainer pc, ItemContainer targetContainer) => 
            pc.AllowTransfersHere && (!pc.TransferOnlyOnePerContainer || targetContainer.Inventory.AllItems.None(i => i.Prefab.Identifier == item));

        public static bool IsContainerPreferred(IEnumerable<Identifier> preferences, ItemContainer c) => preferences.Any(id => c.Item.Prefab.Identifier == id || c.Item.HasTag(id));
        public static bool IsContainerPreferred(IEnumerable<Identifier> preferences, IEnumerable<Identifier> ids) => ids.Any(id => preferences.Contains(id));

        protected override void CreateInstance(Rectangle rect)
        {
            throw new InvalidOperationException("Can't call ItemPrefab.CreateInstance");
        }

        public override void Dispose()
        {
            Item.RemoveByPrefab(this);
        }

        public void InheritFrom(ItemPrefab parent)
        {
            ConfigElement = (this as IImplementsVariants<ItemPrefab>).DoInherit(null);
            ParseConfigElement(parent);
        }

        public ItemPrefab FindByPrefabInstance(PrefabInstance instance){
            Prefabs.TryGet(instance, out ItemPrefab res);
            return res;
        }
        public ItemPrefab GetPrevious(Identifier identifier)
        {
            ItemPrefab res;
			if (identifier != Identifier)
			{
				if (Prefabs.Any(p => p.Identifier == identifier))
				{
					res = Prefabs[identifier];
				}
				else
				{
					res = null;
				}
			}
			else
			{
				if (Prefabs.AllPrefabs.Any(p => p.Key == identifier))
				{
					string best_effort_package_id = ContentPackage.GetBestEffortId();
					res = Prefabs.AllPrefabs.Where(p => p.Key == identifier)
						.Single().Value
						.GetPrevious(best_effort_package_id);
				}
				else
				{
					res = null;
				}
			}
			return res;
        }


        public override string ToString()
        {
            return $"{Name} (identifier: {Identifier})";
        }
    }
}
