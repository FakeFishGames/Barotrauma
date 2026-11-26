#nullable enable
using Barotrauma.Extensions;
using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Color = Microsoft.Xna.Framework.Color;
using Rectangle = Microsoft.Xna.Framework.Rectangle;
using Vector2 = Microsoft.Xna.Framework.Vector2;
using Vector4 = Microsoft.Xna.Framework.Vector4;

namespace Barotrauma.Items.Components
{
    public class ProducedItem
    {
        [Serialize(0f, IsPropertySaveable.Yes, "Deprecated. Use ProbabilityAtMinCondition and ProbabilityAtMaxCondition instead.")]
        public float Probability { get; set; }

        [Serialize(0f, IsPropertySaveable.Yes, "Probability of spawning this item when the growable's Health is at 0.")]
        public float ProbabilityAtMinCondition { get; set; }

        [Serialize(1f, IsPropertySaveable.Yes, "Probability of spawning this item when the growable's Health it is at MaxHealth.")]
        public float ProbabilityAtMaxCondition { get; set; }

        [Serialize(0f, IsPropertySaveable.Yes, "Absolute amount of fertilizer consumed from the planter when this item is produced.")]
        public float FertilizerConsumedPerFruiting { get; set; }

        public readonly List<StatusEffect> StatusEffects = new List<StatusEffect>();

        public readonly Item Producer;
        
        public readonly ItemPrefab? Prefab;

        // Note: This is currently used only for spawning the growable's seeds
        public ProducedItem(Item producer, ItemPrefab prefab)
        {
            Producer = producer;
            Prefab = prefab;
        }

        public ProducedItem(Item producer, ContentXElement element)
        {
            SerializableProperty.DeserializeProperties(this, element);

            Producer = producer;

            Identifier itemIdentifier = element.GetAttributeIdentifier("identifier", Identifier.Empty);
            if (!itemIdentifier.IsEmpty)
            {
                Prefab = ItemPrefab.Find(null, itemIdentifier);
            }

            // Backwards compatibility: if the old Probability is set but the new properties aren't, use Probability for both
            if (Probability > 0f && ProbabilityAtMinCondition == 0f && ProbabilityAtMaxCondition == 1f)
            {
                ProbabilityAtMinCondition = Probability;
                ProbabilityAtMaxCondition = Probability;
            }

            LoadSubElements(element);
        }

