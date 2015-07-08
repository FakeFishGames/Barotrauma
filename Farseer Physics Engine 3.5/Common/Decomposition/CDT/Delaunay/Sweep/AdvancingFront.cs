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
//   Removed BST code, but not all artifacts of it
// Future possibilities
//   Eliminate Add/RemoveNode ?
//   Comments comments and more comments!

using System;
using System.Text;

namespace FarseerPhysics.Common.Decomposition.CDT.Delaunay.Sweep
{
    /**
     * @author Thomas Åhlen (thahlen@gmail.com)
     */

    internal class AdvancingFront
    {
        public AdvancingFrontNode Head;
        protected AdvancingFrontNode Search;
        public AdvancingFrontNode Tail;

        public AdvancingFront(AdvancingFrontNode head, AdvancingFrontNode tail)
        {
            Head = head;
            Tail = tail;
            Search = head;
            AddNode(head);
            AddNode(tail);
        }

        public void AddNode(AdvancingFrontNode node)
        {
            //_searchTree.put(node.key, node);
        }

        public void RemoveNode(AdvancingFrontNode node)
        {
            //_searchTree.delete( node.key );
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            AdvancingFrontNode node = Head;
            while (node != Tail)
            {
                sb.Append(node.Point.X).Append("->");
                node = node.Next;
            }
            sb.Append(Tail.Point.X);
            return sb.ToString();
        }

        /// <summary>
        /// MM:  This seems to be used by LocateNode to guess a position in the implicit linked list of AdvancingFrontNodes near x
        ///      Removed an overload that depended on this being exact
        /// </summary>
        private AdvancingFrontNode FindSearchNode(double x)
        {
            // TODO: implement BST index 
            return Search;
        }

        /// <summary>
        /// We use a balancing tree to locate a node smaller or equal to given key value
        /// </summary>
        public AdvancingFrontNode LocateNode(TriangulationPoint point)
        {
            return LocateNode(point.X);
        }

        private AdvancingFrontNode LocateNode(double x)
        {
            AdvancingFrontNode node = FindSearchNode(x);
            if (x < node.Value)
            {
                while ((node = node.Prev) != null)
                    if (x >= node.Value)
                    {
                        Search = node;
                        return node;
                    }
            }
            else
            {
                while ((node = node.Next) != null)
                    if (x < node.Value)
                    {
                        Search = node.Prev;
                        return node.Prev;
                    }
            }
            return null;
        }

        /// <summary>
        /// This implementation will use simple node traversal algorithm to find a point on the front
        /// </summary>
        public AdvancingFrontNode LocatePoint(TriangulationPoint point)
        {
            double px = point.X;
            AdvancingFrontNode node = FindSearchNode(px);
            double nx = node.Point.X;

            if (px == nx)
            {
                if (point != node.Point)
                {
                    // We might have two nodes with same x value for a short time
                    if (point == node.Prev.Point)
                    {
                        node = node.Prev;
                    }
                    else if (point == node.Next.Point)
                    {
                        node = node.Next;
                    }
                    else
                    {
                        throw new Exception("Failed to find Node for given afront point");
                        //node = null;
                    }
                }
            }
            else if (px < nx)
            {
                while ((node = node.Prev) != null)
                {
                    if (point == node.Point)
                    {
                        break;
                    }
                }
            }
            else
            {
                while ((node = node.Next) != null)
                {
                    if (point == node.Point)
                    {
                        break;
                    }
                }
            }
            Search = node;
            return node;
        }
    }
}