using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.RuinGeneration;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public class PlacedModule : ISerializableEntity
    {
        /// <summary>
        /// Info of this outpost module
        /// </summary>
        public readonly SubmarineInfo Info;

        /// <summary>
        /// Which module is this one attached to
        /// </summary>
        public PlacedModule PreviousModule { get; private set; }

        [Serialize(-1, IsPropertySaveable.Yes)]
        public int Index { get; private set; }

        [Serialize(-1, IsPropertySaveable.Yes)]
        public int PreviousModuleIndex { get; private set; }

        /// <summary>
        /// The position of this module's gap that attaches to the previous module
        /// </summary>
        [Serialize(0, IsPropertySaveable.Yes)]
        public OutpostModuleInfo.GapPosition ThisGapPosition { get; set; } = 0;

        [Serialize(0, IsPropertySaveable.Yes)]
        public OutpostModuleInfo.GapPosition UsedGapPositions { get; set; }

        public readonly HashSet<Identifier> FulfilledModuleTypes = new HashSet<Identifier>();

        public readonly List<MapEntity> Entities = new List<MapEntity>();

        public readonly List<MapEntity> HallwayEntities = new List<MapEntity>();
        /// <summary>
        /// Which of the module counts defined in the generation parameters was this module placed to fulfill?
        /// </summary>
        public readonly HashSet<OutpostGenerationParams.ModuleCount> FulfilledModuleCounts = new HashSet<OutpostGenerationParams.ModuleCount>();

        [Serialize("0,0", IsPropertySaveable.Yes)]
        public Vector2 Offset { get; set; }

        /// <summary>
        /// An temporary offset that's used when moving the outpost modules around during outpost generation
        /// </summary>
        public Vector2 TemporaryMoveOffset { get; set; }

        private Gap thisGap, previousGap;
        public Gap ThisGap
        {
            get => thisGap;
            set
            {
                thisGap = value;
                ThisGapId = thisGap?.ID ?? Entity.NullEntityID;
            }
        }

        public Gap PreviousGap
        {
            get => previousGap;
            set
            {
                previousGap = value;
                PreviousGapId = previousGap?.ID ?? Entity.NullEntityID;
            }
        }

        [Serialize(Entity.NullEntityID, IsPropertySaveable.Yes)]
        public ushort ThisGapId { get; private set; }

        [Serialize(Entity.NullEntityID, IsPropertySaveable.Yes)]
        public ushort PreviousGapId { get; private set; }

        [Serialize("0,0", IsPropertySaveable.Yes)]
        public Vector2 PositionRelativeToSub { get; set; }

        [Serialize("0,0,0,0", IsPropertySaveable.Yes)]
        public Rectangle Bounds { get; set; }

        [Serialize("0,0,0,0", IsPropertySaveable.Yes)]
        public Rectangle HullBounds { get; set; }

        [Serialize("0,0,0,0", IsPropertySaveable.Yes)]
        public Rectangle VisibleEntityBounds { get; set; }

        public ImmutableArray<ushort> EntityIds { get; set; }

        public ImmutableArray<ushort> HallwayEntityIds { get; set; }

        public class GapInfo
        {
            private Gap gap;
            public Gap Gap
            {
                get => gap;
                set
                {
                    gap = value;
                    GapId = gap == null ? Entity.NullEntityID : gap.ID;
                }
            }

            public ushort GapId { get; private set; }
            /// <summary>
            /// The bounds of the gap, the position being relative to the position of the module.
            /// </summary>
            public readonly Rectangle Rect;

            public GapInfo(XElement element)
            {
                GapId = element.GetAttributeUInt16(nameof(GapId), Entity.NullEntityID);
                Rect = element.GetAttributeRect(nameof(Rect), Rectangle.Empty);
            }

            public GapInfo(Gap gap)
            {
                Gap = gap;
                GapId = gap.ID;
                Rect = gap.Rect;
            }

            public void Save(XElement parentElement)
            {
                parentElement.Add(
                    new XElement(nameof(GapInfo),
                        new XAttribute(nameof(GapId), GapId),
                        new XAttribute(nameof(Rect), XMLExtensions.RectToString(Rect))));
            }

            public override string ToString()
            {
                return $"{nameof(GapInfo)} ({nameof(GapId)}: {GapId})";
            }
        }

        /// <summary>
        /// Info of all gaps that exist (or have existed) in the module. 
        /// Some of these may have been <see cref="MapEntity.IsDisabled">disabled</see> because they ended up being unused (not connected to any module) during outpost generation.
        /// </summary>
        public readonly List<GapInfo> AllGaps = [];

        public string Name => $"{nameof(PlacedModule)} ({Info?.DisplayName ?? "null"})";

        public Dictionary<Identifier, SerializableProperty> SerializableProperties { get; }

        public PlacedModule(int moduleIndex, SubmarineInfo thisModule, PlacedModule previousModule, OutpostModuleInfo.GapPosition thisGapPosition)
        {
            if (thisModule == null) { throw new ArgumentException("Module info cannot be null.", nameof(thisModule)); }

            Index = moduleIndex;
            Info = thisModule;
            PreviousModule = previousModule;
            PreviousModuleIndex = previousModule?.Index ?? -1;
            ThisGapPosition = thisGapPosition;
            UsedGapPositions = thisGapPosition;
            if (PreviousModule != null)
            {
                PreviousModule.UsedGapPositions |= OutpostGenerator.GetOpposingGapPosition(thisGapPosition);
            }
            SerializableProperties = SerializableProperty.GetProperties(this);
        }

        #region DLC / code mod features
        
        /// <summary>
        /// Currently only used for purchasable modules in the DLC. Defined in xml.
        /// </summary>
        public Identifier Category { get; set; } = Identifier.Empty;

        /// <summary>
        /// Can be used to load information about a placed module from XML. Not used by the vanilla game, but can be used by DLCs or code mods.
        /// </summary>
        public PlacedModule(XElement element)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
            EntityIds = element.GetAttributeUshortArray(nameof(EntityIds), []).ToImmutableArray();
            HallwayEntityIds = element.GetAttributeUshortArray(nameof(HallwayEntityIds), []).ToImmutableArray();
            string moduleName = element.GetAttributeString(nameof(SubmarineInfo), string.Empty);
            Info = SubmarineInfo.SavedSubmarines.FirstOrDefault(s => s.Name == moduleName && s.IsOutpostModule);
            Category = element.GetAttributeIdentifier(nameof(Category), Identifier.Empty);

            if (Info == null)
            {
                throw new Exception($"Failed to find the module file for the placed module \"{moduleName}\".");
            }
            foreach (var subElement in element.Elements())
            {
                if (subElement.NameAsIdentifier() == nameof(GapInfo))
                {
                    AllGaps.Add(new GapInfo(subElement));
                }
            }
        }

        /// <summary>
        /// Does the gap belong to this module?
        /// </summary>
        public bool IsGapInThisModule(Gap gap) => AllGaps.Any(g => g.Gap == gap);

        /// <summary>
        /// Handles initializing entities in the module (making sure the links between the entities have been created, that doors are linked to gaps, etc).
        /// Normally this is done automatically during outpost generation as a part of <see cref="OutpostGenerator.LoadModule(Submarine, PlacedModule, int, OutpostGenerationParams, ref int, Rectangle?, Gap)"/>,
        /// but doing it manually may be necessary when the outpost is loaded from XML using some kind of custom outpost saving/loading logic.
        /// </summary>
        public void InitializeEntities()
        {
            if (Index == -1)
            {
                DebugConsole.ThrowError($"Error when initializing entities in the module {Name}. Index of the module hasn't been set yet.");
            }

            MapEntity.InitializeLoadedLinks(Entities);

            foreach (MapEntity entity in Entities.ToList())
            {
                if (entity is Item item)
                {
                    item.OriginalModuleIndex = Index;
                    var door = item.GetComponent<Door>();
                    if (door != null)
                    {
                        door.RefreshLinkedGap();
                        if (!Entities.Contains(door.LinkedGap)) { Entities.Add(door.LinkedGap); }
                    }
                    foreach (var connectionPanel in item.GetComponents<ConnectionPanel>())
                    {
                        connectionPanel.OnMapLoaded();
                    }
                    foreach (var itemContainer in item.GetComponents<ItemContainer>())
                    {
                        itemContainer.OnMapLoaded();
                    }
                    item.GetComponent<CircuitBox>()?.OnMapLoaded();
                }
            }
            EntityIds = Entities.Select(e => e.ID).ToImmutableArray();
        }

        /// <summary>
        /// Handles initialization that needs to be done after all the modules and entities of an outpost have been loaded from XML
        /// (e.g. linking modules to each other and the entities they consist of). 
        /// Note that this is not necessary to call on procedurally generated outpost or modules loaded with <see cref="OutpostGenerator.LoadModule(Submarine, PlacedModule, int, OutpostGenerationParams, ref int, Rectangle?, Gap)"/>,
        /// but only on outposts that are loaded from XML using some kind of custom outpost saving/loading logic.
        /// 
        /// Not used by the vanilla game, but can be used by DLCs or code mods.
        /// </summary>
        public static void InitializeLoadedModules(IEnumerable<PlacedModule> modules, IEnumerable<MapEntity> entities, IdRemap idRemap)
        {
            foreach (var module in modules)
            {
                module.PreviousModule = modules.FirstOrDefault(otherModule => otherModule.Index == module.PreviousModuleIndex);

                if (module.EntityIds != null)
                {
                    foreach (ushort entityId in module.EntityIds)
                    {
                        ushort remappedId = idRemap.GetOffsetId(entityId);
                        if (Entity.FindEntityByID(remappedId) is not MapEntity entity)
                        {
                            DebugConsole.ThrowError($"Error while loading the module {module.Name}: could not find an entity with the id {entityId}, remapped id: {remappedId}.");
                        }
                        else
                        {
                            module.Entities.Add(entity);
                            switch (entity)
                            {
                                case Hull hull:
                                    hull.SetModuleTags(module.Info.OutpostModuleInfo.ModuleFlags);
                                    break;
                                case Item it:
                                    it.OnItemReplaced = (thisEntity, newEntity) =>
                                    {
                                        if (module.Entities.Remove(thisEntity))
                                        {
                                            module.Entities.Add(newEntity);
                                        }
                                    };
                                    break;
                            }
                        }
                    }
                }
                if (module.HallwayEntityIds != null)
                {
                    foreach (ushort entityId in module.HallwayEntityIds)
                    {
                        ushort remappedId = idRemap.GetOffsetId(entityId);
                        if (Entity.FindEntityByID(remappedId) is not MapEntity hallwayEntity)
                        {
                            DebugConsole.ThrowError($"Error while loading the module {module.Name}: could not find a hallway entity with the id {entityId}, remapped id: {remappedId}.");
                        }
                        else
                        {
                            module.HallwayEntities.Add(hallwayEntity);
                        }
                    }
                }
                module.InitializeEntities();

                //if gap id is null, it means this is the initial module (usually airlock) that other modules attach to
                if (module.PreviousGapId != Entity.NullEntityID)
                {
                    ushort previousGapId = module.PreviousGapId;
                    ushort remappedPreviousGapId = idRemap.GetOffsetId(previousGapId);
                    module.PreviousGap = entities.FirstOrDefault(entity => entity is Gap gap && gap.ID == remappedPreviousGapId) as Gap;
                    if (module.PreviousGap == null)
                    {
                        DebugConsole.ThrowError($"Error while loading the module {module.Name}: could not find the gap this module is attached to (id {previousGapId}, remapped id: {remappedPreviousGapId}).");
                    }
                }
                if (module.ThisGapId != Entity.NullEntityID)
                {
                    ushort thisGapId = module.ThisGapId;
                    ushort remappedThisGapId = idRemap.GetOffsetId(thisGapId);
                    module.ThisGap = entities.FirstOrDefault(entity => entity is Gap gap && gap.ID == remappedThisGapId) as Gap;
                    if (module.ThisGap == null)
                    {
                        DebugConsole.ThrowError($"Error while loading the module {module.Name}: could not find the gap that attaches to the previous module (id {thisGapId}, remapped id: {remappedThisGapId}).");
                    }
                }

                foreach (GapInfo gapInfo in module.AllGaps)
                {
                    ushort remappedGapId = idRemap.GetOffsetId(gapInfo.GapId);
                    gapInfo.Gap = Entity.FindEntityByID(remappedGapId) as Gap;
                    if (gapInfo.Gap == null)
                    {
                        DebugConsole.ThrowError($"Error while loading the module {module.Name}: could not find a gap with the id {gapInfo.GapId}, remapped id: {remappedGapId}.");
                    }
                }
            }
        }

        /// <summary>
        /// Saves information about the placed module into XML.
        /// Not used by the vanilla game, but can be used by DLCs or code mods.
        /// </summary>
        public void Save(XElement parentElement)
        {
            //remove e.g. items that have been consumed, used as materials
            Entities.RemoveAll(static e => e.Removed);
            HallwayEntities.RemoveAll(static e => e.Removed);

            var moduleElement = new XElement(nameof(PlacedModule),
                new XAttribute(nameof(EntityIds), string.Join(",", Entities.Select(e => e.ID))),
                new XAttribute(nameof(HallwayEntityIds), string.Join(",", HallwayEntities.Select(e => e.ID))),
                new XAttribute(nameof(Category), Category),
                new XAttribute(nameof(SubmarineInfo), Info.Name));
            SerializableProperty.SerializeProperties(this, moduleElement);
            foreach (var gapInfo in AllGaps)
            {
                gapInfo.Save(moduleElement);
            }
            parentElement.Add(moduleElement);
        }

        #endregion

        public override string ToString() => Name;

    }

    public static class OutpostGenerator
    {
        /// <summary>
        /// How many times the generator retries generating an outpost with a different seed if it fails to generate a valid outpost with no overlaps.
        /// </summary>
        const int MaxOutpostGenerationRetries = 6;

        public static Submarine Generate(OutpostGenerationParams generationParams, LocationType locationType, bool onlyEntrance = false, bool allowInvalidOutpost = false)
        {
            return Generate(generationParams, locationType, location: null, onlyEntrance, allowInvalidOutpost);
        }

        public static Submarine Generate(OutpostGenerationParams generationParams, Location location, bool onlyEntrance = false, bool allowInvalidOutpost = false)
        {
            return Generate(generationParams, location.Type, location, onlyEntrance, allowInvalidOutpost);
        }

        private static SubmarineInfo usedForceOutpostModule; 

        private static Submarine Generate(OutpostGenerationParams generationParams, LocationType locationType, Location location, bool onlyEntrance = false, bool allowInvalidOutpost = false)
        {
            var outpostModuleFiles = GetAvailableModuleFiles();
            if (location != null)
            {
                if (location.IsCriticallyRadiated() && OutpostGenerationParams.OutpostParams.FirstOrDefault(p => p.Identifier == generationParams.ReplaceInRadiation) is { } newParams)
                {
                    generationParams = newParams;
                }

                locationType = location.Type;
            }

            Submarine sub = null;
            if (generationParams.OutpostTag.IsEmpty && 
                generationParams.OutpostFilePath.IsNullOrEmpty())
            {
                if (location?.LoadOutpost != null)
                {
                    sub = location.LoadOutpost(generationParams, locationType, location, onlyEntrance, allowInvalidOutpost);
                    if (sub != null)
                    {
                        return sub;
                    }
                    DebugConsole.NewMessage($"No outpost returned by {nameof(Location.LoadOutpost)}. Generating the outpost instead...");                    
                }

                var forceOutpostModule = GameMain.GameSession?.ForceOutpostModule;
                sub = GenerateFromModules(generationParams, outpostModuleFiles, sub, locationType, location, onlyEntrance, allowInvalidOutpost);
                if (sub != null) 
                { 
                    return sub;
                }
                else if (forceOutpostModule != null)
                {
                    //failed to force the module, abort
                    return null;
                }
            }

            var prebuiltOutpostInfo = ChooseOutpost(generationParams);
            prebuiltOutpostInfo.Type = SubmarineType.Outpost;            
            sub = new Submarine(prebuiltOutpostInfo);
            sub.Info.OutpostGenerationParams = generationParams;
            location?.RemoveTakenItems();
            EnableFactionSpecificEntities(sub, location);
            return sub;
        }

        public static List<OutpostFile> GetOutpostFiles(bool includeTutorialOutposts = false)
        {
            var outpostFiles = ContentPackageManager.EnabledPackages.All
                .SelectMany(p => p.GetFiles<OutpostFile>())
                .Where(f => !TutorialPrefab.Prefabs.Any(tp => tp.OutpostPath == f.Path));
            if (!includeTutorialOutposts)
            {
                outpostFiles = outpostFiles.Where(f => !TutorialPrefab.Prefabs.Any(tp => tp.OutpostPath == f.Path));
            }
            return outpostFiles.OrderBy(f => f.UintIdentifier).ToList();
        }

        public static List<SubmarineInfo> GetOutpostInfos(bool includeTutorialOutposts = false)
        {
            var outpostFiles = GetOutpostFiles(includeTutorialOutposts);
            List<SubmarineInfo> outpostInfos = [];
            foreach (var outpostFile in outpostFiles)
            {
                outpostInfos.Add(new SubmarineInfo(outpostFile.Path.Value));
            }
            return outpostInfos;
        }

        private static SubmarineInfo ChooseOutpost(OutpostGenerationParams generationParams)
        {
            List<SubmarineInfo> outpostInfos = GetOutpostInfos();

            bool allowProcedurallyGeneratedOutposts = false;

            //if there's missions selected that allow outpost selection from some specific set of outposts,
            //choose one of those outposts
            List<SubmarineInfo> outpostInfosSuitableForMission = new List<SubmarineInfo>();
            if (GameMain.GameSession?.GameMode is { } gameMode)
            {
                foreach (var mission in gameMode.Missions)
                {
                    if (mission.Prefab.AllowOutpostSelectionProcedurallyGenerated)
                    {
                        allowProcedurallyGeneratedOutposts = true;
                    }
                    if (!mission.Prefab.AllowOutpostSelectionFromTag.IsEmpty)
                    {
                        foreach (var outpostInfo in outpostInfos)
                        {
                            if (outpostInfo.OutpostTags.Contains(mission.Prefab.AllowOutpostSelectionFromTag) &&
                                !outpostInfosSuitableForMission.Contains(outpostInfo))
                            {
                                outpostInfosSuitableForMission.Add(outpostInfo);
                            }
                        }
                    }
                }
            }

            //if an outpost has been select in the server settings, choose that...
            if (GameMain.NetworkMember?.ServerSettings is { } serverSettings &&
                serverSettings.SelectedOutpostName != "Random")
            {
                if (serverSettings.SelectedOutpostName == Tags.ProcedurallyGeneratedOutpostTag && 
                    allowProcedurallyGeneratedOutposts)
                {
                    //return nothing -> the outpost will be generated instead of loading a pre-made one
                    return null;
                }

                var matchingOutpost = outpostInfos.FirstOrDefault(o => o.Name == serverSettings.SelectedOutpostName);
                //...but only if the outpost is suitable for the mission (or if the mission has no specific requirements for the outpost)
                if (outpostInfosSuitableForMission.Contains(matchingOutpost) || 
                    outpostInfosSuitableForMission.None())
                {
                    if (matchingOutpost != null)
                    {
                        return matchingOutpost;
                    }
                }
            }

            if (outpostInfosSuitableForMission.Any())
            {
                return outpostInfosSuitableForMission.GetRandom(Rand.RandSync.ServerAndClient);
            }

            if (generationParams.OutpostTag.IsEmpty)
            {
                //attempt to use an outpost defined as a fallback, if that fails, any outpost without tags
                Identifier fallBackIdentifier = "fallback".ToIdentifier();
                if (outpostInfos.Any(o => o.OutpostTags.Contains(fallBackIdentifier)))
                {
                    outpostInfos = outpostInfos.FindAll(o => o.OutpostTags.Contains(fallBackIdentifier));
                }
                else
                {
                    outpostInfos = outpostInfos.FindAll(o => o.OutpostTags.None());
                }
            }
            else
            {
                if (outpostInfos.Any(o => o.OutpostTags.Contains(generationParams.OutpostTag)))
                {
                    outpostInfos = outpostInfos.FindAll(o => o.OutpostTags.Contains(generationParams.OutpostTag));
                }
                else
                {
                    DebugConsole.ThrowError($"Could not find any outposts with the tag {generationParams.OutpostTag}. Choosing a random one instead...");
                }
            }
            if (!outpostInfos.Any())
            {
                throw new Exception("Failed to generate an outpost. Could not generate an outpost from the available outpost modules and there are no pre-built outposts available.");
            }
            return outpostInfos.GetRandom(Rand.RandSync.ServerAndClient);
        }

        private static List<OutpostModuleFile> GetAvailableModuleFiles()
        {
            var outpostModuleFiles = ContentPackageManager.EnabledPackages.All
                .SelectMany(p => p.GetFiles<OutpostModuleFile>())
                .OrderBy(f => f.UintIdentifier);
            var uintIdDupes = outpostModuleFiles.Where(f1 =>
                outpostModuleFiles.Any(f2 => f1 != f2 && f1.UintIdentifier == f2.UintIdentifier));
            if (uintIdDupes.Any())
            {
                throw new Exception($"OutpostModuleFile UintIdentifier duplicates found: {uintIdDupes.Select(f => f.Path)}");
            }
            return outpostModuleFiles.ToList();
        }

        private static List<SubmarineInfo> GetAvailableModules(OutpostGenerationParams generationParams, List<OutpostModuleFile> moduleFiles)
        {
            var outpostModuleFiles = moduleFiles ?? GetAvailableModuleFiles();

            List<SubmarineInfo> outpostModules = new List<SubmarineInfo>();
            foreach (var outpostModuleFile in outpostModuleFiles)
            {
                var subInfo = new SubmarineInfo(outpostModuleFile.Path.Value);
                if (subInfo.OutpostModuleInfo != null)
                {
                    if (generationParams is RuinGeneration.RuinGenerationParams)
                    {
                        //if the module doesn't have the ruin flag or any other flag used in the generation params, don't use it in ruins
                        if (!subInfo.OutpostModuleInfo.ModuleFlags.Contains("ruin".ToIdentifier()) &&
                            !generationParams.ModuleCounts.Any(m => subInfo.OutpostModuleInfo.ModuleFlags.Contains(m.Identifier)))
                        {
                            continue;
                        }
                    }
                    else if (subInfo.OutpostModuleInfo.ModuleFlags.Contains("ruin".ToIdentifier()))
                    {
                        continue;
                    }
                    outpostModules.Add(subInfo);
                }
            }
            return outpostModules;
        }

        private static Submarine GenerateFromModules(OutpostGenerationParams generationParams, List<OutpostModuleFile> outpostModuleFiles, Submarine sub, LocationType locationType, Location location, bool onlyEntrance = false, bool allowInvalidOutpost = false)
        {
            //load the infos of the outpost module files
            List<SubmarineInfo> outpostModules = GetAvailableModules(generationParams, outpostModuleFiles);

            List<PlacedModule> selectedModules = new List<PlacedModule>();
            bool generationFailed = false;
            int remainingOutpostGenerationTries = MaxOutpostGenerationRetries;
            while (remainingOutpostGenerationTries > -1 && outpostModules.Any())
            {
                if (sub != null)
                {
#if SERVER
                    int eventCount = GameMain.Server.EntityEventManager.Events.Count();
                    int uniqueEventCount = GameMain.Server.EntityEventManager.UniqueEvents.Count();
#endif
                    HashSet<Submarine> connectedSubs = new HashSet<Submarine>() { sub };
                    foreach (Submarine otherSub in Submarine.Loaded)
                    {
                        //remove linked subs too
                        if (otherSub.Submarine == sub) { connectedSubs.Add(otherSub); }
                    }
                    List<MapEntity> entities = MapEntity.MapEntityList.FindAll(e => connectedSubs.Contains(e.Submarine));
                    entities.ForEach(e => e.Remove());
                    foreach (Submarine otherSub in connectedSubs)
                    {
                        otherSub.Remove();
                    }
#if SERVER
                    //remove any events created during the removal of the entities
                    GameMain.Server.EntityEventManager.Events.RemoveRange(eventCount, GameMain.Server.EntityEventManager.Events.Count - eventCount);
                    GameMain.Server.EntityEventManager.UniqueEvents.RemoveRange(uniqueEventCount, GameMain.Server.EntityEventManager.UniqueEvents.Count - uniqueEventCount);
#endif
                    if (remainingOutpostGenerationTries <= 0)
                    {
                        generationFailed = true;
                        break;
                    }
                }

                selectedModules.Clear();
                //select which module types the outpost should consist of
                List<OutpostGenerationParams.ModuleCount> pendingModules = new List<OutpostGenerationParams.ModuleCount>();
                if (generationParams.ModuleCounts.Any())
                {
                    pendingModules = onlyEntrance ?
                        generationParams.ModuleCounts[0].ToEnumerable().ToList() :
                        SelectModules(outpostModules, location, generationParams);
                }

                foreach (OutpostGenerationParams.ModuleCount moduleCount in pendingModules.ToList())
                {
                    Identifier flag = moduleCount.Identifier;

                    if (flag == "none") { continue; }
                    int pendingCount = pendingModules.Count(m => m == moduleCount);
                    int availableModuleCount =
                        outpostModules
                            .Where(m => m.OutpostModuleInfo.ModuleFlags.Any(f => f == flag))
                            .Select(m => m.OutpostModuleInfo.MaxCount)
                            .DefaultIfEmpty(0)
                            .Sum();

                    if (availableModuleCount < pendingCount)
                    {
                        DebugConsole.ThrowError($"Error in outpost generation parameters. Trying to place {pendingCount} modules of the type \"{flag}\", but there aren't enough suitable modules available. You may need to increase the \"max count\" value of some of the modules in the sub editor or decrease the number of modules in the outpost.");
                        for (int i = 0; i < (pendingCount - availableModuleCount); i++)
                        {
                            pendingModules.Remove(moduleCount);
                        }
                    }
                }

                if (pendingModules.None())
                {
                    DebugConsole.ThrowError($"Error when generating an outpost with the generation parameters \"{generationParams.Identifier}\"." +
                        " No modules were chosen for generation. Are the outpost generation parameters missing modules, or should the outpost load from XML instead of being procedurally generated?");
                    return null;
                }

                //the first module is spawned separately, remove it from the list of pending modules
                Identifier initialModuleFlag = pendingModules.FirstOrDefault()?.Identifier ?? "airlock".ToIdentifier();
                pendingModules.RemoveAt(0);

                usedForceOutpostModule = null;
                bool hasForceOutpostWithInitialFlag = GameMain.GameSession?.ForceOutpostModule != null && GameMain.GameSession.ForceOutpostModule.OutpostModuleInfo.ModuleFlags.Contains(initialModuleFlag);
                var initialModule = hasForceOutpostWithInitialFlag ? GameMain.GameSession.ForceOutpostModule : GetRandomModule(outpostModules, initialModuleFlag, locationType);

                if (hasForceOutpostWithInitialFlag)
                {
                    DebugConsole.NewMessage($"Forcing module \"{GameMain.GameSession.ForceOutpostModule.OutpostModuleInfo.Name}\" as the initial module...", Color.Yellow);
                    usedForceOutpostModule = GameMain.GameSession.ForceOutpostModule;
                }

                if (initialModule == null)
                {
                    //reset the forced outpost module so that it won't be used
                    //if we attempt to generate a new outpost later after this failed attempt
                    GameMain.GameSession.ForceOutpostModule = null;
                    throw new Exception("Failed to generate an outpost (no airlock modules found).");
                }

                if (remainingOutpostGenerationTries == 1)
                {
                    //generation has failed and only one attempt left, try removing duplicate modules
                    pendingModules = pendingModules.Distinct().ToList();
                }

                selectedModules.Add(new PlacedModule(moduleIndex: 0, thisModule: initialModule, previousModule: null, thisGapPosition: OutpostModuleInfo.GapPosition.None));
                selectedModules.Last().FulfilledModuleTypes.Add(initialModuleFlag);

                //place modules that can be attached to any position first, and the ones that must be attached to the sides last
                List<OutpostGenerationParams.ModuleCount> pendingModulesAnyPosition =
                    pendingModules.FindAll(m => m.OutpostSideToAttachTo == OutpostModuleInfo.GapPosition.None);
                List<OutpostGenerationParams.ModuleCount> pendingModulesEdges =
                    pendingModules.FindAll(m => m.OutpostSideToAttachTo != OutpostModuleInfo.GapPosition.None);
                System.Diagnostics.Debug.Assert(pendingModulesAnyPosition.Count + pendingModulesEdges.Count == pendingModules.Count);

                if (remainingOutpostGenerationTries == 1 && pendingModulesEdges.Any())
                {
                    //generation has failed and only one attempt left, leave out the modules that must be placed at the edges (they're optional, and can cause overlaps)
                    DebugConsole.AddWarning("Leaving out the modules that must be placed at the edges of the outpost to increase the chances of generating a valid layout. Modules left out: " + string.Join(", ", pendingModulesEdges));
                    pendingModulesEdges.Clear();
                }

                AppendToModule(
                    selectedModules.Last(), outpostModules.ToList(), pendingModulesAnyPosition,
                    selectedModules,
                    locationType,
                    allowExtendBelowInitialModule: generationParams is RuinGeneration.RuinGenerationParams,
                    allowDifferentLocationType: remainingOutpostGenerationTries == 1);

                PlaceModulesToEdges(
                    pendingModulesEdges,
                    selectedModules,
                    availableModules: outpostModules.ToList(),
                    locationType,
                    allowDifferentLocationType: remainingOutpostGenerationTries == 1);

                if (GameMain.GameSession?.ForceOutpostModule != null && 
                    usedForceOutpostModule == null)
                {
                    if (remainingOutpostGenerationTries > 0)
                    {
                        remainingOutpostGenerationTries--;
                        continue;
                    }
                    DebugConsole.ThrowError($"Could not force the outpost module \"{GameMain.GameSession.ForceOutpostModule.OutpostModuleInfo.Name}\" to the outpost. Loading the module as-is...");
                    return null;
                }

                if (GameMain.GameSession != null)
                {
                    GameMain.GameSession.ForceOutpostModule = null;
                }

                //Don't consider the outpost to be invalid if placing some modules to the edges failed.
                //That would be too error-prone, because it's not always guaranteed that modules have enough spare doors/hatches to attach extra modules to any side.
                if (pendingModulesEdges.Any(module => module.Identifier != "none"))
                {
                    DebugConsole.AddWarning("Could not generate an outpost with all of the required modules at the edges. Some modules may not have enough connections at the edges to generate a valid layout. Pending modules: " + string.Join(", ", pendingModulesEdges));
                }
                if (pendingModulesAnyPosition.Any(module => module.Identifier != "none"))
                {
                    if (!allowInvalidOutpost)
                    {
                        remainingOutpostGenerationTries--;
                        if (remainingOutpostGenerationTries > 0)
                        {
                            //tries left -> don't finish generating the outpost, try generating another layout
                            continue;
                        }
                        else
                        {
                            //out of tries, log an error, but let the method continue into loading the outpost (even if it doesn't have all the required modules)
                            DebugConsole.AddSafeError("Could not generate an outpost with all of the required modules. Some modules may not have enough connections at the edges to generate a valid layout. Pending modules: " + string.Join(", ", pendingModulesAnyPosition));
                        }
                    }
                    else
                    {
                        DebugConsole.AddSafeError("Could not generate an outpost with all of the required modules. Some modules may not have enough connections at the edges to generate a valid layout. Pending modules: " + string.Join(", ", pendingModulesAnyPosition) + ". Won't retry because invalid outposts are allowed.");
                    }
                }

                var outpostInfo = new SubmarineInfo()
                {
                    Type = SubmarineType.Outpost
                };
                if (location != null)
                {
                    outpostInfo.DisplayName = location.DisplayName;
                }
                generationFailed = false;
                outpostInfo.OutpostGenerationParams = generationParams;
                sub = new Submarine(outpostInfo, loadEntities: loadEntities);
                sub.Info.OutpostGenerationParams = generationParams;
                if (!generationFailed)
                {
                    foreach (Hull hull in Hull.HullList)
                    {
                        if (hull.Submarine != sub) { continue; }
                        if (string.IsNullOrEmpty(hull.RoomName))
                        {
                            hull.RoomName = hull.CreateRoomName();
                        }
                    }
                    if (Level.IsLoadedOutpost)
                    {
                        location?.RemoveTakenItems();
                    }
                    foreach (WayPoint wp in WayPoint.WayPointList)
                    {
                        if (wp.CurrentHull == null && wp.Submarine == sub)
                        {
                            wp.FindHull();
                        }
                    }
                    EnableFactionSpecificEntities(sub, location);
                    return sub;
                }
                remainingOutpostGenerationTries--;
            }

            DebugConsole.AddSafeError("Failed to generate an outpost with a valid layout and all the required modules. Trying to use a pre-built outpost instead...");
            return null;

            List<MapEntity> loadEntities(Submarine sub)
            {
                sub.OutpostModules.AddRange(selectedModules);
                int idOffset = sub.IdOffset;
                for (int i = 0; i < selectedModules.Count; i++)
                {
                    try
                    {
                        LoadModule(sub, selectedModules[i], i, generationParams, ref idOffset);
                    }
                    catch (Exception e) 
                    {
                        DebugConsole.ThrowError(e.Message);
                        generationFailed = true;
                        return new List<MapEntity>();
                    }
                }

                int maxMoveAmount = Math.Max(2000, selectedModules.Max(m => Math.Max(m.Bounds.Width, m.Bounds.Height)));

                bool overlapsFound = true;
                PlacedModule overlappingModule1, overlappingModule2, moduleBelowAirlock;
                int iteration = 0;
                const int MaxIterations = 20;
                while (overlapsFound)
                {
                    overlapsFound = false;
                    overlappingModule1 = overlappingModule2 = moduleBelowAirlock = null;
                    foreach (PlacedModule placedModule in selectedModules)
                    {
                        if (placedModule.PreviousModule == null) { continue; }

                        List<PlacedModule> subsequentModules = new List<PlacedModule>();
                        GetSubsequentModules(placedModule, selectedModules, ref subsequentModules);
                        List<PlacedModule> otherModules = selectedModules.Except(subsequentModules).ToList();

                        int remainingOverlapPreventionTries = 10;
                        while (FindOverlap(subsequentModules, otherModules, out var module1, out var module2) && remainingOverlapPreventionTries > 0)
                        {
                            overlappingModule1 = module1;
                            overlappingModule2 = module2;
                            overlapsFound = true;
                            if (FindOverlapSolution(subsequentModules, module1, module2, selectedModules, generationParams.MinHallwayLength, maxMoveAmount, out Dictionary<PlacedModule, Vector2> solution))
                            {
                                foreach (KeyValuePair<PlacedModule, Vector2> kvp in solution)
                                {
                                    kvp.Key.Offset += kvp.Value;                                    
                                }
                            }
                            else
                            {
                                break;
                            }
                            remainingOverlapPreventionTries--;
                        }

                        //check that the module doesn't extend below the airlock and potentially overlap with the sub
                        if (generationParams is not RuinGenerationParams &&
                            //only try regenerating once, after that let the modules extend below the airlock
                            //and hopefully resolve it with an extension piece between the sub and the outpost
                            remainingOutpostGenerationTries == MaxOutpostGenerationRetries &&
                            //if the module is horizontally very far, it's ok to expand below the airlock
                            (placedModule.Bounds.X + placedModule.Offset.X < 5000 && placedModule.Bounds.Right + placedModule.Offset.X > -5000) &&
                            ModuleBelowInitialModule(placedModule, selectedModules.First()))
                        {
                            moduleBelowAirlock = placedModule;
                            overlapsFound = true;
                        }
                    }

                    iteration++;
                    if (iteration > MaxIterations)
                    {
#if DEBUG
                        string warningMsg = "Failed to create an outpost layout with no overlaps."; 
                        if (overlappingModule1 != null && overlappingModule2 != null)
                        {
                            warningMsg +=  $" Overlapping modules: {overlappingModule1.Info.Name}, {overlappingModule2.Info.Name}.";
                        }
                        if (moduleBelowAirlock != null)
                        {
                            warningMsg += $" Module below airlock: {moduleBelowAirlock.Info.Name}.";
                        }                       
                        if (remainingOutpostGenerationTries > 0)
                        {
                            warningMsg += " Retrying...";
                        }

                        DebugConsole.AddWarning(warningMsg);
#endif
                        generationFailed = true;
                        break;
                    }
                }

                AttemptToFixTurretPlacement(selectedModules, maxMoveAmount);

                List<MapEntity> allEntities = selectedModules.SelectMany(m => m.Entities).ToList();

                if (!generationFailed)
                {
                    foreach (PlacedModule module in selectedModules)
                    {
                        Vector2 moveAmount = module.Offset + sub.HiddenSubPosition;
                        Submarine.RepositionEntities(moveAmount, module.Entities);
                        module.PositionRelativeToSub += moveAmount;
                    }

                    Gap.UpdateHulls();
                    allEntities.AddRange(GenerateHallways(sub, locationType, selectedModules, outpostModules));
                    LinkOxygenGenerators(allEntities);
                    if (generationParams.LockUnusedDoors)
                    {
                        LockUnusedDoors(selectedModules, generationParams.RemoveUnusedGaps);
                    }
                    if (generationParams.DrawBehindSubs)
                    {
                        foreach (var entity in allEntities)
                        {
                            if (entity is Structure structure)
                            {
                                //eww
                                structure.SpriteDepth = MathHelper.Lerp(0.999f, 0.9999f, structure.SpriteDepth);
#if CLIENT
                                    foreach (var light in structure.Lights)
                                    {
                                        light.IsBackground = true;
                                    }
#endif
                            }
                        }
                    }
                    AlignLadders(selectedModules);
                    if (generationParams.MaxWaterPercentage > 0.0f)
                    {
                        foreach (var entity in allEntities)
                        {
                            if (entity is Hull hull)
                            {
                                float diff = generationParams.MaxWaterPercentage - generationParams.MinWaterPercentage;
                                if (diff < 0.01f)
                                {
                                    // Overfill the hulls to get rid of air pockets in the vertical hallways. Airpockets make it impossible to swim up the hallways.
                                    hull.WaterVolume = hull.Volume * 2;
                                }
                                else
                                {
                                    hull.WaterVolume = hull.Volume * Rand.Range(generationParams.MinWaterPercentage, generationParams.MaxWaterPercentage, Rand.RandSync.ServerAndClient) * 0.01f;
                                }
                            }
                        }
                    }
                }

                return allEntities;
            }
        }

        /// <param name="useGapWithRect">If not null, a gap with this rectangle is chosen as the gap to attach to the previous module. 
        /// We're using the rect instead of e.g. an ID or an index, because otherwise it would be tricky to map the non-instantiated gaps in a pending module to the actual instantiated gaps.</param>
        /// <param name="forcePreviousGap">If not null, the module is forced to attach to this gap.</param>
        public static void LoadModule(Submarine sub, PlacedModule selectedModule, int moduleIndex, OutpostGenerationParams generationParams, ref int idOffset,
            Rectangle? useGapWithRect = null,
            Gap forcePreviousGap = null)
        {
            //set the sub's game version to the module's to get the items/structures in the module to update
            sub.Info.GameVersion = selectedModule.Info.GameVersion;
            List<MapEntity> entities = MapEntity.LoadAll(sub, selectedModule.Info.SubmarineElement, selectedModule.Info.FilePath, idOffset);
            selectedModule.Entities.AddRange(entities);
            //reset afterwards (this generated outpost doesn't really have a version number)
            sub.Info.GameVersion = null;

            selectedModule.InitializeEntities();
            var moduleEntities = selectedModule.Entities;

            foreach (var moduleEntity in moduleEntities)
            {
                switch (moduleEntity)
                {
                    case Gap gap:
                        selectedModule.AllGaps.Add(new PlacedModule.GapInfo(gap));
                        break;
                    case Item it:
                        it.OnItemReplaced = (thisEntity, newEntity) =>
                        {
                            if (selectedModule.Entities.Remove(thisEntity))
                            {
                                selectedModule.Entities.Add(newEntity);
                            }
                        };
                        break;
                }
            }

            if (usedForceOutpostModule != null && usedForceOutpostModule == selectedModule.Info)
            {
                sub.ForcedOutpostModuleWayPoints = moduleEntities.OfType<WayPoint>().ToList();
            }

            idOffset = moduleEntities.Max(e => e.ID) + 1;

            var visibleEntities = moduleEntities.Where(e => e is Structure or Hull || (e is Item { ParentInventory: null } item && item.GetComponent<Wire>() == null));
            var wallEntities = moduleEntities.Where(e => e is Structure s && s.HasBody).Cast<Structure>();
            var hullEntities = moduleEntities.Where(e => e is Hull).Cast<Hull>();

            // Tell the hulls what tags the module has, used to spawn NPCs on specific rooms
            foreach (Hull hull in hullEntities)
            {
                hull.SetModuleTags(selectedModule.Info.OutpostModuleInfo.ModuleFlags);
            }

            if (Screen.Selected is { IsEditor: false })
            {
                foreach (Identifier layer in selectedModule.Info.LayersHiddenByDefault)
                {
                    Submarine.SetLayerEnabled(layer, enabled: false, entities: moduleEntities);
                }
            }

            if (!visibleEntities.Any())
            {
                selectedModule.VisibleEntityBounds = new Rectangle(Point.Zero, Submarine.GridSize.ToPoint());
            }
            else
            {
                Point min = new Point(visibleEntities.Min(static e => e.WorldRect.X), visibleEntities.Min(static e => e.WorldRect.Y - e.WorldRect.Height));
                Point max = new Point(visibleEntities.Max(static e => e.WorldRect.Right), visibleEntities.Max(static e => e.WorldRect.Y));
                selectedModule.VisibleEntityBounds = new Rectangle(min, max - min);

                var minEntityX = visibleEntities.MinBy(e => e.WorldRect.X);
                var minEntityY = visibleEntities.MinBy(e => e.WorldRect.Y - e.WorldRect.Height);
                var maxEntityX = visibleEntities.MaxBy(e => e.WorldRect.Right);
                var maxEntityY = visibleEntities.MaxBy(e => e.WorldRect.Y);
            }

            if (!hullEntities.Any())
            {
                selectedModule.HullBounds = new Rectangle(Point.Zero, Submarine.GridSize.ToPoint());
            }
            else
            {
                Point min = new Point(hullEntities.Min(static e => e.WorldRect.X), hullEntities.Min(static e => e.WorldRect.Y - e.WorldRect.Height));
                Point max = new Point(hullEntities.Max(static e => e.WorldRect.Right), hullEntities.Max(static e => e.WorldRect.Y));
                selectedModule.HullBounds = new Rectangle(min, max - min);
            }

            if (!wallEntities.Any())
            {
                selectedModule.Bounds = new Rectangle(Point.Zero, Submarine.GridSize.ToPoint());
            }
            else
            {
                Point min = new Point(wallEntities.Min(static e => e.WorldRect.X), wallEntities.Min(static e => e.WorldRect.Y - e.WorldRect.Height));
                Point max = new Point(wallEntities.Max(static e => e.WorldRect.Right), wallEntities.Max(static e => e.WorldRect.Y));
                selectedModule.Bounds = new Rectangle(min, max - min);
            }

            if (selectedModule.PreviousModule != null)
            {
                selectedModule.PreviousGap = forcePreviousGap ?? GetGap(selectedModule.PreviousModule.Entities, GetOpposingGapPosition(selectedModule.ThisGapPosition));
                if (selectedModule.PreviousGap == null)
                {
                    throw new Exception($"Error during outpost generation: trying to attach {selectedModule.Info.Name}. {GetOpposingGapPosition(selectedModule.ThisGapPosition)} gap not found in module {selectedModule.PreviousModule.Info.Name}.");
                }
                if (useGapWithRect != null)
                {
                    selectedModule.ThisGap ??= selectedModule.AllGaps.FirstOrDefault(g => g.Rect == useGapWithRect.Value)?.Gap;
                }
                else
                {
                    //use specific gap if defined
                    if (selectedModule.ThisGapId != Entity.NullEntityID)
                    {
                        selectedModule.ThisGap ??=
                            selectedModule.AllGaps.FirstOrDefault(g => g.Gap?.ID == selectedModule.ThisGapId)?.Gap;
                    }
                }

                //backwards compatibility: use the first gap on a specific side (previously we assumed there was always at most one gap per side)
                selectedModule.ThisGap ??= GetGap(moduleEntities, selectedModule.ThisGapPosition);
                if (selectedModule.ThisGap == null)
                {
                    throw new Exception($"Error during outpost generation: trying to attach {selectedModule.Info.Name}. {selectedModule.ThisGapPosition} gap not found in module {selectedModule.Info.Name}.");
                }

                Vector2 moveDir = GetGapDirection(selectedModule.ThisGapPosition);
                selectedModule.Offset =
                    (selectedModule.PreviousGap.WorldPosition + selectedModule.PreviousModule.Offset) -
                    selectedModule.ThisGap.WorldPosition;
                if (generationParams.AlwaysGenerateHallways)
                {
                    if (selectedModule.PreviousGap.ConnectedDoor != null || selectedModule.ThisGap.ConnectedDoor != null)
                    {
                        selectedModule.Offset += moveDir * generationParams.MinHallwayLength;
                    }
                }
            }
        }

        /// <summary>
        /// Select the number and types of the modules to use in the outpost.
        /// </summary>
        private static List<OutpostGenerationParams.ModuleCount> SelectModules(IEnumerable<SubmarineInfo> availableModules, Location location, OutpostGenerationParams generationParams)
        {
            int totalModuleCount = generationParams.TotalModuleCount;
            int totalModuleCountExcludingOptional = totalModuleCount - generationParams.ModuleCounts.Count(m => m.Probability < 1.0f);
            var pendingModules = new List<OutpostGenerationParams.ModuleCount>();
            bool availableModulesFound = true;

            OutpostGenerationParams.ModuleCount initialModule = generationParams.ModuleCounts.FirstOrDefault();
            pendingModules.Add(initialModule);
            while (pendingModules.Count < totalModuleCountExcludingOptional && availableModulesFound)
            {
                availableModulesFound = false;
                foreach (var moduleCount in generationParams.ModuleCounts)
                {
                    float? difficulty = Level.ForcedDifficulty ?? location?.LevelData?.Difficulty;
                    if (difficulty.HasValue)
                    {
                        if (difficulty.Value < moduleCount.MinDifficulty || difficulty.Value > moduleCount.MaxDifficulty)
                        {
                            continue;
                        }
                    }

                    //if this is a module that we're trying to force into the outpost, 
                    //ignore probability and faction requirements
                    if (GameMain.GameSession?.ForceOutpostModule == null ||
                        !GameMain.GameSession.ForceOutpostModule.OutpostModuleInfo.ModuleFlags.Contains(moduleCount.Identifier))
                    {
                        if (moduleCount.Probability < 1.0f &&
                            Rand.Range(0.0f, 1.0f, Rand.RandSync.ServerAndClient) > moduleCount.Probability)
                        {
                            continue;
                        }
                        if (!moduleCount.RequiredFaction.IsEmpty && 
                            location?.Faction?.Prefab.Identifier != moduleCount.RequiredFaction && 
                            location?.SecondaryFaction?.Prefab.Identifier != moduleCount.RequiredFaction)
                        {
                            continue;
                        }
                    }
                    if (pendingModules.Count(m => m == moduleCount) >= moduleCount.Count)
                    {
                        continue;
                    }
                    if (!availableModules.Any(m => m.OutpostModuleInfo.ModuleFlags.Contains(moduleCount.Identifier)))
                    {
                        DebugConsole.ThrowError($"Failed to add a module to the outpost (no modules with the flag \"{moduleCount.Identifier}\" found).");
                        continue;
                    }
                    availableModulesFound = true;
                    pendingModules.Add(moduleCount);
                }
            }
            pendingModules = pendingModules.OrderBy(moduleCount => moduleCount.Order).ThenBy(f => Rand.Value(Rand.RandSync.ServerAndClient)).ToList();
            while (pendingModules.Count < totalModuleCount && generationParams.AppendToReachTotalModuleCount)
            {
                //don't place "none" modules at the end because
                // a. "filler rooms" at the end of a hallway are pointless
                // b. placing the unnecessary filler rooms first give more options for the placement of the more important modules 
                pendingModules.Insert(Rand.Int(pendingModules.Count - 1, Rand.RandSync.ServerAndClient),
                    new OutpostGenerationParams.ModuleCount("none".ToIdentifier(), count: 1));
            }

            //make sure the initial module is inserted first
            pendingModules.Remove(initialModule);
            pendingModules.Insert(0, initialModule);

            if (pendingModules.Count > totalModuleCount)
            {
                DebugConsole.ThrowError($"Error during outpost generation. {pendingModules.Count} modules set to be used the outpost, but total module count is only {totalModuleCount}. Leaving out some of the modules...");
                int removeCount = pendingModules.Count - totalModuleCount;
                for (int i = 0; i < removeCount; i++)
                {
                    pendingModules.Remove(pendingModules.Last());
                }
            }

            return pendingModules;
        }

        /// <summary>
        /// Attaches additional modules to all the available gaps of the given module, 
        /// and continues recursively through the attached modules until all the pending module types have been placed.
        /// </summary>
        /// <param name="currentModule">The module to attach to.</param>
        /// <param name="availableModules">Which modules we can choose from.</param>
        /// <param name="pendingModules">Which types of modules we still need in the outpost.</param>
        /// <param name="selectedModules">The modules we've already selected to be used in the outpost.</param>
        /// <param name="locationType">The type of the location we're generating the outpost for.</param>
        /// <param name="tryReplacingCurrentModule">If we fail to append to the current module, should we try replacing it with something else and see if we can append to it then?</param>
        /// <param name="allowExtendBelowInitialModule">Is the module allowed to be placed further down than the initial module (usually the airlock module)? 
        /// Note that at this point we only determine which module to attach to which, but not the actual positions or bounds of the modules, so it's possible for a module to attach to the side of the airlock but still extend below the airlock if it's very tall for example.</param>
        /// <param name="allowDifferentLocationType">If we fail to find a module suitable for the location type, should we use a module that's meant for a different location type instead?</param>
        private static bool AppendToModule(PlacedModule currentModule,
            List<SubmarineInfo> availableModules,
            List<OutpostGenerationParams.ModuleCount> pendingModules,
            List<PlacedModule> selectedModules, 
            LocationType locationType,
            bool tryReplacingCurrentModule = true,
            bool allowExtendBelowInitialModule = false,
            bool allowDifferentLocationType = false)
        {
            if (pendingModules.Count == 0) { return true; }

            List<PlacedModule> placedModules = new List<PlacedModule>();
            foreach (OutpostModuleInfo.GapPosition gapPosition in GapPositions.Randomize(Rand.RandSync.ServerAndClient))
            {
                if (currentModule.UsedGapPositions.HasFlag(gapPosition)) { continue; }
                if (DisallowBelowAirlock(allowExtendBelowInitialModule, gapPosition, currentModule)) { continue; }

                PlacedModule newModule = null;
                //try appending to the current module if possible
                if (currentModule.Info.OutpostModuleInfo.GapPositions.HasFlag(gapPosition))
                {
                    newModule = AppendModule(currentModule, GetOpposingGapPosition(gapPosition), availableModules, pendingModules, selectedModules, locationType, 
                        allowDifferentLocationType,
                        allowIgnoringAllowAttachTo: false);
                }

                if (newModule != null) 
                { 
                    placedModules.Add(newModule); 
                }
                else
                {
                    //couldn't append to current module, try one of the other placed modules
                    //in two passes: first requiring the "allow attach to" setting to be respected, then ignoring it if nothing suitable can't be found otherwise
                    for (int i = 0; i < 2; i++)
                    {
                        bool allowIgnoringAllowAttachTo = i > 0;
                        foreach (PlacedModule otherModule in selectedModules)
                        {
                            if (otherModule == currentModule) { continue; }
                            foreach (OutpostModuleInfo.GapPosition otherGapPosition in
                                GapPositions.Where(g => !otherModule.UsedGapPositions.HasFlag(g) && otherModule.Info.OutpostModuleInfo.GapPositions.HasFlag(g)))
                            {
                                if (DisallowBelowAirlock(allowExtendBelowInitialModule, otherGapPosition, otherModule)) { continue; }
                                newModule = AppendModule(otherModule, GetOpposingGapPosition(otherGapPosition), availableModules, pendingModules, selectedModules, locationType,
                                    allowDifferentLocationType, allowIgnoringAllowAttachTo: allowIgnoringAllowAttachTo);
                                if (newModule != null)
                                {
                                    placedModules.Add(newModule);
                                    break;
                                }
                            }
                            if (newModule != null) { break; }
                        }
                        if (newModule != null) { break; }
                    }
                }
                if (pendingModules.Count == 0) { return true; }                    
            }            

            //couldn't place a module anywhere, we're probably fucked!
            if (placedModules.Count == 0 && tryReplacingCurrentModule && currentModule.PreviousModule != null && !selectedModules.Any(m => m != currentModule && m.PreviousModule == currentModule))
            {
                //try to replace the previously placed module with something else that we can append to
                for (int i = 0; i < 10; i++)
                {
                    selectedModules.Remove(currentModule);
                    assertAllPreviousModulesPresent();
                    //readd the module types that the previous module was supposed to fulfill to the pending module types
                    pendingModules.AddRange(currentModule.FulfilledModuleCounts);
                    if (!availableModules.Contains(currentModule.Info)) { availableModules.Add(currentModule.Info); }
                    //retry
                    currentModule = AppendModule(currentModule.PreviousModule, currentModule.ThisGapPosition, availableModules, pendingModules, selectedModules, locationType, allowDifferentLocationType: true);
                    assertAllPreviousModulesPresent();
                    if (currentModule == null) { break; }
                    if (AppendToModule(currentModule, availableModules, pendingModules, selectedModules, locationType, tryReplacingCurrentModule: false, allowExtendBelowInitialModule, allowDifferentLocationType))
                    {
                        assertAllPreviousModulesPresent();
                        return true;
                    }
                }
                return false;
            }

            foreach (PlacedModule placedModule in placedModules)
            {
                AppendToModule(placedModule, availableModules, pendingModules, selectedModules, locationType, tryReplacingCurrentModule: true, allowExtendBelowInitialModule, allowDifferentLocationType);
            }
            return placedModules.Count > 0;

            void assertAllPreviousModulesPresent()
            {
                System.Diagnostics.Debug.Assert(selectedModules.All(m => m.PreviousModule == null || selectedModules.Contains(m.PreviousModule)));
            }

            static bool DisallowBelowAirlock(bool allowExtendBelowInitialModule, OutpostModuleInfo.GapPosition gapPosition, PlacedModule currentModule)
            {
                if (!allowExtendBelowInitialModule)
                {
                    //don't continue downwards if it'd extend below the airlock
                    if (gapPosition == OutpostModuleInfo.GapPosition.Bottom && currentModule.Offset.Y <= 1) { return true; }
                }
                return false;
            }
        }

        /// <summary>
        /// Attaches a new random module to one side of the given module
        /// </summary>
        /// <param name="currentModule">The module to attach to</param>
        /// <param name="gapPosition">Which side of the module to attach the new module to</param>
        /// <param name="availableModules">Which modules we can choose from</param>
        /// <param name="pendingModules">Which types of modules we still need in the outpost</param>
        /// <param name="selectedModules">The modules we've already selected to be used in the outpost.</param>
        /// <param name="allowDifferentLocationType">Is it ok to ignore location type restrictions of the modules if no valid module to append can't be found otherwise?</param>
        /// <param name="allowIgnoringAllowAttachTo">Is it ok to ignore "allow attaching to" settings of the modules if no valid module to append can't be found otherwise?</param>
        private static PlacedModule AppendModule(
            PlacedModule currentModule,
            OutpostModuleInfo.GapPosition gapPosition,
            List<SubmarineInfo> availableModules,
            List<OutpostGenerationParams.ModuleCount> pendingModules,
            List<PlacedModule> selectedModules,
            LocationType locationType,
            bool allowDifferentLocationType,
            bool allowIgnoringAllowAttachTo = true)
        {
            if (pendingModules.Count == 0) { return null; }

            Identifier flagToPlace = "none".ToIdentifier();
            SubmarineInfo nextModule = null;
            OutpostGenerationParams.ModuleCount selectedModuleCount = null;

            foreach (var moduleCount in pendingModules
                .OrderByDescending(moduleCount => currentModule?.Info?.OutpostModuleInfo.AllowAttachToModules.Contains(moduleCount.Identifier) ?? false))
            {
                flagToPlace = moduleCount.Identifier;
                nextModule = GetRandomModule(currentModule?.Info?.OutpostModuleInfo, availableModules, flagToPlace, gapPosition, locationType, 
                    allowDifferentLocationType, allowIgnoringAllowAttachTo);
                selectedModuleCount = moduleCount;
                if (nextModule != null) { break; }
            }

            if (nextModule != null)
            {
                var newModule = new PlacedModule(moduleIndex: selectedModules.Count, nextModule, currentModule, gapPosition)
                {
                    Offset = currentModule.Offset + GetGapDirection(gapPosition),
                };
                foreach (Identifier moduleFlag in nextModule.OutpostModuleInfo.ModuleFlags)
                {
                    if (pendingModules.All(moduleCount => moduleCount.Identifier != moduleFlag)) { continue; }
                    if (moduleFlag != "none" || flagToPlace == "none")
                    {
                        newModule.FulfilledModuleCounts.Add(selectedModuleCount);
                        pendingModules.Remove(selectedModuleCount);
                    }
                }
                selectedModules.Add(newModule);
                if (selectedModules.Count(m => m.Info == nextModule) >= nextModule.OutpostModuleInfo.MaxCount)
                {
                    availableModules.Remove(nextModule);
                }
                return newModule;
            }
            return null;
        }

        /// <summary>
        /// Attaches the pending modules to the edges of the outpost. <see cref="OutpostGenerationParams.ModuleCount.OutpostSideToAttachTo"/> must be set to something else than None on the modules. 
        /// </summary>
        /// <param name="pendingModules">The modules that should be attached to the outpost.</param>
        /// <param name="selectedModules">The modules we've already selected to be used in the outpost.</param>
        /// <param name="availableModules">Which modules we can choose from.</param>
        /// <param name="locationType">The type of the location we're generating the outpost for.</param>
        /// <param name="allowDifferentLocationType">If we fail to find a module suitable for the location type, should we use a module that's meant for a different location type instead?</param>
        private static void PlaceModulesToEdges(
            List<OutpostGenerationParams.ModuleCount> pendingModules,
            List<PlacedModule> selectedModules,
            IEnumerable<SubmarineInfo> availableModules,
            LocationType locationType,
            bool allowDifferentLocationType)
        {
            System.Diagnostics.Debug.Assert(pendingModules.All(m => m.OutpostSideToAttachTo != OutpostModuleInfo.GapPosition.None));

            float minX = selectedModules.Min(m => m.Offset.X), maxX = selectedModules.Max(m => m.Offset.X);
            float minY = selectedModules.Min(m => m.Offset.Y), maxY = selectedModules.Max(m => m.Offset.Y);

            for (int i = pendingModules.Count - 1; i >= 0; i--)
            {
                var pendingModule = pendingModules[i];

                PlacedModule moduleToAttachTo = null;
                float bestPriority = 0;
                foreach (var module in selectedModules)
                {
                    float priority = CalculatePriority(module, pendingModule.OutpostSideToAttachTo);
                    //note: priorities of 0 or less mean the module isn't valid and can be skipped
                    if (priority <= 0) { continue; }
                    if (priority > bestPriority ||
                        //some randomness in choosing between modules with the same priority
                        (priority == bestPriority && Rand.Range(0.0f, 1.0f, Rand.RandSync.ServerAndClient) < 0.5f))
                    {
                        bestPriority = priority;
                        moduleToAttachTo = module;
                    }
                }
                if (moduleToAttachTo == null)
                {
                    DebugConsole.AddWarning($"Failed to find a suitable place for the module {pendingModule.Identifier}. No suitable positions to attach to at {pendingModule.OutpostSideToAttachTo}");
                    continue;
                }

                //all sides the module can potentially attach to
                var sidesModuleCanAttachTo = EnumExtensions.GetIndividualFlags(pendingModule.OutpostSideToAttachTo);
                //only the sides the module can actually attach to when attaching to the specific previous module
                var sidesModuleCanAttachToOnPreviousModule = sidesModuleCanAttachTo.Where(g => moduleToAttachTo.Info.OutpostModuleInfo.GapPositions.HasFlag(g));
                //choose a random side to attach to
                var sideToAttachTo = sidesModuleCanAttachToOnPreviousModule.ToList().GetRandom(Rand.RandSync.ServerAndClient);
                var gapPosition = GetOpposingGapPosition(sideToAttachTo);

                if (sideToAttachTo == OutpostModuleInfo.GapPosition.None)
                {
                    DebugConsole.ThrowError($"Failed to determine the side the module {pendingModule.Name} is supposed to attach to on {moduleToAttachTo.Name}.");
                }

                var moduleToAttach = 
                    GetRandomModule(moduleToAttachTo.Info.OutpostModuleInfo, availableModules, pendingModule.Identifier, gapPosition, locationType,
                        allowDifferentLocationType, allowIgnoringAllowAttachTo: false) ??
                    //if the 1st try fails, try again, now allowing ignoring the "allow attach to" setting
                    GetRandomModule(moduleToAttachTo.Info.OutpostModuleInfo, availableModules, pendingModule.Identifier, gapPosition, locationType,
                        allowDifferentLocationType, allowIgnoringAllowAttachTo: true);

                if (moduleToAttach == null)
                {
                    DebugConsole.AddSafeError($"Could not find a module to attach to the side of the outpost. Module type: {pendingModule.Identifier}, module to attach to: {moduleToAttachTo.Info.Name}, side to attach to: {sideToAttachTo}, location type: {locationType}");
                }
                else
                {
                    var newModule = new PlacedModule(moduleIndex: selectedModules.Count, moduleToAttach, moduleToAttachTo, gapPosition);
                    newModule.FulfilledModuleCounts.Add(pendingModule);
                    selectedModules.Add(newModule);
                }
                pendingModules.RemoveAt(i);
            }

            float CalculatePriority(PlacedModule module, OutpostModuleInfo.GapPosition gapPosition)
            {
                float priority = 0;
                bool isHorizontal = false;
                if (gapPosition.HasFlag(OutpostModuleInfo.GapPosition.Left) && 
                    HasSuitableGap(module, OutpostModuleInfo.GapPosition.Left) &&
                    module.Offset.X == minX)
                {
                    priority += 1; 
                    isHorizontal = true;
                }
                if (gapPosition.HasFlag(OutpostModuleInfo.GapPosition.Right) && 
                    HasSuitableGap(module, OutpostModuleInfo.GapPosition.Right) &&
                    module.Offset.X == maxX)
                {
                    priority += 1;
                    isHorizontal = true;
                }
                if (gapPosition.HasFlag(OutpostModuleInfo.GapPosition.Top) && 
                    HasSuitableGap(module, OutpostModuleInfo.GapPosition.Top) &&
                    module.Offset.Y == maxY)
                {
                    priority += 1;
                }
                if (gapPosition.HasFlag(OutpostModuleInfo.GapPosition.Bottom) && 
                    HasSuitableGap(module, OutpostModuleInfo.GapPosition.Bottom) &&
                    module.Offset.Y == minY)
                {
                    //use the horizontal offset as the priority = prefer modules closer to the sides,
                    //to avoid placing things close to the initial module
                    priority += Math.Abs((int)module.Offset.X) + 1;
                }
                if (isHorizontal)
                {
                    //disfavor modules high up in the outpost (causes turrets to be placed low, where they are less likely to get blocked by modules below them)
                    priority -= Math.Min(module.Offset.Y * 0.1f, 0.1f);
                }
                return priority;
            }

            static bool HasSuitableGap(PlacedModule module, OutpostModuleInfo.GapPosition gapPosition)
            {
                return
                    module.Info.OutpostModuleInfo.GapPositions.HasFlag(gapPosition) &&
                    !module.UsedGapPositions.HasFlag(gapPosition);
            }
        }

        /// <summary>
        /// Check if any of the modules in modules1 overlap with modules in modules2
        /// </summary>
        /// <param name="checkOverlapWithAttachedModule">Should the method also check for overlaps between modules attached to each other?</param>
        /// <param name="modulesAlreadyInPlace">See <see cref="ModuleOverlapsWithModuleConnections"/></param>
        public static bool FindOverlap(IEnumerable<PlacedModule> modules1, IEnumerable<PlacedModule> modules2, out PlacedModule module1, out PlacedModule module2, bool checkOverlapWithAttachedModule = true, bool modulesAlreadyInPlace = false)
        {
            module1 = null;
            module2 = null;
            foreach (PlacedModule module in modules1)
            {
                foreach (PlacedModule otherModule in modules2)
                {
                    if (module == otherModule) { continue; }
                    if (!checkOverlapWithAttachedModule)
                    {
                        if (module.PreviousModule == otherModule) { continue; }
                    }
                    if (ModulesOverlap(module, otherModule, modulesAlreadyInPlace))
                    {
                        module1 = module;
                        module2 = otherModule;
                        return true;
                    }
                }
            }
            return false;
        }

        public const float OverlapToleranceBetweenAdjacentModules = 16;

        /// <summary>
        /// Check if the modules overlap, taking their Offsets and MoveOffsets into account
        /// </summary>
        /// <param name="modulesAlreadyInPlace">See <see cref="ModuleOverlapsWithModuleConnections"/></param>
        public static bool ModulesOverlap(PlacedModule module1, PlacedModule module2, bool modulesAlreadyInPlace = false)
        {
            Vector2 GetPosition(PlacedModule m) => (modulesAlreadyInPlace ? m.PositionRelativeToSub : m.Offset) + m.TemporaryMoveOffset;

            Rectangle bounds1 = module1.Bounds;
            bounds1.Location += GetPosition(module1).ToPoint();
            Rectangle bounds2 = module2.Bounds;
            bounds2.Location += GetPosition(module2).ToPoint();

            //more tolerance on adjacent modules to prevent generating an unnecessary, small hallway between them
            if (module1.PreviousModule == module2 || module2.PreviousModule == module1)
            {
                bounds1.Inflate(new Vector2(-OverlapToleranceBetweenAdjacentModules));
                bounds2.Inflate(new Vector2(-OverlapToleranceBetweenAdjacentModules));
            }

            Rectangle hullBounds1 = module1.HullBounds;
            hullBounds1.Location += GetPosition(module1).ToPoint();
            Rectangle hullBounds2 = module2.HullBounds;
            hullBounds2.Location += GetPosition(module2).ToPoint();

            hullBounds1.Inflate(-32, -32);
            hullBounds2.Inflate(-32, -32);

            return hullBounds1.Intersects(hullBounds2) || hullBounds1.Intersects(bounds2) || hullBounds2.Intersects(bounds1);
        }

        /// <summary>
        /// Check if any of the modules overlaps with a connection between 2 other modules
        /// </summary>
        private static bool AnyModuleOverlapsWithModuleConnections(IEnumerable<PlacedModule> modules)
        {
            foreach (PlacedModule module in modules)
            {
                if (ModuleOverlapsWithModuleConnections(module, modules)) { return true; }
            }
            return false;
        }

        /// <summary>
        /// Check if the hallway leading to moduleWithHallway overlaps with any of the other modules.
        /// </summary>
        public static bool AnyModuleOverlapsWithModuleConnections(PlacedModule moduleWithHallway, IEnumerable<PlacedModule> modules, bool modulesAlreadyInPlace = false)
        {
            foreach (PlacedModule otherModule in modules)
            {
                if (ModuleOverlapsWithModuleConnections(otherModule, moduleWithHallway.ToEnumerable(), modulesAlreadyInPlace)) { return true; }
            }
            return false;
        }

        /// <summary>
        /// Check if any of the hallways between the modules in the provided list overlap with the specified module.
        /// </summary>
        /// <param name="modulesAlreadyInPlace">True if the entities of the modules have already been moved to <see cref="PlacedModule.PositionRelativeToSub"/> (when adding modules to an existing outpost),
        /// false if <see cref="PlacedModule.Offset"/> determines where they will be placed (during outpost generation).</param>
        public static bool ModuleOverlapsWithModuleConnections(PlacedModule module, IEnumerable<PlacedModule> modulesWithHallway, bool modulesAlreadyInPlace = false)
        {
            //if the entities are in place, the gaps have already been moved along with the rest of the module
            Vector2 GetGapOffset(PlacedModule m) => (modulesAlreadyInPlace ? Vector2.Zero : m.Offset) + m.TemporaryMoveOffset;

            Rectangle rect = module.Bounds;
            rect.Location += ((modulesAlreadyInPlace ? module.PositionRelativeToSub : module.Offset) + module.TemporaryMoveOffset).ToPoint();
            rect.Y += module.Bounds.Height;

            Vector2? selfGapPos1 = null;
            Vector2? selfGapPos2 = null;
            if (module.PreviousModule != null)
            {
                selfGapPos1 = GetGapOffset(module) + module.ThisGap.Position;
                selfGapPos2 = GetGapOffset(module.PreviousModule) + module.PreviousGap.Position;
            }

            foreach (PlacedModule otherModule in modulesWithHallway)
            {
                if (otherModule == module || otherModule.PreviousModule == null || otherModule.PreviousModule == module) { continue; }

                //cast at both edges of the gap and see if it overlaps with anything
                for (int i = -1; i <= 1; i += 2)
                {
                    Vector2 gapEdgeOffset =
                        otherModule.ThisGap.IsHorizontal ?
                        Vector2.UnitY * otherModule.ThisGap.Rect.Height / 2 * i * 0.9f :
                        Vector2.UnitX * otherModule.ThisGap.Rect.Width / 2 * i * 0.9f;

                    Vector2 gapPos1 = GetGapOffset(otherModule) + otherModule.ThisGap.Position + gapEdgeOffset;
                    Vector2 gapPos2 = GetGapOffset(otherModule.PreviousModule) + otherModule.PreviousGap.Position + gapEdgeOffset;
                    if (Submarine.RectContains(rect, gapPos1) || 
                        Submarine.RectContains(rect, gapPos2) || 
                        MathUtils.GetLineWorldRectangleIntersection(gapPos1, gapPos2, rect, out _))
                    {
                        return true;
                    }

                    //check if the connection overlaps with this module's connection
                    if (selfGapPos1.HasValue && selfGapPos2.HasValue &&
                        !gapPos1.NearlyEquals(gapPos2) && !selfGapPos1.Value.NearlyEquals(selfGapPos2.Value) &&
                        MathUtils.LineSegmentsIntersect(gapPos1, gapPos2, selfGapPos1.Value, selfGapPos2.Value))
                    {
                        return true;
                    }
                }
            }
            
            return false;
        }

        /// <summary>
        /// Check if the lowest point of the module is below the lowest point of the initial (docking) module. 
        /// This shouldn't happen, because it can cause modules to overlap with the docked sub.
        /// </summary>
        /// <param name="modulesAlreadyInPlace">See <see cref="ModuleOverlapsWithModuleConnections"/></param>
        public static bool ModuleBelowInitialModule(PlacedModule module, PlacedModule initialModule, bool modulesAlreadyInPlace = false)
        {
            Rectangle bounds = module.Bounds;
            bounds.Location += ((modulesAlreadyInPlace ? module.PositionRelativeToSub : module.Offset) + module.TemporaryMoveOffset).ToPoint();

            Rectangle initialModuleBounds = initialModule.Bounds;
            initialModuleBounds.Location += ((modulesAlreadyInPlace ? initialModule.PositionRelativeToSub : initialModule.Offset) + initialModule.TemporaryMoveOffset).ToPoint();

            return bounds.Y < initialModuleBounds.Y;
        }

        /// <summary>
        /// Attempt to find a way to move the modules in a way that stops the 2 specific modules from overlapping.
        /// Done by iterating through the modules and testing how much the subsequent modules (i.e. modules that are further from the initial outpost) 
        /// would need to be moved further to solve the overlap. The solution that requires moving the modules the least is chosen.
        /// </summary>
        /// <param name="movableModules">The set of modules the method is allowed to move</param>
        /// <param name="module1">Module overlapping with module2</param>
        /// <param name="module2">Module overlapping with module1</param>
        /// <param name="allmodules">All generated modules</param>
        /// <param name="solution">The solution to the overlap (if any). Key = placed module, value = distance to move the module</param>
        /// <returns>Was a solution found for resolving the overlap.</returns>
        private static bool FindOverlapSolution(
            IEnumerable<PlacedModule> movableModules, 
            PlacedModule module1, PlacedModule module2, 
            IEnumerable<PlacedModule> allmodules, 
            float minMoveAmount,
            int maxMoveAmount,
            out Dictionary<PlacedModule, Vector2> solution)
        {
            solution = new Dictionary<PlacedModule, Vector2>();
            foreach (PlacedModule module in movableModules)
            {
                solution[module] = Vector2.Zero;
            }

            Vector2 shortestMove = new Vector2(float.MaxValue, float.MaxValue);
            bool solutionFound = false;
            foreach (PlacedModule module in movableModules)
            {
                if (module.ThisGap.ConnectedDoor == null && module.PreviousGap.ConnectedDoor == null) { continue; }
                Vector2 moveDir = GetGapDirection(module.ThisGapPosition);
                const float moveStep = 50.0f;
                Vector2 currentMove = moveDir * Math.Max(minMoveAmount, moveStep);

                List<PlacedModule> subsequentModules2 = new List<PlacedModule>();
                GetSubsequentModules(module, movableModules, ref subsequentModules2);
                while (currentMove.LengthSquared() < maxMoveAmount * maxMoveAmount)
                {
                    foreach (PlacedModule movedModule in subsequentModules2)
                    {
                        movedModule.TemporaryMoveOffset = currentMove;
                    }
                    if (!ModulesOverlap(module1, module2) && 
                        !AnyModuleOverlapsWithModuleConnections(allmodules) &&
                        currentMove.LengthSquared() < shortestMove.LengthSquared())
                    {
                        shortestMove = currentMove;
                        foreach (PlacedModule movedModule in allmodules)
                        {
                            solution[movedModule] = subsequentModules2.Contains(movedModule) ? currentMove : Vector2.Zero;
                            solutionFound = true;
                        }
                        break;
                    }
                    currentMove += moveDir * moveStep;
                }
                foreach (PlacedModule movedModule in allmodules)
                {
                    movedModule.TemporaryMoveOffset = Vector2.Zero;
                }
            }

            return solutionFound;
        }

        private static void AttemptToFixTurretPlacement(IEnumerable<PlacedModule> selectedModules, float maxMoveAmount)
        {
            const float MoveStep = 200.0f;

            foreach (PlacedModule turretModule in selectedModules)
            {
                if (turretModule.FulfilledModuleCounts.None(moduleCount => moduleCount.RequireDownwardsVisibility)) { continue; }
                if (turretModule.ThisGapPosition != OutpostModuleInfo.GapPosition.Left && turretModule.ThisGapPosition != OutpostModuleInfo.GapPosition.Right) { continue; }

                float currentMoveAmount = 0.0f;
                Vector2 moveDir = GetGapDirection(turretModule.ThisGapPosition);

                bool overlapFound = false;
                bool maxMoveAmountReached = false;
                //we need to iterate over the modules until there's no overlaps -
                //if we just went through the selectedmodules once, we might end up moving the turret so it doesn't overlap with let's say the last module in the list,
                //causing a new overlap with some already-processed module
                do
                {
                    var selectedModulesExceptTurret = selectedModules.Where(m => m != turretModule);

                    overlapFound = false;
                    foreach (var otherModule in selectedModulesExceptTurret)
                    {
                        Rectangle bounds1 = otherModule.Bounds;
                        bounds1.Location += (otherModule.Offset + otherModule.TemporaryMoveOffset).ToPoint();

                        Vector2 turretModulePosition = turretModule.Offset + turretModule.TemporaryMoveOffset;
                        while (MathUtils.GetLineRectangleIntersection(turretModulePosition, turretModulePosition - Vector2.UnitY * 100000.0f, bounds1, out _))
                        {
                            overlapFound = true;
                            currentMoveAmount += MoveStep;
                            turretModule.TemporaryMoveOffset += moveDir * MoveStep;
                            turretModulePosition = turretModule.Offset + turretModule.TemporaryMoveOffset;
                            if (currentMoveAmount + MoveStep > maxMoveAmount)
                            {
                                maxMoveAmountReached = true;
                                break;
                            }
                        }

                        if (ModuleOverlapsWithModuleConnections(turretModule, selectedModulesExceptTurret) ||
                            selectedModulesExceptTurret.Any(m => ModulesOverlap(turretModule, m)))
                        {
                            //if moving the module would lead to an overlap with some other module or hallway, we can't resolve this
                            DebugConsole.AddWarning($"Failed to place the turret {turretModule} so it isn't above any other module: moving the module further would cause it to overlap with other modules or hallways.");
                            turretModule.TemporaryMoveOffset = Vector2.Zero;
                            break;
                        }
                        //can't move any further -> no point in checking any other modules, we wouldn't be able to do anything about it
                        if (maxMoveAmountReached)
                        {
                            turretModule.TemporaryMoveOffset = Vector2.Zero;
                            break;
                        }
                    }
                } while (overlapFound && !maxMoveAmountReached);

                //apply the offset calculated above
                turretModule.Offset += turretModule.TemporaryMoveOffset;
                turretModule.TemporaryMoveOffset = Vector2.Zero;                
            }
        }

        private static SubmarineInfo GetRandomModule(IEnumerable<SubmarineInfo> modules, Identifier moduleFlag, LocationType locationType)
        {
            IEnumerable<SubmarineInfo> availableModules = null;
            if (moduleFlag.IsEmpty || moduleFlag == "none")
            {
                availableModules = modules.Where(m => !m.OutpostModuleInfo.ModuleFlags.Any() || m.OutpostModuleInfo.ModuleFlags.Contains("none".ToIdentifier()));
            }
            else
            {
                availableModules = modules.Where(m => m.OutpostModuleInfo.ModuleFlags.Contains(moduleFlag));
                if (moduleFlag != "hallwayhorizontal" && moduleFlag != "hallwayvertical")
                {
                    availableModules = availableModules.Where(m => !m.OutpostModuleInfo.ModuleFlags.Contains("hallwayhorizontal".ToIdentifier()) && !m.OutpostModuleInfo.ModuleFlags.Contains("hallwayvertical".ToIdentifier()));
                }
            }

            if (!availableModules.Any()) { return null; }

            //try to search for modules made specifically for this location type first
            var modulesSuitableForLocationType =
                availableModules.Where(m => m.OutpostModuleInfo.IsAllowedInLocationType(locationType) && !m.OutpostModuleInfo.IsAllowedInAnyLocationType());

            //if not found, search for modules suitable for any location type
            if (!modulesSuitableForLocationType.Any())
            {
                modulesSuitableForLocationType = availableModules.Where(m => m.OutpostModuleInfo.IsAllowedInAnyLocationType());
            }

            if (!modulesSuitableForLocationType.Any())
            {
                DebugConsole.NewMessage($"Could not find a suitable module for the location type {locationType}. Module flag: {moduleFlag}.", Color.Orange);
                return ToolBox.SelectWeightedRandom(availableModules.ToList(), availableModules.Select(m => m.OutpostModuleInfo.Commonness).ToList(), Rand.RandSync.ServerAndClient);
            }
            else
            {
                return ToolBox.SelectWeightedRandom(modulesSuitableForLocationType.ToList(), modulesSuitableForLocationType.Select(m => m.OutpostModuleInfo.Commonness).ToList(), Rand.RandSync.ServerAndClient);
            }
        }

        /// <param name="allowDifferentLocationType">Is it ok to ignore location type restrictions of the modules if no suitable module can be found otherwise?</param>
        /// <param name="allowIgnoringAllowAttachTo">Is it ok to ignore "allow attaching to" settings of the modules if no suitable module can be found otherwise?</param>
        private static SubmarineInfo GetRandomModule(OutpostModuleInfo prevModule, IEnumerable<SubmarineInfo> modules, Identifier moduleFlag, OutpostModuleInfo.GapPosition gapPosition, LocationType locationType, 
            bool allowDifferentLocationType,
            bool allowIgnoringAllowAttachTo)
        {
            IEnumerable<SubmarineInfo> modulesWithCorrectFlags = null;
            if (moduleFlag.IsEmpty || moduleFlag.Equals("none"))
            {
                modulesWithCorrectFlags = modules
                    .Where(m => !m.OutpostModuleInfo.ModuleFlags.Any() || (m.OutpostModuleInfo.ModuleFlags.Count() == 1 && m.OutpostModuleInfo.ModuleFlags.Contains("none".ToIdentifier())));
            }
            else
            {
                modulesWithCorrectFlags = modules
                    .Where(m => m.OutpostModuleInfo.ModuleFlags.Contains(moduleFlag));
            }
            modulesWithCorrectFlags = modulesWithCorrectFlags.Where(m => m.OutpostModuleInfo.GapPositions.HasFlag(gapPosition) && m.OutpostModuleInfo.CanAttachToPrevious.HasFlag(gapPosition));

            var suitableModules = GetSuitableModules(modulesWithCorrectFlags, requireAllowAttachToPrevious: true, requireCorrectLocationType: true, requireLocationTypeSpecific: true);
            var suitableModulesForAnyOutpost = GetSuitableModules(modulesWithCorrectFlags, requireAllowAttachToPrevious: true, requireCorrectLocationType: true, requireLocationTypeSpecific: false);
            if (!suitableModules.Any())
            {
                //no suitable module found, see if we can find a "generic" module that's not meant for any specific type of outpost
                suitableModules = suitableModulesForAnyOutpost;
                //still not found, see if we can find something that's otherwise suitable but not meant to attach to the previous module
                if (!suitableModules.Any() && allowIgnoringAllowAttachTo)
                {
                    suitableModules = GetSuitableModules(modulesWithCorrectFlags, requireAllowAttachToPrevious: false, requireCorrectLocationType: true, requireLocationTypeSpecific: true);
                }
                //still not found! Try if we can find a generic module that's not meant to attach to the previous module
                if (!suitableModules.Any() && allowIgnoringAllowAttachTo)
                {
                    suitableModules = GetSuitableModules(modulesWithCorrectFlags, requireAllowAttachToPrevious: false, requireCorrectLocationType: true, requireLocationTypeSpecific: false);
                }
            }

            if (!suitableModules.Any())
            {
                if (allowDifferentLocationType && modulesWithCorrectFlags.Any())
                {
                    DebugConsole.NewMessage($"Could not find a suitable module for the location type {locationType}. Module flag: {moduleFlag}.", Color.Orange);
                    return ToolBox.SelectWeightedRandom(modulesWithCorrectFlags.ToList(), modulesWithCorrectFlags.Select(m => m.OutpostModuleInfo.Commonness).ToList(), Rand.RandSync.ServerAndClient);
                }
                else
                {
                    return null;
                }
            }
            else
            {
                var suitableModule = ToolBox.SelectWeightedRandom(suitableModules.ToList(), suitableModules.Select(m => m.OutpostModuleInfo.Commonness).ToList(), Rand.RandSync.ServerAndClient);
                
                if (GameMain.GameSession?.ForceOutpostModule != null)
                {
                    if (suitableModules.Any(module => module.OutpostModuleInfo.Name == GameMain.GameSession.ForceOutpostModule.OutpostModuleInfo.Name) ||
                        suitableModulesForAnyOutpost.Any(module => module.OutpostModuleInfo.Name == GameMain.GameSession.ForceOutpostModule.OutpostModuleInfo.Name))
                    {
                        var forceOutpostModule = GameMain.GameSession.ForceOutpostModule;
                        System.Diagnostics.Debug.WriteLine($"Inserting Force outpost module in Outpost generation: {forceOutpostModule.OutpostModuleInfo.Name}");
                        GameMain.GameSession.ForceOutpostModule = null;
                        usedForceOutpostModule = forceOutpostModule;
                        return forceOutpostModule;
                    }
                    else if (GameMain.GameSession.ForceOutpostModule.OutpostModuleInfo.ModuleFlags.Contains(moduleFlag))
                    {
                        // if our force module has the same tag as the selected random one, return nothing
                        // because we don't want another module of the same type to be hogging the only spot for that type
                        return null;
                    }
                }

                return suitableModule;
            }

            IEnumerable<SubmarineInfo> GetSuitableModules(IEnumerable<SubmarineInfo> modules, bool requireAllowAttachToPrevious, bool requireCorrectLocationType, bool requireLocationTypeSpecific)
            {
                IEnumerable<SubmarineInfo> suitable = modules;
                if (requireCorrectLocationType)
                {
                    suitable = modules.Where(m => m.OutpostModuleInfo.IsAllowedInLocationType(locationType, requireLocationTypeSpecific: requireLocationTypeSpecific));                    
                }
                if (requireAllowAttachToPrevious && prevModule != null)
                {
                    suitable = suitable.Where(m => CanAttachTo(m.OutpostModuleInfo, prevModule));                    
                }
                return suitable;
            }
        }

        /// <summary>
        /// Get the modules that are further from the initial module than the startModule. StartModule is also included in the list.
        /// </summary>
        public static void GetSubsequentModules(PlacedModule startModule, IEnumerable<PlacedModule> allModules, ref List<PlacedModule> subsequentModules)
        {
            System.Diagnostics.Debug.Assert(!subsequentModules.Contains(startModule));
            subsequentModules.Add(startModule);
            foreach (PlacedModule module in allModules)
            {
                if (module.PreviousModule == startModule)
                {
                    GetSubsequentModules(module, allModules, ref subsequentModules);
                }
            }
        }

        private readonly static OutpostModuleInfo.GapPosition[] GapPositions = new[]
        {
            OutpostModuleInfo.GapPosition.Right,
            OutpostModuleInfo.GapPosition.Left,
            OutpostModuleInfo.GapPosition.Top,
            OutpostModuleInfo.GapPosition.Bottom
        };

        public static OutpostModuleInfo.GapPosition GetOpposingGapPosition(OutpostModuleInfo.GapPosition thisGapPosition)
        {
            return thisGapPosition switch
            {
                OutpostModuleInfo.GapPosition.Right => OutpostModuleInfo.GapPosition.Left,
                OutpostModuleInfo.GapPosition.Left => OutpostModuleInfo.GapPosition.Right,
                OutpostModuleInfo.GapPosition.Bottom => OutpostModuleInfo.GapPosition.Top,
                OutpostModuleInfo.GapPosition.Top => OutpostModuleInfo.GapPosition.Bottom,
                OutpostModuleInfo.GapPosition.None => OutpostModuleInfo.GapPosition.None,
                _ => throw new ArgumentException()
            };
        }

        /// <summary>
        /// Returns the direction from the gap to the module, i.e. -Vector2.UnitX for a gap on the right side of the module.
        /// </summary>
        public static Vector2 GetGapDirection(OutpostModuleInfo.GapPosition thisGapPosition)
        {
            return thisGapPosition switch
            {
                OutpostModuleInfo.GapPosition.Right => -Vector2.UnitX,
                OutpostModuleInfo.GapPosition.Left => Vector2.UnitX,
                OutpostModuleInfo.GapPosition.Bottom => Vector2.UnitY,
                OutpostModuleInfo.GapPosition.Top => -Vector2.UnitY,
                OutpostModuleInfo.GapPosition.None => Vector2.Zero,
                _ => throw new ArgumentException()
            };
        }

        public static Gap GetGap(IEnumerable<MapEntity> entities, OutpostModuleInfo.GapPosition gapPosition)
        {
            Gap selectedGap = null;
            foreach (MapEntity entity in entities)
            {
                if (entity is not Gap gap) { continue; }
                if (gap.ConnectedDoor != null && !gap.ConnectedDoor.UseBetweenOutpostModules) { continue; }
                switch (gapPosition)
                {
                    case OutpostModuleInfo.GapPosition.Right:
                        if (gap.IsHorizontal && (selectedGap == null || gap.WorldPosition.X > selectedGap.WorldPosition.X) &&
                            !entities.Any(e => e is Hull && e.WorldPosition.X > gap.WorldPosition.X && gap.WorldRect.Y - gap.WorldRect.Height <= e.WorldRect.Y && gap.WorldRect.Y >= e.WorldRect.Y - e.WorldRect.Height))
                        {
                            selectedGap = gap;
                        }
                        break;
                    case OutpostModuleInfo.GapPosition.Left:
                        if (gap.IsHorizontal && (selectedGap == null || gap.WorldPosition.X < selectedGap.WorldPosition.X) &&
                            !entities.Any(e => e is Hull && e.WorldPosition.X < gap.WorldPosition.X && gap.WorldRect.Y - gap.WorldRect.Height <= e.WorldRect.Y && gap.WorldRect.Y >= e.WorldRect.Y - e.WorldRect.Height))
                        {
                            selectedGap = gap;
                        }
                        break;
                    case OutpostModuleInfo.GapPosition.Top:
                        if (!gap.IsHorizontal && (selectedGap == null || gap.WorldPosition.Y > selectedGap.WorldPosition.Y) &&
                            !entities.Any(e => e is Hull && e.WorldPosition.Y > gap.WorldPosition.Y && gap.WorldRect.Right >= e.WorldRect.X && gap.WorldRect.X <= e.WorldRect.Right))
                        {
                            selectedGap = gap;
                        }
                        break;
                    case OutpostModuleInfo.GapPosition.Bottom:
                        if (!gap.IsHorizontal && (selectedGap == null || gap.WorldPosition.Y < selectedGap.WorldPosition.Y) &&
                            !entities.Any(e => e is Hull && e.WorldPosition.Y < gap.WorldPosition.Y && gap.WorldRect.Right >= e.WorldRect.X && gap.WorldRect.X <= e.WorldRect.Right))
                        {
                            selectedGap = gap;
                        }
                        break;
                }
            }
            return selectedGap;
        }

        public static OutpostModuleInfo.GapPosition GetGapPosition(Gap gap)
        {
            if (gap.IsRoomToRoom || gap.linkedTo.None()) { return OutpostModuleInfo.GapPosition.None; }
            if (gap.ConnectedDoor != null && !gap.ConnectedDoor.UseBetweenOutpostModules) { return OutpostModuleInfo.GapPosition.None; }
            if (gap.IsHorizontal)
            {
                return gap.WorldPosition.X > gap.linkedTo.First().WorldPosition.X ? OutpostModuleInfo.GapPosition.Right : OutpostModuleInfo.GapPosition.Left;
            }
            else
            {
                return gap.WorldPosition.Y > gap.linkedTo.First().WorldPosition.Y ? OutpostModuleInfo.GapPosition.Top : OutpostModuleInfo.GapPosition.Bottom;
            }
        }

        private static bool CanAttachTo(OutpostModuleInfo from, OutpostModuleInfo to)
        {
            if (!from.AllowAttachToModules.Any() || from.AllowAttachToModules.All(s => s == "any")) { return true; }                
            return from.AllowAttachToModules.Any(s => to.ModuleFlags.Contains(s));
        }

        public static List<MapEntity> GenerateHallway(Submarine sub, LocationType locationType, PlacedModule placedModule, OutpostGenerationParams generationParams)
        {
            return GenerateHallways(sub, locationType, placedModule.ToEnumerable(), GetAvailableModules(generationParams, GetAvailableModuleFiles()));
        }

        private static List<MapEntity> GenerateHallways(Submarine sub, LocationType locationType, IEnumerable<PlacedModule> placedModules, IEnumerable<SubmarineInfo> availableModules)
        {
            //if a hallway is shorter than this, one of the doors at the ends of the hallway is removed
            const float MinTwoDoorHallwayLength = 32.0f;

            var waypoints = GetAllWaypoints().ToList();

            List<MapEntity> placedEntities = new List<MapEntity>();
            foreach (PlacedModule module in placedModules)
            {
                if (module.PreviousModule == null) { continue; }
                
                var thisJunctionBox = Powered.PoweredList.FirstOrDefault(p => p is PowerTransfer pt && IsLinked(module.ThisGap, pt))?.Item?.GetComponent<ConnectionPanel>();
                var previousJunctionBox = Powered.PoweredList.FirstOrDefault(p => p is PowerTransfer pt && IsLinked(module.PreviousGap, pt))?.Item?.GetComponent<ConnectionPanel>();

                static bool IsLinked(Gap gap, PowerTransfer junctionBox)
                {
                    if (junctionBox.Item.linkedTo.Contains(gap)) { return true; }
                    if (gap.ConnectedDoor != null && junctionBox.Item.linkedTo.Contains(gap.ConnectedDoor.Item)) { return true; }
                    if (gap.linkedTo.Contains(junctionBox.Item)) { return true; }
                    if (gap.ConnectedDoor != null && gap.ConnectedDoor.Item.linkedTo.Contains(junctionBox.Item)) { return true; }
                    return false;
                }

                if (thisJunctionBox != null && previousJunctionBox != null)
                {
                    for (int i = 0; i < thisJunctionBox.Connections.Count && i < previousJunctionBox.Connections.Count; i++)
                    {
                        var wirePrefab = MapEntityPrefab.FindByIdentifier((thisJunctionBox.Connections[i].IsPower ? "redwire" : "bluewire").ToIdentifier()) as ItemPrefab;
                        var wire = new Item(wirePrefab, thisJunctionBox.Item.Position, sub).GetComponent<Wire>();

                        if (!thisJunctionBox.Connections[i].TryAddLink(wire))
                        {
                            DebugConsole.AddWarning($"Failed to connect junction boxes between outpost modules (not enough free connections in module \"{module.Info.Name}\")");
                            continue;
                        }
                        if (!previousJunctionBox.Connections[i].TryAddLink(wire))
                        {
                            DebugConsole.AddWarning($"Failed to connect junction boxes between outpost modules (not enough free connections in module \"{module.PreviousModule.Info.Name}\")");
                            continue;
                        }
                        wire.TryConnect(thisJunctionBox.Connections[i], addNode: false);
                        wire.TryConnect(previousJunctionBox.Connections[i], addNode: false);
                        wire.SetNodes(new List<Vector2>());
                        module.HallwayEntities.Add(wire.Item);
                    }
                }

                bool isHorizontal =
                    module.ThisGapPosition == OutpostModuleInfo.GapPosition.Left ||
                    module.ThisGapPosition == OutpostModuleInfo.GapPosition.Right;

                if (!module.ThisGap.linkedTo.Any())
                {
                    DebugConsole.ThrowError($"Error during outpost generation: {module.ThisGapPosition} gap in module \"{module.Info.Name}\" was not linked to any hulls.");
                    continue;
                }
                if (!module.PreviousGap.linkedTo.Any())
                {
                    DebugConsole.ThrowError($"Error during outpost generation: {GetOpposingGapPosition(module.ThisGapPosition)} gap in module \"{module.PreviousModule.Info.Name}\" was not linked to any hulls.");
                    continue;
                }

                MapEntity leftHull = module.ThisGap.Position.X < module.PreviousGap.Position.X ? module.ThisGap.linkedTo[0] : module.PreviousGap.linkedTo[0];
                MapEntity rightHull = module.ThisGap.Position.X > module.PreviousGap.Position.X ?
                    module.ThisGap.linkedTo.Count == 1 ? module.ThisGap.linkedTo[0] : module.ThisGap.linkedTo[1] :
                    module.PreviousGap.linkedTo.Count == 1 ? module.PreviousGap.linkedTo[0] : module.PreviousGap.linkedTo[1];
                MapEntity topHull = module.ThisGap.Position.Y > module.PreviousGap.Position.Y ? module.ThisGap.linkedTo[0] : module.PreviousGap.linkedTo[0];
                MapEntity bottomHull = module.ThisGap.Position.Y < module.PreviousGap.Position.Y ?
                    module.ThisGap.linkedTo.Count == 1 ? module.ThisGap.linkedTo[0] : module.ThisGap.linkedTo[1] :
                    module.PreviousGap.linkedTo.Count == 1 ? module.PreviousGap.linkedTo[0] : module.PreviousGap.linkedTo[1];

                float hallwayLength = isHorizontal ?
                    rightHull.WorldRect.X - leftHull.WorldRect.Right : 
                    topHull.WorldRect.Y - topHull.RectHeight - bottomHull.WorldRect.Y;

                bool thisGapInUse = module.ThisGap != null && module.ThisGap.ConnectedDoor == null;
                //gap in use -> disable linked entities that are marked to be disabled
                SetOverlappingWallsDisabled(module.Entities, module.ThisGap, disabled: thisGapInUse);

                bool previousGapInUse = module.PreviousGap != null && module.PreviousGap.ConnectedDoor == null;
                //gap in use -> disable linked entities that are marked to be disabled
                SetOverlappingWallsDisabled(module.PreviousModule.Entities, module.PreviousGap, disabled: previousGapInUse);

                bool generateHallway = hallwayLength > MinTwoDoorHallwayLength;

                if (module.ThisGap != null && module.PreviousGap != null)
                {
                    var gapToDisable = module.ThisGap.ConnectedDoor == null ? module.ThisGap : module.PreviousGap;
                    var gapToUse = gapToDisable == module.ThisGap ? module.PreviousGap : module.ThisGap;

                    //if the hallway is very short, remove one of the doors
                    bool disable = hallwayLength <= MinTwoDoorHallwayLength;
                    gapToDisable.ConnectedDoor?.Item.linkedTo.ForEachMod(lt => (lt as Structure)?.SetDisabled(disable));
                    //enable ladders
                    gapToDisable.ConnectedDoor?.Item.linkedTo.ForEachMod(lt => 
                    {
                        if (lt is Item { IsLadder: true } ladder)
                        {
                            ladder.SetDisabled(false);
                            //the ladder's waypoints may have been disabled along with the ladder (see LockUnusedDoors), re-enable them too
                            var ladderComponent = ladder.GetComponent<Ladder>();
                            waypoints.Where(wp => wp.Ladders == ladderComponent).ForEach(wp => wp.SetDisabled(false));
                        }
                    });

                    if (gapToDisable.ConnectedDoor?.Item.Connections != null)
                    {
                        foreach (Connection c in gapToDisable.ConnectedDoor.Item.Connections)
                        {
                            c.Wires.Where(w => w != null).ForEach(w => w.Item.SetDisabled(disable));
                        }
                    }

                    WayPoint waypointToDisable = waypoints.Find(wp => wp.ConnectedGap == gapToDisable);
                    WayPoint waypointToUse = waypoints.Find(wp => wp.ConnectedGap == gapToUse);
                    if (waypointToDisable != null && waypointToUse != null)
                    {
                        if (!generateHallway)
                        {
                            //no hallway (or waypoints in there) will be generated, just connect the modules' waypoints directly
                            //remove one of the waypoints, move links to the other waypoint
                            foreach (MapEntity me in waypointToDisable.linkedTo)
                            {
                                if (me is WayPoint wayPoint && !waypointToUse.linkedTo.Contains(wayPoint) && wayPoint != waypointToUse)
                                {
                                    waypointToUse.ConnectTo(wayPoint);
                                }
                            }
                            waypointToDisable.SetDisabled(disable);
                        }
                    }
                    else
                    {
                        string errorMsg = $"Failed to connect waypoints between outpost modules {module.Info.Name} and {module.PreviousModule.Info.Name}.";
                        if ((gapToDisable == module.ThisGap && waypointToDisable == null) ||
                            (gapToUse == module.ThisGap && waypointToUse == null))
                        {
                            DebugConsole.ThrowError(errorMsg + $" No waypoint in the {module.ThisGapPosition.ToString().ToLower()} gap of the module \"{module.Info.Name}\".");
                        }
                        if ((gapToDisable == module.PreviousGap && waypointToDisable == null) ||
                            (gapToUse == module.PreviousGap && waypointToUse == null))
                        {
                            DebugConsole.ThrowError(errorMsg + $" No waypoint in the {GetOpposingGapPosition(module.ThisGapPosition).ToString().ToLower()} gap of the module \"{module.PreviousModule.Info.Name}\".");
                        }
                    }

                    gapToDisable.ConnectedDoor?.Item.SetDisabled(disable);                    
                    if (hallwayLength <= 1.0f)
                    {
                        gapToDisable.SetDisabled(disable);
                    }                    
                }

                if (hallwayLength <= 1.0f) { continue; }

                Identifier moduleFlag = (isHorizontal ? "hallwayhorizontal" : "hallwayvertical").ToIdentifier();
                var hallwayModules = availableModules.Where(m => m.OutpostModuleInfo.ModuleFlags.Contains(moduleFlag));

                var suitableHallwayModules = hallwayModules.Where(m =>
                         m.OutpostModuleInfo.AllowAttachToModules.Any(s => module.Info.OutpostModuleInfo.ModuleFlags.Contains(s)) &&
                         m.OutpostModuleInfo.AllowAttachToModules.Any(s => module.PreviousModule.Info.OutpostModuleInfo.ModuleFlags.Contains(s)));
                if (suitableHallwayModules.None())
                {
                    suitableHallwayModules = hallwayModules.Where(m =>
                                        !m.OutpostModuleInfo.AllowAttachToModules.Any() ||
                                        m.OutpostModuleInfo.AllowAttachToModules.All(s => s == "any"));
                }

                var hallwayInfo = GetRandomModule(suitableHallwayModules, moduleFlag, locationType);
                if (hallwayInfo == null)
                {
                    DebugConsole.ThrowError($"Generating hallways between outpost modules failed. No {(isHorizontal ? "horizontal" : "vertical")} hallway modules suitable for use between the modules \"{module.Info.DisplayName}\" and \"{module.PreviousModule.Info.DisplayName}\".");
                    return placedEntities;
                }

                var hallwayEntities = MapEntity.LoadAll(sub, hallwayInfo.SubmarineElement, hallwayInfo.FilePath, -1);

                //remove items that don't fit in the hallway
                foreach (var entityToRemove in hallwayEntities.FindAll(e => e is Item item && item.GetComponent<Door>() == null && (isHorizontal ? e.Rect.Width : e.Rect.Height) > hallwayLength))
                {
                    entityToRemove.Remove();
                    hallwayEntities.Remove(entityToRemove);
                }

                //find the largest hull to use it as the center point of the hallway
                //and the bounds of all the hulls, used when resizing the hallway to fit between the modules
                Vector2 hullCenter = Vector2.Zero;
                Rectangle hullBounds = Rectangle.Empty;
                float largestHullVolume = 0.0f;
                foreach (MapEntity me in hallwayEntities)
                {
                    if (me is Hull hull)
                    {
                        if (hull.Volume > largestHullVolume)
                        {
                            largestHullVolume = hull.Volume;
                            hullCenter = hull.WorldPosition;
                        }
                        hullBounds = new Rectangle(
                            Math.Min(hullBounds.X, me.WorldRect.X),
                            Math.Min(hullBounds.Y, me.WorldRect.Y - me.WorldRect.Height),
                            Math.Max(hullBounds.Width, me.WorldRect.Right),
                            Math.Max(hullBounds.Height, me.WorldRect.Y));
                    }
                }
                hullBounds.Width -= hullBounds.X;
                hullBounds.Height -= hullBounds.Y;

                float scaleFactor = isHorizontal ?
                    hallwayLength / (float)hullBounds.Width :
                    hallwayLength / (float)hullBounds.Height;
                System.Diagnostics.Debug.Assert(scaleFactor > 0.0f);

                placedEntities.AddRange(hallwayEntities);
                MapEntity.InitializeLoadedLinks(hallwayEntities);
                Vector2 moveAmount = (module.ThisGap.Position + module.PreviousGap.Position) / 2 - hullCenter;
                Submarine.RepositionEntities(moveAmount, hallwayEntities);
                hullBounds.Location += moveAmount.ToPoint();

                Debug.Assert(hallwayEntities.Distinct().Count() == hallwayEntities.Count, "Duplicate entities present in the list of hallway entities.");
                module.HallwayEntities.AddRange(hallwayEntities);

                //resize/reposition entities to make the hallway fit between the modules
                foreach (MapEntity me in hallwayEntities)
                {
                    if (me is Hull)
                    {
                        if (hallwayLength <= MinTwoDoorHallwayLength)
                        {
                            //if the hallway is very short, stretch the hulls in adjacent modules and remove the hull in between
                            if (isHorizontal)
                            {
                                int midX = (leftHull.Rect.Right + rightHull.Rect.X) / 2;
                                leftHull.Rect = new Rectangle(leftHull.Rect.X, leftHull.Rect.Y, midX - leftHull.Rect.X, leftHull.Rect.Height);
                                rightHull.Rect = new Rectangle(midX, rightHull.Rect.Y, rightHull.Rect.Right - midX, rightHull.Rect.Height);
                            }
                            else
                            {
                                int midY = (topHull.Rect.Y - topHull.Rect.Height + bottomHull.Rect.Y) / 2;
                                topHull.Rect = new Rectangle(topHull.Rect.X, topHull.Rect.Y, topHull.Rect.Width, topHull.Rect.Y - midY);
                                bottomHull.Rect = new Rectangle(bottomHull.Rect.X, midY, bottomHull.Rect.Width, midY - (bottomHull.Rect.Y - bottomHull.Rect.Height));
                            }
                            me.Remove();
                        }
                        else
                        {
                            if (isHorizontal)
                            {
                                //extend from the right edge of the hull on the left to the left edge of the hull on the right
                                me.Rect = new Rectangle(leftHull.Rect.Right, me.Rect.Y, rightHull.Rect.X - leftHull.Rect.Right, me.Rect.Height);
                            }
                            else
                            {
                                //extend from the top of the hull below to the bottom of the hull above
                                me.Rect = new Rectangle(me.Rect.X, topHull.Rect.Y - topHull.Rect.Height, me.Rect.Width, topHull.Rect.Y - topHull.Rect.Height - bottomHull.Rect.Y);
                            }
                        }
                    }
                    else if (me is Structure || (me is Item item && item.GetComponent<Door>() == null))
                    {
                        if (isHorizontal)
                        {
                            if (!me.ResizeHorizontal)
                            {
                                int xPos = (int)(leftHull.WorldRect.Right + (me.WorldPosition.X - hullBounds.X) * scaleFactor);
                                me.Rect = new Rectangle(xPos - me.RectWidth / 2, me.Rect.Y, me.Rect.Width, me.Rect.Height);
                            }
                            else
                            {
                                int minX = (int)(leftHull.WorldRect.Right + (me.WorldRect.X - hullBounds.X) * scaleFactor);
                                int maxX = (int)(leftHull.WorldRect.Right + (me.WorldRect.Right - hullBounds.X) * scaleFactor);
                                me.Rect = new Rectangle(minX, me.Rect.Y, Math.Max(maxX - minX, 16), me.Rect.Height);
                            }
                        }
                        else
                        {
                            if (!me.ResizeVertical)
                            {
                                int yPos = (int)(topHull.WorldRect.Y - topHull.RectHeight + (me.WorldPosition.Y - hullBounds.Bottom) * scaleFactor);
                                me.Rect = new Rectangle(me.Rect.X, yPos + me.RectHeight / 2, me.Rect.Width, me.Rect.Height);
                            }
                            else
                            {
                                int minY = (int)(bottomHull.WorldRect.Y + (me.WorldRect.Y - me.RectHeight - hullBounds.Y) * scaleFactor);
                                int maxY = (int)(bottomHull.WorldRect.Y + (me.WorldRect.Y - hullBounds.Y) * scaleFactor);
                                me.Rect = new Rectangle(me.Rect.X, maxY, me.Rect.Width, Math.Max(maxY - minY, 16));
                            }
                        }
                    }
                }

                if (generateHallway)
                {
                    //connect waypoints
                    var startWaypoint = waypoints.Find(wp => wp.ConnectedGap == module.ThisGap);
                    if (startWaypoint == null)
                    {
                        DebugConsole.ThrowError($"Failed to connect waypoints between outpost modules. No waypoint in the {module.ThisGapPosition.ToString().ToLower()} gap of the module \"{module.Info.Name}\".");
                        continue;
                    }
                    var endWaypoint = waypoints.Find(wp => wp.ConnectedGap == module.PreviousGap);
                    if (endWaypoint == null)
                    {
                        DebugConsole.ThrowError($"Failed to connect waypoints between outpost modules. No waypoint in the {GetOpposingGapPosition(module.ThisGapPosition).ToString().ToLower()} gap of the module \"{module.PreviousModule.Info.Name}\".");
                        continue;
                    }

                    if (startWaypoint.WorldPosition.X > endWaypoint.WorldPosition.X)
                    {
                        (endWaypoint, startWaypoint) = (startWaypoint, endWaypoint);
                    }

                    startWaypoint.SetDisabled(false);
                    endWaypoint.SetDisabled(false);

                    //if the hallway is longer than 100 pixels, generate some waypoints inside it and make sure 
                    //for vertical hallways this isn't necessarily, it's done as a part of the ladder generation in AlignLadders
                    const float distanceBetweenWaypoints = 100.0f;
                    if (hallwayLength > distanceBetweenWaypoints)
                    {
                        WayPoint firstNewWayPoint = null;
                        WayPoint lastNewWayPoint = null;
                        if (isHorizontal)
                        {
                            WayPoint prevWayPoint = startWaypoint;
                            for (float x = leftHull.Rect.Right + distanceBetweenWaypoints / 2; x < rightHull.Rect.X - distanceBetweenWaypoints / 2; x += distanceBetweenWaypoints)
                            {
                                var newWayPoint = new WayPoint(new Vector2(x, hullBounds.Y + 110.0f), SpawnType.Path, sub);
                                firstNewWayPoint ??= newWayPoint;
                                prevWayPoint.ConnectTo(newWayPoint);
                                prevWayPoint = newWayPoint;
                                lastNewWayPoint = newWayPoint;
                                module.HallwayEntities.Add(newWayPoint);
                            }
                            firstNewWayPoint?.ConnectTo(startWaypoint);
                            lastNewWayPoint?.ConnectTo(endWaypoint);
                        }
                        else if (startWaypoint.Ladders == null)
                        {
                            WayPoint prevWayPoint = startWaypoint;
                            float bottom = bottomHull.Rect.Y;
                            float top = topHull.Rect.Y - topHull.Rect.Height;
                            for (float y = bottom + distanceBetweenWaypoints; y < top - distanceBetweenWaypoints; y += distanceBetweenWaypoints)
                            {
                                var newWayPoint = new WayPoint(new Vector2(startWaypoint.Position.X, y), SpawnType.Path, sub);
                                firstNewWayPoint ??= newWayPoint; 
                                prevWayPoint.ConnectTo(newWayPoint);
                                prevWayPoint = newWayPoint;
                                lastNewWayPoint = newWayPoint;
                                module.HallwayEntities.Add(newWayPoint);
                            }
                            firstNewWayPoint?.ConnectTo(startWaypoint);
                            lastNewWayPoint?.ConnectTo(endWaypoint);
                        }
                        else
                        {
                            //creating new ladder waypoints are handled in AlignLadders
                            continue;
                        }
                    }
                    else
                    {
                        startWaypoint.ConnectTo(endWaypoint);
                    }
                    //ensure the waypoint at the end is no longer directly connected to waypoints at the other side of the hallway (we want to use the waypoints generated through the hallway instead)
                    RemoveBlockedConnections(startWaypoint, endWaypoint);
                    //same the other way around
                    RemoveBlockedConnections(endWaypoint, startWaypoint);
                }

                module.HallwayEntityIds = module.HallwayEntities.Select(e => e.ID).ToImmutableArray();
            }
            return placedEntities;
        }

        private static void RemoveBlockedConnections(WayPoint startWaypoint, WayPoint endWaypoint)
        {
            foreach (MapEntity linkedEntity in endWaypoint.linkedTo.ToList())
            {
                if (linkedEntity is WayPoint waypoint && waypoint != startWaypoint &&
                    //check there's no walls or doors between the waypoints
                    Submarine.PickBody(endWaypoint.SimPosition, waypoint.SimPosition,
                        customPredicate: (Fixture f) => { return f != endWaypoint.ConnectedDoor?.OutsideSubmarineFixture && f.Body != endWaypoint.ConnectedDoor?.Body.FarseerBody; },
                        collisionCategory: Physics.CollisionWall) is Body body && body.UserData is not Gap)
                {

                    endWaypoint.RemoveConnectionTo(waypoint);
                }
            }
        }

        public static void LinkOxygenGenerators(IEnumerable<MapEntity> entities, bool removeExistingLinks = false)
        {
            List<OxygenGenerator> oxygenGenerators = [];
            List<Vent> vents = [];
            foreach (MapEntity e in entities)
            {
                if (e is Item item)
                {
                    var oxygenGenerator = item.GetComponent<OxygenGenerator>();
                    if (oxygenGenerator != null) { oxygenGenerators.Add(oxygenGenerator); }
                    var vent = item.GetComponent<Vent>();
                    if (vent != null) { vents.Add(vent); }
                }
            }

            //remove any existing links to oxygen generators
            if (removeExistingLinks)
            {
                foreach (var oxygenGenerator in oxygenGenerators)
                {
                    oxygenGenerator.Item.linkedTo.RemoveAll(linked => linked is Item linkedItem && linkedItem.GetComponent<Vent>() != null);
                }
            }

            //link every vent to the closest oxygen generator
            foreach (Vent vent in vents)
            {
                OxygenGenerator closestOxygenGenerator = null;
                float closestDist = float.MaxValue;
                foreach (OxygenGenerator oxygenGenerator in oxygenGenerators)
                {
                    float dist = Vector2.DistanceSquared(oxygenGenerator.Item.WorldPosition, vent.Item.WorldPosition);
                    if (dist < closestDist)
                    {
                        closestOxygenGenerator = oxygenGenerator;
                        closestDist = dist;
                    }
                }
                if (closestOxygenGenerator != null && !closestOxygenGenerator.Item.linkedTo.Contains(vent.Item))
                {
                    closestOxygenGenerator.Item.linkedTo.Add(vent.Item);
                }
            }
        }

        private static void EnableFactionSpecificEntities(Submarine sub, Location location)
        {
            sub.EnableFactionSpecificEntities(location?.Faction?.Prefab.Identifier ?? Identifier.Empty);        
        }

        public static void LockUnusedDoors(IEnumerable<PlacedModule> placedModules, bool removeUnusedGaps)
        {
            var waypoints = GetAllWaypoints().ToList();
            foreach (PlacedModule module in placedModules)
            {
                foreach (MapEntity me in module.Entities)
                {
                    if (me is not Gap gap) { continue; }
                    var door = gap.ConnectedDoor;
                    if (door != null && !door.UseBetweenOutpostModules) { continue; }

                    //door or gap between modules (in use)
                    if (IsGapInUse(gap, placedModules))
                    {
                        //gap in use -> disable linked entities that are marked to be removed
                        if (gap.ConnectedDoor == null)
                        {
                            SetOverlappingWallsDisabled(module.Entities, gap, disabled: true);
                        }
                        door?.Item.linkedTo
                            .Where(lt => ShouldDisableLinkedEntity(lt, doorInUse: true, module: module, placedModules: placedModules))
                            .ForEachMod(lt => SetLinkedEntityDisabled(lt, module, disabled: true));
                    }
                    //door or gap not between modules (not used, should be locked/disabled)
                    else
                    {
                        //docking port -> don't disable
                        if (door != null && DockingPort.List.Any(d => Submarine.RectContains(d.Item.WorldRect, door.Item.WorldPosition))) { continue; }

                        //between two hulls of the same module -> don't disable
                        if (gap.linkedTo.Count == 2 && 
                            module.Entities.Contains(gap.linkedTo[0]) &&
                            module.Entities.Contains(gap.linkedTo[1]))
                        {
                            continue;
                        }
                        if (door != null)
                        {
                            if (door.Item.linkedTo.Any(lt => lt is Structure))
                            {
                                foreach (var entity in door.Item.linkedTo)
                                {
                                    SetLinkedEntityDisabled(entity, module, disabled: ShouldDisableLinkedEntity(entity, doorInUse: false, module: module, placedModules: placedModules));
                                }
                                waypoints.Where(wp => wp.ConnectedDoor == door).ForEachMod(lt => SetLinkedEntityDisabled(lt, module, disabled: true));
                                SetLinkedEntityDisabled(door.Item, module, disabled: true);
                                continue;
                            }
                            else
                            {
                                door.Stuck = 100.0f;
                                door.Item.NonInteractable = true;
                                var connectionPanel = door.Item.GetComponent<ConnectionPanel>();
                                if (connectionPanel != null) { connectionPanel.TemporarilyLocked = true; }
                            }
                        }
                        else if (removeUnusedGaps)
                        {
                            SetLinkedEntityDisabled(gap, module, disabled: true);
                            waypoints.Where(wp => wp.ConnectedGap == gap).ForEachMod(lt => SetLinkedEntityDisabled(lt, module, disabled: true));
                        }
                    }
                }
            }

            static bool IsGapInUse(Gap gap, IEnumerable<PlacedModule> placedModules)
            {
                if (gap == null) { return false; }
                return placedModules.Any(m => m.PreviousGap == gap || m.ThisGap == gap);
            }

            static bool ShouldDisableLinkedEntity(MapEntity e, bool doorInUse, PlacedModule module, IEnumerable<PlacedModule> placedModules)
            {
                if (e is Item { IsLadder: true } ladderItem)
                {
                    var linkedDoors = Door.DoorList.Where(otherDoor => otherDoor.Item.linkedTo.Contains(ladderItem)).ToList();
                    if (linkedDoors.Count > 1)
                    {
                        if (ladderItem.RemoveIfLinkedOutpostDoorInUse)
                        {
                            //if there's multiple doors linked to the ladder, never remove it
                            //(the ladder is presumably not just for moving between two modules in that case, but might e.g. go through the whole module)
                            return false;
                        }
                        //ladder explicitly set to be removed when the door is not in use:
                        //keep it if any of the linked doors is in use, remove only if none of them are
                        return linkedDoors.None(linkedDoor => IsGapInUse(linkedDoor.LinkedGap, placedModules));
                    }
                    return ladderItem.RemoveIfLinkedOutpostDoorInUse == doorInUse;
                }

                if (e is Structure structure)
                {
                    return structure.RemoveIfLinkedOutpostDoorInUse == doorInUse;
                }
                else if (e is Item item)
                {
                    if (item.GetComponent<PowerTransfer>() != null) { return false; }
                    return item.RemoveIfLinkedOutpostDoorInUse == doorInUse;
                }
                return false;
            }

            void SetLinkedEntityDisabled(MapEntity linkedEntity, PlacedModule module, bool disabled)
            {
                Gap gap = linkedEntity as Gap;
                if (linkedEntity is Item linkedItem)
                {
                    if (linkedItem.Connections != null)
                    {
                        foreach (Connection connection in linkedItem.Connections)
                        {
                            foreach (Wire w in connection.Wires.ToArray())
                            {
                                w?.Item.SetDisabled(disabled);
                            }
                        }
                    }
                    //if we end up disabling a ladder, disable its waypoints too
                    if (linkedItem.GetComponent<Ladder>() is Ladder ladder)
                    {
                        var ladderWaypoints = waypoints.FindAll(wp => wp.Ladders == ladder);
                        foreach (var ladderWaypoint in ladderWaypoints)
                        {
                            //got through all waypoints linked to the ladder waypoints, and link them together
                            //so we don't end up breaking up any paths by removing the ladder waypoints
                            for (int i = 0; i < ladderWaypoint.linkedTo.Count; i++)
                            {
                                if (ladderWaypoint.linkedTo[i] is not WayPoint waypoint1 || waypoint1.Ladders == ladder) { continue; }
                                for (int j = i + 1; j < ladderWaypoint.linkedTo.Count; j++)
                                {
                                    if (ladderWaypoint.linkedTo[j] is not WayPoint waypoint2 || waypoint2.Ladders == ladder) { continue; }
                                    waypoint1.ConnectTo(waypoint2);
                                }
                            }
                        }
                        ladderWaypoints.ForEach(wp => wp.SetDisabled(disabled));
                    }
                    gap = linkedItem.GetComponent<Door>()?.LinkedGap;
                }
                linkedEntity.SetDisabled(disabled); 
                if (gap != null && disabled)
                {
                    //remove links to other modules from the gap (so a disabled/unused gap at the edge of the hull doesn't get linked to another nearby module,
                    //which would make it more difficult to determine the positioning/usability of the gap for module placement)
                    gap.linkedTo.RemoveAll(linked => !module.Entities.Contains(linked));
                }
            }
        }

        /// <summary>
        /// Returns all existing. Used instead of <see cref="WayPoint.WayPointList"/> because it excludes disabled waypoints,
        /// which we need during outpost generation because previously-disabled waypoints may need to be re-enabled as modules are moved and hallways generated or removed.
        /// </summary>
        /// <returns></returns>
        private static IEnumerable<WayPoint> GetAllWaypoints()
        {
            foreach (var mapEntity in MapEntity.MapEntityList)
            {
                if (mapEntity is WayPoint wayPoint)
                {
                    yield return wayPoint;
                }
            }
        }

        private static void SetOverlappingWallsDisabled(IEnumerable<MapEntity> entities, Gap gap, bool disabled)
        {
            foreach (var otherEntity in entities)
            {
                if (otherEntity is Structure { HasBody: true, IsPlatform: false, RemoveIfLinkedOutpostDoorInUse: true } &&
                    Submarine.RectContains(otherEntity.WorldRect, gap.WorldPosition))
                {
                    otherEntity.SetDisabled(disabled);
                }
            }
        }

        public static void SetWiresLocked(Submarine outpost, bool isLocked)
        {
            //note that GetItems will not return items from the sub here, just different parts of the outpost
            //(subs of different type are not searched)
            if (outpost != null &&
                outpost.Info.OutpostGenerationParams is not { AlwaysRewireable: true })
            {
                foreach (var item in outpost.GetItems(alsoFromConnectedSubs: true))
                {
                    foreach (var connectionPanel in item.GetComponents<ConnectionPanel>())
                    {
                        connectionPanel.TemporarilyLockedInNonPlayerOutpost = isLocked;
                    }
                }
            }
        }

        public static void SetDevicesDetachable(Submarine outpost, bool disableDetaching)
        {
            foreach (var item in outpost.GetItems(alsoFromConnectedSubs: true))
            {
                if (item.GetComponent<Holdable>() is { } holdable)
                {
                    if (holdable.Attachable)
                    {
                        holdable.TemporarilyDisableDetaching = disableDetaching;
                    }
                }
            }
        }

        /// <summary>
        /// Sets all the items in the outpost to be interactable by the player team (only modifying <see cref="Item.NonPlayerTeamInteractable"/> - item that are non-interactable in general are kept that way),
        /// and removes requirements for ID card tags that the outpost dwellers get from outpost spawnpoints (as these are usually impossible for the player crew to get).
        /// </summary>
        public static void SetDevicesPlayerTeamInteractable(Submarine outpost)
        {
            if (outpost == null) { return; }
            HashSet<Identifier> idCardTags = outpost.GetWaypoints(alsoFromConnectedSubs: true)
                .SelectMany(wp => wp.IdCardTags).Select(tag => tag.ToIdentifier())
                .Distinct().ToHashSet();
            idCardTags.Add(Tags.OutpostNPCIdCardTag);
            foreach (var idCardTag in outpost.IDCardTagsOriginallyInSub)
            {
                idCardTags.Add(idCardTag);
            }

            //do this instead of using Submarine.GetItems, because it will only find items from connected subs of the same type,
            //which would exclude e.g. drones docked with the outpost
            var outpostParts = GetConnectedOutpostParts(outpost);

            foreach (var item in Item.ItemList)
            {
                if (!outpostParts.Contains(item.Submarine)) { continue; }
                if (item.NonPlayerTeamInteractable)
                {
                    DebugConsole.NewMessage($"Made '{item.Name}' interactable to the player team.");
                    item.NonPlayerTeamInteractable = false;
                }

                if (item.GetComponent<WifiComponent>() is WifiComponent wifiComponent)
                {
                    wifiComponent.TeamID = CharacterTeamType.Team1;
                }

                //find the items that have been configured to be usable with ID card tags of the outpost dwellers,
                //and make them usable with any ID card
                foreach (var itemComponent in item.Components)
                {
                    if (!itemComponent.RequiredItems.TryGetValue(RelatedItem.RelationType.Picked, out var requirementList)) { continue; }
                    foreach (var requirement in requirementList)
                    {
                        foreach (Identifier idCardTag in idCardTags)
                        {
                            if (requirement.Identifiers.Contains(idCardTag))
                            {
                                DebugConsole.NewMessage($"Replaced the ID card requirement '{idCardTag}' with '{Tags.IdCardTag}' in '{item.Name}'.");
                                requirement.ReplaceIdentifier(idCardTag.ToIdentifier(), Tags.IdCardTag);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns all the parts of the outpost (the outpost itself, the docking port extension, any droned/elevators etc)
        /// </summary>
        public static HashSet<Submarine> GetConnectedOutpostParts(Submarine outpost)
        {
            HashSet<Submarine> outpostParts = new HashSet<Submarine>();
            GetConnectedOutpostPartsRecursive(outpost, outpostParts);
            return outpostParts;
        }

        private static void GetConnectedOutpostPartsRecursive(Submarine sub, HashSet<Submarine> connectedParts)
        {
            connectedParts.Add(sub);
            foreach (var dockedTo in sub.DockedTo)
            {
                if (connectedParts.Contains(dockedTo)) { continue; }
                //stop the search at any part of the main sub
                if (dockedTo == Submarine.MainSub || (dockedTo.Info.Type == SubmarineType.Player && dockedTo.DockedTo.Contains(Submarine.MainSub))) { continue; }
                connectedParts.Add(dockedTo);
                GetConnectedOutpostPartsRecursive(dockedTo, connectedParts);
            }
        }

        public static void SetAllowSellingOutpostItems(Submarine outpost, bool allowSelling)
        {
            var outpostParts = GetConnectedOutpostParts(outpost);
            foreach (var outpostPart in outpostParts)
            {
                outpostPart.AllowSellingItems = allowSelling;
            }
            foreach (var item in Item.ItemList)
            {
                if (!outpostParts.Contains(item.Submarine)) { continue; }
                item.AllowSellingIfSpawnedInCurrentOutpost = allowSelling;
                //if selling the item is allowed, taking it should be too
                item.AllowStealing = allowSelling;
            }
        }

        public static void AlignLadders(IEnumerable<PlacedModule> placedModules)
        {
            var waypoints = GetAllWaypoints().ToList();

            //how close ladders have to be horizontally for them to get aligned with each other
            float horizontalTolerance = 30.0f;
            foreach (PlacedModule module in placedModules)
            {
                var topModule =
                    module.ThisGapPosition == OutpostModuleInfo.GapPosition.Top ?
                    module.PreviousModule :
                    placedModules.FirstOrDefault(m => m.PreviousModule == module && m.ThisGapPosition == OutpostModuleInfo.GapPosition.Bottom);
                if (topModule == null) { continue; }

                var topGap = module.ThisGapPosition == OutpostModuleInfo.GapPosition.Top ? module.ThisGap : topModule.ThisGap;
                var bottomGap = module.ThisGapPosition == OutpostModuleInfo.GapPosition.Top ? module.PreviousGap : topModule.PreviousGap;
                //the module that's attached to the other one owns the hallway between them (and the ladder waypoints generated in it)
                var hallwayOwner = module.ThisGapPosition == OutpostModuleInfo.GapPosition.Top ? module : topModule;

                foreach (MapEntity me in module.Entities)
                {
                    var ladder = (me as Item)?.GetComponent<Ladder>();
                    if (ladder == null) { continue; }
                    if (ladder.Item.WorldRect.Right < topGap.WorldRect.X || ladder.Item.WorldPosition.X > topGap.WorldRect.Right) { continue; }

                    var topLadder = topModule.Entities.Find(e => 
                        (e as Item)?.GetComponent<Ladder>() != null && 
                        Math.Abs(e.WorldPosition.X - me.WorldPosition.X) < horizontalTolerance);

                    //the lowest point on the top ladder (or the topmost module's hulls, if there's no ladder), in world coordinates
                    int topLadderBottom = topLadder != null ?
                        topLadder.WorldRect.Y - topLadder.WorldRect.Height :
                        topModule.Entities.OfType<Hull>().Min(h => h.WorldRect.Y - h.WorldRect.Height);

                    //new rect in the sub's local coordinates
                    var newLadderRect = new Rectangle(
                        ladder.Item.Rect.X,
                        topLadderBottom - (int)ladder.Item.Submarine.Position.Y,
                        ladder.Item.Rect.Width,
                        topLadderBottom - (ladder.Item.WorldRect.Y - ladder.Item.WorldRect.Height));

                    Rectangle testOverlapRect = new Rectangle(newLadderRect.X, newLadderRect.Y + 30, newLadderRect.Width, newLadderRect.Height - 60);
                    if (testOverlapRect.Height <= 0) { continue; }

                    //don't extend the ladder if it'd have to go through a wall
                    if (module.Entities.Any(e => e is Structure structure && structure.HasBody && !structure.IsPlatform && Submarine.RectsOverlap(testOverlapRect, structure.Rect)))
                    {
                        continue;
                    }
                    float ladderMoveAmount = Math.Abs(ladder.Item.Rect.Y - newLadderRect.Y);
                    Debug.Assert(ladderMoveAmount < 10000.0f, $"Ladder moved by a huge amount ({ladderMoveAmount} px) when aligning ladders between modules. This is probably a bug.");
                    Debug.Assert(newLadderRect.Height > 0, "Ladder height negative when aligning ladders between modules.");
                    ladder.Item.Rect = newLadderRect;

                    if (topGap != null && bottomGap != null)
                    {
                        var startWaypoint = waypoints.Find(wp => wp.ConnectedGap == bottomGap);
                        var endWaypoint = waypoints.Find(wp => wp.ConnectedGap == topGap);

                        float margin = 100;
                        if (startWaypoint != null && endWaypoint != null)
                        {
                            startWaypoint.SetDisabled(false);
                            startWaypoint.Ladders?.SetDisabled(false);

                            endWaypoint.SetDisabled(false);
                            endWaypoint.Ladders?.SetDisabled(false);

                            //remove existing waypoints before creating new ones
                            foreach (WayPoint waypoint in waypoints)
                            {
                                if (waypoint.Ladders == ladder && 
                                    waypoint != startWaypoint && 
                                    waypoint != endWaypoint &&
                                    waypoint.Position.Y < topGap.Position.Y &&
                                    waypoint.Position.Y > bottomGap.Position.Y)
                                {
                                    foreach (var linkedTo in waypoint.linkedTo.ToList())
                                    {
                                        if (linkedTo is WayPoint linkedWaypoint)
                                        {
                                            waypoint.RemoveConnectionTo(linkedWaypoint);
                                        }
                                    }
                                    waypoint.Remove();
                                }
                            }

                            WayPoint prevWaypoint = startWaypoint;
                            for (float y = bottomGap.Position.Y + margin; y <= topGap.Position.Y - margin; y += WayPoint.LadderWaypointInterval)
                            {
                                var wayPoint = new WayPoint(new Vector2(startWaypoint.Position.X, y), SpawnType.Path, ladder.Item.Submarine)
                                {
                                    Ladders = ladder
                                };
                                prevWaypoint.ConnectTo(wayPoint);
                                prevWaypoint = wayPoint;
                                //make the waypoints part of the hallway, so they get moved/removed with it
                                hallwayOwner.HallwayEntities.Add(wayPoint);
                            }
                            prevWaypoint.ConnectTo(endWaypoint);
                        }
                    }                    
                }
            }
        }

        public static void PowerUpOutpost(Submarine sub)
        {
            //create a copy of the list, because EntitySpawner may not exist yet we're generating the level,
            //which can cause items to be removed/instantiated directly 
            var entities = MapEntity.MapEntityList.Where(me => me.Submarine == sub).ToList();

            foreach (MapEntity e in entities)
            {
                if (e is not Item item) { continue; }
                var reactor = item.GetComponent<ReactorBase>();
                if (reactor != null)
                {
                    reactor.PowerOn = true;
                    reactor.AutoTemp = true;
                }
            }

            for (int i = 0; i < 600; i++)
            {
                Powered.UpdatePower((float)Timing.Step);
                foreach (MapEntity e in entities)
                {
                    if (e is not Item item || item.GetComponent<Powered>() == null) { continue; }
                    item.Update((float)Timing.Step, GameMain.GameScreen.Cam);
                }
            }
        }


        public static void SpawnNPCs(Location location, Submarine outpost, bool spawnNPCsEvenIfDisallowedByOutpostGenerationParameters = false)
        {
            if (outpost?.Info?.OutpostGenerationParams is not { } outpostGenerationParams) { return; }
            if (!spawnNPCsEvenIfDisallowedByOutpostGenerationParameters && 
                !outpostGenerationParams.SpawnNPCs) 
            {
                //if the outpost generator is not set to spawn npcs and there are none of them atm,
                //we can safely assume the population and initial population are 0

                //if there are any, the DLC might've spawned persistent dwellers in a player outpost, in which case we don't touch these
                if (outpost.Info.OutpostNPCs.None())
                {
                    location.BasePopulation = location.InitialPopulation = 0;
                }
                return; 
            }

            if (Level.Loaded != null)
            {
                Rand.SetSyncedSeed(ToolBox.StringToInt(Level.Loaded.Seed));
            }

            List<(HumanPrefab HumanPrefab, CharacterInfo CharacterInfo)> selectedCharacters = [];

            var humanPrefabs = outpostGenerationParams.GetHumanPrefabs(location?.Faction?.Prefab, location?.SecondaryFaction?.Prefab, outpost, Rand.RandSync.ServerAndClient);
            if (location.InitialPopulation == -1)
            {
                location.InitialPopulation = humanPrefabs.Count;
            }
            if (location.BasePopulation == -1)
            {
                location.BasePopulation = humanPrefabs.Count;
            }

            List<int> generatedCharacterIds = new List<int>();
            foreach (HumanPrefab humanPrefab in humanPrefabs)
            {
                if (humanPrefab is null) { continue; }
                var characterInfo = humanPrefab.CreateCharacterInfo(Rand.RandSync.ServerAndClient);
                int characterId = characterInfo.GetIdentifier();
                generatedCharacterIds.Add(characterId);
                if (location != null && 
                    location.KilledCharacters.FirstOrDefault(character => character.Identifier == characterId) is { } killedCharacter) 
                {
                    //replace killed characters, except if they died recently or if the population of the outpost has dropped too low
                    if (killedCharacter.WorldStepsSinceKilled < location.Type.ReplaceKilledCharactersAfter ||
                        location.IsBelowInsufficientPopulationThreshold)
                    {
                        continue;
                    }
                    //try multiple times in case we end up rolling an identical character
                    for (int tries = 0; tries < 100; tries++)
                    {
                        var replacementCharacterInfo = humanPrefab.CreateCharacterInfo(Rand.RandSync.ServerAndClient);
                        int replacementCharacterId = characterInfo.GetIdentifier();
                        if (location != null && !location.KilledCharacters.Any(character => character.Identifier == characterId))
                        {
                            characterInfo = replacementCharacterInfo;
                            break;
                        }
                    }                    
                }
                selectedCharacters.Add((humanPrefab, characterInfo));
                if (selectedCharacters.Count() >= location.BasePopulation) { break; }
            }
            //if we there are killed characters present in the list that wouldn't generate anyway, we can remove those
            //this can happen if something (e.g. new jobs being added) makes the results of the RNG change and a different set of NPCs to be generated
            location.UnregisterKilledCharacters(location.KilledCharacters.Where(c => !generatedCharacterIds.Contains(c.Identifier)).ToList());

            //if base population has been modified, we may need to spawn more characters
            int extraCharactersToSpawn = location.Population - selectedCharacters.Count;
            if (extraCharactersToSpawn > 0)
            {
                DebugConsole.NewMessage(
                    $"Population of the location is larger than the number of spawned characters "+
                    $"(population: {location.Population}, base population: {location.BasePopulation}, spawned characters: {selectedCharacters.Count}), spawning {extraCharactersToSpawn} more...",
                    Color.Magenta);
            }
            for (int i = 0; i < extraCharactersToSpawn; i++)
            {
                var humanPrefab = humanPrefabs.GetRandom(prefab => prefab.CampaignInteractionType == CampaignMode.InteractionType.None && prefab.Job != "outpostmanager".ToIdentifier(), Rand.RandSync.ServerAndClient);
                if (humanPrefab == null)
                {
                    DebugConsole.ThrowError($"Attempted to spawn more NPCs in the outpost, but could not find any NPCs for the outpost generation parameters \"{outpostGenerationParams.Identifier}\".");
                    break;
                }
                var characterInfo = humanPrefab.CreateCharacterInfo(Rand.RandSync.ServerAndClient);
                selectedCharacters.Add((humanPrefab, characterInfo));
            }

            List<Character> spawnedCharacters = new List<Character>();
            foreach ((var humanPrefab, var characterInfo) in selectedCharacters)
            {
                Rand.SetSyncedSeed(ToolBox.StringToInt(characterInfo.Name));

                ISpatialEntity gotoTarget = SpawnAction.GetSpawnPos(SpawnAction.SpawnLocationType.Outpost, SpawnType.Human, humanPrefab.GetModuleFlags(), humanPrefab.GetSpawnPointTags(),
                    debugLogName: $"Outpost NPC ({humanPrefab.Identifier})");
                if (gotoTarget == null)
                {
                    gotoTarget = outpost.GetHulls(true).GetRandom(Rand.RandSync.ServerAndClient);
                }
                characterInfo.TeamID = CharacterTeamType.FriendlyNPC;
                var npc = Character.Create(characterInfo.SpeciesName, SpawnAction.OffsetSpawnPos(gotoTarget.WorldPosition, 100.0f), ToolBox.RandomSeed(8), characterInfo, hasAi: true, createNetworkEvent: true);
                spawnedCharacters.Add(npc);

                npc.AnimController.FindHull(gotoTarget.WorldPosition, setSubmarine: true);
                npc.TeamID = CharacterTeamType.FriendlyNPC;
                npc.HumanPrefab = humanPrefab;
                npc.SpawnedByOutpostGenerator = true;
                outpost.Info.AddOutpostNPC(npc);
                if (GameMain.NetworkMember?.ServerSettings != null && !GameMain.NetworkMember.ServerSettings.KillableNPCs)
                {
                    npc.CharacterHealth.Unkillable = true;
                }
                humanPrefab.GiveItems(npc, outpost, gotoTarget as WayPoint, Rand.RandSync.ServerAndClient);
                foreach (Item item in npc.Inventory.FindAllItems(it => it != null, recursive: true))
                {
                    item.AllowStealing = outpostGenerationParams.AllowStealing;
                    item.SpawnedInNonPlayerOutpostOrSubmarine = true;
                    if (item.GetComponent<IdCard>() is not null)
                    {
                        item.AddTag(Tags.OutpostNPCIdCardTag);
                    }
                }
                humanPrefab.InitializeCharacter(npc, gotoTarget);
            }

            AssignCommander(spawnedCharacters);
        }

        /// <summary>
        /// Assigns one of the NPCs as a "commander" (<see cref="ShipCommandManager"/>) who handles assigning orders to the outpost dwellers.
        /// Necessary for e.g. bots to be ordered to turrets.
        /// </summary>
        public static void AssignCommander(IEnumerable<Character> outpostCharacters)
        {
            Character commander =
                outpostCharacters.FirstOrDefault(character => character.JobIdentifier == "securityofficer") ??
                outpostCharacters.MaxBy(character => character.GetSkillLevel(Tags.WeaponsSkill));
            if (commander?.AIController is HumanAIController humanAIController)
            {
                var shipCommandManager = humanAIController.InitShipCommandManager(Submarine.MainSub.ToEnumerable(), outpostCharacters);
                shipCommandManager.StopObjectivesIfHostilesOnBoard = false;
                shipCommandManager.RequireJobAppropriateOrders = true;
            }
        }
    }
}
