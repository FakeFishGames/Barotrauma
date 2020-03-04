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

namespace FarseerPhysics.Common.Decomposition.CDT.Delaunay.Sweep
{
    /**
     * 
     * @author Thomas Åhlén, thahlen@gmail.com
     *
     */

    internal class DTSweepContext : TriangulationContext
    {
        // Inital triangle factor, seed triangle will extend 30% of 
        // PointSet width to both left and right.
        private const float ALPHA = 0.3f;

        public DTSweepBasin Basin = new DTSweepBasin();
        public DTSweepEdgeEvent EdgeEvent = new DTSweepEdgeEvent();

        private DTSweepPointComparator _comparator = new DTSweepPointComparator();
        public AdvancingFront aFront;

        public DTSweepContext()
        {
            Clear();
        }

        public TriangulationPoint Head { get; set; }
        public TriangulationPoint Tail { get; set; }

        public void RemoveFromList(DelaunayTriangle triangle)
        {
            Triangles.Remove(triangle);
            // TODO: remove all neighbor pointers to this triangle
            //        for( int i=0; i<3; i++ )
            //        {
            //            if( triangle.neighbors[i] != null )
            //            {
            //                triangle.neighbors[i].clearNeighbor( triangle );
            //            }
            //        }
            //        triangle.clearNeighbors();
        }

        public void MeshClean(DelaunayTriangle triangle)
        {
            MeshCleanReq(triangle);
        }

        private void MeshCleanReq(DelaunayTriangle triangle)
        {
            if (triangle != null && !triangle.IsInterior)
            {
                triangle.IsInterior = true;
                Triangulatable.AddTriangle(triangle);
                for (int i = 0; i < 3; i++)
                {
                    if (!triangle.EdgeIsConstrained[i])
                    {
                        MeshCleanReq(triangle.Neighbors[i]);
                    }
                }
            }
        }

        public override void Clear()
        {
            base.Clear();
            Triangles.Clear();
        }

        public void AddNode(AdvancingFrontNode node)
        {
            //        Console.WriteLine( "add:" + node.key + ":" + System.identityHashCode(node.key));
            //        m_nodeTree.put( node.getKey(), node );
            aFront.AddNode(node);
        }

        public void RemoveNode(AdvancingFrontNode node)
        {
            //        Console.WriteLine( "remove:" + node.key + ":" + System.identityHashCode(node.key));
            //        m_nodeTree.delete( node.getKey() );
            aFront.RemoveNode(node);
        }

        public AdvancingFrontNode LocateNode(TriangulationPoint point)
        {
            return aFront.LocateNode(point);
        }

        public void CreateAdvancingFront()
        {
            AdvancingFrontNode head, tail, middle;
            // Initial triangle
            DelaunayTriangle iTriangle = new DelaunayTriangle(Points[0], Tail, Head);
            Triangles.Add(iTriangle);

            head = new AdvancingFrontNode(iTriangle.Points[1]);
            head.Triangle = iTriangle;
            middle = new AdvancingFrontNode(iTriangle.Points[0]);
            middle.Triangle = iTriangle;
            tail = new AdvancingFrontNode(iTriangle.Points[2]);

            aFront = new AdvancingFront(head, tail);
            aFront.AddNode(middle);

            // TODO: I think it would be more intuitive if head is middles next and not previous
            //       so swap head and tail
            aFront.Head.Next = middle;
            middle.Next = aFront.Tail;
            middle.Prev = aFront.Head;
            aFront.Tail.Prev = middle;
        }

        /// <summary>
        /// Try to map a node to all sides of this triangle that don't have 
        /// a neighbor.
        /// </summary>
        public void MapTriangleToNodes(DelaunayTriangle t)
        {
            AdvancingFrontNode n;
            for (int i = 0; i < 3; i++)
            {
                if (t.Neighbors[i] == null)
                {
                    n = aFront.LocatePoint(t.PointCW(t.Points[i]));
                    if (n != null)
                    {
                        n.Triangle = t;
                    }
                }
            }
        }

        public override void PrepareTriangulation(Triangulatable t)
        {
            base.PrepareTriangulation(t);

            double xmax, xmin;
            double ymax, ymin;

            xmax = xmin = Points[0].X;
            ymax = ymin = Points[0].Y;

            // Calculate bounds. Should be combined with the sorting
            foreach (TriangulationPoint p in Points)
            {
                if (p.X > xmax)
                    xmax = p.X;
                if (p.X < xmin)
                    xmin = p.X;
                if (p.Y > ymax)
                    ymax = p.Y;
                if (p.Y < ymin)
                    ymin = p.Y;
            }

            double deltaX = ALPHA*(xmax - xmin);
            double deltaY = ALPHA*(ymax - ymin);
            TriangulationPoint p1 = new TriangulationPoint(xmax + deltaX, ymin - deltaY);
            TriangulationPoint p2 = new TriangulationPoint(xmin - deltaX, ymin - deltaY);

            Head = p1;
            Tail = p2;

            //        long time = System.nanoTime();
            // Sort the points along y-axis
            Points.Sort(_comparator);
            //        logger.info( "Triangulation setup [{}ms]", ( System.nanoTime() - time ) / 1e6 );
        }


        public void FinalizeTriangulation()
        {
            Triangulatable.AddTriangles(Triangles);
            Triangles.Clear();
        }

        public override TriangulationConstraint NewConstraint(TriangulationPoint a, TriangulationPoint b)
        {
            return new DTSweepConstraint(a, b);
        }

        #region Nested type: DTSweepBasin

        public class DTSweepBasin
        {
            public AdvancingFrontNode bottomNode;
            public bool leftHighest;
            public AdvancingFrontNode leftNode;
            public AdvancingFrontNode rightNode;
            public double width;
        }

        #endregion

        #region Nested type: DTSweepEdgeEvent

        public class DTSweepEdgeEvent
        {
            public DTSweepConstraint ConstrainedEdge;
            public bool Right;
        }

        #endregion
    }
}