#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;

namespace Barotrauma.MapCreatures.Behavior
{
    class BallastFloraBranch : VineTile
    {
        public readonly BallastFloraBehavior? ParentBallastFlora;
        public int ID = -1;

        public Item? ClaimedItem;
        public int ClaimedItemId = -1;

        public float MaxHealth = 100f;

        private float health = 100;
        public float Health
        {
            get { return health; }
            set { health = MathHelper.Clamp(value, 0.0f, MaxHealth); }
        }

        public float RemoveTimer = 60.0f;
        
        public bool SpawningItem;
        public Item? AttackItem;

        public bool IsRoot;
        /// <summary>
        /// Decorative branches that grow around the root
        /// </summary>
        public bool IsRootGrowth;
        public bool Removed;

        public bool DisconnectedFromRoot;

        public Hull? CurrentHull;

        public float Pulse = 1.0f;
        private bool inflate;
        private float pulseDelay = Rand.Range(0f, 3f);

        private BallastFloraBranch? parentBranch;
        public BallastFloraBranch? ParentBranch
        {
            get { return parentBranch; }
            set
            {
                if (value != parentBranch)
                {
                    parentBranch = value;
                    if (parentBranch != null)
                    {
                        BranchDepth = parentBranch.BranchDepth + 1;
                    }
                }
            }
        }
        /// <summary>
        /// How far from the root this branch is
        /// </summary>
        public int BranchDepth { get; private set; }

        public float AccumulatedDamage;
        public float DamageVisualizationTimer;
#if CLIENT
        public Vector2 ShakeAmount;
#endif

        // Adjacent tiles, used to free up sides when this branch gets removed
        public readonly Dictionary<TileSide, BallastFloraBranch> Connections = new Dictionary<TileSide, BallastFloraBranch>();

        public BallastFloraBranch(BallastFloraBehavior? parent, BallastFloraBranch? parentBranch, Vector2 position, VineTileType type, FoliageConfig? flowerConfig = null, FoliageConfig? leafConfig = null, Rectangle? rect = null)
            : base(null, position, type, flowerConfig, leafConfig, rect)
        {
            ParentBranch = parentBranch;
            ParentBallastFlora = parent;
        }

        public void UpdateHealth()
        {
            if (MaxHealth <= Health) { return; }
            Color healthColor = Color.White * (1.0f - Health / MaxHealth);
            HealthColor = Color.Lerp(HealthColor, healthColor, 0.05f);
        }

        public void UpdatePulse(float deltaTime, float inflateSpeed, float deflateSpeed, float delay)
        {
            if (ParentBallastFlora == null || DisconnectedFromRoot) { return; }

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

        private readonly static List<BallastFloraBehavior> _entityList = new List<BallastFloraBehavior>();
        public static IEnumerable<BallastFloraBehavior> EntityList => _entityList;

        public enum NetworkHeader
        {
            Spawn,
            Kill,
            BranchCreate,
            BranchRemove,
            BranchDamage,
            Infect,
            Remove
        }

        public enum AttackType
        {
            Fire,
            Explosives,
            Other,
            CutFromRoot
        }

        public struct AITarget
        {
            public Identifier[] Tags;
            public int Priority;

            public AITarget(ContentXElement element)
            {
                Tags = element.GetAttributeIdentifierArray("tags", Array.Empty<Identifier>())!;
                Priority = element.GetAttributeInt("priority", 0);
            }

            public bool Matches(Item item)
            {        
                foreach (Identifier targetTag in Tags)
                {
                    if (item.HasTag(targetTag)) { return true; }
                }
                return false;
            }
        }

        [Serialize(0.25f, IsPropertySaveable.Yes, "Scale of the branches.")]
        public float BaseBranchScale { get; set; }

        [Serialize(0.25f, IsPropertySaveable.Yes, "Scale of the flowers.")]
        public float BaseFlowerScale { get; set; }

        [Serialize(0.5f, IsPropertySaveable.Yes, "Scale of the leaves.")]
        public float BaseLeafScale { get; set; }

        [Serialize(0.33f, IsPropertySaveable.Yes, "Chance for a flower to appear on a branch.")]
        public float FlowerProbability { get; set; }

        [Serialize(0.7f, IsPropertySaveable.Yes, "Chance for leaves to appear on a branch.")]
        public float LeafProbability { get; set; }

        [Serialize(3f, IsPropertySaveable.Yes, "Delay between pulses.")]
        public float PulseDelay { get; set; }

        [Serialize(3f, IsPropertySaveable.Yes, "How fast the flower inflates during a pulse.")]
        public float PulseInflateSpeed { get; set; }

        [Serialize(1f, IsPropertySaveable.Yes, "How fast the flower deflates.")]
        public float PulseDeflateSpeed { get; set; }

        [Serialize(32, IsPropertySaveable.Yes, "How many vines must grow before the plant breaks through the wall.")]
        public int BreakthroughPoint { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, "Has the plant grown large enough to expose itself.")]
        public bool HasBrokenThrough { get; set; }

