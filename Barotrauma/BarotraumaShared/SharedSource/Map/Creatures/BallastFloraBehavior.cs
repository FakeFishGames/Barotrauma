#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;

namespace Barotrauma.MapCreatures.Behavior
{
    class BallastFloraBranch : VineTile
    {
        public readonly BallastFloraBehavior? ParentBallastFlora;
        public int ID = -1;

        public ushort ClaimedItem;
        public bool HasClaimedItem;

        public float MaxHealth = 100f;
        public float Health = 100f;
        
        public bool SpawningItem;
        public Item? AttackItem;

        public bool IsRoot;
        public bool Removed;

        public Hull? CurrentHull;

        public float Pulse = 1.0f;
        private bool inflate;
        private float pulseDelay = Rand.Range(0f, 3f);

        public float AccumulatedDamage;

        // Adjacent tiles, used to free up sides when this branch gets removed
        public readonly Dictionary<TileSide, BallastFloraBranch> Connections = new Dictionary<TileSide, BallastFloraBranch>();

        public BallastFloraBranch(BallastFloraBehavior? parent, Vector2 position, VineTileType type, FoliageConfig? flowerConfig = null, FoliageConfig? leafConfig = null, Rectangle? rect = null)
            : base(null, position, type, flowerConfig, leafConfig, rect)
        {
            ParentBallastFlora = parent;
        }

        public void UpdateHealth()
        {
            if (MaxHealth <= Health) { return; }
            Color healthColor = Color.White * (1.0f - Health / MaxHealth);
            HealthColor = healthColor;
        }

        public void UpdatePulse(float deltaTime, float inflateSpeed, float deflateSpeed, float delay)
        {
            if (ParentBallastFlora == null) { return; }

            if (pulseDelay > 0)
            {
                pulseDelay -= deltaTime;
                return;
            }

            if (inflate)
            {
                Pulse += inflateSpeed * deltaTime;

                if (Pulse > 1.25f)
                {
                    inflate = false;
                }
            }
            else
            {
                Pulse -= deflateSpeed * deltaTime;
                if (Pulse < 1f)
                {
                    inflate = true;
                    pulseDelay = delay;
                }
            }
        }
    }

    internal partial class BallastFloraBehavior : ISerializableEntity
    {
#if DEBUG
        public List<Tuple<Vector2, Vector2>> debugSearchLines = new List<Tuple<Vector2, Vector2>>();
#endif

        private static List<BallastFloraBehavior> _entityList = new List<BallastFloraBehavior>();
        public static IEnumerable<BallastFloraBehavior> EntityList => _entityList;

        public enum NetworkHeader
        {
            Spawn,
            Kill,
            BranchCreate,
            BranchRemove,
            BranchDamage,
            Infect
        }

        public enum AttackType
        {
            Fire,
            Explosives,
            Other
        }

        public struct AITarget
        {
            public string[] Tags;
            public int Priority;

            public AITarget(XElement element)
            {
                Tags = element.GetAttributeStringArray("tags", new string[0]);
                Priority = element.GetAttributeInt("priority", 0);
            }

            public bool Matches(Item item)
            {
                foreach (string tag in item.GetTags())
                {
                    foreach (string targetTag in Tags)
                    {
                        if (tag == targetTag) { return true; }
                    }
                }

                return false;
            }
        }

        [Serialize(0.25f, true, "Scale of the branches")]
        public float BaseBranchScale { get; set; }

        [Serialize(0.25f, true, "Scale of the flowers")]
        public float BaseFlowerScale { get; set; }

        [Serialize(0.5f, true, "Scale of the leaves")]
        public float BaseLeafScale { get; set; }

        [Serialize(0.33f, true, "Chance for a flower to appear on the branch")]
        public float FlowerProbability { get; set; }

        [Serialize(0.7f, true, "Change for leaves to appear for the branch")]
        public float LeafProbability { get; set; }

        [Serialize(3f, true, "Delay between pulses")]
        public float PulseDelay { get; set; }

        [Serialize(3f, true, "How fast the flower inflates during a pulse")]
        public float PulseInflateSpeed { get; set; }

        [Serialize(1f, true, "How fast the flower deflates")]
        public float PulseDeflateSpeed { get; set; }

