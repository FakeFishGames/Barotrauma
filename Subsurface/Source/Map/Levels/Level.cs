using FarseerPhysics;
using FarseerPhysics.Common;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Factories;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Voronoi2;

namespace Subsurface
{
    class Level
    {
        public static Level Loaded
        {
            get { return loaded; }
        }

        static Level loaded;

        private static Texture2D shaftTexture;

        //how close the sub has to be to start/endposition to exit
        const float ExitDistance = 6000.0f;

        private string seed;

        private int siteInterval;

        public const int GridCellWidth = 2000;
        private List<VoronoiCell>[,] cellGrid;

        private float shaftHeight;

        //List<Body> bodies;
        private List<VoronoiCell> cells;

        private static BasicEffect basicEffect;

        private VertexPositionTexture[] vertices;
        private VertexBuffer vertexBuffer;

        private Vector2 startPosition;
        private Vector2 endPosition;

        private Rectangle borders;

        private List<Body> bodies;

        private List<Vector2> positionsOfInterest;

        public Vector2 StartPosition
        {
            get { return startPosition; }
        }

        public bool AtStartPosition
        {
            get;
            private set;
        }

        public Vector2 EndPosition
        {
            get { return endPosition; }
        }

        public bool AtEndPosition
        {
            get;
            private set;
        }

        public Vector2 Position
        {
            get { return ConvertUnits.ToDisplayUnits(cells[0].body.Position); }
        }