        [Serialize(300, IsPropertySaveable.Yes, "How far the ballast flora can detect items from.")]
        public int Sight { get; set; }

        [Serialize(100, IsPropertySaveable.Yes, "How much health the branches have.")]
        public int BranchHealth { get; set; }

        [Serialize(400, IsPropertySaveable.Yes, "How much health the root has.")]
        public int RootHealth { get; set; }

        [Serialize(0.00025f, IsPropertySaveable.Yes, "How fast the root's health regenerates per each grown branch.")]
        public float HealthRegenPerBranch { get; set; }

        [Serialize(30, IsPropertySaveable.Yes, "How far away from the root branches can regenerate health (in number of branches). The amount of regen decreases lineary further from the root.")]
        public int MaxBranchHealthRegenDistance { get; set; }

        [Serialize("255,255,255,255", IsPropertySaveable.Yes)]
        public Color RootColor { get; set; }

        [Serialize(300f, IsPropertySaveable.Yes, "How much power the ballast flora takes from junction boxes.")]
        public float PowerConsumptionMin { get; set; }

        [Serialize(3000f, IsPropertySaveable.Yes, "How much the power drain spikes.")]
        public float PowerConsumptionMax { get; set; }

        [Serialize(10f, IsPropertySaveable.Yes, "How long it takes for power drain to wind down.")]
        public float PowerConsumptionDuration { get; set; }

        [Serialize(250f, IsPropertySaveable.Yes, "How much power does it take to accelerate growth.")]
        public float PowerRequirement { get; set; }

        [Serialize(5f, IsPropertySaveable.Yes, "Maximum anger, anger increases when the plant gets damaged and increases growth speed.")]
        public float MaxAnger { get; set; }

        [Serialize(10000f, IsPropertySaveable.Yes, "Maximum power buffer.")]
        public float MaxPowerCapacity { get; set; }

        [Serialize("", IsPropertySaveable.Yes, "Item prefab that is spawned when threatened.")]
        public Identifier AttackItemPrefab { get; set; } = Identifier.Empty;

        [Serialize(0.8f, IsPropertySaveable.Yes, "How resistant the ballast flora is to explosives before it blooms.")]
        public float ExplosionResistance { get; set; }

        [Serialize(5f, IsPropertySaveable.Yes, "How much damage is taken from open fires.")]
        public float FireVulnerability { get; set; }

        [Serialize(0.5f, IsPropertySaveable.Yes, "How much resistance against fire is gained while submerged.")]
        public float SubmergedWaterResistance { get; set; }

        [Serialize(0.8f, IsPropertySaveable.Yes, "What depth the branches will be drawn on.")]
        public float BranchDepth { get; set; }
        
        [Serialize("", IsPropertySaveable.Yes, "What sound to play when the ballast flora bursts through walls.")]
        public string BurstSound { get; set; } = "";

        private float availablePower;

