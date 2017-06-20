using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Voronoi2;
using Microsoft.Xna.Framework.Graphics;
using FarseerPhysics;
using FarseerPhysics.Common;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Factories;

namespace Barotrauma
{
    static class CaveGenerator
    {
        public static List<VoronoiCell> CarveCave(List<VoronoiCell> cells, Vector2 startPoint, out List<VoronoiCell> newCells)
        {
            Voronoi voronoi = new Voronoi(1.0);

            List<Vector2> sites = new List<Vector2>();

            float siteInterval = 400.0f;
            float siteVariance = siteInterval * 0.4f;

            Vector4 edges = new Vector4(
                cells.Min(x => x.edges.Min(e => e.point1.X)),
                cells.Min(x => x.edges.Min(e => e.point1.Y)),
                cells.Max(x => x.edges.Max(e => e.point1.X)),
                cells.Max(x => x.edges.Max(e => e.point1.Y)));

            edges.X -= siteInterval * 2;
            edges.Y -= siteInterval * 2;
            edges.Z += siteInterval * 2;
            edges.W += siteInterval * 2;

            Rectangle borders = new Rectangle((int)edges.X, (int)edges.Y, (int)(edges.Z - edges.X), (int)(edges.W - edges.Y));

            for (float x = edges.X + siteInterval; x < edges.Z - siteInterval; x += siteInterval)
            {
                for (float y = edges.Y + siteInterval; y < edges.W - siteInterval; y += siteInterval)
                {
                    if (Rand.Int(5, false) == 0) continue; //skip some positions to make the cells more irregular

                    sites.Add(new Vector2(x, y) + Rand.Vector(siteVariance, false));
                }
            }

            List<GraphEdge> graphEdges = voronoi.MakeVoronoiGraph(sites, edges.X, edges.Y, edges.Z, edges.W);

            List<VoronoiCell>[,] cellGrid;
            newCells = GraphEdgesToCells(graphEdges, borders, 1000, out cellGrid);

            foreach (VoronoiCell cell in newCells)
            {
                //if the cell is at the edge of the graph, remove it
                if (cell.edges.Any(e => 
                    e.point1.X == edges.X || e.point1.X == edges.Z ||
                    e.point1.Y == edges.Z || e.point1.Y == edges.W))
                {
                    cell.CellType = CellType.Removed;
                    continue;
                }
                
                //remove cells that aren't inside any of the original "base cells"
                if (cells.Any(c => c.IsPointInside(cell.Center))) continue;
                foreach (GraphEdge edge in cell.edges)
                {
                    //mark all the cells adjacent to the removed cell as edges of the cave
                    var adjacent = edge.AdjacentCell(cell);
                    if (adjacent != null && adjacent.CellType != CellType.Removed) adjacent.CellType = CellType.Edge;
                }

                cell.CellType = CellType.Removed;
            }

            newCells.RemoveAll(newCell => newCell.CellType == CellType.Removed);

            //start carving from the edge cell closest to the startPoint
            VoronoiCell startCell = null;
            float closestDist = 0.0f;
            foreach (VoronoiCell cell in newCells)
            {
                if (cell.CellType != CellType.Edge) continue;

                float dist = Vector2.Distance(startPoint, cell.Center);
                if (dist < closestDist || startCell == null)
                {
                    startCell = cell;
                    closestDist = dist;
                }
            }

            startCell.CellType = CellType.Path;

            List<VoronoiCell> path = new List<VoronoiCell>() {startCell};
            VoronoiCell pathCell = startCell;
            for (int i = 0; i < newCells.Count / 2; i++)
            {
                var allowedNextCells = new List<VoronoiCell>();
                foreach (GraphEdge edge in pathCell.edges)
                {
                    var adjacent = edge.AdjacentCell(pathCell);
                    if (adjacent == null ||
                        adjacent.CellType == CellType.Removed ||
                        adjacent.CellType == CellType.Edge) continue;

                    allowedNextCells.Add(adjacent);
                }

                if (allowedNextCells.Count == 0)
                {
                    if (i>5) break;
                    
                    foreach (GraphEdge edge in pathCell.edges)
                    {
                        var adjacent = edge.AdjacentCell(pathCell);
                        if (adjacent == null ||
                            adjacent.CellType == CellType.Removed) continue;

                        allowedNextCells.Add(adjacent);
                    }

                    if (allowedNextCells.Count == 0) break;
                }
                
                //randomly pick one of the adjacent cells as the next cell
                pathCell = allowedNextCells[Rand.Int(allowedNextCells.Count, false)];

                //randomly take steps further away from the startpoint to make the cave expand further
                if (Rand.Int(4, false) == 0)
                {
                    float furthestDist = 0.0f;
                    foreach (VoronoiCell nextCell in allowedNextCells)
                    {
                        float dist = Vector2.Distance(startCell.Center, nextCell.Center);
                        if (dist > furthestDist || furthestDist == 0.0f)
                        {
                            furthestDist = dist;
                            pathCell = nextCell;
                        }
                    }
                }

                pathCell.CellType = CellType.Path;
                path.Add(pathCell);
            }

            //make sure the tunnel is always wider than minPathWidth
            float minPathWidth = 100.0f;
            for (int i = 0; i < path.Count; i++)
            {
                var cell = path[i];
                foreach (GraphEdge edge in cell.edges)
                {
                    if (edge.point1 == edge.point2) continue;
                    if (Vector2.Distance(edge.point1, edge.point2) > minPathWidth) continue;
                    
                    GraphEdge adjacentEdge = cell.edges.Find(e => e != edge && (e.point1 == edge.point1 || e.point2 == edge.point1));

                    var adjacentCell = adjacentEdge.AdjacentCell(cell);
                    if (i>0 && (adjacentCell.CellType == CellType.Path || adjacentCell.CellType == CellType.Edge)) continue;

                    adjacentCell.CellType = CellType.Path;
                    path.Add(adjacentCell);
                }
            }

            return path;
        }

