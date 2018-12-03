using Barotrauma.Networking;
using Barotrauma.RuinGeneration;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Factories;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Voronoi2;

namespace Barotrauma
{
    partial class Level : Entity, IServerSerializable
    {
        //all entities are disabled after they reach this depth
        public const int MaxEntityDepth = -300000;
        public const float ShaftHeight = 1000.0f;

        public static Level Loaded
        {
            get { return loaded; }
        }

        [Flags]
        public enum PositionType
        {
            MainPath = 1, Cave = 2, Ruin = 4
        }

        public struct InterestingPosition
        {
            public Point Position;
            public readonly PositionType PositionType;

            public InterestingPosition(Point position, PositionType positionType)
            {
                Position = position;
                PositionType = positionType;
            }
        }

        static Level loaded;

        //how close the sub has to be to start/endposition to exit
        public const float ExitDistance = 6000.0f;

        private string seed;

        public const int GridCellSize = 2000;
        private List<VoronoiCell>[,] cellGrid;

        private float[,] sonarDisruptionStrength;

        private List<LevelWall> extraWalls;

        private LevelWall seaFloor;

        private List<VoronoiCell> cells;

        //private VertexBuffer vertexBuffer;

        private Point startPosition, endPosition;

        private Rectangle borders;

        private List<Body> bodies;

        private List<InterestingPosition> positionsOfInterest;

        private List<Ruin> ruins;

        private LevelGenerationParams generationParams;

        private List<List<Point>> smallTunnels = new List<List<Point>>();

        private LevelObjectManager levelObjectManager;

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
            get { return new Point(borders.Width, borders.Height); }
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

        public LevelWall SeaFloor
        {
            get { return seaFloor; }
        }

        public List<Ruin> Ruins
        {
            get { return ruins; }
        }

        public List<LevelWall> ExtraWalls
        {
            get { return extraWalls; }
        }

        public List<List<Point>> SmallTunnels
        {
            get { return smallTunnels; }
        }

        public List<InterestingPosition> PositionsOfInterest
        {
            get { return positionsOfInterest; }
        }

        public string Seed
        {
            get { return seed; }
        }

        public Biome Biome;

        /// <summary>
        /// A random integer assigned at the end of level generation. If these values differ between clients/server,
        /// it means the levels aren't identical for some reason and there will most likely be major ID mismatches.
        /// </summary>
        public int EqualityCheckVal
        {
            get;
            private set;
        }

        public float Difficulty
        {
            get;
            private set;
        }

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

        public LevelObjectManager LevelObjectManager
        {
            get { return levelObjectManager; }
        }

        public bool Mirrored
        {
            get;
            private set;
        }

        public LevelGenerationParams GenerationParams
        {
            get { return generationParams; }
        }

        public Color BackgroundTextureColor
        {
            get { return generationParams.BackgroundTextureColor; }
        }

        public Color BackgroundColor
        {
            get { return generationParams.BackgroundColor; }
        }

        public Color WallColor
        {
            get { return generationParams.WallColor; }
        }

        /// <summary>
        /// Instantiates a level (the Generate-function still needs to be called before the level is playable)
        /// </summary>
        /// <param name="difficulty">A scalar between 0-100</param>
        /// <param name="sizeFactor">A scalar between 0-1 (0 = the minimum width defined in the generation params is used, 1 = the max width is used)</param>
        public Level(string seed, float difficulty, float sizeFactor, LevelGenerationParams generationParams, Biome biome)
            : base(null)
        {

            this.seed = seed;
            this.Biome = biome;
            this.Difficulty = difficulty;
            this.generationParams = generationParams;
            
            sizeFactor = MathHelper.Clamp(sizeFactor, 0.0f, 1.0f);
            int width = (int)MathHelper.Lerp(generationParams.MinWidth, generationParams.MaxWidth, sizeFactor);

            borders = new Rectangle(0, 0,
                (width / GridCellSize) * GridCellSize,
                (generationParams.Height / GridCellSize) * GridCellSize);

            //remove from entity dictionary
            base.Remove();
        }

