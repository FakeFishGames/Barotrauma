using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
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

            public readonly HashSet<string> FulfilledModuleTypes = new HashSet<string>();

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

        public static Submarine Generate(OutpostGenerationParams generationParams, LocationType locationType, bool onlyEntrance = false)
        {
            return Generate(generationParams, locationType, location: null, onlyEntrance);
        }

        public static Submarine Generate(OutpostGenerationParams generationParams, Location location, bool onlyEntrance = false)
        {
            return Generate(generationParams, location.Type, location, onlyEntrance);
        }

        private static Submarine Generate(OutpostGenerationParams generationParams, LocationType locationType, Location location, bool onlyEntrance = false)
        {
            var outpostModuleFiles = ContentPackage.GetFilesOfType(GameMain.Config.AllEnabledPackages, ContentType.OutpostModule);
            if (location != null)
            {
                if (location.IsCriticallyRadiated() && OutpostGenerationParams.Params.FirstOrDefault(p => p.Identifier.Equals(generationParams.ReplaceInRadiation, StringComparison.OrdinalIgnoreCase)) is { } newParams)
                {
                    generationParams = newParams;
                }

                locationType = location.GetLocationType();
            }
            
            //load the infos of the outpost module files
            List<SubmarineInfo> outpostModules = new List<SubmarineInfo>();
            foreach (ContentFile outpostModuleFile in outpostModuleFiles)
            {
                var subInfo = new SubmarineInfo(outpostModuleFile.Path);
                if (subInfo.OutpostModuleInfo != null)
                {
                    outpostModules.Add(subInfo);
                }
            }

            List<PlacedModule> selectedModules = new List<PlacedModule>();
            bool generationFailed = false;
            int remainingTries = 5;
            Submarine sub = null;
            while (remainingTries > -1 && outpostModules.Any())
            {
                if (sub != null)
                {
#if SERVER
                    int eventCount = GameMain.Server.EntityEventManager.Events.Count();
                    int uniqueEventCount = GameMain.Server.EntityEventManager.UniqueEvents.Count();
#endif
                    List<MapEntity> entities = MapEntity.mapEntityList.FindAll(e => e.Submarine == sub);
                    entities.ForEach(e => e.Remove());
                    sub.Remove();
#if SERVER
                    //remove any events created during the removal of the entities
                    GameMain.Server.EntityEventManager.Events.RemoveRange(eventCount, GameMain.Server.EntityEventManager.Events.Count - eventCount);
                    GameMain.Server.EntityEventManager.UniqueEvents.RemoveRange(uniqueEventCount, GameMain.Server.EntityEventManager.UniqueEvents.Count - uniqueEventCount);
#endif
                    if (remainingTries <= 0) 
                    {
                        generationFailed = true;
                        break; 
                    }
                }

                selectedModules.Clear();
                //select which module types the outpost should consist of
                var pendingModuleFlags = onlyEntrance ? new List<string>() { generationParams.ModuleCounts.First().Key } : SelectModules(outpostModules, generationParams);
                foreach (string flag in pendingModuleFlags.Distinct().ToList())
                {
                    if (flag.Equals("none", StringComparison.OrdinalIgnoreCase)) { continue; }
                    int pendingCount = pendingModuleFlags.Count(f => f == flag);
                    int availableModuleCount =
                        outpostModules
                            .Where(m => m.OutpostModuleInfo.ModuleFlags.Any(f => f.Equals(flag, StringComparison.OrdinalIgnoreCase)))
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
                string initialModuleFlag = pendingModuleFlags.FirstOrDefault() ?? "airlock";
                pendingModuleFlags.Remove(initialModuleFlag);                

                var initialModule = GetRandomModule(outpostModules, initialModuleFlag, locationType);
                if (initialModule == null)
                {
                    throw new Exception("Failed to generate an outpost (no airlock modules found).");
                }
                foreach (string initialFlag in initialModule.OutpostModuleInfo.ModuleFlags)
                {
                    if (pendingModuleFlags.Contains("initialFlag")) { pendingModuleFlags.Remove(initialFlag); }
                }

                if (remainingTries == 1)
                {
                    //generation has failed and only one attempt left, try removing duplicate modules
                    pendingModuleFlags = pendingModuleFlags.Distinct().ToList();
                }

                selectedModules.Add(new PlacedModule(initialModule, null, OutpostModuleInfo.GapPosition.None));
                selectedModules.Last().FulfilledModuleTypes.Add(initialModuleFlag);
                AppendToModule(selectedModules.Last(), outpostModules.ToList(), pendingModuleFlags, selectedModules, locationType);
                if (pendingModuleFlags.Any(flag => !flag.Equals("none", StringComparison.OrdinalIgnoreCase)))
                {
                    remainingTries--;
                    if (remainingTries <= 0)
                    {
                        DebugConsole.ThrowError("Could not generate an outpost with all of the required modules. Some modules may not have enough doors at the edges to generate a valid layout. Pending modules: " + string.Join(", ", pendingModuleFlags));
                    }
                    continue;
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
                    foreach (Hull hull in Hull.hullList)
                    {
                        if (hull.Submarine != sub) { continue; }
                        if (string.IsNullOrEmpty(hull.RoomName) || 
                            hull.RoomName.Contains("RoomName.", StringComparison.OrdinalIgnoreCase))
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
                    return sub; 
                }
                remainingTries--;
            }

            DebugConsole.NewMessage("Failed to generate an outpost without overlapping modules. Trying to use a pre-built outpost instead...");

            var outpostFiles = ContentPackage.GetFilesOfType(GameMain.Config.AllEnabledPackages, ContentType.Outpost);
            if (!outpostFiles.Any())
            {
                throw new Exception("Failed to generate an outpost. Could not generate an outpost from the available outpost modules and there are no pre-built outposts available.");
            }
            var prebuiltOutpostInfo = new SubmarineInfo(outpostFiles.GetRandom(Rand.RandSync.Server).Path)
            {
                Type = SubmarineType.Outpost
            };
            sub = new Submarine(prebuiltOutpostInfo);
            sub.Info.OutpostGenerationParams = generationParams;
            location?.RemoveTakenItems();
            return sub;

            List<MapEntity> loadEntities(Submarine sub)
            {
                Dictionary<PlacedModule, List<MapEntity>> entities = new Dictionary<PlacedModule, List<MapEntity>>();
                int idOffset = sub.IdOffset;
                for (int i = 0; i < selectedModules.Count; i++)
                {
                    var selectedModule = selectedModules[i];
                    sub.Info.GameVersion = selectedModule.Info.GameVersion;
                    var moduleEntities = MapEntity.LoadAll(sub, selectedModule.Info.SubmarineElement, selectedModule.Info.FilePath, idOffset);
                    idOffset = moduleEntities.Max(e => e.ID);
                    MapEntity.InitializeLoadedLinks(moduleEntities);

                    foreach (MapEntity entity in moduleEntities)
                    {
                        entity.OriginalModuleIndex = i;
                        if (!(entity is Item item)) { continue; }
                        item.GetComponent<Door>()?.RefreshLinkedGap();
                        item.GetComponent<ConnectionPanel>()?.InitializeLinks();
                        item.GetComponent<ItemContainer>()?.OnMapLoaded();
                    }

                    var wallEntities = moduleEntities.Where(e => e is Structure).Cast<Structure>();
                    var hullEntities = moduleEntities.Where(e => e is Hull).Cast<Hull>();

                    // Tell the hulls what tags the module has, used to spawn NPCs on specific rooms
                    foreach (Hull hull in hullEntities)
                    {
                        hull.SetModuleTags(selectedModule.Info.OutpostModuleInfo.ModuleFlags);
                    }
                    
                    selectedModule.HullBounds = new Rectangle(
                        hullEntities.Min(e => e.WorldRect.X), hullEntities.Min(e => e.WorldRect.Y - e.WorldRect.Height),
                        hullEntities.Max(e => e.WorldRect.Right), hullEntities.Max(e => e.WorldRect.Y));
                    selectedModule.HullBounds = new Rectangle(
                        selectedModule.HullBounds.X, selectedModule.HullBounds.Y,
                        selectedModule.HullBounds.Width - selectedModule.HullBounds.X, selectedModule.HullBounds.Height - selectedModule.HullBounds.Y);
                    selectedModule.Bounds = new Rectangle(
                        wallEntities.Min(e => e.WorldRect.X), wallEntities.Min(e => e.WorldRect.Y - e.WorldRect.Height),
                        wallEntities.Max(e => e.WorldRect.Right), wallEntities.Max(e => e.WorldRect.Y));
                    selectedModule.Bounds = new Rectangle(
                        selectedModule.Bounds.X, selectedModule.Bounds.Y,
                        selectedModule.Bounds.Width - selectedModule.Bounds.X, selectedModule.Bounds.Height - selectedModule.Bounds.Y);

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
                        selectedModule.Offset += moveDir * generationParams.MinHallwayLength;
                    }
                    entities[selectedModule] = moduleEntities;
                }

                bool overlapsFound = true;
                int iteration = 0;
                while (overlapsFound)
                {
                    overlapsFound = false;
                    foreach (PlacedModule placedModule in selectedModules)
                    {
                        if (placedModule.PreviousModule == null) { continue; }

                        List<PlacedModule> subsequentModules = new List<PlacedModule>();
                        GetSubsequentModules(placedModule, selectedModules, ref subsequentModules);
                        List<PlacedModule> otherModules = selectedModules.Except(subsequentModules).ToList();

                        int remainingTries = 10;
                        while (FindOverlap(subsequentModules, otherModules, out var module1, out var module2) && remainingTries > 0)
                        {
                            overlapsFound = true;
                            if (FindOverlapSolution(subsequentModules, module1, module2, selectedModules, out Dictionary<PlacedModule,Vector2> solution))
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
                            remainingTries--;         
                        }
                    }
                    iteration++;
                    if (iteration > 10) 
                    {
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
                    allEntities.AddRange(GenerateHallways(sub, locationType, selectedModules, outpostModules, entities));
                    LinkOxygenGenerators(allEntities);
                    LockUnusedDoors(selectedModules, entities);
                    AlignLadders(selectedModules, entities);
                    PowerUpOutpost(entities.SelectMany(e => e.Value));
                }

                return allEntities;
            }
        }

        /// <summary>
        /// Select the number and types of the modules to use in the outpost
        /// </summary>
        private static List<string> SelectModules(IEnumerable<SubmarineInfo> modules, OutpostGenerationParams generationParams)
        {
            int totalModuleCount = generationParams.TotalModuleCount;
            var pendingModuleFlags = new List<string>();
            bool availableModulesFound = true;

            string initialModuleFlag = generationParams.ModuleCounts.FirstOrDefault().Key;
            pendingModuleFlags.Add(initialModuleFlag);
            while (pendingModuleFlags.Count < totalModuleCount && availableModulesFound)
            {
                availableModulesFound = false;
                foreach (var moduleFlag in generationParams.ModuleCounts)
                {
                    if (pendingModuleFlags.Count(m => m == moduleFlag.Key) >= generationParams.GetModuleCount(moduleFlag.Key))
                    {
                        continue;
                    }
                    if (!modules.Any(m => m.OutpostModuleInfo.ModuleFlags.Contains(moduleFlag.Key)))
                    {
                        DebugConsole.ThrowError($"Failed to add a module to the outpost (no modules with the flag \"{moduleFlag.Key}\" found).");
                        continue;
                    }
                    availableModulesFound = true;
                    pendingModuleFlags.Add(moduleFlag.Key);
                }
            }
            pendingModuleFlags.Shuffle(Rand.RandSync.Server);
            while (pendingModuleFlags.Count < totalModuleCount)
            {
                //don't place "none" modules at the end because
                // a. "filler rooms" at the end of a hallway are pointless
                // b. placing the unnecessary filler rooms first give more options for the placement of the more important modules 
                pendingModuleFlags.Insert(Rand.Int(pendingModuleFlags.Count - 1, Rand.RandSync.Server), "none");
            }

            //make sure the initial module is inserted first
            pendingModuleFlags.Remove(initialModuleFlag);
            pendingModuleFlags.Insert(0, initialModuleFlag);

            return pendingModuleFlags;
        }

        /// <summary>
        /// Attaches additional modules to all the available gaps of the given module, 
        /// and continues recursively through the attached modules until all the pending module types have been placed.
        /// </summary>
        /// <param name="currentModule">The module to attach to</param>
        /// <param name="availableModules">Which modules we can choose from</param>
        /// <param name="pendingModuleFlags">Which types of modules we still need in the outpost</param>
        /// <param name="selectedModules">The modules we've already selected to be used in the outpost.</param>
        private static bool AppendToModule(PlacedModule currentModule,
            List<SubmarineInfo> availableModules,
            List<string> pendingModuleFlags,
            List<PlacedModule> selectedModules, 
            LocationType locationType,
            bool retry = true)
        {
            if (pendingModuleFlags.Count == 0) { return true; }

            List<PlacedModule> placedModules = new List<PlacedModule>();
            foreach (OutpostModuleInfo.GapPosition gapPosition in GapPositions().Randomize(Rand.RandSync.Server))
            {
                if (currentModule.UsedGapPositions.HasFlag(gapPosition)) { continue; }
                //don't continue downwards if it'd extend below the airlock
                if (gapPosition == OutpostModuleInfo.GapPosition.Bottom && currentModule.Offset.Y <= 1) { continue; }
                if (currentModule.Info.OutpostModuleInfo.GapPositions.HasFlag(gapPosition))
                {
                    var newModule = AppendModule(currentModule, GetOpposingGapPosition(gapPosition), availableModules, pendingModuleFlags, selectedModules, locationType);
                    if (newModule != null) { placedModules.Add(newModule); }
                    if (pendingModuleFlags.Count == 0) { return true; }
                }
            }

            //couldn't place anything, retry
            if (placedModules.Count == 0 && retry && !selectedModules.Any(m => m != currentModule && m.PreviousModule == currentModule.PreviousModule))
            {
                //try to append to some other module first
                foreach (PlacedModule otherModule in selectedModules)
                {
                   if (AppendToModule(otherModule, availableModules, pendingModuleFlags, selectedModules, locationType, retry: false))
                    {
                        return true;
                    }
                }
                //try to replace the previously placed module with something else that we can append to
                var failedModule = currentModule;
                for (int i = 0; i < 10; i++)
                {
                    selectedModules.Remove(currentModule);
                    //readd the module types that the previous module was supposed to fulfill to the pending module types
                    pendingModuleFlags.AddRange(currentModule.FulfilledModuleTypes);
                    if (!availableModules.Contains(currentModule.Info)) { availableModules.Add(currentModule.Info); }
                    //retry
                    currentModule = AppendModule(currentModule.PreviousModule, currentModule.ThisGapPosition, availableModules, pendingModuleFlags, selectedModules, locationType);
                    if (currentModule == null) { break; }
                    if (AppendToModule(currentModule, availableModules, pendingModuleFlags, selectedModules, locationType, retry: false))
                    {
                        return true;
                    }
                }
                return false;
            }

            foreach (PlacedModule placedModule in placedModules)
            {
                AppendToModule(placedModule, availableModules, pendingModuleFlags, selectedModules, locationType);
            }
            return placedModules.Count > 0;
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
            List<string> pendingModuleFlags,
            List<PlacedModule> selectedModules,
            LocationType locationType)
        {
            if (pendingModuleFlags.Count == 0) { return null; }

            string flagToPlace = "none";
            SubmarineInfo nextModule = null;
            foreach (string moduleFlag in pendingModuleFlags)
            {
                flagToPlace = moduleFlag;
                nextModule = GetRandomModule(currentModule?.Info?.OutpostModuleInfo, availableModules, flagToPlace, gapPosition, locationType);
                if (nextModule != null) { break; }
            }

            if (nextModule != null)
            {
                var newModule = new PlacedModule(nextModule, currentModule, gapPosition)
                {
                    Offset = currentModule.Offset + GetMoveDir(gapPosition),
                };
                foreach (string moduleFlag in nextModule.OutpostModuleInfo.ModuleFlags)
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
                        if (Submarine.RectContains(rect, gapPos1) || Submarine.RectContains(rect, gapPos2) || MathUtils.GetLineRectangleIntersection(gapPos1, gapPos2, rect, out _))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
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
        private static bool FindOverlapSolution(IEnumerable<PlacedModule> movableModules, PlacedModule module1, PlacedModule module2, IEnumerable<PlacedModule> allmodules, out Dictionary<PlacedModule, Vector2> solution)
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
                Vector2 moveDir = GetMoveDir(module.ThisGapPosition);
                Vector2 moveStep = moveDir * 50.0f;
                Vector2 currentMove = Vector2.Zero;
                float maxMoveAmount = 2000.0f;

                List<PlacedModule> subsequentModules2 = new List<PlacedModule>();
                GetSubsequentModules(module, movableModules, ref subsequentModules2);
                while (currentMove.LengthSquared() < maxMoveAmount * maxMoveAmount)
                {
                    currentMove += moveStep;
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
                }
                foreach (PlacedModule movedModule in allmodules)
                {
                    movedModule.MoveOffset = Vector2.Zero;
                }
            }

            return solutionFound;
        }

        private static SubmarineInfo GetRandomModule(IEnumerable<SubmarineInfo> modules, string moduleFlag, LocationType locationType)
        {
            IEnumerable<SubmarineInfo> availableModules = null;
            if (string.IsNullOrEmpty(moduleFlag) || moduleFlag.Equals("none"))
            {
                availableModules = modules.Where(m => !m.OutpostModuleInfo.ModuleFlags.Any() || m.OutpostModuleInfo.ModuleFlags.Contains("none"));
            }
            else
            {
                availableModules = modules.Where(m => m.OutpostModuleInfo.ModuleFlags.Contains(moduleFlag));
            }

            if (availableModules.Count() == 0) { return null; }

            //try to search for modules made specifically for this location type first
            var modulesSuitableForLocationType =
                availableModules.Where(m => m.OutpostModuleInfo.AllowedLocationTypes.Contains(locationType.Identifier.ToLowerInvariant()));

            //if not found, search for modules suitable for any location type
            if (!modulesSuitableForLocationType.Any())
            {
                modulesSuitableForLocationType = availableModules.Where(m => !m.OutpostModuleInfo.AllowedLocationTypes.Any());
            }

            if (!modulesSuitableForLocationType.Any())
            {
                DebugConsole.NewMessage($"Could not find a suitable module for the location type {locationType}. Module flag: {moduleFlag}.", Color.Orange);
                return ToolBox.SelectWeightedRandom(availableModules.ToList(), availableModules.Select(m => m.OutpostModuleInfo.Commonness).ToList(), Rand.RandSync.Server);
            }
            else
            {
                return ToolBox.SelectWeightedRandom(modulesSuitableForLocationType.ToList(), modulesSuitableForLocationType.Select(m => m.OutpostModuleInfo.Commonness).ToList(), Rand.RandSync.Server);
            }
        }

        private static SubmarineInfo GetRandomModule(OutpostModuleInfo prevModule, IEnumerable<SubmarineInfo> modules, string moduleFlag, OutpostModuleInfo.GapPosition gapPosition, LocationType locationType)
        {
            IEnumerable<SubmarineInfo> availableModules = null;
            if (string.IsNullOrEmpty(moduleFlag) || moduleFlag.Equals("none"))
            {
                availableModules = modules
                    .Where(m => !m.OutpostModuleInfo.ModuleFlags.Any() || (m.OutpostModuleInfo.ModuleFlags.Count() == 1 && m.OutpostModuleInfo.ModuleFlags.Contains("none")) && m.OutpostModuleInfo.GapPositions.HasFlag(gapPosition));
            }
            else
            {
                availableModules = modules
                    .Where(m => m.OutpostModuleInfo.ModuleFlags.Contains(moduleFlag) && m.OutpostModuleInfo.GapPositions.HasFlag(gapPosition));
            }
            if (prevModule != null)
            {
                availableModules = availableModules.Where(m => CanAttachTo(m.OutpostModuleInfo, prevModule) && CanAttachTo(prevModule, m.OutpostModuleInfo));
            }

            if (availableModules.Count() == 0) { return null; }

            //try to search for modules made specifically for this location type first
            var modulesSuitableForLocationType =
                availableModules.Where(m => m.OutpostModuleInfo.AllowedLocationTypes.Contains(locationType.Identifier.ToLowerInvariant()));

            //if not found, search for modules suitable for any location type
            if (!modulesSuitableForLocationType.Any())
            {
                modulesSuitableForLocationType = availableModules.Where(m => !m.OutpostModuleInfo.AllowedLocationTypes.Any());
            }

            if (!modulesSuitableForLocationType.Any())
            {
                DebugConsole.NewMessage($"Could not find a suitable module for the location type {locationType}. Module flag: {moduleFlag}.", Color.Orange);
                return ToolBox.SelectWeightedRandom(availableModules.ToList(), availableModules.Select(m => m.OutpostModuleInfo.Commonness).ToList(), Rand.RandSync.Server);
            }
            else
            {
                return ToolBox.SelectWeightedRandom(modulesSuitableForLocationType.ToList(), modulesSuitableForLocationType.Select(m => m.OutpostModuleInfo.Commonness).ToList(), Rand.RandSync.Server);
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

        private static IEnumerable<OutpostModuleInfo.GapPosition> GapPositions()
        {
            yield return OutpostModuleInfo.GapPosition.Right;
            yield return OutpostModuleInfo.GapPosition.Left;
            yield return OutpostModuleInfo.GapPosition.Top;
            yield return OutpostModuleInfo.GapPosition.Bottom;
        }

        private static OutpostModuleInfo.GapPosition GetOpposingGapPosition(OutpostModuleInfo.GapPosition thisGapPosition)
        {
            return thisGapPosition switch
            {
                OutpostModuleInfo.GapPosition.Right => OutpostModuleInfo.GapPosition.Left,
                OutpostModuleInfo.GapPosition.Left => OutpostModuleInfo.GapPosition.Right,
                OutpostModuleInfo.GapPosition.Bottom => OutpostModuleInfo.GapPosition.Top,
                OutpostModuleInfo.GapPosition.Top => OutpostModuleInfo.GapPosition.Bottom,
                OutpostModuleInfo.GapPosition.None => OutpostModuleInfo.GapPosition.None,
                _ => throw new InvalidOperationException()
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
                _ => throw new InvalidOperationException()
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
            if (!from.AllowAttachToModules.Any() || from.AllowAttachToModules.All(s => s.Equals("any", StringComparison.OrdinalIgnoreCase))) { return true; }                
            return from.AllowAttachToModules.Any(s => to.ModuleFlags.Contains(s));
        }

        private static List<MapEntity> GenerateHallways(Submarine sub, LocationType locationType, IEnumerable<PlacedModule> placedModules, IEnumerable<SubmarineInfo> availableModules, Dictionary<PlacedModule, List<MapEntity>> allEntities)
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
                        var wirePrefab = MapEntityPrefab.Find(name: null, identifier: thisJunctionBox.Connections[i].IsPower ? "redwire" : "bluewire") as ItemPrefab;
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
                        wire.Connect(thisJunctionBox.Connections[i], addNode: false);
                        wire.Connect(previousJunctionBox.Connections[i], addNode: false);
                        wire.SetNodes(new List<Vector2>());
                    }
                }

                bool isHorizontal =
                    module.ThisGapPosition == OutpostModuleInfo.GapPosition.Left ||
                    module.ThisGapPosition == OutpostModuleInfo.GapPosition.Right;

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
                                c.Wires.ForEach(w => w?.Item.Remove());
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

                        gapToRemove.ConnectedDoor?.Item.Remove(); 
                        if (hallwayLength <= 1.0f) { gapToRemove?.Remove(); }
                    }
                }

                if (hallwayLength <= 1.0f) { continue; }

                var suitableModules = availableModules.Where(m =>
                        m.OutpostModuleInfo.AllowAttachToModules.Any(s => module.Info.OutpostModuleInfo.ModuleFlags.Contains(s)) &&
                        m.OutpostModuleInfo.AllowAttachToModules.Any(s => module.PreviousModule.Info.OutpostModuleInfo.ModuleFlags.Contains(s)));
                if (suitableModules.Count() == 0)
                {
                    suitableModules = availableModules.Where(m =>
                                        !m.OutpostModuleInfo.AllowAttachToModules.Any() ||
                                        m.OutpostModuleInfo.AllowAttachToModules.All(s => s.Equals("any", StringComparison.OrdinalIgnoreCase)));
                }
                var hallwayInfo = GetRandomModule(suitableModules, isHorizontal ? "hallwayhorizontal" : "hallwayvertical", locationType);
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
                        DebugConsole.ThrowError($"Failed to connect waypoints between outpost modules. No waypoint in the {GetOpposingGapPosition(module.ThisGapPosition).ToString().ToLower()} gap of the module \"{module.Info.Name}\".");
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
                        var temp = startWaypoint;
                        startWaypoint = endWaypoint;
                        endWaypoint = temp;
                    }

                    if (hallwayLength > 100 && isHorizontal)
                    {
                        WayPoint prevWayPoint = startWaypoint;
                        for (float x = leftHull.Rect.Right + 50; x < rightHull.Rect.X - 50; x += 100.0f)
                        {
                            var newWayPoint = new WayPoint(new Vector2(x, hullBounds.Y + 110.0f), SpawnType.Path, sub);
                            prevWayPoint.linkedTo.Add(newWayPoint);
                            newWayPoint.linkedTo.Add(prevWayPoint);
                            prevWayPoint = newWayPoint;
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

                    WayPoint closestWaypoint = null;
                    float closestDistSqr = 30.0f * 30.0f;
                    foreach (WayPoint waypoint in WayPoint.WayPointList)
                    {
                        if (waypoint == startWaypoint) { continue; }
                        float dist = Vector2.DistanceSquared(waypoint.WorldPosition, startWaypoint.WorldPosition);
                        if (dist < closestDistSqr)
                        {
                            closestWaypoint = waypoint;
                            closestDistSqr = dist;
                        }
                    }
                    if (closestWaypoint != null)
                    {
                        startWaypoint.linkedTo.Add(closestWaypoint);
                        closestWaypoint.linkedTo.Add(startWaypoint);
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

        private static void LockUnusedDoors(IEnumerable<PlacedModule> placedModules, Dictionary<PlacedModule, List<MapEntity>> entities)
        {
            foreach (PlacedModule module in placedModules)
            {
                foreach (MapEntity me in entities[module])
                {
                    var gap = me as Gap;
                    if (gap == null) { continue; }
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
                    else
                    {
                        gap.Remove();
                        WayPoint.WayPointList.Where(wp => wp.ConnectedGap == gap).ForEachMod(wp => wp.Remove());
                    }                    
                }
                entities[module].RemoveAll(e => e.Removed);
            }

            static bool ShouldRemoveLinkedEntity(MapEntity e, bool doorInUse, PlacedModule module)
            {
                if (e is Item it && it.GetComponent<Ladder>() != null)
                {
                    if (module.UsedGapPositions.HasFlag(OutpostModuleInfo.GapPosition.Top) || module.UsedGapPositions.HasFlag(OutpostModuleInfo.GapPosition.Bottom))
                    {
                        return false;
                    }
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
                if (linked is Item linkedItem && linkedItem.Connections != null)
                {
                    foreach (Connection connection in linkedItem.Connections)
                    {
                        foreach (Wire w in connection.Wires)
                        {
                            w?.Item.Remove();
                        }
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
                        if (startWaypoint != null && endWaypoint != null)
                        {
                            WayPoint prevWaypoint = startWaypoint;
                            for (float y = startWaypoint.Position.Y + WayPoint.LadderWaypointInterval; y <= endWaypoint.Position.Y - WayPoint.LadderWaypointInterval; y += WayPoint.LadderWaypointInterval)
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

        private static void PowerUpOutpost(IEnumerable<MapEntity> entities)
        {
            foreach (Entity e in entities)
            {
                if (!(e is Item item)) { continue; }
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
                foreach (Entity e in entities)
                {
                    if (!(e is Item item) || item.GetComponent<Powered>() == null) { continue; }
                    item.Update((float)Timing.Step, GameMain.GameScreen.Cam);
                }
            }
        }

        public static void SpawnNPCs(Location location, Submarine outpost)
        {
            if (outpost?.Info?.OutpostGenerationParams == null) { return; }

            List<HumanPrefab> killedCharacters = new List<HumanPrefab>();
            Dictionary<HumanPrefab, CharacterInfo> selectedCharacters = new Dictionary<HumanPrefab, CharacterInfo>();
            foreach (HumanPrefab humanPrefab in outpost.Info.OutpostGenerationParams.GetHumanPrefabs(Rand.RandSync.Server))
            {
                var characterInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobPrefab: humanPrefab.GetJobPrefab(Rand.RandSync.Server), randSync: Rand.RandSync.Server);
                if (location != null && location.KilledCharacterIdentifiers.Contains(characterInfo.GetIdentifier())) 
                {
                    killedCharacters.Add(humanPrefab);
                    continue; 
                }
                selectedCharacters.Add(humanPrefab, characterInfo);
            }

            //replace killed characters with new ones
            foreach (HumanPrefab killedCharacter in killedCharacters)
            {
                int tries = 0;
                while (tries < 100)
                {
                    var characterInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobPrefab: killedCharacter.GetJobPrefab(Rand.RandSync.Server), randSync: Rand.RandSync.Server);
                    if (!location.KilledCharacterIdentifiers.Contains(characterInfo.GetIdentifier()))
                    {
                        selectedCharacters.Add(killedCharacter, characterInfo);
                        break;
                    }
                }
            }

            foreach (var selectedCharacter in selectedCharacters)
            {
                HumanPrefab humanPrefab = selectedCharacter.Key;
                CharacterInfo characterInfo = selectedCharacter.Value;

                Rand.SetSyncedSeed(ToolBox.StringToInt(characterInfo.Name));

                ISpatialEntity gotoTarget = SpawnAction.GetSpawnPos(SpawnAction.SpawnLocationType.Outpost, SpawnType.Human, humanPrefab.GetModuleFlags(), humanPrefab.GetSpawnPointTags());
                if (gotoTarget == null)
                {
                    gotoTarget = outpost.GetHulls(true).GetRandom(Rand.RandSync.Server);
                }
                characterInfo.TeamID = CharacterTeamType.FriendlyNPC;
                var npc = Character.Create(CharacterPrefab.HumanConfigFile, SpawnAction.OffsetSpawnPos(gotoTarget.WorldPosition, 100.0f), ToolBox.RandomSeed(8), characterInfo, hasAi: true, createNetworkEvent: true);
                npc.AnimController.FindHull(gotoTarget.WorldPosition, true);
                npc.TeamID = CharacterTeamType.FriendlyNPC;
                npc.Prefab = humanPrefab;
                if (!outpost.Info.OutpostNPCs.ContainsKey(humanPrefab.Identifier))
                {
                    outpost.Info.OutpostNPCs.Add(humanPrefab.Identifier, new List<Character>());
                }
                outpost.Info.OutpostNPCs[humanPrefab.Identifier].Add(npc);
                if (GameMain.NetworkMember?.ServerSettings != null && !GameMain.NetworkMember.ServerSettings.KillableNPCs)
                {
                    npc.CharacterHealth.Unkillable = true;
                }
                else
                {
                    npc.AddStaticHealthMultiplier(humanPrefab.HealthMultiplier);
                }
                humanPrefab.GiveItems(npc, outpost, Rand.RandSync.Server);
                foreach (Item item in npc.Inventory.FindAllItems(it => it != null, recursive: true))
                {
                    item.AllowStealing = outpost.Info.OutpostGenerationParams.AllowStealing;
                    item.SpawnedInOutpost = true;
                }
                npc.GiveIdCardTags(gotoTarget as WayPoint);
                humanPrefab.InitializeCharacter(npc, gotoTarget);
            }
        }
    }
}
