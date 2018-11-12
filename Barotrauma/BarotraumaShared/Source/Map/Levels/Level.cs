using Barotrauma.RuinGeneration;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Voronoi2;

namespace Barotrauma
{
    partial class Level
    {
        //all entities are disabled after they reach this depth
        public const float MaxEntityDepth = -300000.0f;
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

        struct InterestingPosition
        {
            public Vector2 Position;
            public readonly PositionType PositionType;

            public InterestingPosition(Vector2 position, PositionType positionType)
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

        private LevelWall[] extraWalls;

        //private float shaftHeight;

        //List<Body> bodies;
        //TODO: change back to private
        public List<VoronoiCell> cells;

        //private VertexBuffer vertexBuffer;

        private Vector2 startPosition, endPosition;

        private Rectangle borders;

        private List<Body> bodies;

        private List<InterestingPosition> positionsOfInterest;

        private List<Ruin> ruins;

        private Color backgroundColor;
        private Color wallColor;

        private LevelGenerationParams generationParams;

        private List<List<Vector2>> smallTunnels = new List<List<Vector2>>();

        private static BackgroundSpriteManager backgroundSpriteManager;

        private List<Vector2> bottomPositions;

        public Vector2 StartPosition
        {
            get { return startPosition; }
        }

        public Vector2 Size
        {
            get { return new Vector2(borders.Width, borders.Height); }
        }

        public Vector2 EndPosition
        {
            get { return endPosition; }
        }

        public float BottomPos
        {
            get;
            private set;
        }

        public float SeaFloorTopPos
        {
            get;
            private set;
        }

        public List<Ruin> Ruins
        {
            get { return ruins; }
        }
        
        public LevelWall[] ExtraWalls
        {
            get { return extraWalls; }
        }

        public List<List<Vector2>> SmallTunnels
        {
            get { return smallTunnels; }
        }

        public string Seed
        {
            get { return seed; }
        }

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

        public bool Mirrored
        {
            get;
            private set;
        }

        public LevelGenerationParams GenerationParams
        {
            get { return generationParams; }
        }

        public Color BackgroundColor
        {
            get { return backgroundColor; }
        }

        public Color WallColor
        {
            get { return wallColor; }
        }

        public Level(string seed, float difficulty, LevelGenerationParams generationParams)
        {
            this.seed = seed;
            
            this.Difficulty = difficulty;

            this.generationParams = generationParams;

            borders = new Rectangle(0, 0, 
                (int)(Math.Ceiling(generationParams.Width / GridCellSize) * GridCellSize), 
                (int)(Math.Ceiling(generationParams.Height / GridCellSize) * GridCellSize));
        }

        public static Level CreateRandom(LocationConnection locationConnection)
        {
            string seed = locationConnection.Locations[0].Name + locationConnection.Locations[1].Name;
            
            return new Level(seed, locationConnection.Difficulty, LevelGenerationParams.GetRandom(seed, locationConnection.Biome));
        }

        public static Level CreateRandom(string seed = "")
        {
            if (seed == "")
            {
                seed = Rand.Range(0, int.MaxValue, Rand.RandSync.Server).ToString();
            }

            Rand.SetSyncedSeed(ToolBox.StringToInt(seed));

            return new Level(seed, Rand.Range(30.0f, 80.0f, Rand.RandSync.Server), LevelGenerationParams.GetRandom(seed));
        }

