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

        [Serialize(10, IsPropertySaveable.Yes), Editable(MinValueInt = 1, MaxValueInt = 50)]
        public int TotalModuleCount
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

        [Serialize("", IsPropertySaveable.Yes), Editable]
        public string ReplaceInRadiation { get; set; }

        public ContentPath OutpostFilePath { get; set; }

        public class ModuleCount
        {
            public Identifier Identifier;
            public int Count;
            public int Order;

            public ModuleCount(ContentXElement element)
            {
                Identifier = element.GetAttributeIdentifier("flag", element.GetAttributeIdentifier("moduletype", ""));
                Count = element.GetAttributeInt("count", 0);
                Order = element.GetAttributeInt("order", 0);
            }

            public ModuleCount(Identifier id, int count)
            {
                Identifier = id;
                Count = count;
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
                
                public Entry(HumanPrefab humanPrefab)
                {
                    this.humanPrefab = humanPrefab;
                }

                public Entry(Identifier setIdentifier, Identifier npcIdentifier)
                {
                    this.setIdentifier = setIdentifier;
                    this.npcIdentifier = npcIdentifier;
                }
                
                public HumanPrefab HumanPrefab
                    => humanPrefab ?? NPCSet.Get(setIdentifier, npcIdentifier);
            }

            private readonly List<Entry> entries = new List<Entry>();

            public void Add(HumanPrefab humanPrefab)
                => entries.Add(new Entry(humanPrefab));
            
            
            public void Add(Identifier setIdentifier, Identifier npcIdentifier)
                => entries.Add(new Entry(setIdentifier, npcIdentifier));

            public IEnumerator<HumanPrefab> GetEnumerator()
            {
                foreach (var entry in entries)
                {
                    yield return entry.HumanPrefab;
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public int Count => entries.Count;

            public HumanPrefab this[int index] => entries[index].HumanPrefab;
        }
        
        private readonly ImmutableArray<IReadOnlyList<HumanPrefab>> humanPrefabCollections;

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; private set; }

        private ImmutableHashSet<Identifier> StoreIdentifiers { get; set; }

        #warning TODO: this shouldn't really accept any ContentFile, issue is that RuinConfigFile and OutpostConfigFile are separate derived classes
        public OutpostGenerationParams(ContentXElement element, ContentFile file) : base(file, element.GetAttributeIdentifier("identifier", ""))
        {
            Name = element.GetAttributeString("name", Identifier.Value);
            allowedLocationTypes = element.GetAttributeIdentifierArray("allowedlocationtypes", Array.Empty<Identifier>()).ToHashSet();
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            OutpostFilePath = element.GetAttributeContentPath(nameof(OutpostFilePath));
            
            var humanPrefabCollections = new List<IReadOnlyList<HumanPrefab>>();
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

                            if (from != Identifier.Empty)
                            {
                                newCollection.Add(from, npcElement.GetAttributeIdentifier("identifier", Identifier.Empty));
                            }
                            else
                            {
                                newCollection.Add(new HumanPrefab(npcElement, file, npcSetIdentifier: from));
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

        public IReadOnlyList<HumanPrefab> GetHumanPrefabs(Rand.RandSync randSync)
        {
            if (!humanPrefabCollections.Any()) { return Array.Empty<HumanPrefab>(); }
            return humanPrefabCollections.GetRandom(randSync);
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
