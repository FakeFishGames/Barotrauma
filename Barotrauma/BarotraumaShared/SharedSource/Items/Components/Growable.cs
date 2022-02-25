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
using Vector2 = Microsoft.Xna.Framework.Vector2;
using Vector4 = Microsoft.Xna.Framework.Vector4;

namespace Barotrauma.Items.Components
{
    internal class ProducedItem
    {
        [Serialize(0f, IsPropertySaveable.Yes)]
        public float Probability { get; set; }

        public readonly List<StatusEffect> StatusEffects = new List<StatusEffect>();

        public readonly Item Producer;
        
        public readonly ItemPrefab? Prefab;

        public ProducedItem(Item producer, ItemPrefab prefab, float probability)
        {
            Producer = producer;
            Prefab = prefab;
            Probability = probability;
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
                        if (effect.type != ActionType.OnProduceSpawned)
                        {
                            DebugConsole.ThrowError("Only OnProduceSpawned type can be used in <ProducedItem>.");
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
    internal enum VineTileType
    {
        Stem = 0b0000,
        CrossJunction = 0b1111,
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
    internal enum TileSide
    {
        None = 0,
        Top = 1 << 0,
        Left = 1 << 1,
        Bottom = 1 << 2,
        Right = 1 << 3
    }

    internal struct FoliageConfig
    {
        public static FoliageConfig EmptyConfig = new FoliageConfig { Variant = -1, Rotation = 0f, Scale = 1.0f };
        public static readonly int EmptyConfigValue = EmptyConfig.Serialize();

        public int Variant;
        public float Rotation;
        public float Scale;

        public readonly int Serialize()
        {
            int variant = Math.Min(Variant + 1, 15);
            int scale = (int) (Scale * 10f);
            int rotation = (int) (Rotation / MathHelper.TwoPi * 10f);

            return variant | (scale << 4) | (rotation << 8);
        }

        public static FoliageConfig Deserialize(int value)
        {
            int variant = value & 0x00F;
            int scale = (value & 0x0F0) >> 4;
            int rotation = (value & 0xF00) >> 8;

            return new FoliageConfig { Variant = variant - 1, Scale = scale / 10f, Rotation = rotation / 10f * MathHelper.TwoPi };
        }

        public static FoliageConfig CreateRandomConfig(int maxVariants, float minScale, float maxScale, Random? random = null)
        {
            int flowerVariant = Growable.RandomInt(0, maxVariants, random);
            float flowerScale = (float) Growable.RandomDouble(minScale, maxScale, random);
            float flowerRotation = (float) Growable.RandomDouble(0, MathHelper.TwoPi, random);
            return new FoliageConfig { Variant = flowerVariant, Scale = flowerScale, Rotation = flowerRotation };
        }
    }

    internal partial class VineTile
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
        public static int Size = 32;
        
        
        public float VineStep;
        public float FlowerStep;

        private float growthStep;
        public float GrowthStep
        {
            get => growthStep;
            set
            {
                const float limit = 1.0f;
                growthStep = value;
                VineStep = Math.Min((float) Math.Pow(value, 2), limit);
                if (value > limit)
                {
                    FlowerStep = Math.Min((float) Math.Pow(value - limit, 2), limit);
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
            Rect = rect ?? CreatePlantRect(position);
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

        public void UpdateScale(float deltaTime)
        {
            bool decayed = Parent?.Decayed ?? false;

            if (decayed && GrowthStep > 1.0f)
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

            if (GrowthStep >= 2.0f || decayed) { return; }

            GrowthStep += deltaTime;

            if (GrowthStep < 1.0f)
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

            Type = (VineTileType) Sides;
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

                value = pool.RandomElementByWeight(i => weights[i]);
            }
            
            return (TileSide) (1 << value);
        }

        public bool CanGrowMore() => (Sides | BlockedSides).Count() < 4;

        public bool IsSideBlocked(TileSide side) => BlockedSides.HasFlag(side) || Sides.HasFlag(side);

        public static Rectangle CreatePlantRect(Vector2 pos) => new Rectangle((int) pos.X - Size / 2, (int) pos.Y + Size / 2, Size, Size);
    }

    internal static class GrowthSideExtension
    {
        // K&R algorithm for counting how many bits are set in a bit field
        public static int Count(this TileSide side)
        {
            int n = (int) side;
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

    internal partial class Growable : ItemComponent, IServerSerializable
    {
        // used for debugging where a vine failed to grow
        public readonly HashSet<Rectangle> FailedRectangles = new HashSet<Rectangle>();

        [Serialize(1f, IsPropertySaveable.Yes, "How fast the plant grows.")]
        public float GrowthSpeed { get; set; }

        [Serialize(100f, IsPropertySaveable.Yes, "How long the plant can go without watering.")]
        public float MaxHealth { get; set; }

        [Serialize(1f, IsPropertySaveable.Yes, "How much damage the plant takes while in water.")]
        public float FloodTolerance { get; set; }

        [Serialize(1f, IsPropertySaveable.Yes, "How much damage the plant takes while growing.")]
        public float Hardiness { get; set; }

        [Serialize(0.01f, IsPropertySaveable.Yes, "How often a seed is produced.")]
        public float SeedRate { get; set; }

        [Serialize(0.01f, IsPropertySaveable.Yes, "How often a product item is produced.")]
        public float ProductRate { get; set; }

        [Serialize(0.5f, IsPropertySaveable.Yes, "Probability of an attribute being randomly modified in a newly produced seed.")]
        public float MutationProbability { get; set; }

        [Serialize("1.0,1.0,1.0,1.0", IsPropertySaveable.Yes, "Color of the flowers.")]
        public Color FlowerTint { get; set; }

        [Serialize(3, IsPropertySaveable.Yes, "Number of flowers drawn when fully grown")]
        public int FlowerQuantity { get; set; }

        [Serialize(0.25f, IsPropertySaveable.Yes, "Size of the flower sprites.")]
        public float BaseFlowerScale { get; set; }

        [Serialize(0.5f, IsPropertySaveable.Yes, "Size of the leaf sprites.")]
        public float BaseLeafScale { get; set; }

        [Serialize("1.0,1.0,1.0,1.0", IsPropertySaveable.Yes, "Color of the leaves.")]
        public Color LeafTint { get; set; }

        [Serialize(0.33f, IsPropertySaveable.Yes, "Chance of a leaf appearing behind a branch.")]
        public float LeafProbability { get; set; }

        [Serialize("1.0,1.0,1.0,1.0", IsPropertySaveable.Yes, "Color of the vines.")]
        public Color VineTint { get; set; }

        [Serialize(32, IsPropertySaveable.Yes, "Maximum number of vine tiles the plant can grow.")]
        public int MaximumVines { get; set; }

        [Serialize(0.25f, IsPropertySaveable.Yes, "Size of the vine sprites.")]
        public float VineScale { get; set; }

        [Serialize("0.26,0.27,0.29,1.0", IsPropertySaveable.Yes, "Tint of a dead plant.")]
        public Color DeadTint { get; set; }

        [Serialize("1,1,1,1", IsPropertySaveable.Yes, "Probability for the plant to grow in a direction.")]
        public Vector4 GrowthWeights { get; set; }

        [Serialize(0.0f, IsPropertySaveable.Yes, "How much damage is taken from fires.")]
        public float FireVulnerability { get; set; }

        private const float increasedDeathSpeed = 10f;
        private bool accelerateDeath;
        private float health;
        private int flowerVariants;
        private int leafVariants;
        private int[] flowerTiles;

        public float Health
        {
            get => health;
            set => health = Math.Clamp(value, 0, MaxHealth);
        }

        public bool Decayed;
        public bool FullyGrown;

        private const int maxProductDelay = 10,
                          maxVineGrowthDelay = 10;

        private int productDelay;
        private int vineDelay;
        private float fireCheckCooldown;

        public readonly List<ProducedItem> ProducedItems = new List<ProducedItem>();
        public readonly List<VineTile> Vines = new List<VineTile>();
        private readonly ProducedItem ProducedSeed;

        private static float MinFlowerScale = 0.5f, MaxFlowerScale = 1.0f, MinLeafScale = 0.5f, MaxLeafScale = 1.0f;
        private const int VineChunkSize = 32;

        public Growable(Item item, ContentXElement element) : base(item, element)
        {
            SerializableProperty.DeserializeProperties(this, element);

            Health = MaxHealth;

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

            ProducedSeed = new ProducedItem(this.item, this.item.Prefab, 1.0f);
            flowerTiles = new int[FlowerQuantity];
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

        public void OnGrowthTick(Planter planter, PlantSlot slot)
        {
            if (Decayed) { return; }

            if (FullyGrown)
            {
                TryGenerateProduct(planter, slot);
            }

            if (Health > 0)
            {
                GrowVines(planter, slot);

                // fertilizer makes the plant tick faster, compensate by halving water requirement
                float multipler = planter.Fertilizer > 0 ? 0.5f : 1f;

                Health -= (accelerateDeath ? Hardiness * increasedDeathSpeed : Hardiness) * multipler;

                if (planter.Item.InWater)
                {
                    Health -= FloodTolerance * multipler;
                }
#if SERVER
                if (FullyGrown)
                {
                    if (serverHealthUpdateTimer > serverHealthUpdateDelay)
                    {
                        item.CreateServerEvent(this);
                        serverHealthUpdateTimer = 0;
                    }
                    else
                    {
                        serverHealthUpdateTimer++;
                    }
                }
#endif
            }

            CheckPlantState();

#if CLIENT
            UpdateBranchHealth();
#endif
        }

        private void UpdateBranchHealth()
        {
            Color healthColor = Color.White * (1.0f - Health / MaxHealth);
            foreach (VineTile vine in Vines)
            {
                vine.HealthColor = healthColor;
            }
        }

        private void TryGenerateProduct(Planter planter, PlantSlot slot)
        {
            productDelay++;
            if (productDelay <= maxProductDelay) { return; }

            productDelay = 0;

            bool spawnProduct = Rand.Range(0f, 1f, Rand.RandSync.Unsynced) < ProductRate,
                 spawnSeed = Rand.Range(0f, 1f, Rand.RandSync.Unsynced) < SeedRate;

            Vector2 spawnPos;

            if (spawnProduct || spawnSeed)
            {
                VineTile vine = Vines.GetRandomUnsynced();
                spawnPos = vine.GetWorldPosition(planter, slot.Offset);
            }
            else
            {
                return;
            }

            if (spawnProduct && ProducedItems.Any())
            {
                SpawnItem(Item, ProducedItems.RandomElementByWeight(it => it.Probability), spawnPos);
                return;
            }

            if (spawnSeed)
            {
                SpawnItem(Item, ProducedSeed, spawnPos);
            }

            static void SpawnItem(Item thisItem, ProducedItem producedItem, Vector2 pos)
            {
                if (producedItem.Prefab == null) { return; }

                GameAnalyticsManager.AddDesignEvent("MicroInteraction:" + (GameMain.GameSession?.GameMode?.Preset.Identifier.Value ?? "null") + ":GardeningProduce:" + thisItem.Prefab.Identifier + ":" + producedItem.Prefab.Identifier);

                Entity.Spawner?.AddItemToSpawnQueue(producedItem.Prefab, pos, onSpawned: it =>
                {
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
                    vine.DecayDelay = (float) RandomDouble(0f, 30f);
                }
#endif
#if SERVER
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

            if (!FullyGrown && !accelerateDeath && Vines.Any() && Vines.All(tile => !tile.CanGrowMore()))
            {
                accelerateDeath = true;
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

            UpdateFires(deltaTime);

#if CLIENT
            foreach (VineTile vine in Vines)
            {
                vine.UpdateScale(deltaTime);
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

                    fireCheckCooldown = 5f;
                }
                else
                {
                    fireCheckCooldown -= deltaTime;
                }
            }
        }

        private void GrowVines(Planter planter, PlantSlot slot)
        {
            if (FullyGrown) { return; }

            vineDelay++;
            if (vineDelay <= maxVineGrowthDelay / GrowthSpeed) { return; }

            vineDelay = 0;

            if (!Vines.Any())
            {
                // generate first stem
                GenerateStem();
                return;
            }

            int count = Vines.Count;

            TryGenerateBranches(planter, slot);

            if (Vines.Count > count)
            {
#if SERVER
                for (int i = 0; i < Vines.Count; i += VineChunkSize)
                {
                    GameMain.Server.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ComponentState, item.GetComponentIndex(this), i });
                }
#elif CLIENT
                ResetPlanterSize();
#endif
            }
        }

        private void GenerateStem()
        {
            VineTile stem = new VineTile(this, Vector2.Zero, VineTileType.Stem) { BlockedSides = TileSide.Bottom | TileSide.Left | TileSide.Right };
            Vines.Add(stem);
        }

        private void TryGenerateBranches(Planter planter, PlantSlot slot, Random? random = null, Random? flowerRandom = null)
        {
            List<VineTile> newList = new List<VineTile>(Vines);
            foreach (VineTile oldVines in newList)
            {
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
                    int index = (int) Math.Log2((int) side);
                    if (MathUtils.NearlyEqual(weights[index], 0f))
                    {
                        oldVines.FailedGrowthAttempts++;
                        continue;
                    }
                }

                Vector2 pos = oldVines.AdjacentPositions[side];
                Rectangle rect = VineTile.CreatePlantRect(pos);

                if (CollidesWithWorld(rect, planter, slot))
                {
                    oldVines.BlockedSides |= side;
                    oldVines.FailedGrowthAttempts++;
                    continue;
                }

                FoliageConfig flowerConfig = FoliageConfig.EmptyConfig;
                FoliageConfig leafConfig = FoliageConfig.EmptyConfig;

                if (flowerTiles.Any(i => Vines.Count == i))
                {
                    flowerConfig = FoliageConfig.CreateRandomConfig(flowerVariants, MinFlowerScale, MaxFlowerScale, flowerRandom);
                }

                if (LeafProbability >= RandomDouble(0d, 1.0d, flowerRandom) && leafVariants > 0)
                {
                    leafConfig = FoliageConfig.CreateRandomConfig(leafVariants, MinLeafScale, MaxLeafScale, flowerRandom);
                }

                VineTile newVine = new VineTile(this, pos, VineTileType.CrossJunction, flowerConfig, leafConfig, rect);

                foreach (VineTile otherVine in Vines)
                {
                    var (distX, distY) = pos - otherVine.Position;
                    int absDistX = (int) Math.Abs(distX), absDistY = (int) Math.Abs(distY);

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
                vineElement.Add(new XAttribute("sides", (int) vine.Sides));
                vineElement.Add(new XAttribute("blockedsides", (int) vine.BlockedSides));
                vineElement.Add(new XAttribute("pos", XMLExtensions.Vector2ToString(vine.Position)));
                vineElement.Add(new XAttribute("tile", (int) vine.Type));
                vineElement.Add(new XAttribute("failedattempts", vine.FailedGrowthAttempts));
#if SERVER
                vineElement.Add(new XAttribute("growthscale", Decayed ? 1.0f : 2.0f));
#else
                vineElement.Add(new XAttribute("growthscale", vine.GrowthStep));
#endif
                vineElement.Add(new XAttribute("flowerconfig", vine.FlowerConfig.Serialize()));
                vineElement.Add(new XAttribute("leafconfig", vine.LeafConfig.Serialize()));

                element.Add(vineElement);
            }

            return element;
        }

        public override void Load(ContentXElement componentElement, bool usePrefabValues, IdRemap idRemap)
        {
            base.Load(componentElement, usePrefabValues, idRemap);
            flowerTiles = componentElement.GetAttributeIntArray("flowertiles", Array.Empty<int>())!;
            Decayed = componentElement.GetAttributeBool("decayed", false);

            Vines.Clear();
            foreach (var element in componentElement.Elements())
            {
                if (element.Name.ToString().Equals("vine", StringComparison.OrdinalIgnoreCase))
                {
                    VineTileType type = (VineTileType) element.GetAttributeInt("tile", 0);
                    Vector2 pos = element.GetAttributeVector2("pos", Vector2.Zero);
                    TileSide sides = (TileSide) element.GetAttributeInt("sides", 0);
                    TileSide blockedSides = (TileSide) element.GetAttributeInt("blockedsides", 0);
                    int failedAttempts = element.GetAttributeInt("failedattempts", 0);
                    float growthscale = element.GetAttributeFloat("growthscale", 0f);
                    int flowerConfig = element.GetAttributeInt("flowerconfig", FoliageConfig.EmptyConfigValue);
                    int leafConfig = element.GetAttributeInt("leafconfig", FoliageConfig.EmptyConfigValue);

                    VineTile tile = new VineTile(this, pos, type, FoliageConfig.Deserialize(flowerConfig), FoliageConfig.Deserialize(leafConfig))
                    {
                        Sides = sides, BlockedSides = blockedSides, FailedGrowthAttempts = failedAttempts, GrowthStep = growthscale
                    };

                    Vines.Add(tile);
                }
            }
        }

        private bool CanGrowMore() => Vines.Any(tile => tile.CanGrowMore());

        public static int RandomInt(int min, int max, Random? random = null) => random?.Next(min, max) ?? Rand.Range(min, max);
        public static double RandomDouble(double min, double max, Random? random = null) => random?.NextDouble() * (max - min) + min ?? Rand.Range(min, max);
    }
}