        private void LoadSubElements(ContentXElement element)
        {
            if (!element.HasElements) { return; }

            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "statuseffect":
                    {
                        StatusEffect effect = StatusEffect.Load(subElement, Prefab?.Name.Value);
                        if (effect.Type != ActionType.OnProduceSpawned)
                        {
                            DebugConsole.ThrowError("Only OnProduceSpawned type can be used in <ProducedItem>.", 
                                contentPackage: element.ContentPackage);
                            continue;
                        }

                        StatusEffects.Add(effect);
                        break;
                    }
                }
            }
        }
    }

    // ReSharper disable UnusedMember.Global
    public enum VineTileType
    {
        Stem = 0b0000,
        CrossJunction = 0b1111,
        HorizontalLine = 0b1010,
        VerticalLine = 0b0101,
        /*backwards compatibility, the vertical and horizontal "lane" used to be backwards*/
        VerticalLane = 0b1010,
        HorizontalLane = 0b0101,
        TurnTopRight = 0b1001,
        TurnTopLeft = 0b0011,
        TurnBottomLeft = 0b0110,
        TurnBottomRight = 0b1100,
        TSectionTop = 0b1011,
        TSectionLeft = 0b0111,
        TSectionBottom = 0b1110,
        TSectionRight = 0b1101,
        StumpTop = 0b0001,
        StumpLeft = 0b0010,
        StumpBottom = 0b0100,
        StumpRight = 0b1000
    }

    [Flags]
    public enum TileSide
    {
        None = 0,
        Top = 1 << 0,
        Left = 1 << 1,
        Bottom = 1 << 2,
        Right = 1 << 3
    }

    public readonly record struct FoliageConfig(int Variant, float Rotation, float Scale)
    {
        public readonly float RotationRad = MathHelper.ToRadians(Rotation);
        public static readonly FoliageConfig EmptyConfig = new(-1, 0f, 1f);

        public void WriteFoliageConfig(float rotationMin, float rotationMax, IWriteMessage msg)
        {
            msg.WriteRangedInteger(Variant, -1, 15);
            msg.WriteRangedSingle(MathHelper.Clamp(Rotation, rotationMin, rotationMax), rotationMin, rotationMax, bitCount: 8);
            msg.WriteRangedSingle(Scale, 0f, 2f, bitCount: 8);
        }

        public static FoliageConfig ReadFoliageConfig(float rotationMin, float rotationMax, IReadMessage msg)
        {
            int variant = msg.ReadRangedInteger(-1, 15);
            float rotation = msg.ReadRangedSingle(rotationMin, rotationMax, bitCount: 8);
            // 2f seems to be a bit arbitrary but the previous upper limit for scale was 1.5f so I think it's fine
            float scale = msg.ReadRangedSingle(0f, 2f, bitCount: 8);
            return new FoliageConfig(variant, rotation, scale);
        }

        public static FoliageConfig FromXML(XElement element)
        {
            int variant = element.GetAttributeInt("variant", -1);
            float rotation = element.GetAttributeFloat("rotation", 0f);
            float scale = element.GetAttributeFloat("scale", 1f);
            return new FoliageConfig(variant, rotation, scale);
        }

        public XElement ToXML(string name)
            => new XElement(name,
                            new XAttribute("variant", Variant),
                            new XAttribute("rotation", Rotation),
                            new XAttribute("scale", Scale));

        public static FoliageConfig CreateRandomConfig(int maxVariants, float minScale, float maxScale, Random? random = null, float minRotation = 0, float maxRotation = 360)
        {
            int flowerVariant = Growable.RandomInt(0, maxVariants, random);
            float flowerScale = (float)Growable.RandomDouble(minScale, maxScale, random);
            float flowerRotation = (float)Growable.RandomDouble(minRotation, maxRotation, random);
            return new FoliageConfig(Variant: flowerVariant, Rotation: flowerRotation, Scale: flowerScale);
        }
    }

    public struct LegacyFoliageConfig
    {
        public static LegacyFoliageConfig EmptyConfig = new LegacyFoliageConfig { Variant = -1, Rotation = 0f, Scale = 1.0f };
        public static readonly int EmptyConfigValue = EmptyConfig.Serialize();

        public int Variant;
        public float Rotation;
        public float Scale;

        public FoliageConfig ToNewFormat() => new FoliageConfig(Variant, MathHelper.ToDegrees(Rotation), Scale);

        public readonly int Serialize()
        {
            int variant = Math.Min(Variant + 1, 15);
            int scale = (int) (Scale * 10f);
            int rotation = (int) (Rotation / MathHelper.TwoPi * 10f);

            return variant | (scale << 4) | (rotation << 8);
        }

        public static LegacyFoliageConfig Deserialize(int value)
        {
            int variant = value & 0x00F;
            int scale = (value & 0x0F0) >> 4;
            int rotation = (value & 0xF00) >> 8;

            return new LegacyFoliageConfig { Variant = variant - 1, Scale = scale / 10f, Rotation = rotation / 10f * MathHelper.TwoPi };
        }
    }

    public partial class VineTile
    {
        public TileSide Sides = TileSide.None;
        public TileSide BlockedSides = TileSide.None;

        public FoliageConfig FlowerConfig;
        public FoliageConfig LeafConfig;

        public int FailedGrowthAttempts;
        public Rectangle Rect;
        public Vector2 Position;

        private readonly float diameter;
        public Vector2 offset;

        public VineTileType Type;
        public readonly Dictionary<TileSide, Vector2> AdjacentPositions;
        public static int Size = 32; // Default "grid" size, can be overridden by Growable
        
        
        public float VineStep;
        public float FlowerStep;

        /// <summary>
        /// <see cref="GrowthStep"/> at which the vine itself is fully grown, and the flowers and leaves start growing.
        /// </summary>
        public const float VineGrownFloweringStarted = 1.0f;

        /// <summary>
        /// <see cref="GrowthStep"/> at which the vine, its flowers and leaves are all fully grown.
        /// </summary>
        public const float FullyGrownWithFlowers = 2.0f;

        private float growthStep;
        public float GrowthStep
        {
            get => growthStep;
            set
            {
                growthStep = value;
                VineStep = Math.Min((float)Math.Pow(value, 2), 1.0f);
                if (value > VineGrownFloweringStarted)
                {
                    FlowerStep = Math.Min((float)Math.Pow(value - VineGrownFloweringStarted, 2), 1.0f);
                }
            }
        }

        public Color HealthColor = Color.Transparent;
        public float DecayDelay;

        private readonly Growable? Parent;

        public VineTile(Growable? parent, Vector2 position, VineTileType type, FoliageConfig? flowerConfig = null, FoliageConfig? leafConfig = null, Rectangle? rect = null)
        {
            FlowerConfig = flowerConfig ?? FoliageConfig.EmptyConfig;
            LeafConfig = leafConfig ?? FoliageConfig.EmptyConfig;
            Position = position;
            Rect = rect ?? CreatePlantRect(position, parent);
            Parent = parent;
            Type = type;
            diameter = Rect.Width / 2.0f;

            AdjacentPositions = new Dictionary<TileSide, Vector2>
            {
                { TileSide.Top, new Vector2(Position.X, Position.Y + Rect.Height) },
                { TileSide.Bottom, new Vector2(Position.X, Position.Y - Rect.Height) },
                { TileSide.Left, new Vector2(Position.X - Rect.Width, Position.Y) },
                { TileSide.Right, new Vector2(Position.X + Rect.Width, Position.Y) }
            };
        }

        public void UpdateScale(float deltaTime, float growthInterval)
        {
            bool decayed = Parent?.Decayed ?? false;

            if (decayed && GrowthStep > VineGrownFloweringStarted)
            {
                if (DecayDelay > 0)
                {
                    DecayDelay -= deltaTime;
                }
                else
                {
                    GrowthStep -= 0.25f * deltaTime;
                }
            }

            if (GrowthStep >= FullyGrownWithFlowers || decayed) { return; }

            GrowthStep += deltaTime / Math.Max(growthInterval, 1.0f);

            if (GrowthStep < VineGrownFloweringStarted)
            {
                // I don't know how or why this works
                float offsetAmount = diameter * VineStep - diameter;
                switch (Type)
                {
                    case VineTileType.StumpLeft:
                        offset.X = offsetAmount;
                        break;
                    case VineTileType.StumpRight:
                        offset.X = -offsetAmount;
                        break;
                    case VineTileType.StumpTop:
                        offset.Y = offsetAmount;
                        break;
                    case VineTileType.Stem:
                    case VineTileType.StumpBottom:
                        offset.Y = -offsetAmount;
                        break;
                    default:
                        offset = Vector2.Zero;
                        break;
                }
            }
            else
            {
                offset = Vector2.Zero;
            }
        }

        public Vector2 GetWorldPosition(Planter planter, Vector2 slotOffset)
        {
            return planter.Item.WorldPosition + slotOffset + Position;
        }

        public void UpdateType()
        {
            if (Type == VineTileType.Stem) { return; }

            Type = (VineTileType)Sides;
        }

        /// <summary>
        /// Returns a random side that is not occupied.
        /// </summary>
        /// <remarks>
        /// There is probably a much better way of doing this than allocating memory with an array
        /// but this felt like the most reliable approach I could come up with.
        /// </remarks>
        /// <returns></returns>
        public TileSide GetRandomFreeSide(Random? random = null)
        {
            const int maxSides = 4;
            TileSide occupiedSides = Sides | BlockedSides;
            int setBits = occupiedSides.Count();
            if (setBits >= maxSides) { return TileSide.None; }

            int possible = maxSides - setBits;
            int[] pool = new int[possible];

            for (int i = 0, j = 0; i < maxSides; i++)
            {
                if (!occupiedSides.HasFlag((TileSide) (1 << i)))
                {
                    pool[j] = i;
                    j++;
                }
            }

            int value;
            if (Parent == null)
            {
                value = pool[Growable.RandomInt(0, possible, random)];
            }
            else
            {
                var (x, y, z, w) = Parent.GrowthWeights;
                float[] weights = { x, y, z, w };

                value = pool.GetRandomByWeight(i => weights[i], Rand.RandSync.Unsynced);
            }
            
            return (TileSide) (1 << value);
        }

        public bool CanGrowMore() => (Sides | BlockedSides).Count() < 4;

        public bool IsSideBlocked(TileSide side) => BlockedSides.HasFlag(side) || Sides.HasFlag(side);

        public static Rectangle CreatePlantRect(Vector2 pos) => CreatePlantRect(pos, null);
        
        public static Rectangle CreatePlantRect(Vector2 pos, Growable? growable)
        {
            int tileSize = growable?.VineTileSize ?? Size;
            return new Rectangle((int)pos.X - tileSize / 2, (int)pos.Y + tileSize / 2, tileSize, tileSize);
        }
    }

    internal static class GrowthSideExtension
    {
        // K&R algorithm for counting how many bits are set in a bit field
        public static int Count(this TileSide side)
        {
            int n = (int)side;
            int count = 0;
            while (n != 0)
            {
                count += n & 1;
                n >>= 1;
            }

            return count;
        }

        public static TileSide GetOppositeSide(this TileSide side)
            => side switch
            {
                TileSide.Left => TileSide.Right,
                TileSide.Right => TileSide.Left,
                TileSide.Bottom => TileSide.Top,
                TileSide.Top => TileSide.Bottom,
                _ => throw new ArgumentException($"Expected Left, Right, Bottom or Top, got {side}")
            };
    }

    public partial class Growable : ItemComponent, IServerSerializable
    {
        // used for debugging where a vine failed to grow
        public readonly HashSet<Rectangle> FailedRectangles = new HashSet<Rectangle>();
        
        /// <summary>
        /// Name of the plant that grows from this item, used in UIs and NPC dialog.
        /// </summary>
        public LocalizedString GrownPlantName { get; private set; }

        [Serialize(10f, IsPropertySaveable.Yes, "Interval in seconds for how often the plant checks if it should grow.")]
        public float GrowthInterval { get; set; }

        public float GrowthIntervalModified
        {
            get
            {
                return Planter.DebugGrowthMultiplier is float debugMultiplier ? GrowthInterval / debugMultiplier : GrowthInterval;
            }
        }
        
        [Serialize(10f, IsPropertySaveable.Yes, "Interval in seconds for how often the plant checks if it should produce something.")]
        public float ProductInterval { get; set; }

        public float ProductIntervalModified
        {
            get
            {
                return Planter.DebugGrowthMultiplier is float debugMultiplier ? ProductInterval / debugMultiplier : ProductInterval;
            }
        }

        private float health;

        [Serialize(100.0f, IsPropertySaveable.Yes, "Current health value.")]
        public float Health
        {
            get => health;
            set => health = Math.Clamp(value, 0, MaxHealth);
        }

        /// <summary>
        /// Health value for when the plant is considered to be as healthy as it can be, calculated based on <see cref="HealthPerVine"/> and the current number of vines.
        /// </summary>
        public float MaxHealth { get; set; }
        
        [Serialize(12f, IsPropertySaveable.Yes, "How much each vine adds to the plant's maximum health.")]
        public float HealthPerVine { get; set; }
        
        [Serialize(0.25f, IsPropertySaveable.Yes, "How much health the plant recovers per second when no issues are present.")]
        public float OptimalHealthRecovery { get; set; }
        
        [Serialize(-0.35f, IsPropertySaveable.Yes, "What kind of effect salinity has on the plant. Negative number means damage.")]
        public float SalinityEffect { get; set; }
        
        [Serialize(0.005f, IsPropertySaveable.Yes, "How much salt is absorbed (consumed, evaporated or otherwise vanished) per second.")]
        public float SaltAbsorption { get; set; }
        
        [Serialize(0.2f, IsPropertySaveable.Yes, "Relative water level (0-1) below which plants take dryness damage.")]
        public float TooDryThreshold { get; set; }
    
        [Serialize(0.05f, IsPropertySaveable.Yes, "Damage per second plants take when too dry.")]
        public float DrynessDamage { get; set; }
    
        [Serialize(1.0f, IsPropertySaveable.Yes, "Relative water level (0-1) above which plants take overwater damage.")]
        public float OverwaterThreshold { get; set; }
    
        [Serialize(0.05f, IsPropertySaveable.Yes, "Damage per second plants take when overwatered.")]
        public float OverwaterDamage { get; set; }

        [Serialize(0.0015f, IsPropertySaveable.Yes, "How much water each vine of the plant consumes per second.")]
        public float WaterUsedPerVine { get; set; }

        [Serialize(0.1f, IsPropertySaveable.Yes, "Relative fertilizer level (0-1) below which plants take damage for being underfertilized.")]
        public float UnderfertilizedThreshold { get; set; }
    
        [Serialize(0.05f, IsPropertySaveable.Yes, "Damage per second plants take when underfertilized.")]
        public float UnderfertilizedDamage { get; set; }
    
        [Serialize(1.0f, IsPropertySaveable.Yes, "Relative fertilizer level (0-1) above which plants take damage for being overfertilized.")]
        public float OverfertilizedThreshold { get; set; }
    
        [Serialize(0.05f, IsPropertySaveable.Yes, "Damage per second plants take when overfertilized.")]
        public float OverfertilizedDamage { get; set; }
        
        [Serialize(0.0008f, IsPropertySaveable.Yes, "How much fertilizer each vine of the plant consumes per second.")]
        public float FertilizerUsedPerVine { get; set; }
        
        [Serialize(15f, IsPropertySaveable.Yes, "How much oxygen each vine of the plant provides to the hull per second.")]
        public float OxygenPerVine { get; set; }
        
        [Serialize(1, IsPropertySaveable.Yes, "Minimum planter slot size required by this plant. Works in integer increments, 1 equating to the slot of the small planter.")]
        public int MinimumPlanterSize { get; set; }
        
        [Serialize(0.01f, IsPropertySaveable.Yes, "Percentage chance of a seed item being produced per growth tick (determined by planter). 0.01 means 1% chance. Not used in vanilla plants.")]
        public float SeedSpawnChance { get; set; }

        [Serialize(0.01f, IsPropertySaveable.Yes, "How often a product item is produced per growth tick (determined by planter). 0.01 means 1% chance.")]
        public float ProductSpawnChance { get; set; }

        [Serialize(0.5f, IsPropertySaveable.Yes, "Completely unused property that was added on the first design pass but due to the first pass being too complex was never used and now it is used by mods so it cannot be removed.")]
        public float MutationProbability { get; set; }

        [Serialize("1.0,1.0,1.0,1.0", IsPropertySaveable.Yes, "Color of the flowers.")]
        public Color FlowerTint { get; set; }

        [Serialize(3, IsPropertySaveable.Yes, "Number of flowers drawn.")]
        public int FlowerQuantity { get; set; }

        [Serialize(0.25f, IsPropertySaveable.Yes, "Size of the flower sprites.")]
        public float BaseFlowerScale { get; set; }

        [Serialize(0.5f, IsPropertySaveable.Yes, "Size of the leaf sprites.")]
        public float BaseLeafScale { get; set; }
        
        [Serialize(0.5f, IsPropertySaveable.Yes, "Minimum size of the flower sprites.")]
        public float MinFlowerScale { get; set; }
        [Serialize(1.0f, IsPropertySaveable.Yes, "Maximum size of the flower sprites.")]
        public float MaxFlowerScale { get; set; }
        [Serialize(0.5f, IsPropertySaveable.Yes, "Minimum size of the leaf sprites.")]
        public float MinLeafScale { get; set; }
        [Serialize(1.0f, IsPropertySaveable.Yes, "Maximum size of the leaf sprites.")]
        public float MaxLeafScale { get; set; }
        
        [Serialize(0f, IsPropertySaveable.Yes, "Minimum rotation of the flower sprites in degrees.")]
        public float MinFlowerRotation { get; set; }
        [Serialize(360f, IsPropertySaveable.Yes, "Maximum rotation of the flower sprites in degrees.")]
        public float MaxFlowerRotation { get; set; }
        [Serialize(0f, IsPropertySaveable.Yes, "Minimum rotation of the leaf sprites in degrees.")]
        public float MinLeafRotation { get; set; }
        [Serialize(360f, IsPropertySaveable.Yes, "Maximum rotation of the leaf sprites in degrees.")]
        public float MaxLeafRotation { get; set; }

        [Serialize("1.0,1.0,1.0,1.0", IsPropertySaveable.Yes, "Color of the leaves.")]
        public Color LeafTint { get; set; }

        [Serialize(0.33f, IsPropertySaveable.Yes, "Chance of a leaf appearing in a branch.")]
        public float LeafProbability { get; set; }

        [Serialize("1.0,1.0,1.0,1.0", IsPropertySaveable.Yes, "Color of the vines.")]
        public Color VineTint { get; set; }

        [Serialize(32, IsPropertySaveable.Yes, "Maximum number of vine tiles the plant can grow.")]
        public int MaximumVines { get; set; }

        [Serialize(0.25f, IsPropertySaveable.Yes, "Size of the vine sprites.")]
        public float VineScale { get; set; }

        [Serialize(32, IsPropertySaveable.Yes, "Size (in pixels) of the vine tiles, which form a grid controlling spacing between vine segments.")]
        public int VineTileSize { get; set; }

        [Serialize("0.26,0.27,0.29,1.0", IsPropertySaveable.Yes, "Tint of a dead plant.")]
        public Color DeadTint { get; set; }

        [Serialize("1,1,1,1", IsPropertySaveable.Yes, "Probability for the plant to grow in a direction.")]
        public Vector4 GrowthWeights { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes, "How much water is lost due to fires every 10 seconds.")]
        public float FireVulnerability { get; set; }

        [Serialize(0f, IsPropertySaveable.Yes, "PlantLightAmount below which plants take \"not enough light damage\".")]
        public float NotEnoughLightThreshold { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes, "Damage per second plants take when not receiving enough light.")]
        public float NotEnoughLightDamage { get; set; }

        [Serialize(100.0f, IsPropertySaveable.Yes, "PlantLightAmount above which plants take \"too much light damage\".")]
        public float TooMuchLightThreshold { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes, "Damage per second plants take when receiving too much light.")]
        public float TooMuchLightDamage { get; set; }

        [Serialize("0.0, 1.0", IsPropertySaveable.Yes, "Modifier to the percentage of product and seed items produced before the plant is fully grown based on how many vines have been grown. 0 would mean no products or seeds are produced while 0.5 would mean half of the normal amount.")]
        public Vector2 LinearProductAndSeedMultiplierBeforeFullyGrown { get; set; }
        
        [Serialize(0.3f, IsPropertySaveable.Yes, "Minimum percentage of vines required to spawn any products/seeds. At 0.3 it means 30% of the maximum vines are required before any chance of fruits to spawn.")]
        public float RequiredVinePercentageForProducts { get; set; }

        [Serialize("1,1,0.88,1", IsPropertySaveable.Yes, "Tint of a plant that is too dry.")]
        public Color TooDryColor { get; set; }
        
        [Serialize("1,0.65,0,1", IsPropertySaveable.Yes, "Tint of a plant that has been watered too much.")]
        public Color OverwateredColor { get; set; }
        
        [Serialize("0.56,0.93,0.56,1", IsPropertySaveable.Yes, "Tint of a plant that suffers from nutrient deficiency (= not enough fertilizer).")]
        public Color UnderfertilizedColor { get; set; }
        
        [Serialize("0.55,0.27,0.07,1", IsPropertySaveable.Yes, "Tint of a plant that suffers from nutrient burn (= fertilized too much).")]
        public Color OverfertilizedColor{ get; set; }

        [Serialize("0,0", IsPropertySaveable.No, "An offset for drawing the entire plant. Can be useful e.g. with oddly shaped plants that have no stems and need to be closer to the planter.")]
        public Vector2 DrawOffset { get; private set; }

        private int flowerVariants;
        private int leafVariants;
        private int[] flowerTiles;

        public bool Decayed { get; set; }
        public bool FullyGrown { get; set; }

        public float GrowthProgress => Vines.Count / (float)MaximumVines;

        /// <summary>
        /// Multiplier to the product and seed spawn chances based on how far the plant has grown.
        /// 0 if the plant can't produce anything yet (or anymore), 1 when it's fully grown.
        /// </summary>
        /// <param name="growthProgress">Growth (0 - 1) to calculate the multiplier for, eg. to estimate future output. Defaults to the current <see cref="GrowthProgress"/>.</param>
        public float GetProductSpawnChanceMultiplier(float? growthProgress = null)
        {
            float progress = growthProgress ?? GrowthProgress;
            if (!CanGenerateProducts(progress)) { return 0.0f; }
            if (FullyGrown || progress >= 1.0f) { return 1.0f; }
            return MathHelper.Lerp(LinearProductAndSeedMultiplierBeforeFullyGrown.X, LinearProductAndSeedMultiplierBeforeFullyGrown.Y,
                progress);
        }

        private float secondsSinceProduct;
        private float secondsSinceGrowth;
        private float fireCheckCooldown;

        public readonly List<ProducedItem> ProducedItems = new List<ProducedItem>();
        public readonly List<VineTile> Vines = new List<VineTile>();
        private readonly ProducedItem ProducedSeed;
        
        private const int VineChunkSize = 32;

        public Growable(Item item, ContentXElement element) : base(item, element)
        {
            SerializableProperty.DeserializeProperties(this, element);

            LoadBackwardsCompatibilityValues(element);

            if (TooDryThreshold > OverwaterThreshold)
            {
                DebugConsole.AddWarning($"Error in {nameof(Growable)} ({item.Name}): {nameof(TooDryThreshold)} is larger than {nameof(OverwaterThreshold)}.", 
                    contentPackage: item.ContentPackage);
            }
            if (UnderfertilizedThreshold > OverfertilizedThreshold)
            {
                DebugConsole.AddWarning($"Error in {nameof(Growable)} ({item.Name}): {nameof(UnderfertilizedThreshold)} is larger than {nameof(OverfertilizedThreshold)}.",
                    contentPackage: item.ContentPackage);
            }
            if (HealthPerVine <= 0.0f)
            {
                DebugConsole.AddWarning($"Error in {nameof(Growable)} ({item.Name}): {nameof(HealthPerVine)} must be set to greater than 0, the growable's MaxHealth is always a multiple of it.",
                    contentPackage: item.ContentPackage);
            }

            if (element.HasElements)
            {
                foreach (var subElement in element.Elements())
                {
                    switch (subElement.Name.ToString().ToLowerInvariant())
                    {
                        case "produceditem":
                            ProducedItems.Add(new ProducedItem(this.item, subElement));
                            break;
                        case "vinesprites":
                            LoadVines(subElement);
                            break;
                    }
                }
            }

            string? plantNameTag = element.GetAttributeString("grownplantname", null);
            GrownPlantName = plantNameTag.IsNullOrEmpty() ? item.Prefab.Name : TextManager.Get(plantNameTag);

            ProducedSeed = new ProducedItem(this.item, this.item.Prefab);
            flowerTiles = new int[FlowerQuantity];

            UpdateMaxHealth();
        }

        private void LoadBackwardsCompatibilityValues(ContentXElement element)
        {           
            // backwards compatibility (only applied if these are found from the save)
            float hardiness = element.GetAttributeFloat("hardiness", -1.0f);
            if (hardiness > 0.0f)
            {
                //the old "hardiness" value was kind of backwards, it was used the same way as WaterUsedPerSecond, and used to just determine how much health the plant loses per second 
                //(health used to basically represent how much water the plant has, and watering cans increased health)
                OverwaterDamage = hardiness;
                DrynessDamage = hardiness;
                OverfertilizedDamage = hardiness;
                UnderfertilizedDamage = hardiness;

                DebugConsole.AddWarning($"{item.Name}) uses the old \"hardiness\" property which has been replaced by the properties {nameof(OverwaterDamage)}, {nameof(DrynessDamage)}, {nameof(OverfertilizedDamage)} and {nameof(UnderfertilizedDamage)}." +
                    " Converting the old hardiness value to the new values.",
                    contentPackage: item.ContentPackage);
            }
            float growthSpeed = element.GetAttributeFloat("growthspeed", 0.0f);
            if (growthSpeed > 0.0f)
            {
                GrowthInterval = 1.0f / growthSpeed;
            }
            float waterUsedPerSecond = element.GetAttributeFloat("WaterUsedPerSecond", 0.0f);
            if (waterUsedPerSecond > 0.0f)
            {
                DebugConsole.AddWarning($"{item.Name}) uses the old \"waterUsedPerSecond\" property which has been replaced by the property {nameof(WaterUsedPerVine)}." +
                    "Converting the the new value based on the maximum number of vines.",
                    contentPackage: item.ContentPackage);
                WaterUsedPerVine = waterUsedPerSecond * MaximumVines;
            }
            ProductSpawnChance = element.GetAttributeFloat("productrate", ProductSpawnChance);
            SeedSpawnChance = element.GetAttributeFloat("seedrate", SeedSpawnChance);
            //health not saved yet (newly created item, not yet saved in a sub file) -> assume full health
            if (element.GetAttribute("health") == null)
            {
                Health = MaxHealth;
            }
        }

        public override void OnItemLoaded()
        {
            base.OnItemLoaded();
            if (flowerTiles.All(i => i == 0))
            {
                GenerateFlowerTiles();
            }
        }

        private void GenerateFlowerTiles(Random? random = null)
        {
            flowerTiles = new int[FlowerQuantity];
            List<int> pool = new List<int>();
            for (int i = 0; i < MaximumVines - 1; i++) { pool.Add(i); }

            for (int i = 0; i < flowerTiles.Length; i++)
            {
                int index = RandomInt(0, pool.Count, random);
                flowerTiles[i] = pool[index];
                pool.RemoveAt(index);
            }
        }

        partial void LoadVines(ContentXElement element);


        public void OnGrowthTick(Planter planter, PlantSlot slot, float tickDuration)
        {
            if (Decayed) { return; }
            
            // Deal with salinity
            if (planter.Salinity > 0)
            {
                Health += CurrentSalinityEffect(planter) * tickDuration; // Effect on health might be positive or negative
                planter.Salinity -= SaltAbsorption * tickDuration;
            }
            
            // Negative health effects
            if (planter.NormalizedWaterLevel < TooDryThreshold)
            {
                Health -= DrynessDamage * tickDuration;
            }
            else if (planter.NormalizedWaterLevel > OverwaterThreshold)
            {
                Health -= OverwaterDamage * tickDuration;
            }
            if (planter.NormalizedFertilizerLevel < UnderfertilizedThreshold)
            {
                Health -= UnderfertilizedDamage * tickDuration;
            }
            else if (planter.NormalizedFertilizerLevel > OverfertilizedThreshold)
            {
                Health -= OverfertilizedDamage * tickDuration;
            }
            if (planter.PlantLightAmount < NotEnoughLightThreshold)
            {
                Health -= NotEnoughLightDamage * tickDuration;
            }
            else if (planter.PlantLightAmount > TooMuchLightThreshold)
            {
                Health -= TooMuchLightDamage * tickDuration;
            }

            // Health recovers only within optimal thresholds
            bool hasOptimalConditions = HasOptimalConditions(this, planter);
            if (hasOptimalConditions)
            {
                Health += OptimalHealthRecovery * tickDuration;
            }

            // Output and growth
            if (Health > 0)
            {
                if (CanGenerateProducts())
                {
                    TryGenerateProduct(planter, slot, tickDuration);
                }
                if (CanGenerateVines())
                {
                    TickVineGrowth(planter, slot, tickDuration);
                }
                
#if SERVER
                if (serverUpdateTimer > ServerUpdateDelay)
                {
                    item.CreateServerEvent(this);
                    serverUpdateTimer = 0;
                }
                else
                {
                    serverUpdateTimer += tickDuration;
                }
#endif
            }

            CheckPlantState();
#if CLIENT
            UpdateVineHealthVisuals();
#endif

            // Consume resources (do last, after checking the water/fertilizer levels and such, otherwise e.g. a plant requiring 100% water would never grow)
            planter.CurrentWater -= WaterUsedPerVine * Math.Max(Vines.Count, 1) * tickDuration;
            planter.Fertilizer -= FertilizerUsedPerVine * Vines.Count * tickDuration;

        }

        public float CurrentSalinityEffect(Planter planter)
        {
            return SalinityEffect * (planter.Salinity / Planter.SeawaterSalinity);
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            //item is broken (condition at 0), but the plant has not been marked as decayed yet
            //most likely means the server set the condition to 0 -> check plant state to make the plant decay client-side too
            if (!Decayed)
            {
                CheckPlantState();
            }
        }

        private void UpdateVineHealthVisuals()
        {
            if (GetPlanter() is not Planter planter)
            {
                return;
            }
            
            Color healthColor = Color.White;
            float healthAlpha = 1.0f - (Health / MaxHealth);
            Color statusColor = Color.White;
            
            if (planter.NormalizedWaterLevel < TooDryThreshold)
            {
                statusColor = TooDryColor;
            }
            else if (planter.NormalizedWaterLevel > OverwaterThreshold)
            {
                statusColor = OverwateredColor;
            }
            
            if (planter.NormalizedFertilizerLevel < UnderfertilizedThreshold)
            {
                statusColor = statusColor == Color.White ? UnderfertilizedColor : Color.Lerp(statusColor, UnderfertilizedColor, 0.5f);
            }
            else if (planter.NormalizedFertilizerLevel > OverfertilizedThreshold)
            {
                statusColor = statusColor == Color.White ? OverfertilizedColor : Color.Lerp(statusColor, OverfertilizedColor, 0.5f);
            }
            
            // Combine status color with the opacity indicating health
            healthColor = Color.Lerp(Color.White, statusColor, healthAlpha);
            
            foreach (VineTile vine in Vines)
            {
                vine.HealthColor = healthColor;
            }
        }

        /// <param name="growthProgress">Growth (0 - 1) to check, eg. to estimate future output. Defaults to the current <see cref="GrowthProgress"/>.</param>
        public bool CanGenerateProducts(float? growthProgress = null)
        {
            return
                !Decayed &&
                !item.Removed &&
                (growthProgress ?? GrowthProgress) >= RequiredVinePercentageForProducts;
        }

        public bool CanGenerateVines()
        {
            return
                !Decayed &&
                !item.Removed &&
                HasOptimalConditions(this, GetPlanter());
        }

        private void TryGenerateProduct(Planter planter, PlantSlot slot, float tickDuration)
        {
            secondsSinceProduct += tickDuration;
            if (secondsSinceProduct <= ProductIntervalModified) { return; }
            secondsSinceProduct = 0;

            float spawnChanceMultiplier = GetProductSpawnChanceMultiplier();
            if (MathUtils.NearlyEqual(spawnChanceMultiplier, 0f))
            {
                return;
            }

            var potentialVines = Vines.Where(vine => vine.Type != VineTileType.Stem);

            bool spawnProduct = Rand.Range(0f, 1f) < (ProductSpawnChance * spawnChanceMultiplier) && potentialVines.Any();
            bool spawnSeed = Rand.Range(0f, 1f) < (SeedSpawnChance * spawnChanceMultiplier) && potentialVines.Any();

            Vector2 spawnPos;
            if (spawnProduct || spawnSeed)
            {
                VineTile vine = potentialVines.GetRandomUnsynced()!;
                spawnPos = vine.GetWorldPosition(planter, slot.Offset);
            }
            else
            {
                return;
            }

            if (spawnProduct && ProducedItems.Count > 0)
            {
                // Calculate each "fruit" item's spawning probability based on the growable's current condition
                var weightedItems = ProducedItems.Select(it => new
                {
                    Item = it,
                    Weight = MathHelper.Lerp(it.ProbabilityAtMinCondition, it.ProbabilityAtMaxCondition, Health / MaxHealth)
                });

                if (weightedItems.GetRandomByWeight(it => it.Weight, Rand.RandSync.Unsynced) is var selectedItem
                    && selectedItem.Item is ProducedItem producedItem)
                {
                    SpawnItem(Item, producedItem, spawnPos, planter);
                }
                return;
            }

            if (spawnSeed)
            {
                SpawnItem(Item, ProducedSeed, spawnPos, planter);
            }

            static void SpawnItem(Item thisItem, ProducedItem producedItem, Vector2 pos, Planter planter)
            {
                if (producedItem.Prefab == null) { return; }

                // Consume fertilizer if specified
                if (producedItem.FertilizerConsumedPerFruiting > 0f)
                {
                    planter.Fertilizer -= producedItem.FertilizerConsumedPerFruiting;
                }

                GameAnalyticsManager.AddDesignEvent("MicroInteraction:" + (GameMain.GameSession?.GameMode?.Preset.Identifier.Value ?? "null") + ":GardeningProduce:" + thisItem.Prefab.Identifier + ":" + producedItem.Prefab.Identifier);

                Entity.Spawner?.AddItemToSpawnQueue(producedItem.Prefab, pos, onSpawned: it =>
                {
                    //inherit the "stealability" of the plant -> taking fruit in an npc outpost counts as stealing
                    it.AllowStealing = thisItem.AllowStealing;
                    it.OriginalLocationSeed = thisItem.OriginalLocationSeed;
                    it.SpawnedInNonPlayerOutpostOrSubmarine = thisItem.SpawnedInNonPlayerOutpostOrSubmarine;
                    it.AllowSellingIfSpawnedInCurrentOutpost = thisItem.AllowSellingIfSpawnedInCurrentOutpost;
                    foreach (StatusEffect effect in producedItem.StatusEffects)
                    {
                        it.ApplyStatusEffect(effect, ActionType.OnProduceSpawned, 1.0f, isNetworkEvent: true);
                    }
                    it.ApplyStatusEffects(ActionType.OnProduceSpawned, 1.0f, isNetworkEvent: true);
                });
            }
        }

        /// <summary>
        /// Updates plant's state to fully grown or dead depending on its conditions.
        /// </summary>
        /// <returns>True if the plant has finished growing.</returns>
        private bool CheckPlantState()
        {
            if (Decayed) { return true; }

            if (Health <= 0)
            {
                if (!Decayed)
                {
                    GameAnalyticsManager.AddDesignEvent("MicroInteraction:" + (GameMain.GameSession?.GameMode?.Preset.Identifier.Value ?? "null") + ":GardeningDied:" + item.Prefab.Identifier);
                }

                Decayed = true;
#if CLIENT
                foreach (VineTile vine in Vines)
                {
                    vine.DecayDelay = (float)RandomDouble(0f, 30f);
                }
#elif SERVER
                item.CreateServerEvent(this);
#endif
                return true;
            }

            if (Vines.Count >= MaximumVines && !FullyGrown)
            {
                FullyGrown = true;
#if SERVER
                item.CreateServerEvent(this);
#endif
                return true;
            }

            // if the player somehow finds a way to extract the seed out of a planter kill the plant
            if (item.ParentInventory is CharacterInventory)
            {
                Decayed = true;
#if SERVER
                item.CreateServerEvent(this);
#endif
                return true;
            }

            return false;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);

            if (Health > 0 && OxygenPerVine > 0 && item.CurrentHull != null)
            {
                item.CurrentHull.Oxygen += OxygenPerVine * Vines.Count * deltaTime;
            }

            UpdateFires(deltaTime);