        public List<Vector2> PositionsOfInterest
        {
            get { return positionsOfInterest; }
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

        public Level(string seed, float difficulty, int width, int height, int siteInterval)
        {
            if (shaftTexture == null) shaftTexture = TextureLoader.FromFile("Content/Map/shaft.png");

            if (basicEffect==null)
            {
                
                basicEffect = new BasicEffect(GameMain.CurrGraphicsDevice);
                basicEffect.VertexColorEnabled = false;

                basicEffect.TextureEnabled = true;
                basicEffect.Texture = TextureLoader.FromFile("Content/Map/iceSurface.png");
            }

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

        public void Generate(float minWidth, bool mirror=false)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            if (loaded != null)
            {
                loaded.Unload();
            }

            loaded = this;

            Voronoi voronoi = new Voronoi(1.0);

            List<Vector2> sites = new List<Vector2>();

            bodies = new List<Body>();

            Random rand = new Random(ToolBox.StringToInt(seed));

            float siteVariance = siteInterval * 0.8f;
            for (int x = siteInterval / 2; x < borders.Width; x += siteInterval)
            {
                for (int y = siteInterval / 2; y < borders.Height; y += siteInterval)
                {
                    Vector2 site = new Vector2(
                        x + (float)(rand.NextDouble() - 0.5) * siteVariance,
                        y + (float)(rand.NextDouble() - 0.5) * siteVariance);

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

            //generate a path from the left edge of the map to right edge
            Rectangle pathBorders = new Rectangle(
                borders.X + (int)minWidth * 2, borders.Y + (int)minWidth * 2,
                borders.Right - (int)minWidth * 4, borders.Y + borders.Height - (int)minWidth * 4);

            List<Vector2> pathNodes = new List<Vector2>();

            startPosition = new Vector2((int)minWidth * 2, rand.Next((int)minWidth * 2, borders.Height - (int)minWidth * 2));
            endPosition = new Vector2(borders.Width - (int)minWidth * 2, rand.Next((int)minWidth * 2, borders.Height - (int)minWidth * 2));

            pathNodes.Add(new Vector2(startPosition.X, borders.Height));
            pathNodes.Add(startPosition);            
            pathNodes.Add(endPosition);
            pathNodes.Add(new Vector2(endPosition.X, borders.Height));

            if (mirror)
            {
                pathNodes.Reverse();
            }

            List<VoronoiCell> pathCells = GeneratePath(rand,
                pathNodes, cells, pathBorders, minWidth, 0.3f, mirror, true);

            //place some enemy spawnpoints at random points in the path
            for (int i = 0; i <3 ; i++ )
            {
                Vector2 position = pathCells[rand.Next((int)(pathCells.Count * 0.5f), pathCells.Count - 2)].Center;
                WayPoint wayPoint = new WayPoint(new Rectangle((int)position.X, (int)position.Y, 10, 10));
                wayPoint.MoveWithLevel = true;
                wayPoint.SpawnType = SpawnType.Enemy;
            }

            startPosition = pathCells[0].Center;
            endPosition = pathCells[pathCells.Count - 1].Center;

            //generate a couple of random paths
            for (int i = 0; i <= rand.Next() % 3; i++)
            {
                //pathBorders = new Rectangle(
                //borders.X + siteInterval * 2, borders.Y - siteInterval * 2,
                //borders.Right - siteInterval * 2, borders.Y + borders.Height - siteInterval * 2);

                Vector2 start = pathCells[rand.Next(1, pathCells.Count - 2)].Center;

                float x = pathBorders.X + (float)rand.NextDouble() * (pathBorders.Right - pathBorders.X);
                float y = pathBorders.Y + (float)rand.NextDouble() * (pathBorders.Bottom - pathBorders.Y);

                if (mirror) x = borders.Width - x;

                Vector2 end = new Vector2(x, y);

                var newPathCells = GeneratePath(rand, new List<Vector2> { start, end }, cells, pathBorders, 0.0f, 0.8f, mirror);

                for (int n = 0; n < newPathCells.Count-5; n += 3)
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
            //for (int i = 0; i < 2; i++)
            //{
            //    Vector2 tunnelStart = (i == 0) ? startPosition : endPosition;

            //    for (int n = -1; n < 2; n += 2)
            //    {
            //        int cellIndex = FindCellIndex(new Vector2(tunnelStart.X + minWidth * 0.5f * n, tunnelStart.Y), 3);

            //        foreach (GraphEdge ge in cells[cellIndex].edges)
            //        {
            //            if (ge.point1.Y > cells[cellIndex].Center.Y) ge.point1.Y = borders.Height + shaftHeight;
            //            if (ge.point2.Y > cells[cellIndex].Center.Y) ge.point2.Y = borders.Height + shaftHeight;
            //        }
            //    }
            //}

            //startPosition.Y += shaftHeight;
            //endPosition.Y += shaftHeight;

            GeneratePolygons(cells, pathCells);

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

            vertexBuffer = new VertexBuffer(GameMain.CurrGraphicsDevice, VertexPositionTexture.VertexDeclaration, vertices.Length, BufferUsage.WriteOnly);
            vertexBuffer.SetData(vertices);

            if (mirror)
            {
                Vector2 temp = startPosition;
                startPosition = endPosition;
                endPosition = temp;
            }

            Debug.WriteLine("Generated a map with " + sites.Count + " sites in " + sw.ElapsedMilliseconds + " ms");
        }

        private List<VoronoiCell> GeneratePath(Random rand, List<Vector2> points, List<VoronoiCell> cells, Microsoft.Xna.Framework.Rectangle limits, float minWidth, float wanderAmount = 0.3f, bool mirror=false, bool placeWaypoints=false)
        {
            Stopwatch sw2 = new Stopwatch();
            sw2.Start();
            
            //how heavily the path "steers" towards the endpoint
            //lower values will cause the path to "wander" more, higher will make it head straight to the end
            wanderAmount = MathHelper.Clamp(wanderAmount, 0.0f, 1.0f);

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

                //steer towards target
                if (rand.NextDouble() > wanderAmount)
                {
                    for (int i = 0; i < currentCell.edges.Count; i++)
                    {
                        if (!MathUtils.LinesIntersect(currentCell.Center, targetCells[currentTargetIndex].Center, currentCell.edges[i].point1, currentCell.edges[i].point2)) continue;
                        edgeIndex = i;
                        break;
                    }
                }
                //choose random edge (ignoring ones where the adjacent cell is outside limits)
                else
                {
                    List<GraphEdge> allowedEdges = new List<GraphEdge>();

                    foreach (GraphEdge edge in currentCell.edges)
                    {
                        if (!limits.Contains(edge.AdjacentCell(currentCell).Center)) continue;

                        allowedEdges.Add(edge);
                    }
                    if (allowedEdges.Count==0)
                    {
                        edgeIndex = rand.Next() % currentCell.edges.Count;
                    }
                    else
                    {
                        edgeIndex = rand.Next() % allowedEdges.Count;
                        if (mirror && edgeIndex > 0) edgeIndex = allowedEdges.Count - edgeIndex;
                        edgeIndex = currentCell.edges.IndexOf(allowedEdges[edgeIndex]);
                    }
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
                WayPoint newWaypoint = new WayPoint(new Rectangle((int)pathCells[0].Center.X, (int)(borders.Height + shaftHeight), 10, 10));
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

                    newWaypoint = new WayPoint(new Rectangle((int)pathCells[i].Center.X, (int)pathCells[i].Center.Y, 10, 10));
                    newWaypoint.MoveWithLevel = true;
                    if (prevWaypoint != null)
                    {
                        prevWaypoint.linkedTo.Add(newWaypoint);
                        newWaypoint.linkedTo.Add(prevWaypoint);
                    }
                    prevWaypoint = newWaypoint;
                }

                newWaypoint = new WayPoint(new Rectangle((int)pathCells[pathCells.Count - 1].Center.X, (int)(borders.Height + shaftHeight), 10, 10));
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

        private void GeneratePolygons(List<VoronoiCell> cells, List<VoronoiCell> emptyCells)
        {
            List<VertexPositionTexture> verticeList = new List<VertexPositionTexture>();
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
                    if (!emptyCells.Contains(adjacentCell)) continue;

                    ge.isSolid = true;

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
                        verticeList.Add(new VertexPositionTexture(new Vector3(vertex, 0.0f), vertex/1000.0f));
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

                    Vertices bodyVertices = new Vertices(triangles[i]);
                    FixtureFactory.AttachPolygon(bodyVertices, 5.0f, edgeBody);
                }

                edgeBody.UserData = cell;
                edgeBody.SleepingAllowed = false;
                edgeBody.BodyType = BodyType.Kinematic;
                edgeBody.CollisionCategories = Physics.CollisionWall | Physics.CollisionLevel;

                cell.body = edgeBody;
                bodies.Add(edgeBody);
            }

            for (int i = 0; i < 2; i++ )
            {
                Body shaftBody = BodyFactory.CreateRectangle(GameMain.World, 100.0f, 10.0f, 5.0f);
                shaftBody.BodyType = BodyType.Kinematic;
                shaftBody.CollisionCategories = Physics.CollisionWall | Physics.CollisionLevel;
                shaftBody.SetTransform(ConvertUnits.ToSimUnits((i==0) ? startPosition : endPosition), 0.0f);
                shaftBody.SleepingAllowed = false;
                bodies.Add(shaftBody);
            }

                vertices = verticeList.ToArray();
        }

        public void SetPosition(Vector2 pos)
        {
            Vector2 amount = pos - Position;
            Vector2 simAmount = ConvertUnits.ToSimUnits(amount);
            //foreach (VoronoiCell cell in cells)
            //{
            //    if (cell.body == null) continue;
            //    cell.body.SleepingAllowed = false;
            //    cell.body.SetTransform(cell.body.Position + simAmount, cell.body.Rotation);
            //}

            int i = 0;
            foreach (Body body in bodies)
            {
                i++;
                body.SetTransform(body.Position + simAmount, body.Rotation);
            }

            foreach (MapEntity mapEntity in MapEntity.mapEntityList)
            {
                Item item = mapEntity as Item;
                if (item == null)
                {
                    //if (!mapEntity.MoveWithLevel) continue;
                    //mapEntity.Move(amount);
                }
                else if (item.body != null)
                {
                    if (item.CurrentHull != null) continue;
                    item.SetTransform(item.SimPosition+amount, item.body.Rotation);
                }
            }
        }

        Vector2 prevVelocity;
        public void Move(Vector2 amount)
        {
            Vector2 velocity = amount;
            Vector2 simVelocity = ConvertUnits.ToSimUnits(amount / (float)Physics.step);

            foreach (Body body in bodies)
            {
                body.LinearVelocity = simVelocity;
            }

            foreach (Character character in Character.CharacterList)
            {
                foreach (Limb limb in character.AnimController.Limbs)
                {
                    if (character.AnimController.CurrentHull != null) continue;
                    
                    limb.body.LinearVelocity += simVelocity;                    
                }
            }

            foreach (Item item in Item.itemList)
            {        
                if (item.body==null || item.CurrentHull != null) continue;
                item.body.LinearVelocity += simVelocity;                
            }

            AtStartPosition = Vector2.Distance(startPosition, -Position) < ExitDistance;
            AtEndPosition   = Vector2.Distance(endPosition, -Position) < ExitDistance;
            
            prevVelocity = simVelocity;
        }

        public static void AfterWorldStep()
        {
            if (loaded == null) return;

            loaded.ResetBodyVelocities();
        }

        private void ResetBodyVelocities()
        {
            if (prevVelocity == Vector2.Zero) return;
            if (!MathUtils.IsValid(prevVelocity))
            {
                prevVelocity = Vector2.Zero;
                return;
            }

            foreach (Character character in Character.CharacterList)
            {
                if (character.AnimController.CurrentHull != null) continue;

                foreach (Limb limb in character.AnimController.Limbs)
                {
                    limb.body.LinearVelocity -= prevVelocity;
                }
            }

            foreach (Item item in Item.itemList)
            {
                if (item.body == null || item.CurrentHull != null) continue;
                item.body.LinearVelocity -= prevVelocity;
            }
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

            System.Diagnostics.Debug.WriteLine("pos: " + Position);
        }

        Vector2 observerPosition;
        public void SetObserverPosition(Vector2 position)
        {
            //observerPosition = position - this.Position;
            //int gridPosX = (int)Math.Floor(observerPosition.X / GridCellWidth);
            //int gridPosY = (int)Math.Floor(observerPosition.Y / GridCellWidth);
            //int searchOffset = 2;

            //int startX = Math.Max(gridPosX - searchOffset, 0);
            //int endX = Math.Min(gridPosX + searchOffset, cellGrid.GetLength(0) - 1);

            //int startY = Math.Max(gridPosY - searchOffset, 0);
            //int endY = Math.Min(gridPosY + searchOffset, cellGrid.GetLength(1) - 1);

            //for (int x = 0; x < cellGrid.GetLength(0); x++)
            //{
            //    for (int y = 0; y < cellGrid.GetLength(1); y++)
            //    {
            //        for (int i = 0; i < cellGrid[x, y].Count; i++)
            //        {
            //            //foreach (Body b in cellGrid[x, y][i].bodies)
            //            //{
            //            if (cellGrid[x, y][i].body == null) continue;
            //            cellGrid[x, y][i].body.Enabled = true;// (x >= startX && x <= endX && y >= startY && y <= endY);
            //            //}
            //        }
            //    }
            //}
        }


        public void Draw(SpriteBatch spriteBatch)
        {
            Vector2 pos = endPosition;            
            pos.Y = -pos.Y - Position.Y;

            if (GameMain.GameScreen.Cam.WorldView.Y < -pos.Y-512) return;

            pos.X = GameMain.GameScreen.Cam.WorldView.X-512.0f;
            //pos.X += Position.X % 512;

            int width = (int)(Math.Ceiling(GameMain.GameScreen.Cam.WorldView.Width/512.0f + 2.0f)*512.0f);

            spriteBatch.Draw(shaftTexture,
                new Rectangle((int)(MathUtils.Round(pos.X, 512.0f) + Position.X % 512) , (int)pos.Y, width, 512),
                new Rectangle(0, 0, width, 256),
                Color.White, 0.0f,
                Vector2.Zero,
                SpriteEffects.None, 0.0f);

            //pos = startPosition;
            //pos.X += Position.X;
            //pos.Y = -pos.Y - Position.Y;

            //spriteBatch.Draw(shaftTexture,
            //    new Rectangle((int)(pos.X - shaftWidth/2), (int)pos.Y, shaftWidth, 512), 
            //    new Rectangle(0, 0, shaftWidth, 256),
            //    Color.White, 0.0f,
            //    Vector2.Zero,
            //    SpriteEffects.None, 0.0f);

            //List<Vector2[]> edges = GetCellEdges(observerPosition, 1, false);

            //foreach (VoronoiCell cell in cells)
            //{
            //    for (int i = 0; i < cell.bodyVertices.Count - 1; i++)
            //    {
            //        Vector2 start = cell.bodyVertices[i];
            //        start.X += Position.X;
            //        start.Y = -start.Y - Position.Y;
            //        start.X += Rand.Range(-10.0f, 10.0f);

            //        Vector2 end = cell.bodyVertices[i + 1];
            //        end.X += Position.X;
            //        end.Y = -end.Y - Position.Y;
            //        end.X += Rand.Range(-10.0f, 10.0f);

            //        GUI.DrawLine(spriteBatch, start, end, (cell.body != null && cell.body.Enabled) ? Color.Red : Color.Red);
            //    }
            //}
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
                        for (int i = 0; i < cell.edges.Count; i++)
                        {
                            cells.Add(cell);
                            //GUI.DrawLine(spriteBatch, start, end, (cell.body != null && cell.body.Enabled) ? Color.Green : Color.Red);
                        }
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
                            Vector2 start = cell.edges[i].point1 + Position;
                            start.Y = -start.Y;

                            Vector2 end = cell.edges[i].point2 + Position;
                            end.Y = -end.Y;

                            edges.Add(new Vector2[] { start, end });
                            //GUI.DrawLine(spriteBatch, start, end, (cell.body != null && cell.body.Enabled) ? Color.Green : Color.Red);
                        }
                    }
                }
            }

            return edges;
        }

        public void Render(GraphicsDevice graphicsDevice, Camera cam)
        {
            if (vertices == null) return;
            if (vertices.Length <= 0) return;

            basicEffect.World = Matrix.CreateTranslation(new Vector3(Position, 0.0f)) * cam.ShaderTransform
                * Matrix.CreateOrthographic(GameMain.GraphicsWidth, GameMain.GraphicsHeight, -1, 1) * 0.5f;
            
            basicEffect.CurrentTechnique.Passes[0].Apply();

            graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;

            graphicsDevice.DrawUserPrimitives<VertexPositionTexture>(PrimitiveType.TriangleList, vertices, 0, (int)Math.Floor(vertices.Length / 3.0f));
        }

        private void Unload()
        {
            //position = Vector2.Zero;

            //foreach (VoronoiCell cell in cells)
            //{
            //    //foreach (Body b in cell.bodies)
            //    //{
            //        Game1.world.RemoveBody(cell.body);
            //    //}
            //}


            //bodies = null;

            vertices = null;

            cells = null;

            bodies.Clear();
            bodies = null;

            vertexBuffer.Dispose();
            vertexBuffer = null;
        }

    }
      
}
