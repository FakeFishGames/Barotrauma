using FarseerPhysics;
using FarseerPhysics.Collision.Shapes;
using FarseerPhysics.Common;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Voronoi2;

namespace Barotrauma
{
    static partial class CaveGenerator
    {
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
                if (Vector2.DistanceSquared(ge.Point1, ge.Point2) < 0.001f) { continue; }

                for (int i = 0; i < 2; i++)
                {
                    Site site = (i == 0) ? ge.Site1 : ge.Site2;

                    int x = (int)(Math.Floor((site.Coord.X - borders.X) / gridCellSize));
                    int y = (int)(Math.Floor((site.Coord.Y - borders.Y) / gridCellSize));

                    x = MathHelper.Clamp(x, 0, cellGrid.GetLength(0) - 1);
                    y = MathHelper.Clamp(y, 0, cellGrid.GetLength(1) - 1);

                    VoronoiCell cell = cellGrid[x, y].Find(c => c.Site == site);

                    if (cell == null)
                    {
                        cell = new VoronoiCell(site);
                        cellGrid[x, y].Add(cell);
                        cells.Add(cell);
                    }

                    if (ge.Cell1 == null)
                    {
                        ge.Cell1 = cell;
                    }
                    else
                    {
                        ge.Cell2 = cell;
                    }
                    cell.Edges.Add(ge);
                }
            }

            //add edges to the borders of the graph
            foreach (var cell in cells)
            {
                Vector2? point1 = null, point2 = null;
                foreach (GraphEdge ge in cell.Edges)
                {
                    if (MathUtils.NearlyEqual(ge.Point1.X, borders.X) || MathUtils.NearlyEqual(ge.Point1.X, borders.Right) ||
                        MathUtils.NearlyEqual(ge.Point1.Y, borders.Y) || MathUtils.NearlyEqual(ge.Point1.Y, borders.Bottom))
                    {
                        if (point1 == null)
                        {
                            point1 = ge.Point1;
                        }
                        else if (point2 == null)
                        {
                            if (MathUtils.NearlyEqual(point1.Value, ge.Point1)) { continue; }
                            point2 = ge.Point1;
                        }
                    }
                    if (MathUtils.NearlyEqual(ge.Point2.X, borders.X) || MathUtils.NearlyEqual(ge.Point2.X, borders.Right) ||
                        MathUtils.NearlyEqual(ge.Point2.Y, borders.Y) || MathUtils.NearlyEqual(ge.Point2.Y, borders.Bottom))
                    {
                        if (point1 == null)
                        {
                            point1 = ge.Point2;
                        }
                        else
                        {
                            if (MathUtils.NearlyEqual(point1.Value, ge.Point2)) { continue; }
                            point2 = ge.Point2;
                        }
                    }
                    if (point1.HasValue && point2.HasValue)
                    {
                        Debug.Assert(point1 != point2);
                        bool point1OnSide = MathUtils.NearlyEqual(point1.Value.X, borders.X) || MathUtils.NearlyEqual(point1.Value.X, borders.Right);
                        bool point2OnSide = MathUtils.NearlyEqual(point2.Value.X, borders.X) || MathUtils.NearlyEqual(point2.Value.X, borders.Right);
                        //one point is one the side, another on top/bottom
                        // -> the cell is in the corner of the level, we need 2 edges
                        if (point1OnSide != point2OnSide)
                        {
                            Vector2 cornerPos = new Vector2(
                                point1.Value.X < borders.Center.X ? borders.X : borders.Right,
                                point1.Value.Y < borders.Center.Y ? borders.Y : borders.Bottom);
                            cell.Edges.Add(
                                new GraphEdge(point1.Value, cornerPos)
                                {
                                    Cell1 = cell,
                                    IsSolid = true,
                                    Site1 = cell.Site,
                                    OutsideLevel = true
                                }); 
                            cell.Edges.Add(
                                 new GraphEdge(point2.Value, cornerPos)
                                 {
                                     Cell1 = cell,
                                     IsSolid = true,
                                     Site1 = cell.Site,
                                     OutsideLevel = true
                                 });
                        }
                        else
                        {
                            cell.Edges.Add(
                                new GraphEdge(point1.Value, point2.Value)
                                {
                                    Cell1 = cell,
                                    IsSolid = true,
                                    Site1 = cell.Site,
                                    OutsideLevel = true
                                });
                        }
                        break;
                    }
                }
            }

