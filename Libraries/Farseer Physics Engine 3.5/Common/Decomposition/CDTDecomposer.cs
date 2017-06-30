/*
* Farseer Physics Engine:
* Copyright (c) 2012 Ian Qvist
*/

using System.Collections.Generic;
using System.Diagnostics;
using FarseerPhysics.Common.Decomposition.CDT;
using FarseerPhysics.Common.Decomposition.CDT.Delaunay;
using FarseerPhysics.Common.Decomposition.CDT.Delaunay.Sweep;
using FarseerPhysics.Common.Decomposition.CDT.Polygon;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Common.Decomposition
{
    /// <summary>
    /// 2D constrained Delaunay triangulation algorithm.
    /// Based on the paper "Sweep-line algorithm for constrained Delaunay triangulation" by V. Domiter and and B. Zalik
    /// 
    /// Properties:
    /// - Creates triangles with a large interior angle.
    /// - Supports holes
    /// - Generate a lot of garbage due to incapsulation of the Poly2Tri library.
    /// - Running time is O(n^2), n = number of vertices.
    /// - Does not care about winding order.
    /// 
    /// Source: http://code.google.com/p/poly2tri/
    /// </summary>
    internal static class CDTDecomposer
    {
        /// <summary>
        /// Decompose the polygon into several smaller non-concave polygon.
        /// </summary>
        public static List<Vertices> ConvexPartition(Vertices vertices)
        {
            Debug.Assert(vertices.Count > 3);

            Polygon poly = new Polygon();

            foreach (Vector2 vertex in vertices)
                poly.Points.Add(new TriangulationPoint(vertex.X, vertex.Y));

            if (vertices.Holes != null)
            {
                foreach (Vertices holeVertices in vertices.Holes)
                {
                    Polygon hole = new Polygon();

                    foreach (Vector2 vertex in holeVertices)
                        hole.Points.Add(new TriangulationPoint(vertex.X, vertex.Y));

                    poly.AddHole(hole);
                }
            }

            DTSweepContext tcx = new DTSweepContext();
            tcx.PrepareTriangulation(poly);
            DTSweep.Triangulate(tcx);

            List<Vertices> results = new List<Vertices>();

            foreach (DelaunayTriangle triangle in poly.Triangles)
            {
                Vertices v = new Vertices();
                foreach (TriangulationPoint p in triangle.Points)
                {
                    v.Add(new Vector2((float)p.X, (float)p.Y));
                }
                results.Add(v);
            }

            return results;
        }
    }
}