using Barotrauma.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Voronoi2;

namespace Barotrauma
{
    static partial class CaveGenerator
    {
        public static List<VertexPositionTexture> GenerateWallVertices(List<Vector2[]> triangles, LevelGenerationParams generationParams, float zCoord)
        {
            var vertices = new List<VertexPositionTexture>();
            for (int i = 0; i < triangles.Count; i++)
            {
                foreach (Vector2 vertex in triangles[i])
                {
                    Vector2 uvCoords = vertex / generationParams.WallTextureSize;
                    vertices.Add(new VertexPositionTexture(new Vector3(vertex, zCoord), uvCoords));
                }
            }

            return vertices;
        }

        public static List<VertexPositionTexture> GenerateWallEdgeVertices(List<VoronoiCell> cells, Level level, float zCoord)
        {
            float outWardThickness = level.GenerationParams.WallEdgeExpandOutwardsAmount;

            List<VertexPositionTexture> vertices = new List<VertexPositionTexture>();
            foreach (VoronoiCell cell in cells)
            {
                float circumference = 0.0f;
                foreach (GraphEdge edge in cell.Edges)
                {
                    circumference += Vector2.Distance(edge.Point1, edge.Point2);
                }
                foreach (GraphEdge edge in cell.Edges)
                {
                    if (!edge.IsSolid) { continue; }

                    GraphEdge leftEdge = cell.Edges.Find(e => e != edge && (edge.Point1.NearlyEquals(e.Point1) || edge.Point1.NearlyEquals(e.Point2)));
                    var leftAdjacentCell = leftEdge?.AdjacentCell(cell);
                    if (leftAdjacentCell != null)
                    {
                        var adjEdge = leftAdjacentCell.Edges.Find(e => e != leftEdge && e.IsSolid && (edge.Point1.NearlyEquals(e.Point1) || edge.Point1.NearlyEquals(e.Point2)));
                        if (adjEdge != null) { leftEdge = adjEdge; }
                    }

                    GraphEdge rightEdge = cell.Edges.Find(e => e != edge && (edge.Point2.NearlyEquals(e.Point1) || edge.Point2.NearlyEquals(e.Point2)));
                    var rightAdjacentCell = rightEdge?.AdjacentCell(cell);
                    if (rightAdjacentCell != null)
                    {
                        var adjEdge = rightAdjacentCell.Edges.Find(e => e != rightEdge && e.IsSolid && (edge.Point2.NearlyEquals(e.Point1) || edge.Point2.NearlyEquals(e.Point2)));
                        if (adjEdge != null) { rightEdge = adjEdge; }
                    }

                    Vector2 leftNormal = Vector2.Zero, rightNormal = Vector2.Zero;

                    float inwardThickness1 = level.GenerationParams.WallEdgeExpandInwardsAmount;
                    float inwardThickness2 = level.GenerationParams.WallEdgeExpandInwardsAmount;
                    if (leftEdge != null && !leftEdge.IsSolid)
                    {
                        leftNormal = edge.Point1.NearlyEquals(leftEdge.Point1) ? 
                            Vector2.Normalize(leftEdge.Point2 - leftEdge.Point1) :
                            Vector2.Normalize(leftEdge.Point1 - leftEdge.Point2);
                    }
                    else if (leftEdge != null)
                    {
                        leftNormal = -Vector2.Normalize(edge.GetNormal(cell) + leftEdge.GetNormal(leftAdjacentCell ?? cell));
                        if (!MathUtils.IsValid(leftNormal)) { leftNormal = -edge.GetNormal(cell); }
                    }
                    else
                    {
                        leftNormal = Vector2.Normalize(cell.Center - edge.Point1);
                    }
                    inwardThickness1 = Math.Min(Vector2.Distance(edge.Point1, cell.Center), inwardThickness1);

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
                            if (GameMain.World.BodyList.Contains(cell.Body)) { GameMain.World.Remove(cell.Body); }
                            cell.Body = null;
                        }
                        leftNormal = Vector2.UnitX;
                        break;
                    }

                    if (rightEdge != null && !rightEdge.IsSolid)
                    {
                        rightNormal = edge.Point2.NearlyEquals(rightEdge.Point1) ?
                            Vector2.Normalize(rightEdge.Point2 - rightEdge.Point1) :
                            Vector2.Normalize(rightEdge.Point1 - rightEdge.Point2);
                    }
                    else if (rightEdge != null)
                    {
                        rightNormal = -Vector2.Normalize(edge.GetNormal(cell) + rightEdge.GetNormal(rightAdjacentCell ?? cell));
                        if (!MathUtils.IsValid(rightNormal)) { rightNormal = -edge.GetNormal(cell); }
                    }
                    else
                    {
                        rightNormal = Vector2.Normalize(cell.Center - edge.Point2);
                    }
                    inwardThickness2 = Math.Min(Vector2.Distance(edge.Point2, cell.Center), inwardThickness2);

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
                            if (GameMain.World.BodyList.Contains(cell.Body)) { GameMain.World.Remove(cell.Body); }
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
                        point1UV -= MathHelper.TwoPi;
                    }
                    int textureRepeatCount = (int)Math.Max(circumference / 2 / level.GenerationParams.WallEdgeTextureWidth, 1);
                    point1UV = point1UV / MathHelper.TwoPi * textureRepeatCount;
                    point2UV = point2UV / MathHelper.TwoPi * textureRepeatCount;

                    for (int i = 0; i < 2; i++)
                    {
                        Vector2[] verts = new Vector2[3];
                        VertexPositionTexture[] vertPos = new VertexPositionTexture[3];
                        
                        if (i == 0)
                        {
                            verts[0] = edge.Point1 - leftNormal * outWardThickness;
                            verts[1] = edge.Point2 - rightNormal * outWardThickness;
                            verts[2] = edge.Point1 + leftNormal * inwardThickness1;

                            vertPos[0] = new VertexPositionTexture(new Vector3(verts[0], zCoord), new Vector2(point1UV, 0.0f));
                            vertPos[1] = new VertexPositionTexture(new Vector3(verts[1], zCoord), new Vector2(point2UV, 0.0f));
                            vertPos[2] = new VertexPositionTexture(new Vector3(verts[2], zCoord), new Vector2(point1UV, 1.0f));
                        }
                        else
                        {

                            verts[0] = edge.Point1 + leftNormal * inwardThickness1;
                            verts[1] = edge.Point2 - rightNormal * outWardThickness;
                            verts[2] = edge.Point2 + rightNormal * inwardThickness2;

                            vertPos[0] = new VertexPositionTexture(new Vector3(verts[0], zCoord), new Vector2(point1UV, 1.0f));
                            vertPos[1] = new VertexPositionTexture(new Vector3(verts[1], zCoord), new Vector2(point2UV, 0.0f));
                            vertPos[2] = new VertexPositionTexture(new Vector3(verts[2], zCoord), new Vector2(point2UV, 1.0f));
                        }
                        vertices.AddRange(vertPos);                        
                    }
                }
            }

            return vertices;
        }
    }
}