        public void Generate(bool mirror)
        {
            Mirrored = mirror;

            if (backgroundSpriteManager == null)
            {
                var files = GameMain.SelectedPackage.GetFilesOfType(ContentType.BackgroundSpritePrefabs);
                if (files.Count > 0)
                    backgroundSpriteManager = new BackgroundSpriteManager(files);
                else
                    backgroundSpriteManager = new BackgroundSpriteManager("Content/BackgroundSprites/BackgroundSpritePrefabs.xml");
            }
#if CLIENT
            if (backgroundCreatureManager == null)
            {
                var files = GameMain.SelectedPackage.GetFilesOfType(ContentType.BackgroundCreaturePrefabs);
                if (files.Count > 0)
                    backgroundCreatureManager = new BackgroundCreatureManager(files);
                else
                    backgroundCreatureManager = new BackgroundCreatureManager("Content/BackgroundSprites/BackgroundCreaturePrefabs.xml");
            }
#endif

            Stopwatch sw = new Stopwatch();
            sw.Start();

            if (loaded != null) loaded.Unload();            
            loaded = this;

            positionsOfInterest = new List<InterestingPosition>();
            
            Voronoi voronoi = new Voronoi(1.0);

            List<Vector2> sites = new List<Vector2>();

            bodies = new List<Body>();

            Rand.SetSyncedSeed(ToolBox.StringToInt(seed));

#if CLIENT
            renderer = new LevelRenderer(this);

            backgroundColor = generationParams.BackgroundColor;
            float avgValue = (backgroundColor.R + backgroundColor.G + backgroundColor.G) / 3;
            GameMain.LightManager.AmbientLight = new Color(backgroundColor * (10.0f / avgValue), 1.0f);
#endif

            SeaFloorTopPos = generationParams.SeaFloorDepth + generationParams.MountainHeightMax + generationParams.SeaFloorVariance;

            float minWidth = 6500.0f;
            if (Submarine.MainSub != null)
            {
                Rectangle dockedSubBorders = Submarine.MainSub.GetDockedBorders();
                minWidth = Math.Max(minWidth, Math.Max(dockedSubBorders.Width, dockedSubBorders.Height));
            }

            Rectangle pathBorders = borders;
            pathBorders.Inflate(-minWidth * 2, -minWidth * 2);

            startPosition = new Vector2(
                Rand.Range(minWidth, minWidth * 2, Rand.RandSync.Server),
                Rand.Range(borders.Height * 0.5f, borders.Height - minWidth * 2, Rand.RandSync.Server));

            endPosition = new Vector2(
                borders.Width - Rand.Range(minWidth, minWidth * 2, Rand.RandSync.Server),
                Rand.Range(borders.Height * 0.5f, borders.Height - minWidth * 2, Rand.RandSync.Server));

            //----------------------------------------------------------------------------------
            //generate the initial nodes for the main path and smaller tunnels
            //----------------------------------------------------------------------------------

            List<Vector2> pathNodes = new List<Vector2>();
            pathNodes.Add(new Vector2(startPosition.X, borders.Height));

            Vector2 nodeInterval = generationParams.MainPathNodeIntervalRange;

            for (float  x = startPosition.X + nodeInterval.X;
                        x < endPosition.X   - nodeInterval.X;
                        x += Rand.Range(nodeInterval.X, nodeInterval.Y, Rand.RandSync.Server))
            {
                pathNodes.Add(new Vector2(x, Rand.Range(pathBorders.Y, pathBorders.Bottom, Rand.RandSync.Server)));
            }

            pathNodes.Add(new Vector2(endPosition.X, borders.Height));
            
            if (pathNodes.Count <= 2)
            {
                pathNodes.Insert(1, borders.Center.ToVector2());
            }

            GenerateTunnels(pathNodes, minWidth);

            //----------------------------------------------------------------------------------
            //generate voronoi sites
            //----------------------------------------------------------------------------------

            Vector2 siteInterval = generationParams.VoronoiSiteInterval;
            Vector2 siteVariance = generationParams.VoronoiSiteVariance;
            for (float x = siteInterval.X / 2; x < borders.Width; x += siteInterval.X)
            {
                for (float y = siteInterval.Y / 2; y < borders.Height; y += siteInterval.Y)
                {
                    Vector2 site = new Vector2(
                        x + Rand.Range(-siteVariance.X, siteVariance.X, Rand.RandSync.Server),
                        y + Rand.Range(-siteVariance.Y, siteVariance.Y, Rand.RandSync.Server));

                    if (smallTunnels.Any(t => t.Any(node => Vector2.Distance(node, site) < siteInterval.Length())))
                    {
                        //add some more sites around the small tunnels to generate more small voronoi cells
                        if (x < borders.Width - siteInterval.X) sites.Add(new Vector2(x, y) + Vector2.UnitX * siteInterval * 0.5f);
                        if (y < borders.Height - siteInterval.Y) sites.Add(new Vector2(x, y) + Vector2.UnitY * siteInterval * 0.5f);
                        if (x < borders.Width - siteInterval.X && y < borders.Height - siteInterval.Y) sites.Add(new Vector2(x, y) + Vector2.One * siteInterval * 0.5f);
                    }
                    
                    sites.Add(site);
                }
            }


            //----------------------------------------------------------------------------------
            // construct the voronoi graph and cells
            //----------------------------------------------------------------------------------

            Stopwatch sw2 = new Stopwatch();
            sw2.Start();

            List<GraphEdge> graphEdges = voronoi.MakeVoronoiGraph(sites, borders.Width, borders.Height);

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
                positionsOfInterest.Add(new InterestingPosition(mainPath[i].Center, PositionType.MainPath));
            }

