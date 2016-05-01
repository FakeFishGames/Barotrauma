using FarseerPhysics;
using FarseerPhysics.Common;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Factories;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using Voronoi2;
using Barotrauma.RuinGeneration;

namespace Barotrauma
{

    class Level
    {
        public static Level Loaded
        {
            get { return loaded; }
        }

        [Flags]
        public enum PositionType
        {
            MainPath=1, Cave=2, Ruin=4
        }
        
        struct InterestingPosition
        {
            public readonly Vector2 Position;
            public readonly PositionType PositionType;

            public InterestingPosition(Vector2 position, PositionType positionType)
            {
                Position = position;
                PositionType = positionType;
            }
        }

        static Level loaded;

        private LevelRenderer renderer;

        //how close the sub has to be to start/endposition to exit
        public const float ExitDistance = 6000.0f;

        private string seed;

        private int siteInterval;

        public const int GridCellSize = 2000;
        private List<VoronoiCell>[,] cellGrid;

        private WrappingWall[,] wrappingWalls;

        private float shaftHeight;

        //List<Body> bodies;
        private List<VoronoiCell> cells;

        //private VertexBuffer vertexBuffer;

        private Vector2 startPosition, endPosition;

        private Rectangle borders;

        private List<Body> bodies;

        private List<InterestingPosition> positionsOfInterest;

        private List<Ruin> ruins;

        private Color backgroundColor;

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

        public List<Ruin> Ruins
        {
            get { return ruins; }
        }
        
        public WrappingWall[,] WrappingWalls
        {
            get { return wrappingWalls; }
        }

        public string Seed
        {
            get { return seed; }
        }

        public float Difficulty
        {
            get;
            private set;
        }

        public Body[] ShaftBodies
        {
            get;
            private set;
        }

        public Color BackgroundColor
        {
            get { return backgroundColor; }
        }

        public Level(string seed, float difficulty, int width, int height, int siteInterval)
        {
            this.seed = seed;

            this.siteInterval = siteInterval;

            this.Difficulty = difficulty;

            borders = new Rectangle(0, 0, width, height);
        }

        public static Level CreateRandom(LocationConnection locationConnection)
        {
            string seed = locationConnection.Locations[0].Name + locationConnection.Locations[1].Name;

            Rand.SetSyncedSeed(ToolBox.StringToInt(seed));

            return new Level(seed, locationConnection.Difficulty, Rand.Range(80000, 120000, false), Rand.Range(40000, 60000, false), 2000);
        }

        public static Level CreateRandom(string seed = "")
        {
            if (seed == "")
            {
                seed = Rand.Range(0, int.MaxValue, false).ToString();
            }

            Rand.SetSyncedSeed(ToolBox.StringToInt(seed));

            return new Level(seed, Rand.Range(30.0f, 80.0f, false), Rand.Range(80000, 120000, false), Rand.Range(40000, 60000, false), 2000);
        }

        public void Generate(bool mirror=false)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            if (loaded != null) loaded.Unload();            
            loaded = this;

            positionsOfInterest = new List<InterestingPosition>();

            renderer = new LevelRenderer(this);

            Voronoi voronoi = new Voronoi(1.0);

            List<Vector2> sites = new List<Vector2>();

            bodies = new List<Body>();

            Rand.SetSyncedSeed(ToolBox.StringToInt(seed));

            float brightness = Rand.Range(1.0f, 1.3f, false);
            backgroundColor = Color.Lerp(new Color(11, 18, 26), new Color(11, 26, 18), Rand.Range(0.0f, 1.0f, false)) * brightness;


            backgroundColor = new Color(backgroundColor, 1.0f);

            float minWidth = Submarine.Loaded == null ? 0.0f : Math.Max(Submarine.Borders.Width, Submarine.Borders.Height);
            minWidth = Math.Max(minWidth, 6500.0f);

            startPosition = new Vector2(minWidth * 2, Rand.Range(minWidth * 2, borders.Height - minWidth * 2, false));
            endPosition = new Vector2(borders.Width - minWidth * 2, Rand.Range(minWidth * 2, borders.Height - minWidth * 2, false));
            
