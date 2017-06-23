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

                    GraphEdge leftEdge = cell.edges.Find(e => e != edge && (edge.point1 == e.point1 || edge.point1 == e.point2));
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
    }
}
