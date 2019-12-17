using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Voronoi2;

namespace Barotrauma
{
    static partial class CaveGenerator
    {
        public static List<VertexPositionTexture> GenerateRenderVerticeList(List<Vector2[]> triangles)
        {
            var verticeList = new List<VertexPositionTexture>();
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

            return verticeList;
        }

        public static VertexPositionTexture[] GenerateWallShapes(List<VoronoiCell> cells, Level level)
        {
            float outWardThickness = 30.0f;

            List<VertexPositionTexture> verticeList = new List<VertexPositionTexture>();
            foreach (VoronoiCell cell in cells)
            {
                CompareCCW compare = new CompareCCW(cell.Center);
                foreach (GraphEdge edge in cell.Edges)
                {
                    if (edge.Cell1 != null && edge.Cell1.Body == null && edge.Cell1.CellType != CellType.Empty) edge.Cell1 = null;
                    if (edge.Cell2 != null && edge.Cell2.Body == null && edge.Cell2.CellType != CellType.Empty) edge.Cell2 = null;

                    if (compare.Compare(edge.Point1, edge.Point2) == -1)
                    {
                        var temp = edge.Point1;
                        edge.Point1 = edge.Point2;
                        edge.Point2 = temp;
                    }
                }
            }
            
            foreach (VoronoiCell cell in cells)
            {
                foreach (GraphEdge edge in cell.Edges)
                {
                    if (!edge.IsSolid) continue;

                    GraphEdge leftEdge = cell.Edges.Find(e => e != edge && (edge.Point1 == e.Point1 || edge.Point1 == e.Point2));
                    GraphEdge rightEdge = cell.Edges.Find(e => e != edge && (edge.Point2 == e.Point1 || edge.Point2 == e.Point2));

                    Vector2 leftNormal = Vector2.Zero, rightNormal = Vector2.Zero;

                    float inwardThickness1 = 100;
                    float inwardThickness2 = 100;
                    if (leftEdge != null && !leftEdge.IsSolid)
                    {
                        leftNormal = edge.Point1 == leftEdge.Point1 ? 
                            Vector2.Normalize(leftEdge.Point2 - leftEdge.Point1) :
                            Vector2.Normalize(leftEdge.Point1 - leftEdge.Point2);
                        inwardThickness1 = Vector2.Distance(leftEdge.Point1, leftEdge.Point2) / 2;
                    }
                    else
                    {
                        leftNormal = Vector2.Normalize(cell.Center - edge.Point1);
                        inwardThickness1 = Vector2.Distance(edge.Point1, cell.Center) / 2;
                    }

                    if (!MathUtils.IsValid(leftNormal))
                    {
#if DEBUG
                        DebugConsole.ThrowError("Invalid left normal");
#endif
                        GameAnalyticsManager.AddErrorEventOnce("CaveGenerator.GenerateWallShapes:InvalidLeftNormal:" + level.Seed,
                            GameAnalyticsSDK.Net.EGAErrorSeverity.Warning,
                            "Invalid left normal (leftedge: " + leftEdge + ", rightedge: " + rightEdge + ", normal: " + leftNormal + ", seed: " + level.Seed + ")");

                        if (cell.Body != null)
                        {
                            GameMain.World.Remove(cell.Body);
                            cell.Body = null;
                        }
                        leftNormal = Vector2.UnitX;
                        break;
                    }

                    if (rightEdge != null && !rightEdge.IsSolid)
                    {
                        rightNormal = edge.Point2 == rightEdge.Point1 ?
                            Vector2.Normalize(rightEdge.Point2 - rightEdge.Point1) :
                            Vector2.Normalize(rightEdge.Point1 - rightEdge.Point2);
                        inwardThickness2 = Vector2.Distance(rightEdge.Point1, rightEdge.Point2) / 2;
                    }
                    else
                    {
                        rightNormal = Vector2.Normalize(cell.Center - edge.Point2);
                        inwardThickness2 = Vector2.Distance(edge.Point2, cell.Center) / 2;
                    }

                    if (!MathUtils.IsValid(rightNormal))
                    {
#if DEBUG
                        DebugConsole.ThrowError("Invalid right normal");
#endif
                        GameAnalyticsManager.AddErrorEventOnce("CaveGenerator.GenerateWallShapes:InvalidRightNormal:" + level.Seed,
                            GameAnalyticsSDK.Net.EGAErrorSeverity.Warning,
                            "Invalid right normal (leftedge: " + leftEdge + ", rightedge: " + rightEdge + ", normal: " + rightNormal + ", seed: " + level.Seed + ")");

                        if (cell.Body != null)
                        {
                            GameMain.World.Remove(cell.Body);
                            cell.Body = null;
                        }
                        rightNormal = Vector2.UnitX;
                        break;
                    }

                    float point1UV = MathUtils.WrapAngleTwoPi(MathUtils.VectorToAngle(edge.Point1 - cell.Center));
                    float point2UV = MathUtils.WrapAngleTwoPi(MathUtils.VectorToAngle(edge.Point2 - cell.Center));
                    //handle wrapping around 0/360
                    if (point1UV - point2UV > MathHelper.Pi)
                    {
                        point2UV += MathHelper.TwoPi;
                    }
                    //the texture wraps around the cell 4 times
                    //TODO: define the uv scale in level generation parameters?
                    point1UV = point1UV / MathHelper.TwoPi * 4;
                    point2UV = point2UV / MathHelper.TwoPi * 4;

                    for (int i = 0; i < 2; i++)
                    {
                        Vector2[] verts = new Vector2[3];
                        VertexPositionTexture[] vertPos = new VertexPositionTexture[3];
                        
                        if (i == 0)
                        {
                            verts[0] = edge.Point1 - leftNormal * outWardThickness;
                            verts[1] = edge.Point2 - rightNormal * outWardThickness;
                            verts[2] = edge.Point1 + leftNormal * inwardThickness1;

                            vertPos[0] = new VertexPositionTexture(new Vector3(verts[0], 0.0f), new Vector2(point1UV, 0.0f));
                            vertPos[1] = new VertexPositionTexture(new Vector3(verts[1], 0.0f), new Vector2(point2UV, 0.0f));
                            vertPos[2] = new VertexPositionTexture(new Vector3(verts[2], 0.0f), new Vector2(point1UV, 0.5f));
                        }
                        else
                        {

                            verts[0] = edge.Point1 + leftNormal * inwardThickness1;
                            verts[1] = edge.Point2 - rightNormal * outWardThickness;
                            verts[2] = edge.Point2 + rightNormal * inwardThickness2;

                            vertPos[0] = new VertexPositionTexture(new Vector3(verts[0], 0.0f), new Vector2(point1UV, 0.5f));
                            vertPos[1] = new VertexPositionTexture(new Vector3(verts[1], 0.0f), new Vector2(point2UV, 0.0f));
                            vertPos[2] = new VertexPositionTexture(new Vector3(verts[2], 0.0f), new Vector2(point2UV, 0.5f));
                        }
                        verticeList.AddRange(vertPos);                        
                    }
                }
            }

            return verticeList.ToArray();
        }
    }
}