        [Serialize(0f, IsPropertySaveable.Yes, "How much power the ballast flora has stored.")]
        public float AvailablePower
        {
            get => availablePower;
            set => availablePower = Math.Max(value, MaxPowerCapacity);
        }

        private float anger;

        [Serialize(1f, IsPropertySaveable.Yes, "How enraged the flora is, affects how fast it grows.")]
        public float Anger
        {
            get => anger;
            set => anger = Math.Clamp(value, 1f, MaxAnger);
        }

        public string Name { get; } = "";

        public Hull Parent { get; private set; }

        public BallastFloraPrefab Prefab { get; private set; }

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; private set; }

        public Vector2 Offset;

        public readonly HashSet<Item> ClaimedTargets = new HashSet<Item>();
        public readonly HashSet<PowerTransfer> ClaimedJunctionBoxes = new HashSet<PowerTransfer>();
        public readonly HashSet<PowerContainer> ClaimedBatteries = new HashSet<PowerContainer>();
        public readonly Dictionary<Item, int> IgnoredTargets = new Dictionary<Item, int>();

        private readonly List<Tuple<UInt16, int>> tempClaimedTargets = new List<Tuple<ushort, int>>();

        private int flowerVariants, leafVariants;
        public readonly List<AITarget> Targets = new List<AITarget>();

        public float PowerConsumptionTimer;

        private float defenseCooldown, toxinsCooldown, fireCheckCooldown;
        private float selfDamageTimer, toxinsTimer, toxinsSpawnTimer;

        private readonly List<BallastFloraBranch> branchesVulnerableToFire = new List<BallastFloraBranch>();