        public static Level CreateRandom(LocationConnection locationConnection)
        {
            string seed = locationConnection.Locations[0].Name + locationConnection.Locations[1].Name;

            float sizeFactor = MathUtils.InverseLerp(
                MapGenerationParams.Instance.SmallLevelConnectionLength, 
                MapGenerationParams.Instance.LargeLevelConnectionLength,
                locationConnection.Length);

            return new Level(seed,
                locationConnection.Difficulty, 
                sizeFactor,
                LevelGenerationParams.GetRandom(seed, locationConnection.Biome), 
                locationConnection.Biome);
        }

        public static Level CreateRandom(string seed = "", float? difficulty = null, LevelGenerationParams generationParams = null)
        {
            if (seed == "")
            {
                seed = Rand.Range(0, int.MaxValue, Rand.RandSync.Server).ToString();
            }

            Rand.SetSyncedSeed(ToolBox.StringToInt(seed));

            if (generationParams == null) generationParams = LevelGenerationParams.GetRandom(seed);
            var biome = LevelGenerationParams.GetBiomes().Find(b => generationParams.AllowedBiomes.Contains(b));

            return new Level(
                seed,
                difficulty ?? Rand.Range(30.0f, 80.0f, Rand.RandSync.Server),
                Rand.Range(0.0f, 1.0f, Rand.RandSync.Server),
                generationParams,
                biome);
        }