        public static List<VoronoiCell> GraphEdgesToCells(List<GraphEdge> graphEdges, Rectangle borders, float gridCellSize, out List<VoronoiCell>[,] cellGrid)
        {
            List<VoronoiCell> cells = new List<VoronoiCell>();

            cellGrid = new List<VoronoiCell>[(int)Math.Ceiling(borders.Width / gridCellSize), (int)Math.Ceiling(borders.Height / gridCellSize)];
            for (int x = 0; x < borders.Width / gridCellSize; x++)
            {
                for (int y = 0; y < borders.Height / gridCellSize; y++)
                {
                    cellGrid[x, y] = new List<VoronoiCell>();
                }
            }

            foreach (GraphEdge ge in graphEdges)
            {
                if (ge.point1 == ge.point2) continue;

                for (int i = 0; i < 2; i++)
                {
                    Site site = (i == 0) ? ge.site1 : ge.site2;

                    int x = (int)(Math.Floor((site.coord.x-borders.X) / gridCellSize));
                    int y = (int)(Math.Floor((site.coord.y-borders.Y) / gridCellSize));

                    x = MathHelper.Clamp(x, 0, cellGrid.GetLength(0)-1);
                    y = MathHelper.Clamp(y, 0, cellGrid.GetLength(1)-1);
                        
                    VoronoiCell cell = cellGrid[x,y].Find(c => c.site == site);

                    if (cell == null)
                    {
                        cell = new VoronoiCell(site);
                        cellGrid[x, y].Add(cell);
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

            return cells;
        }


        private static Vector2 GetEdgeNormal(GraphEdge edge, VoronoiCell cell = null)
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

        public static List<VoronoiCell> GeneratePath(
            List<Vector2> pathNodes, List<VoronoiCell> cells, List<VoronoiCell>[,] cellGrid,
            int gridCellSize, Rectangle limits, float wanderAmount = 0.3f, bool mirror = false, Vector2? gridOffset = null)
        {
            var targetCells = new List<VoronoiCell>();
            for (int i = 0; i < pathNodes.Count; i++)
            {
                //a search depth of 2 is large enough to find a cell in almost all maps, but in case it fails, we increase the depth
                int searchDepth = 2;
                while (searchDepth < 5)
                {
                    int cellIndex = FindCellIndex(pathNodes[i], cells, cellGrid, gridCellSize, searchDepth, gridOffset);
                    if (cellIndex > -1)
                    {
                        targetCells.Add(cells[cellIndex]);
                        break;
                    }

                    searchDepth++;
                }

            }

            return GeneratePath(targetCells, cells, cellGrid, gridCellSize, limits, wanderAmount, mirror);
        }


        public static List<VoronoiCell> GeneratePath(
            List<VoronoiCell> targetCells, List<VoronoiCell> cells, List<VoronoiCell>[,] cellGrid, 
            int gridCellSize, Rectangle limits, float wanderAmount = 0.3f, bool mirror = false)
        {
            Stopwatch sw2 = new Stopwatch();
            sw2.Start();

            //how heavily the path "steers" towards the endpoint
            //lower values will cause the path to "wander" more, higher will make it head straight to the end
            wanderAmount = MathHelper.Clamp(wanderAmount, 0.0f, 1.0f);

            List<GraphEdge> allowedEdges = new List<GraphEdge>();
            List<VoronoiCell> pathCells = new List<VoronoiCell>();
            
            VoronoiCell currentCell = targetCells[0];
            currentCell.CellType = CellType.Path;
            pathCells.Add(currentCell);

            int currentTargetIndex = 1;

            int iterationsLeft = cells.Count;

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
                currentCell.CellType = CellType.Path;
                pathCells.Add(currentCell);

                iterationsLeft--;

                if (currentCell == targetCells[currentTargetIndex])
                {
                    currentTargetIndex += 1;
                    if (currentTargetIndex >= targetCells.Count) break;
                }

            } while (currentCell != targetCells[targetCells.Count - 1] && iterationsLeft > 0);


            Debug.WriteLine("gettooclose: " + sw2.ElapsedMilliseconds + " ms");
            sw2.Restart();

            return pathCells;
        }

        public static List<Body> GeneratePolygons(List<VoronoiCell> cells, out List<VertexPositionTexture> verticeList, bool setSolid=true)
        {
            verticeList = new List<VertexPositionTexture>();
            var bodies = new List<Body>();

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
                    //if (adjacentCell!=null && cells.Contains(adjacentCell)) continue;

                    if (setSolid) ge.isSolid = (adjacentCell == null || !cells.Contains(adjacentCell));

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
                        //shift the coordinates around a bit to make the texture repetition less obvious
                        Vector2 uvCoords = new Vector2(
                            vertex.X / 2000.0f + (float)Math.Sin(vertex.X / 500.0f) * 0.15f,
                            vertex.Y / 2000.0f + (float)Math.Sin(vertex.Y / 700.0f) * 0.15f);

                        verticeList.Add(new VertexPositionTexture(new Vector3(vertex, 1.0f), uvCoords));
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


                if (cell.CellType == CellType.Empty) continue;

                triangles = MathUtils.TriangulateConvexHull(bodyPoints, ConvertUnits.ToSimUnits(cell.Center));

                Body cellBody = new Body(GameMain.World);

                for (int i = 0; i < triangles.Count; i++)
                {
                    //don't create a triangle if any of the vertices are too close to each other
                    //(apparently Farseer doesn't like polygons with a very small area, see Shape.ComputeProperties)
                    if (Vector2.Distance(triangles[i][0], triangles[i][1]) < 0.05f ||
                        Vector2.Distance(triangles[i][0], triangles[i][2]) < 0.05f ||
                        Vector2.Distance(triangles[i][1], triangles[i][2]) < 0.05f) continue;
                    
                    Vertices bodyVertices = new Vertices(triangles[i]);
                    FixtureFactory.AttachPolygon(bodyVertices, 5.0f, cellBody);
                }
                
                cellBody.UserData = cell;
                cellBody.SleepingAllowed = false;
                cellBody.BodyType = BodyType.Kinematic;
                cellBody.CollisionCategories = Physics.CollisionLevel;

                cell.body = cellBody;
                bodies.Add(cellBody);
            }

            return bodies;
        }

        public static VertexPositionTexture[] GenerateWallShapes(List<VoronoiCell> cells)
        {
            float inwardThickness = 500.0f, outWardThickness = 30.0f;

            List<VertexPositionTexture> verticeList = new List<VertexPositionTexture>();

            foreach (VoronoiCell cell in cells)
            {
                //if (cell.body == null) continue;
                foreach (GraphEdge edge in cell.edges)
                {
                    if (edge.cell1 != null && edge.cell1.body == null && edge.cell1.CellType != CellType.Empty) edge.cell1 = null;
                    if (edge.cell2 != null && edge.cell2.body == null && edge.cell2.CellType != CellType.Empty) edge.cell2 = null;

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
                //if (cell.body == null) continue;
                foreach (GraphEdge edge in cell.edges)
                {
                    if (!edge.isSolid) continue;

                    GraphEdge leftEdge  = cell.edges.Find(e => e != edge && (edge.point1 == e.point1 || edge.point1 == e.point2));
                    GraphEdge rightEdge = cell.edges.Find(e => e != edge && (edge.point2 == e.point1 || edge.point2 == e.point2));
                    
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


                    if (!MathUtils.IsValid(leftNormal))
                    {
#if DEBUG
                        DebugConsole.ThrowError("Invalid left normal");
#endif
                        if (cell.body != null)
                        {
                            GameMain.World.RemoveBody(cell.body);
                            cell.body = null;
                        }
                        leftNormal = Vector2.UnitX;
                        break;
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

                    if (!MathUtils.IsValid(rightNormal))
                    {
#if DEBUG
                        DebugConsole.ThrowError("Invalid right normal");
#endif
                        if (cell.body != null)
                        {
                            GameMain.World.RemoveBody(cell.body);
                            cell.body = null;
                        }
                        rightNormal = Vector2.UnitX;
                        break;
                    }

                    for (int i = 0; i < 2; i++)
                    {
                        Vector2[] verts = new Vector2[3];
                        VertexPositionTexture[] vertPos = new VertexPositionTexture[3];


                        if (i == 0)
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

                        for (int j = 0; j < 3; j++)
                        {
                            verticeList.Add(vertPos[j]);
                        }
                    }
                }
            }

            return verticeList.ToArray();
        }

        /// <summary>
        /// find the index of the cell which the point is inside
        /// (actually finds the cell whose center is closest, but it's always the correct cell assuming the point is inside the borders of the diagram)
        /// </summary>
        public static int FindCellIndex(Vector2 position,List<VoronoiCell> cells, List<VoronoiCell>[,] cellGrid, int gridCellSize, int searchDepth = 1, Vector2? offset = null)
        {
            float closestDist = 0.0f;
            VoronoiCell closestCell = null;

            Vector2 gridOffset = offset == null ? Vector2.Zero : (Vector2)offset;
            position -= gridOffset;

            int gridPosX = (int)Math.Floor(position.X / gridCellSize);
            int gridPosY = (int)Math.Floor(position.Y / gridCellSize);

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


    }
}
