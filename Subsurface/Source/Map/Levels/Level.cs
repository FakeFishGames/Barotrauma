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

namespace Barotrauma
{
    class Level
    {
        public static Level Loaded
        {
            get { return loaded; }
        }

        static Level loaded;

        private LevelRenderer renderer;

        //how close the sub has to be to start/endposition to exit
        public const float ExitDistance = 6000.0f;

        private string seed;

        private int siteInterval;

        public const int GridCellWidth = 2000;
        private List<VoronoiCell>[,] cellGrid;

        private WrappingWall[,] wrappingWalls;

        private float shaftHeight;

        //List<Body> bodies;
        private List<VoronoiCell> cells;

        //private VertexBuffer vertexBuffer;

        private Vector2 startPosition, endPosition;

        private Rectangle borders;

        private List<Body> bodies;

        private List<Vector2> positionsOfInterest;

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
                
        public List<Vector2> PositionsOfInterest
        {
            get { return positionsOfInterest; }
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

        public Level(string seed, float difficulty, int width, int height, int siteInterval)
        {
            this.seed = seed;

            this.siteInterval = siteInterval;

            this.Difficulty = difficulty;

            positionsOfInterest = new List<Vector2>();

            borders = new Rectangle(0, 0, width, height);
        }

        public static Level CreateRandom(LocationConnection locationConnection)
        {
            string seed = locationConnection.Locations[0].Name + locationConnection.Locations[1].Name;
            return new Level(seed, locationConnection.Difficulty, 100000, 40000, 2000);
        }

        public static Level CreateRandom(string seed = "")
        {
            if (seed == "")
            {
                seed = Rand.Range(0, int.MaxValue, false).ToString();
            }
            return new Level(seed, Rand.Range(30.0f,80.0f,false), 100000, 40000, 2000);
        }

        public void Generate(bool mirror=false)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            if (loaded != null) loaded.Unload();
            
            loaded = this;

            renderer = new LevelRenderer(this);

            Voronoi voronoi = new Voronoi(1.0);

            List<Vector2> sites = new List<Vector2>();

            bodies = new List<Body>();

            Rand.SetSyncedSeed(ToolBox.StringToInt(seed));

            float siteVariance = siteInterval * 0.4f;
            for (int x = siteInterval / 2; x < borders.Width; x += siteInterval)
            {
                for (int y = siteInterval / 2; y < borders.Height; y += siteInterval)
                {
                    Vector2 site = new Vector2(x, y) + Rand.Vector(siteVariance, false);

                    if (mirror) site.X = borders.Width - site.X;

                    sites.Add(site);
                }
            }

            Stopwatch sw2 = new Stopwatch();
            sw2.Start();

            List<GraphEdge> graphEdges = voronoi.MakeVoronoiGraph(sites, borders.Width, borders.Height);


            Debug.WriteLine("MakeVoronoiGraph: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();

            cellGrid = new List<VoronoiCell>[borders.Width / GridCellWidth, borders.Height / GridCellWidth];
            for (int x = 0; x < borders.Width / GridCellWidth; x++)
            {
                for (int y = 0; y < borders.Height / GridCellWidth; y++)
                {
                    cellGrid[x, y] = new List<VoronoiCell>();
                }
            }

            //construct voronoi cells based on the graph edges
            cells = new List<VoronoiCell>();
            foreach (GraphEdge ge in graphEdges)
            {
                for (int i = 0; i < 2; i++)
                {
                    Site site = (i == 0) ? ge.site1 : ge.site2;

                    VoronoiCell cell = cellGrid[
                        (int)Math.Floor(site.coord.x / GridCellWidth),
                        (int)Math.Floor(site.coord.y / GridCellWidth)].Find(c => c.site == site);

                    if (cell == null)
                    {
                        cell = new VoronoiCell(site);
                        cellGrid[(int)Math.Floor(cell.Center.X / GridCellWidth), (int)Math.Floor(cell.Center.Y / GridCellWidth)].Add(cell);
                        cells.Add(cell);
                    }

                    if (ge.cell1 == null)
                    {
                        ge.cell1 = cell;
                    }
                    else
                    {
                        ge.cell2 = cell;
                    }
                    cell.edges.Add(ge);
                }
            }
            
            Debug.WriteLine("find cells: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();

            float minWidth = Submarine.Loaded == null ? 3000.0f : Math.Max(Submarine.Borders.Width, Submarine.Borders.Height);

            //generate a path from the left edge of the map to right edge
            Rectangle pathBorders = new Rectangle(
                borders.X + (int)minWidth * 2, borders.Y + (int)minWidth * 2,
                borders.Right - (int)minWidth * 4, borders.Y + borders.Height - (int)minWidth * 4);

            List<Vector2> pathNodes = new List<Vector2>();

            startPosition = new Vector2((int)minWidth * 2, Rand.Range((int)minWidth * 2, borders.Height - (int)minWidth * 2, false));
            endPosition = new Vector2(borders.Width - (int)minWidth * 2, Rand.Range((int)minWidth * 2, borders.Height - (int)minWidth * 2, false));

            pathNodes.Add(new Vector2(startPosition.X, borders.Height));
            pathNodes.Add(startPosition);            
            pathNodes.Add(endPosition);
            pathNodes.Add(new Vector2(endPosition.X, borders.Height));

            if (mirror)
            {
                pathNodes.Reverse();
            }

            List<VoronoiCell> pathCells = GeneratePath(pathNodes, cells, pathBorders, minWidth, 0.3f, mirror, true);

            //place some enemy spawnpoints at random points in the path
            for (int i = 0; i <3 ; i++ )
            {
                Vector2 position = pathCells[Rand.Range((int)(pathCells.Count * 0.5f), pathCells.Count - 2, false)].Center;
                WayPoint wayPoint = new WayPoint(new Rectangle((int)position.X, (int)position.Y, 10, 10), null);
                wayPoint.MoveWithLevel = true;
                wayPoint.SpawnType = SpawnType.Enemy;
            }

            startPosition = pathCells[0].Center;
            endPosition = pathCells[pathCells.Count - 1].Center;

            //generate a couple of random paths
            for (int i = 0; i <= Rand.Range(1,4,false); i++)
            {
                //pathBorders = new Rectangle(
                //borders.X + siteInterval * 2, borders.Y - siteInterval * 2,
                //borders.Right - siteInterval * 2, borders.Y + borders.Height - siteInterval * 2);

                Vector2 start = pathCells[Rand.Range(1, pathCells.Count - 2,false)].Center;

                float x = pathBorders.X + Rand.Range(0, pathBorders.Right - pathBorders.X, false);
                float y = pathBorders.Y + Rand.Range(0,pathBorders.Bottom - pathBorders.Y, false);

                if (mirror) x = borders.Width - x;

                Vector2 end = new Vector2(x, y);

                var newPathCells = GeneratePath(new List<Vector2> { start, end }, cells, pathBorders, 0.0f, 0.8f, mirror);

                for (int n = 0; n < newPathCells.Count; n += 5)
                {
                    positionsOfInterest.Add(newPathCells[n].Center);
                }                

                pathCells.AddRange(newPathCells);
            }

            Debug.WriteLine("path: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();
            
            //for (int i = 0; i < 2; i++ )
            //{
            //    Vector2 tunnelStart = (i == 0) ? startPosition : endPosition;

            //    pathCells.AddRange
            //    (
            //        GeneratePath(rand, tunnelStart, new Vector2(tunnelStart.X, borders.Height), cells, pathBorders, minWidth, 0.1f, mirror)
            //    );
            //}
            
            cells = CleanCells(pathCells);

            pathCells.AddRange(CreateBottomHoles(0.8f, new Rectangle(
                (int)(borders.Width * 0.2f), 0,
                (int)(borders.Width * 0.6f), (int)(borders.Height * 0.5f))));

            foreach (VoronoiCell cell in pathCells)
            {
                cells.Remove(cell);
            }

            for (int x = 0; x < cellGrid.GetLength(0); x++)
            {
                for (int y = 0; y < cellGrid.GetLength(1); y++)
                {
                    cellGrid[x, y].Clear();
                }
            }

            foreach (VoronoiCell cell in cells)
            {
                cellGrid[(int)Math.Floor(cell.Center.X / GridCellWidth), (int)Math.Floor(cell.Center.Y / GridCellWidth)].Add(cell);
            }

            startPosition.Y = borders.Height;
            endPosition.Y = borders.Height;

            renderer.SetBodyVertices(GeneratePolygons(cells, pathCells));
            renderer.SetWallVertices(GenerateWallShapes(cells));

            
            wrappingWalls = new WrappingWall[2, 2];

            for (int side = 0; side < 2; side++)
            {
                for (int i = 0; i < 2; i++)
                {
                    wrappingWalls[side, i] = new WrappingWall(pathCells, cells, borders.Height * 0.5f,
                        (side == 0 ? -1 : 1) * (i == 0 ? 1 : 2));

                    wrappingWalls[side, i].SetBodyVertices(GeneratePolygons(wrappingWalls[side, i].Cells, new List<VoronoiCell>(), false));
                    wrappingWalls[side, i].SetWallVertices(GenerateWallShapes(wrappingWalls[side, i].Cells));
                    //wrappingWalls[side, i].Cells[0].edges[1].isSolid = false;
                    //wrappingWalls[side, i].Cells[0].edges[3].isSolid = false;

                    //wrappingWalls[side, i].Cells[wrappingWalls[side, i].Cells.Count-1].edges[1].isSolid = false;
                    //wrappingWalls[side, i].Cells[wrappingWalls[side, i].Cells.Count - 1].edges[3].isSolid = false;
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
                ShaftBodies[i] = BodyFactory.CreateRectangle(GameMain.World, 100.0f, 10.0f, 5.0f);
                ShaftBodies[i].BodyType = BodyType.Static;
                ShaftBodies[i].CollisionCategories = Physics.CollisionLevel;
                ShaftBodies[i].SetTransform(ConvertUnits.ToSimUnits((i == 0) ? startPosition : endPosition), 0.0f);
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

            renderer.PlaceSprites(100);

            Debug.WriteLine("**********************************************************************************");
            Debug.WriteLine("Generated a map with " + sites.Count + " sites in " + sw.ElapsedMilliseconds + " ms");
            Debug.WriteLine("Seed: "+seed);
            Debug.WriteLine("**********************************************************************************");
        }

        private List<VoronoiCell> GeneratePath(List<Vector2> points, List<VoronoiCell> cells, Microsoft.Xna.Framework.Rectangle limits, float minWidth, float wanderAmount = 0.3f, bool mirror=false, bool placeWaypoints=false)
        {
            Stopwatch sw2 = new Stopwatch();
            sw2.Start();
            
            //how heavily the path "steers" towards the endpoint
            //lower values will cause the path to "wander" more, higher will make it head straight to the end
            wanderAmount = MathHelper.Clamp(wanderAmount, 0.0f, 1.0f);

            List<GraphEdge> allowedEdges = new List<GraphEdge>();
            List<VoronoiCell> pathCells = new List<VoronoiCell>();

            VoronoiCell[] targetCells = new VoronoiCell[points.Count];
            for (int i = 0; i <targetCells.Length; i++)
            {
                targetCells[i]= cells[FindCellIndex(points[i])];
            }

            VoronoiCell currentCell = targetCells[0];
            pathCells.Add(currentCell);

            int currentTargetIndex = 1;            
            
            do
            {
                int edgeIndex = 0;

                allowedEdges.Clear();
                foreach (GraphEdge edge in currentCell.edges)
                {
                    if (!limits.Contains(edge.AdjacentCell(currentCell).Center)) continue;

                    allowedEdges.Add(edge);
                }

                //steer towards target
                if (Rand.Range(0.0f, 1.0f, false) > wanderAmount || allowedEdges.Count == 0)
                {
                    for (int i = 0; i < currentCell.edges.Count; i++)
                    {
                        if (!MathUtils.LinesIntersect(currentCell.Center, targetCells[currentTargetIndex].Center, 
                            currentCell.edges[i].point1, currentCell.edges[i].point2)) continue;
                        edgeIndex = i;
                        break;
                    }
                }
                //choose random edge (ignoring ones where the adjacent cell is outside limits)
                else
                {


                    //if (allowedEdges.Count==0)
                    //{
                    //    edgeIndex = Rand.Int(currentCell.edges.Count, false);
                    //}
                    //else
                    //{
                        edgeIndex = Rand.Int(allowedEdges.Count, false);
                        if (mirror && edgeIndex > 0) edgeIndex = allowedEdges.Count - edgeIndex;
                        edgeIndex = currentCell.edges.IndexOf(allowedEdges[edgeIndex]);
                    //}
                }

                currentCell = currentCell.edges[edgeIndex].AdjacentCell(currentCell);
                pathCells.Add(currentCell);

                if (currentCell==targetCells[currentTargetIndex])
                {
                    currentTargetIndex += 1;
                    if (currentTargetIndex>=targetCells.Length) break;
                }

            } while (currentCell != targetCells[targetCells.Length-1]);

            if (placeWaypoints)
            {
                WayPoint newWaypoint = new WayPoint(new Rectangle((int)pathCells[0].Center.X, (int)(borders.Height + shaftHeight), 10, 10), null);
                newWaypoint.MoveWithLevel = true;

                WayPoint prevWaypoint = newWaypoint;

                for (int i = 0; i < pathCells.Count; i++)
                {
                    //clean "loops" from the path
                    for (int n = 0; n < i; n++)
                    {
                        if (pathCells[n] != pathCells[i]) continue;

                        pathCells.RemoveRange(n+1, i-n);                        
                        break;
                    }
                    if (i >= pathCells.Count) break;

                    newWaypoint = new WayPoint(new Rectangle((int)pathCells[i].Center.X, (int)pathCells[i].Center.Y, 10, 10), null);
                    newWaypoint.MoveWithLevel = true;
                    if (prevWaypoint != null)
                    {
                        prevWaypoint.linkedTo.Add(newWaypoint);
                        newWaypoint.linkedTo.Add(prevWaypoint);
                    }
                    prevWaypoint = newWaypoint;
                }

                newWaypoint = new WayPoint(new Rectangle((int)pathCells[pathCells.Count - 1].Center.X, (int)(borders.Height + shaftHeight), 10, 10), null);
                newWaypoint.MoveWithLevel = true;

                prevWaypoint.linkedTo.Add(newWaypoint);
                newWaypoint.linkedTo.Add(prevWaypoint);
                
            }

            Debug.WriteLine("genpath: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();

            List<VoronoiCell> removedCells = GetTooCloseCells(pathCells, minWidth);
            foreach (VoronoiCell removedCell in removedCells)
            {
                if (pathCells.Contains(removedCell)) continue;
                pathCells.Add(removedCell);
            }

            Debug.WriteLine("gettooclose: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();

            return pathCells;
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
                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        if (x == 0 && y == 0) continue;
                        Vector2 cornerPos = position + new Vector2(x * minDistance, y * minDistance);

                        int cellIndex = FindCellIndex(cornerPos);
                        if (cellIndex == -1) continue;
                        if (!tooCloseCells.Contains(cells[cellIndex]))
                        {
                            tooCloseCells.Add(cells[cellIndex]);
                        }
                    }
                }

                position += Vector2.Normalize(emptyCells[targetCellIndex].Center - position) * step;

                if (Vector2.Distance(emptyCells[targetCellIndex].Center, position) < step * 2.0f) targetCellIndex++;

            } while (Vector2.Distance(position, emptyCells[emptyCells.Count - 1].Center) > step * 2.0f);

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
                    if (!newCells.Contains(adjacent)) newCells.Add(adjacent);
                }
            }

            return newCells;
        }

        /// <summary>
        /// find the index of the cell which the point is inside
        /// (actually finds the cell whose center is closest, but it's always the correct cell assuming the point is inside the borders of the diagram)
        /// </summary>
        private int FindCellIndex(Vector2 position, int searchDepth = 1)
        {
            float closestDist = 0.0f;
            VoronoiCell closestCell = null;

            int gridPosX = (int)Math.Floor(position.X / GridCellWidth);
            int gridPosY = (int)Math.Floor(position.Y / GridCellWidth);

            for (int x = Math.Max(gridPosX - searchDepth, 0); x <= Math.Min(gridPosX + searchDepth, cellGrid.GetLength(0) - 1); x++)
            {
                for (int y = Math.Max(gridPosY - searchDepth, 0); y <= Math.Min(gridPosY + searchDepth, cellGrid.GetLength(1) - 1); y++)
                {
                    for (int i = 0; i < cellGrid[x, y].Count; i++)
                    {
                        float dist = Vector2.Distance(cellGrid[x, y][i].Center, position);
                        if (closestDist != 0.0f && dist > closestDist) continue;

                        closestDist = dist;
                        closestCell = cellGrid[x, y][i];
                    }
                }
            }
            
            return cells.IndexOf(closestCell);
        }


        private VertexPositionColor[] GeneratePolygons(List<VoronoiCell> cells, List<VoronoiCell> emptyCells, bool setSolid=true)
        {
            List<VertexPositionColor> verticeList = new List<VertexPositionColor>();
            //bodies = new List<Body>();

            List<Vector2> tempVertices = new List<Vector2>();
            List<Vector2> bodyPoints = new List<Vector2>();

            for (int n = cells.Count - 1; n >= 0; n-- )
            {
                VoronoiCell cell = cells[n];
                
                bodyPoints.Clear();
                tempVertices.Clear();
                foreach (GraphEdge ge in cell.edges)
                {
                    if (Math.Abs(Vector2.Distance(ge.point1, ge.point2))<0.1f) continue;
                    if (!tempVertices.Contains(ge.point1)) tempVertices.Add(ge.point1);
                    if (!tempVertices.Contains(ge.point2)) tempVertices.Add(ge.point2);

                    VoronoiCell adjacentCell = ge.AdjacentCell(cell);
                    if (adjacentCell!=null && cells.Contains(adjacentCell)) continue;

                    if (setSolid) ge.isSolid = true;

                    if (!bodyPoints.Contains(ge.point1)) bodyPoints.Add(ge.point1);
                    if (!bodyPoints.Contains(ge.point2)) bodyPoints.Add(ge.point2);
                }

                if (tempVertices.Count < 3 || bodyPoints.Count < 2)
                {
                    cells.RemoveAt(n);
                    continue;
                }

                var triangles = MathUtils.TriangulateConvexHull(tempVertices, cell.Center);
                for (int i = 0; i < triangles.Count; i++)
                {
                    foreach (Vector2 vertex in triangles[i])
                    {
                        verticeList.Add(new VertexPositionColor(new Vector3(vertex, 0.0f), Color.Black));
                    }
                }

                if (bodyPoints.Count < 2) continue;

                if (bodyPoints.Count < 3)
                {
                    foreach (Vector2 vertex in tempVertices)
                    {
                        if (bodyPoints.Contains(vertex)) continue;
                        bodyPoints.Add(vertex);
                        break;
                    }
                }

                for (int i = 0; i < bodyPoints.Count; i++)
                {
                    cell.bodyVertices.Add(bodyPoints[i]);
                    bodyPoints[i] = ConvertUnits.ToSimUnits(bodyPoints[i]);
                }

                triangles = MathUtils.TriangulateConvexHull(bodyPoints, cell.Center);

                Body edgeBody = new Body(GameMain.World);

                for (int i = 0; i < triangles.Count; i++)
                {
                    if (triangles[i][0].Y == triangles[i][1].Y && triangles[i][0].Y == triangles[i][2].Y) continue;
                    if (triangles[i][0].X == triangles[i][1].X && triangles[i][0].X == triangles[i][2].X) continue;

                    if (Vector2.DistanceSquared(triangles[i][0], triangles[i][1]) < 0.1f) continue;
                    if (Vector2.DistanceSquared(triangles[i][1], triangles[i][2]) < 0.1f) continue;

                    Vertices bodyVertices = new Vertices(triangles[i]);
                    FixtureFactory.AttachPolygon(bodyVertices, 5.0f, edgeBody);
                }

                edgeBody.UserData = cell;
                edgeBody.SleepingAllowed = false;
                edgeBody.BodyType = BodyType.Kinematic;
                edgeBody.CollisionCategories = Physics.CollisionLevel;

                cell.body = edgeBody;
                bodies.Add(edgeBody);
            }

            return verticeList.ToArray();
        }

        private VertexPositionTexture[] GenerateWallShapes(List<VoronoiCell> cells)
        {
            float inwardThickness = 500.0f, outWardThickness = 30.0f;

            List<VertexPositionTexture> verticeList = new List<VertexPositionTexture>();

            foreach (VoronoiCell cell in cells)
            {
                if (cell.body == null) continue;
                foreach (GraphEdge edge in cell.edges)
                {
                    if (edge.cell1 != null && edge.cell1.body == null) edge.cell1 = null;
                    if (edge.cell2 != null && edge.cell2.body == null) edge.cell2 = null;

                    CompareCCW compare = new CompareCCW(cell.Center);
                    if (compare.Compare(edge.point1, edge.point2) == -1)
                    {
                        var temp = edge.point1;
                        edge.point1 = edge.point2;
                        edge.point2 = temp;
                    }
                }
            }

            foreach (VoronoiCell cell in cells)
            {
                if (cell.body == null) continue;
                foreach (GraphEdge edge in cell.edges)
                {
                    if (!edge.isSolid) continue;

                    GraphEdge leftEdge = null, rightEdge = null;

                    foreach (GraphEdge edge2 in cell.edges)
                    {
                        if (edge == edge2) continue;
                        if (edge.point1 == edge2.point1 || 
                            edge.point1 == edge2.point2)
                        {
                            leftEdge = edge2;
                        }
                        else if(edge.point2 == edge2.point2 ||  edge.point2 == edge2.point1)
                        {
                            rightEdge = edge2;
                        }
                    }
                    
                    Vector2 leftNormal = Vector2.Zero, rightNormal = Vector2.Zero;

                    if (leftEdge == null)
                    {
                        leftNormal = GetEdgeNormal(edge, cell);
                    }
                    else
                    {
                        leftNormal = (leftEdge.isSolid) ? 
                            Vector2.Normalize(GetEdgeNormal(leftEdge) + GetEdgeNormal(edge, cell)) : 
                            Vector2.Normalize(leftEdge.Center - edge.point1);
                    }


                    if (rightEdge == null)
                    {
                        rightNormal = GetEdgeNormal(edge, cell);
                    }
                    else
                    {
                        rightNormal = (rightEdge.isSolid) ?
                            Vector2.Normalize(GetEdgeNormal(rightEdge) + GetEdgeNormal(edge, cell)) :
                            Vector2.Normalize(rightEdge.Center - edge.point2);
                    }




                    for (int i = 0; i < 2; i++)
                    {
                        Vector2[] verts = new Vector2[3];
                        VertexPositionTexture[] vertPos = new VertexPositionTexture[3];

                        
                        if (i==0)
                        {
                            verts[0] = edge.point1 - leftNormal * outWardThickness;
                            verts[1] = edge.point2 - rightNormal * outWardThickness;
                            verts[2] = edge.point1 + leftNormal * inwardThickness;

                            vertPos[0] = new VertexPositionTexture(new Vector3(verts[0], 0.0f), Vector2.Zero);
                            vertPos[1] = new VertexPositionTexture(new Vector3(verts[1], 0.0f), Vector2.UnitX);
                            vertPos[2] = new VertexPositionTexture(new Vector3(verts[2], 0.0f), new Vector2(0, 0.5f));
                        }
                        else
                        {
                            verts[0] = edge.point1 + leftNormal * inwardThickness;
                            verts[1] = edge.point2 - rightNormal * outWardThickness;
                            verts[2] = edge.point2 + rightNormal * inwardThickness;

                            vertPos[0] = new VertexPositionTexture(new Vector3(verts[0], 0.0f), new Vector2(0.0f, 0.5f));
                            vertPos[1] = new VertexPositionTexture(new Vector3(verts[1], 0.0f), Vector2.UnitX);
                            vertPos[2] = new VertexPositionTexture(new Vector3(verts[2], 0.0f), new Vector2(1.0f, 0.5f));
                        }
                        
                        var comparer = new CompareCCW((verts[0] + verts[1] + verts[2]) / 3.0f);
                        Array.Sort(verts, vertPos, comparer);

                        for (int j = 0; j<3; j++)
                        {
                            verticeList.Add(vertPos[j]);
                        }
                    }
                }
            }

            return verticeList.ToArray();
        }

        private Vector2 GetEdgeNormal(GraphEdge edge, VoronoiCell cell = null)
        {
            if (cell == null) cell = edge.AdjacentCell(null);
            if (cell == null) return Vector2.UnitX;

            CompareCCW compare = new CompareCCW(cell.Center);
            if (compare.Compare(edge.point1, edge.point2) == -1)
            {
                var temp = edge.point1;
                edge.point1 = edge.point2;
                edge.point2 = temp;
            }

            Vector2 normal = Vector2.Zero;

            normal = Vector2.Normalize(edge.point2 - edge.point1);
            Vector2 diffToCell = Vector2.Normalize(cell.Center - edge.point2);

            normal = new Vector2(-normal.Y, normal.X);

            if (Vector2.Dot(normal, diffToCell) < 0)
            {
                normal = -normal;
            }
            
            return normal;            
        }

        public Vector2 GetRandomItemPos(float offsetFromWall = 10.0f)
        {
            Vector2 position = Vector2.Zero;

            offsetFromWall = ConvertUnits.ToSimUnits(offsetFromWall);

            int tries = 0;
            do
            {
                Vector2 startPos = ConvertUnits.ToSimUnits(PositionsOfInterest[Rand.Int(PositionsOfInterest.Count, false)]);

                Vector2 endPos = startPos - ConvertUnits.ToSimUnits(Vector2.UnitY * Size.Y);

                if (Submarine.PickBody(
                    startPos,
                    endPos,
                    null, Physics.CollisionLevel) != null)
                {
                    position = ConvertUnits.ToDisplayUnits(Submarine.LastPickedPosition +  Vector2.Normalize(startPos - endPos)*offsetFromWall);
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
        }

        public void DrawBack(SpriteBatch spriteBatch, Camera cam, BackgroundCreatureManager backgroundSpriteManager = null)
        {
            if (renderer == null) return;
            renderer.DrawBackground(spriteBatch, cam, backgroundSpriteManager);
        }


        public void DebugCheckPos()
        {

            Vector2 avgPos = Vector2.Zero;
            foreach (VoronoiCell cell in cells)
            {
                if (cell.body == null) continue;


                System.Diagnostics.Debug.WriteLine(cell.body.Position);
                avgPos += cell.body.Position;
            }

            System.Diagnostics.Debug.WriteLine("avgpos: " + avgPos / cells.Count);

            //System.Diagnostics.Debug.WriteLine("pos: " + Position);
        }
        
        public List<VoronoiCell> GetCells(Vector2 pos, int searchDepth = 2)
        {
            int gridPosX = (int)Math.Floor(pos.X / GridCellWidth);
            int gridPosY = (int)Math.Floor(pos.Y / GridCellWidth);

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

            for (int side = 0; side < 2; side++)
            {
                for (int n = 0; n < 2; n++)
                {
                    if (Vector2.Distance(wrappingWalls[side, n].MidPos, pos) > WrappingWall.WallWidth) continue;

                    foreach (VoronoiCell cell in wrappingWalls[side, n].Cells)
                    {
                        cells.Add(cell);
                    }
                }
            }

            return cells;
        }

        public List<Vector2[]> GetCellEdges(Vector2 refPos, int searchDepth = 2, bool onlySolid = true)
        {
            int gridPosX = (int)Math.Floor(refPos.X / GridCellWidth);
            int gridPosY = (int)Math.Floor(refPos.Y / GridCellWidth);

            int startX = Math.Max(gridPosX - searchDepth, 0);
            int endX = Math.Min(gridPosX + searchDepth, cellGrid.GetLength(0) - 1);

            int startY = Math.Max(gridPosY - searchDepth, 0);
            int endY = Math.Min(gridPosY + searchDepth, cellGrid.GetLength(1) - 1);

            List<Vector2[]> edges = new List<Vector2[]>();

            for (int x = startX; x < endX; x++)
            {
                for (int y = startY; y < endY; y++)
                {
                    foreach (VoronoiCell cell in cellGrid[x, y])
                    {
                        for (int i = 0; i < cell.edges.Count; i++)
                        {
                            if (onlySolid && !cell.edges[i].isSolid) continue;

                            Vector2 start = cell.edges[i].point1;
                            start.Y = -start.Y;

                            Vector2 end = cell.edges[i].point2;
                            end.Y = -end.Y;
                            
                            edges.Add(new Vector2[] { start, end });
                            //GUI.DrawLine(spriteBatch, start, end, (cell.body != null && cell.body.Enabled) ? Color.Green : Color.Red);
                        }
                    }
                }
            }

            for (int side = 0; side < 2; side++ )
            {
                for (int n = 0 ; n<2; n++)
                {
                    if (Vector2.Distance(wrappingWalls[side, n].MidPos, refPos) > WrappingWall.WallWidth) continue;

                    foreach (VoronoiCell cell in wrappingWalls[side, n].Cells)
                    {
                        Vector2 offset = wrappingWalls[side, n].Offset;
                        for (int i = 0; i < cell.edges.Count; i++)
                        {
                            if (onlySolid && !cell.edges[i].isSolid) continue;
                            Vector2 start = cell.edges[i].point1 + offset;
                            start.Y = -start.Y;

                            Vector2 end = cell.edges[i].point2 + offset;
                            end.Y = -end.Y;

                            edges.Add(new Vector2[] { start, end });
                        }
                    }
                }
            }

                return edges;
        }

        private void Unload()
        {
            renderer.Dispose();
            renderer = null;
            
            cells = null;
            
            bodies.Clear();
            bodies = null;

            loaded = null;

            //vertexBuffer.Dispose();
            //vertexBuffer = null;
        }

    }
      
}
