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
        public static List<VertexPositionColor> GenerateWallVertices(List<Vector2[]> triangles, Color color, float zCoord)
        {
            var vertices = new List<VertexPositionColor>();
            for (int i = 0; i < triangles.Count; i++)
            {
                foreach (Vector2 vertex in triangles[i])
                {
                    vertices.Add(new VertexPositionColor(new Vector3(vertex, zCoord), color));
                }
            }
            return vertices;
        }

        /// <summary>
        /// Generates texture coordinates for the vertices based on their positions
        /// </summary>
        public static VertexPositionColorTexture[] ConvertToTextured(VertexPositionColor[] verts, float textureSize)
        {
            VertexPositionColorTexture[] texturedVerts = new VertexPositionColorTexture[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                VertexPositionColor vertex = verts[i];
                texturedVerts[i] = new VertexPositionColorTexture(vertex.Position, vertex.Color, textureCoordinate: Vector2.Zero);
            }
            GenerateTextureCoordinates(texturedVerts, textureSize);
            return texturedVerts;
        }

        /// <summary>
        /// Generates texture coordinates for the vertices based on their positions
        /// </summary>
        public static void GenerateTextureCoordinates(VertexPositionColorTexture[] verts, float textureSize)
        {
            for (int i = 0; i < verts.Length; i++)
            {
                VertexPositionColorTexture vertex = verts[i];
                Vector2 uvCoords = new Vector2(vertex.Position.X, vertex.Position.Y) / textureSize;
                verts[i] = new VertexPositionColorTexture(verts[i].Position, verts[i].Color, uvCoords);
            }            
        }

        public static List<VertexPositionColorTexture> GenerateWallEdgeVertices(
            List<VoronoiCell> cells,
            float expandOutwards, float expandInwards,
            Color outerColor, Color innerColor,
            Level level, float zCoord, bool preventExpandThroughCell = false)
        {
            float outWardThickness = expandOutwards;

            List<VertexPositionColorTexture> vertices = new List<VertexPositionColorTexture>();
            foreach (VoronoiCell cell in cells)
            {
                Vector2 minVert = cell.Edges[0].Point1;
                Vector2 maxVert = cell.Edges[0].Point1;
                float circumference = 0.0f;
                foreach (GraphEdge edge in cell.Edges)
                {
                    circumference += Vector2.Distance(edge.Point1, edge.Point2);
                    minVert = new Vector2(
                        Math.Min(minVert.X, edge.Point1.X),
                        Math.Min(minVert.Y, edge.Point1.Y));
                    maxVert = new Vector2(
                        Math.Max(maxVert.X, edge.Point1.X),
                        Math.Max(maxVert.Y, edge.Point1.Y));
                }
                Vector2 center = (minVert + maxVert) / 2;
                foreach (GraphEdge edge in cell.Edges)
                {
                    if (!edge.IsSolid) { continue; }

                    //the left-side edge on this same cell
                    GraphEdge myLeftEdge = cell.Edges.Find(e => e != edge && (edge.Point1.NearlyEquals(e.Point1) || edge.Point1.NearlyEquals(e.Point2)));
                    //the left-side edge on either this cell, or the adjacent one if this is attached to another cell
                    GraphEdge leftEdge = myLeftEdge;
                    var leftAdjacentCell = leftEdge?.AdjacentCell(cell);
                    if (leftAdjacentCell != null)
                    {
                        var adjEdge = leftAdjacentCell.Edges.Find(e => e != leftEdge && e.IsSolid && (edge.Point1.NearlyEquals(e.Point1) || edge.Point1.NearlyEquals(e.Point2)));
                        if (adjEdge != null) { leftEdge = adjEdge; }
                    }

                    //the right-side edge on this same cell
                    GraphEdge myRightEdge = cell.Edges.Find(e => e != edge && (edge.Point2.NearlyEquals(e.Point1) || edge.Point2.NearlyEquals(e.Point2)));
                    //the right-side edge on either this cell, or the adjacent one if this is attached to another cell
                    GraphEdge rightEdge = myRightEdge;
                    var rightAdjacentCell = rightEdge?.AdjacentCell(cell);
                    if (rightAdjacentCell != null)
                    {
                        var adjEdge = rightAdjacentCell.Edges.Find(e => e != rightEdge && e.IsSolid && (edge.Point2.NearlyEquals(e.Point1) || edge.Point2.NearlyEquals(e.Point2)));
                        if (adjEdge != null) { rightEdge = adjEdge; }
                    }

                    Vector2 leftNormal = Vector2.Zero, rightNormal = Vector2.Zero;

                    float inwardThickness1 = Math.Min(expandInwards, edge.Length);
                    float inwardThickness2 = inwardThickness1;
                    if (leftEdge != null && !leftEdge.IsSolid)
                    {
                        //the left-side edge is non-solid (an edge between two cells, not an actual solid wall edge)
                        // -> expand in the direction of that edge
                        leftNormal = edge.Point1.NearlyEquals(leftEdge.Point1) ? 
                            Vector2.Normalize(leftEdge.Point2 - leftEdge.Point1) :
                            Vector2.Normalize(leftEdge.Point1 - leftEdge.Point2);
                        //maximum expansion is half of the size of the edge (otherwise the expansions from different sides of the edge could overlap or even extend "through" the cell)
                        inwardThickness1 = Math.Min(inwardThickness1, leftEdge.Length / 2);
                    }
                    else if (leftEdge != null)
                    {
                        //use the average of this edge's and the adjacent edge's normals
                        leftNormal = -Vector2.Normalize(edge.GetNormal(cell) + leftEdge.GetNormal(leftAdjacentCell ?? cell));
                        if (!MathUtils.IsValid(leftNormal)) { leftNormal = -edge.GetNormal(cell); }
                        //maximum expansion is the length of the adjacent edge (more expansion causes the textures to distort)
                        inwardThickness1 = Math.Min(Math.Min(inwardThickness1, leftEdge.Length), myLeftEdge.Length);
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
                            GameAnalyticsManager.ErrorSeverity.Warning,
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
                        inwardThickness2 = Math.Min(inwardThickness2, rightEdge.Length / 2);
                    }
                    else if (rightEdge != null)
                    {
                        rightNormal = -Vector2.Normalize(edge.GetNormal(cell) + rightEdge.GetNormal(rightAdjacentCell ?? cell));
                        if (!MathUtils.IsValid(rightNormal)) { rightNormal = -edge.GetNormal(cell); }
                        inwardThickness2 = Math.Min(Math.Min(inwardThickness2, rightEdge.Length), myRightEdge.Length);
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
                            GameAnalyticsManager.ErrorSeverity.Warning,
                            "Invalid right normal (leftedge: " + leftEdge + ", rightedge: " + rightEdge + ", normal: " + rightNormal + ", seed: " + level.Seed + ")");

                        if (cell.Body != null)
                        {
                            if (GameMain.World.BodyList.Contains(cell.Body)) { GameMain.World.Remove(cell.Body); }
                            cell.Body = null;
                        }
                        rightNormal = Vector2.UnitX;
                        break;
                    }

                    float point1UV = MathUtils.WrapAngleTwoPi(MathUtils.VectorToAngle(edge.Point1 - center));
                    float point2UV = MathUtils.WrapAngleTwoPi(MathUtils.VectorToAngle(edge.Point2 - center));
                    //handle wrapping around 0/360
                    if (point1UV - point2UV > MathHelper.Pi) 
                    { 
                        point1UV -= MathHelper.TwoPi;
                    }
                    int textureRepeatCount = (int)Math.Max(circumference / 2 / level.GenerationParams.WallEdgeTextureWidth, 1);
                    point1UV = point1UV / MathHelper.TwoPi * textureRepeatCount;
                    point2UV = point2UV / MathHelper.TwoPi * textureRepeatCount;

                    //if calculating the UVs based on polar coordinates would result in stretching (using less than 10% of the texture for a wall the size of the texture)
                    //just calculate the UVs based on the length of the wall
                    //(this will mean the textures don't align at point2, but it doesn't seem that noticeable)
                    if ((point2UV - point1UV) * level.GenerationParams.WallEdgeTextureWidth < edge.Length * 0.1f)
                    {
                        point2UV = point1UV + edge.Length / 2 / level.GenerationParams.WallEdgeTextureWidth;
                    }

                    //"extruding" inwards, need to make sure we don't make the edge poke through the cell from the other side
                    if (preventExpandThroughCell)
                    {
                        foreach (GraphEdge otherEdge in cell.Edges)
                        {
                            if (otherEdge == edge || Vector2.Dot(otherEdge.GetNormal(cell), edge.GetNormal(cell)) > 0) { continue; }
                            if (otherEdge != leftEdge)
                            {
                                inwardThickness1 = ClampThickness(otherEdge, edge.Point1, leftNormal, inwardThickness1);
                            }
                            if (otherEdge != rightEdge)
                            {
                                inwardThickness2 = ClampThickness(otherEdge, edge.Point2, rightNormal, inwardThickness2);
                            }
                        }

                        static float ClampThickness(GraphEdge otherEdge, Vector2 thisPoint, Vector2 thisEdgeNormal, float currThickness)
                        {
                            if (MathUtils.GetLineIntersection(
                                thisPoint, thisPoint + thisEdgeNormal * currThickness,
                                otherEdge.Point1, otherEdge.Point2, areLinesInfinite: false, out Vector2 intersection1))
                            {
                                return Math.Min(currThickness, Vector2.Distance(thisPoint, intersection1));
                            }
                            return currThickness;
                        }
                    }

                    //there needs to be some minimum amount of inward thickness,
                    //if the edge texture doesn't extend inside at all you can see through between the edge texture and the solid part of the cell
                    inwardThickness1 = Math.Max(inwardThickness1, Math.Min(100.0f, expandInwards));
                    inwardThickness2 = Math.Max(inwardThickness2, Math.Min(100.0f, expandInwards));

                    for (int i = 0; i < 2; i++)
                    {
                        Vector2[] verts = new Vector2[3];
                        VertexPositionColorTexture[] vertPos = new VertexPositionColorTexture[3];
                        
                        if (i == 0)
                        {
                            verts[0] = edge.Point1 - leftNormal * outWardThickness;
                            verts[1] = edge.Point2 - rightNormal * outWardThickness;
                            verts[2] = edge.Point1 + leftNormal * inwardThickness1;

                            vertPos[0] = new VertexPositionColorTexture(new Vector3(verts[0], zCoord), outerColor, new Vector2(point1UV, 0.0f));
                            vertPos[1] = new VertexPositionColorTexture(new Vector3(verts[1], zCoord), outerColor, new Vector2(point2UV, 0.0f));
                            vertPos[2] = new VertexPositionColorTexture(new Vector3(verts[2], zCoord), innerColor, new Vector2(point1UV, 1.0f));
                        }
                        else
                        {

                            verts[0] = edge.Point1 + leftNormal * inwardThickness1;
                            verts[1] = edge.Point2 - rightNormal * outWardThickness;
                            verts[2] = edge.Point2 + rightNormal * inwardThickness2;

                            vertPos[0] = new VertexPositionColorTexture(new Vector3(verts[0], zCoord), innerColor, new Vector2(point1UV, 1.0f));
                            vertPos[1] = new VertexPositionColorTexture(new Vector3(verts[1], zCoord), outerColor, new Vector2(point2UV, 0.0f));
                            vertPos[2] = new VertexPositionColorTexture(new Vector3(verts[2], zCoord), innerColor, new Vector2(point2UV, 1.0f));
                        }
                        vertices.AddRange(vertPos);                        
                    }
                }
            }

            return vertices;
        }
    }
}
