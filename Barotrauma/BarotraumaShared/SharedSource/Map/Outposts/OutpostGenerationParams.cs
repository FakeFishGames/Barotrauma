using Barotrauma.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    class OutpostGenerationParams : PrefabWithUintIdentifier, ISerializableEntity
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

        [Serialize(false, IsPropertySaveable.Yes, description: "Should this outpost always be destructible, regardless if damaging outposts is allowed by the server?"), Editable]
        public bool AlwaysDestructible
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
        }

        private readonly List<ModuleCount> moduleCounts = new List<ModuleCount>();

        public IReadOnlyList<ModuleCount> ModuleCounts
        {
            get { return moduleCounts; }
        }

        private class NpcCollection : IReadOnlyList<HumanPrefab>
        {
            private class Entry
            {
                private readonly HumanPrefab humanPrefab = null;
                public readonly Identifier SetIdentifier = Identifier.Empty;
                public readonly Identifier NpcIdentifier = Identifier.Empty;

                public readonly Identifier FactionIdentifier = Identifier.Empty;

                public readonly ContentPackage ContentPackage;
                
                public Entry(HumanPrefab humanPrefab, Identifier factionIdentifier, ContentPackage contentPackage)
                {
                    this.humanPrefab = humanPrefab;
                    FactionIdentifier = factionIdentifier;
                    ContentPackage = contentPackage;
                }

                public Entry(Identifier setIdentifier, Identifier npcIdentifier, Identifier factionIdentifier, ContentPackage contentPackage)
                {
                    SetIdentifier = setIdentifier;
                    NpcIdentifier = npcIdentifier;
                    FactionIdentifier = factionIdentifier;
                    ContentPackage = contentPackage;
                }
                
                public HumanPrefab HumanPrefab
                    => humanPrefab ?? NPCSet.Get(SetIdentifier, NpcIdentifier, contentPackageToLogInError: ContentPackage);
            }

            private readonly List<Entry> entries = new List<Entry>();

            public void Add(HumanPrefab humanPrefab, Identifier factionIdentifier, ContentPackage contentPackage)
                => entries.Add(new Entry(humanPrefab, factionIdentifier, contentPackage));
            
            
            public void Add(Identifier setIdentifier, Identifier npcIdentifier, Identifier factionIdentifier, ContentPackage contentPackage)
                => entries.Add(new Entry(setIdentifier, npcIdentifier, factionIdentifier, contentPackage));

            public IEnumerator<HumanPrefab> GetEnumerator()
            {
                foreach (var entry in entries)
                {
                    if (entry == null) { continue; }
                    yield return entry.HumanPrefab;
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public IEnumerable<HumanPrefab> GetByFaction(IEnumerable<FactionPrefab> factions)
            {
                foreach (var entry in entries)
                {
                    if (entry.FactionIdentifier == Identifier.Empty || factions.Any(f => f.Identifier == entry.FactionIdentifier))
                    {
                        if (entry.HumanPrefab != null)
                        {
                            yield return entry.HumanPrefab;
                        }
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
                            if (from != Identifier.Empty)
                            {
                                newCollection.Add(from, npcElement.GetAttributeIdentifier("identifier", Identifier.Empty), faction, npcElement.ContentPackage);
                            }
                            else
                            {
                                newCollection.Add(new HumanPrefab(npcElement, file, npcSetIdentifier: from), faction, npcElement.ContentPackage);
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

        public IReadOnlyList<HumanPrefab> GetHumanPrefabs(IEnumerable<FactionPrefab> factions, Submarine sub, Rand.RandSync randSync)
        {
            if (!humanPrefabCollections.Any()) { return Array.Empty<HumanPrefab>(); }

            var collection = humanPrefabCollections.GetRandom(randSync);
            return collection
                .GetByFaction(factions)
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
                            storeIdentifiers.Add(prefab.Identifier);
                        }
                    }
                }
                StoreIdentifiers = storeIdentifiers.ToImmutableHashSet();
            }
            return StoreIdentifiers;
        }

        public override void Dispose() { }
    }
}