            List<Vector2> pathNodes = new List<Vector2>();
            Rectangle pathBorders = borders;// new Rectangle((int)minWidth, (int)minWidth, borders.Width - (int)minWidth * 2, borders.Height - (int)minWidth);   
            pathBorders.Inflate(-minWidth*2, -minWidth*2);


            pathNodes.Add(new Vector2(startPosition.X, borders.Height));
            pathNodes.Add(startPosition);


            for (float x = startPosition.X; x < endPosition.X; x += Rand.Range(5000.0f, 10000.0f, false))
            {
                pathNodes.Add(new Vector2(x, Rand.Range(pathBorders.Y, pathBorders.Bottom, false)));
            }

            for (int i = 2; i < pathNodes.Count; i+=3 )
            {
                positionsOfInterest.Add(new InterestingPosition(pathNodes[i], PositionType.MainPath));
            }

            pathNodes.Add(endPosition);
            pathNodes.Add(new Vector2(endPosition.X, borders.Height));

            int smallTunnelCount = 5;

            List<List<Vector2>> smallTunnels = new List<List<Vector2>>();

            for (int i = 0; i < smallTunnelCount; i++)
            {
                var tunnelStartPos = pathNodes[Rand.Range(2, pathNodes.Count - 2, false)];
                tunnelStartPos.X = MathHelper.Clamp(tunnelStartPos.X, pathBorders.X, pathBorders.Right);

                float tunnelLength = Rand.Range(5000.0f, 10000.0f, false);

                var tunnelNodes = MathUtils.GenerateJaggedLine(
                    tunnelStartPos, 
                    new Vector2(tunnelStartPos.X, pathBorders.Bottom)+Rand.Vector(tunnelLength,false), 
                    4, 1000.0f);

                List<Vector2> tunnel = new List<Vector2>();
                foreach (Vector2[] tunnelNode in tunnelNodes)
                {
                    if (!pathBorders.Contains(tunnelNode[0])) continue;
                    tunnel.Add(tunnelNode[0]);
                }

                if (tunnel.Any()) smallTunnels.Add(tunnel);
            }



            float siteVariance = siteInterval * 0.4f;
            for (int x = siteInterval / 2; x < borders.Width; x += siteInterval)
            {
                for (int y = siteInterval / 2; y < borders.Height; y += siteInterval)
                {
                    Vector2 site = new Vector2(x, y) + Rand.Vector(siteVariance, false);

                    if (smallTunnels.Any(t => t.Any(node => Vector2.Distance(node, site) < siteInterval)))
                    {
                        if (x < borders.Width - siteInterval) sites.Add(new Vector2(x, y) + Vector2.UnitX * siteInterval * 0.5f);
                        if (y < borders.Height - siteInterval) sites.Add(new Vector2(x, y) + Vector2.UnitY * siteInterval * 0.5f);
                        if (x < borders.Width - siteInterval && y < borders.Height - siteInterval) sites.Add(new Vector2(x, y) + Vector2.One * siteInterval * 0.5f);
                    }

                    if (mirror) site.X = borders.Width - site.X;

                    sites.Add(site);
                }
            }

            Stopwatch sw2 = new Stopwatch();
            sw2.Start();

            List<GraphEdge> graphEdges = voronoi.MakeVoronoiGraph(sites, borders.Width, borders.Height);


