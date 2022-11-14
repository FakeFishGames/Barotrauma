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


        [Serialize(-1, IsPropertySaveable.Yes), Editable(MinValueInt = -1, MaxValueInt = 10)]
        public int ForceToEndLocationIndex
        {
            get;
            set;
        }


        [Serialize(10, IsPropertySaveable.Yes), Editable(MinValueInt = 1, MaxValueInt = 50)]
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

        [Serialize(200.0f, IsPropertySaveable.Yes), Editable(MinValueFloat = 0.0f, MaxValueFloat = 1000.0f)]
        public float MinHallwayLength
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.Yes), Editable]
        public bool AlwaysDestructible
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.Yes), Editable]
        public bool AlwaysRewireable
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.Yes), Editable]
        public bool AllowStealing
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.Yes), Editable]
        public bool SpawnCrewInsideOutpost
        {
            get;
            set;
        }
        
        [Serialize(true, IsPropertySaveable.Yes), Editable]
        public bool LockUnusedDoors
        {
            get;
            set;
        }

        [Serialize(true, IsPropertySaveable.Yes), Editable]
        public bool RemoveUnusedGaps
        {
            get;
            set;
        }

        [Serialize(false, IsPropertySaveable.Yes), Editable]
        public bool DrawBehindSubs
        {
            get;
            set;
        }

        [Serialize(0.0f, IsPropertySaveable.Yes), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f)]
        public float MinWaterPercentage
        {
            get;
            set;
        }

        [Serialize(0.0f, IsPropertySaveable.Yes), Editable(MinValueFloat = 0.0f, MaxValueFloat = 100.0f)]
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

        [Serialize("", IsPropertySaveable.Yes), Editable]
        public string ReplaceInRadiation { get; set; }

        public ContentPath OutpostFilePath { get; set; }

        public class ModuleCount
        {
            public Identifier Identifier;
            public int Count;
            public int Order;

            public Identifier RequiredFaction;

            public ModuleCount(ContentXElement element)
            {
                Identifier = element.GetAttributeIdentifier("flag", element.GetAttributeIdentifier("moduletype", ""));
                Count = element.GetAttributeInt("count", 0);
                Order = element.GetAttributeInt("order", 0);
                RequiredFaction = element.GetAttributeIdentifier("requiredfaction", Identifier.Empty);
            }

            public ModuleCount(Identifier id, int count)
            {
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
                private readonly Identifier setIdentifier = Identifier.Empty;
                private readonly Identifier npcIdentifier = Identifier.Empty;

                public readonly Identifier FactionIdentifier = Identifier.Empty;
                
                public Entry(HumanPrefab humanPrefab, Identifier factionIdentifier)
                {
                    this.humanPrefab = humanPrefab;
                    this.FactionIdentifier = factionIdentifier;
                }

                public Entry(Identifier setIdentifier, Identifier npcIdentifier, Identifier factionIdentifier)
                {
                    this.setIdentifier = setIdentifier;
                    this.npcIdentifier = npcIdentifier;
                    this.FactionIdentifier = factionIdentifier;
                }
                
                public HumanPrefab HumanPrefab
                    => humanPrefab ?? NPCSet.Get(setIdentifier, npcIdentifier);
            }

            private readonly List<Entry> entries = new List<Entry>();

            public void Add(HumanPrefab humanPrefab, Identifier factionIdentifier)
                => entries.Add(new Entry(humanPrefab, factionIdentifier));
            
            
            public void Add(Identifier setIdentifier, Identifier npcIdentifier, Identifier factionIdentifier)
                => entries.Add(new Entry(setIdentifier, npcIdentifier, factionIdentifier));

            public IEnumerator<HumanPrefab> GetEnumerator()
            {
                foreach (var entry in entries)
                {
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
                    DebugConsole.ThrowError($"Error in outpost generation parameters \"{Identifier}\". \"{levelTypeStr}\" is not a valid level type.");
                }
            }

            OutpostFilePath = element.GetAttributeContentPath(nameof(OutpostFilePath));
            
            var humanPrefabCollections = new List<NpcCollection>();
            foreach (var subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "modulecount":
                        moduleCounts.Add(new ModuleCount(subElement));
                        break;
                    case "npcs":
                        var newCollection = new NpcCollection();
                        foreach (var npcElement in subElement.Elements())
                        {
                            Identifier from = npcElement.GetAttributeIdentifier("from", Identifier.Empty);
                            Identifier faction = npcElement.GetAttributeIdentifier("faction", Identifier.Empty);
                            if (from != Identifier.Empty)
                            {
                                newCollection.Add(from, npcElement.GetAttributeIdentifier("identifier", Identifier.Empty), faction);
                            }
                            else
                            {
                                newCollection.Add(new HumanPrefab(npcElement, file, npcSetIdentifier: from), faction);
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

        public void SetModuleCount(Identifier moduleFlag, int count)
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
                    moduleCounts.Add(new ModuleCount(moduleFlag, count));
                }
                else
                {
                    moduleCount.Count = count;
                }
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

        public IReadOnlyList<HumanPrefab> GetHumanPrefabs(IEnumerable<FactionPrefab> factions, Rand.RandSync randSync)
        {
            if (!humanPrefabCollections.Any()) { return Array.Empty<HumanPrefab>(); }

            var collection = humanPrefabCollections.GetRandom(randSync);
            return collection.GetByFaction(factions).ToImmutableList();
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
