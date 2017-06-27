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
//   Polygon constructors sprused up, checks for 3+ polys
//   Naming of everything
//   getTriangulationMode() -> TriangulationMode { get; }
//   Exceptions replaced
// Future possibilities
//   We have a lot of Add/Clear methods -- we may prefer to just expose the container
//   Some self-explanitory methods may deserve commenting anyways

using System;
using System.Collections.Generic;
using System.Linq;
using FarseerPhysics.Common.Decomposition.CDT.Delaunay;

namespace FarseerPhysics.Common.Decomposition.CDT.Polygon
{
    internal class Polygon : Triangulatable
    {
        protected List<Polygon> _holes;
        protected PolygonPoint _last;
        protected List<TriangulationPoint> _points = new List<TriangulationPoint>();
        protected List<TriangulationPoint> _steinerPoints;
        protected List<DelaunayTriangle> _triangles;

        /// <summary>
        /// Create a polygon from a list of at least 3 points with no duplicates.
        /// </summary>
        /// <param name="points">A list of unique points</param>
        public Polygon(IList<PolygonPoint> points)
        {
            if (points.Count < 3) throw new ArgumentException("List has fewer than 3 points", "points");

            // Lets do one sanity check that first and last point hasn't got same position
            // Its something that often happen when importing polygon data from other formats
            if (points[0].Equals(points[points.Count - 1])) points.RemoveAt(points.Count - 1);

            _points.AddRange(points.Cast<TriangulationPoint>());
        }

        /// <summary>
        /// Create a polygon from a list of at least 3 points with no duplicates.
        /// </summary>
        /// <param name="points">A list of unique points.</param>
        public Polygon(IEnumerable<PolygonPoint> points) : this((points as IList<PolygonPoint>) ?? points.ToArray())
        {
        }

        public Polygon()
        {
        }

        public IList<Polygon> Holes
        {
            get { return _holes; }
        }

        #region Triangulatable Members

        public TriangulationMode TriangulationMode
        {
            get { return TriangulationMode.Polygon; }
        }

        public IList<TriangulationPoint> Points
        {
            get { return _points; }
        }

        public IList<DelaunayTriangle> Triangles
        {
            get { return _triangles; }
        }

        public void AddTriangle(DelaunayTriangle t)
        {
            _triangles.Add(t);
        }

        public void AddTriangles(IEnumerable<DelaunayTriangle> list)
        {
            _triangles.AddRange(list);
        }

        public void ClearTriangles()
        {
            if (_triangles != null) _triangles.Clear();
        }

        /// <summary>
        /// Creates constraints and populates the context with points
        /// </summary>
        /// <param name="tcx">The context</param>
        public void PrepareTriangulation(TriangulationContext tcx)
        {
            if (_triangles == null)
            {
                _triangles = new List<DelaunayTriangle>(_points.Count);
            }
            else
            {
                _triangles.Clear();
            }

            // Outer constraints
            for (int i = 0; i < _points.Count - 1; i++)
            {
                tcx.NewConstraint(_points[i], _points[i + 1]);
            }
            tcx.NewConstraint(_points[0], _points[_points.Count - 1]);
            tcx.Points.AddRange(_points);

            // Hole constraints
            if (_holes != null)
            {
                foreach (Polygon p in _holes)
                {
                    for (int i = 0; i < p._points.Count - 1; i++)
                    {
                        tcx.NewConstraint(p._points[i], p._points[i + 1]);
                    }
                    tcx.NewConstraint(p._points[0], p._points[p._points.Count - 1]);
                    tcx.Points.AddRange(p._points);
                }
            }

            if (_steinerPoints != null)
            {
                tcx.Points.AddRange(_steinerPoints);
            }
        }

        #endregion

        public void AddSteinerPoint(TriangulationPoint point)
        {
            if (_steinerPoints == null)
            {
                _steinerPoints = new List<TriangulationPoint>();
            }
            _steinerPoints.Add(point);
        }

        public void AddSteinerPoints(List<TriangulationPoint> points)
        {
            if (_steinerPoints == null)
            {
                _steinerPoints = new List<TriangulationPoint>();
            }
            _steinerPoints.AddRange(points);
        }

        public void ClearSteinerPoints()
        {
            if (_steinerPoints != null)
            {
                _steinerPoints.Clear();
            }
        }

        /// <summary>
        /// Add a hole to the polygon.
        /// </summary>
        /// <param name="poly">A subtraction polygon fully contained inside this polygon.</param>
        public void AddHole(Polygon poly)
        {
            if (_holes == null) _holes = new List<Polygon>();
            _holes.Add(poly);
            // XXX: tests could be made here to be sure it is fully inside
            //        addSubtraction( poly.getPoints() );
        }

        /// <summary>
        /// Inserts newPoint after point.
        /// </summary>
        /// <param name="point">The point to insert after in the polygon</param>
        /// <param name="newPoint">The point to insert into the polygon</param>
        public void InsertPointAfter(PolygonPoint point, PolygonPoint newPoint)
        {
            // Validate that 
            int index = _points.IndexOf(point);
            if (index == -1)
                throw new ArgumentException(
                    "Tried to insert a point into a Polygon after a point not belonging to the Polygon", "point");
            newPoint.Next = point.Next;
            newPoint.Previous = point;
            point.Next.Previous = newPoint;
            point.Next = newPoint;
            _points.Insert(index + 1, newPoint);
        }

        /// <summary>
        /// Inserts list (after last point in polygon?)
        /// </summary>
        /// <param name="list"></param>
        public void AddPoints(IEnumerable<PolygonPoint> list)
        {
            PolygonPoint first;
            foreach (PolygonPoint p in list)
            {
                p.Previous = _last;
                if (_last != null)
                {
                    p.Next = _last.Next;
                    _last.Next = p;
                }
                _last = p;
                _points.Add(p);
            }
            first = (PolygonPoint) _points[0];
            _last.Next = first;
            first.Previous = _last;
        }

        /// <summary>
        /// Adds a point after the last in the polygon.
        /// </summary>
        /// <param name="p">The point to add</param>
        public void AddPoint(PolygonPoint p)
        {
            p.Previous = _last;
            p.Next = _last.Next;
            _last.Next = p;
            _points.Add(p);
        }

        /// <summary>
        /// Removes a point from the polygon.
        /// </summary>
        /// <param name="p"></param>
        public void RemovePoint(PolygonPoint p)
        {
            PolygonPoint next, prev;

            next = p.Next;
            prev = p.Previous;
            prev.Next = next;
            next.Previous = prev;
            _points.Remove(p);
        }
    }
}