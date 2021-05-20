/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

/* Poly2Tri
 * Copyright (c) 2009-2010, Poly2Tri Contributors
 * http://code.google.com/p/poly2tri/
 *
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without modification,
 * are permitted provided that the following conditions are met:
 *
 * * Redistributions of source code must retain the above copyright notice,
 *   this list of conditions and the following disclaimer.
 * * Redistributions in binary form must reproduce the above copyright notice,
 *   this list of conditions and the following disclaimer in the documentation
 *   and/or other materials provided with the distribution.
 * * Neither the name of Poly2Tri nor the names of its contributors may be
 *   used to endorse or promote products derived from this software without specific
 *   prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
 * "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
 * LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
 * A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
 * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
 * EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
 * PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
 * PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
 * LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
 * NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

// Changes from the Java version
//   attributification
// Future possibilities
//   Flattening out the number of indirections
//     Replacing arrays of 3 with fixed-length arrays?
//     Replacing bool[3] with a bit array of some sort?
//     Bundling everything into an AoS mess?
//     Hardcode them all as ABC ?

using System;
using System.Collections.Generic;
using System.Diagnostics;
using FarseerPhysics.Common.Decomposition.CDT.Delaunay.Sweep;
using FarseerPhysics.Common.Decomposition.CDT.Util;

namespace FarseerPhysics.Common.Decomposition.CDT.Delaunay
{
    internal class DelaunayTriangle
    {
        /** Neighbor pointers */

        /** Flags to determine if an edge is a Delauney edge */
        public FixedBitArray3 EdgeIsConstrained;

        /** Flags to determine if an edge is a Constrained edge */
        public FixedBitArray3 EdgeIsDelaunay;
        public Util.FixedArray3<DelaunayTriangle> Neighbors;

        /** Has this triangle been marked as an interior triangle? */

        public Util.FixedArray3<TriangulationPoint> Points;

        public DelaunayTriangle(TriangulationPoint p1, TriangulationPoint p2, TriangulationPoint p3)
        {
            Points[0] = p1;
            Points[1] = p2;
            Points[2] = p3;
        }

        public bool IsInterior { get; set; }

        public int IndexOf(TriangulationPoint p)
        {
            int i = Points.IndexOf(p);
            if (i == -1) throw new Exception("Calling index with a point that doesn't exist in triangle");
            return i;
        }

        //TODO: Port note - different implementation
        public int IndexCW(TriangulationPoint p)
        {
            int index = IndexOf(p);
            switch (index)
            {
                case 0:
                    return 2;
                case 1:
                    return 0;
                default:
                    return 1;
            }
        }

        //TODO: Port note - different implementation
        public int IndexCCW(TriangulationPoint p)
        {
            int index = IndexOf(p);
            switch (index)
            {
                case 0:
                    return 1;
                case 1:
                    return 2;
                default:
                    return 0;
            }
        }

        public bool Contains(TriangulationPoint p)
        {
            return (p == Points[0] || p == Points[1] || p == Points[2]);
        }

        public bool Contains(DTSweepConstraint e)
        {
            return (Contains(e.P) && Contains(e.Q));
        }

        public bool Contains(TriangulationPoint p, TriangulationPoint q)
        {
            return (Contains(p) && Contains(q));
        }

        /// <summary>
        /// Update neighbor pointers
        /// </summary>
        /// <param name="p1">Point 1 of the shared edge</param>
        /// <param name="p2">Point 2 of the shared edge</param>
        /// <param name="t">This triangle's new neighbor</param>
        private void MarkNeighbor(TriangulationPoint p1, TriangulationPoint p2, DelaunayTriangle t)
        {
            if ((p1 == Points[2] && p2 == Points[1]) || (p1 == Points[1] && p2 == Points[2]))
            {
                Neighbors[0] = t;
            }
            else if ((p1 == Points[0] && p2 == Points[2]) || (p1 == Points[2] && p2 == Points[0]))
            {
                Neighbors[1] = t;
            }
            else if ((p1 == Points[0] && p2 == Points[1]) || (p1 == Points[1] && p2 == Points[0]))
            {
                Neighbors[2] = t;
            }
            else
            {
                Debug.WriteLine("Neighbor error, please report!");
                // throw new Exception("Neighbor error, please report!");
            }
        }

        /// <summary>
        /// Exhaustive search to update neighbor pointers
        /// </summary>
        public void MarkNeighbor(DelaunayTriangle t)
        {
            if (t.Contains(Points[1], Points[2]))
            {
                Neighbors[0] = t;
                t.MarkNeighbor(Points[1], Points[2], this);
            }
            else if (t.Contains(Points[0], Points[2]))
            {
                Neighbors[1] = t;
                t.MarkNeighbor(Points[0], Points[2], this);
            }
            else if (t.Contains(Points[0], Points[1]))
            {
                Neighbors[2] = t;
                t.MarkNeighbor(Points[0], Points[1], this);
            }
            else
            {
                Debug.WriteLine("markNeighbor failed");
            }
        }

        public void ClearNeighbors()
        {
            Neighbors[0] = Neighbors[1] = Neighbors[2] = null;
        }

        public void ClearNeighbor(DelaunayTriangle triangle)
        {
            if (Neighbors[0] == triangle)
            {
                Neighbors[0] = null;
            }
            else if (Neighbors[1] == triangle)
            {
                Neighbors[1] = null;
            }
            else
            {
                Neighbors[2] = null;
            }
        }

        /**
         * Clears all references to all other triangles and points
         */

        public void Clear()
        {
            DelaunayTriangle t;
            for (int i = 0; i < 3; i++)
            {
                t = Neighbors[i];
                t?.ClearNeighbor(this);
            }
            ClearNeighbors();
            Points[0] = Points[1] = Points[2] = null;
        }

        /// <param name="t">Opposite triangle</param>
        /// <param name="p">The point in t that isn't shared between the triangles</param>
        public TriangulationPoint OppositePoint(DelaunayTriangle t, TriangulationPoint p)
        {
            Debug.Assert(t != this, "self-pointer error");
            return PointCW(t.PointCW(p));
        }

        public DelaunayTriangle NeighborCW(TriangulationPoint point)
        {
            return Neighbors[(Points.IndexOf(point) + 1)%3];
        }

        public DelaunayTriangle NeighborCCW(TriangulationPoint point)
        {
            return Neighbors[(Points.IndexOf(point) + 2)%3];
        }

        public DelaunayTriangle NeighborAcross(TriangulationPoint point)
        {
            return Neighbors[Points.IndexOf(point)];
        }

        public TriangulationPoint PointCCW(TriangulationPoint point)
        {
            return Points[(IndexOf(point) + 1)%3];
        }

        public TriangulationPoint PointCW(TriangulationPoint point)
        {
            return Points[(IndexOf(point) + 2)%3];
        }

        private void RotateCW()
        {
            var t = Points[2];
            Points[2] = Points[1];
            Points[1] = Points[0];
            Points[0] = t;
        }

        /// <summary>
        /// Legalize triangle by rotating clockwise around oPoint
        /// </summary>
        /// <param name="oPoint">The origin point to rotate around</param>
        /// <param name="nPoint">???</param>
        public void Legalize(TriangulationPoint oPoint, TriangulationPoint nPoint)
        {
            RotateCW();
            Points[IndexCCW(oPoint)] = nPoint;
        }

        public override string ToString()
        {
            return Points[0] + "," + Points[1] + "," + Points[2];
        }

        /// <summary>
        /// Finalize edge marking
        /// </summary>
        public void MarkNeighborEdges()
        {
            for (int i = 0; i < 3; i++)
                if (EdgeIsConstrained[i] && Neighbors[i] != null)
                {
                    Neighbors[i].MarkConstrainedEdge(Points[(i + 1)%3], Points[(i + 2)%3]);
                }
        }

        public void MarkEdge(DelaunayTriangle triangle)
        {
            for (int i = 0; i < 3; i++)
                if (EdgeIsConstrained[i])
                {
                    triangle.MarkConstrainedEdge(Points[(i + 1)%3], Points[(i + 2)%3]);
                }
        }

        public void MarkEdge(List<DelaunayTriangle> tList)
        {
            foreach (DelaunayTriangle t in tList)
                for (int i = 0; i < 3; i++)
                    if (t.EdgeIsConstrained[i])
                    {
                        MarkConstrainedEdge(t.Points[(i + 1)%3], t.Points[(i + 2)%3]);
                    }
        }

        public void MarkConstrainedEdge(int index)
        {
            EdgeIsConstrained[index] = true;
        }

        public void MarkConstrainedEdge(DTSweepConstraint edge)
        {
            MarkConstrainedEdge(edge.P, edge.Q);
        }

        /// <summary>
        /// Mark edge as constrained
        /// </summary>
        public void MarkConstrainedEdge(TriangulationPoint p, TriangulationPoint q)
        {
            int i = EdgeIndex(p, q);
            if (i != -1) EdgeIsConstrained[i] = true;
        }

        public double Area()
        {
            double b = Points[0].X - Points[1].X;
            double h = Points[2].Y - Points[1].Y;

            return Math.Abs((b*h*0.5f));
        }

        public TriangulationPoint Centroid()
        {
            double cx = (Points[0].X + Points[1].X + Points[2].X)/3f;
            double cy = (Points[0].Y + Points[1].Y + Points[2].Y)/3f;
            return new TriangulationPoint(cx, cy);
        }

        /// <summary>
        /// Get the index of the neighbor that shares this edge (or -1 if it isn't shared)
        /// </summary>
        /// <returns>index of the shared edge or -1 if edge isn't shared</returns>
        public int EdgeIndex(TriangulationPoint p1, TriangulationPoint p2)
        {
            int i1 = Points.IndexOf(p1);
            int i2 = Points.IndexOf(p2);

            // Points of this triangle in the edge p1-p2
            bool a = (i1 == 0 || i2 == 0);
            bool b = (i1 == 1 || i2 == 1);
            bool c = (i1 == 2 || i2 == 2);

            if (b && c) return 0;
            if (a && c) return 1;
            if (a && b) return 2;
            return -1;
        }

        public bool GetConstrainedEdgeCCW(TriangulationPoint p)
        {
            return EdgeIsConstrained[(IndexOf(p) + 2)%3];
        }

        public bool GetConstrainedEdgeCW(TriangulationPoint p)
        {
            return EdgeIsConstrained[(IndexOf(p) + 1)%3];
        }

        public bool GetConstrainedEdgeAcross(TriangulationPoint p)
        {
            return EdgeIsConstrained[IndexOf(p)];
        }

        public void SetConstrainedEdgeCCW(TriangulationPoint p, bool ce)
        {
            EdgeIsConstrained[(IndexOf(p) + 2)%3] = ce;
        }

        public void SetConstrainedEdgeCW(TriangulationPoint p, bool ce)
        {
            EdgeIsConstrained[(IndexOf(p) + 1)%3] = ce;
        }

        public void SetConstrainedEdgeAcross(TriangulationPoint p, bool ce)
        {
            EdgeIsConstrained[IndexOf(p)] = ce;
        }

        public bool GetDelaunayEdgeCCW(TriangulationPoint p)
        {
            return EdgeIsDelaunay[(IndexOf(p) + 2)%3];
        }

        public bool GetDelaunayEdgeCW(TriangulationPoint p)
        {
            return EdgeIsDelaunay[(IndexOf(p) + 1)%3];
        }

        public bool GetDelaunayEdgeAcross(TriangulationPoint p)
        {
            return EdgeIsDelaunay[IndexOf(p)];
        }

        public void SetDelaunayEdgeCCW(TriangulationPoint p, bool ce)
        {
            EdgeIsDelaunay[(IndexOf(p) + 2)%3] = ce;
        }

        public void SetDelaunayEdgeCW(TriangulationPoint p, bool ce)
        {
            EdgeIsDelaunay[(IndexOf(p) + 1)%3] = ce;
        }

        public void SetDelaunayEdgeAcross(TriangulationPoint p, bool ce)
        {
            EdgeIsDelaunay[IndexOf(p)] = ce;
        }
    }
}