            List<VoronoiCell> pathCells = new List<VoronoiCell>(mainPath);

            //make sure the path is wide enough to pass through
            EnlargeMainPath(pathCells, minWidth);

            foreach (InterestingPosition positionOfInterest in positionsOfInterest)
            {
                WayPoint wayPoint = new WayPoint(
                    positionOfInterest.Position,
                    SpawnType.Enemy,
                    submarine: null);
            }

            startPosition.X = pathCells[0].Center.X;

            //----------------------------------------------------------------------------------
            // tunnels through the tunnel nodes
            //----------------------------------------------------------------------------------

            foreach (List<Vector2> tunnel in smallTunnels)
            {
                if (tunnel.Count < 2) continue;

                //find the cell which the path starts from
                int startCellIndex = CaveGenerator.FindCellIndex(tunnel[0], cells, cellGrid, GridCellSize, 1);
                if (startCellIndex < 0) continue;

                //if it wasn't one of the cells in the main path, don't create a tunnel
                if (cells[startCellIndex].CellType != CellType.Path) continue;

                var newPathCells = CaveGenerator.GeneratePath(tunnel, cells, cellGrid, GridCellSize, pathBorders);

                positionsOfInterest.Add(new InterestingPosition(tunnel.Last(), PositionType.Cave));

                if (tunnel.Count > 4) positionsOfInterest.Add(new InterestingPosition(tunnel[tunnel.Count / 2], PositionType.Cave));
                
                pathCells.AddRange(newPathCells);
            }
            
            sw2.Restart();


            //----------------------------------------------------------------------------------
            // remove unnecessary cells and create some holes at the bottom of the level
            //----------------------------------------------------------------------------------
            
            cells = CleanCells(pathCells);            
            pathCells.AddRange(CreateBottomHoles(generationParams.BottomHoleProbability, new Rectangle(
                (int)(borders.Width * 0.2f), 0,
                (int)(borders.Width * 0.6f), (int)(borders.Height * 0.8f))));

            foreach (VoronoiCell cell in cells)
            {
                if (cell.Center.Y < borders.Height / 2) continue;
                cell.edges.ForEach(e => e.OutsideLevel = true);
            }

            //----------------------------------------------------------------------------------
            // initialize the cells that are still left and insert them into the cell grid
            //----------------------------------------------------------------------------------
            