        public void Generate(bool mirror)
        {
            if (loaded != null) loaded.Remove();            
            loaded = this;
            
            levelObjectManager = new LevelObjectManager();

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
            
            positionsOfInterest = new List<InterestingPosition>();
            extraWalls = new List<LevelWall>();
            bodies = new List<Body>();
            List<Vector2> sites = new List<Vector2>();
            
            Voronoi voronoi = new Voronoi(1.0);
            Rand.SetSyncedSeed(ToolBox.StringToInt(seed));

#if CLIENT
            renderer = new LevelRenderer(this);
#endif

            SeaFloorTopPos = generationParams.SeaFloorDepth + generationParams.MountainHeightMax + generationParams.SeaFloorVariance;

            int minWidth = 6500;
            if (Submarine.MainSub != null)
            {
                Rectangle dockedSubBorders = Submarine.MainSub.GetDockedBorders();
                minWidth = Math.Max(minWidth, Math.Max(dockedSubBorders.Width, dockedSubBorders.Height));
            }

            Rectangle pathBorders = borders;
            pathBorders.Inflate(-minWidth * 2, -minWidth * 2);

            startPosition = new Point(
                Rand.Range(minWidth, minWidth * 2, Rand.RandSync.Server),
                Rand.Range(borders.Height / 2, borders.Height - minWidth * 2, Rand.RandSync.Server));

            endPosition = new Point(
                borders.Width - Rand.Range(minWidth, minWidth * 2, Rand.RandSync.Server),
                Rand.Range(borders.Height / 2, borders.Height - minWidth * 2, Rand.RandSync.Server));

            //----------------------------------------------------------------------------------
            //generate the initial nodes for the main path and smaller tunnels
            //----------------------------------------------------------------------------------

            List<Point> pathNodes = new List<Point>();
            pathNodes.Add(new Point(startPosition.X, borders.Height));

            Point nodeInterval = generationParams.MainPathNodeIntervalRange;

            for (int  x = startPosition.X + nodeInterval.X;
                        x < endPosition.X   - nodeInterval.X;
                        x += Rand.Range(nodeInterval.X, nodeInterval.Y, Rand.RandSync.Server))
            {
                pathNodes.Add(new Point(x, Rand.Range(pathBorders.Y, pathBorders.Bottom, Rand.RandSync.Server)));
            }

            pathNodes.Add(new Point(endPosition.X, borders.Height));
            
            if (pathNodes.Count <= 2)
            {
                pathNodes.Insert(1, borders.Center);
            }

            GenerateTunnels(pathNodes, minWidth);

            //----------------------------------------------------------------------------------
            //generate voronoi sites
            //----------------------------------------------------------------------------------

            Point siteInterval = generationParams.VoronoiSiteInterval;
            int siteIntervalSqr = (siteInterval.X * siteInterval.X + siteInterval.Y * siteInterval.Y);
            Point siteVariance = generationParams.VoronoiSiteVariance;
            List<double> siteCoordsX = new List<double>((borders.Height / siteInterval.Y) * (borders.Width / siteInterval.Y));
            List<double> siteCoordsY = new List<double>((borders.Height / siteInterval.Y) * (borders.Width / siteInterval.Y));
            for (int x = siteInterval.X / 2; x < borders.Width; x += siteInterval.X)
            {
                for (int y = siteInterval.Y / 2; y < borders.Height; y += siteInterval.Y)
                {
                    int siteX = x + Rand.Range(-siteVariance.X, siteVariance.X, Rand.RandSync.Server);
                    int siteY = y + Rand.Range(-siteVariance.Y, siteVariance.Y, Rand.RandSync.Server);

                    if (smallTunnels.Any(t => t.Any(node => MathUtils.DistanceSquared(node.X, node.Y, siteX, siteY) < siteIntervalSqr)))
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
                positionsOfInterest.Add(new InterestingPosition(
                    new Point((int)mainPath[i].Site.Coord.X, (int)mainPath[i].Site.Coord.Y), 
                    PositionType.MainPath));
            }

            List<VoronoiCell> pathCells = new List<VoronoiCell>(mainPath);

            //make sure the path is wide enough to pass through
            EnlargeMainPath(pathCells, minWidth);

            foreach (InterestingPosition positionOfInterest in positionsOfInterest)
            {
                WayPoint wayPoint = new WayPoint(
                    positionOfInterest.Position.ToVector2(),
                    SpawnType.Enemy,
                    submarine: null);
            }

            startPosition.X = (int)pathCells[0].Site.Coord.X;

            //----------------------------------------------------------------------------------
            // tunnels through the tunnel nodes
            //----------------------------------------------------------------------------------

            List<List<Point>> validTunnels = new List<List<Point>>();
            foreach (List<Point> tunnel in smallTunnels)
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
                positionsOfInterest.Add(new InterestingPosition(tunnel.Last(), PositionType.Cave));
                if (tunnel.Count > 4) positionsOfInterest.Add(new InterestingPosition(tunnel[tunnel.Count / 2], PositionType.Cave));
                validTunnels.Add(tunnel);
                pathCells.AddRange(newPathCells);
            }
            smallTunnels = validTunnels;

            sw2.Restart();


            //----------------------------------------------------------------------------------
            // remove unnecessary cells and create some holes at the bottom of the level
            //----------------------------------------------------------------------------------
            
            cells = CleanCells(pathCells);

            int xPadding = borders.Width / 5;
            int yPadding = borders.Height / 5;
            pathCells.AddRange(CreateBottomHoles(generationParams.BottomHoleProbability, new Rectangle(
                xPadding, 0,
                borders.Width - xPadding * 2, borders.Height - yPadding)));

            foreach (VoronoiCell cell in cells)
            {
                if (cell.Site.Coord.Y < borders.Height / 2) continue;
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
                        if (mirroredEdges.Contains(edge)) continue;
                        edge.Point1.X = borders.Width - edge.Point1.X;
                        edge.Point2.X = borders.Width - edge.Point2.X;
                        if (!mirroredSites.Contains(edge.Site1))
                        {
                            //make sure that sites right at the edge of a grid cell end up in the same cell as in the non-mirrored level
                            if (edge.Site1.Coord.X % GridCellSize < 1.0f &&
                                edge.Site1.Coord.X % GridCellSize >= 0.0f) edge.Site1.Coord.X += 1.0f;
                            edge.Site1.Coord.X = borders.Width - edge.Site1.Coord.X;
                            mirroredSites.Add(edge.Site1);
                        }
                        if (!mirroredSites.Contains(edge.Site2))
                        {
                            if (edge.Site2.Coord.X % GridCellSize < 1.0f &&
                                edge.Site2.Coord.X % GridCellSize >= 0.0f) edge.Site2.Coord.X += 1.0f;
                            edge.Site2.Coord.X = borders.Width - edge.Site2.Coord.X;
                            mirroredSites.Add(edge.Site2);
                        }
                        mirroredEdges.Add(edge);
                    }
                }