            Debug.WriteLine("MakeVoronoiGraph: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();
            
            //construct voronoi cells based on the graph edges
            cells = CaveGenerator.GraphEdgesToCells(graphEdges, borders, GridCellSize, out cellGrid);
            
            Debug.WriteLine("find cells: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();
            
            List<VoronoiCell> mainPath = CaveGenerator.GeneratePath(pathNodes, cells, cellGrid, GridCellSize,                
                new Rectangle(pathBorders.X, pathBorders.Y, pathBorders.Width, borders.Height), 0.3f, mirror);

            List<VoronoiCell> pathCells = new List<VoronoiCell>(mainPath);

            EnlargeMainPath(pathCells, minWidth);

            foreach (InterestingPosition positionOfInterest in positionsOfInterest)
            {
                WayPoint wayPoint = new WayPoint(positionOfInterest.Position, SpawnType.Enemy, null);
                wayPoint.MoveWithLevel = true;
            }

            startPosition.X = pathCells[0].Center.X;

            foreach (List<Vector2> tunnel in smallTunnels)
            {
                if (tunnel.Count<2) continue;

                //find the cell which the path starts from
                int startCellIndex = CaveGenerator.FindCellIndex(tunnel[0], cells, cellGrid, GridCellSize, 1);
                if (startCellIndex < 0) continue;

                //if it wasn't one of the cells in the main path, don't create a tunnel
                if (cells[startCellIndex].CellType != CellType.Path) continue;

                var newPathCells = CaveGenerator.GeneratePath(tunnel, cells, cellGrid, GridCellSize, pathBorders);

                positionsOfInterest.Add(new InterestingPosition(tunnel.Last(), PositionType.Cave));

                if (tunnel.Count() > 4) positionsOfInterest.Add(new InterestingPosition(tunnel[tunnel.Count() / 2], PositionType.Cave));
                
                pathCells.AddRange(newPathCells);
            }

            Debug.WriteLine("path: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();
            
            cells = CleanCells(pathCells);
            
            pathCells.AddRange(CreateBottomHoles(Rand.Range(0.1f,0.8f, false), new Rectangle(
                (int)(borders.Width * 0.2f), 0,
                (int)(borders.Width * 0.6f), (int)(borders.Height * 0.3f))));

            foreach (VoronoiCell cell in pathCells)
            {
                cell.CellType = CellType.Path;
                cells.Remove(cell);
            }
            
            //generate some narrow caves
            int caveAmount = 0;// Rand.Int(3, false);
            List<VoronoiCell> usedCaveCells = new List<VoronoiCell>();
            for (int i = 0; i < caveAmount; i++)
            {
                Vector2 startPoint = Vector2.Zero;
                VoronoiCell startCell = null;

                var caveCells = new List<VoronoiCell>();

                int maxTries = 5, tries = 0;              
                while (tries<maxTries)
                {
                    startCell = cells[Rand.Int(cells.Count, false)];

                    //find an edge between the cell and the already carved path
                    GraphEdge startEdge =
                        startCell.edges.Find(e => pathCells.Contains(e.AdjacentCell(startCell)));
                                        
                    if (startEdge != null)
                    {
                        startPoint = (startEdge.point1 + startEdge.point2) / 2.0f;
                        startPoint += startPoint - startCell.Center;

                        //get the cells in which the cave will be carved
                        caveCells = GetCells(startCell.Center, 2);
                        //remove cells that have already been "carved" out
                        caveCells.RemoveAll(c => c.CellType == CellType.Path);

                        //if any of the cells have already been used as a cave, continue and find some other cells
                        if (usedCaveCells.Any(c => caveCells.Contains(c))) continue;
                        break;
                    }

                    tries++;
                }

                //couldn't find a place for a cave -> abort
                if (tries >= maxTries) break;

                if (!caveCells.Any()) continue;

                usedCaveCells.AddRange(caveCells);

                List<VoronoiCell> caveSolidCells;
                var cavePathCells = CaveGenerator.CarveCave(caveCells, startPoint, out caveSolidCells);

                //remove the large cells used as a "base" for the cave (they've now been replaced with smaller ones)
                caveCells.ForEach(c => cells.Remove(c));
                
                cells.AddRange(caveSolidCells);

                foreach (VoronoiCell cell in cavePathCells)
                {
                    cells.Remove(cell);
                }

                pathCells.AddRange(cavePathCells);

                for (int j = cavePathCells.Count / 2; j < cavePathCells.Count; j += 10)
                {
                    positionsOfInterest.Add(new InterestingPosition(cavePathCells[j].Center, PositionType.Cave));
                }
            }

            for (int x = 0; x < cellGrid.GetLength(0); x++)
            {
                for (int y = 0; y < cellGrid.GetLength(1); y++)
                {
                    cellGrid[x, y] .Clear();
                }
            }

            foreach (VoronoiCell cell in cells)
            {
                int x = (int)Math.Floor(cell.Center.X / GridCellSize);
                int y = (int)Math.Floor(cell.Center.Y / GridCellSize);

                if (x < 0 || y < 0 || x >= cellGrid.GetLength(0) || y >= cellGrid.GetLength(1)) continue;

                cellGrid[x,y].Add(cell);
            }









            Vector2 ruinSize = new Vector2(Rand.Range(5000.0f, 8000.0f, false), Rand.Range(5000.0f, 8000.0f, false));
            float ruinRadius = Math.Max(ruinSize.X, ruinSize.Y) * 0.5f;

            Vector2 ruinPos = cells[Rand.Int(cells.Count, false)].Center;

            int iter = 0;

            while (mainPath.Any(p => Vector2.Distance(ruinPos, p.Center) < ruinRadius*2.0f))
            {
                Vector2 weighedPathPos = ruinPos;
                iter++;

                foreach (VoronoiCell pathCell in mainPath)
                {
                    float dist = Vector2.Distance(pathCell.Center, ruinPos);
                    if (dist > 10000.0f) continue;

                    Vector2 moveAmount = Vector2.Normalize(ruinPos - pathCell.Center) * 100000.0f / dist;

                    //if (weighedPathPos.Y + moveAmount.Y > borders.Bottom - ruinSize.X)
                    //{
                    //    moveAmount.X = (Math.Abs(moveAmount.Y) + Math.Abs(moveAmount.X))*Math.Sign(moveAmount.X);
                    //    moveAmount.Y = 0.0f;
                    //}

                    weighedPathPos += moveAmount;
                }

                ruinPos = weighedPathPos;

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

            var ruin = new Ruin(closestPathCell, cells, new Rectangle((ruinPos - ruinSize * 0.5f).ToPoint(), ruinSize.ToPoint()));

            ruins = new List<Ruin>();
            ruins.Add(ruin);

            ruin.RuinShapes.Sort((shape1, shape2) => shape2.DistanceFromEntrance.CompareTo(shape1.DistanceFromEntrance));
            for (int i = 0; i < 4; i++ )
            {
                positionsOfInterest.Add(new InterestingPosition(ruin.RuinShapes[i].Rect.Center.ToVector2(), PositionType.Ruin));
            }
            
            startPosition.Y = borders.Height;
            endPosition.Y = borders.Height;

            List<VoronoiCell> cellsWithBody = new List<VoronoiCell>(cells);
            foreach (RuinShape ruinShape in ruin.RuinShapes)
            {
                var tooClose = GetTooCloseCells(ruinShape.Rect.Center.ToVector2(), Math.Max(ruinShape.Rect.Width, ruinShape.Rect.Height));

                tooClose.ForEach(c => 
                { 
                    if (c.edges.Any(e => ruinShape.Rect.Contains(e.point1) || ruinShape.Rect.Contains(e.point2))) c.CellType = CellType.Empty; 
                });
            }

            List<VertexPositionColor> bodyVertices;
            bodies = CaveGenerator.GeneratePolygons(cellsWithBody, out bodyVertices);

            renderer.SetBodyVertices(bodyVertices.ToArray());
            renderer.SetWallVertices(CaveGenerator.GenerateWallShapes(cells));

            renderer.PlaceSprites(1000);
            
            wrappingWalls = new WrappingWall[2, 2];

            for (int side = 0; side < 2; side++)
            {
                for (int i = 0; i < 2; i++)
                {
                    wrappingWalls[side, i] = new WrappingWall(pathCells, cells, borders.Height * 0.5f,
                        (side == 0 ? -1 : 1) * (i + 1));

                    List<VertexPositionColor> wrappingWallVertices;
                    CaveGenerator.GeneratePolygons(wrappingWalls[side, i].Cells, out wrappingWallVertices, false);

                    wrappingWalls[side, i].SetBodyVertices(wrappingWallVertices.ToArray());
                    wrappingWalls[side, i].SetWallVertices(CaveGenerator.GenerateWallShapes(wrappingWalls[side, i].Cells));
                }

            }
            for (int side = 0; side < 2; side++)
            {
                for (int i = 0; i < 2; i++)
                {
                    cells.AddRange(wrappingWalls[side, i].Cells);
                }
            }


            ShaftBodies = new Body[2];
            for (int i = 0; i < 2; i++)
            {
                ShaftBodies[i] = BodyFactory.CreateRectangle(GameMain.World, 200.0f, 10.0f, 5.0f);
                ShaftBodies[i].BodyType = BodyType.Static;
                ShaftBodies[i].CollisionCategories = Physics.CollisionLevel;

                Vector2 shaftPos = (i == 0) ? startPosition : endPosition;
                shaftPos.Y = borders.Height + 150.0f;

                ShaftBodies[i].SetTransform(ConvertUnits.ToSimUnits(shaftPos), 0.0f);
                bodies.Add(ShaftBodies[i]);
            }

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

            Debug.WriteLine("Generatelevel: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();

            //vertexBuffer = new VertexBuffer(GameMain.CurrGraphicsDevice, VertexPositionTexture.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
            //vertexBuffer.SetData(vertices);

            if (mirror)
            {
                Vector2 temp = startPosition;
                startPosition = endPosition;
                endPosition = temp;
            }

            

             //RuinGeneration.RuinGenerator.Draw(spriteBatch);


            Debug.WriteLine("**********************************************************************************");
            Debug.WriteLine("Generated a map with " + sites.Count + " sites in " + sw.ElapsedMilliseconds + " ms");
            Debug.WriteLine("Seed: "+seed);
            Debug.WriteLine("**********************************************************************************");
        }


        private List<VoronoiCell> CreateBottomHoles(float holeProbability, Rectangle limits)
        {
            List<VoronoiCell> toBeRemoved = new List<VoronoiCell>();
            foreach (VoronoiCell cell in cells)
            {
                if (Rand.Range(0.0f, 1.0f, false) > holeProbability) continue;

                if (!limits.Contains(cell.Center)) continue;

                toBeRemoved.Add(cell);
            }

            return toBeRemoved;

            //foreach (VoronoiCell cell in toBeRemoved)
            //{
            //    cells.Remove(cell);
            //}
        }

        private void EnlargeMainPath(List<VoronoiCell> pathCells, float minWidth)
        {
            List<WayPoint> wayPoints = new List<WayPoint>();

            var newWaypoint = new WayPoint(new Rectangle((int)pathCells[0].Center.X, (int)(borders.Height + shaftHeight), 10, 10), null);
            newWaypoint.MoveWithLevel = true;
            wayPoints.Add(newWaypoint);

            //WayPoint prevWaypoint = newWaypoint;

            for (int i = 0; i < pathCells.Count; i++)
            {
                ////clean "loops" from the path
                //for (int n = 0; n < i; n++)
                //{
                //    if (pathCells[n] != pathCells[i]) continue;

                //    pathCells.RemoveRange(n + 1, i - n);
                //    break;
                //}
                //if (i >= pathCells.Count) break;

                pathCells[i].CellType = CellType.Path;

                newWaypoint = new WayPoint(new Rectangle((int)pathCells[i].Center.X, (int)pathCells[i].Center.Y, 10, 10), null);
                newWaypoint.MoveWithLevel = true;
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
                
                //prevWaypoint = newWaypoint;
            }

            newWaypoint = new WayPoint(new Rectangle((int)pathCells[pathCells.Count - 1].Center.X, (int)(borders.Height + shaftHeight), 10, 10), null);
            newWaypoint.MoveWithLevel = true;
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

            //for (float x = -minDistance; x <= minDistance; x+=siteInterval)
            //{
            //    for (float y = -minDistance; y <= minDistance; y += siteInterval)
            //    {
            //        Vector2 cornerPos = position + new Vector2(x,y);

            //        int cellIndex = CaveGenerator.FindCellIndex(cornerPos, cells, cellGrid, GridCellSize);
            //        if (cellIndex == -1) continue;
            //        if (!tooCloseCells.Contains(cells[cellIndex]))
            //        {
            //            tooCloseCells.Add(cells[cellIndex]);
            //        }
            //    }
            //}

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
                    if (adjacent!=null && !newCells.Contains(adjacent)) newCells.Add(adjacent);
                }
            }

            return newCells;
        }

        public Vector2 GetRandomItemPos(PositionType spawnPosType, float offsetFromWall = 10.0f)
        {
            if (!positionsOfInterest.Any()) return Size*0.5f;

            Vector2 position = Vector2.Zero;

            offsetFromWall = ConvertUnits.ToSimUnits(offsetFromWall);

            int tries = 0;
            do
            {
                Vector2 startPos = Level.Loaded.GetRandomInterestingPosition(true, spawnPosType);
                
                Vector2 endPos = startPos - Vector2.UnitY * Size.Y;

                if (Submarine.PickBody(
                    ConvertUnits.ToSimUnits(startPos),
                    ConvertUnits.ToSimUnits(endPos),
                    null, Physics.CollisionLevel) != null)
                {
                    position = ConvertUnits.ToDisplayUnits(Submarine.LastPickedPosition) +  Vector2.Normalize(startPos - endPos)*offsetFromWall;
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

        public Vector2 GetRandomInterestingPosition(bool useSyncedRand, PositionType positionType)
        {
            if (!positionsOfInterest.Any()) return Size * 0.5f;

            var matchingPositions = positionsOfInterest.FindAll(p => positionType.HasFlag(p.PositionType));
            if (!matchingPositions.Any())
            {
                return positionsOfInterest[Rand.Int(positionsOfInterest.Count, !useSyncedRand)].Position;
            }

            return matchingPositions[Rand.Int(matchingPositions.Count, !useSyncedRand)].Position;
        }

        public void Update (float deltaTime)
        {
            if (Submarine.Loaded!=null)
            {
                WrappingWall.UpdateWallShift(Submarine.Loaded.WorldPosition, wrappingWalls);
            }

            renderer.Update(deltaTime);
        }

        public void DrawFront(SpriteBatch spriteBatch)
        {
            if (renderer == null) return;
            renderer.Draw(spriteBatch);

            if (GameMain.DebugDraw)
            {
                foreach (InterestingPosition pos in positionsOfInterest)
                {
                    Color color = Color.Yellow;
                    if (pos.PositionType == PositionType.Cave)
                    {
                        color = Color.DarkOrange;
                    }
                    else if (pos.PositionType == PositionType.Ruin)
                    {
                        color = Color.LightGray;
                    }
                   

                    GUI.DrawRectangle(spriteBatch, new Vector2(pos.Position.X-15.0f, -pos.Position.Y-15.0f), new Vector2(30.0f, 30.0f), color, true);
                }
            }
        }

        public void DrawBack(GraphicsDevice graphics, SpriteBatch spriteBatch, Camera cam, BackgroundCreatureManager backgroundSpriteManager = null)
        {
            graphics.Clear(backgroundColor);

            if (renderer == null) return;
            renderer.DrawBackground(spriteBatch, cam, backgroundSpriteManager);
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

            for (int x = startX; x < endX; x++)
            {
                for (int y = startY; y < endY; y++)
                {
                    foreach (VoronoiCell cell in cellGrid[x, y])
                    {
                        cells.Add(cell);
                    }
                }
            }

            if (wrappingWalls == null) return cells;

            for (int side = 0; side < 2; side++)
            {
                for (int n = 0; n < 2; n++)
                {
                    if (wrappingWalls[side, n] == null) continue;

                    if (Vector2.Distance(wrappingWalls[side, n].MidPos, pos) > WrappingWall.WallWidth) continue;

                    foreach (VoronoiCell cell in wrappingWalls[side, n].Cells)
                    {
                        cells.Add(cell);
                    }
                }
            }

            return cells;
        }

        private void Unload()
        {
            if (renderer!=null) renderer.Dispose();
            renderer = null;

            for (int side = 0; side < 2; side++)
            {
                for (int i = 0; i < 2; i++)
                {
                    wrappingWalls[side, i].Dispose();
                }
            }
            
            cells = null;
            
            bodies.Clear();
            bodies = null;

            loaded = null;
        }

    }
      
}