        public readonly List<BallastFloraBranch> Branches = new List<BallastFloraBranch>();
        private BallastFloraBranch? root;
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
                else
                {
                    string errorMsg = $"Error in BallastFloraBehavior.OnMapLoaded: could not find the item claimed by the ballast flora.";
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("BallastFloraBehavior.OnMapLoaded:ClaimedItemNotFound", GameAnalyticsManager.ErrorSeverity.Warning, errorMsg);
                }
            }

            foreach (BallastFloraBranch branch in Branches)
            {
                SetHull(branch);
                if (branch.ClaimedItemId > -1)
                {
                    if (Entity.FindEntityByID((ushort)branch.ClaimedItemId) is Item item)
                    {
                        branch.ClaimedItem = item;
                    }
                    else
                    {
                        string errorMsg = $"Error in BallastFloraBehavior.OnMapLoaded: could not find the item claimed by a branch.";
                        DebugConsole.ThrowError(errorMsg);
                        GameAnalyticsManager.AddErrorEventOnce("BallastFloraBehavior.OnMapLoaded:BranchClaimedItemNotFound", GameAnalyticsManager.ErrorSeverity.Warning, errorMsg);
                    }
                }
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
            if (firstGrowth) { GenerateRoot(); }
            _entityList.Add(this);
        }

        partial void LoadPrefab(ContentXElement element);

        public void LoadTargets(ContentXElement element)
        {
            foreach (var subElement in element.Elements())
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
                    new XAttribute("isrootgrowth", branch.IsRootGrowth),
                    new XAttribute("health", branch.Health.ToString("G", CultureInfo.InvariantCulture)),
                    new XAttribute("maxhealth", branch.MaxHealth.ToString("G", CultureInfo.InvariantCulture)),
                    new XAttribute("sides", (int)branch.Sides),
                    new XAttribute("blockedsides", (int)branch.BlockedSides));

                if (branch.ClaimedItem != null)
                {
                    be.Add(new XAttribute("claimed", (int)(branch.ClaimedItem?.ID ?? -1)));
                }
                if (branch.ParentBranch != null)
                {
                    be.Add(new XAttribute("parentbranch", (int)(branch.ParentBranch?.ID ?? -1)));
                }

                saveElement.Add(be);
            }

            foreach (Item target in ClaimedTargets)
            {
                if (target.Infector == null)
                {
                    string errorMsg = $"Error in BallastFloraBehavior.Save: claimed target \"{target.Prefab.Identifier}\" had no infector set.";
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("BallastFloraBehavior.Save:InfectorNull", GameAnalyticsManager.ErrorSeverity.Warning, errorMsg);
                    continue;
                }
                XElement te = new XElement("ClaimedTarget", new XAttribute("id", target.ID), new XAttribute("branchId", target.Infector.ID));
                saveElement.Add(te);
            }

            element.Add(saveElement);
        }

        public void LoadSave(XElement element, IdRemap idRemap)
        {
            List<(BallastFloraBranch branch, int parentBranchId)> branches = new List<(BallastFloraBranch branch, int parentBranchId)>();
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            Offset = element.GetAttributeVector2("offset", Vector2.Zero);
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "branch":
                        LoadBranch(subElement, idRemap);
                        break;
                    case "claimedtarget":
                        int id = subElement.GetAttributeInt("id", -1);
                        int branchId = subElement.GetAttributeInt("branchId", -1);
                        if (id > 0)
                        {
                            tempClaimedTargets.Add(Tuple.Create(idRemap.GetOffsetId(id), branchId));
                        }
                        break;
                }
            }

            foreach ((BallastFloraBranch branch, int parentBranchId) in branches)
            {
                if (parentBranchId > -1)
                {
                    var parentBranch = Branches.Find(b => b.ID == parentBranchId);
                    if (parentBranch == null)
                    {
                        DebugConsole.AddWarning($"Error while loading ballast flora: couldn't find a parent branch with the ID {parentBranchId}");
                    }
                    else
                    {
                        branch.ParentBranch = parentBranch;
                    }
                }
            }

            if (root == null)
            {
                Branches.ForEach(b => b.DisconnectedFromRoot = true);
            }
            else
            {
                CheckDisconnectedFromRoot();
            }

            void LoadBranch(XElement branchElement, IdRemap idRemap)
            {
                Vector2 pos = branchElement.GetAttributeVector2("pos", Vector2.Zero);
                bool isRoot = branchElement.GetAttributeBool("isroot", false);
                bool isRootGrowth = branchElement.GetAttributeBool("isrootgrowth", false);
                int flowerConfig = getInt("flowerconfig");
                int leafconfig = getInt("leafconfig");
                int id = getInt("ID");
                float health = getFloat("health");
                float maxhealth = getFloat("maxhealth");
                int sides = getInt("sides");
                int blockedSides = getInt("blockedsides");
                int claimedId = branchElement.GetAttributeInt("claimed", -1);
                int parentBranchId = branchElement.GetAttributeInt("parentbranch", -1);

                BallastFloraBranch newBranch = new BallastFloraBranch(this, null, pos, VineTileType.CrossJunction, FoliageConfig.Deserialize(flowerConfig), FoliageConfig.Deserialize(leafconfig))
                {
                    ID = id,
                    Health = health,
                    MaxHealth = maxhealth,
                    Sides = (TileSide) sides,
                    BlockedSides = (TileSide) blockedSides,
                    IsRoot = isRoot,
                    IsRootGrowth = isRootGrowth
                };
                branches.Add((newBranch, parentBranchId));

                if (newBranch.IsRoot) { root = newBranch; }

                if (claimedId > -1)
                {
                    newBranch.ClaimedItemId = idRemap.GetOffsetId((ushort)claimedId);
                }

                Branches.Add(newBranch);

                int getInt(string name) => branchElement.GetAttributeInt(name, 0);
                float getFloat(string name) => branchElement.GetAttributeFloat(name, 0f);
            }
        }

        public void Update(float deltaTime)
        {
            if (GameMain.NetworkMember == null || !GameMain.NetworkMember.IsClient) 
            {
                if (Branches.Count == 0)
                {
                    Remove();
                    return;
                }                
            }

            foreach (BallastFloraBranch branch in Branches)
            {
                branch.UpdateScale(deltaTime);
                branch.UpdatePulse(deltaTime, PulseInflateSpeed, PulseDeflateSpeed, PulseDelay);
#if CLIENT
                branch.UpdateHealth();
#endif
            }

            UpdateDamage(deltaTime);

            UpdatePowerDrain(deltaTime);

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            if (root != null && HealthRegenPerBranch > 0.0f)
            {
                float healAmount = Branches.Count(b => !b.IsRoot && !b.IsRootGrowth && !b.DisconnectedFromRoot) * HealthRegenPerBranch;

                foreach (BallastFloraBranch branch in Branches)
                {
                    if (branch.Health > branch.MaxHealth * 0.9f || branch.DisconnectedFromRoot) { continue; }
                    float branchHealAmount = (float)(MaxBranchHealthRegenDistance - branch.BranchDepth) / MaxBranchHealthRegenDistance * healAmount;
                    if (branchHealAmount <= 0.0f) { continue; }
                    float prevHealth = branch.Health;
                    branch.Health += branchHealAmount;
                    branch.AccumulatedDamage += (prevHealth - branch.Health);
                }
            }
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

            if (toxinsTimer > 0.1f)
            {
                toxinsSpawnTimer -= deltaTime;
                if (!AttackItemPrefab.IsEmpty && toxinsSpawnTimer <= 0.0f)
                {
                    toxinsSpawnTimer = 1.0f;
                    Dictionary<Hull, List<BallastFloraBranch>> branches = new Dictionary<Hull, List<BallastFloraBranch>>();
                    foreach (BallastFloraBranch branch in Branches)
                    {
                        if (branch.CurrentHull == null || branch.FlowerConfig.Variant < 0 || branch.DisconnectedFromRoot) { continue; }

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
                            BallastFloraBranch randomBranch = branches[hull].GetRandomUnsynced();
                            randomBranch.SpawningItem = true;
                    
                            ItemPrefab prefab = ItemPrefab.Find(null, AttackItemPrefab);
#warning TODO: Parent needs a nullability sanity check
                            Entity.Spawner?.AddItemToSpawnQueue(prefab, Parent!.Position + Offset + randomBranch.Position, Parent.Submarine, onSpawned: item =>
                            {
                                randomBranch.AttackItem = item;
                                randomBranch.SpawningItem = false;
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

        partial void UpdateDamage(float deltaTime);

        private readonly List<BallastFloraBranch> toBeRemoved = new List<BallastFloraBranch>();
        private void UpdateSelfDamage(float deltaTime)
        {
            if (selfDamageTimer <= 0)
            {
                if (!HasBrokenThrough && !CanGrowMore())
                {
                    Branches.ForEachMod(branch =>
                    {
                        float maxHealth = branch.IsRoot ? RootHealth : BranchHealth;
                        DamageBranch(branch, Rand.Range(1f, maxHealth), AttackType.Other);
                    });
                }
                selfDamageTimer = 1f;
            }
            toBeRemoved.Clear();
            foreach (BallastFloraBranch branch in Branches)
            {
                if (!branch.IsRoot)
                {
                    if (branch.ParentBranch == null || branch.ParentBranch.DisconnectedFromRoot || branch.ParentBranch.Health <= 0.0f)
                    {
                        float parentHealth = branch.ParentBranch == null ? 0.0f : branch.ParentBranch.Health / branch.ParentBranch.MaxHealth;
                        float speed = MathHelper.Lerp(5.0f, 0.1f, parentHealth);
                        DamageBranch(branch, speed * speed * deltaTime, AttackType.CutFromRoot);
                    }
                }
                if (branch.Health <= 0.0f)
                {
                    if (branch.ClaimedItem != null)
                    {
                        RemoveClaim(branch.ClaimedItem);
                        branch.ClaimedItem = null;
                    }

                    branch.RemoveTimer -= deltaTime;
                    if (branch.RemoveTimer <= 0.0f)
                    {
                        toBeRemoved.Add(branch);
                    }
                }
            }
            foreach (BallastFloraBranch branch in toBeRemoved)
            {
                RemoveBranch(branch);
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
        public void SetHull(BallastFloraBranch branch)
        {
            branch.CurrentHull = Hull.FindHull(GetWorldPosition() + branch.Position, Parent, true);
        }

        private void GenerateRoot()
        {
            if (root != null)
            {
                DebugConsole.ThrowError("Error in ballast flora: tried to grow a root even though root has already been created.\n" + Environment.StackTrace);
            }

            root = new BallastFloraBranch(this, null, Vector2.Zero, VineTileType.Stem, FoliageConfig.EmptyConfig, FoliageConfig.EmptyConfig)
            {
                BlockedSides = TileSide.Bottom | TileSide.Left | TileSide.Right,
                GrowthStep = 1f,
                MaxHealth = RootHealth,
                Health = RootHealth,
                IsRoot = true,
                CurrentHull = Parent,
                ID = CreateID()
            };
            
            Branches.Add(root);
            CreateBody(root);
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

        public bool TryGrowBranch(BallastFloraBranch parent, TileSide side, out List<BallastFloraBranch> result, bool isRootGrowth = false, Vector2? forcePosition = null)
        {
            result = new List<BallastFloraBranch>();
            if (!isRootGrowth && parent.IsSideBlocked(side)) { return false; }

            Vector2 pos = forcePosition ?? parent.AdjacentPositions[side];
            Rectangle rect = VineTile.CreatePlantRect(pos);

            if (CollidesWithWorld(rect, checkOtherBranches: !isRootGrowth))
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

            BallastFloraBranch newBranch = new BallastFloraBranch(this, parent, pos, VineTileType.CrossJunction, flowerConfig, leafConfig, rect)
            {
                ID = CreateID(),
                MaxHealth = BranchHealth,
                Health = BranchHealth,
                IsRootGrowth = isRootGrowth
            };

            SetHull(newBranch);
            
            if (newBranch.CurrentHull == null || newBranch.CurrentHull.Submarine != Parent.Submarine)
            {
                if (!isRootGrowth) { parent.BlockedSides |= side; }
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

            int rootGrowthCount = Branches.Count(b => b.IsRootGrowth);
            if (rootGrowthCount < GetDesiredRootGrowthAmount())
            {
                if (root != null)
                {
                    Vector2 rootGrowthPos = Rand.Vector(Math.Max(rootGrowthCount, 1) * Rand.Range(3.0f, 5.0f));
                    TryGrowBranch(root, TileSide.None, out List<BallastFloraBranch> newRootGrowth, isRootGrowth: true, forcePosition: rootGrowthPos);
                }
            }

#if SERVER
            CreateNetworkMessage(new BranchCreateEventData(newBranch, parent));
#endif
            return true;
        }

        private int GetDesiredRootGrowthAmount()
        {
            if (root == null) { return 0; }
            return MathHelper.Clamp(Branches.Count(b => !b.IsRootGrowth && b.Health > 0) / 20, 3, 30);
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
                branch.ClaimedItem = target;
            }

#if SERVER
            if (!load)
            {
                CreateNetworkMessage(new InfectEventData(target, InfectEventData.InfectState.Yes, branch));
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
                    if (otherBranch.BlockedSides.HasFlag(connectingSide))
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

            if (type != AttackType.Other && type != AttackType.CutFromRoot)
            {
                branch.DamageVisualizationTimer = 1.0f;
            }

            if (branch.IsRootGrowth && root != null && root.Health > 0.0f) { return; }

            if (type != AttackType.Other && type != AttackType.CutFromRoot)
            {
                branch.AccumulatedDamage += damage;
                Anger += damage * 0.001f;
            }

            if (GameMain.NetworkMember != null)
            { 
                // damage is handled server side
                if (GameMain.NetworkMember.IsClient)
                {
                    return;
                }
                else
                {
                    //accumulate damage on the server's side to ensure clients get notified 
                    if (type == AttackType.Other || type == AttackType.CutFromRoot)
                    {
                        branch.AccumulatedDamage += damage;
                    }
                }
            }

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
                    else
                    {
                        defenseCooldown = 10f;
                    }
                }
            }

            if (damage > 0)
            {
                damage = Math.Min(damage, branch.Health);
            }
            else
            {
                damage = Math.Max(damage, branch.Health - branch.MaxHealth);
            }
            branch.Health -= damage;

#if SERVER
            GameMain.Server?.KarmaManager?.OnBallastFloraDamaged(attacker, damage);
#endif

            if (branch.Health <= 0 && type != AttackType.CutFromRoot)
            {
                RemoveBranch(branch);
                if (branch.IsRoot) { Kill(); }
            }
        }

        private void CheckDisconnectedFromRoot()
        {
            bool foundDisconnected;
            do
            {
                foundDisconnected = false;
                foreach (BallastFloraBranch branch in Branches)
                {
                    if (branch.ParentBranch == null || branch.DisconnectedFromRoot) { continue; }
                    if (branch.ParentBranch.Removed || branch.ParentBranch.DisconnectedFromRoot)
                    {
                        branch.DisconnectedFromRoot = true;
                        foundDisconnected = true;
                    }
                }
            } while (foundDisconnected);

        }

        public void RemoveBranch(BallastFloraBranch branch)
        {
            bool isClient = GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient;

            Anger += 0.01f;

            bool wasRemoved = branch.Removed;
            Branches.Remove(branch);
            branch.Removed = true;

            CheckDisconnectedFromRoot();

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
            CreateDeathParticle(branch, 1.0f);
#endif

            if (isClient) { return; }

            int rootGrowthCount = Branches.Count(b => b.IsRootGrowth);
            if (rootGrowthCount > GetDesiredRootGrowthAmount())
            {
                var rootGrowth = Branches.LastOrDefault(b => b.IsRootGrowth);
                if (rootGrowth != null)
                {
                    RemoveBranch(rootGrowth);
                }
            }

            if (branch.ClaimedItem != null)
            {
                RemoveClaim(branch.ClaimedItem);
            }

            if (branch.IsRoot)
            {
                Kill();
                return;
            }
#if SERVER
            if (!wasRemoved && Parent != null && !Parent.Removed)
            {
                CreateNetworkMessage(new BranchRemoveEventData(branch));
            }
#endif
        }

        public void RemoveClaim(Item item)
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
            if (!item.Removed && Parent != null && !Parent.Removed)
            {
                CreateNetworkMessage(new InfectEventData(item, InfectEventData.InfectState.No, null));
            }
#endif
        }

        public void Kill()
        {
            foreach (var branch in Branches)
            {
                branch.DisconnectedFromRoot = true;
            }

            foreach (Item target in ClaimedTargets)
            {
                target.Infector = null;
            }

            StateMachine?.State?.Exit();
#if SERVER
            if (Parent != null && !Parent.Removed)
            {
                CreateNetworkMessage(new KillEventData());
            }
#endif
        }

        public void Remove()
        {
            Kill();

            Branches.ForEachMod(RemoveBranch);
            Branches.Clear();
            toBeRemoved.Clear();
            Parent.BallastFlora = null;

            // clean up leftover (can probably be removed)
            foreach (Body body in bodies)
            {
                Debug.Assert(false, "Leftover bodies found after the ballast flora has died.");
                GameMain.World.Remove(body);
            }

            _entityList.Remove(this);
#if SERVER            
            if (Parent != null && !Parent.Removed)
            {
                CreateNetworkMessage(new RemoveEventData());
            }
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

        private bool CollidesWithWorld(Rectangle rect, bool checkOtherBranches = true)
        {
            if (checkOtherBranches && Branches.Any(g => g.Rect.Contains(rect))) { return true; }

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