                foreach (List<Point> smallTunnel in smallTunnels)
                {
                    for (int i = 0; i < smallTunnel.Count; i++)
                    {
                        smallTunnel[i] = new Point(borders.Width - smallTunnel[i].X, smallTunnel[i].Y);
                    }
                }

                for (int i = 0; i < positionsOfInterest.Count; i++)
                {
                    positionsOfInterest[i] = new InterestingPosition(
                        new Point(borders.Width - positionsOfInterest[i].Position.X, positionsOfInterest[i].Position.Y),
                        positionsOfInterest[i].PositionType);
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
            
            //----------------------------------------------------------------------------------
            // create some ruins
            //----------------------------------------------------------------------------------

            ruins = new List<Ruin>();
            for (int i = 0; i < generationParams.RuinCount; i++)
            {
                GenerateRuin(mainPath, this, mirror);
            }

            //----------------------------------------------------------------------------------
            // create floating ice chunks
            //----------------------------------------------------------------------------------

            if (generationParams.FloatingIceChunkCount > 0)
            {
                List<Point> iceChunkPositions = new List<Point>();
                foreach (InterestingPosition pos in positionsOfInterest)
                {
                    if (pos.PositionType != PositionType.MainPath || pos.Position.X < 5000 || pos.Position.X > Size.X - 5000) continue;
                    if (Math.Abs(pos.Position.X - StartPosition.X) < 10000) continue;
                    if (Math.Abs(pos.Position.Y - StartPosition.Y) < 10000) continue;
                    if (GetTooCloseCells(pos.Position.ToVector2(), minWidth * 0.7f).Count > 0) continue;
                    iceChunkPositions.Add(pos.Position);
                }
                        
                for (int i = 0; i < generationParams.FloatingIceChunkCount; i++)
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
                    newChunk.Body.GravityScale = 0.0f;
                    newChunk.Body.Mass *= 10.0f;
                    extraWalls.Add(newChunk);
                    iceChunkPositions.Remove(selectedPos);
                }
            }

            //----------------------------------------------------------------------------------
            // generate the bodies and rendered triangles of the cells
            //----------------------------------------------------------------------------------

            startPosition.Y = borders.Height;
            endPosition.Y = borders.Height;

            foreach (VoronoiCell cell in cells)
            {
                foreach (GraphEdge ge in cell.Edges)
                {
                    VoronoiCell adjacentCell = ge.AdjacentCell(cell);
                    ge.IsSolid = (adjacentCell == null || !cells.Contains(adjacentCell));
                }
            }

            List<VoronoiCell> cellsWithBody = new List<VoronoiCell>(cells);
            if (generationParams.CellRoundingAmount > 0.01f || generationParams.CellIrregularity > 0.01f)
            {
                foreach (VoronoiCell cell in cellsWithBody)
                {
                    CaveGenerator.RoundCell(cell,
                        minEdgeLength: generationParams.CellSubdivisionLength,
                        roundingAmount: generationParams.CellRoundingAmount,
                        irregularity: generationParams.CellIrregularity);
                }
            }

            bodies.Add(CaveGenerator.GeneratePolygons(cellsWithBody, this, out List<Vector2[]> triangles));

#if CLIENT
            renderer.SetBodyVertices(CaveGenerator.GenerateRenderVerticeList(triangles).ToArray(), generationParams.WallColor);
            renderer.SetWallVertices(CaveGenerator.GenerateWallShapes(cellsWithBody, this), generationParams.WallColor);
#endif

