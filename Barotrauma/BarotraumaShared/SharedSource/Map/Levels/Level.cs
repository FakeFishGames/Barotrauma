using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Barotrauma.RuinGeneration;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Voronoi2;

namespace Barotrauma
{
    partial class Level : Entity, IServerSerializable
    {
        //all entities are disabled after they reach this depth
        public const int MaxEntityDepth = -300000;
        public const float ShaftHeight = 1000.0f;
        /// <summary>
        /// The level generator won't try to adjust the width of the main path above this limit.
        /// </summary>
        public const int MaxSubmarineWidth = 16000;

        public static Level Loaded { get; private set; }

        [Flags]
        public enum PositionType
        {
            MainPath = 1, Cave = 2, Ruin = 4, Wreck = 8
        }

        public struct InterestingPosition
        {
            public Point Position;
            public readonly PositionType PositionType;
            public bool IsValid;
            public Submarine Submarine;
            public Ruin Ruin;

            public InterestingPosition(Point position, PositionType positionType, bool isValid = true, Submarine submarine = null, Ruin ruin = null)
            {
                Position = position;
                PositionType = positionType;
                IsValid = isValid;
                Submarine = submarine;
                Ruin = ruin;
            }
        }

        //how close the sub has to be to start/endposition to exit
        public const float ExitDistance = 6000.0f;
        public const int GridCellSize = 2000;
        private List<VoronoiCell>[,] cellGrid;
        private List<VoronoiCell> cells;
        
        private Point startPosition, endPosition;

        private readonly Rectangle borders;

        private List<Body> bodies;

        private List<Point> bottomPositions;

        //no need for frequent network updates, as currently the only thing that's synced
        //are the slowly moving ice chunks that move in a very predictable way
        const float NetworkUpdateInterval = 5.0f;
        private float networkUpdateTimer;

        public Vector2 StartPosition
        {
            get { return startPosition.ToVector2(); }
        }

        public Point Size
        {
            get { return LevelData.Size; }
        }

        public Vector2 EndPosition
        {
            get { return endPosition.ToVector2(); }
        }

        public int BottomPos
        {
            get;
            private set;
        }

        public int SeaFloorTopPos
        {
            get;
            private set;
        }

        public LevelWall SeaFloor { get; private set; }

        public List<Ruin> Ruins { get; private set; }

        public List<Submarine> Wrecks { get; private set; }

        public List<LevelWall> ExtraWalls { get; private set; }

        public List<List<Point>> SmallTunnels { get; private set; } = new List<List<Point>>();

        public List<InterestingPosition> PositionsOfInterest { get; private set; }

        public Submarine StartOutpost { get; private set; }
        public Submarine EndOutpost { get; private set; }

        private SubmarineInfo preSelectedStartOutpost;
        private SubmarineInfo preSelectedEndOutpost;

        public readonly LevelData LevelData;

        /// <summary>
        /// Random integers generated during the level generation. If these values differ between clients/server,
        /// it means the levels aren't identical for some reason and there will most likely be major ID mismatches.
        /// </summary>
        public List<int> EqualityCheckValues
        {
            get;
            private set;
        } = new List<int>();

        public List<Entity> EntitiesBeforeGenerate { get; private set; } = new List<Entity>();
        public int EntityCountBeforeGenerate { get; private set; }
        public int EntityCountAfterGenerate { get; private set; }

        public Body TopBarrier
        {
            get;
            private set;
        }

        public Body BottomBarrier
        {
            get;
            private set;
        }

        public LevelObjectManager LevelObjectManager { get; private set; }

        public bool Generating { get; private set; }

        public Location StartLocation { get; private set; }
        public Location EndLocation { get; private set; }

        public bool Mirrored
        {
            get;
            private set;
        }

        public string Seed 
        {
            get { return LevelData.Seed; }
        }

        public float Difficulty
        {
            get { return LevelData.Difficulty; }
        }

        public LevelData.LevelType Type
        {
            get { return LevelData.Type; }
        }

        /// <summary>
        /// Is there a loaded level set and is it an outpost?
        /// </summary>
        public static bool IsLoadedOutpost => Loaded?.Type == LevelData.LevelType.Outpost;

        public LevelGenerationParams GenerationParams
        {
            get { return LevelData.GenerationParams; }
        }

        public Color BackgroundTextureColor
        {
            get { return LevelData.GenerationParams.BackgroundTextureColor; }
        }

        public Color BackgroundColor
        {
            get { return LevelData.GenerationParams.BackgroundColor; }
        }

        public Color WallColor
        {
            get { return LevelData.GenerationParams.WallColor; }
        }

        private Level(LevelData levelData) : base(null)
        {
            this.LevelData = levelData;
            borders = new Rectangle(Point.Zero, levelData.Size);

            //remove from entity dictionary
            base.Remove();
        }

        public static Level Generate(LevelData levelData, bool mirror, SubmarineInfo startOutpost = null, SubmarineInfo endOutpost = null)
        {
            Debug.Assert(levelData.Biome != null);
            if (levelData.Biome == null) { throw new ArgumentException("Biome was null"); }
            if (levelData.Size.X <= 0) { throw new ArgumentException("Level width needs to be larger than zero."); }
            if (levelData.Size.Y <= 0) { throw new ArgumentException("Level height needs to be larger than zero."); }

            Level level = new Level(levelData)
            {
                preSelectedStartOutpost = startOutpost,
                preSelectedEndOutpost = endOutpost
            };
            level.Generate(mirror);
            return level;
        }