            foreach (VoronoiCell cell in pathCells)
            {
                cell.edges.ForEach(e => e.OutsideLevel = false);

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
                    foreach (GraphEdge edge in cell.edges)
                    {
                        if (mirroredEdges.Contains(edge)) continue;
                        edge.point1.X = borders.Width - edge.point1.X;
                        edge.point2.X = borders.Width - edge.point2.X;
                        if (!mirroredSites.Contains(edge.site1))
                        {
                            //make sure that sites right at the edge of a grid cell end up in the same cell as in the non-mirrored level
                            if (edge.site1.coord.x % GridCellSize < 1.0f &&
                                edge.site1.coord.x % GridCellSize >= 0.0f) edge.site1.coord.x += 1.0f;
                            edge.site1.coord.x = borders.Width - edge.site1.coord.x;
                            mirroredSites.Add(edge.site1);
                        }
                        if (!mirroredSites.Contains(edge.site2))
                        {
                            if (edge.site2.coord.x % GridCellSize < 1.0f &&
                                edge.site2.coord.x % GridCellSize >= 0.0f) edge.site2.coord.x += 1.0f;
                            edge.site2.coord.x = borders.Width - edge.site2.coord.x;
                            mirroredSites.Add(edge.site2);
                        }
                        mirroredEdges.Add(edge);
                    }
                }


                foreach (List<Vector2> smallTunnel in smallTunnels)
                {
                    for (int i = 0; i < smallTunnel.Count; i++)
                    {
                        smallTunnel[i] = new Vector2(borders.Width - smallTunnel[i].X, smallTunnel[i].Y);
                    }
                }