            TopBarrier = BodyFactory.CreateEdge(GameMain.World, 
                ConvertUnits.ToSimUnits(new Vector2(borders.X, 0)), 
                ConvertUnits.ToSimUnits(new Vector2(borders.Right, 0)));

            TopBarrier.SetTransform(ConvertUnits.ToSimUnits(new Vector2(0.0f, borders.Height)), 0.0f);                
            TopBarrier.BodyType = BodyType.Static;
            TopBarrier.CollisionCategories = Physics.CollisionLevel;

            bodies.Add(TopBarrier);

            GenerateSeaFloor(mirror);

            levelObjectManager.PlaceObjects(this, generationParams.LevelObjectAmount);

            EqualityCheckVal = Rand.Int(int.MaxValue, Rand.RandSync.Server);

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

            if (mirror)
            {
                Point temp = startPosition;
                startPosition = endPosition;
                endPosition = temp;
            }

            sonarDisruptionStrength = new float[cellGrid.GetLength(0), cellGrid.GetLength(1)];

            Debug.WriteLine("**********************************************************************************");
            Debug.WriteLine("Generated a map with " + siteCoordsX.Count + " sites in " + sw.ElapsedMilliseconds + " ms");
            Debug.WriteLine("Seed: " + seed);
            Debug.WriteLine("**********************************************************************************");

            if (GameSettings.VerboseLogging)
            {
                DebugConsole.NewMessage("Generated level with the seed " + seed + " (type: " + generationParams.Name + ")", Color.White);
            }

            //assign an ID to make entity events work
            ID = FindFreeID();
        }


        private List<VoronoiCell> CreateBottomHoles(float holeProbability, Rectangle limits)
        {
            List<VoronoiCell> toBeRemoved = new List<VoronoiCell>();
            foreach (VoronoiCell cell in cells)
            {
                if (Rand.Range(0.0f, 1.0f, Rand.RandSync.Server) > holeProbability) continue;

                if (!limits.Contains(cell.Site.Coord.X, cell.Site.Coord.Y)) continue;

                float closestDist = 0.0f;
                WayPoint closestWayPoint = null;
                foreach (WayPoint wp in WayPoint.WayPointList)
                {
                    if (wp.SpawnType != SpawnType.Path) continue;

                    float dist = Math.Abs(cell.Center.X - wp.WorldPosition.X);
                    if (closestWayPoint == null || dist < closestDist)
                    {
                        closestDist = dist;
                        closestWayPoint = wp;
                    }
                }

                if (closestWayPoint.WorldPosition.Y < cell.Center.Y) continue;

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

        private List<VoronoiCell> GetTooCloseCells(Vector2 position, float minDistance)
        {
            List<VoronoiCell> tooCloseCells = new List<VoronoiCell>();

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

                if (tooClose && !tooCloseCells.Contains(cell)) tooCloseCells.Add(cell);
            }
            
            return tooCloseCells;
        }


        /// <summary>
        /// remove all cells except those that are adjacent to the empty cells
        /// </summary>
        private List<VoronoiCell> CleanCells(List<VoronoiCell> emptyCells)
        {
            List<VoronoiCell> newCells = new List<VoronoiCell>();

            foreach (VoronoiCell cell in emptyCells)
            {
                foreach (GraphEdge edge in cell.Edges)
                {
                    VoronoiCell adjacent = edge.AdjacentCell(cell);
                    if (adjacent != null && !newCells.Contains(adjacent))
                    {
                        newCells.Add(adjacent);
                    }
                }
            }

            return newCells;
        }

        private void GenerateSeaFloor(bool mirror)
        {
            BottomPos = generationParams.SeaFloorDepth;
            SeaFloorTopPos = BottomPos;

            bottomPositions = new List<Point>
            {
                new Point(0, BottomPos)
            };

            int mountainCount = Rand.Range(generationParams.MountainCountMin, generationParams.MountainCountMax, Rand.RandSync.Server);
            for (int i = 0; i < mountainCount; i++)
            {
                bottomPositions.Add(
                    new Point(Size.X / (mountainCount + 1) * (i + 1),
                    BottomPos + Rand.Range(generationParams.MountainHeightMin, generationParams.MountainHeightMax, Rand.RandSync.Server)));
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
                            (bottomPositions[i].Y + bottomPositions[i + 1].Y) / 2 + Rand.Range(0, generationParams.SeaFloorVariance, Rand.RandSync.Server)));
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
            seaFloor = new LevelWall(bottomPositions.Select(p => p.ToVector2()).ToList(), new Vector2(0.0f, -2000.0f), generationParams.WallColor, this);
            extraWalls.Add(seaFloor);

            BottomBarrier = BodyFactory.CreateEdge(GameMain.World,
                ConvertUnits.ToSimUnits(new Vector2(borders.X, 0)),
                ConvertUnits.ToSimUnits(new Vector2(borders.Right, 0)));

            BottomBarrier.SetTransform(ConvertUnits.ToSimUnits(new Vector2(0.0f, BottomPos)), 0.0f);
            BottomBarrier.BodyType = BodyType.Static;
            BottomBarrier.CollisionCategories = Physics.CollisionLevel;

            bodies.Add(BottomBarrier);
        }