        private void Generate(bool mirror)
        {
            if (Loaded != null) { Loaded.Remove(); }
            Loaded = this;
            Generating = true;

            EqualityCheckValues.Clear();
            EntitiesBeforeGenerate = GetEntities().ToList();
            EntityCountBeforeGenerate = EntitiesBeforeGenerate.Count();

            StartLocation = GameMain.GameSession?.StartLocation;
            EndLocation = GameMain.GameSession?.EndLocation;

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            LevelObjectManager = new LevelObjectManager();

            if (Type == LevelData.LevelType.Outpost) { mirror = false; }
            Mirrored = mirror;

#if CLIENT
            if (backgroundCreatureManager == null)
            {
                var files = GameMain.Instance.GetFilesOfType(ContentType.BackgroundCreaturePrefabs);
                if (files.Count() > 0)
                    backgroundCreatureManager = new BackgroundCreatureManager(files);
                else
                    backgroundCreatureManager = new BackgroundCreatureManager("Content/BackgroundCreatures/BackgroundCreaturePrefabs.xml");
            }
#endif
            Stopwatch sw = new Stopwatch();
            sw.Start();
            
            PositionsOfInterest = new List<InterestingPosition>();
            ExtraWalls = new List<LevelWall>();
            bodies = new List<Body>();
            List<Vector2> sites = new List<Vector2>();
            
            Voronoi voronoi = new Voronoi(1.0);
            Rand.SetSyncedSeed(ToolBox.StringToInt(Seed));

#if CLIENT
            renderer = new LevelRenderer(this);
#endif

            SeaFloorTopPos = GenerationParams.SeaFloorDepth + GenerationParams.MountainHeightMax + GenerationParams.SeaFloorVariance;

            int minWidth = Math.Min(GenerationParams.MinTunnelRadius, MaxSubmarineWidth);
            if (Submarine.MainSub != null)
            {
                Rectangle dockedSubBorders = Submarine.MainSub.GetDockedBorders();
                dockedSubBorders.Inflate(dockedSubBorders.Size.ToVector2() * 0.15f);
                minWidth = Math.Max(minWidth, Math.Max(dockedSubBorders.Width, dockedSubBorders.Height));
                minWidth = Math.Min(minWidth, MaxSubmarineWidth);
            }
            minWidth = Math.Min(minWidth, borders.Width / 5);

            Rectangle pathBorders = borders;
            pathBorders.Inflate(
                -Math.Min(Math.Min(minWidth * 2, MaxSubmarineWidth), borders.Width / 5), 
                -Math.Min(minWidth, borders.Height / 5));

            if (pathBorders.Width <= 0) { throw new InvalidOperationException($"The width of the level's path area is invalid ({pathBorders.Width})"); }
            if (pathBorders.Height <= 0) { throw new InvalidOperationException($"The height of the level's path area is invalid ({pathBorders.Height})"); }

            startPosition = new Point(
               (int)MathHelper.Lerp(minWidth, borders.Width - minWidth, GenerationParams.StartPosition.X),
               (int)MathHelper.Lerp(borders.Bottom - minWidth, borders.Y + minWidth, GenerationParams.StartPosition.Y));
            endPosition = new Point(
               (int)MathHelper.Lerp(minWidth, borders.Width - minWidth, GenerationParams.EndPosition.X),
               (int)MathHelper.Lerp(borders.Bottom - minWidth, borders.Y + minWidth, GenerationParams.EndPosition.Y));

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            //----------------------------------------------------------------------------------
            //generate the initial nodes for the main path and smaller tunnels
            //----------------------------------------------------------------------------------

            List<Point> pathNodes = new List<Point> { startPosition };

            Point nodeInterval = GenerationParams.MainPathNodeIntervalRange;

            for (int  x = startPosition.X + nodeInterval.X;
                        x < endPosition.X  - nodeInterval.X;
                        x += Rand.Range(nodeInterval.X, nodeInterval.Y, Rand.RandSync.Server))
            {
                pathNodes.Add(new Point(x, Rand.Range(pathBorders.Y, pathBorders.Bottom, Rand.RandSync.Server)));
            }

            if (pathNodes.Count == 1)
            {
                pathNodes.Add(new Point(pathBorders.Center.X, pathBorders.Y));
            }
            //if all nodes ended up high up in the level, move one down to make sure we utilize the full height of the level
            else if (pathNodes.GetRange(1, pathNodes.Count - 1).All(p => p.Y > pathBorders.Y + pathBorders.Height * 0.25f))
            {
                int nodeIndex = Rand.Range(1, pathNodes.Count, Rand.RandSync.Server);
                pathNodes[nodeIndex] = new Point(pathNodes[nodeIndex].X, pathBorders.Y);
            }

            pathNodes.Add(endPosition);

            GenerateTunnels(pathNodes, minWidth);

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            //----------------------------------------------------------------------------------
            //generate voronoi sites
            //----------------------------------------------------------------------------------

            Point siteInterval = GenerationParams.VoronoiSiteInterval;
            int siteIntervalSqr = (siteInterval.X * siteInterval.X + siteInterval.Y * siteInterval.Y);
            Point siteVariance = GenerationParams.VoronoiSiteVariance;
            List<double> siteCoordsX = new List<double>((borders.Height / siteInterval.Y) * (borders.Width / siteInterval.Y));
            List<double> siteCoordsY = new List<double>((borders.Height / siteInterval.Y) * (borders.Width / siteInterval.Y));
            for (int x = siteInterval.X / 2; x < borders.Width; x += siteInterval.X)
            {
                for (int y = siteInterval.Y / 2; y < borders.Height; y += siteInterval.Y)
                {
                    int siteX = x + Rand.Range(-siteVariance.X, siteVariance.X, Rand.RandSync.Server);
                    int siteY = y + Rand.Range(-siteVariance.Y, siteVariance.Y, Rand.RandSync.Server);

                    if (SmallTunnels.Any(t => t.Any(node => MathUtils.DistanceSquared(node.X, node.Y, siteX, siteY) < siteIntervalSqr)))
                    {
                        //add some more sites around the small tunnels to generate more small voronoi cells
                        if (x < borders.Width - siteInterval.X)
                        {
                            siteCoordsX.Add(x + siteInterval.X / 2);
                            siteCoordsY.Add(y);
                        }
                        if (y < borders.Height - siteInterval.Y)
                        {
                            siteCoordsX.Add(x);
                            siteCoordsY.Add(y + siteInterval.Y / 2);
                        }
                        if (x < borders.Width - siteInterval.X && y < borders.Height - siteInterval.Y)
                        {
                            siteCoordsX.Add(x + siteInterval.X / 2);
                            siteCoordsY.Add(y + siteInterval.Y / 2);
                        }
                    }

                    siteCoordsX.Add(siteX);
                    siteCoordsY.Add(siteY);
                }
            }

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            //----------------------------------------------------------------------------------
            // construct the voronoi graph and cells
            //----------------------------------------------------------------------------------

            Stopwatch sw2 = new Stopwatch();
            sw2.Start();

            Debug.Assert(siteCoordsX.Count == siteCoordsY.Count);
            List<GraphEdge> graphEdges = voronoi.MakeVoronoiGraph(siteCoordsX.ToArray(), siteCoordsY.ToArray(), borders.Width, borders.Height);

            Debug.WriteLine("MakeVoronoiGraph: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();
            
            //construct voronoi cells based on the graph edges
            cells = CaveGenerator.GraphEdgesToCells(graphEdges, borders, GridCellSize, out cellGrid);
            
            Debug.WriteLine("find cells: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();
            
            //----------------------------------------------------------------------------------
            // generate a path through the initial path nodes
            //----------------------------------------------------------------------------------

            List<VoronoiCell> mainPath = CaveGenerator.GeneratePath(pathNodes, cells, cellGrid, GridCellSize,                
                new Rectangle(pathBorders.X, pathBorders.Y, pathBorders.Width, borders.Height), 0.5f, false);

            for (int i = 2; i < mainPath.Count; i += 3)
            {
                PositionsOfInterest.Add(new InterestingPosition(
                    new Point((int)mainPath[i].Site.Coord.X, (int)mainPath[i].Site.Coord.Y), 
                    PositionType.MainPath));
            }

            List<VoronoiCell> pathCells = new List<VoronoiCell>(mainPath);

            //make sure the path is wide enough to pass through
            EnlargeMainPath(pathCells, minWidth);

            foreach (InterestingPosition positionOfInterest in PositionsOfInterest)
            {
                WayPoint wayPoint = new WayPoint(
                    positionOfInterest.Position.ToVector2(),
                    SpawnType.Enemy,
                    submarine: null);
            }

            startPosition.X = (int)pathCells[0].Site.Coord.X;

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            //----------------------------------------------------------------------------------
            // tunnels through the tunnel nodes
            //----------------------------------------------------------------------------------

            List<List<Point>> validTunnels = new List<List<Point>>();
            foreach (List<Point> tunnel in SmallTunnels)
            {
                if (tunnel.Count < 2) continue;

                //find the cell which the path starts from
                int startCellIndex = CaveGenerator.FindCellIndex(tunnel[0], cells, cellGrid, GridCellSize, 1);
                if (startCellIndex < 0) continue;

                //if it wasn't one of the cells in the main path, don't create a tunnel
                if (cells[startCellIndex].CellType != CellType.Path) continue;

                int mainPathCellCount = 0;
                for (int j = 0; j < tunnel.Count; j++)
                {
                    int tunnelCellIndex = CaveGenerator.FindCellIndex(tunnel[j], cells, cellGrid, GridCellSize, 1);
                    if (tunnelCellIndex > -1 && cells[tunnelCellIndex].CellType == CellType.Path) mainPathCellCount++;
                }
                if (mainPathCellCount > tunnel.Count / 2) continue;

                var newPathCells = CaveGenerator.GeneratePath(tunnel, cells, cellGrid, GridCellSize, pathBorders);
                if (newPathCells.Any())
                {
                    PositionsOfInterest.Add(new InterestingPosition(newPathCells.Last().Center.ToPoint(), PositionType.Cave));
                    if (newPathCells.Count > 4) { PositionsOfInterest.Add(new InterestingPosition(newPathCells[newPathCells.Count / 2].Center.ToPoint(), PositionType.Cave)); }
                }
                validTunnels.Add(tunnel);
                pathCells.AddRange(newPathCells);
            }
            SmallTunnels = validTunnels;

            sw2.Restart();


            //----------------------------------------------------------------------------------
            // remove unnecessary cells and create some holes at the bottom of the level
            //----------------------------------------------------------------------------------
            
            cells = CleanCells(pathCells);

            int xPadding = borders.Width / 5;
            int yPadding = borders.Height / 5;
            pathCells.AddRange(CreateHoles(GenerationParams.BottomHoleProbability, new Rectangle(
                xPadding, 0,
                borders.Width - xPadding * 2, borders.Height - yPadding), minWidth));

            foreach (VoronoiCell cell in cells)
            {
                if (cell.Site.Coord.Y < borders.Height / 2) { continue; }
                cell.Edges.ForEach(e => e.OutsideLevel = true);
            }

            //----------------------------------------------------------------------------------
            // initialize the cells that are still left and insert them into the cell grid
            //----------------------------------------------------------------------------------
            
            foreach (VoronoiCell cell in pathCells)
            {
                cell.Edges.ForEach(e => e.OutsideLevel = false);

                cell.CellType = CellType.Path;
                cells.Remove(cell);
            }
            
            for (int x = 0; x < cellGrid.GetLength(0); x++)
            {
                for (int y = 0; y < cellGrid.GetLength(1); y++)
                {
                    cellGrid[x, y].Clear();
                }
            }

            //----------------------------------------------------------------------------------
            // mirror if needed
            //----------------------------------------------------------------------------------
            
            if (mirror)
            {
                HashSet<GraphEdge> mirroredEdges = new HashSet<GraphEdge>();
                HashSet<Site> mirroredSites = new HashSet<Site>();
                List<VoronoiCell> allCells = new List<VoronoiCell>(cells);
                allCells.AddRange(pathCells);
                foreach (VoronoiCell cell in allCells)
                {
                    foreach (GraphEdge edge in cell.Edges)
                    {
                        if (mirroredEdges.Contains(edge)) { continue; }
                        edge.Point1.X = borders.Width - edge.Point1.X;
                        edge.Point2.X = borders.Width - edge.Point2.X;
                        if (edge.Site1 != null && !mirroredSites.Contains(edge.Site1))
                        {
                            //make sure that sites right at the edge of a grid cell end up in the same cell as in the non-mirrored level
                            if (edge.Site1.Coord.X % GridCellSize < 1.0f &&
                                edge.Site1.Coord.X % GridCellSize >= 0.0f) { edge.Site1.Coord.X += 1.0f; }
                            edge.Site1.Coord.X = borders.Width - edge.Site1.Coord.X;
                            mirroredSites.Add(edge.Site1);
                        }
                        if (edge.Site2 != null && !mirroredSites.Contains(edge.Site2))
                        {
                            if (edge.Site2.Coord.X % GridCellSize < 1.0f &&
                                edge.Site2.Coord.X % GridCellSize >= 0.0f) { edge.Site2.Coord.X += 1.0f; }
                            edge.Site2.Coord.X = borders.Width - edge.Site2.Coord.X;
                            mirroredSites.Add(edge.Site2);
                        }
                        mirroredEdges.Add(edge);
                    }
                }


                foreach (List<Point> smallTunnel in SmallTunnels)
                {
                    for (int i = 0; i < smallTunnel.Count; i++)
                    {
                        smallTunnel[i] = new Point(borders.Width - smallTunnel[i].X, smallTunnel[i].Y);
                    }
                }

                for (int i = 0; i < PositionsOfInterest.Count; i++)
                {
                    PositionsOfInterest[i] = new InterestingPosition(
                        new Point(borders.Width - PositionsOfInterest[i].Position.X, PositionsOfInterest[i].Position.Y),
                        PositionsOfInterest[i].PositionType);
                }

                foreach (WayPoint waypoint in WayPoint.WayPointList)
                {
                    if (waypoint.Submarine != null) continue;
                    waypoint.Move(new Vector2((borders.Width / 2 - waypoint.Position.X) * 2, 0.0f));
                }

                startPosition.X = borders.Width - startPosition.X;
                endPosition.X = borders.Width - endPosition.X;
            }

            foreach (VoronoiCell cell in cells)
            {
                int x = (int)Math.Floor(cell.Site.Coord.X / GridCellSize);
                int y = (int)Math.Floor(cell.Site.Coord.Y / GridCellSize);

                if (x < 0 || y < 0 || x >= cellGrid.GetLength(0) || y >= cellGrid.GetLength(1)) continue;

                cellGrid[x, y].Add(cell);
            }

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            //----------------------------------------------------------------------------------
            // create some ruins
            //----------------------------------------------------------------------------------

            Ruins = new List<Ruin>();
            for (int i = 0; i < GenerationParams.RuinCount; i++)
            {
                GenerateRuin(mainPath, mirror);
            }

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            //----------------------------------------------------------------------------------
            // create floating ice chunks
            //----------------------------------------------------------------------------------

            if (GenerationParams.FloatingIceChunkCount > 0)
            {
                List<Point> iceChunkPositions = new List<Point>();
                foreach (InterestingPosition pos in PositionsOfInterest)
                {
                    if (pos.PositionType != PositionType.MainPath || pos.Position.X < 5000 || pos.Position.X > Size.X - 5000) continue;
                    if (Math.Abs(pos.Position.X - StartPosition.X) < minWidth * 2 || Math.Abs(pos.Position.X - EndPosition.X) < minWidth * 2) continue;
                    if (GetTooCloseCells(pos.Position.ToVector2(), minWidth * 0.7f).Count > 0) continue;
                    iceChunkPositions.Add(pos.Position);
                }
                        
                for (int i = 0; i < GenerationParams.FloatingIceChunkCount; i++)
                {
                    if (iceChunkPositions.Count == 0) break;
                    Point selectedPos = iceChunkPositions[Rand.Int(iceChunkPositions.Count, Rand.RandSync.Server)];
                    float chunkRadius = Rand.Range(500.0f, 1000.0f, Rand.RandSync.Server);
                    var newChunk = new LevelWall(CaveGenerator.CreateRandomChunk(chunkRadius, 8, chunkRadius * 0.8f), Color.White, this, true)
                    {
                        MoveSpeed = Rand.Range(100.0f, 200.0f, Rand.RandSync.Server),
                        MoveAmount = new Vector2(0.0f, minWidth * 0.7f)
                    };
                    newChunk.Body.Position = ConvertUnits.ToSimUnits(selectedPos.ToVector2());
                    newChunk.Body.BodyType = BodyType.Dynamic;
                    newChunk.Body.FixedRotation = true;
                    newChunk.Body.LinearDamping = 0.5f;
                    newChunk.Body.IgnoreGravity = true;
                    newChunk.Body.Mass *= 10.0f;
                    ExtraWalls.Add(newChunk);
                    iceChunkPositions.Remove(selectedPos);
                }
            }

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            //----------------------------------------------------------------------------------
            // generate the bodies and rendered triangles of the cells
            //----------------------------------------------------------------------------------

            foreach (VoronoiCell cell in cells)
            {
                foreach (GraphEdge ge in cell.Edges)
                {
                    VoronoiCell adjacentCell = ge.AdjacentCell(cell);
                    ge.IsSolid = (adjacentCell == null || !cells.Contains(adjacentCell));
                }
            }

            List<VoronoiCell> cellsWithBody = new List<VoronoiCell>(cells);
            if (GenerationParams.CellRoundingAmount > 0.01f || GenerationParams.CellIrregularity > 0.01f)
            {
                foreach (VoronoiCell cell in cellsWithBody)
                {
                    CaveGenerator.RoundCell(cell,
                        minEdgeLength: GenerationParams.CellSubdivisionLength,
                        roundingAmount: GenerationParams.CellRoundingAmount,
                        irregularity: GenerationParams.CellIrregularity);
                }
            }

            bodies.Add(CaveGenerator.GeneratePolygons(cellsWithBody, this, out List<Vector2[]> triangles));

#if CLIENT
            renderer.SetBodyVertices(CaveGenerator.GenerateRenderVerticeList(triangles).ToArray(), GenerationParams.WallColor);
            renderer.SetWallVertices(CaveGenerator.GenerateWallShapes(cellsWithBody, this), GenerationParams.WallColor);
#endif

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            //----------------------------------------------------------------------------------
            // create outposts at the start and end of the level
            //----------------------------------------------------------------------------------

            CreateOutposts();

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

            //----------------------------------------------------------------------------------
            // top barrier & sea floor
            //----------------------------------------------------------------------------------

            TopBarrier = GameMain.World.CreateEdge( 
                ConvertUnits.ToSimUnits(new Vector2(borders.X, 0)), 
                ConvertUnits.ToSimUnits(new Vector2(borders.Right, 0)));

            TopBarrier.SetTransform(ConvertUnits.ToSimUnits(new Vector2(0.0f, borders.Height)), 0.0f);                
            TopBarrier.BodyType = BodyType.Static;
            TopBarrier.CollisionCategories = Physics.CollisionLevel;

            bodies.Add(TopBarrier);

            GenerateSeaFloor(mirror);

            if (mirror)
            {
                Point temp = startPosition;
                startPosition = endPosition;
                endPosition = temp;
            }
            if (StartOutpost != null)
            {
                startPosition = new Point((int)StartOutpost.WorldPosition.X, (int)StartOutpost.WorldPosition.Y);
            }
            if (EndOutpost != null)
            {
                endPosition = new Point((int)EndOutpost.WorldPosition.X, (int)EndOutpost.WorldPosition.Y);
            }

            CreateWrecks();
            LevelObjectManager.PlaceObjects(this, GenerationParams.LevelObjectAmount);
            GenerateItems();

            EqualityCheckValues.Add(Rand.Int(int.MaxValue, Rand.RandSync.Server));

#if CLIENT
            backgroundCreatureManager.SpawnSprites(80);
#endif

            foreach (VoronoiCell cell in cells)
            {
                foreach (GraphEdge edge in cell.Edges)
                {
                    edge.Cell1 = null;
                    edge.Cell2 = null;
                    edge.Site1 = null;
                    edge.Site2 = null;
                }
            }

            //initialize MapEntities that aren't in any sub (e.g. items inside ruins)
            MapEntity.MapLoaded(MapEntity.mapEntityList.FindAll(me => me.Submarine == null), false);

            Debug.WriteLine("Generatelevel: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();

            Debug.WriteLine("**********************************************************************************");
            Debug.WriteLine("Generated a map with " + siteCoordsX.Count + " sites in " + sw.ElapsedMilliseconds + " ms");
            Debug.WriteLine("Seed: " + Seed);
            Debug.WriteLine("**********************************************************************************");

            if (GameSettings.VerboseLogging)
            {
                DebugConsole.NewMessage("Generated level with the seed " + Seed + " (type: " + GenerationParams.Identifier + ")", Color.White);
            }

            EntityCountAfterGenerate = GetEntities().Count();

#if SERVER
            if (GameMain.Server.EntityEventManager.Events.Count() > 0)
            {
                DebugConsole.NewMessage("WARNING: Entity events have been created during level generation. Events should not be created until the round is fully initialized.");
            }
            GameMain.Server.EntityEventManager.Clear();
#endif

            //assign an ID to make entity events work
            ID = FindFreeID();
            Generating = false;
        }


        private List<VoronoiCell> CreateHoles(float holeProbability, Rectangle limits, int submarineSize)
        {
            List<VoronoiCell> toBeRemoved = new List<VoronoiCell>();
            foreach (VoronoiCell cell in cells)
            {
                if (GenerationParams.CreateHoleNextToEnd)
                {
                    if ((!Mirrored && cell.Center.X > endPosition.X) || (Mirrored && cell.Center.X < StartPosition.X))
                    {
                        if (cell.Edges.Any(e => e.Point1.Y > Size.Y - submarineSize || e.Point2.Y > Size.Y - submarineSize))
                        {
                            toBeRemoved.Add(cell);
                            continue;
                        }
                    }
                }

                if (Rand.Range(0.0f, 1.0f, Rand.RandSync.Server) > holeProbability) { continue; }

                if (!limits.Contains(cell.Site.Coord.X, cell.Site.Coord.Y)) { continue; }

                float closestDist = 0.0f;
                WayPoint closestWayPoint = null;
                foreach (WayPoint wp in WayPoint.WayPointList)
                {
                    if (wp.SpawnType != SpawnType.Path){ continue; }

                    float dist = Math.Abs(cell.Center.X - wp.WorldPosition.X);
                    if (closestWayPoint == null || dist < closestDist)
                    {
                        closestDist = dist;
                        closestWayPoint = wp;
                    }
                }

                if (closestWayPoint.WorldPosition.Y < cell.Center.Y) { continue; }

                toBeRemoved.Add(cell);
            }

            return toBeRemoved;
        }

        private void EnlargeMainPath(List<VoronoiCell> pathCells, float minWidth)
        {
            List<WayPoint> wayPoints = new List<WayPoint>();

            var newWaypoint = new WayPoint(new Rectangle((int)pathCells[0].Site.Coord.X, borders.Height, 10, 10), null);
            wayPoints.Add(newWaypoint);
            
            for (int i = 0; i < pathCells.Count; i++)
            {
                pathCells[i].CellType = CellType.Path;

                newWaypoint = new WayPoint(new Rectangle((int)pathCells[i].Site.Coord.X, (int)pathCells[i].Center.Y, 10, 10), null);
                wayPoints.Add(newWaypoint);
               
                wayPoints[wayPoints.Count-2].linkedTo.Add(newWaypoint);
                newWaypoint.linkedTo.Add(wayPoints[wayPoints.Count - 2]);

                for (int n = 0; n < wayPoints.Count; n++)
                {
                    if (wayPoints[n].Position != newWaypoint.Position) continue;

                    wayPoints[n].linkedTo.Add(newWaypoint);
                    newWaypoint.linkedTo.Add(wayPoints[n]);

                    break;
                }
            }

            newWaypoint = new WayPoint(new Rectangle((int)pathCells[pathCells.Count - 1].Site.Coord.X, borders.Height, 10, 10), null);
            wayPoints.Add(newWaypoint);

            wayPoints[wayPoints.Count - 2].linkedTo.Add(newWaypoint);
            newWaypoint.linkedTo.Add(wayPoints[wayPoints.Count - 2]);

            if (minWidth > 0.0f)
            {
                List<VoronoiCell> removedCells = GetTooCloseCells(pathCells, minWidth);
                foreach (VoronoiCell removedCell in removedCells)
                {
                    if (removedCell.CellType == CellType.Path) continue;

                    pathCells.Add(removedCell);
                    removedCell.CellType = CellType.Path;
                }
            }
        }

        private List<VoronoiCell> GetTooCloseCells(List<VoronoiCell> emptyCells, float minDistance)
        {
            List<VoronoiCell> tooCloseCells = new List<VoronoiCell>();

            Vector2 position = emptyCells[0].Center;

            if (minDistance <= 0.0f) return tooCloseCells;

            float step = 100.0f;
            int targetCellIndex = 1;

            minDistance *= 0.5f;
            do
            {
                tooCloseCells.AddRange(GetTooCloseCells(position, minDistance));

                position += Vector2.Normalize(emptyCells[targetCellIndex].Center - position) * step;

                if (Vector2.Distance(emptyCells[targetCellIndex].Center, position) < step * 2.0f) targetCellIndex++;

            } while (Vector2.Distance(position, emptyCells[emptyCells.Count - 1].Center) > step * 2.0f);

            return tooCloseCells;
        }

        public List<VoronoiCell> GetTooCloseCells(Vector2 position, float minDistance)
        {
            HashSet<VoronoiCell> tooCloseCells = new HashSet<VoronoiCell>();
            var closeCells = GetCells(position, 3);
            float minDistSqr = minDistance * minDistance;
            foreach (VoronoiCell cell in closeCells)
            {
                bool tooClose = false;
                foreach (GraphEdge edge in cell.Edges)
                {                    
                    if (Vector2.DistanceSquared(edge.Point1, position) < minDistSqr ||
                        Vector2.DistanceSquared(edge.Point2, position) < minDistSqr)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) { tooCloseCells.Add(cell); }
            }            
            return tooCloseCells.ToList();
        }


        /// <summary>
        /// remove all cells except those that are adjacent to the empty cells
        /// </summary>
        private List<VoronoiCell> CleanCells(List<VoronoiCell> emptyCells)
        {
            HashSet<VoronoiCell> newCells = new HashSet<VoronoiCell>();
            foreach (VoronoiCell cell in emptyCells)
            {
                foreach (GraphEdge edge in cell.Edges)
                {
                    VoronoiCell adjacent = edge.AdjacentCell(cell);
                    if (adjacent != null) { newCells.Add(adjacent); }
                }
            }
            return newCells.ToList();
        }

        private void GenerateSeaFloor(bool mirror)
        {
            BottomPos = GenerationParams.SeaFloorDepth;
            SeaFloorTopPos = BottomPos;

            bottomPositions = new List<Point>
            {
                new Point(0, BottomPos)
            };

            int mountainCount = Rand.Range(GenerationParams.MountainCountMin, GenerationParams.MountainCountMax, Rand.RandSync.Server);
            for (int i = 0; i < mountainCount; i++)
            {
                bottomPositions.Add(
                    new Point(Size.X / (mountainCount + 1) * (i + 1),
                    BottomPos + Rand.Range(GenerationParams.MountainHeightMin, GenerationParams.MountainHeightMax, Rand.RandSync.Server)));
            }
            bottomPositions.Add(new Point(Size.X, BottomPos));

            int minVertexInterval = 5000;
            float currInverval = Size.X / 2;
            while (currInverval > minVertexInterval)
            {
                for (int i = 0; i < bottomPositions.Count - 1; i++)
                {
                    bottomPositions.Insert(i + 1,
                        new Point(
                            (bottomPositions[i].X + bottomPositions[i + 1].X) / 2,
                            (bottomPositions[i].Y + bottomPositions[i + 1].Y) / 2 + Rand.Range(0, GenerationParams.SeaFloorVariance, Rand.RandSync.Server)));
                    i++;
                }

                currInverval /= 2;
            }

            if (mirror)
            {
                for (int i = 0; i < bottomPositions.Count; i++)
                {
                    bottomPositions[i] = new Point(borders.Size.X - bottomPositions[i].X, bottomPositions[i].Y);
                }
            }

            SeaFloorTopPos = bottomPositions.Max(p => p.Y);
            SeaFloor = new LevelWall(bottomPositions.Select(p => p.ToVector2()).ToList(), new Vector2(0.0f, -2000.0f), GenerationParams.WallColor, this);
            ExtraWalls.Add(SeaFloor);

            BottomBarrier = GameMain.World.CreateEdge(
                ConvertUnits.ToSimUnits(new Vector2(borders.X, 0)),
                ConvertUnits.ToSimUnits(new Vector2(borders.Right, 0)));

            BottomBarrier.SetTransform(ConvertUnits.ToSimUnits(new Vector2(0.0f, BottomPos)), 0.0f);
            BottomBarrier.BodyType = BodyType.Static;
            BottomBarrier.CollisionCategories = Physics.CollisionLevel;

            bodies.Add(BottomBarrier);
        }

        private void GenerateTunnels(List<Point> pathNodes, int pathWidth)
        {
            SmallTunnels = new List<List<Point>>();
            for (int i = 0; i < GenerationParams.SmallTunnelCount; i++)
            {
                var tunnelStartPos = pathNodes[Rand.Range(1, pathNodes.Count - 2, Rand.RandSync.Server)];

                List<Point> tunnelNodes = new List<Point>()
                {
                    tunnelStartPos,
                    tunnelStartPos + new Point(0, Math.Sign(tunnelStartPos.Y - Size.Y / 2) * pathWidth * 2)
                };

                List<Point> tunnel = GenerateTunnel(
                    tunnelNodes, 
                    Rand.Range(GenerationParams.SmallTunnelLengthRange.X, GenerationParams.SmallTunnelLengthRange.Y, Rand.RandSync.Server), 
                    pathNodes);
                if (tunnel.Any()) SmallTunnels.Add(tunnel);

                int branches = Rand.Range(0, 3, Rand.RandSync.Server);
                for (int j = 0; j < branches; j++)
                {
                    List<Point> branch = GenerateTunnel(
                        new List<Point>() { tunnel[Rand.Int(tunnel.Count, Rand.RandSync.Server)] },
                        Rand.Range(GenerationParams.SmallTunnelLengthRange.X, GenerationParams.SmallTunnelLengthRange.Y, Rand.RandSync.Server) * 0.5f,
                        pathNodes);
                    if (branch.Any()) SmallTunnels.Add(branch);
                }
                
            }
        }

        private List<Point> GenerateTunnel(List<Point> tunnelNodes, float tunnelLength, List<Point> avoidNodes)
        {
            int sectionLength = 1000;

            float currLength = 0.0f;
            DoubleVector2 dir = null;
            while (currLength < tunnelLength)
            {
                var prevDir = dir;
                dir = Rand.Vector(1.0, Rand.RandSync.Server);
                                
                dir.Y += Math.Sign(tunnelNodes[tunnelNodes.Count - 1].Y - Size.Y / 2) * 0.5f;
                if (prevDir != null)
                {
                    dir.X = (dir.X + prevDir.X) / 2.0;
                    dir.Y = (dir.Y + prevDir.Y) / 2.0;
                }

                double avoidDist = 20000;
                double avoidDistSqr = avoidDist * avoidDist;
                foreach (Point pathNode in avoidNodes)
                {
                    double diffX =  tunnelNodes[tunnelNodes.Count - 1].X - pathNode.X;
                    double diffY = tunnelNodes[tunnelNodes.Count - 1].Y - pathNode.Y;
                    if (Math.Abs(diffX) < 1.0f || Math.Abs(diffY) < 1.0f) continue;

                    double distSqr = (diffX * diffX + diffY * diffY);
                    Debug.Assert(distSqr > 0);
                    if (distSqr < avoidDistSqr)
                    {
                        double dist = Math.Sqrt(distSqr);

                        dir.X += (diffX / dist) * (1.0f - dist / avoidDist);
                        dir.Y += (diffY / dist) * (1.0f - dist / avoidDist);
                    }
                }

                dir.Normalize();

                if (tunnelNodes.Last().Y + dir.Y > Size.Y)
                {
                    //head back down if the tunnel has reached the top of the level
                    dir.Y = -dir.Y;
                }
                else if (tunnelNodes.Last().Y + dir.Y * 500 < 500)
                {
                    //head back up if reached the bottom of the level
                    dir.Y = -dir.Y;
                }
                else if (tunnelNodes.Last().Y + dir.Y + dir.Y < 0.0f ||
                    tunnelNodes.Last().Y + dir.Y + dir.Y < SeaFloorTopPos)
                {
                    //head back up if reached the sea floor
                    dir.Y = -dir.Y;
                }

                Point nextNode = tunnelNodes.Last() + new Point((int)(dir.X * sectionLength), (int)(dir.Y * sectionLength));
                nextNode.X = MathHelper.Clamp(nextNode.X, 500, Size.X - 500);
                nextNode.Y = MathHelper.Clamp(nextNode.Y, SeaFloorTopPos, Size.Y - 500);
                tunnelNodes.Add(nextNode);
                currLength += sectionLength;
            }

            return tunnelNodes;
        }

        private void GenerateRuin(List<VoronoiCell> mainPath, bool mirror)
        {
            var ruinGenerationParams = RuinGenerationParams.GetRandom();

            Point ruinSize = new Point(
                Rand.Range(ruinGenerationParams.SizeMin.X, ruinGenerationParams.SizeMax.X, Rand.RandSync.Server), 
                Rand.Range(ruinGenerationParams.SizeMin.Y, ruinGenerationParams.SizeMax.Y, Rand.RandSync.Server));
            int ruinRadius = Math.Max(ruinSize.X, ruinSize.Y) / 2;
            
            int cellIndex = Rand.Int(cells.Count, Rand.RandSync.Server);
            Point ruinPos = new Point((int)cells[cellIndex].Site.Coord.X, (int)cells[cellIndex].Site.Coord.X);

            //50% chance of placing the ruins at a cave
            if (Rand.Range(0.0f, 1.0f, Rand.RandSync.Server) < 0.5f)
            {
                TryGetInterestingPosition(true, PositionType.Cave, 0.0f, out ruinPos);
            }

            ruinPos.Y = Math.Min(ruinPos.Y, borders.Y + borders.Height - ruinSize.Y / 2);
            ruinPos.Y = Math.Max(ruinPos.Y, SeaFloorTopPos + ruinSize.Y / 2);

            double minMainPathDist = ruinRadius * 2;
            double minMainPathDistSqr = minMainPathDist * minMainPathDist;

            double minOutpostDist = Math.Min(Math.Min(10000.0f, Size.X / 3), Size.Y / 3);
            double minOutpostDistSqr = minOutpostDist * minOutpostDist;

            int iter = 0;
            while (mainPath.Any(p => MathUtils.DistanceSquared(ruinPos.X, ruinPos.Y, p.Site.Coord.X, p.Site.Coord.Y) < minMainPathDistSqr) ||
                Ruins.Any(r => r.Area.Intersects(new Rectangle(ruinPos - new Point(ruinSize.X / 2, ruinSize.Y / 2), ruinSize)) ||
                MathUtils.DistanceSquared(ruinPos.X, ruinPos.Y, StartPosition.X, StartPosition.Y) < minOutpostDistSqr ||
                MathUtils.DistanceSquared(ruinPos.X, ruinPos.Y, StartPosition.X, Size.Y) < minOutpostDistSqr ||
                MathUtils.DistanceSquared(ruinPos.X, ruinPos.Y, EndPosition.X, EndPosition.Y) < minOutpostDistSqr) ||
                MathUtils.DistanceSquared(ruinPos.X, ruinPos.Y, EndPosition.X, Size.Y) < minOutpostDistSqr)
            {
                double weighedPathPosX = ruinPos.X;
                double weighedPathPosY = ruinPos.Y;
                iter++;

                for (int i = 0; i < 2; i++)
                {
                    double diffX = i == 0 ? ruinPos.X - StartPosition.X : ruinPos.Y - StartPosition.X;
                    double diffY = i == 0 ? ruinPos.Y - StartPosition.Y : ruinPos.Y - StartPosition.Y;

                    double distSqr = diffX * diffX + diffY * diffY;
                    if (distSqr < minMainPathDistSqr)
                    {
                        double dist = Math.Sqrt(distSqr);
                        double moveAmountX = minMainPathDist * diffX / dist;
                        double moveAmountY = minMainPathDist * diffY / dist;
                        weighedPathPosX += moveAmountX;
                        weighedPathPosY += moveAmountY;
                        weighedPathPosY = Math.Min(borders.Y + borders.Height - ruinSize.Y / 2, weighedPathPosY);
                    }
                }

                foreach (VoronoiCell pathCell in mainPath)
                {
                    double diffX = ruinPos.X - pathCell.Site.Coord.X;
                    double diffY = ruinPos.Y - pathCell.Site.Coord.Y;

                    double distSqr = diffX * diffX + diffY * diffY;
                    if (distSqr < 1.0)
                    {
                        diffX = 0;
                        diffY = 1;
                        distSqr = 1.0;
                    }
                    if (distSqr > 10000.0 * 10000.0) continue;

                    double dist = Math.Sqrt(distSqr);
                    double moveAmountX = 100.0 * diffX / dist;
                    double moveAmountY = 100.0 * diffY / dist;

                    weighedPathPosX += moveAmountX;
                    weighedPathPosY += moveAmountY;
                    weighedPathPosY = Math.Min(borders.Y + borders.Height - ruinSize.Y / 2, weighedPathPosY);
                }

                Rectangle ruinArea = new Rectangle(ruinPos - new Point(ruinSize.X / 2, ruinSize.Y / 2), ruinSize);
                foreach (Ruin otherRuin in Ruins)
                {
                    if (!otherRuin.Area.Intersects(ruinArea)) continue;

                    double diffX = ruinArea.Center.X - otherRuin.Area.Center.X;
                    double diffY = ruinArea.Center.Y - otherRuin.Area.Center.Y;

                    double distSqr = diffX * diffX + diffY * diffY;
                    if (distSqr < 0.01f)
                    {
                        diffX = 0;
                        diffY = -1;
                        distSqr = 1;
                    }

                    double dist = Math.Sqrt(distSqr);
                    double moveAmountX = diffX / dist;
                    double moveAmountY = diffY / dist;

                    int move = (Math.Max(ruinArea.Width, ruinArea.Height) + Math.Max(otherRuin.Area.Width, otherRuin.Area.Height)) / 2;
                    moveAmountX *= move;
                    moveAmountY *= move;

                    weighedPathPosX += moveAmountX;
                    weighedPathPosY += moveAmountY;
                }                
                ruinPos = new Point((int)weighedPathPosX, (int)weighedPathPosY);

                //if we can't find a suitable position after 10 000 iterations, give up
                if (iter > 10000)
                {
                    if (Ruins.Count > 0)
                    {
                        //we already have some ruins, don't add this one at all
                        return;
                    }
                    string errorMsg = "Failed to find a suitable position for ruins. Level seed: " + Seed +
                        ", ruin size: " + ruinSize + ", selected sub " + (Submarine.MainSub == null ? "none" : Submarine.MainSub.Info.Name);
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("Level.GenerateRuins:PosNotFound", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    break;
                }
                //if we haven't found a position after 500 iterations, try another starting point
                else if (iter > 500 && iter % 500 == 0)
                {
                    int newCellIndex = Rand.Int(cells.Count, Rand.RandSync.Server);
                    ruinPos = new Point((int)cells[newCellIndex].Site.Coord.X, (int)cells[newCellIndex].Site.Coord.X);
                }
                ruinPos.Y = Math.Min(ruinPos.Y, borders.Y + borders.Height - ruinSize.Y / 2);
                ruinPos.Y = Math.Max(ruinPos.Y, SeaFloorTopPos + ruinSize.Y / 2);
            }

            if (Math.Abs(ruinPos.X) > int.MaxValue / 2 || Math.Abs(ruinPos.Y) > int.MaxValue / 2)
            {
                DebugConsole.ThrowError("Something went wrong during ruin generation. Ruin position: " + ruinPos);
                return;
            }

            VoronoiCell closestPathCell = null;
            double closestDist = 0.0f;
            foreach (VoronoiCell pathCell in mainPath)
            {
                double dist = MathUtils.DistanceSquared(pathCell.Site.Coord.X, pathCell.Site.Coord.Y, ruinPos.X, ruinPos.Y);
                if (closestPathCell == null || dist < closestDist)
                {
                    closestPathCell = pathCell;
                    closestDist = dist;
                }
            }
            
            var ruin = new Ruin(closestPathCell, cells, ruinGenerationParams, new Rectangle(ruinPos - new Point(ruinSize.X / 2, ruinSize.Y / 2), ruinSize), mirror);
            Ruins.Add(ruin);
            
            ruin.RuinShapes.Sort((shape1, shape2) => shape2.DistanceFromEntrance.CompareTo(shape1.DistanceFromEntrance));
            // TODO: autogenerate waypoints inside the ruins and connect them to the main path in multiple places.
            // We need the waypoints for the AI navigation and we could use them for spawning the creatures too.
            int waypointCount = 0;
            foreach (WayPoint wp in WayPoint.WayPointList)
            {
                if (wp.SpawnType != SpawnType.Enemy || wp.Submarine != null) { continue; }
                if (ruin.RuinShapes.Any(rs => rs.Rect.Contains(wp.WorldPosition)))
                {
                    PositionsOfInterest.Add(new InterestingPosition(new Point((int)wp.WorldPosition.X, (int)wp.WorldPosition.Y), PositionType.Ruin, ruin: ruin));
                    waypointCount++;
                }
            }

            //not enough waypoints inside ruins -> create some spawn positions manually            
            for (int i = 0; i < 4 - waypointCount && i < ruin.RuinShapes.Count; i++)
            {
                PositionsOfInterest.Add(new InterestingPosition(ruin.RuinShapes[i].Rect.Center, PositionType.Ruin, ruin: ruin));
            }

            foreach (RuinShape ruinShape in ruin.RuinShapes)
            {
                var tooClose = GetTooCloseCells(ruinShape.Rect.Center.ToVector2(), Math.Max(ruinShape.Rect.Width, ruinShape.Rect.Height));

                foreach (VoronoiCell cell in tooClose)
                {
                    if (cell.CellType == CellType.Empty) continue;
                    foreach (GraphEdge e in cell.Edges)
                    {
                        Rectangle rect = ruinShape.Rect;
                        rect.Y += rect.Height;
                        if (ruinShape.Rect.Contains(e.Point1) || ruinShape.Rect.Contains(e.Point2) ||
                            MathUtils.GetLineRectangleIntersection(e.Point1, e.Point2, rect, out _))
                        {
                            cell.CellType = CellType.Removed;
                            int x = (int)Math.Floor(cell.Center.X / GridCellSize);
                            int y = (int)Math.Floor(cell.Center.Y / GridCellSize);
                            cellGrid[x, y].Remove(cell);
                            cells.Remove(cell);
                            break;
                        }
                    }
                }
            }

            //cast a ray from the closest path cell towards the ruin and remove the cell it hits
            //to ensure that there's always at least one way from the main tunnel to the ruin
            List<VoronoiCell> validCells = cells.FindAll(c => c.CellType != CellType.Empty && c.CellType != CellType.Removed);
            foreach (VoronoiCell cell in validCells)
            {
                foreach (GraphEdge e in cell.Edges)
                {
                    if (MathUtils.LinesIntersect(closestPathCell.Center, ruinPos.ToVector2(), e.Point1, e.Point2))
                    {
                        cell.CellType = CellType.Removed;
                        int x = (int)Math.Floor(cell.Center.X / GridCellSize);
                        int y = (int)Math.Floor(cell.Center.Y / GridCellSize);
                        cellGrid[x, y].Remove(cell);
                        cells.Remove(cell);
                        break;
                    }
                }
                if (cell.CellType == CellType.Removed)
                {
                    break;
                }
            }
        }

        private void GenerateItems()
        {
            string levelName = GenerationParams.Identifier.ToLowerInvariant();
            List<Pair<ItemPrefab, float>> levelItems = new List<Pair<ItemPrefab, float>>();
            foreach (ItemPrefab itemPrefab in ItemPrefab.Prefabs)
            {
                if (itemPrefab.LevelCommonness.TryGetValue(levelName, out float commonness) || 
                    itemPrefab.LevelCommonness.TryGetValue("", out commonness))
                {
                    levelItems.Add(new Pair<ItemPrefab, float>(itemPrefab, commonness));
                }
            }

            DebugConsole.Log("Generating level resources...");

            for (int i = 0; i < GenerationParams.ItemCount; i++)
            {
                var selectedPrefab = ToolBox.SelectWeightedRandom(
                    levelItems.Select(it => it.First).ToList(),
                    levelItems.Select(it => it.Second).ToList(),
                    Rand.RandSync.Server);
                if (selectedPrefab == null) { break; }

                var selectedCell = cells[Rand.Int(cells.Count, Rand.RandSync.Server)];
                var selectedEdge = selectedCell.Edges.GetRandom(e => e.IsSolid && !e.OutsideLevel, Rand.RandSync.Server);
                if (selectedEdge == null) continue;


                float edgePos = Rand.Range(0.0f, 1.0f, Rand.RandSync.Server);
                Vector2 selectedPos = Vector2.Lerp(selectedEdge.Point1, selectedEdge.Point2, edgePos);
                Vector2 edgeNormal = selectedEdge.GetNormal(selectedCell);

                var item = new Item(selectedPrefab, selectedPos, submarine: null);
                item.Move(edgeNormal * item.Rect.Height / 2, ignoreContacts: true);
                
                var holdable = item.GetComponent<Holdable>();
                if (holdable == null)
                {
                    DebugConsole.ThrowError("Error while placing items in the level - item \"" + item.Name + "\" is not holdable and cannot be attached to the level walls.");
                }
                else
                {
                    holdable.AttachToWall();
#if CLIENT
                    item.Rotation = MathHelper.ToDegrees(-MathUtils.VectorToAngle(edgeNormal) + MathHelper.PiOver2);
#endif
                }
            }

            DebugConsole.Log("Level resources generated");
        }

        public Vector2 GetRandomItemPos(PositionType spawnPosType, float randomSpread, float minDistFromSubs, float offsetFromWall = 10.0f)
        {
            if (!PositionsOfInterest.Any())
            {
                return new Vector2(Size.X / 2, Size.Y / 2);
            }

            Vector2 position = Vector2.Zero;

            int tries = 0;
            do
            {
                Loaded.TryGetInterestingPosition(true, spawnPosType, minDistFromSubs, out Vector2 startPos);

                Vector2 offset = Rand.Vector(Rand.Range(0.0f, randomSpread, Rand.RandSync.Server), Rand.RandSync.Server);
                if (!cells.Any(c => c.IsPointInside(startPos + offset)))
                {
                    startPos += offset;
                }

                Vector2 endPos = startPos - Vector2.UnitY * Size.Y;

                if (Submarine.PickBody(
                    ConvertUnits.ToSimUnits(startPos),
                    ConvertUnits.ToSimUnits(endPos),
                    null, Physics.CollisionLevel | Physics.CollisionWall) != null)
                {
                    position = ConvertUnits.ToDisplayUnits(Submarine.LastPickedPosition) + Vector2.Normalize(startPos - endPos) * offsetFromWall;
                    break;
                }

                tries++;

                if (tries == 10)
                {
                    position = EndPosition - Vector2.UnitY * 300.0f;
                }

            } while (tries < 10);

            return position;
        }


        public bool TryGetInterestingPosition(bool useSyncedRand, PositionType positionType, float minDistFromSubs, out Vector2 position)
        {
            bool success = TryGetInterestingPosition(useSyncedRand, positionType, minDistFromSubs, out Point pos);
            position = pos.ToVector2();
            return success;
        }

        public bool TryGetInterestingPosition(bool useSyncedRand, PositionType positionType, float minDistFromSubs, out Point position)
        {
            if (!PositionsOfInterest.Any())
            {
                position = new Point(Size.X / 2, Size.Y / 2);
                return false;
            }

            List<InterestingPosition> suitablePositions = PositionsOfInterest.FindAll(p => positionType.HasFlag(p.PositionType));
            //avoid floating ice chunks on the main path
            if (positionType == PositionType.MainPath)
            {
                suitablePositions.RemoveAll(p => ExtraWalls.Any(w => w.Cells.Any(c => c.IsPointInside(p.Position.ToVector2()))));
            }
            if (!suitablePositions.Any())
            {
                string errorMsg = "Could not find a suitable position of interest. (PositionType: " + positionType + ", minDistFromSubs: " + minDistFromSubs + ")\n" + Environment.StackTrace.CleanupStackTrace();
                GameAnalyticsManager.AddErrorEventOnce("Level.TryGetInterestingPosition:PositionTypeNotFound", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
#if DEBUG
                DebugConsole.ThrowError(errorMsg);
#endif
                position = PositionsOfInterest[Rand.Int(PositionsOfInterest.Count, (useSyncedRand ? Rand.RandSync.Server : Rand.RandSync.Unsynced))].Position;
                return false;
            }

            List<InterestingPosition> farEnoughPositions = new List<InterestingPosition>(suitablePositions);
            if (minDistFromSubs > 0.0f)
            {
                foreach (Submarine sub in Submarine.Loaded)
                {
                    if (sub.Info.Type != SubmarineType.Player) { continue; }
                    farEnoughPositions.RemoveAll(p => Vector2.DistanceSquared(p.Position.ToVector2(), sub.WorldPosition) < minDistFromSubs * minDistFromSubs);
                }
            }
            if (!farEnoughPositions.Any())
            {
                string errorMsg = "Could not find a position of interest far enough from the submarines. (PositionType: " + positionType + ", minDistFromSubs: " + minDistFromSubs + ")\n" + Environment.StackTrace.CleanupStackTrace();
                GameAnalyticsManager.AddErrorEventOnce("Level.TryGetInterestingPosition:TooCloseToSubs", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
#if DEBUG
                DebugConsole.ThrowError(errorMsg);
#endif
                float maxDist = 0.0f;
                position = suitablePositions.First().Position;
                foreach (InterestingPosition pos in suitablePositions)
                {
                    float dist = Submarine.Loaded.Sum(s => 
                        Submarine.MainSubs.Contains(s) ? Vector2.DistanceSquared(s.WorldPosition, pos.Position.ToVector2()) : 0.0f);
                    if (dist > maxDist)
                    {
                        position = pos.Position;
                        maxDist = dist;
                    }
                }

                return false;
            }

            position = farEnoughPositions[Rand.Int(farEnoughPositions.Count, (useSyncedRand ? Rand.RandSync.Server : Rand.RandSync.Unsynced))].Position;
            return true;
        }

        public void Update(float deltaTime, Camera cam)
        {
            LevelObjectManager.Update(deltaTime);
            
            foreach (LevelWall wall in ExtraWalls)
            {
                wall.Update(deltaTime);
            }
            
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
            {
                networkUpdateTimer += deltaTime;
                if (networkUpdateTimer > NetworkUpdateInterval)
                {
                    if (ExtraWalls.Any(w => w.Body.BodyType != BodyType.Static))
                    {
                        GameMain.NetworkMember.CreateEntityEvent(this);
                    }
                    networkUpdateTimer = 0.0f;
                }
            }

#if CLIENT
            backgroundCreatureManager.Update(deltaTime, cam);
            WaterRenderer.Instance?.ScrollWater(Vector2.UnitY, (float)deltaTime);
            renderer.Update(deltaTime, cam);
#endif
        }

        public Vector2 GetBottomPosition(float xPosition)
        {
            int index = (int)Math.Floor(xPosition / Size.X * (bottomPositions.Count - 1));
            if (index < 0 || index >= bottomPositions.Count - 1) return new Vector2(xPosition, BottomPos);

            float yPos = MathHelper.Lerp(
                bottomPositions[index].Y,
                bottomPositions[index + 1].Y,
                (xPosition - bottomPositions[index].X) / (bottomPositions[index + 1].X - bottomPositions[index].X));

            return new Vector2(xPosition, yPos);
        }
        
        public List<VoronoiCell> GetAllCells()
        {
            List<VoronoiCell> cells = new List<VoronoiCell>();
            for (int x = 0; x < cellGrid.GetLength(0); x++)
            {
                for (int y = 0; y < cellGrid.GetLength(1); y++)
                {
                    cells.AddRange(cellGrid[x, y]);
                }
            }
            return cells;
        }

        private readonly List<VoronoiCell> tempCells = new List<VoronoiCell>();
        public List<VoronoiCell> GetCells(Vector2 worldPos, int searchDepth = 2)
        {
            tempCells.Clear();
            int gridPosX = (int)Math.Floor(worldPos.X / GridCellSize);
            int gridPosY = (int)Math.Floor(worldPos.Y / GridCellSize);

            int startX = Math.Max(gridPosX - searchDepth, 0);
            int endX = Math.Min(gridPosX + searchDepth, cellGrid.GetLength(0) - 1);

            int startY = Math.Max(gridPosY - searchDepth, 0);
            int endY = Math.Min(gridPosY + searchDepth, cellGrid.GetLength(1) - 1);

            for (int y = startY; y <= endY; y++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    tempCells.AddRange(cellGrid[x, y]);
                }
            }
            
            foreach (LevelWall wall in ExtraWalls)
            {
                foreach (VoronoiCell cell in wall.Cells)
                {
                    tempCells.Add(cell);
                }
            }
            
            return tempCells;
        }

        public string GetWreckIDTag(string originalTag, Submarine wreck)
        {
            string shortSeed = ToolBox.StringToInt(LevelData.Seed + wreck?.Info.Name).ToString();
            if (shortSeed.Length > 6) { shortSeed = shortSeed.Substring(0, 6); }
            return originalTag + "_" + shortSeed;
        }

        // For debugging
        private readonly Dictionary<Submarine, List<Vector2>> wreckPositions = new Dictionary<Submarine, List<Vector2>>();
        private readonly Dictionary<Submarine, List<Rectangle>> blockedRects = new Dictionary<Submarine, List<Rectangle>>();
        private void CreateWrecks()
        {
            var totalSW = new Stopwatch();
            var tempSW = new Stopwatch();
            totalSW.Start();
            var wreckFiles = ContentPackage.GetFilesOfType(GameMain.Config.AllEnabledPackages, ContentType.Wreck).ToList();
            if (wreckFiles.None())
            {
                DebugConsole.ThrowError("No wreck files found in the selected content packages!");
                return;
            }
            wreckFiles.Shuffle(Rand.RandSync.Server);

            int wreckCount = Math.Min(Loaded.GenerationParams.WreckCount, wreckFiles.Count);
            // Min distance between a wreck and the start/end/other wreck.
            float minDistance = Sonar.DefaultSonarRange;
            float squaredMinDistance = minDistance * minDistance;
            Vector2 start = startPosition.ToVector2();
            Vector2 end = endPosition.ToVector2();
            var waypoints = WayPoint.WayPointList.Where(wp => 
                wp.Submarine == null &&
                wp.SpawnType == SpawnType.Path && 
                Vector2.DistanceSquared(wp.WorldPosition, start) > squaredMinDistance && 
                Vector2.DistanceSquared(wp.WorldPosition, end) > squaredMinDistance).ToList();
            Wrecks = new List<Submarine>(wreckCount);
            for (int i = 0; i < wreckCount; i++)
            {
                ContentFile contentFile = wreckFiles[i];
                if (contentFile == null) { continue; }                
                var subDoc = SubmarineInfo.OpenFile(contentFile.Path);
                Rectangle borders = Submarine.GetBorders(subDoc.Root);
                string wreckName = System.IO.Path.GetFileNameWithoutExtension(contentFile.Path);
                // Add some margin so that the wreck doesn't block the path entirely. It's still possible that some larger subs can't pass by.
                Point paddedDimensions = new Point(borders.Width + 3000, borders.Height + 3000);
                tempSW.Restart();
                // For storing the translations. Used only for debugging.
                var positions = new List<Vector2>();
                var rects = new List<Rectangle>();
                int maxAttempts = 50;
                int attemptsLeft = maxAttempts;
                bool success = false;
                Vector2 spawnPoint = Vector2.Zero;
                var allCells = Loaded.GetAllCells();
                while (attemptsLeft > 0)
                {
                    if (attemptsLeft < maxAttempts)
                    {
                        Debug.WriteLine($"Failed to position the wreck {wreckName}. Trying again.");
                    }
                    attemptsLeft--;
                    if (TryGetSpawnPoint(out spawnPoint))
                    {
                        success = TryPositionWreck(borders, wreckName, ref spawnPoint);
                        if (success)
                        {
                            break;
                        }
                        else
                        {
                            positions.Clear();
                        }
                    }
                    else
                    {
                        DebugConsole.NewMessage($"Failed to find any spawn point for the wreck: {wreckName} (No valid waypoints left).", Color.Red);
                        break;
                    }
                }
                tempSW.Stop();
                if (success)
                {
                    Debug.WriteLine($"Wreck {wreckName} successfully positioned to {spawnPoint} in {tempSW.ElapsedMilliseconds} (ms)");
                    tempSW.Restart();
                    SubmarineInfo info = new SubmarineInfo(contentFile.Path)
                    {
                        Type = SubmarineType.Wreck
                    };
                    Submarine wreck = new Submarine(info);
                    wreck.MakeWreck();
                    tempSW.Stop();
                    Debug.WriteLine($"Wreck {wreck.Info.Name} loaded in { tempSW.ElapsedMilliseconds} (ms)");
                    Wrecks.Add(wreck);
                    wreck.SetPosition(spawnPoint);
                    wreckPositions.Add(wreck, positions);
                    blockedRects.Add(wreck, rects);
                    PositionsOfInterest.Add(new InterestingPosition(spawnPoint.ToPoint(), PositionType.Wreck, submarine: wreck));
                    foreach (Hull hull in wreck.GetHulls(false))
                    {
                        if (Rand.Value(Rand.RandSync.Server) <= Loaded.GenerationParams.WreckHullFloodingChance)
                        {
                            hull.WaterVolume = hull.Volume * Rand.Range(Loaded.GenerationParams.WreckFloodingHullMinWaterPercentage, Loaded.GenerationParams.WreckFloodingHullMaxWaterPercentage, Rand.RandSync.Server);
                        }
                    }
                    // Only spawn thalamus when the wreck has some thalamus items defined.
                    if (Rand.Value(Rand.RandSync.Server) <= Loaded.GenerationParams.ThalamusProbability && wreck.GetItems(false).Any(i => i.Prefab.Category == MapEntityCategory.Thalamus))
                    {
                        if (!wreck.CreateWreckAI())
                        {
                            DebugConsole.NewMessage($"Failed to create wreck AI inside {wreckName}.", Color.Red);
                            wreck.DisableWreckAI();
                        }
                    }
                    else
                    {
                        wreck.DisableWreckAI();
                    }
                }
                else
                {
                    DebugConsole.NewMessage($"Failed to position wreck {wreckName}. Used {tempSW.ElapsedMilliseconds.ToString()} (ms).", Color.Red);
                }

                bool TryPositionWreck(Rectangle borders, string wreckName, ref Vector2 spawnPoint)
                {
                    positions.Add(spawnPoint);
                    bool bottomFound = TryRaycastToBottom(borders, ref spawnPoint);
                    positions.Add(spawnPoint);

                    bool leftSideBlocked = IsSideBlocked(borders, false);
                    bool rightSideBlocked = IsSideBlocked(borders, true);
                    int step = 5;
                    if (rightSideBlocked && !leftSideBlocked)
                    {
                        bottomFound = TryMove(borders, ref spawnPoint, -step);
                    }
                    else if (leftSideBlocked && !rightSideBlocked)
                    {
                        bottomFound = TryMove(borders, ref spawnPoint, step);
                    }
                    else if (!bottomFound)
                    {
                        if (!leftSideBlocked)
                        {
                            bottomFound = TryMove(borders, ref spawnPoint, -step);
                        }
                        else if (!rightSideBlocked)
                        {
                            bottomFound = TryMove(borders, ref spawnPoint, step);
                        }
                        else
                        {
                            Debug.WriteLine($"Invalid position {spawnPoint}. Does not touch the ground.");
                            return false;
                        }
                    }
                    positions.Add(spawnPoint);
                    bool isBlocked = IsBlocked(spawnPoint, borders.Size - new Point(step + 50));
                    if (isBlocked)
                    {
                        rects.Add(ToolBox.GetWorldBounds(spawnPoint.ToPoint(), borders.Size));
                        Debug.WriteLine($"Invalid position {spawnPoint}. Blocked by level walls.");
                    }
                    else if (!bottomFound)
                    {
                        Debug.WriteLine($"Invalid position {spawnPoint}. Does not touch the ground.");
                    }
                    else
                    {
                        var sp = spawnPoint;
                        if (Wrecks.Any(w => Vector2.DistanceSquared(w.WorldPosition, sp) < squaredMinDistance))
                        {
                            Debug.WriteLine($"Invalid position {spawnPoint}. Too close to other wreck(s).");
                            return false;
                        }
                    }
                    return !isBlocked && bottomFound;

                    bool TryMove(Rectangle borders, ref Vector2 spawnPoint, float amount)
                    {
                        float maxMovement = 5000;
                        float totalAmount = 0;
                        bool foundBottom = TryRaycastToBottom(borders, ref spawnPoint);
                        while (!IsSideBlocked(borders, amount > 0))
                        {
                            foundBottom = TryRaycastToBottom(borders, ref spawnPoint);
                            totalAmount += amount;
                            spawnPoint = new Vector2(spawnPoint.X + amount, spawnPoint.Y);
                            if (Math.Abs(totalAmount) > maxMovement)
                            {
                                Debug.WriteLine($"Moving the wreck {wreckName} failed.");
                                break;
                            }
                        }
                        return foundBottom;
                    }
                }

                bool TryGetSpawnPoint(out Vector2 spawnPoint)
                {
                    spawnPoint = Vector2.Zero;
                    while (waypoints.Any())
                    {
                        var wp = waypoints.GetRandom(Rand.RandSync.Server);
                        waypoints.Remove(wp);
                        if (!IsBlocked(wp.WorldPosition, paddedDimensions))
                        {
                            spawnPoint = wp.WorldPosition;
                            return true;
                        }
                    }
                    return false;
                }

                static bool TryRaycastToBottom(Rectangle borders, ref Vector2 spawnPoint)
                {
                    // Shoot five rays and pick the highest hit point.
                    int rayCount = 5;
                    var positions = new Vector2[rayCount];
                    bool hit = false;
                    for (int i = 0; i < rayCount; i++)
                    {
                        float quarterWidth = borders.Width * 0.25f;
                        Vector2 rayStart = spawnPoint;
                        switch (i)
                        {
                            case 1:
                                rayStart = new Vector2(spawnPoint.X - quarterWidth, spawnPoint.Y);
                                break;
                            case 2:
                                rayStart = new Vector2(spawnPoint.X + quarterWidth, spawnPoint.Y);
                                break;
                            case 3:
                                rayStart = new Vector2(spawnPoint.X - quarterWidth / 2, spawnPoint.Y);
                                break;
                            case 4:
                                rayStart = new Vector2(spawnPoint.X + quarterWidth / 2, spawnPoint.Y);
                                break;
                        }
                        var simPos = ConvertUnits.ToSimUnits(rayStart);
                        var body = Submarine.PickBody(simPos, new Vector2(simPos.X, -1), 
                            customPredicate: f => f.Body?.UserData is VoronoiCell cell && cell.Body.BodyType == BodyType.Static, 
                            collisionCategory: Physics.CollisionLevel | Physics.CollisionWall);
                        if (body != null)
                        {
                            positions[i] = ConvertUnits.ToDisplayUnits(Submarine.LastPickedPosition) + new Vector2(0, borders.Height / 2);
                            hit = true;
                        }
                    }
                    float highestPoint = positions.Max(p => p.Y);
                    spawnPoint = new Vector2(spawnPoint.X, highestPoint);
                    return hit;
                }

                bool IsSideBlocked(Rectangle borders, bool front)
                {
                    // Shoot three rays and check whether any of them hits.
                    int rayCount = 3;
                    Vector2 halfSize = borders.Size.ToVector2() / 2;
                    Vector2 quarterSize = halfSize / 2;
                    var positions = new Vector2[rayCount];
                    for (int i = 0; i < rayCount; i++)
                    {
                        float dir = front ? 1 : -1;
                        Vector2 rayStart;
                        Vector2 to;
                        switch (i)
                        {
                            case 1:
                                rayStart = new Vector2(spawnPoint.X + halfSize.X * dir, spawnPoint.Y + quarterSize.Y);
                                to = new Vector2(spawnPoint.X + (halfSize.X - quarterSize.X) * dir, rayStart.Y);
                                break;
                            case 2:
                                rayStart = new Vector2(spawnPoint.X + halfSize.X * dir, spawnPoint.Y - quarterSize.Y);
                                to = new Vector2(spawnPoint.X + (halfSize.X - quarterSize.X) * dir, rayStart.Y);
                                break;
                            case 0:
                            default:
                                rayStart = spawnPoint;
                                to = new Vector2(spawnPoint.X + halfSize.X * dir, rayStart.Y);
                                break;
                        }
                        Vector2 simPos = ConvertUnits.ToSimUnits(rayStart);
                        if (Submarine.PickBody(simPos, ConvertUnits.ToSimUnits(to), 
                            customPredicate: f => f.Body?.UserData is VoronoiCell cell, 
                            collisionCategory: Physics.CollisionLevel | Physics.CollisionWall) != null)
                        {
                            return true;
                        }
                    }
                    return false;
                }

                bool IsBlocked(Vector2 pos, Point size, float maxDistanceMultiplier = 1)
                {
                    float maxDistance = size.Multiply(maxDistanceMultiplier).ToVector2().LengthSquared();
                    Rectangle bounds = ToolBox.GetWorldBounds(pos.ToPoint(), size);
                    if (Ruins.Any(r => ToolBox.GetWorldBounds(r.Area.Center, r.Area.Size).IntersectsWorld(bounds)))
                    {
                        return true;
                    }
                    return cells.Any(c => c.Body != null && Vector2.DistanceSquared(pos, c.Center) <= maxDistance && c.BodyVertices.Any(v => bounds.ContainsWorld(v)));
                }
            }
            totalSW.Stop();
            Debug.WriteLine($"{Wrecks.Count} wrecks created in { totalSW.ElapsedMilliseconds.ToString()} (ms)");
        }

        private void CreateOutposts()
        {
            var outpostFiles = ContentPackage.GetFilesOfType(GameMain.Config.AllEnabledPackages, ContentType.Outpost).ToList();
            if (!outpostFiles.Any() && !OutpostGenerationParams.Params.Any() && LevelData.ForceOutpostGenerationParams == null)
            {
                DebugConsole.ThrowError("No outpost files found in the selected content packages");
                return;
            }

            for (int i = 0; i < 2; i++)
            {
                if (Submarine.MainSubs.Length > 1 && Submarine.MainSubs[0] != null && Submarine.MainSubs[1] != null)
                {
                    continue;
                }

                bool isStart = (i == 0) == !Mirrored;
                if (isStart)
                {
                    //only create a starting outpost in campaign and tutorial modes
#if CLIENT       
                    if (Screen.Selected != GameMain.LevelEditorScreen && !IsModeStartOutpostCompatible())
                    {
                        continue;
                    }
#else
                    if (!IsModeStartOutpostCompatible())
                    {
                        continue;
                    }
#endif
                    if (StartLocation != null && !StartLocation.Type.HasOutpost) { continue; }
                }
                else
                {
                    //don't create an end outpost for locations
                    if (LevelData.Type == LevelData.LevelType.Outpost) { continue; }
                    if (EndLocation != null && !EndLocation.Type.HasOutpost) { continue; }
                }

                SubmarineInfo outpostInfo;
                Submarine outpost;
                if (i == 0 && preSelectedStartOutpost == null || i == 1 && preSelectedEndOutpost == null)
                {
                    if (OutpostGenerationParams.Params.Any() || LevelData.ForceOutpostGenerationParams != null)
                    {
                        Location location = i == 0 ? StartLocation : EndLocation;

                        OutpostGenerationParams outpostGenerationParams = null;
                        if (LevelData.ForceOutpostGenerationParams != null)
                        {
                            outpostGenerationParams = LevelData.ForceOutpostGenerationParams;
                        }
                        else
                        {
                            var suitableParams = OutpostGenerationParams.Params
                                .Where(p => location == null || p.AllowedLocationTypes.Contains(location.Type.Identifier));
                            if (suitableParams.Count() == 0)
                            {
                                suitableParams = OutpostGenerationParams.Params
                                    .Where(p => location == null || !p.AllowedLocationTypes.Any());
                            }

                            if (!suitableParams.Any())
                            {
                                DebugConsole.ThrowError("No suitable outpost generation parameters found for the location type \"" + location.Type.Identifier + "\". Selecting random parameters.");
                                suitableParams = OutpostGenerationParams.Params;
                            }
                            outpostGenerationParams = suitableParams.GetRandom(Rand.RandSync.Server);
                        }

                        LocationType locationType = location?.Type;
                        if (locationType == null)
                        {
                            locationType = LocationType.List.GetRandom(Rand.RandSync.Server);
                            if (outpostGenerationParams.AllowedLocationTypes.Any())
                            {
                                locationType = LocationType.List.Where(lt => 
                                    outpostGenerationParams.AllowedLocationTypes.Any(allowedType => 
                                      allowedType.Equals("any", StringComparison.OrdinalIgnoreCase) || lt.Identifier.Equals(allowedType, StringComparison.OrdinalIgnoreCase))).GetRandom();
                            }
                        }

                        if (location != null)
                        {
                            DebugConsole.NewMessage($"Generating an outpost for the {(isStart ? "start" : "end")} of the level... (Location: {location.Name}, level type: {LevelData.Type})");
                            outpost = OutpostGenerator.Generate(outpostGenerationParams, location, onlyEntrance: LevelData.Type != LevelData.LevelType.Outpost);
                        }
                        else
                        {
                            DebugConsole.NewMessage($"Generating an outpost for the {(isStart ? "start" : "end")} of the level... (Location type: {locationType}, level type: {LevelData.Type})");
                            outpost = OutpostGenerator.Generate(outpostGenerationParams, locationType, onlyEntrance: LevelData.Type != LevelData.LevelType.Outpost);
                        }
                    }
                    else
                    {
                        DebugConsole.NewMessage($"Loading a pre-built outpost for the {(isStart ? "start" : "end")} of the level...");
                        //backwards compatibility: if there are no generation params available, try to load an outpost file saved as a sub
                        ContentFile outpostFile = outpostFiles.GetRandom(Rand.RandSync.Server);
                        outpostInfo = new SubmarineInfo(outpostFile.Path)
                        {
                            Type = SubmarineType.Outpost
                        };
                        outpost = new Submarine(outpostInfo);
                    }
                }
                else
                {
                    DebugConsole.NewMessage($"Loading a pre-selected outpost for the {(isStart ? "start" : "end")} of the level...");
                    outpostInfo = (i == 0) ? preSelectedStartOutpost : preSelectedEndOutpost;
                    outpostInfo.Type = SubmarineType.Outpost;
                    outpost = new Submarine(outpostInfo);
                }

                Point? minSize = null;
                DockingPort subPort = null;
                float closestDistance = float.MaxValue;
                if (Submarine.MainSub != null)
                {
                    Point subSize = Submarine.MainSub.GetDockedBorders().Size;
                    Point outpostSize = outpost.GetDockedBorders().Size;
                    minSize = new Point(Math.Max(subSize.X, outpostSize.X), subSize.Y + outpostSize.Y);

                    foreach (DockingPort port in DockingPort.List)
                    {
                        if (port.IsHorizontal || port.Docked) { continue; }
                        if (port.Item.Submarine != Submarine.MainSub) { continue; }
                        //the submarine port has to be at the top of the sub
                        if (port.Item.WorldPosition.Y < Submarine.MainSub.WorldPosition.Y) { continue; }
                        float dist = Math.Abs(port.Item.WorldPosition.X - Submarine.MainSub.WorldPosition.X);
                        if (dist < closestDistance)
                        {
                            subPort = port;
                            closestDistance = dist;
                        }
                    }
                }

                DockingPort outpostPort = null;
                closestDistance = float.MaxValue;
                foreach (DockingPort port in DockingPort.List)
                {
                    if (port.IsHorizontal || port.Docked) { continue; }
                    if (port.Item.Submarine != outpost) { continue; }
                    //the outpost port has to be at the bottom of the outpost
                    if (port.Item.WorldPosition.Y > outpost.WorldPosition.Y) { continue; }
                    float dist = Math.Abs(port.Item.WorldPosition.X - outpost.WorldPosition.X);
                    if (dist < closestDistance)
                    {
                        outpostPort = port;
                        closestDistance = dist;
                    }
                }

                float subDockingPortOffset = subPort == null ? 0.0f : subPort.Item.WorldPosition.X - Submarine.MainSub.WorldPosition.X;
                //don't try to compensate if the port is very far from the sub's center of mass
                if (Math.Abs(subDockingPortOffset) > 5000.0f)
                {
                    subDockingPortOffset = MathHelper.Clamp(subDockingPortOffset, -5000.0f, 5000.0f);
                    string warningMsg = "Docking port very far from the sub's center of mass (submarine: " + Submarine.MainSub.Info.Name + ", dist: " + subDockingPortOffset + "). The level generator may not be able to place the outpost so that docking is possible.";
                    DebugConsole.NewMessage(warningMsg, Color.Orange);
                    GameAnalyticsManager.AddErrorEventOnce("Lever.CreateOutposts:DockingPortVeryFar" + Submarine.MainSub.Info.Name, GameAnalyticsSDK.Net.EGAErrorSeverity.Warning, warningMsg);
                }

                float outpostDockingPortOffset = subPort == null ? 0.0f : outpostPort.Item.WorldPosition.X - outpost.WorldPosition.X;
                //don't try to compensate if the port is very far from the outpost's center of mass
                if (Math.Abs(outpostDockingPortOffset) > 5000.0f)
                {
                    outpostDockingPortOffset = MathHelper.Clamp(outpostDockingPortOffset, -5000.0f, 5000.0f);
                    string warningMsg = "Docking port very far from the outpost's center of mass (outpost: " + outpost.Info.Name + ", dist: " + outpostDockingPortOffset + "). The level generator may not be able to place the outpost so that docking is possible.";
                    DebugConsole.NewMessage(warningMsg, Color.Orange);
                    GameAnalyticsManager.AddErrorEventOnce("Lever.CreateOutposts:OutpostDockingPortVeryFar" + outpost.Info.Name, GameAnalyticsSDK.Net.EGAErrorSeverity.Warning, warningMsg);
                }

                Vector2 spawnPos = outpost.FindSpawnPos(i == 0 ? StartPosition : EndPosition, minSize, subDockingPortOffset - outpostDockingPortOffset, verticalMoveDir: 1);
                if (Type == LevelData.LevelType.Outpost)
                {
                    spawnPos.Y = Math.Min(Size.Y - outpost.Borders.Height * 0.6f, spawnPos.Y + outpost.Borders.Height / 2);
                }
                outpost.SetPosition(spawnPos);
                if ((i == 0) == !Mirrored)
                {
                    StartOutpost = outpost;
                    if (StartLocation != null) { outpost.Info.Name = StartLocation.Name; }
                }
                else
                {
                    EndOutpost = outpost;
                    if (EndLocation != null) { outpost.Info.Name = EndLocation.Name; }
                }
            }
        }

        private bool IsModeStartOutpostCompatible()
        {
#if CLIENT
            return GameMain.GameSession?.GameMode is CampaignMode || GameMain.GameSession?.GameMode is TutorialMode || GameMain.GameSession?.GameMode is TestGameMode;
#else
            return GameMain.GameSession?.GameMode is CampaignMode;
#endif
        }

        public void SpawnCorpses()
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            foreach (Submarine wreck in Wrecks)
            {
                int corpseCount = Rand.Range(Loaded.GenerationParams.MinCorpseCount, Loaded.GenerationParams.MaxCorpseCount);
                var allSpawnPoints = WayPoint.WayPointList.FindAll(wp => wp.Submarine == wreck && wp.CurrentHull != null);
                var pathPoints = allSpawnPoints.FindAll(wp => wp.SpawnType == SpawnType.Path);
                pathPoints.Shuffle(Rand.RandSync.Unsynced);
                var corpsePoints = allSpawnPoints.FindAll(wp => wp.SpawnType == SpawnType.Corpse);
                corpsePoints.Shuffle(Rand.RandSync.Unsynced);
                int spawnCounter = 0;
                for (int j = 0; j < corpseCount; j++)
                {
                    WayPoint sp = corpsePoints.FirstOrDefault() ?? pathPoints.FirstOrDefault();
                    JobPrefab job = sp?.AssignedJob;
                    CorpsePrefab selectedPrefab;
                    if (job == null)
                    {
                        selectedPrefab = GetCorpsePrefab(p => p.SpawnPosition == PositionType.Wreck);
                    }
                    else
                    {
                        selectedPrefab = GetCorpsePrefab(p => p.SpawnPosition == PositionType.Wreck && (p.Job == "any" || p.Job == job.Identifier));
                        if (selectedPrefab == null)
                        {
                            corpsePoints.Remove(sp);
                            pathPoints.Remove(sp);
                            sp = corpsePoints.FirstOrDefault(sp => sp.AssignedJob == null) ?? pathPoints.FirstOrDefault(sp => sp.AssignedJob == null);
                            // Deduce the job from the selected prefab
                            selectedPrefab = GetCorpsePrefab(p => p.SpawnPosition == PositionType.Wreck);
                        }
                    }
                    if (selectedPrefab == null) { continue; }
                    Vector2 worldPos;
                    if (sp == null)
                    {
                        if (!TryGetExtraSpawnPoint(out worldPos))
                        {
                            break;
                        }
                    }
                    else
                    {
                        worldPos = sp.WorldPosition;
                        corpsePoints.Remove(sp);
                        pathPoints.Remove(sp);
                    }

                    job ??= selectedPrefab.GetJobPrefab();
                    if (job == null) { continue; }

                    var characterInfo = new CharacterInfo(CharacterPrefab.HumanSpeciesName, jobPrefab: job, randSync: Rand.RandSync.Server);
                    var corpse = Character.Create(CharacterPrefab.HumanConfigFile, worldPos, ToolBox.RandomSeed(8), characterInfo, hasAi: true, createNetworkEvent: true);
                    corpse.AnimController.FindHull(worldPos, true);
                    corpse.TeamID = Character.TeamType.None;
                    corpse.EnableDespawn = false;
                    selectedPrefab.GiveItems(corpse, wreck);
                    corpse.Kill(CauseOfDeathType.Unknown, causeOfDeathAffliction: null, log: false);
                    spawnCounter++;

                    static CorpsePrefab GetCorpsePrefab(Func<CorpsePrefab, bool> predicate)
                    {
                        IEnumerable<CorpsePrefab> filteredPrefabs = CorpsePrefab.Prefabs.Where(predicate);
                        return ToolBox.SelectWeightedRandom(filteredPrefabs.ToList(), filteredPrefabs.Select(p => p.Commonness).ToList(), Rand.RandSync.Unsynced);
                    }
                }
#if DEBUG
                DebugConsole.NewMessage($"{spawnCounter}/{corpseCount} corpses spawned in {wreck.Info.Name}.", spawnCounter == corpseCount ? Color.Green : Color.Yellow);
#endif
                bool TryGetExtraSpawnPoint(out Vector2 point)
                {
                    point = Vector2.Zero;
                    var hull = Hull.hullList.FindAll(h => h.Submarine == wreck).GetRandom();
                    if (hull != null)
                    {
                        point = hull.WorldPosition;
                    }
                    return hull != null;
                }
            }
        }

        public void SpawnNPCs()
        {
            if (Type != LevelData.LevelType.Outpost) { return; }
            foreach (Submarine sub in Submarine.Loaded)
            {
                if (sub?.Info?.OutpostGenerationParams != null)
                {
                    OutpostGenerator.SpawnNPCs((GameMain.GameSession?.GameMode as CampaignMode)?.Map?.CurrentLocation, sub);
                }
            }
        }

        public void DebugSetStartLocation(Location newStartLocation)
        {
            StartLocation = newStartLocation;
        }

        public void DebugSetEndLocation(Location newEndLocation)
        {
            EndLocation = newEndLocation;
        }

        public override void Remove()
        {
            base.Remove();
#if CLIENT
            if (renderer != null) 
            {
                renderer.Dispose();
                renderer = null;
            }
#endif

            if (LevelObjectManager != null)
            {
                LevelObjectManager.Remove();
                LevelObjectManager = null;
            }

            if (Ruins != null)
            {
                Ruins.Clear();
                Ruins = null;
            }

            if (ExtraWalls != null)
            {
                foreach (LevelWall w in ExtraWalls)
                {
                    w.Dispose();
                }

                ExtraWalls = null;
            }

            cells = null;
            
            if (bodies != null)
            {
                bodies.Clear();
                bodies = null;
            }

            Loaded = null;
        }
        
        public void ServerWrite(IWriteMessage msg, Client c, object[] extraData = null)
        {
            foreach (LevelWall levelWall in ExtraWalls)
            {
                if (levelWall.Body.BodyType == BodyType.Static) continue;

                msg.Write(levelWall.Body.Position.X);
                msg.Write(levelWall.Body.Position.Y);
                msg.WriteRangedSingle(levelWall.MoveState, 0.0f, MathHelper.TwoPi, 16);
            }
        }
    }      
}
