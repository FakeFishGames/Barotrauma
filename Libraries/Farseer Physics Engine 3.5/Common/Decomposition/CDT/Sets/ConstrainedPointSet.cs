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

using System.Collections.Generic;

namespace FarseerPhysics.Common.Decomposition.CDT.Sets
{
    /*
     * Extends the PointSet by adding some Constraints on how it will be triangulated<br>
     * A constraint defines an edge between two points in the set, these edges can not
     * be crossed. They will be enforced triangle edges after a triangulation.
     * <p>
     * 
     * 
     * @author Thomas Åhlén, thahlen@gmail.com
     */

    internal class ConstrainedPointSet : PointSet
    {
        private List<TriangulationPoint> _constrainedPointList;

        public ConstrainedPointSet(List<TriangulationPoint> points, int[] index)
            : base(points)
        {
            EdgeIndex = index;
        }

        /**
         * 
         * @param points - A list of all points in PointSet
         * @param constraints - Pairs of two points defining a constraint, all points <b>must</b> be part of given PointSet!
         */

        public ConstrainedPointSet(List<TriangulationPoint> points, IEnumerable<TriangulationPoint> constraints)
            : base(points)
        {
            _constrainedPointList = new List<TriangulationPoint>();
            _constrainedPointList.AddRange(constraints);
        }

        public int[] EdgeIndex { get; private set; }

        public override TriangulationMode TriangulationMode
        {
            get { return TriangulationMode.Constrained; }
        }

        public override void PrepareTriangulation(TriangulationContext tcx)
        {
            base.PrepareTriangulation(tcx);
            if (_constrainedPointList != null)
            {
                TriangulationPoint p1, p2;
                List<TriangulationPoint>.Enumerator iterator = _constrainedPointList.GetEnumerator();
                while (iterator.MoveNext())
                {
                    p1 = iterator.Current;
                    iterator.MoveNext();
                    p2 = iterator.Current;
                    tcx.NewConstraint(p1, p2);
                }
            }
            else
            {
                for (int i = 0; i < EdgeIndex.Length; i += 2)
                {
                    // XXX: must change!!
                    tcx.NewConstraint(Points[EdgeIndex[i]], Points[EdgeIndex[i + 1]]);
                }
            }
        }

        /**
         * TODO: TO BE IMPLEMENTED!
         * Peforms a validation on given input<br>
         * 1. Check's if there any constraint edges are crossing or collinear<br>
         * 2. 
         * @return
         */

        public bool isValid()
        {
            return true;
        }
    }
}