        private void GenerateTunnels(List<Point> pathNodes, int pathWidth)
        {
            smallTunnels = new List<List<Point>>();
            for (int i = 0; i < generationParams.SmallTunnelCount; i++)
            {
                var tunnelStartPos = pathNodes[Rand.Range(1, pathNodes.Count - 2, Rand.RandSync.Server)];

                int tunnelLength = Rand.Range(
                    generationParams.SmallTunnelLengthRange.X,
                    generationParams.SmallTunnelLengthRange.Y,
                    Rand.RandSync.Server);

                List<Point> tunnelNodes = new List<Point>()
                {
                    tunnelStartPos,
                    tunnelStartPos + new Point(0, Math.Sign(tunnelStartPos.Y - Size.Y / 2) * pathWidth * 2)
                };

                List<Point> tunnel = GenerateTunnel(
                    tunnelNodes, 
                    Rand.Range(generationParams.SmallTunnelLengthRange.X, generationParams.SmallTunnelLengthRange.Y, Rand.RandSync.Server), 
                    pathNodes);
                if (tunnel.Any()) smallTunnels.Add(tunnel);

                int branches = Rand.Range(0, 3, Rand.RandSync.Server);
                for (int j = 0; j < branches; j++)
                {
                    List<Point> branch = GenerateTunnel(
                        new List<Point>() { tunnel[Rand.Int(tunnel.Count, Rand.RandSync.Server)] },
                        Rand.Range(generationParams.SmallTunnelLengthRange.X, generationParams.SmallTunnelLengthRange.Y, Rand.RandSync.Server) * 0.5f,
                        pathNodes);
                    if (branch.Any()) smallTunnels.Add(branch);
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

        private void GenerateRuin(List<VoronoiCell> mainPath, Level level, bool mirror)
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

            double minDist = ruinRadius * 2;
            double minDistSqr = minDist * minDist;

            int iter = 0;
            while (mainPath.Any(p => MathUtils.DistanceSquared(ruinPos.X, ruinPos.Y, p.Site.Coord.X, p.Site.Coord.Y) < minDistSqr) ||
                ruins.Any(r => r.Area.Intersects(new Rectangle(ruinPos - new Point(ruinSize.X / 2, ruinSize.Y / 2), ruinSize))))
            {
                double weighedPathPosX = ruinPos.X;
                double weighedPathPosY = ruinPos.Y;
                iter++;

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

                    double moveAmountX = 1000.0 * diffX / distSqr;
                    double moveAmountY = 1000.0 * diffY / distSqr;

                    weighedPathPosX += moveAmountX;
                    weighedPathPosY += moveAmountY;
                    weighedPathPosY = Math.Min(borders.Y + borders.Height - ruinSize.Y / 2, weighedPathPosY);
                }

                Rectangle ruinArea = new Rectangle(ruinPos - new Point(ruinSize.X / 2, ruinSize.Y / 2), ruinSize);
                foreach (Ruin otherRuin in ruins)
                {
                    if (!otherRuin.Area.Intersects(ruinArea)) continue;

                    double diffX = ruinArea.Center.X - otherRuin.Area.Center.X;
                    double diffY = ruinArea.Center.Y - otherRuin.Area.Center.Y;

                    double distSqr = diffX * diffX + diffY * diffY;
                    if (distSqr < 0.01f) { diffX = 0; diffY = -1; }

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
                if (ruinPos.Y + ruinSize.Y / 2 > level.Size.Y)
                {
                    ruinPos.Y -= ((ruinPos.Y + ruinSize.Y / 2) - level.Size.Y);
                }
                if (iter > 10000) break;
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
            ruins.Add(ruin);
            
            ruin.RuinShapes.Sort((shape1, shape2) => shape2.DistanceFromEntrance.CompareTo(shape1.DistanceFromEntrance));
            for (int i = 0; i < 4 && i < ruin.RuinShapes.Count; i++)
            {
                positionsOfInterest.Add(new InterestingPosition(ruin.RuinShapes[i].Rect.Center, PositionType.Ruin));
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
                            MathUtils.GetLineRectangleIntersection(e.Point1, e.Point2, rect) != null)
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
        }

        public Vector2 GetRandomItemPos(PositionType spawnPosType, float randomSpread, float minDistFromSubs, float offsetFromWall = 10.0f)
        {
            if (!positionsOfInterest.Any())
            {
                return new Vector2(Size.X / 2, Size.Y / 2);
            }

            Vector2 position = Vector2.Zero;

            offsetFromWall = ConvertUnits.ToSimUnits(offsetFromWall);

            int tries = 0;
            do
            {
                Loaded.TryGetInterestingPosition(true, spawnPosType, minDistFromSubs, out Vector2 startPos);

                startPos += Rand.Vector(Rand.Range(0.0f, randomSpread, Rand.RandSync.Server), Rand.RandSync.Server);

                Vector2 endPos = startPos - Vector2.UnitY * Size.Y;

                if (Submarine.PickBody(
                    ConvertUnits.ToSimUnits(startPos),
                    ConvertUnits.ToSimUnits(endPos),
                    null, Physics.CollisionLevel) != null)
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
            if (!positionsOfInterest.Any())
            {
                position = new Point(Size.X / 2, Size.Y / 2);
                return false;
            }

            var matchingPositions = positionsOfInterest.FindAll(p => positionType.HasFlag(p.PositionType));

            if (minDistFromSubs > 0.0f)
            {
                foreach (Submarine sub in Submarine.Loaded)
                {
                    matchingPositions.RemoveAll(p => Vector2.DistanceSquared(p.Position.ToVector2(), sub.WorldPosition) < minDistFromSubs * minDistFromSubs);
                }
            }

            if (!matchingPositions.Any())
            {
#if DEBUG
                DebugConsole.ThrowError("Could not find a suitable position of interest. (PositionType: " + positionType + ", minDistFromSubs: " + minDistFromSubs + "\n" + Environment.StackTrace);
#endif

                position = positionsOfInterest[Rand.Int(positionsOfInterest.Count, (useSyncedRand ? Rand.RandSync.Server : Rand.RandSync.Unsynced))].Position;
                return false;
            }

            position = matchingPositions[Rand.Int(matchingPositions.Count, (useSyncedRand ? Rand.RandSync.Server : Rand.RandSync.Unsynced))].Position;
            return true;
        }

        public void Update(float deltaTime, Camera cam)
        {
            levelObjectManager.Update(deltaTime);

            for (int x = 0; x < sonarDisruptionStrength.GetLength(0); x++)
            {
                for (int y = 0; y < sonarDisruptionStrength.GetLength(1); y++)
                {
                    //disruption fades out over time if the entities causing it stop calling SetSonarDisruptionStrength
                    sonarDisruptionStrength[x, y] = Math.Max(0.0f, sonarDisruptionStrength[x, y] - deltaTime);
                }
            }

            foreach (LevelWall wall in ExtraWalls)
            {
                wall.Update(deltaTime);
            }
            
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
            {
                networkUpdateTimer += deltaTime;
                if (networkUpdateTimer > NetworkUpdateInterval)
                {
                    if (extraWalls.Any(w => w.Body.BodyType != BodyType.Static))
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

        public float GetSonarDisruptionStrength(Vector2 worldPos)
        {
            int gridPosX = (int)Math.Floor(worldPos.X / GridCellSize);
            if (gridPosX < 0 || gridPosX >= sonarDisruptionStrength.GetLength(0)) return 0.0f;
            int gridPosY = (int)Math.Floor(worldPos.Y / GridCellSize);
            if (gridPosY < 0 || gridPosY >= sonarDisruptionStrength.GetLength(1)) return 0.0f;

            return sonarDisruptionStrength[gridPosX, gridPosY];
        }

        public void SetSonarDisruptionStrength(Vector2 worldPos, float strength)
        {
            int gridPosX = (int)Math.Floor(worldPos.X / GridCellSize);
            if (gridPosX < 0 || gridPosX >= sonarDisruptionStrength.GetLength(0)) return;
            int gridPosY = (int)Math.Floor(worldPos.Y / GridCellSize);
            if (gridPosY < 0 || gridPosY >= sonarDisruptionStrength.GetLength(1)) return;

            sonarDisruptionStrength[gridPosX, gridPosY] = MathHelper.Clamp(strength, 0.0f, 1.0f);
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

        public List<VoronoiCell> GetCells(Vector2 worldPos, int searchDepth = 2)
        {
            int gridPosX = (int)Math.Floor(worldPos.X / GridCellSize);
            int gridPosY = (int)Math.Floor(worldPos.Y / GridCellSize);

            int startX = Math.Max(gridPosX - searchDepth, 0);
            int endX = Math.Min(gridPosX + searchDepth, cellGrid.GetLength(0) - 1);

            int startY = Math.Max(gridPosY - searchDepth, 0);
            int endY = Math.Min(gridPosY + searchDepth, cellGrid.GetLength(1) - 1);

            List<VoronoiCell> cells = new List<VoronoiCell>();
            for (int y = startY; y <= endY; y++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    cells.AddRange(cellGrid[x, y]);
                }
            }
            
            foreach (LevelWall wall in extraWalls)
            {
                foreach (VoronoiCell cell in wall.Cells)
                {
                    cells.Add(cell);
                }
            }
            
            return cells;
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

            if (levelObjectManager != null)
            {
                levelObjectManager.Remove();
                levelObjectManager = null;
            }

            if (ruins != null)
            {
                ruins.Clear();
                ruins = null;
            }

            if (extraWalls != null)
            {
                foreach (LevelWall w in extraWalls)
                {
                    w.Dispose();
                }

                extraWalls = null;
            }

            cells = null;
            
            if (bodies != null)
            {
                bodies.Clear();
                bodies = null;
            }

            loaded = null;
        }

        public void ServerWrite(NetBuffer msg, Client c, object[] extraData = null)
        {
            foreach (LevelWall levelWall in extraWalls)
            {
                if (levelWall.Body.BodyType == BodyType.Static) continue;

                msg.Write(levelWall.Body.Position.X);
                msg.Write(levelWall.Body.Position.Y);
                msg.WriteRangedSingle(levelWall.MoveState, 0.0f, MathHelper.TwoPi, 16);
            }
        }
    }      
}
