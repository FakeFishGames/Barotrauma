using System;
using System.Collections.Generic;
using System.Diagnostics;
using FarseerPhysics.Common.ConvexHull;

namespace FarseerPhysics.Common.Decomposition
{
    public enum TriangulationAlgorithm
    {
        /// <summary>
        /// Convex decomposition algorithm using ear clipping
        /// 
        /// Properties:
        /// - Only works on simple polygons.
        /// - Does not support holes.
        /// - Running time is O(n^2), n = number of vertices.
        /// </summary>
        Earclip,

        /// <summary>
        /// Convex decomposition algorithm created by Mark Bayazit (http://mnbayazit.com/)
        /// 
        /// Properties:
        /// - Tries to decompose using polygons instead of triangles.
        /// - Tends to produce optimal results with low processing time.
        /// - Running time is O(nr), n = number of vertices, r = reflex vertices.
        /// - Does not support holes.
        /// </summary>
        Bayazit,

        /// <summary>
        /// Convex decomposition algorithm created by unknown
        /// 
        /// Properties:
        /// - No support for holes
        /// - Very fast
        /// - Only works on simple polygons
        /// - Only works on counter clockwise polygons
        /// </summary>
        Flipcode,

        /// <summary>
        /// Convex decomposition algorithm created by Raimund Seidel
        /// 
        /// Properties:
        /// - Decompose the polygon into trapezoids, then triangulate.
        /// - To use the trapezoid data, use ConvexPartitionTrapezoid()
        /// - Generate a lot of garbage due to incapsulation of the Poly2Tri library.
        /// - Running time is O(n log n), n = number of vertices.
        /// - Running time is almost linear for most simple polygons.
        /// - Does not care about winding order. 
        /// </summary>
        Seidel,
        SeidelTrapezoids,

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
        /// </summary>
        Delauny
    }

    public static class Triangulate
    {
        public static List<Vertices> ConvexPartition(Vertices vertices, TriangulationAlgorithm algorithm, bool discardAndFixInvalid = true, float tolerance = 0.001f)
        {
            if (vertices.Count <= 3)
                return new List<Vertices> { vertices };

            List<Vertices> results;

            switch (algorithm)
            {
                case TriangulationAlgorithm.Earclip:
                    if (Settings.SkipSanityChecks)
                        Debug.Assert(!vertices.IsCounterClockWise(), "The Earclip algorithm expects the polygon to be clockwise.");
                    else
                    {
                        if (vertices.IsCounterClockWise())
                        {
                            Vertices temp = new Vertices(vertices);
                            temp.Reverse();
                            results = EarclipDecomposer.ConvexPartition(temp, tolerance);
                        }
                        else
                            results = EarclipDecomposer.ConvexPartition(vertices, tolerance);
                    }
                    break;
                case TriangulationAlgorithm.Bayazit:
                    if (Settings.SkipSanityChecks)
                        Debug.Assert(vertices.IsCounterClockWise(), "The polygon is not counter clockwise. This is needed for Bayazit to work correctly.");
                    else
                    {
                        if (!vertices.IsCounterClockWise())
                        {
                            Vertices temp = new Vertices(vertices);
                            temp.Reverse();
                            results = BayazitDecomposer.ConvexPartition(temp);
                        }
                        else
                            results = BayazitDecomposer.ConvexPartition(vertices);
                    }
                    break;
                case TriangulationAlgorithm.Flipcode:
                    if (Settings.SkipSanityChecks)
                        Debug.Assert(vertices.IsCounterClockWise(), "The polygon is not counter clockwise. This is needed for Bayazit to work correctly.");
                    else
                    {
                        if (!vertices.IsCounterClockWise())
                        {
                            Vertices temp = new Vertices(vertices);
                            temp.Reverse();
                            results = FlipcodeDecomposer.ConvexPartition(temp);
                        }
                        else
                            results = FlipcodeDecomposer.ConvexPartition(vertices);
                    }
                    break;
                case TriangulationAlgorithm.Seidel:
                    results = SeidelDecomposer.ConvexPartition(vertices, tolerance);
                    break;
                case TriangulationAlgorithm.SeidelTrapezoids:
                    results = SeidelDecomposer.ConvexPartitionTrapezoid(vertices, tolerance);
                    break;
                case TriangulationAlgorithm.Delauny:
                    results = CDTDecomposer.ConvexPartition(vertices);
                    break;
                default:
                    throw new ArgumentOutOfRangeException("algorithm");
            }

            if (discardAndFixInvalid)
            {
                for (int i = results.Count - 1; i >= 0; i--)
                {
                    Vertices polygon = results[i];

                    if (!ValidatePolygon(polygon))
                        results.RemoveAt(i);
                }
            }

            return results;
        }

        private static bool ValidatePolygon(Vertices polygon)
        {
            PolygonError errorCode = polygon.CheckPolygon();

            if (errorCode == PolygonError.InvalidAmountOfVertices || errorCode == PolygonError.AreaTooSmall || errorCode == PolygonError.SideTooSmall || errorCode == PolygonError.NotSimple)
                return false;

            if (errorCode == PolygonError.NotCounterClockWise) //NotCounterCloseWise is the last check in CheckPolygon(), thus we don't need to call ValidatePolygon again.
                polygon.Reverse();

            if (errorCode == PolygonError.NotConvex)
            {
                polygon = GiftWrap.GetConvexHull(polygon);
                return ValidatePolygon(polygon);
            }

            return true;
        }
    }
}