        [Serialize(32, true, "How many vines must grow before the plant breaks thru the wall")]
        public int BreakthroughPoint { get; set; }

        [Serialize(false, true, "Has the plant grown large enough to expose itself")]
        public bool HasBrokenThrough { get; set; }

        [Serialize(300, true, "How far the ballast flora can detect items")]
        public int Sight { get; set; }

        [Serialize(100, true, "How much health the branches have")]
        public int BranchHealth { get; set; }

        [Serialize(400, true, "How much health the stem has")]
        public int StemHealth { get; set; }

        [Serialize(300f, true, "How much power the ballast flora takes from junction boxes")]
        public float PowerConsumptionMin { get; set; }

        [Serialize(3000f, true, "How much the power drain spikes")]
        public float PowerConsumptionMax { get; set; }

        [Serialize(10f, true, "How long it takes for power drain to wind down")]
        public float PowerConsumptionDuration { get; set; }

        [Serialize(250f, true, "How much power does it take to accelerate growth")]
        public float PowerRequirement { get; set; }

        [Serialize(5f, true, "Maximum anger, anger increases when the plant gets damaged and increases growth speed")]
        public float MaxAnger { get; set; }

        [Serialize(10000f, true, "Maximum power buffer")]
        public float MaxPowerCapacity { get; set; }

        [Serialize("", true, "Item prefab that is spawned when threatened")]
        public string AttackItemPrefab { get; set; } = "";

        [Serialize(0.8f, true, "How resistant the ballast flora is to exlposives before it blooms")]
        public float ExplosionResistance { get; set; }

        [Serialize(5f, true, "How much damage is taken from open fires")]
        public float FireVulnerability { get; set; }

        [Serialize(0.5f, true, "How much resistance against fire is gained while submerged.")]
        public float SubmergedWaterResistance { get; set; }

        [Serialize(0.8f, true, "What depth the branches will be drawn on")]
        public float BranchDepth { get; set; }
        
        [Serialize("", true, "What sound to play when the ballast flora bursts thru walls")]
        public string BurstSound { get; set; } = "";

        private float availablePower;

        [Serialize(0f, true, "How much power the ballast flora has stored.")]
        public float AvailablePower
        {
            get => availablePower;
            set => availablePower = Math.Max(value, MaxPowerCapacity);
        }

        private float anger;

        [Serialize(1f, true, "How enraged the flora is, affects how fast it grows.")]
        public float Anger
        {
            get => anger;
            set => anger = Math.Clamp(value, 1f, MaxAnger);
        }

        public string Name { get; } = "";

        public Hull Parent { get; private set; }

        public BallastFloraPrefab Prefab { get; private set; }

        public Dictionary<string, SerializableProperty> SerializableProperties { get; private set; }

        public Vector2 Offset;

        public readonly List<Item> ClaimedTargets = new List<Item>();
        public readonly List<PowerTransfer> ClaimedJunctionBoxes = new List<PowerTransfer>();
        public readonly List<PowerContainer> ClaimedBatteries = new List<PowerContainer>();
        public readonly Dictionary<Item, int> IgnoredTargets = new Dictionary<Item, int>();

        private readonly List<Tuple<UInt16, int>> tempClaimedTargets = new List<Tuple<ushort, int>>();

        private int flowerVariants, leafVariants;
        public readonly List<AITarget> Targets = new List<AITarget>();

        public float PowerConsumptionTimer;

        private float defenseCooldown, toxinsCooldown, fireCheckCooldown;
        private float damageIndicatorTimer, selfDamageTimer, toxinsTimer;

        private readonly List<BallastFloraBranch> branchesVulnerableToFire = new List<BallastFloraBranch>();

        public readonly List<BallastFloraBranch> Branches = new List<BallastFloraBranch>();
        private readonly List<Body> bodies = new List<Body>();

        public readonly BallastFloraStateMachine StateMachine;

        public int GrowthWarps;

        public void OnMapLoaded()
        {
            foreach ((ushort itemId, int branchid) in tempClaimedTargets)
            {
                if (Entity.FindEntityByID(itemId) is Item item)
                {
                    ClaimTarget(item, Branches.FirstOrDefault(b => b.ID == branchid), true);
                }
            }

            foreach (BallastFloraBranch branch in Branches)
            {
                UpdateConnections(branch);
                CreateBody(branch);
            }
        }


