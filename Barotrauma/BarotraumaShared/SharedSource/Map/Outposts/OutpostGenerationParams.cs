using Barotrauma.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    public class OutpostGenerationParams : PrefabWithUintIdentifier, ISerializableEntity
    {
        public readonly static PrefabCollection<OutpostGenerationParams> OutpostParams = new PrefabCollection<OutpostGenerationParams>();
        
        public virtual string Name { get; private set; }
        
        private readonly HashSet<Identifier> allowedLocationTypes = new HashSet<Identifier>();

        /// <summary>
        /// Identifiers of the location types this outpost can appear in. If empty, can appear in all types of locations.
        /// </summary>
        public IEnumerable<Identifier> AllowedLocationTypes 
        { 
            get { return allowedLocationTypes; } 
        }

        private readonly HashSet<Identifier> allowedGameModeIdentifiers = new HashSet<Identifier>();

        public IEnumerable<Identifier> AllowedGameModeIdentifiers
        {
            get { return allowedGameModeIdentifiers; }
        }

        [Serialize(-1, IsPropertySaveable.Yes, description: "Should this type of outpost be forced to the locations at the end of the campaign map? 0 = first end level, 1 = second end level, and so on."), Editable(MinValueInt = -1, MaxValueInt = 10)]
        public int ForceToEndLocationIndex
        {
            get;
            set;
        }

        [Serialize(-1, IsPropertySaveable.Yes, description: "The closer to the current level difficulty this value is, the higher the probability of choosing these generation params are. Defaults to -1, which means we use the current difficulty."), Editable(MinValueInt = 1, MaxValueInt = 50)]
        public int PreferredDifficulty
        {
            get;
            set;
        }

        [Serialize(10, IsPropertySaveable.Yes, description: "Total number of modules in the outpost."), Editable(MinValueInt = 1, MaxValueInt = 50)]
        public int TotalModuleCount
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.Yes, description: "Should the generator append generic (module flag \"none\") modules to the outpost to reach the total module count."), Editable]
        public bool AppendToReachTotalModuleCount
        {
            get;
            set;
        }

        [Serialize(200.0f, IsPropertySaveable.Yes, description: "Minimum length of the hallways between modules. If 0, the generator will place the modules directly against each other assuming it can be done without making any modules overlap."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1000.0f)]
        public float MinHallwayLength
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.Yes, description: "Should hallways of the minimum hallway length be always generated between modules, even if they could be placed directly against each other with no overlaps?"), Editable]
        public bool AlwaysGenerateHallways
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should this outpost always be destructible, regardless if damaging outposts is allowed by the server?"), Editable]
        public bool AlwaysDestructible
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should all structures have their ai target forcefully turned on?"), Editable]
        public bool EnableAITargetForAllStructures
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should this outpost always be rewireable, regardless if rewiring is allowed by the server?"), Editable]
        public bool AlwaysRewireable
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should stealing from this outpost be always allowed?"), Editable]
        public bool AllowStealing
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.Yes, description: "Should the crew spawn inside the outpost (if not, they'll spawn in the submarine)."), Editable]
        public bool SpawnCrewInsideOutpost
        {
            get;
            set;
        }
        
        [Serialize(true, IsPropertySaveable.Yes, description: "Should doors at the edges of an outpost module that didn't get connected to another module be locked?"), Editable]
        public bool LockUnusedDoors
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.Yes, description: "Should gaps at the edges of an outpost module that didn't get connected to another module be removed?"), Editable]
        public bool RemoveUnusedGaps
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.Yes, description: "Should the whole outpost render behind submarines? Only set this to true if the submarine is intended to go inside the outpost."), Editable]
        public bool DrawBehindSubs
        {
            get;
            set;
        }

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "Minimum amount of water in the hulls of the outpost."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f)]
        public float MinWaterPercentage
        {
            get;
            set;
        }

        [Serialize(0.0f, IsPropertySaveable.Yes, description: "Maximum amount of water in the hulls of the outpost."), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f)]
        public float MaxWaterPercentage
        {
            get;
            set;
        }

        public LevelData.LevelType? LevelType
        {
            get;
            set;
        }

        [Serialize("", IsPropertySaveable.Yes, description: "Identifier of the outpost generation parameters that should be used if this outpost has become critically irradiated."), Editable]
        public string ReplaceInRadiation { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: "By default, sonar only shows the outline of the sub/outpost from the outside. Enable this if you want to see each structure individually."), Editable]
        public bool AlwaysShowStructuresOnSonar
        {
            get;
            set;
        }

        public ContentPath OutpostFilePath { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "If set, a fully pre-built outpost with this tag will be used instead of generating the outpost."), Editable]
        public Identifier OutpostTag { get; set; }

        [Serialize("", IsPropertySaveable.Yes, description: "If set, an outpost with this tag will be used as the entrance to the outpost (i.e. the outpost that you dock with in the normal level to enter the outpost level)."), Editable]
        public Identifier EntranceTag { get; set; }

        [Serialize(false, IsPropertySaveable.Yes, description: $"If enabled, the full outpost is generated in the levels between locations, instead of just the entrance (see {nameof(EntranceTag)})."), Editable]
        public bool GenerateFullOutpostInsteadOfEntrance { get; set; }

        [Serialize(true, IsPropertySaveable.Yes, description: "Should NPCs be spawned in the outpost? Requires NPCs to be defined in the outpost generation parameters.")]
        public bool SpawnNPCs
        {
            get;
            set;
        }

        /// <summary>
        /// Events that always automatically trigger in these types of outposts.
        /// </summary>
        public ImmutableHashSet<Identifier> TriggerEvents { get; set; } = ImmutableHashSet<Identifier>.Empty;

        public class ModuleCount : ISerializableEntity
        {
            public Identifier Identifier;

            [Serialize(0, IsPropertySaveable.Yes), Editable]
            public int Count { get; set; }

            [Serialize(0, IsPropertySaveable.Yes, description: "Can be used to enforce the modules to be placed in a specific order, starting from the docking module (0 = first, 1 = second, etc)."), Editable]
            public int Order { get; set; }

            [Serialize(0.0f, IsPropertySaveable.Yes, description: "Minimum difficulty of the current level for the module to appear in the outpost."), Editable]
            public float MinDifficulty { get; set; }

            [Serialize(100.0f, IsPropertySaveable.Yes, description: "Maximum difficulty of the current level for the module to appear in the outpost."), Editable]
            public float MaxDifficulty { get; set; }

            [Serialize(1.0f, IsPropertySaveable.Yes, description: "Probability for this type of module to be included in the outpost."), Editable(MinValueFloat = 0, MaxValueFloat = 1)]
            public float Probability { get; set; }

            [Serialize(OutpostModuleInfo.GapPosition.None, IsPropertySaveable.Yes, description: "Which side of the outpost should the module be attached to? Can be used to ensure that some module (e.g. a module with a turret) is spawned at the very edge of the outpost.")]
            public OutpostModuleInfo.GapPosition OutpostSideToAttachTo { get; set; }

            [Serialize(false, IsPropertySaveable.Yes, description:
                "When enabled, the module must be placed so there's no other module right below it." +
                " Useful to ensure turrets don't get placed somewhere high up in the outpost, where their coverage would get blocked by some module below them.")]
            public bool RequireDownwardsVisibility { get; set; }

            [Serialize("", IsPropertySaveable.Yes), Editable]
            public Identifier RequiredFaction { get; set; }

            public string Name => Identifier.Value;

            public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; set; }

            public ModuleCount(ContentXElement element)
            {
                Identifier = element.GetAttributeIdentifier("flag", element.GetAttributeIdentifier("moduletype", ""));
                SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            }

            public ModuleCount(Identifier id, int count)
            {
                SerializableProperties = SerializableProperty.DeserializeProperties(this, element: null);
                Identifier = id;
                Count = count;
                RequiredFaction = Identifier.Empty;
            }

            public override string ToString()                
            {
                string outpostSideStr = OutpostSideToAttachTo == OutpostModuleInfo.GapPosition.None ? string.Empty : $", {OutpostSideToAttachTo}";
                return $"{nameof(ModuleCount)} ({Name}{outpostSideStr})";
            }
        }

        private readonly List<ModuleCount> moduleCounts = new List<ModuleCount>();

        public IReadOnlyList<ModuleCount> ModuleCounts
        {
            get { return moduleCounts; }
        }

        private class NpcCollection : IReadOnlyList<HumanPrefab>
        {
            public enum FactionType
            {
                Either,
                Primary, 
                Secondary
            }

            private class Entry
            {
                private readonly HumanPrefab humanPrefab = null;
                public readonly Identifier SetIdentifier = Identifier.Empty;
                public readonly Identifier NpcIdentifier = Identifier.Empty;

                /// <summary>
                /// If set, this faction must be present in the outpost for the NPC to spawn. 
                /// <see cref="FactionType"/> determines if the faction needs to be the primary one, the secondary one or either.
                /// </summary>
                public readonly Identifier FactionIdentifier = Identifier.Empty;

                /// <summary>
                /// Determines whether <see cref="FactionIdentifier"/> needs to match the primary faction, the secondary faction or either.
                /// </summary>
                public readonly FactionType FactionType = FactionType.Either;

                /// <summary>
                /// If the population of the outpost has been reduced from the initial population (e.g. due to the DLC or some mod reducing the population in some custom way),
                /// some of the NPCs defined in the outpost generation parameters will not spawn. This field can be used to prioritize certain NPCs, making it e.g. the
                /// outpost manager and service-offering characters be removed from the outpost last.
                /// </summary>
                public readonly int SpawnPriority = 0;

                public readonly ContentPackage ContentPackage;
                
                public Entry(HumanPrefab humanPrefab, Identifier factionIdentifier, FactionType factionType, int spawnPriority, ContentPackage contentPackage)
                {
                    this.humanPrefab = humanPrefab;
                    FactionIdentifier = factionIdentifier;
                    FactionType = factionType;
                    SpawnPriority = spawnPriority;
                    ContentPackage = contentPackage;
                }

                public Entry(Identifier setIdentifier, Identifier npcIdentifier, Identifier factionIdentifier, FactionType factionType, int spawnPriority, ContentPackage contentPackage)
                {
                    SetIdentifier = setIdentifier;
                    NpcIdentifier = npcIdentifier;
                    FactionIdentifier = factionIdentifier;
                    FactionType = factionType;
                    SpawnPriority = spawnPriority;
                    ContentPackage = contentPackage;
                }
                
                public HumanPrefab HumanPrefab
                    => humanPrefab ?? NPCSet.Get(SetIdentifier, NpcIdentifier, contentPackageToLogInError: ContentPackage);
            }

            private readonly List<Entry> entries = [];

            public void Add(HumanPrefab humanPrefab, Identifier factionIdentifier, FactionType factionType, int spawnPriority, ContentPackage contentPackage)
            {
                entries.Add(new Entry(humanPrefab, factionIdentifier, factionType, spawnPriority, contentPackage));
                entries.Sort((a, b) => b.SpawnPriority.CompareTo(a.SpawnPriority));
            }


            public void Add(Identifier setIdentifier, Identifier npcIdentifier, Identifier factionIdentifier, FactionType factionType, int spawnPriority, ContentPackage contentPackage)
            {
                entries.Add(new Entry(setIdentifier, npcIdentifier, factionIdentifier, factionType, spawnPriority, contentPackage));
                entries.Sort((a, b) => b.SpawnPriority.CompareTo(a.SpawnPriority));
            }

            public IEnumerator<HumanPrefab> GetEnumerator()
            {
                foreach (var entry in entries)
                {
                    if (entry == null) { continue; }
                    yield return entry.HumanPrefab;
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public IEnumerable<HumanPrefab> GetByFaction(FactionPrefab faction, FactionPrefab secondaryFaction)
            {
                foreach (var entry in entries)
                {
                    if (entry.FactionIdentifier != Identifier.Empty)
                    {
                        switch (entry.FactionType)
                        {
                            case FactionType.Either:
                                if (entry.FactionIdentifier != faction?.Identifier && entry.FactionIdentifier != secondaryFaction?.Identifier) { continue; }
                                break;
                            case FactionType.Primary:
                                if (entry.FactionIdentifier != faction?.Identifier) { continue; }
                                break;
                            case FactionType.Secondary:
                                if (entry.FactionIdentifier != secondaryFaction?.Identifier) { continue; }
                                break;
                        }
                    }
                    
                    if (entry.HumanPrefab != null)
                    {
                        yield return entry.HumanPrefab;
                    }
                    
                }
            }

            public int Count => entries.Count;

            public HumanPrefab this[int index] => entries[index].HumanPrefab;
        }
        
        private readonly ImmutableArray<NpcCollection> humanPrefabCollections;

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; private set; }

        private ImmutableHashSet<Identifier> StoreIdentifiers { get; set; }

        #warning TODO: this shouldn't really accept any ContentFile, issue is that RuinConfigFile and OutpostConfigFile are separate derived classes
        public OutpostGenerationParams(ContentXElement element, ContentFile file) : base(file, element.GetAttributeIdentifier("identifier", ""))
        {
            Name = element.GetAttributeString("name", Identifier.Value);
            allowedLocationTypes = element.GetAttributeIdentifierArray("allowedlocationtypes", Array.Empty<Identifier>()).ToHashSet();
            allowedGameModeIdentifiers = element.GetAttributeIdentifierArray("allowedgamemodes", Array.Empty<Identifier>()).ToHashSet();
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);

            if (element.GetAttribute("leveltype") != null)
            {
                string levelTypeStr = element.GetAttributeString("leveltype", "");
                if (Enum.TryParse(levelTypeStr, out LevelData.LevelType parsedLevelType))
                {
                    LevelType = parsedLevelType;
                }
                else
                {
                    DebugConsole.ThrowError($"Error in outpost generation parameters \"{Identifier}\". \"{levelTypeStr}\" is not a valid level type.", contentPackage: element.ContentPackage);
                }
            }

            OutpostFilePath = element.GetAttributeContentPath(nameof(OutpostFilePath));
            OutpostTag = element.GetAttributeIdentifier(nameof(OutpostTag), Identifier.Empty);
            TriggerEvents = element.GetAttributeIdentifierImmutableHashSet(nameof(TriggerEvents), ImmutableHashSet<Identifier>.Empty);

            var humanPrefabCollections = new List<NpcCollection>();
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "modulecount":
                        var newModuleCount = new ModuleCount(subElement);
                        if (moduleCounts.None() && newModuleCount.Probability < 1.0f)
                        {
                            DebugConsole.AddWarning(
                                $"Potential error in outpost generation parameters \"{Identifier}\"." +
                                $" The first module is set to spawn with a probability of {newModuleCount.Probability}%. The first module must always spawn, so the probability will be ignored.",
                                contentPackage: ContentPackage);
                            newModuleCount.Probability = 1.0f;
                        }
                        else if (newModuleCount.Probability <= 0.0f)
                        {
                            DebugConsole.AddWarning(
                                $"Potential error in outpost generation parameters \"{Identifier}\"." +
                                $" Probability of the module {newModuleCount.Identifier} is 0% (the module should never spawn, so there's no reason to include it in the generation parameters.",
                                contentPackage: ContentPackage);
                        }
                        moduleCounts.Add(newModuleCount);
                        break;
                    case "npcs":
                        var newCollection = new NpcCollection();
                        foreach (var npcElement in subElement.Elements())
                        {
                            Identifier from = npcElement.GetAttributeIdentifier("from", Identifier.Empty);
                            Identifier faction = npcElement.GetAttributeIdentifier("faction", Identifier.Empty);
                            NpcCollection.FactionType factionType = npcElement.GetAttributeEnum("factiontype", NpcCollection.FactionType.Either);
                            int spawnPriority = npcElement.GetAttributeInt("spawnpriority", 0);
                            if (from != Identifier.Empty)
                            {
                                newCollection.Add(
                                    from, 
                                    npcElement.GetAttributeIdentifier("identifier", Identifier.Empty), 
                                    faction, factionType, 
                                    spawnPriority, 
                                    npcElement.ContentPackage);
                            }
                            else
                            {
                                newCollection.Add(
                                    new HumanPrefab(npcElement, file, npcSetIdentifier: from), 
                                    faction, factionType, 
                                    spawnPriority, 
                                    npcElement.ContentPackage);
                            }
                        }
                        humanPrefabCollections.Add(newCollection);
                        break;
                }
            }

            this.humanPrefabCollections = humanPrefabCollections.ToImmutableArray();
        }

        public int GetModuleCount(Identifier moduleFlag)
        {
            if (moduleFlag == Identifier.Empty || moduleFlag == "none") { return int.MaxValue; }
            return moduleCounts.FirstOrDefault(m => m.Identifier == moduleFlag)?.Count ?? 0;
        }

        public void SetModuleCount(Identifier moduleFlag, int count, float? probability = null, float? minDifficulty = null, float? maxDifficulty = null)
        {
            if (moduleFlag == Identifier.Empty || moduleFlag == "none") { return; }
            if (count <= 0)
            {
                moduleCounts.RemoveAll(m => m.Identifier == moduleFlag);
            }
            else
            {
                var moduleCount = moduleCounts.FirstOrDefault(m => m.Identifier == moduleFlag);
                if (moduleCount == null)
                {
                    moduleCount = new ModuleCount(moduleFlag, count);
                    if (moduleCount.Probability <= 0.0f)
                    {
                        DebugConsole.AddWarning(
                            $"Potential error in outpost generation parameters \"{Identifier}\"."+
                            $" Probability of the module {moduleCount.Identifier} is 0 (the module should never spawn, so there's no reason to include it in the generation parameters.",
                            contentPackage: ContentPackage);
                    }
                    moduleCounts.Add(moduleCount);
                }
                moduleCount.Count = count;
                if (probability.HasValue) { moduleCount.Probability = probability.Value; }
                if (minDifficulty.HasValue) { moduleCount.MinDifficulty = minDifficulty.Value; }
                if (maxDifficulty.HasValue) { moduleCount.MaxDifficulty = maxDifficulty.Value; }
            }
        }

        public void SetAllowedLocationTypes(IEnumerable<Identifier> allowedLocationTypes)
        {
            this.allowedLocationTypes.Clear();
            foreach (Identifier locationType in allowedLocationTypes)
            {
                if (locationType == "any") { continue; }
                this.allowedLocationTypes.Add(locationType);
            }
        }

        public IReadOnlyList<HumanPrefab> GetHumanPrefabs(FactionPrefab faction, FactionPrefab secondaryFaction, Submarine sub, Rand.RandSync randSync)
        {
            if (!humanPrefabCollections.Any()) { return Array.Empty<HumanPrefab>(); }

            var collection = humanPrefabCollections.GetRandom(randSync);
            return collection                
                .GetByFaction(faction, secondaryFaction)
                .Where(humanPrefab => !humanPrefab.RequireSpawnPointTag || WayPoint.WayPointList.Any(wp => wp.Submarine == sub && humanPrefab.GetSpawnPointTags().Any(tag => wp.Tags.Contains(tag))))
                .ToImmutableList();
        }

        public bool CanHaveCampaignInteraction(CampaignMode.InteractionType interactionType)
        {
            foreach (var collection in humanPrefabCollections)
            {
                foreach (var prefab in collection)
                {
                    if (prefab != null && prefab.CampaignInteractionType == interactionType)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public ImmutableHashSet<Identifier> GetStoreIdentifiers()
        {
            if (StoreIdentifiers == null)
            {
                var storeIdentifiers = new HashSet<Identifier>();
                foreach (var collection in humanPrefabCollections)
                {
                    foreach (var prefab in collection)
                    {
                        if (prefab?.CampaignInteractionType == CampaignMode.InteractionType.Store)
                        {
                            storeIdentifiers.Add(prefab.GetStoreIdentifier());
                        }
                    }
                }
                StoreIdentifiers = storeIdentifiers.ToImmutableHashSet();
            }
            return StoreIdentifiers;
        }

        public override void Dispose() { }

        public override string ToString() => $"{nameof(OutpostGenerationParams)} ({Identifier})";
    }
}