                for (int i = 0; i < positionsOfInterest.Count; i++)
                {
                    positionsOfInterest[i] = new InterestingPosition(
                        new Vector2(borders.Width - positionsOfInterest[i].Position.X, positionsOfInterest[i].Position.Y),
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
                int x = (int)Math.Floor(cell.Center.X / GridCellSize);
                int y = (int)Math.Floor(cell.Center.Y / GridCellSize);

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
            // generate the bodies and rendered triangles of the cells
            //----------------------------------------------------------------------------------

            startPosition.Y = borders.Height;
            endPosition.Y = borders.Height;

            List<VoronoiCell> cellsWithBody = new List<VoronoiCell>(cells);

            List<Vector2[]> triangles;
            bodies = CaveGenerator.GeneratePolygons(cellsWithBody, this, out triangles);

#if CLIENT
            renderer.SetBodyVertices(CaveGenerator.GenerateRenderVerticeList(triangles).ToArray(), generationParams.WallColor);
            renderer.SetWallVertices(CaveGenerator.GenerateWallShapes(cells, this), generationParams.WallColor);
#endif

            TopBarrier = BodyFactory.CreateEdge(GameMain.World, 
                ConvertUnits.ToSimUnits(new Vector2(borders.X, 0)), 
                ConvertUnits.ToSimUnits(new Vector2(borders.Right, 0)));

            TopBarrier.SetTransform(ConvertUnits.ToSimUnits(new Vector2(0.0f, borders.Height)), 0.0f);                
            TopBarrier.BodyType = BodyType.Static;
            TopBarrier.CollisionCategories = Physics.CollisionLevel;

            bodies.Add(TopBarrier);

            GenerateSeaFloor(mirror);

            backgroundSpriteManager.PlaceSprites(this, generationParams.BackgroundSpriteAmount);

            EqualityCheckVal = Rand.Int(int.MaxValue, Rand.RandSync.Server);

#if CLIENT
            backgroundCreatureManager.SpawnSprites(80);
#endif

            foreach (VoronoiCell cell in cells)
            {
                foreach (GraphEdge edge in cell.edges)
                {
                    edge.cell1 = null;
                    edge.cell2 = null;
                    edge.site1 = null;
                    edge.site2 = null;
                }
            }
            
            //initialize MapEntities that aren't in any sub (e.g. items inside ruins)
            MapEntity.MapLoaded(null);

            Debug.WriteLine("Generatelevel: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();

            if (mirror)
            {
                Vector2 temp = startPosition;
                startPosition = endPosition;
                endPosition = temp;
            }

            Debug.WriteLine("**********************************************************************************");
            Debug.WriteLine("Generated a map with " + sites.Count + " sites in " + sw.ElapsedMilliseconds + " ms");
            Debug.WriteLine("Seed: " + seed);
            Debug.WriteLine("**********************************************************************************");

            if (GameSettings.VerboseLogging)
            {
                DebugConsole.NewMessage("Generated level with the seed " + seed + " (type: " + generationParams.Name + ")", Color.White);
            }
        }


        private List<VoronoiCell> CreateBottomHoles(float holeProbability, Rectangle limits)
        {
            List<VoronoiCell> toBeRemoved = new List<VoronoiCell>();
            foreach (VoronoiCell cell in cells)
            {
                if (Rand.Range(0.0f, 1.0f, Rand.RandSync.Server) > holeProbability) continue;

                if (!limits.Contains(cell.Center)) continue;

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

            var newWaypoint = new WayPoint(new Rectangle((int)pathCells[0].Center.X, borders.Height, 10, 10), null);
            wayPoints.Add(newWaypoint);
            
            for (int i = 0; i < pathCells.Count; i++)
            {
                pathCells[i].CellType = CellType.Path;

                newWaypoint = new WayPoint(new Rectangle((int)pathCells[i].Center.X, (int)pathCells[i].Center.Y, 10, 10), null);
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

            newWaypoint = new WayPoint(new Rectangle((int)pathCells[pathCells.Count - 1].Center.X, borders.Height, 10, 10), null);
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

            if (minDistance == 0.0f) return tooCloseCells;

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

            foreach (VoronoiCell cell in closeCells)
            {
                bool tooClose = false;
                foreach (GraphEdge edge in cell.edges)
                {
                    
                    if (Vector2.Distance(edge.point1, position) < minDistance ||
                        Vector2.Distance(edge.point2, position) < minDistance)
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
                foreach (GraphEdge edge in cell.edges)
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
            
            bottomPositions = new List<Vector2>();
            bottomPositions.Add(new Vector2(0, BottomPos));

            int mountainCount = Rand.Range(generationParams.MountainCountMin, generationParams.MountainCountMax, Rand.RandSync.Server);
            for (int i = 0; i < mountainCount; i++)
            {
                bottomPositions.Add(
                    new Vector2(Size.X / (mountainCount + 1) * (i + 1),
                    BottomPos + Rand.Range(generationParams.MountainHeightMin, generationParams.MountainHeightMax, Rand.RandSync.Server)));
            }
            bottomPositions.Add(new Vector2(Size.X, BottomPos));

            float minVertexInterval = 5000.0f;
            float currInverval = Size.X / 2.0f;
            while (currInverval > minVertexInterval)
            {
                for (int i = 0; i < bottomPositions.Count - 1; i++)
                {
                    bottomPositions.Insert(i + 1,
                        (bottomPositions[i] + bottomPositions[i + 1]) / 2.0f +
                        Vector2.UnitY * Rand.Range(0.0f, generationParams.SeaFloorVariance, Rand.RandSync.Server));

                    i++;
                }

                currInverval /= 2.0f;
            }

            if (mirror)
            {
                for (int i = 0; i < bottomPositions.Count; i++)
                {
                    bottomPositions[i] = new Vector2(borders.Size.X - bottomPositions[i].X, bottomPositions[i].Y);
                }
            }

            SeaFloorTopPos = bottomPositions.Max(p => p.Y);
            
            extraWalls = new LevelWall[] { new LevelWall(bottomPositions, new Vector2(0.0f, -2000.0f), backgroundColor, this) };

            BottomBarrier = BodyFactory.CreateEdge(GameMain.World,
                ConvertUnits.ToSimUnits(new Vector2(borders.X, 0)),
                ConvertUnits.ToSimUnits(new Vector2(borders.Right, 0)));

            BottomBarrier.SetTransform(ConvertUnits.ToSimUnits(new Vector2(0.0f, BottomPos)), 0.0f);
            BottomBarrier.BodyType = BodyType.Static;
            BottomBarrier.CollisionCategories = Physics.CollisionLevel;

            bodies.Add(BottomBarrier);
        }

        private void GenerateTunnels(List<Vector2> pathNodes, float pathWidth)
        {
            smallTunnels = new List<List<Vector2>>();
            for (int i = 0; i < generationParams.SmallTunnelCount; i++)
            {
                int startNodeIndex = Rand.Range(1, pathNodes.Count - 2, Rand.RandSync.Server);
                var tunnelStartPos = Vector2.Lerp(pathNodes[startNodeIndex], pathNodes[startNodeIndex + 1], Rand.Range(0.0f, 1.0f, Rand.RandSync.Server));

                float tunnelLength = Rand.Range(
                    generationParams.SmallTunnelLengthRange.X,
                    generationParams.SmallTunnelLengthRange.Y,
                    Rand.RandSync.Server);

                List<Vector2> tunnelNodes = new List<Vector2>()
                {
                    tunnelStartPos,
                    tunnelStartPos + Vector2.UnitY * Math.Sign(tunnelStartPos.Y - Size.Y / 2) * pathWidth * 2
                };

                List<Vector2> tunnel = GenerateTunnel(
                    tunnelNodes, 
                    Rand.Range(generationParams.SmallTunnelLengthRange.X, generationParams.SmallTunnelLengthRange.Y, Rand.RandSync.Server), 
                    pathNodes);
                if (tunnel.Any()) smallTunnels.Add(tunnel);

                int branches = Rand.Range(0, 3, Rand.RandSync.Server);
                for (int j = 0; j < branches; j++)
                {
                    List<Vector2> branch = GenerateTunnel(
                        new List<Vector2>() { tunnel[Rand.Int(tunnel.Count, Rand.RandSync.Server)] },
                        Rand.Range(generationParams.SmallTunnelLengthRange.X, generationParams.SmallTunnelLengthRange.Y, Rand.RandSync.Server) * 0.5f,
                        pathNodes);
                    if (branch.Any()) smallTunnels.Add(branch);
                }
                
            }
        }

        private List<Vector2> GenerateTunnel(List<Vector2> tunnelNodes, float tunnelLength, List<Vector2> avoidNodes)
        {
            float sectionLength = 1000.0f;

            float currLength = 0.0f;
            while (currLength < tunnelLength)
            {
                Vector2 dir = Rand.Vector(1.0f, Rand.RandSync.Server);
                                
                dir.Y += Math.Sign(tunnelNodes[tunnelNodes.Count - 1].Y - Size.Y / 2) * 0.5f;
                if (tunnelNodes.Count > 1)
                {
                    //keep heading roughly in the same direction as the previous nodes
                    Vector2 prevNodeDiff = tunnelNodes[tunnelNodes.Count - 1] - tunnelNodes[tunnelNodes.Count - 2];
                    if (prevNodeDiff != Vector2.Zero)
                    {
                        dir += Vector2.Normalize(tunnelNodes[tunnelNodes.Count - 1] - tunnelNodes[tunnelNodes.Count - 2]) * 0.5f;
                    }
                }

                float avoidDist = 20000.0f;
                foreach (Vector2 pathNode in avoidNodes)
                {
                    Vector2 diff = tunnelNodes[tunnelNodes.Count - 1] - pathNode;
                    if (diff == Vector2.Zero) continue;

                    float dist = diff.Length();
                    if (dist < avoidDist)
                    {
                        dir += (diff / dist) * (1.0f - dist / avoidDist);
                    }
                }

                Vector2 normalizedDir = Vector2.Normalize(dir);

                if (tunnelNodes.Last().Y + normalizedDir.Y > Size.Y)
                {
                    //head back down if the tunnel has reached the top of the level
                    normalizedDir.Y = -normalizedDir.Y;
                }
                else if (tunnelNodes.Last().Y + normalizedDir.Y + normalizedDir.Y < 0.0f ||
                    tunnelNodes.Last().Y + normalizedDir.Y + normalizedDir.Y < SeaFloorTopPos)
                {
                    //head back up if reached the sea floor
                    normalizedDir.Y = -normalizedDir.Y;
                }

                Vector2 nextNode = tunnelNodes.Last() + normalizedDir * sectionLength;

                nextNode.X = MathHelper.Clamp(nextNode.X, 500.0f, Size.X - 500.0f);
                nextNode.Y = MathHelper.Clamp(nextNode.Y, SeaFloorTopPos, Size.Y - 500.0f);
                tunnelNodes.Add(nextNode);
                currLength += sectionLength;
            }

            return tunnelNodes;
        }

        private void GenerateRuin(List<VoronoiCell> mainPath, Level level, bool mirror)
        {
            Vector2 ruinSize = new Vector2(Rand.Range(5000.0f, 8000.0f, Rand.RandSync.Server), Rand.Range(5000.0f, 8000.0f, Rand.RandSync.Server));
            float ruinRadius = Math.Max(ruinSize.X, ruinSize.Y) * 0.5f;
            
            int cellIndex = Rand.Int(cells.Count, Rand.RandSync.Server);
            Vector2 ruinPos = cells[cellIndex].Center;

            //50% chance of placing the ruins at a cave
            if (Rand.Range(0.0f, 1.0f, Rand.RandSync.Server) < 0.5f)
            {
                TryGetInterestingPosition(true, PositionType.Cave, 0.0f, out ruinPos);
            }

            ruinPos.Y = Math.Min(ruinPos.Y, borders.Y + borders.Height - ruinSize.Y / 2);
            ruinPos.Y = Math.Max(ruinPos.Y, SeaFloorTopPos + ruinSize.Y / 2.0f);

            float minDist = ruinRadius * 2.0f;
            float minDistSqr = minDist * minDist;
            
            int iter = 0;
            while (mainPath.Any(p => Vector2.DistanceSquared(ruinPos, p.Center) < minDistSqr) ||
                ruins.Any(r => r.Area.Intersects(new Rectangle(MathUtils.ToPoint(ruinPos - ruinSize / 2), MathUtils.ToPoint(ruinSize)))))
            {
                Vector2 weighedPathPos = ruinPos;
                iter++;

                foreach (VoronoiCell pathCell in mainPath)
                {
                    Vector2 diff = ruinPos - pathCell.Center;
                    float distSqr = diff.LengthSquared();
                    if (distSqr < 1.0f)
                    {
                        diff = Vector2.UnitY;
                        distSqr = 1.0f;
                    }
                    if (distSqr > 10000.0f * 10000.0f) continue;

                    Vector2 moveAmount = Vector2.Normalize(diff) * 100000.0f / (float)Math.Sqrt(distSqr);

                    weighedPathPos += moveAmount;
                    weighedPathPos.Y = Math.Min(borders.Y + borders.Height - ruinSize.Y / 2, weighedPathPos.Y);
                }
                Rectangle ruinArea = new Rectangle(MathUtils.ToPoint(ruinPos - ruinSize / 2), MathUtils.ToPoint(ruinSize));
                foreach (Ruin otherRuin in ruins)
                {
                    if (!otherRuin.Area.Intersects(ruinArea)) continue;

                    Vector2 diff = (ruinArea.Center - otherRuin.Area.Center).ToVector2();
                    if (diff.LengthSquared() < 0.01f) { diff = -Vector2.UnitY; }
                    weighedPathPos += Vector2.Normalize(diff) *
                        (Math.Max(ruinArea.Width, ruinArea.Height) + Math.Max(otherRuin.Area.Width, otherRuin.Area.Height)) / 2.0f;
                }
                
                ruinPos = weighedPathPos;
                if (ruinPos.Y + ruinSize.Y / 2.0f > level.Size.Y)
                {
                    ruinPos.Y -= ((ruinPos.Y + ruinSize.Y / 2.0f) - level.Size.Y);
                }

                if (iter > 10000) break;
            }

            VoronoiCell closestPathCell = null;
            float closestDist = 0.0f;
            foreach (VoronoiCell pathCell in mainPath)
            {
                float dist = Vector2.Distance(pathCell.Center, ruinPos);
                if (closestPathCell == null || dist < closestDist)
                {
                    closestPathCell = pathCell;
                    closestDist = dist;
                }
            }
            
            var ruin = new Ruin(closestPathCell, cells, new Rectangle(MathUtils.ToPoint(ruinPos - ruinSize * 0.5f), MathUtils.ToPoint(ruinSize)), mirror);
            ruins.Add(ruin);
            
            ruin.RuinShapes.Sort((shape1, shape2) => shape2.DistanceFromEntrance.CompareTo(shape1.DistanceFromEntrance));
            for (int i = 0; i < 4; i++)
            {
                positionsOfInterest.Add(new InterestingPosition(ruin.RuinShapes[i].Rect.Center.ToVector2(), PositionType.Ruin));
            }

            foreach (RuinShape ruinShape in ruin.RuinShapes)
            {
                var tooClose = GetTooCloseCells(ruinShape.Rect.Center.ToVector2(), Math.Max(ruinShape.Rect.Width, ruinShape.Rect.Height));

                foreach (VoronoiCell cell in tooClose)
                {
                    if (cell.CellType == CellType.Empty) continue;
                    foreach (GraphEdge e in cell.edges)
                    {
                        Rectangle rect = ruinShape.Rect;
                        rect.Y += rect.Height;
                        if (ruinShape.Rect.Contains(e.point1) || ruinShape.Rect.Contains(e.point2) ||
                            MathUtils.GetLineRectangleIntersection(e.point1, e.point2, rect) != null)
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
            if (!positionsOfInterest.Any()) return Size*0.5f;

            Vector2 position = Vector2.Zero;

            offsetFromWall = ConvertUnits.ToSimUnits(offsetFromWall);

            int tries = 0;
            do
            {
                Vector2 startPos;
                Loaded.TryGetInterestingPosition(true, spawnPosType, minDistFromSubs, out startPos);

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
            if (!positionsOfInterest.Any())
            {
                position = Size * 0.5f;
                return false;
            }

            var matchingPositions = positionsOfInterest.FindAll(p => positionType.HasFlag(p.PositionType));

            if (minDistFromSubs > 0.0f)
            {
                foreach (Submarine sub in Submarine.Loaded)
                {
                    matchingPositions.RemoveAll(p => Vector2.DistanceSquared(p.Position, sub.WorldPosition) < minDistFromSubs * minDistFromSubs);
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
            backgroundSpriteManager.Update(deltaTime);

#if CLIENT
            backgroundCreatureManager.Update(deltaTime, cam);

            if (Hull.renderer != null)
            {
                Hull.renderer.ScrollWater((float)deltaTime);
            }

            renderer.Update(deltaTime);
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

        public List<VoronoiCell> GetCells(Vector2 pos, int searchDepth = 2)
        {
            int gridPosX = (int)Math.Floor(pos.X / GridCellSize);
            int gridPosY = (int)Math.Floor(pos.Y / GridCellSize);

            int startX = Math.Max(gridPosX - searchDepth, 0);
            int endX = Math.Min(gridPosX + searchDepth, cellGrid.GetLength(0) - 1);

            int startY = Math.Max(gridPosY - searchDepth, 0);
            int endY = Math.Min(gridPosY + searchDepth, cellGrid.GetLength(1) - 1);

            List<VoronoiCell> cells = new List<VoronoiCell>();

            for (int y = startY; y <= endY; y++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    foreach (VoronoiCell cell in cellGrid[x, y]) cells.Add(cell); 
                }                              
            }

            if (extraWalls != null)
            {
                Debug.Assert(extraWalls.Count() == 1, "Level.GetCells may need to be updated to support extra walls other than the ocean floor.");
                if (pos.Y - searchDepth * GridCellSize < SeaFloorTopPos)
                {
                    foreach (VoronoiCell cell in extraWalls[0].Cells)
                    {
                        if (Math.Abs(cell.Center.X - pos.X) < searchDepth * GridCellSize)
                        {
                            cells.Add(cell);
                        }
                    }
                }
            }

            
            return cells;
        }

        private void Unload()
        {
#if CLIENT
            if (renderer != null) 
            {
                renderer.Dispose();
                renderer = null;
            }
#endif

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

    }
      
}