        private int CreateID()
        {
            int maxId = Branches.Any() ? Branches.Max(b => b.ID) : 0;
            return ++maxId;
        }

        public Vector2 GetWorldPosition()
        {
            return Parent.WorldPosition + Offset;
        }

        public BallastFloraBehavior(Hull parent, BallastFloraPrefab prefab, Vector2 offset, bool firstGrowth = false)
        {
            Prefab = prefab;
            Offset = offset;
            Parent = parent;
            SerializableProperties = SerializableProperty.DeserializeProperties(this, prefab.Element);
            LoadPrefab(prefab.Element);
            StateMachine = new BallastFloraStateMachine(this);
            if (firstGrowth) { GenerateStem(); }
            _entityList.Add(this);
        }

        partial void LoadPrefab(XElement element);

        public void LoadTargets(XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                Targets.Add(new AITarget(subElement));
            }
        }

        public void Save(XElement element)
        {
            XElement saveElement = new XElement(nameof(BallastFloraBehavior),
                new XAttribute("identifier", Prefab.Identifier),
                new XAttribute("offset", XMLExtensions.Vector2ToString(Offset)));

            SerializableProperty.SerializeProperties(this, saveElement);

            foreach (BallastFloraBranch branch in Branches)
            {
                XElement be = new XElement("Branch",
                    new XAttribute("flowerconfig", branch.FlowerConfig.Serialize()),
                    new XAttribute("leafconfig", branch.LeafConfig.Serialize()),
                    new XAttribute("pos", XMLExtensions.Vector2ToString(branch.Position)),
                    new XAttribute("ID", branch.ID),
                    new XAttribute("isroot", branch.IsRoot),
                    new XAttribute("health", branch.Health.ToString("G", CultureInfo.InvariantCulture)),
                    new XAttribute("maxhealth", branch.MaxHealth.ToString("G", CultureInfo.InvariantCulture)),
                    new XAttribute("sides", (int)branch.Sides),
                    new XAttribute("blockedsides", (int)branch.BlockedSides));

                if (branch.HasClaimedItem)
                {
                    be.Add(new XAttribute("claimed", (int)branch.ClaimedItem));
                }

                saveElement.Add(be);
            }

            foreach (Item target in ClaimedTargets)
            {
                XElement te = new XElement("ClaimedTarget", new XAttribute("id", target.ID), new XAttribute("branchId", target.Infector.ID));
                saveElement.Add(te);
            }

            element.Add(saveElement);
        }

        public void LoadSave(XElement element)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            Offset = element.GetAttributeVector2("offset", Vector2.Zero);
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "branch":
                        LoadBranch(subElement);
                        break;

