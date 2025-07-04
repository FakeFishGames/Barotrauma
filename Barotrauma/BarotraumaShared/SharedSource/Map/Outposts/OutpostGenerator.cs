﻿using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.RuinGeneration;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Barotrauma
{
    static class OutpostGenerator
    {
        class PlacedModule
        {
            /// <summary>
            /// Info of this outpost module
            /// </summary>
            public readonly SubmarineInfo Info;
            /// <summary>
            /// Which module is this one attached to
            /// </summary>
            public readonly PlacedModule PreviousModule;
            /// <summary>
            /// The position of this module's gap that attaches to the previous module
            /// </summary>
            public readonly OutpostModuleInfo.GapPosition ThisGapPosition = 0;

            public OutpostModuleInfo.GapPosition UsedGapPositions = 0;

            public readonly HashSet<Identifier> FulfilledModuleTypes = new HashSet<Identifier>();

            public Vector2 Offset;

            public Vector2 MoveOffset;

            public Gap ThisGap, PreviousGap;

            public Rectangle Bounds;
            public Rectangle HullBounds;

            public PlacedModule(SubmarineInfo thisModule, PlacedModule previousModule, OutpostModuleInfo.GapPosition thisGapPosition)
            {
                Info = thisModule;
                PreviousModule = previousModule;
                ThisGapPosition = thisGapPosition;
                UsedGapPositions = thisGapPosition;
                if (PreviousModule != null)
                {
                    previousModule.UsedGapPositions |= GetOpposingGapPosition(thisGapPosition);
                }
            }

            public override string ToString()
            {
                return $"OutpostGenerator.PlacedModule ({Info.Name})";
            }
        }

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
            var outpostModuleFiles = ContentPackageManager.EnabledPackages.All
                .SelectMany(p => p.GetFiles<OutpostModuleFile>())
                .OrderBy(f => f.UintIdentifier).ToArray();
            var uintIdDupes = outpostModuleFiles.Where(f1 =>
                outpostModuleFiles.Any(f2 => f1 != f2 && f1.UintIdentifier == f2.UintIdentifier)).ToArray();
            if (uintIdDupes.Any())
            {
                throw new Exception($"OutpostModuleFile UintIdentifier duplicates found: {uintIdDupes.Select(f => f.Path)}");
            }
            if (location != null)
            {
                if (location.IsCriticallyRadiated() && OutpostGenerationParams.OutpostParams.FirstOrDefault(p => p.Identifier == generationParams.ReplaceInRadiation) is { } newParams)
                {
                    generationParams = newParams;
                }

                locationType = location.Type;
            }

            Submarine sub = null;
            if (generationParams.OutpostTag.IsEmpty)
            {
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

        private static SubmarineInfo ChooseOutpost(OutpostGenerationParams generationParams)
        {
            var outpostFiles = ContentPackageManager.EnabledPackages.All
                .SelectMany(p => p.GetFiles<OutpostFile>())
                .Where(f => !TutorialPrefab.Prefabs.Any(tp => tp.OutpostPath == f.Path))
                .OrderBy(f => f.UintIdentifier).ToList();

            List<SubmarineInfo> outpostInfos = new List<SubmarineInfo>();
            foreach (var outpostFile in outpostFiles)
            {
                outpostInfos.Add(new SubmarineInfo(outpostFile.Path.Value));
            }


            //if there's missions selected that allow outpost selection from some specific set of outposts,
            //choose one of those outposts
            List<SubmarineInfo> outpostInfosSuitableForMission = new List<SubmarineInfo>();
            if (GameMain.GameSession?.GameMode is { } gameMode)
            {
                foreach (var mission in gameMode.Missions)
                {
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
                //...but only if the outpost is suitable for the mission (or if the mission has no specific requirements for the outpost)
                if (outpostInfosSuitableForMission.None() ||
                    outpostInfosSuitableForMission.Any(outpostInfo => outpostInfo.OutpostTags.Contains(serverSettings.SelectedOutpostName)))
                {
                    var matchingOutpost = outpostInfos.FirstOrDefault(o => o.Name == serverSettings.SelectedOutpostName);
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
                outpostInfos = outpostInfos.FindAll(o => o.OutpostTags.None());
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

        private static Submarine GenerateFromModules(OutpostGenerationParams generationParams, OutpostModuleFile[] outpostModuleFiles, Submarine sub, LocationType locationType, Location location, bool onlyEntrance = false, bool allowInvalidOutpost = false)
        {
            //load the infos of the outpost module files
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
                List<Identifier> pendingModuleFlags = new List<Identifier>();
                if (generationParams.ModuleCounts.Any())
                {
                    pendingModuleFlags = onlyEntrance ?
                        generationParams.ModuleCounts[0].Identifier.ToEnumerable().ToList() :
                        SelectModules(outpostModules, location, generationParams);
                }

                foreach (Identifier flag in pendingModuleFlags)
                {
                    if (flag == "none") { continue; }
                    int pendingCount = pendingModuleFlags.Count(f => f == flag);
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
                            pendingModuleFlags.Remove(flag);
                        }
                    }
                }

                //the first module is spawned separately, remove it from the list of pending modules
                Identifier initialModuleFlag = pendingModuleFlags.FirstOrDefault().IfEmpty("airlock".ToIdentifier());
                pendingModuleFlags.Remove(initialModuleFlag);

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
                foreach (Identifier initialFlag in initialModule.OutpostModuleInfo.ModuleFlags)
                {
                    if (pendingModuleFlags.Contains("initialFlag".ToIdentifier())) { pendingModuleFlags.Remove(initialFlag); }
                }

                if (remainingOutpostGenerationTries == 1)
                {
                    //generation has failed and only one attempt left, try removing duplicate modules
                    pendingModuleFlags = pendingModuleFlags.Distinct().ToList();
                }

                selectedModules.Add(new PlacedModule(initialModule, null, OutpostModuleInfo.GapPosition.None));
                selectedModules.Last().FulfilledModuleTypes.Add(initialModuleFlag);

                AppendToModule(
                    selectedModules.Last(), outpostModules.ToList(), pendingModuleFlags,
                    selectedModules,
                    locationType,
                    allowExtendBelowInitialModule: generationParams is RuinGeneration.RuinGenerationParams,
                    allowDifferentLocationType: remainingOutpostGenerationTries == 1);

                if (GameMain.GameSession?.ForceOutpostModule != null)
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

                if (pendingModuleFlags.Any(flag => flag != "none"))
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
                            DebugConsole.AddSafeError("Could not generate an outpost with all of the required modules. Some modules may not have enough connections at the edges to generate a valid layout. Pending modules: " + string.Join(", ", pendingModuleFlags));
                        }
                    }
                    else
                    {
                        DebugConsole.AddSafeError("Could not generate an outpost with all of the required modules. Some modules may not have enough connections at the edges to generate a valid layout. Pending modules: " + string.Join(", ", pendingModuleFlags) + ". Won't retry because invalid outposts are allowed.");
                    }
                }

                var outpostInfo = new SubmarineInfo()
                {
                    Type = SubmarineType.Outpost
                };
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
                Dictionary<PlacedModule, List<MapEntity>> entities = new Dictionary<PlacedModule, List<MapEntity>>();
                int idOffset = sub.IdOffset;
                for (int i = 0; i < selectedModules.Count; i++)
                {
                    var selectedModule = selectedModules[i];
                    sub.Info.GameVersion = selectedModule.Info.GameVersion;
                    var moduleEntities = MapEntity.LoadAll(sub, selectedModule.Info.SubmarineElement, selectedModule.Info.FilePath, idOffset);

                    if (usedForceOutpostModule != null && usedForceOutpostModule == selectedModule.Info)
                    {
                        sub.ForcedOutpostModuleWayPoints = moduleEntities.OfType<WayPoint>().ToList();
                    }

                    MapEntity.InitializeLoadedLinks(moduleEntities);

                    foreach (MapEntity entity in moduleEntities.ToList())
                    {
                        entity.OriginalModuleIndex = i;
                        if (entity is not Item item) { continue; }
                        var door = item.GetComponent<Door>();
                        if (door != null)
                        {
                            door.RefreshLinkedGap();
                            if (!moduleEntities.Contains(door.LinkedGap)) { moduleEntities.Add(door.LinkedGap); }
                        }
                        item.GetComponent<ConnectionPanel>()?.InitializeLinks();
                        item.GetComponent<ItemContainer>()?.OnMapLoaded();
                    }
                    idOffset = moduleEntities.Max(e => e.ID) + 1;

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

                    if (!hullEntities.Any())
                    {
                        selectedModule.HullBounds = new Rectangle(Point.Zero, Submarine.GridSize.ToPoint());
                    }
                    else
                    {
                        Point min = new Point(hullEntities.Min(e => e.WorldRect.X), hullEntities.Min(e => e.WorldRect.Y - e.WorldRect.Height));
                        Point max = new Point(hullEntities.Max(e => e.WorldRect.Right), hullEntities.Max(e => e.WorldRect.Y));
                        selectedModule.HullBounds = new Rectangle(min, max - min);
                    }

                    if (!wallEntities.Any())
                    {
                        selectedModule.Bounds = new Rectangle(Point.Zero, Submarine.GridSize.ToPoint());
                    }
                    else
                    {
                        Point min = new Point(wallEntities.Min(e => e.WorldRect.X), wallEntities.Min(e => e.WorldRect.Y - e.WorldRect.Height));
                        Point max = new Point(wallEntities.Max(e => e.WorldRect.Right), wallEntities.Max(e => e.WorldRect.Y));
                        selectedModule.Bounds = new Rectangle(min, max - min);
                    }

                    if (selectedModule.PreviousModule != null)
                    {
                        selectedModule.PreviousGap = GetGap(entities[selectedModule.PreviousModule], GetOpposingGapPosition(selectedModule.ThisGapPosition));
                        if (selectedModule.PreviousGap == null)
                        {
                            DebugConsole.ThrowError($"Error during outpost generation: {GetOpposingGapPosition(selectedModule.ThisGapPosition)} gap not found in module {selectedModule.PreviousModule.Info.Name}.");
                            generationFailed = true;
                            return new List<MapEntity>();
                        }
                        selectedModule.ThisGap = GetGap(moduleEntities, selectedModule.ThisGapPosition);
                        if (selectedModule.ThisGap == null)
                        {
                            DebugConsole.ThrowError($"Error during outpost generation: {selectedModule.ThisGapPosition} gap not found in module {selectedModule.Info.Name}.");
                            generationFailed = true;
                            return new List<MapEntity>();
                        }

                        Vector2 moveDir = GetMoveDir(selectedModule.ThisGapPosition);
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
                    entities[selectedModule] = moduleEntities;
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
                            //if we've already exhausted half of the retries, accept potential overlaps
                            remainingOutpostGenerationTries > MaxOutpostGenerationRetries / 2 &&
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

                List<MapEntity> allEntities = new List<MapEntity>();
                foreach (List<MapEntity> entityList in entities.Values)
                {
                    allEntities.AddRange(entityList);
                }

                if (!generationFailed)
                {
                    foreach (PlacedModule module in selectedModules)
                    {
                        Submarine.RepositionEntities(module.Offset + sub.HiddenSubPosition, entities[module]);
                    }
                    Gap.UpdateHulls();
                    allEntities.AddRange(GenerateHallways(sub, locationType, selectedModules, outpostModules, entities, generationParams is RuinGeneration.RuinGenerationParams));
                    LinkOxygenGenerators(allEntities);
                    if (generationParams.LockUnusedDoors)
                    {
                        LockUnusedDoors(selectedModules, entities, generationParams.RemoveUnusedGaps);
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
                    AlignLadders(selectedModules, entities);
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

        /// <summary>
        /// Select the number and types of the modules to use in the outpost
        /// </summary>
        private static List<Identifier> SelectModules(IEnumerable<SubmarineInfo> modules, Location location, OutpostGenerationParams generationParams)
        {
            int totalModuleCount = generationParams.TotalModuleCount;
            int totalModuleCountExcludingOptional = totalModuleCount - generationParams.ModuleCounts.Count(m => m.Probability < 1.0f);
            var pendingModuleFlags = new List<Identifier>();
            bool availableModulesFound = true;

            Identifier initialModuleFlag = generationParams.ModuleCounts.FirstOrDefault().Identifier;
            pendingModuleFlags.Add(initialModuleFlag);
            while (pendingModuleFlags.Count < totalModuleCountExcludingOptional && availableModulesFound)
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
                    if (pendingModuleFlags.Count(m => m == moduleCount.Identifier) >= generationParams.GetModuleCount(moduleCount.Identifier))
                    {
                        continue;
                    }
                    if (!modules.Any(m => m.OutpostModuleInfo.ModuleFlags.Contains(moduleCount.Identifier)))
                    {
                        DebugConsole.ThrowError($"Failed to add a module to the outpost (no modules with the flag \"{moduleCount.Identifier}\" found).");
                        continue;
                    }
                    availableModulesFound = true;
                    pendingModuleFlags.Add(moduleCount.Identifier);
                }
            }
            pendingModuleFlags.OrderBy(f => generationParams.ModuleCounts.First(m => m.Identifier == f).Order).ThenBy(f => Rand.Value(Rand.RandSync.ServerAndClient));
            while (pendingModuleFlags.Count < totalModuleCount && generationParams.AppendToReachTotalModuleCount)
            {
                //don't place "none" modules at the end because
                // a. "filler rooms" at the end of a hallway are pointless
                // b. placing the unnecessary filler rooms first give more options for the placement of the more important modules 
                pendingModuleFlags.Insert(Rand.Int(pendingModuleFlags.Count - 1, Rand.RandSync.ServerAndClient), "none".ToIdentifier());
            }

            //make sure the initial module is inserted first
            pendingModuleFlags.Remove(initialModuleFlag);
            pendingModuleFlags.Insert(0, initialModuleFlag);

            if (pendingModuleFlags.Count > totalModuleCount)
            {
                DebugConsole.ThrowError($"Error during outpost generation. {pendingModuleFlags.Count} modules set to be used the outpost, but total module count is only {totalModuleCount}. Leaving out some of the modules...");
                int removeCount = pendingModuleFlags.Count - totalModuleCount;
                for (int i = 0; i < removeCount; i++)
                {
                    pendingModuleFlags.Remove(pendingModuleFlags.Last());
                }
            }

            return pendingModuleFlags;
        }

        /// <summary>
        /// Attaches additional modules to all the available gaps of the given module, 
        /// and continues recursively through the attached modules until all the pending module types have been placed.
        /// </summary>
        /// <param name="currentModule">The module to attach to.</param>
        /// <param name="availableModules">Which modules we can choose from.</param>
        /// <param name="pendingModuleFlags">Which types of modules we still need in the outpost.</param>
        /// <param name="selectedModules">The modules we've already selected to be used in the outpost.</param>
        /// <param name="locationType">The type of the location we're generating the outpost for.</param>
        /// <param name="tryReplacingCurrentModule">If we fail to append to the current module, should we try replacing it with something else and see if we can append to it then?</param>
        /// <param name="allowExtendBelowInitialModule">Is the module allowed to be placed further down than the initial module (usually the airlock module)? 
        /// Note that at this point we only determine which module to attach to which, but not the actual positions or bounds of the modules, so it's possible for a module to attach to the side of the airlock but still extend below the airlock if it's very tall for example.</param>
        /// <param name="allowDifferentLocationType">If we fail to find a module suitable for the location type, should we use a module that's meant for a different location type instead?</param>
        private static bool AppendToModule(PlacedModule currentModule,
            List<SubmarineInfo> availableModules,
            List<Identifier> pendingModuleFlags,
            List<PlacedModule> selectedModules, 
            LocationType locationType,
            bool tryReplacingCurrentModule = true,
            bool allowExtendBelowInitialModule = false,
            bool allowDifferentLocationType = false)
        {
            if (pendingModuleFlags.Count == 0) { return true; }

            List<PlacedModule> placedModules = new List<PlacedModule>();
            foreach (OutpostModuleInfo.GapPosition gapPosition in GapPositions.Randomize(Rand.RandSync.ServerAndClient))
            {
                if (currentModule.UsedGapPositions.HasFlag(gapPosition)) { continue; }
                if (DisallowBelowAirlock(allowExtendBelowInitialModule, gapPosition, currentModule)) { continue; }

                PlacedModule newModule = null;
                //try appending to the current module if possible
                if (currentModule.Info.OutpostModuleInfo.GapPositions.HasFlag(gapPosition))
                {
                    newModule = AppendModule(currentModule, GetOpposingGapPosition(gapPosition), availableModules, pendingModuleFlags, selectedModules, locationType, allowDifferentLocationType);
                }

                if (newModule != null) 
                { 
                    placedModules.Add(newModule); 
                }
                else
                {
                    //couldn't append to current module, try one of the other placed modules
                    foreach (PlacedModule otherModule in selectedModules)
                    {
                        if (otherModule == currentModule) { continue; }
                        foreach (OutpostModuleInfo.GapPosition otherGapPosition in 
                            GapPositions.Where(g => !otherModule.UsedGapPositions.HasFlag(g) && otherModule.Info.OutpostModuleInfo.GapPositions.HasFlag(g)))
                        {
                            if (DisallowBelowAirlock(allowExtendBelowInitialModule, otherGapPosition, otherModule)) { continue; }
                            newModule = AppendModule(otherModule, GetOpposingGapPosition(otherGapPosition), availableModules, pendingModuleFlags, selectedModules, locationType, allowDifferentLocationType);
                            if (newModule != null)
                            {
                                placedModules.Add(newModule);
                                break;
                            }
                        }
                        if (newModule != null) { break; }
                    }
                }
                if (pendingModuleFlags.Count == 0) { return true; }                    
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
                    pendingModuleFlags.AddRange(currentModule.FulfilledModuleTypes);
                    if (!availableModules.Contains(currentModule.Info)) { availableModules.Add(currentModule.Info); }
                    //retry
                    currentModule = AppendModule(currentModule.PreviousModule, currentModule.ThisGapPosition, availableModules, pendingModuleFlags, selectedModules, locationType, allowDifferentLocationType: true);
                    assertAllPreviousModulesPresent();
                    if (currentModule == null) { break; }
                    if (AppendToModule(currentModule, availableModules, pendingModuleFlags, selectedModules, locationType, tryReplacingCurrentModule: false, allowExtendBelowInitialModule, allowDifferentLocationType))
                    {
                        assertAllPreviousModulesPresent();
                        return true;
                    }
                }
                return false;
            }

            foreach (PlacedModule placedModule in placedModules)
            {
                AppendToModule(placedModule, availableModules, pendingModuleFlags, selectedModules, locationType, tryReplacingCurrentModule: true, allowExtendBelowInitialModule, allowDifferentLocationType);
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
        /// <param name="pendingModuleFlags">Which types of modules we still need in the outpost</param>
        /// <param name="selectedModules">The modules we've already selected to be used in the outpost.</param>
        private static PlacedModule AppendModule(
            PlacedModule currentModule,
            OutpostModuleInfo.GapPosition gapPosition,
            List<SubmarineInfo> availableModules,
            List<Identifier> pendingModuleFlags,
            List<PlacedModule> selectedModules,
            LocationType locationType,
            bool allowDifferentLocationType)
        {
            if (pendingModuleFlags.Count == 0) { return null; }

            Identifier flagToPlace = "none".ToIdentifier();
            SubmarineInfo nextModule = null;
            foreach (Identifier moduleFlag in pendingModuleFlags.OrderByDescending(f => currentModule?.Info?.OutpostModuleInfo.AllowAttachToModules.Contains(f) ?? false))
            {
                flagToPlace = moduleFlag;
                nextModule = GetRandomModule(currentModule?.Info?.OutpostModuleInfo, availableModules, flagToPlace, gapPosition, locationType, allowDifferentLocationType);
                if (nextModule != null) { break; }
            }

            if (nextModule != null)
            {
                var newModule = new PlacedModule(nextModule, currentModule, gapPosition)
                {
                    Offset = currentModule.Offset + GetMoveDir(gapPosition),
                };
                foreach (Identifier moduleFlag in nextModule.OutpostModuleInfo.ModuleFlags)
                {
                    if (!pendingModuleFlags.Contains(moduleFlag)) { continue; }
                    if (moduleFlag != "none" || flagToPlace == "none")
                    {
                        newModule.FulfilledModuleTypes.Add(moduleFlag);
                        pendingModuleFlags.Remove(moduleFlag);
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
        /// Check if any of the modules in modules1 overlap with modules in modules2
        /// </summary>
        private static bool FindOverlap(IEnumerable<PlacedModule> modules1, IEnumerable<PlacedModule> modules2, out PlacedModule module1, out PlacedModule module2)
        {
            module1 = null;
            module2 = null;
            foreach (PlacedModule module in modules1)
            {
                foreach (PlacedModule otherModule in modules2)
                {
                    if (module == otherModule) { continue; }
                    if (module.PreviousModule == otherModule && module.PreviousGap.ConnectedDoor == null && module.ThisGap.ConnectedDoor == null) { continue; }
                    if (ModulesOverlap(module, otherModule))
                    {
                        module1 = module;
                        module2 = otherModule;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Check if the modules overlap, taking their Offsets and MoveOffsets into account
        /// </summary>
        private static bool ModulesOverlap(PlacedModule module1, PlacedModule module2)
        {
            Rectangle bounds1 = module1.Bounds;
            bounds1.Location += (module1.Offset + module1.MoveOffset).ToPoint();
            Rectangle bounds2 = module2.Bounds;
            bounds2.Location += (module2.Offset + module2.MoveOffset).ToPoint();

            //more tolerance on adjacent modules to prevent generating an unnecessary, small hallway between them
            if (module1.PreviousModule == module2 || module2.PreviousModule == module1)
            {
                bounds1.Inflate(-16, -16);
                bounds2.Inflate(-16, -16);
            }

            Rectangle hullBounds1 = module1.HullBounds;
            hullBounds1.Location += (module1.Offset + module1.MoveOffset).ToPoint();
            Rectangle hullBounds2 = module2.HullBounds;
            hullBounds2.Location += (module2.Offset + module2.MoveOffset).ToPoint();

            hullBounds1.Inflate(-32, -32);
            hullBounds2.Inflate(-32, -32);

            return hullBounds1.Intersects(hullBounds2) || hullBounds1.Intersects(bounds2) || hullBounds2.Intersects(bounds1);
        }

        /// <summary>
        /// Check if any of the modules overlaps with a connection between 2 other modules
        /// </summary>
        private static bool ModuleOverlapsWithModuleConnections(IEnumerable<PlacedModule> modules)
        {            
            foreach (PlacedModule module in modules)
            {
                Rectangle rect = module.Bounds;
                rect.Location += (module.Offset + module.MoveOffset).ToPoint();
                rect.Y += module.Bounds.Height;

                Vector2? selfGapPos1 = null; 
                Vector2? selfGapPos2 = null;
                if (module.PreviousModule != null)
                {
                    selfGapPos1 = module.Offset + module.ThisGap.Position + module.MoveOffset;
                    selfGapPos2 = module.PreviousModule.Offset + module.PreviousGap.Position + module.PreviousModule.MoveOffset;
                }

                foreach (PlacedModule otherModule in modules)
                {
                    if (otherModule == module || otherModule.PreviousModule == null || otherModule.PreviousModule == module) { continue; }

                    //cast at both edges of the gap and see if it overlaps with anything
                    for (int i = -1; i <= 1; i += 2)
                    {
                        Vector2 gapEdgeOffset =
                            otherModule.ThisGap.IsHorizontal ?
                            Vector2.UnitY * otherModule.ThisGap.Rect.Height / 2 * i * 0.9f :
                            Vector2.UnitX * otherModule.ThisGap.Rect.Width / 2 * i * 0.9f;

                        Vector2 gapPos1 = otherModule.Offset + otherModule.ThisGap.Position + gapEdgeOffset + otherModule.MoveOffset;
                        Vector2 gapPos2 = otherModule.PreviousModule.Offset + otherModule.PreviousGap.Position + gapEdgeOffset + otherModule.PreviousModule.MoveOffset;
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
            }
            return false;
        }

        /// <summary>
        /// Check if the lowest point of the module is below the lowest point of the initial (docking) module. 
        /// This shouldn't happen, because it can cause modules to overlap with the docked sub.
        /// </summary>
        private static bool ModuleBelowInitialModule(PlacedModule module, PlacedModule initialModule)
        {
            Rectangle bounds = module.Bounds;
            bounds.Location += (module.Offset + module.MoveOffset).ToPoint();

            Rectangle initialModuleBounds = initialModule.Bounds;
            initialModuleBounds.Location += (initialModule.Offset + initialModule.MoveOffset).ToPoint();

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
                Vector2 moveDir = GetMoveDir(module.ThisGapPosition);
                const float moveStep = 50.0f;
                Vector2 currentMove = moveDir * Math.Max(minMoveAmount, moveStep);

                List<PlacedModule> subsequentModules2 = new List<PlacedModule>();
                GetSubsequentModules(module, movableModules, ref subsequentModules2);
                while (currentMove.LengthSquared() < maxMoveAmount * maxMoveAmount)
                {
                    foreach (PlacedModule movedModule in subsequentModules2)
                    {
                        movedModule.MoveOffset = currentMove;
                    }
                    if (!ModulesOverlap(module1, module2) && 
                        !ModuleOverlapsWithModuleConnections(allmodules) &&
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
                    movedModule.MoveOffset = Vector2.Zero;
                }
            }

            return solutionFound;
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
                availableModules.Where(m => m.OutpostModuleInfo.IsAllowedInLocationType(locationType));

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

        private static SubmarineInfo GetRandomModule(OutpostModuleInfo prevModule, IEnumerable<SubmarineInfo> modules, Identifier moduleFlag, OutpostModuleInfo.GapPosition gapPosition, LocationType locationType, bool allowDifferentLocationType)
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
                if (!suitableModules.Any())
                {
                    suitableModules = GetSuitableModules(modulesWithCorrectFlags, requireAllowAttachToPrevious: false, requireCorrectLocationType: true, requireLocationTypeSpecific: true);
                }
                //still not found! Try if we can find a generic module that's not meant to attach to the previous module
                if (!suitableModules.Any())
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
        private static void GetSubsequentModules(PlacedModule startModule, IEnumerable<PlacedModule> allModules, ref List<PlacedModule> subsequentModules)
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

        private static OutpostModuleInfo.GapPosition GetOpposingGapPosition(OutpostModuleInfo.GapPosition thisGapPosition)
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

        private static Vector2 GetMoveDir(OutpostModuleInfo.GapPosition thisGapPosition)
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

        private static Gap GetGap(IEnumerable<MapEntity> entities, OutpostModuleInfo.GapPosition gapPosition)
        {
            Gap selectedGap = null;
            foreach (MapEntity entity in entities)
            {
                if (!(entity is Gap gap)) { continue; }
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

        private static bool CanAttachTo(OutpostModuleInfo from, OutpostModuleInfo to)
        {
            if (!from.AllowAttachToModules.Any() || from.AllowAttachToModules.All(s => s == "any")) { return true; }                
            return from.AllowAttachToModules.Any(s => to.ModuleFlags.Contains(s));
        }

        private static List<MapEntity> GenerateHallways(Submarine sub, LocationType locationType, IEnumerable<PlacedModule> placedModules, IEnumerable<SubmarineInfo> availableModules, Dictionary<PlacedModule, List<MapEntity>> allEntities, bool isRuin)
        {
            //if a hallway is shorter than this, one of the doors at the ends of the hallway is removed
            const float MinTwoDoorHallwayLength = 32.0f;

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

                if (module.ThisGap != null && module.ThisGap.ConnectedDoor == null)
                {
                    //gap in use -> remove linked entities that are marked to be removed
                    foreach (var otherEntity in allEntities[module])
                    {
                        if (otherEntity is Structure structure && structure.HasBody && !structure.IsPlatform && structure.RemoveIfLinkedOutpostDoorInUse &&
                            Submarine.RectContains(structure.WorldRect, module.ThisGap.WorldPosition))
                        {
                            structure.Remove();
                        }
                    }
                }
                if (module.PreviousGap != null && module.PreviousGap.ConnectedDoor == null)
                {
                    //gap in use -> remove linked entities that are marked to be removed
                    foreach (var otherEntity in allEntities[module.PreviousModule])
                    {
                        if (otherEntity is Structure structure && structure.HasBody && !structure.IsPlatform && structure.RemoveIfLinkedOutpostDoorInUse &&
                            Submarine.RectContains(structure.WorldRect, module.PreviousGap.WorldPosition))
                        {
                            structure.Remove();
                        }
                    }
                }

                //if the hallway is very short, remove one of the doors
                if (hallwayLength <= MinTwoDoorHallwayLength) 
                { 
                    if (module.ThisGap != null && module.PreviousGap != null)
                    {
                        var gapToRemove = module.ThisGap.ConnectedDoor == null ? module.ThisGap : module.PreviousGap;
                        var otherGap = gapToRemove == module.ThisGap ? module.PreviousGap : module.ThisGap;

                        gapToRemove.ConnectedDoor?.Item.linkedTo.ForEachMod(lt => (lt as Structure)?.Remove());
                        if (gapToRemove.ConnectedDoor?.Item.Connections != null)
                        {
                            foreach (Connection c in gapToRemove.ConnectedDoor.Item.Connections)
                            {
                                c.Wires.ToArray().ForEach(w => w?.Item.Remove());
                            }
                        }

                        WayPoint thisWayPoint = WayPoint.WayPointList.Find(wp => wp.ConnectedGap == gapToRemove);
                        WayPoint previousWayPoint = WayPoint.WayPointList.Find(wp => wp.ConnectedGap == otherGap);
                        if (thisWayPoint != null && previousWayPoint != null)
                        {
                            foreach (MapEntity me in thisWayPoint.linkedTo)
                            {
                                if (me is WayPoint wayPoint && !previousWayPoint.linkedTo.Contains(wayPoint))
                                {
                                    previousWayPoint.linkedTo.Add(wayPoint);
                                }
                            }
                            thisWayPoint.Remove();
                        }
                        else
                        {
                            if (thisWayPoint == null)
                            { 
                                DebugConsole.ThrowError($"Failed to connect waypoints between outpost modules. No waypoint in the {module.ThisGapPosition.ToString().ToLower()} gap of the module \"{module.Info.Name}\".");
                            }
                            if (previousWayPoint == null)
                            {
                                DebugConsole.ThrowError($"Failed to connect waypoints between outpost modules. No waypoint in the {GetOpposingGapPosition(module.ThisGapPosition).ToString().ToLower()} gap of the module \"{module.PreviousModule.Info.Name}\".");
                            }
                        }

                        gapToRemove.ConnectedDoor?.Item.Remove(); 
                        if (hallwayLength <= 1.0f) { gapToRemove?.Remove(); }
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

                var moduleEntities = MapEntity.LoadAll(sub, hallwayInfo.SubmarineElement, hallwayInfo.FilePath, -1);

                //remove items that don't fit in the hallway
                moduleEntities.Where(e => e is Item item && item.GetComponent<Door>() == null && (isHorizontal ? e.Rect.Width : e.Rect.Height) > hallwayLength).ForEach(e => e.Remove());

                //find the largest hull to use it as the center point of the hallway
                //and the bounds of all the hulls, used when resizing the hallway to fit between the modules
                Vector2 hullCenter = Vector2.Zero;
                Rectangle hullBounds = Rectangle.Empty;
                float largestHullVolume = 0.0f;
                foreach (MapEntity me in moduleEntities)
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

                placedEntities.AddRange(moduleEntities);
                MapEntity.InitializeLoadedLinks(moduleEntities);
                Vector2 moveAmount = (module.ThisGap.Position + module.PreviousGap.Position) / 2 - hullCenter;
                Submarine.RepositionEntities(moveAmount, moduleEntities);
                hullBounds.Location += moveAmount.ToPoint();

                //resize/reposition entities to make the hallway fit between the modules
                foreach (MapEntity me in moduleEntities)
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

                if (hallwayLength > MinTwoDoorHallwayLength)
                {
                    //connect waypoints
                    var startWaypoint = WayPoint.WayPointList.Find(wp => wp.ConnectedGap == module.ThisGap);
                    if (startWaypoint == null)
                    {
                        DebugConsole.ThrowError($"Failed to connect waypoints between outpost modules. No waypoint in the {module.ThisGapPosition.ToString().ToLower()} gap of the module \"{module.Info.Name}\".");
                        continue;
                    }
                    var endWaypoint = WayPoint.WayPointList.Find(wp => wp.ConnectedGap == module.PreviousGap);
                    if (endWaypoint == null)
                    {
                        DebugConsole.ThrowError($"Failed to connect waypoints between outpost modules. No waypoint in the {GetOpposingGapPosition(module.ThisGapPosition).ToString().ToLower()} gap of the module \"{module.PreviousModule.Info.Name}\".");
                        continue;
                    }

                    if (startWaypoint.WorldPosition.X > endWaypoint.WorldPosition.X)
                    {
                        (endWaypoint, startWaypoint) = (startWaypoint, endWaypoint);
                    }

                    //if the hallway is longer than 100 pixels, generate some waypoints inside it
                    //for vertical hallways this isn't necessarily, it's done as a part of the ladder generation in AlignLadders
                    const float distanceBetweenWaypoints = 100.0f;
                    if (hallwayLength > distanceBetweenWaypoints)
                    {
                        WayPoint prevWayPoint = startWaypoint;
                        WayPoint firstWayPoint = null;
                        if (isHorizontal)
                        {
                            for (float x = leftHull.Rect.Right + distanceBetweenWaypoints / 2; x < rightHull.Rect.X - distanceBetweenWaypoints / 2; x += distanceBetweenWaypoints)
                            {
                                var newWayPoint = new WayPoint(new Vector2(x, hullBounds.Y + 110.0f), SpawnType.Path, sub);
                                firstWayPoint ??= newWayPoint;
                                prevWayPoint.linkedTo.Add(newWayPoint);
                                newWayPoint.linkedTo.Add(prevWayPoint);
                                prevWayPoint = newWayPoint;
                            }
                        }
                        else if (startWaypoint.Ladders == null)
                        {
                            float bottom = bottomHull.Rect.Y;
                            float top = topHull.Rect.Y - topHull.Rect.Height;
                            for (float y = bottom + distanceBetweenWaypoints; y < top - distanceBetweenWaypoints; y += distanceBetweenWaypoints)
                            {
                                var newWayPoint = new WayPoint(new Vector2(startWaypoint.Position.X, y), SpawnType.Path, sub);
                                firstWayPoint ??= newWayPoint; 
                                prevWayPoint.linkedTo.Add(newWayPoint); 
                                newWayPoint.linkedTo.Add(prevWayPoint);
                                prevWayPoint = newWayPoint;
                            }
                        }
                        else
                        {
                            startWaypoint.linkedTo.Add(endWaypoint);
                            endWaypoint.linkedTo.Add(startWaypoint);
                        }
                        if (firstWayPoint != null)
                        {
                            firstWayPoint.linkedTo.Add(startWaypoint);
                            startWaypoint.linkedTo.Add(firstWayPoint);
                        }
                        if (prevWayPoint != null)
                        {
                            prevWayPoint.linkedTo.Add(endWaypoint);
                            endWaypoint.linkedTo.Add(prevWayPoint);
                        }
                    }
                    else
                    {
                        startWaypoint.linkedTo.Add(endWaypoint);
                        endWaypoint.linkedTo.Add(startWaypoint);
                    }
                }
            }
            return placedEntities;
        }

        private static void LinkOxygenGenerators(IEnumerable<MapEntity> entities)
        {
            List<OxygenGenerator> oxygenGenerators = new List<OxygenGenerator>();
            List<Vent> vents = new List<Vent>();
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

        private static void LockUnusedDoors(IEnumerable<PlacedModule> placedModules, Dictionary<PlacedModule, List<MapEntity>> entities, bool removeUnusedGaps)
        {
            foreach (PlacedModule module in placedModules)
            {
                foreach (MapEntity me in entities[module])
                {
                    if (me is not Gap gap) { continue; }
                    var door = gap.ConnectedDoor;
                    if (door != null && !door.UseBetweenOutpostModules) { continue; }
                    if (placedModules.Any(m => m.PreviousGap == gap || m.ThisGap == gap)) 
                    {
                        //gap in use -> remove linked entities that are marked to be removed
                        if (gap.ConnectedDoor == null)
                        {
                            foreach (var otherEntity in entities[module])
                            {
                                if (otherEntity is Structure structure && structure.HasBody && !structure.IsPlatform && structure.RemoveIfLinkedOutpostDoorInUse &&
                                    Submarine.RectContains(structure.WorldRect, gap.WorldPosition))
                                {
                                    RemoveLinkedEntity(otherEntity);
                                }
                            }
                        }
                        door?.Item.linkedTo.Where(lt => ShouldRemoveLinkedEntity(lt, doorInUse: true, module: module)).ForEachMod(lt => RemoveLinkedEntity(lt));
                        continue; 
                    }
                    if (door != null && DockingPort.List.Any(d => Submarine.RectContains(d.Item.WorldRect, door.Item.WorldPosition))) { continue; }

                    //if the door is between two hulls of the same module, don't disable it
                    if (gap.linkedTo.Count == 2 && 
                        entities[module].Contains(gap.linkedTo[0]) &&
                        entities[module].Contains(gap.linkedTo[1]))
                    {
                        continue;
                    }
                    if (door != null)
                    {
                        if (door.Item.linkedTo.Any(lt => lt is Structure))
                        {
                            //door not in use -> remove linked entities that are NOT marked to be removed
                            door.Item.linkedTo.Where(lt => ShouldRemoveLinkedEntity(lt, doorInUse: false, module: module)).ForEachMod(lt => RemoveLinkedEntity(lt));
                            WayPoint.WayPointList.Where(wp => wp.ConnectedDoor == door).ForEachMod(wp => wp.Remove());
                            RemoveLinkedEntity(door.Item);
                            continue;
                        }
                        else
                        {
                            door.Stuck = 100.0f;
                            door.Item.NonInteractable = true;
                            var connectionPanel = door.Item.GetComponent<ConnectionPanel>();
                            if (connectionPanel != null) { connectionPanel.Locked = true; }
                        }
                    }
                    else if (removeUnusedGaps)
                    {
                        gap.Remove();
                        WayPoint.WayPointList.Where(wp => wp.ConnectedGap == gap).ForEachMod(wp => wp.Remove());
                    }
                }
                entities[module].RemoveAll(e => e.Removed);
            }

            static bool ShouldRemoveLinkedEntity(MapEntity e, bool doorInUse, PlacedModule module)
            {
                if (e is Item { IsLadder: true } ladderItem)
                {
                    int linkedToLadderCount = Door.DoorList.Count(otherDoor => otherDoor.Item.linkedTo.Contains(ladderItem));
                    if (linkedToLadderCount > 1)
                    {
                        //if there's multiple doors linked to the ladder, never remove it
                        //(the ladder is presumably not just for moving between two modules in that case, but might e.g. go through the whole module)
                        return false;
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

            static void RemoveLinkedEntity(MapEntity linked)
            {
                if (linked is Item linkedItem)
                {
                    if (linkedItem.Connections != null)
                    {
                        foreach (Connection connection in linkedItem.Connections)
                        {
                            foreach (Wire w in connection.Wires.ToArray())
                            {
                                w?.Item.Remove();
                            }
                        }
                    }
                    //if we end up removing a ladder, remove its waypoints too
                    if (linkedItem.GetComponent<Ladder>() is Ladder ladder)
                    {
                        var ladderWaypoints = WayPoint.WayPointList.FindAll(wp => wp.Ladders == ladder);
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
                        ladderWaypoints.ForEach(wp => wp.Remove());
                    }
                }
                linked.Remove();
            }
        }

        private static void AlignLadders(IEnumerable<PlacedModule> placedModules, Dictionary<PlacedModule, List<MapEntity>> entities)
        {
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

                foreach (MapEntity me in entities[module])
                {
                    var ladder = (me as Item)?.GetComponent<Ladder>();
                    if (ladder == null) { continue; }
                    if (ladder.Item.WorldRect.Right < topGap.WorldRect.X || ladder.Item.WorldPosition.X > topGap.WorldRect.Right) { continue; }

                    var topLadder = entities[topModule].Find(e => 
                        (e as Item)?.GetComponent<Ladder>() != null && 
                        Math.Abs(e.WorldPosition.X - me.WorldPosition.X) < horizontalTolerance);

                    int topLadderDiff = 0;
                    int topLadderBottom = (int)(topModule.HullBounds.Y + topModule.Offset.Y + topModule.MoveOffset.Y + ladder.Item.Submarine.HiddenSubPosition.Y);
                    if (topLadder != null)
                    {
                        topLadderBottom = topLadder.WorldRect.Y - topLadder.WorldRect.Height;
                    }

                    var newLadderRect = new Rectangle(
                        ladder.Item.Rect.X + topLadderDiff,
                        topLadderBottom,
                        ladder.Item.Rect.Width,
                        topLadderBottom - (ladder.Item.WorldRect.Y - ladder.Item.WorldRect.Height));

                    Rectangle testOverlapRect = new Rectangle(newLadderRect.X, newLadderRect.Y + 30, newLadderRect.Width, newLadderRect.Height - 60);
                    if (testOverlapRect.Height <= 0) { continue; }

                    //don't extend the ladder if it'd have to go through a wall
                    if (entities[module].Any(e => e is Structure structure && structure.HasBody && !structure.IsPlatform && Submarine.RectsOverlap(testOverlapRect, structure.Rect)))
                    {
                        continue;
                    }
                    ladder.Item.Rect = newLadderRect;

                    if (topGap != null && bottomGap != null)
                    {
                        var startWaypoint = WayPoint.WayPointList.Find(wp => wp.ConnectedGap == bottomGap);
                        var endWaypoint = WayPoint.WayPointList.Find(wp => wp.ConnectedGap == topGap);
                        float margin = 100;
                        if (startWaypoint != null && endWaypoint != null)
                        {
                            WayPoint prevWaypoint = startWaypoint;
                            for (float y = bottomGap.Position.Y + margin; y <= topGap.Position.Y - margin; y += WayPoint.LadderWaypointInterval)
                            {
                                var wayPoint = new WayPoint(new Vector2(startWaypoint.Position.X, y), SpawnType.Path, ladder.Item.Submarine)
                                {
                                    Ladders = ladder
                                };
                                prevWaypoint.ConnectTo(wayPoint);
                                prevWaypoint = wayPoint;
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
                var reactor = item.GetComponent<Reactor>();
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

        public static void SpawnNPCs(Location location, Submarine outpost)
        {
            if (outpost?.Info?.OutpostGenerationParams == null) { return; }

            List<HumanPrefab> killedCharacters = new List<HumanPrefab>();
            List<(HumanPrefab HumanPrefab, CharacterInfo CharacterInfo)> selectedCharacters
                = new List<(HumanPrefab HumanPrefab, CharacterInfo CharacterInfo)>();

            List<FactionPrefab> factions = new List<FactionPrefab>();
            if (location?.Faction != null) { factions.Add(location.Faction.Prefab); }
            if (location?.SecondaryFaction != null) { factions.Add(location.SecondaryFaction.Prefab); }

            var humanPrefabs = outpost.Info.OutpostGenerationParams.GetHumanPrefabs(factions, outpost, Rand.RandSync.ServerAndClient);
            foreach (HumanPrefab humanPrefab in humanPrefabs)
            {
                if (humanPrefab is null) { continue; }
                var characterInfo = humanPrefab.CreateCharacterInfo(Rand.RandSync.ServerAndClient);
                if (location != null && location.KilledCharacterIdentifiers.Contains(characterInfo.GetIdentifier())) 
                {
                    killedCharacters.Add(humanPrefab);
                    continue; 
                }
                selectedCharacters.Add((humanPrefab, characterInfo));
            }

            //replace killed characters with new ones
            foreach (HumanPrefab killedCharacter in killedCharacters)
            {
                for (int tries = 0; tries < 100; tries++)
                {
                    var characterInfo = killedCharacter.CreateCharacterInfo(Rand.RandSync.ServerAndClient);
                    if (location != null && !location.KilledCharacterIdentifiers.Contains(characterInfo.GetIdentifier()))
                    {
                        selectedCharacters.Add((killedCharacter, characterInfo));
                        break;
                    }
                }
            }

            foreach ((var humanPrefab, var characterInfo) in selectedCharacters)
            {
                Rand.SetSyncedSeed(ToolBox.StringToInt(characterInfo.Name));

                ISpatialEntity gotoTarget = SpawnAction.GetSpawnPos(SpawnAction.SpawnLocationType.Outpost, SpawnType.Human, humanPrefab.GetModuleFlags(), humanPrefab.GetSpawnPointTags());
                if (gotoTarget == null)
                {
                    gotoTarget = outpost.GetHulls(true).GetRandom(Rand.RandSync.ServerAndClient);
                }
                characterInfo.TeamID = CharacterTeamType.FriendlyNPC;
                var npc = Character.Create(characterInfo.SpeciesName, SpawnAction.OffsetSpawnPos(gotoTarget.WorldPosition, 100.0f), ToolBox.RandomSeed(8), characterInfo, hasAi: true, createNetworkEvent: true);
                npc.AnimController.FindHull(gotoTarget.WorldPosition, setSubmarine: true);
                npc.TeamID = CharacterTeamType.FriendlyNPC;
                npc.HumanPrefab = humanPrefab;
                outpost.Info.AddOutpostNPCIdentifierOrTag(npc, humanPrefab.Identifier);
                foreach (Identifier tag in humanPrefab.GetTags())
                {
                    outpost.Info.AddOutpostNPCIdentifierOrTag(npc, tag);
                }
                if (GameMain.NetworkMember?.ServerSettings != null && !GameMain.NetworkMember.ServerSettings.KillableNPCs)
                {
                    npc.CharacterHealth.Unkillable = true;
                }
                humanPrefab.GiveItems(npc, outpost, gotoTarget as WayPoint, Rand.RandSync.ServerAndClient);
                foreach (Item item in npc.Inventory.FindAllItems(it => it != null, recursive: true))
                {
                    item.AllowStealing = outpost.Info.OutpostGenerationParams.AllowStealing;
                    item.SpawnedInCurrentOutpost = true;
                }
                humanPrefab.InitializeCharacter(npc, gotoTarget);
            }
        }
    }
}