            return cells;
        }

        public static void GeneratePath(Level.Tunnel tunnel, Level level)
        {
            var targetCells = new List<VoronoiCell>();
            for (int i = 0; i < tunnel.Nodes.Count; i++)
            {
                var closestCell = level.GetClosestCell(tunnel.Nodes[i].ToVector2());
                if (closestCell != null && !targetCells.Contains(closestCell))
                {
                    targetCells.Add(closestCell);
                }
            }
            tunnel.Cells.AddRange(GeneratePath(targetCells, level.GetAllCells()));
        }

        public static List<VoronoiCell> GeneratePath(List<VoronoiCell> targetCells, List<VoronoiCell> cells)
        {
            List<VoronoiCell> pathCells = new List<VoronoiCell>();

            if (targetCells.Count == 0) { return pathCells; }

            VoronoiCell currentCell = targetCells[0];
            currentCell.CellType = CellType.Path;
            pathCells.Add(currentCell);

            int currentTargetIndex = 0;

            int iterationsLeft = cells.Count / 2;

            do
            {
                int edgeIndex = 0;

                double smallestDist = double.PositiveInfinity;
                for (int i = 0; i < currentCell.Edges.Count; i++)
                {
                    var adjacentCell = currentCell.Edges[i].AdjacentCell(currentCell);
                    if (adjacentCell == null) { continue; }
                    double dist = MathUtils.Distance(adjacentCell.Site.Coord.X, adjacentCell.Site.Coord.Y, targetCells[currentTargetIndex].Site.Coord.X, targetCells[currentTargetIndex].Site.Coord.Y);
                    dist += MathUtils.Distance(adjacentCell.Site.Coord.X, adjacentCell.Site.Coord.Y, currentCell.Site.Coord.X, currentCell.Site.Coord.Y) * 0.5f;
                      
                    //disfavor short edges to prevent generating a very small passage
                    if (Vector2.DistanceSquared(currentCell.Edges[i].Point1, currentCell.Edges[i].Point2) < 150.0f * 150.0f)
                    {
                        //divide by the number of times the current cell has been used
                        //  prevents the path from getting "stuck" (jumping back and forth between adjacent cells) 
                        //  if there's no other way to the destination than going through a short edge
                        dist *= 10.0f / Math.Max(pathCells.Count(c => c == currentCell), 1.0f);
                    }
                    if (dist < smallestDist)
                    {
                        edgeIndex = i;
                        smallestDist = dist;
                    }
                }

                currentCell = currentCell.Edges[edgeIndex].AdjacentCell(currentCell);
                currentCell.CellType = CellType.Path;
                pathCells.Add(currentCell);

                iterationsLeft--;

                if (currentCell == targetCells[currentTargetIndex])
                {
                    currentTargetIndex++;
                    if (currentTargetIndex >= targetCells.Count) { break; }
                }

            } while (currentCell != targetCells[targetCells.Count - 1] && iterationsLeft > 0);

            return pathCells;
        }

        /// <summary>
        /// Makes the cell rounder by subdividing the edges and offsetting them at the middle
        /// </summary>
        /// <param name="minEdgeLength">How small the individual subdivided edges can be (smaller values produce rounder shapes, but require more geometry)</param>
        public static void RoundCell(VoronoiCell cell, float minEdgeLength = 500.0f, float roundingAmount = 0.5f, float irregularity = 0.1f)
        {
            List<GraphEdge> tempEdges = new List<GraphEdge>();
            foreach (GraphEdge edge in cell.Edges)
            {
                if (!edge.IsSolid || edge.OutsideLevel)
                {
                    tempEdges.Add(edge);
                    continue;
                }

                Vector2 edgeDiff = edge.Point2 - edge.Point1;
                Vector2 edgeDir = Vector2.Normalize(edgeDiff);

                //If the edge is next to an empty cell and there's another solid cell at the other side of the empty one,
                //don't touch this edge. Otherwise we may end up closing off small passages between cells.
                var adjacentEmptyCell = edge.AdjacentCell(cell);
                if (adjacentEmptyCell?.CellType == CellType.Solid) { adjacentEmptyCell = null; }
                if (adjacentEmptyCell != null)
                {
                    GraphEdge adjacentEdge = null;
                    //find the edge at the opposite side of the adjacent cell
                    foreach (GraphEdge otherEdge in adjacentEmptyCell.Edges)
                    {
                        if (otherEdge == edge || otherEdge.AdjacentCell(adjacentEmptyCell)?.CellType != CellType.Solid) { continue; }
                        Vector2 otherEdgeDir = Vector2.Normalize(otherEdge.Point2 - otherEdge.Point1);
                        //dot product is > 0.7 if the edges are roughly parallel
                        if (Math.Abs(Vector2.Dot(otherEdgeDir, edgeDir)) > 0.7f)
                        {
                            adjacentEdge = otherEdge;
                            break;
                        }
                    }
                    if (adjacentEdge != null)
                    {
                        tempEdges.Add(edge);
                        continue;
                    }
                }
                List<Vector2> edgePoints = new List<Vector2>();
                Vector2 edgeNormal = edge.GetNormal(cell);

                float edgeLength = Vector2.Distance(edge.Point1, edge.Point2);
                int pointCount = (int)Math.Max(Math.Ceiling(edgeLength / minEdgeLength), 1);
                for (int i = 0; i <= pointCount; i++)
                {
                    if (i == 0)
                    {
                        edgePoints.Add(edge.Point1);
                    }
                    else if (i == pointCount)
                    {
                        edgePoints.Add(edge.Point2);
                    }
                    else
                    {

                        float centerF = 0.5f - Math.Abs(0.5f - (i / (float)pointCount));
                        float randomVariance = Rand.Range(0, irregularity, Rand.RandSync.ServerAndClient);
                        Vector2 extrudedPoint = 
                            edge.Point1 +
                            edgeDiff * (i / (float)pointCount) +
                            edgeNormal * edgeLength * (roundingAmount + randomVariance) * centerF;

                        var nearbyCells = Level.Loaded.GetCells(extrudedPoint, searchDepth: 2);
                        bool isInside = false;
                        foreach (var nearbyCell in nearbyCells)
                        {
                            if (nearbyCell == cell || nearbyCell.CellType != CellType.Solid) { continue; }
                            //check if extruding the edge causes it to go inside another one
                            if (nearbyCell.IsPointInside(extrudedPoint))
                            {
                                isInside = true;
                                break;
                            }
                            //check if another edge will be inside this cell after the extrusion
                            Vector2 triangleCenter = (edge.Point1 + edge.Point2 + extrudedPoint) / 3;
                            foreach (GraphEdge nearbyEdge in nearbyCell.Edges)
                            {
                                if (!MathUtils.LinesIntersect(nearbyEdge.Point1, triangleCenter, edge.Point1, extrudedPoint) && 
                                    !MathUtils.LinesIntersect(nearbyEdge.Point1, triangleCenter, edge.Point2, extrudedPoint) &&
                                    !MathUtils.LinesIntersect(nearbyEdge.Point1, triangleCenter, edge.Point1, edge.Point2))
                                {
                                    isInside = true;
                                    break;
                                }
                            }
                            if (isInside) { break; }
                        }

                        if (!isInside) 
                        { 
                            edgePoints.Add(extrudedPoint); 
                        }                       
                    }
                }

                for (int i = 0; i < edgePoints.Count - 1; i++)
                {
                    tempEdges.Add(new GraphEdge(edgePoints[i], edgePoints[i + 1])
                    {
                        Cell1 = edge.Cell1,
                        Cell2 = edge.Cell2,
                        IsSolid = edge.IsSolid,
                        Site1 = edge.Site1,
                        Site2 = edge.Site2,
                        OutsideLevel = edge.OutsideLevel,
                        NextToCave = edge.NextToCave,
                        NextToMainPath = edge.NextToMainPath,
                        NextToSidePath = edge.NextToSidePath
                    });
                }
            }

            cell.Edges = tempEdges;
        }

        public static Body GeneratePolygons(List<VoronoiCell> cells, Level level, out List<Vector2[]> renderTriangles)
        {
            renderTriangles = new List<Vector2[]>();

            List<Vector2> tempVertices = new List<Vector2>();
            List<Vector2> bodyPoints = new List<Vector2>();

            Body cellBody = new Body()
            {
                SleepingAllowed = false,
                BodyType = BodyType.Static,
                CollisionCategories = Physics.CollisionLevel
            };
            GameMain.World.Add(cellBody, findNewContacts: false);

            for (int n = cells.Count - 1; n >= 0; n-- )
            {
                VoronoiCell cell = cells[n];
                
                bodyPoints.Clear();
                tempVertices.Clear();
                foreach (GraphEdge ge in cell.Edges)
                {
                    if (Vector2.DistanceSquared(ge.Point1, ge.Point2) < 0.01f) continue;
                    if (!tempVertices.Any(v => Vector2.DistanceSquared(ge.Point1, v) < 1.0f))
                    {
                        tempVertices.Add(ge.Point1);
                        bodyPoints.Add(ge.Point1);
                    }
                    if (!tempVertices.Any(v => Vector2.DistanceSquared(ge.Point2, v) < 1.0f))
                    {
                        tempVertices.Add(ge.Point2);
                        bodyPoints.Add(ge.Point2);
                    }
                }

                if (tempVertices.Count < 3 || bodyPoints.Count < 2)
                {
                    cells.RemoveAt(n);
                    continue;
                }

                Vector2 minVert = tempVertices[0];
                Vector2 maxVert = tempVertices[0];
                foreach (var vert in tempVertices)
                {
                    minVert = new Vector2(
                        Math.Min(minVert.X, vert.X),
                        Math.Min(minVert.Y, vert.Y));
                    maxVert = new Vector2(
                        Math.Max(maxVert.X, vert.X),
                        Math.Max(maxVert.Y, vert.Y));
                }
                Vector2 center = (minVert + maxVert) / 2;
                renderTriangles.AddRange(MathUtils.TriangulateConvexHull(tempVertices, center));

                if (bodyPoints.Count < 2) { continue; }

                if (bodyPoints.Count < 3)
                {
                    foreach (Vector2 vertex in tempVertices)
                    {
                        if (bodyPoints.Contains(vertex)) { continue; }
                        bodyPoints.Add(vertex);
                        break;
                    }
                }

                for (int i = 0; i < bodyPoints.Count; i++)
                {
                    cell.BodyVertices.Add(bodyPoints[i]);
                    bodyPoints[i] = ConvertUnits.ToSimUnits(bodyPoints[i]);
                }

                if (cell.CellType == CellType.Empty) { continue; }

                cellBody.UserData = cell;
                var triangles = MathUtils.TriangulateConvexHull(bodyPoints, ConvertUnits.ToSimUnits(center));
                
                for (int i = 0; i < triangles.Count; i++)
                {
                    //don't create a triangle if the area of the triangle is too small
                    //(apparently Farseer doesn't like polygons with a very small area, see Shape.ComputeProperties)
                    Vector2 a = triangles[i][0];
                    Vector2 b = triangles[i][1];
                    Vector2 c = triangles[i][2];
                    float area = Math.Abs(a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y)) / 2.0f;
                    if (area < 1.0f) { continue; }

                    Vertices bodyVertices = new Vertices(triangles[i]);
                    PolygonShape polygon = new PolygonShape(bodyVertices, 5.0f);
                    Fixture fixture = new Fixture(polygon,
                        Physics.CollisionLevel,
                        Physics.CollisionAll)
                    {
                        UserData = cell
                    };
                    cellBody.Add(fixture, resetMassData: false);

                    if (fixture.Shape.MassData.Area < FarseerPhysics.Settings.Epsilon)
                    {
                        DebugConsole.ThrowError("Invalid triangle created by CaveGenerator (" + triangles[i][0] + ", " + triangles[i][1] + ", " + triangles[i][2] + ")");
                        GameAnalyticsManager.AddErrorEventOnce(
                            "CaveGenerator.GeneratePolygons:InvalidTriangle",
                            GameAnalyticsManager.ErrorSeverity.Warning,
                            "Invalid triangle created by CaveGenerator (" + triangles[i][0] + ", " + triangles[i][1] + ", " + triangles[i][2] + "). Seed: " + level.Seed);
                    }
                }                
                cell.Body = cellBody;
            }
            cellBody.ResetMassData();

            return cellBody;
        }

        public static List<Vector2> CreateRandomChunk(float radius, int vertexCount, float radiusVariance)
        {
            return CreateRandomChunk(radius * 2, radius * 2, vertexCount, radiusVariance);
        }

        public static List<Vector2> CreateRandomChunk(float width, float height, int vertexCount, float radiusVariance)
        {
            Debug.Assert(radiusVariance < Math.Min(width, height));
            Debug.Assert(vertexCount >= 3);

            List<Vector2> verts = new List<Vector2>();
            float angleStep = MathHelper.TwoPi / vertexCount;
            float angle = 0.0f;
            for (int i = 0; i < vertexCount; i++)
            {
                Vector2 dir = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                verts.Add(new Vector2(dir.X * width / 2, dir.Y * height / 2) + dir * Rand.Range(-radiusVariance, radiusVariance, Rand.RandSync.ServerAndClient));
                angle += angleStep;
            }
            return verts;
        }

    }
}