                    case "claimedtarget":
                        int id = subElement.GetAttributeInt("id", -1);
                        int branchId = subElement.GetAttributeInt("branchId", -1);
                        if (id > 0)
                        {
                            tempClaimedTargets.Add(Tuple.Create((UInt16)id, branchId));
                        }
                        break;
                }
            }

            void LoadBranch(XElement branchElement)
            {
                Vector2 pos = branchElement.GetAttributeVector2("pos", Vector2.Zero);
                bool isRoot = branchElement.GetAttributeBool("isroot", false);
                int flowerConfig = getInt("flowerconfig");
                int leafconfig = getInt("leafconfig");
                int id = getInt("ID");
                float health = getFloat("health");
                float maxhealth = getFloat("maxhealth");
                int sides = getInt("sides");
                int blockedSides = getInt("blockedsides");
                int claimedId = branchElement.GetAttributeInt("claimed", -1);

                BallastFloraBranch newBranch = new BallastFloraBranch(this, pos, VineTileType.CrossJunction, FoliageConfig.Deserialize(flowerConfig), FoliageConfig.Deserialize(leafconfig))
                {
                    ID = id,
                    Health = health,
                    MaxHealth = maxhealth,
                    Sides = (TileSide) sides,
                    BlockedSides = (TileSide) blockedSides,
                    IsRoot = isRoot
                };

                if (claimedId > -1)
                {
                    newBranch.HasClaimedItem = true;
                    newBranch.ClaimedItem = (ushort) claimedId;
                }

                Branches.Add(newBranch);

                int getInt(string name) => branchElement.GetAttributeInt(name, 0);
                float getFloat(string name) => branchElement.GetAttributeFloat(name, 0f);
            }
        }

        public void Update(float deltaTime)
        {
            foreach (BallastFloraBranch branch in Branches)
            {
                branch.UpdateScale(deltaTime);
                branch.UpdatePulse(deltaTime, PulseInflateSpeed, PulseDeflateSpeed, PulseDelay);
#if CLIENT
                branch.UpdateHealth();
#endif
            }

            if (damageIndicatorTimer <= 0)
            {
                foreach (BallastFloraBranch branch in Branches)
                {
                    if (branch.AccumulatedDamage > 0)
                    {

#if CLIENT
                        CreateDamageParticle(branch, branch.AccumulatedDamage);

                        if (GameMain.DebugDraw)
                        {
                            var pos = (Parent?.Position ?? Vector2.Zero) + Offset + branch.Position;
                            GUI.AddMessage($"{(int)branch.AccumulatedDamage}", GUI.Style.Red, pos, Vector2.UnitY * 10.0f, 3f, playSound: false, subId: Parent?.Submarine?.ID ?? -1);
                        }
#elif SERVER
                        SendNetworkMessage(this, NetworkHeader.BranchDamage, branch, branch.AccumulatedDamage);
#endif
                    }

                    branch.AccumulatedDamage = 0f;
                }

                damageIndicatorTimer = 1f;
            }

            damageIndicatorTimer -= deltaTime;

            UpdatePowerDrain(deltaTime);

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            StateMachine.Update(deltaTime);

            if (HasBrokenThrough)
            {
                // I wasn't 100% sure what the performance impact on this so I decide to limit it to only check every 5 seconds
                if (fireCheckCooldown <= 0)
                {
                    UpdateFireSources();
                    fireCheckCooldown = 5f;
                }
                else
                {
                    fireCheckCooldown -= deltaTime;
                }

                foreach (BallastFloraBranch branch in branchesVulnerableToFire)
                {
                    if (!branch.Removed)
                    {
                        DamageBranch(branch, FireVulnerability * deltaTime, AttackType.Fire, null);
                    }
                }
            }
            
            UpdateSelfDamage(deltaTime);

            if (Anger > 1f)
            {
                Anger -= deltaTime;
            }

            // This entire scope is probably very heavy for GC, need to experiment
            if (toxinsTimer > 0.1f)
            {
                if (!string.IsNullOrWhiteSpace(AttackItemPrefab))
                {
                    Dictionary<Hull, List<BallastFloraBranch>> branches = new Dictionary<Hull, List<BallastFloraBranch>>();
                    foreach (BallastFloraBranch branch in Branches)
                    {
                        if (branch.CurrentHull == null || branch.FlowerConfig.Variant < 0) { continue; }

                        if (branches.TryGetValue(branch.CurrentHull, out List<BallastFloraBranch>? list))
                        {
                            list.Add(branch);
                        }
                        else
                        {
                            branches.Add(branch.CurrentHull, new List<BallastFloraBranch> { branch });
                        }
                    }

                    foreach (Hull hull in branches.Keys)
                    {
                        List<BallastFloraBranch> list = branches[hull];
                        if (!list.Any(HasAcidEmitter))
                        {
                            BallastFloraBranch randomBranh = branches[hull].GetRandom();
                            randomBranh.SpawningItem = true;
                    
                            ItemPrefab prefab = ItemPrefab.Find(null, AttackItemPrefab);
                            Entity.Spawner?.AddToSpawnQueue(prefab, Parent.Position + Offset + randomBranh.Position, Parent.Submarine, null, item =>
                            {
                                randomBranh.AttackItem = item;
                                randomBranh.SpawningItem = false;
                            });
                        }

                        static bool HasAcidEmitter(BallastFloraBranch b) => b.SpawningItem || (b.AttackItem != null && !b.AttackItem.Removed);
                    }
                }

                toxinsTimer -= deltaTime;
            }

            if (defenseCooldown >= 0)
            {
                defenseCooldown -= deltaTime;
            }
            
            if (toxinsCooldown >= 0)
            {
                toxinsCooldown -= deltaTime;
            }
        }

        private void UpdateSelfDamage(float deltaTime)
        {
            if (selfDamageTimer <= 0)
            {
                bool hasRoot = false;
                foreach (BallastFloraBranch branch in Branches)
                {
                    if (branch.IsRoot)
                    {
                        hasRoot = true;
                        break;
                    }
                }

                if (!hasRoot)
                {
                    Kill();
                    return;
                }

                if (!HasBrokenThrough && !CanGrowMore())
                {
                    Branches.ForEachMod(branch =>
                    {
                        float maxHealth = branch.IsRoot ? StemHealth : BranchHealth;
                        DamageBranch(branch, Rand.Range(1f, maxHealth), AttackType.Other);
                    });
                }

                selfDamageTimer = 1f;
            }

            selfDamageTimer -= deltaTime;
        }

        private void UpdatePowerDrain(float deltaTime)
        {
            PowerConsumptionTimer += deltaTime;
            if (PowerConsumptionTimer > PowerConsumptionDuration)
            {
                PowerConsumptionTimer = 0f;
            }

            float powerConsumption = MathHelper.Lerp(PowerConsumptionMax, PowerConsumptionMin, PowerConsumptionTimer / PowerConsumptionDuration);
            float powerDelta = powerConsumption * deltaTime;
            
            foreach (PowerTransfer jb in ClaimedJunctionBoxes)
            {
                if (jb.ExtraLoad > Math.Max(PowerConsumptionMin, PowerConsumptionMax)) { continue; }

                jb.ExtraLoad = powerConsumption;

                float currPowerConsumption = -jb.CurrPowerConsumption;

                if (currPowerConsumption > powerDelta)
                {
                    AvailablePower += powerDelta;
                }
                else
                {
                    AvailablePower += currPowerConsumption * deltaTime;
                }
            }

            float batteryDrain = powerDelta * 0.1f;
            foreach (PowerContainer battery in ClaimedBatteries)
            {
                float amount = Math.Min(battery.MaxOutPut, batteryDrain);

                if (battery.Charge > amount)
                {
                    battery.Charge -= amount;
                    AvailablePower += amount;
                }
            }
        }

        /// <summary>
        /// Update which branches are currently in range of fires
        /// </summary>
        private void UpdateFireSources()
        {
            branchesVulnerableToFire.Clear();
            foreach (BallastFloraBranch branch in Branches)
            {
                if (branch.CurrentHull == null) { continue; }

                foreach (FireSource source in branch.CurrentHull.FireSources)
                {
                    if (source.IsInDamageRange(GetWorldPosition() + branch.Position, source.DamageRange))
                    {
                        branchesVulnerableToFire.Add(branch);
                    }
                }
            }
        }

        private bool IsInWater(BallastFloraBranch branch)
        {
            if (branch.CurrentHull == null) { return false; }

            float surfaceY = branch.CurrentHull.Surface;
            Vector2 pos = Parent.Position + Offset + branch.Position;
            return Parent.WaterVolume > 0.0f && pos.Y < surfaceY;
        }

        // could probably be moved to the branch constructor
        private void SetHull(BallastFloraBranch branch)
        {
            branch.CurrentHull = Hull.FindHull(GetWorldPosition() + branch.Position, Parent, true);
        }

        private void GenerateStem()
        {
            BallastFloraBranch stem = new BallastFloraBranch(this, Vector2.Zero, VineTileType.Stem, FoliageConfig.EmptyConfig, FoliageConfig.EmptyConfig)
            {
                BlockedSides = TileSide.Bottom | TileSide.Left | TileSide.Right,
                GrowthStep = 1f,
                Health = StemHealth,
                MaxHealth = StemHealth,
                IsRoot = true,
                CurrentHull = Parent
            };

            Branches.Add(stem);
            CreateBody(stem);
        }

        public float GetGrowthSpeed(float deltaTime)
        {
            float load = PowerRequirement * Anger * deltaTime;

            if (AvailablePower > load)
            {
                AvailablePower -= load;
                return Anger * 2f * deltaTime;
            }

            return deltaTime;
        }

        public bool TryGrowBranch(BallastFloraBranch parent, TileSide side, out List<BallastFloraBranch> result)
        {
            result = new List<BallastFloraBranch>();
            if (parent.IsSideBlocked(side)) { return false; }

            Vector2 pos = parent.AdjacentPositions[side];
            Rectangle rect = VineTile.CreatePlantRect(pos);

            if (CollidesWithWorld(rect))
            {
                parent.BlockedSides |= side;
                parent.FailedGrowthAttempts++;
                return false;
            }

            FoliageConfig flowerConfig = FoliageConfig.EmptyConfig;
            FoliageConfig leafConfig = FoliageConfig.EmptyConfig;

            if (FlowerProbability > Rand.Range(0d, 1.0d))
            {
                flowerConfig = FoliageConfig.CreateRandomConfig(flowerVariants, 0.5f, 1.0f);
            }

            if (LeafProbability > Rand.Range(0d, 1.0d))
            {
                leafConfig = FoliageConfig.CreateRandomConfig(leafVariants, 0.5f, 1.0f);
            }

            BallastFloraBranch newBranch = new BallastFloraBranch(this, pos, VineTileType.CrossJunction, flowerConfig, leafConfig, rect)
            {
                ID = CreateID(),
                Health = BranchHealth,
                MaxHealth = BranchHealth
            };

            SetHull(newBranch);
            
            if (newBranch.CurrentHull == null || newBranch.CurrentHull.Submarine != Parent.Submarine)
            {
                parent.BlockedSides |= side;
                parent.FailedGrowthAttempts++;
                return false;
            }

            UpdateConnections(newBranch, parent);

            Branches.Add(newBranch);
            result.Add(newBranch);

            OnBranchGrowthSuccess(newBranch);

            if (GrowthWarps > 0)
            {
                GrowthWarps--;
            }

#if SERVER
            SendNetworkMessage(this, NetworkHeader.BranchCreate, newBranch, parent.ID);
#endif
            return true;
        }

        public bool BranchContainsTarget(BallastFloraBranch branch, Item target)
        {
            Rectangle worldRect = branch.Rect;
            worldRect.Location = GetWorldPosition().ToPoint() + worldRect.Location;
            return worldRect.IntersectsWorld(target.WorldRect);
        }

        public void ClaimTarget(Item target, BallastFloraBranch? branch, bool load = false)
        {
            target.Infector = branch;

            if (target.GetComponent<PowerTransfer>() is { } powerTransfer)
            {
                ClaimedJunctionBoxes.Add(powerTransfer);
            }

            if (target.GetComponent<PowerContainer>() is { } powerContainer)
            {
                ClaimedBatteries.Add(powerContainer);
            }

            ClaimedTargets.Add(target);

            if (branch != null)
            {
                branch.ClaimedItem = target.ID;
                branch.HasClaimedItem = true;
            }

#if SERVER
            if (!load)
            {
                SendNetworkMessage(this, NetworkHeader.Infect, target.ID, true, branch);
            }
#endif
        }

        private void UpdateConnections(BallastFloraBranch branch, BallastFloraBranch? parent = null)
        {
            foreach (BallastFloraBranch otherBranch in Branches)
            {
                var (distX, distY) = branch.Position - otherBranch.Position;
                int absDistX = (int) Math.Abs(distX), absDistY = (int) Math.Abs(distY);

                if (absDistX > branch.Rect.Width || absDistY > branch.Rect.Height || absDistX > 0 && absDistY > 0) { continue; }

                TileSide connectingSide = absDistX > absDistY ? distX > 0 ? TileSide.Right : TileSide.Left : distY > 0 ? TileSide.Top : TileSide.Bottom;

                TileSide oppositeSide = connectingSide.GetOppositeSide();

                if (parent != null)
                {
                    if (otherBranch.BlockedSides.IsBitSet(connectingSide))
                    {
                        branch.BlockedSides |= oppositeSide;
                        continue;
                    }

                    if (otherBranch != parent)
                    {
                        otherBranch.BlockedSides |= connectingSide;
                        branch.BlockedSides |= oppositeSide;
                    }
                    else
                    {
                        otherBranch.Sides |= connectingSide;
                        branch.Sides |= oppositeSide;
                    }
                }

                branch.Connections.TryAdd(oppositeSide, otherBranch);
                otherBranch.Connections.TryAdd(connectingSide, branch);
            }
        }

        private void OnBranchGrowthSuccess(BallastFloraBranch newBranch)
        {
            if (!HasBrokenThrough)
            {
                if (Branches.Count > BreakthroughPoint)
                {
                    BreakThrough();
                }

#if CLIENT
                if (newBranch.FlowerConfig.Variant > -1)
                {
                    Vector2 flowerPos = GetWorldPosition() + newBranch.Position;
                    CreateShapnel(flowerPos);
                    newBranch.GrowthStep = 2.0f;
                    SoundPlayer.PlayDamageSound(BurstSound, 1.0f, flowerPos, range: 800);
                }
#endif
            }

            CreateBody(newBranch);

            foreach (BallastFloraBranch vine in Branches)
            {
                vine.UpdateType();
            }
        }

        /// <summary>
        /// Create a body for a branch which works as the hitbox for flamer
        /// </summary>
        /// <param name="branch"></param>
        private void CreateBody(BallastFloraBranch branch)
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            Rectangle rect = branch.Rect;
            Vector2 pos = Parent.Position + Offset + branch.Position;

            float scale = branch.IsRoot ? 3.0f : 1f;
            Body branchBody = GameMain.World.CreateRectangle(ConvertUnits.ToSimUnits(rect.Width * scale), ConvertUnits.ToSimUnits(rect.Height * scale), 1.5f);
            branchBody.BodyType = BodyType.Static;
            branchBody.UserData = branch;
            branchBody.SetCollidesWith(Physics.CollisionRepair);
            branchBody.SetCollisionCategories(Physics.CollisionRepair);
            branchBody.Position = ConvertUnits.ToSimUnits(pos);
            branchBody.Enabled = HasBrokenThrough;

            bodies.Add(branchBody);
        }

        public void DamageBranch(BallastFloraBranch branch, float amount, AttackType type, Character? attacker = null)
        {
            float damage = amount;
            // damage is handled server side currently
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            if (attacker != null && toxinsCooldown <= 0)
            {
                toxinsTimer = 25f;
                toxinsCooldown = 60f;
            }

            if (type == AttackType.Fire)
            {
                if (IsInWater(branch))
                {
                    damage *= 1f - SubmergedWaterResistance;
                }

                if (defenseCooldown <= 0)
                {
                    if (!(StateMachine.State is DefendWithPumpState))
                    {
                        StateMachine.EnterState(new DefendWithPumpState(branch, ClaimedTargets, attacker));
                        defenseCooldown = 180f;
                    }

                    defenseCooldown = 10f;
                }
            }

            branch.AccumulatedDamage += damage;

            branch.Health -= damage;

            if (type != AttackType.Other)
            {
                Anger += damage * 0.001f;
            }

#if SERVER
            GameMain.Server?.KarmaManager?.OnBallastFloraDamaged(attacker, damage);
#endif

            if (branch.Health < 0)
            {
                RemoveBranch(branch);
            }
        }

        public void Remove()
        {
            foreach (Body body in bodies)
            {
                GameMain.World.Remove(body);
            }

            Parent.BallastFlora = null;
            Branches.Clear();

            foreach (Item target in ClaimedTargets)
            {
                target.Infector = null;
            }

            _entityList.Remove(this);
        }

        public void RemoveBranch(BallastFloraBranch branch)
        {
            bool isClient = GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient;

            Anger += 0.01f;

            Branches.Remove(branch);
            branch.Removed = true;

            bodies.ForEachMod(body =>
            {
                if (body.UserData == branch)
                {
                    GameMain.World.Remove(body);
                    bodies.Remove(body);
                    foreach (var (tileSide, otherBranch) in branch.Connections)
                    {
                        TileSide opposite = tileSide.GetOppositeSide();
                        otherBranch.BlockedSides &= ~opposite;
                        otherBranch.Sides &= ~opposite;

                        otherBranch.UpdateType();

                        if (isClient) { continue; }

                        // Remove branches that are not connected to anything anymore
                        if ((otherBranch.Type == VineTileType.Stem || otherBranch.Sides == TileSide.None) && !otherBranch.IsRoot)
                        {
                            RemoveBranch(otherBranch);
                        }
                    }
                }
            });

#if CLIENT
            CreateDeathParticle(branch);
#endif

            if (isClient) { return; }

            if (branch.HasClaimedItem)
            {
                RemoveClaim(branch.ClaimedItem);
            }

            if (branch.IsRoot)
            {
                Kill();
                return;
            }

#if SERVER
            SendNetworkMessage(this, NetworkHeader.BranchRemove, branch);
#endif
        }

        public void RemoveClaim(ushort id)
        {
            ClaimedTargets.ForEachMod(item =>
            {
                if (item.ID == id)
                {
                    if (!IgnoredTargets.ContainsKey(item))
                    {
                        IgnoredTargets.Add(item, 10);
                    }

                    ClaimedTargets.Remove(item);
                    item.Infector = null;

                    ClaimedJunctionBoxes.ForEachMod(jb =>
                    {
                        if (jb.Item == item)
                        {
                            ClaimedJunctionBoxes.Remove(jb);
                        }
                    });

                    ClaimedBatteries.ForEachMod(bat =>
                    {
                        if (bat.Item == item)
                        {
                            ClaimedBatteries.Remove(bat);
                        }
                    });

#if SERVER
                    SendNetworkMessage(this, NetworkHeader.Infect, item.ID, false);
#endif
                }
            });
        }

        public void Kill()
        {
            Branches.ForEachMod(RemoveBranch);
            Parent.BallastFlora = null;

            foreach (Item target in ClaimedTargets)
            {
                target.Infector = null;
            }

            StateMachine?.State?.Exit();

            // clean up leftover (can probably be removed)
            foreach (Body body in bodies)
            {
                Debug.Assert(false, "Leftover bodies found after the ballast flora has died.");
                GameMain.World.Remove(body);
            }

#if SERVER
            SendNetworkMessage(this, NetworkHeader.Kill);
#endif
        }

        private void BreakThrough()
        {
            HasBrokenThrough = true;

            foreach (Body body in bodies)
            {
                body.Enabled = true;
            }

#if CLIENT
            foreach (BallastFloraBranch branch in Branches)
            {
                CreateShapnel(GetWorldPosition() + branch.Position);
            }

            SoundPlayer.PlayDamageSound(BurstSound, BreakthroughPoint, GetWorldPosition(), range: 800);
#endif
        }

        private bool CanGrowMore() => Branches.Any(b => b.CanGrowMore());

        private bool CollidesWithWorld(Rectangle rect)
        {
            if (Branches.Any(g => g.Rect.Contains(rect))) { return true; }

            Rectangle worldRect = rect;
            worldRect.Location = (Parent.Position + Offset).ToPoint() + worldRect.Location;
            worldRect.Y -= worldRect.Height;

            Vector2 topLeft = ConvertUnits.ToSimUnits(new Vector2(worldRect.Left, worldRect.Top)),
                    topRight = ConvertUnits.ToSimUnits(new Vector2(worldRect.Right, worldRect.Top)),
                    bottomLeft = ConvertUnits.ToSimUnits(new Vector2(worldRect.Left, worldRect.Bottom)),
                    bottomRight = ConvertUnits.ToSimUnits(new Vector2(worldRect.Right, worldRect.Bottom));

            bool hasCollision = LineCollides(topLeft, topRight) || LineCollides(topRight, bottomRight) || LineCollides(bottomRight, bottomLeft) || LineCollides(bottomLeft, topLeft);

            return hasCollision;
        }

        private static bool LineCollides(Vector2 point1, Vector2 point2)
        {
            const Category category = Physics.CollisionWall | Physics.CollisionCharacter | Physics.CollisionItem | Physics.CollisionLevel;
            return Submarine.PickBody(point1, point2, collisionCategory: category, customPredicate: CustomPredicate) != null;

            static bool CustomPredicate(Fixture f)
            {
                bool hasCollision = f.CollidesWith.HasFlag(Physics.CollisionItem);
                Body body = f.Body;

                if (body.UserData == null) { return false; }

                switch (body.UserData)
                {
                    case Submarine _:
                    case Structure _:
                        return hasCollision;
                    default:
                        return false;
                }
            }
        }
    }
}