#if CLIENT
            foreach (VineTile vine in Vines)
            {
                vine.UpdateScale(deltaTime, GrowthIntervalModified);
            }
#endif

            CheckPlantState();
        }

        private void UpdateFires(float deltaTime)
        {
            if (!Decayed && item.CurrentHull?.FireSources is { } fireSources && FireVulnerability > 0f)
            {
                if (fireCheckCooldown <= 0)
                {
                    foreach (FireSource source in fireSources)
                    {
                        if (source.IsInDamageRange(item.WorldPosition, source.DamageRange))
                        {
                            Health -= FireVulnerability;
                        }
                    }

                    fireCheckCooldown = 10f;
                }
                else
                {
                    fireCheckCooldown -= deltaTime;
                }
            }
        }

        private void TickVineGrowth(Planter planter, PlantSlot slot, float tickDuration)
        {
            if (FullyGrown) { return; }

            secondsSinceGrowth += tickDuration;
            if (secondsSinceGrowth <= GrowthIntervalModified) { return; }
            secondsSinceGrowth = 0;

            GrowVines(planter, slot, steps: 1);
        }

        /// <summary>
        /// Makes the given number of full vine growth steps at once, ignoring growth interval or the current growing conditions.
        /// Useful for setting the growth directly to a particular pre-determined state.
        /// </summary>
        public void GrowVinesImmediately(int growthAttempts)
        {
            if (GetPlanter() is not Planter planter) { return; }
            PlantSlot slot = planter.PlantSlots.GetValueOrDefault(Array.IndexOf(planter.GrowableSeeds, this), Planter.NullSlot);
            GrowVines(planter, slot, growthAttempts, growImmediately: true);
        }

        /// <param name="growImmediately">Should the new vines appear at full size immediately, instead of growing visually over the growth interval?</param>
        private void GrowVines(Planter planter, PlantSlot slot, int steps, bool growImmediately = false)
        {
            int previousVineCount = Vines.Count;

            for (int i = 0; i < steps && !FullyGrown && Vines.Count < MaximumVines; i++)
            {
                if (Vines.Count == 0)
                {
                    // generate first stem
                    GenerateStem();
                }
                else
                {
                    TryGenerateBranches(planter, slot);
                }
            }

            if (Vines.Count != previousVineCount)
            {
                if (growImmediately)
                {
                    // Start these vines fully grown, same as how vines are loaded from saves made by the server
                    for (int i = previousVineCount; i < Vines.Count; i++)
                    {
                        Vines[i].GrowthStep = VineTile.FullyGrownWithFlowers;
                    }
                }
                UpdateMaxHealth();
                planter.UpdatePlantMidpoint();
#if SERVER
                for (int i = 0; i < Vines.Count; i += VineChunkSize)
                {
                    item.CreateServerEvent(this, new EventData(offset: i, growImmediately));
                }
#elif CLIENT
                ResetPlanterSize();
#endif
            }
        }

        public void GenerateStem()
        {
            VineTile stem = new VineTile(this, Vector2.Zero, VineTileType.Stem) { BlockedSides = TileSide.Bottom | TileSide.Left | TileSide.Right };
            Vines.Add(stem);
        }

        public void TryGenerateBranches(Planter planter, PlantSlot slot, Random? random = null, Random? flowerRandom = null)
        {
            List<VineTile> newList = new List<VineTile>(Vines);
            foreach (VineTile oldVines in newList)
            {
                // Each existing tile may branch, so a single call can add multiple vines
                if (Vines.Count >= MaximumVines) { break; }

                if (oldVines.FailedGrowthAttempts > 8 || !oldVines.CanGrowMore()) { continue; }

                if (RandomInt(0, Vines.Count(tile => tile.CanGrowMore()), random) != 0) { continue; }

                TileSide side = oldVines.GetRandomFreeSide(random);

                if (side == TileSide.None)
                {
                    oldVines.FailedGrowthAttempts++;
                    continue;
                }

                if (GrowthWeights != Vector4.One)
                {
                    var (x, y, z, w) = GrowthWeights;
                    float[] weights = { x, y, z, w };
                    int index = (int)Math.Log2((int)side);
                    if (MathUtils.NearlyEqual(weights[index], 0f))
                    {
                        oldVines.FailedGrowthAttempts++;
                        continue;
                    }
                }

                Vector2 pos = oldVines.AdjacentPositions[side];
                Rectangle rect = VineTile.CreatePlantRect(pos, this);

                // Vertical inset (in pixels) from the plant slot to the top edge of the planter rect
                float slotInsetVertical = MathF.Floor(planter.Item.Rect.Height / 2.0f) - slot.Offset.Y;

                // A vine tile can only grow out the planter if its half-size reaches or exceeds the Y inset,
                // below that CollidesWithWorld would always fire and block growth.
                // = In other words, if the vine tiles are tiny (the plant basically grows from a single point), don't care about overlaps with the planter
                if (VineTileSize / 2.0f >= slotInsetVertical &&
                    CollidesWithWorld(rect, planter, slot))
                {
                    oldVines.BlockedSides |= side;
                    oldVines.FailedGrowthAttempts++;
                    continue;
                }

                FoliageConfig flowerConfig = FoliageConfig.EmptyConfig;
                FoliageConfig leafConfig = FoliageConfig.EmptyConfig;

                if (flowerTiles.Any(i => Vines.Count == i))
                {
                    flowerConfig = FoliageConfig.CreateRandomConfig(flowerVariants, MinFlowerScale, MaxFlowerScale, flowerRandom, MinFlowerRotation, MaxFlowerRotation);
                }

                if (LeafProbability >= RandomDouble(0d, 1.0d, flowerRandom) && leafVariants > 0)
                {
                    leafConfig = FoliageConfig.CreateRandomConfig(leafVariants, MinLeafScale, MaxLeafScale, flowerRandom, MinLeafRotation, MaxLeafRotation);
                }

                VineTile newVine = new VineTile(this, pos, VineTileType.CrossJunction, flowerConfig, leafConfig, rect);

                foreach (VineTile otherVine in Vines)
                {
                    var (distX, distY) = pos - otherVine.Position;
                    int absDistX = (int)Math.Abs(distX), absDistY = (int)Math.Abs(distY);

                    // check if the tile is within the with or height distance from us but ignore diagonals
                    if (absDistX > newVine.Rect.Width || absDistY > newVine.Rect.Height || absDistX > 0 && absDistY > 0) { continue; }

                    // determines what side the tile is relative to the new tile by comparing the X/Y distance values
                    // if the X value is bigger than Y it's to the left or right of us and then check if X is negative or positive to determine if it's right or left
                    TileSide connectingSide = absDistX > absDistY ? distX > 0 ? TileSide.Right : TileSide.Left : distY > 0 ? TileSide.Top : TileSide.Bottom;

                    TileSide oppositeSide = connectingSide.GetOppositeSide();

                    if (otherVine.BlockedSides.HasFlag(connectingSide))
                    {
                        newVine.BlockedSides |= oppositeSide;
                        continue;
                    }

                    if (otherVine != oldVines)
                    {
                        otherVine.BlockedSides |= connectingSide;
                        newVine.BlockedSides |= oppositeSide;
                    }
                    else
                    {
                        otherVine.Sides |= connectingSide;
                        newVine.Sides |= oppositeSide;
                    }
                }

                Vines.Add(newVine);

                foreach (VineTile vine in Vines)
                {
                    vine.UpdateType();
                }
            }
        }

        /// <summary>
        /// Recalculates <see cref="MaxHealth"/> based on the current number of vines. Needs to be called whenever the number
        /// of vines changes, and before setting <see cref="Health"/>, because the setter clamps that value to <see cref="MaxHealth"/>.
        /// </summary>
        private void UpdateMaxHealth()
        {
            float previousMaxHealth = MaxHealth;
            MaxHealth = Math.Max(HealthPerVine, HealthPerVine * Vines.Count);
            if (previousMaxHealth > 0.0f)
            {
                Health *= MaxHealth / previousMaxHealth;
            }
            else
            {
                //max health not set previously, assume full health
                Health = MaxHealth;
            }
        }

        /// <summary>
        /// Recalculates <see cref="MaxHealth"/> and sets the plant to full health.
        /// </summary>
        public void RestoreFullHealth()
        {
            UpdateMaxHealth();
            Health = MaxHealth;
        }

        private bool CollidesWithWorld(Rectangle rect, Planter planter, PlantSlot slot)
        {
            if (Vines.Any(g => g.Rect.Contains(rect))) { return true; }

            Rectangle worldRect = rect;
            worldRect.Location = planter.Item.WorldPosition.ToPoint() + slot.Offset.ToPoint() + worldRect.Location;
            worldRect.Y -= worldRect.Height;

            Rectangle planterRect = planter.Item.WorldRect;
            planterRect.Y -= planterRect.Height;

            if (planterRect.Intersects(worldRect))
            {
#if DEBUG
                if (!FailedRectangles.Contains(worldRect))
                {
                    FailedRectangles.Add(worldRect);
                }
#endif
                return true;
            }

            Vector2 topLeft = ConvertUnits.ToSimUnits(new Vector2(worldRect.Left, worldRect.Top)),
                    topRight = ConvertUnits.ToSimUnits(new Vector2(worldRect.Right, worldRect.Top)),
                    bottomLeft = ConvertUnits.ToSimUnits(new Vector2(worldRect.Left, worldRect.Bottom)),
                    bottomRight = ConvertUnits.ToSimUnits(new Vector2(worldRect.Right, worldRect.Bottom));

            // ray casting a cross on the corners didn't seem to work so we are ray casting along the perimeter
            bool hasCollision = planterRect.Intersects(worldRect) || LineCollides(topLeft, topRight) || LineCollides(topRight, bottomRight) || LineCollides(bottomRight, bottomLeft) || LineCollides(bottomLeft, topLeft);

#if DEBUG
            if (hasCollision)
            {
                if (!FailedRectangles.Contains(worldRect))
                {
                    FailedRectangles.Add(worldRect);
                }
            }
#endif
            return hasCollision;

            static bool LineCollides(Vector2 point1, Vector2 point2)
            {
                const Category category = Physics.CollisionWall | Physics.CollisionCharacter | Physics.CollisionItem | Physics.CollisionLevel;
                return Submarine.PickBody(point1, point2, collisionCategory: category, customPredicate: f => !(f.UserData is Hull) && f.CollidesWith.HasFlag(Physics.CollisionItem)) != null;
            }
        }

        public override XElement Save(XElement parentElement)
        {
            XElement element = base.Save(parentElement);
            element.Add(new XAttribute("flowertiles", string.Join(",", flowerTiles)));
            element.Add(new XAttribute("decayed", Decayed));
            foreach (VineTile vine in Vines)
            {
                XElement vineElement = new XElement("Vine");
                vineElement.Add(new XAttribute("sides", (int)vine.Sides));
                vineElement.Add(new XAttribute("blockedsides", (int)vine.BlockedSides));
                vineElement.Add(new XAttribute("pos", XMLExtensions.Vector2ToString(vine.Position)));
                vineElement.Add(new XAttribute("tile", (int)vine.Type));
                vineElement.Add(new XAttribute("failedattempts", vine.FailedGrowthAttempts));
#if SERVER
                vineElement.Add(new XAttribute("growthscale", Decayed ? VineTile.VineGrownFloweringStarted : VineTile.FullyGrownWithFlowers));
#else
                vineElement.Add(new XAttribute("growthscale", vine.GrowthStep));
#endif
                vineElement.Add(vine.FlowerConfig.ToXML("FlowerConfig"));
                vineElement.Add(vine.LeafConfig.ToXML("LeafConfig"));

                element.Add(vineElement);
            }

            return element;
        }

        public override void Load(ContentXElement componentElement, bool usePrefabValues, IdRemap idRemap, bool isItemSwap)
        {
            // The vines must be loaded before the base implementation because the number of vines determines MaxHealth,
            // which in turn needs to be up to date before the base implementation applies the saved Health value.
            LoadVineTiles(componentElement);
            UpdateMaxHealth();

            base.Load(componentElement, usePrefabValues, idRemap, isItemSwap);
            flowerTiles = componentElement.GetAttributeIntArray("flowertiles", Array.Empty<int>())!;
            Decayed = componentElement.GetAttributeBool("decayed", false);
        }

        private void LoadVineTiles(ContentXElement componentElement)
        {
            Vines.Clear();
            foreach (var element in componentElement.Elements())
            {
                if (element.Name.ToString().Equals("vine", StringComparison.OrdinalIgnoreCase))
                {
                    VineTileType type = (VineTileType)element.GetAttributeInt("tile", 0);
                    Vector2 pos = element.GetAttributeVector2("pos", Vector2.Zero);
                    TileSide sides = (TileSide)element.GetAttributeInt("sides", 0);
                    TileSide blockedSides = (TileSide)element.GetAttributeInt("blockedsides", 0);
                    int failedAttempts = element.GetAttributeInt("failedattempts", 0);
                    float growthscale = element.GetAttributeFloat("growthscale", 0f);

                    FoliageConfig flowerConfig, leafConfig;

                    // backwards compatibility
                    if (element.GetAttribute("flowerconfig") is { } flowerConfigAttr && element.GetAttribute("leafconfig") is { } leafConfigAttr)
                    {
                        flowerConfig = LegacyFoliageConfig.Deserialize(flowerConfigAttr.GetAttributeInt(LegacyFoliageConfig.EmptyConfigValue)).ToNewFormat();
                        leafConfig = LegacyFoliageConfig.Deserialize(leafConfigAttr.GetAttributeInt(LegacyFoliageConfig.EmptyConfigValue)).ToNewFormat();
                    }
                    else
                    {
                        XElement? flowerConfigElement = element.GetChildElement("FlowerConfig");
                        XElement? leafConfigElement = element.GetChildElement("LeafConfig");

                        flowerConfig = flowerConfigElement != null ? FoliageConfig.FromXML(flowerConfigElement) : FoliageConfig.EmptyConfig;
                        leafConfig = leafConfigElement != null ? FoliageConfig.FromXML(leafConfigElement) : FoliageConfig.EmptyConfig;
                    }
                    VineTile tile = new VineTile(this, pos, type, flowerConfig, leafConfig)
                    {
                        Sides = sides, BlockedSides = blockedSides, FailedGrowthAttempts = failedAttempts, GrowthStep = growthscale
                    };

                    Vines.Add(tile);
                }
            }
        }
        
        public Planter? GetPlanter()
        {
            if (item.ParentInventory is ItemInventory itemInventory && itemInventory.Owner is Item parentItem)
            {
                if (parentItem.GetComponent<Planter>() is { } planter)
                {
                    return planter;
                }
            }
            return null;
        }

        public bool CanGrowMore() => Vines.Any(static tile => tile.CanGrowMore());

        public static int RandomInt(int min, int max, Random? random = null) => random?.Next(min, max) ?? Rand.Range(min, max);
        public static double RandomDouble(double min, double max, Random? random = null) => random?.NextDouble() * (max - min) + min ?? Rand.Range(min, max);

        public bool HasOptimalConditions(Planter planter)
        {
            return HasOptimalConditions(this, planter);
        }

        public static bool HasOptimalConditions(Growable growable, Planter? planter)
        {
            if (planter == null) { return false; }
            // Only check thresholds if exceeding them would actually cause damage
            return (growable.DrynessDamage <= 0 || planter.NormalizedWaterLevel >= growable.TooDryThreshold)
                   && (growable.OverwaterDamage <= 0 || planter.NormalizedWaterLevel <= growable.OverwaterThreshold)
                   && (growable.UnderfertilizedDamage <= 0 || planter.NormalizedFertilizerLevel >= growable.UnderfertilizedThreshold)
                   && (growable.OverfertilizedDamage <= 0 || planter.NormalizedFertilizerLevel <= growable.OverfertilizedThreshold)
                   && (planter.Salinity <= 0 || growable.SalinityEffect >= 0)
                   && (growable.NotEnoughLightDamage <= 0 || planter.PlantLightAmount >= growable.NotEnoughLightThreshold)
                   && (growable.TooMuchLightDamage <= 0 || planter.PlantLightAmount <= growable.TooMuchLightThreshold);
        }
    }
}