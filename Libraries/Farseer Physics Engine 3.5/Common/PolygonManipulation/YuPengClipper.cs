/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Common.PolygonManipulation
{
    internal enum PolyClipType
    {
        Intersect,
        Union,
        Difference
    }

    public enum PolyClipError
    {
        None,
        DegeneratedOutput,
        NonSimpleInput,
        BrokenResult
    }

    //Clipper contributed by Helge Backhaus

    public static class YuPengClipper
    {
        private const float ClipperEpsilonSquared = 1.192092896e-07f;

        public static List<Vertices> Union(Vertices polygon1, Vertices polygon2, out PolyClipError error)
        {
            return Execute(polygon1, polygon2, PolyClipType.Union, out error);
        }

        public static List<Vertices> Difference(Vertices polygon1, Vertices polygon2, out PolyClipError error)
        {
            return Execute(polygon1, polygon2, PolyClipType.Difference, out error);
        }

        public static List<Vertices> Intersect(Vertices polygon1, Vertices polygon2, out PolyClipError error)
        {
            return Execute(polygon1, polygon2, PolyClipType.Intersect, out error);
        }

        /// <summary>
        /// Implements "A new algorithm for Boolean operations on general polygons" 
        /// available here: http://liama.ia.ac.cn/wiki/_media/user:dong:dong_cg_05.pdf
        /// Merges two polygons, a subject and a clip with the specified operation. Polygons may not be 
        /// self-intersecting.
        /// 
        /// Warning: May yield incorrect results or even crash if polygons contain collinear points.
        /// </summary>
        /// <param name="subject">The subject polygon.</param>
        /// <param name="clip">The clip polygon, which is added, 
        /// substracted or intersected with the subject</param>
        /// <param name="clipType">The operation to be performed. Either
        /// Union, Difference or Intersection.</param>
        /// <param name="error">The error generated (if any)</param>
        /// <returns>A list of closed polygons, which make up the result of the clipping operation.
        /// Outer contours are ordered counter clockwise, holes are ordered clockwise.</returns>
        private static List<Vertices> Execute(Vertices subject, Vertices clip, PolyClipType clipType, out PolyClipError error)
        {
            Debug.Assert(subject.IsSimple() && clip.IsSimple(), "Non simple input! Input polygons must be simple (cannot intersect themselves).");

            // Copy polygons
            Vertices slicedSubject;
            Vertices slicedClip;
            // Calculate the intersection and touch points between
            // subject and clip and add them to both
            CalculateIntersections(subject, clip, out slicedSubject, out slicedClip);

            // Translate polygons into upper right quadrant
            // as the algorithm depends on it
            Vector2 lbSubject = subject.GetAABB().LowerBound;
            Vector2 lbClip = clip.GetAABB().LowerBound;
            Vector2 translate;
            Vector2.Min(ref lbSubject, ref lbClip, out translate);
            translate = Vector2.One - translate;
            if (translate != Vector2.Zero)
            {
                slicedSubject.Translate(ref translate);
                slicedClip.Translate(ref translate);
            }

            // Enforce counterclockwise contours
            slicedSubject.ForceCounterClockWise();
            slicedClip.ForceCounterClockWise();

            List<Edge> subjectSimplices;
            List<float> subjectCoeff;
            List<Edge> clipSimplices;
            List<float> clipCoeff;
            // Build simplical chains from the polygons and calculate the
            // the corresponding coefficients
            CalculateSimplicalChain(slicedSubject, out subjectCoeff, out subjectSimplices);
            CalculateSimplicalChain(slicedClip, out clipCoeff, out clipSimplices);

            List<Edge> resultSimplices;

            // Determine the characteristics function for all non-original edges
            // in subject and clip simplical chain and combine the edges contributing
            // to the result, depending on the clipType
            CalculateResultChain(subjectCoeff, subjectSimplices, clipCoeff, clipSimplices, clipType,
                                 out resultSimplices);

            List<Vertices> result;
            // Convert result chain back to polygon(s)
            error = BuildPolygonsFromChain(resultSimplices, out result);

            // Reverse the polygon translation from the beginning
            // and remove collinear points from output
            translate *= -1f;
            for (int i = 0; i < result.Count; ++i)
            {
                result[i].Translate(ref translate);
                SimplifyTools.CollinearSimplify(result[i]);
            }
            return result;
        }

        /// <summary>
        /// Calculates all intersections between two polygons.
        /// </summary>
        /// <param name="polygon1">The first polygon.</param>
        /// <param name="polygon2">The second polygon.</param>
        /// <param name="slicedPoly1">Returns the first polygon with added intersection points.</param>
        /// <param name="slicedPoly2">Returns the second polygon with added intersection points.</param>
        private static void CalculateIntersections(Vertices polygon1, Vertices polygon2,
                                                   out Vertices slicedPoly1, out Vertices slicedPoly2)
        {
            slicedPoly1 = new Vertices(polygon1);
            slicedPoly2 = new Vertices(polygon2);

            // Iterate through polygon1's edges
            for (int i = 0; i < polygon1.Count; i++)
            {
                // Get edge vertices
                Vector2 a = polygon1[i];
                Vector2 b = polygon1[polygon1.NextIndex(i)];

                // Get intersections between this edge and polygon2
                for (int j = 0; j < polygon2.Count; j++)
                {
                    Vector2 c = polygon2[j];
                    Vector2 d = polygon2[polygon2.NextIndex(j)];

                    Vector2 intersectionPoint;
                    // Check if the edges intersect
                    if (LineTools.LineIntersect(a, b, c, d, out intersectionPoint))
                    {
                        // calculate alpha values for sorting multiple intersections points on a edge
                        float alpha;
                        // Insert intersection point into first polygon
                        alpha = GetAlpha(a, b, intersectionPoint);
                        if (alpha > 0f && alpha < 1f)
                        {
                            int index = slicedPoly1.IndexOf(a) + 1;
                            while (index < slicedPoly1.Count &&
                                   GetAlpha(a, b, slicedPoly1[index]) <= alpha)
                            {
                                ++index;
                            }
                            slicedPoly1.Insert(index, intersectionPoint);
                        }
                        // Insert intersection point into second polygon
                        alpha = GetAlpha(c, d, intersectionPoint);
                        if (alpha > 0f && alpha < 1f)
                        {
                            int index = slicedPoly2.IndexOf(c) + 1;
                            while (index < slicedPoly2.Count &&
                                   GetAlpha(c, d, slicedPoly2[index]) <= alpha)
                            {
                                ++index;
                            }
                            slicedPoly2.Insert(index, intersectionPoint);
                        }
                    }
                }
            }
            // Check for very small edges
            for (int i = 0; i < slicedPoly1.Count; ++i)
            {
                int iNext = slicedPoly1.NextIndex(i);
                //If they are closer than the distance remove vertex
                if ((slicedPoly1[iNext] - slicedPoly1[i]).LengthSquared() <= ClipperEpsilonSquared)
                {
                    slicedPoly1.RemoveAt(i);
                    --i;
                }
            }
            for (int i = 0; i < slicedPoly2.Count; ++i)
            {
                int iNext = slicedPoly2.NextIndex(i);
                //If they are closer than the distance remove vertex
                if ((slicedPoly2[iNext] - slicedPoly2[i]).LengthSquared() <= ClipperEpsilonSquared)
                {
                    slicedPoly2.RemoveAt(i);
                    --i;
                }
            }
        }

        /// <summary>
        /// Calculates the simplical chain corresponding to the input polygon.
        /// </summary>
        /// <remarks>Used by method <c>Execute()</c>.</remarks>
        private static void CalculateSimplicalChain(Vertices poly, out List<float> coeff,
                                                    out List<Edge> simplicies)
        {
            simplicies = new List<Edge>();
            coeff = new List<float>();
            for (int i = 0; i < poly.Count; ++i)
            {
                simplicies.Add(new Edge(poly[i], poly[poly.NextIndex(i)]));
                coeff.Add(CalculateSimplexCoefficient(Vector2.Zero, poly[i], poly[poly.NextIndex(i)]));
            }
        }

        /// <summary>
        /// Calculates the characteristics function for all edges of
        /// the given simplical chains and builds the result chain.
        /// </summary>
        /// <remarks>Used by method <c>Execute()</c>.</remarks>
        private static void CalculateResultChain(List<float> poly1Coeff, List<Edge> poly1Simplicies,
                                                   List<float> poly2Coeff, List<Edge> poly2Simplicies,
                                                   PolyClipType clipType, out List<Edge> resultSimplices)
        {
            resultSimplices = new List<Edge>();

            for (int i = 0; i < poly1Simplicies.Count; ++i)
            {
                float edgeCharacter = 0;
                if (poly2Simplicies.Contains(poly1Simplicies[i]))
                {
                    edgeCharacter = 1f;
                }
                else if (poly2Simplicies.Contains(-poly1Simplicies[i]) && clipType == PolyClipType.Union)
                {
                    edgeCharacter = 1f;
                }
                else
                {
                    for (int j = 0; j < poly2Simplicies.Count; ++j)
                    {
                        if (!poly2Simplicies.Contains(-poly1Simplicies[i]))
                        {
                            edgeCharacter += CalculateBeta(poly1Simplicies[i].GetCenter(),
                                                           poly2Simplicies[j], poly2Coeff[j]);
                        }
                    }
                }
                if (clipType == PolyClipType.Intersect)
                {
                    if (edgeCharacter == 1f)
                    {
                        resultSimplices.Add(poly1Simplicies[i]);
                    }
                }
                else
                {
                    if (edgeCharacter == 0f)
                    {
                        resultSimplices.Add(poly1Simplicies[i]);
                    }
                }
            }
            for (int i = 0; i < poly2Simplicies.Count; ++i)
            {
                float edgeCharacter = 0f;
                if (!resultSimplices.Contains(poly2Simplicies[i]) &&
                    !resultSimplices.Contains(-poly2Simplicies[i]))
                {
                    if (poly1Simplicies.Contains(-poly2Simplicies[i]) && clipType == PolyClipType.Union)
                    {
                        edgeCharacter = 1f;
                    }
                    else
                    {
                        edgeCharacter = 0f;
                        for (int j = 0; j < poly1Simplicies.Count; ++j)
                        {
                            if (!poly1Simplicies.Contains(poly2Simplicies[i]) && !poly1Simplicies.Contains(-poly2Simplicies[i]))
                            {
                                edgeCharacter += CalculateBeta(poly2Simplicies[i].GetCenter(),
                                                               poly1Simplicies[j], poly1Coeff[j]);
                            }
                        }
                        if (clipType == PolyClipType.Intersect || clipType == PolyClipType.Difference)
                        {
                            if (edgeCharacter == 1f)
                            {
                                resultSimplices.Add(-poly2Simplicies[i]);
                            }
                        }
                        else
                        {
                            if (edgeCharacter == 0f)
                            {
                                resultSimplices.Add(poly2Simplicies[i]);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Calculates the polygon(s) from the result simplical chain.
        /// </summary>
        /// <remarks>Used by method <c>Execute()</c>.</remarks>
        private static PolyClipError BuildPolygonsFromChain(List<Edge> simplicies, out List<Vertices> result)
        {
            result = new List<Vertices>();
            PolyClipError errVal = PolyClipError.None;

            while (simplicies.Count > 0)
            {
                Vertices output = new Vertices();
                output.Add(simplicies[0].EdgeStart);
                output.Add(simplicies[0].EdgeEnd);
                simplicies.RemoveAt(0);
                bool closed = false;
                int index = 0;
                int count = simplicies.Count; // Needed to catch infinite loops
                while (!closed && simplicies.Count > 0)
                {
                    if (VectorEqual(output[output.Count - 1], simplicies[index].EdgeStart))
                    {
                        if (VectorEqual(simplicies[index].EdgeEnd, output[0]))
                        {
                            closed = true;
                        }
                        else
                        {
                            output.Add(simplicies[index].EdgeEnd);
                        }
                        simplicies.RemoveAt(index);
                        --index;
                    }
                    else if (VectorEqual(output[output.Count - 1], simplicies[index].EdgeEnd))
                    {
                        if (VectorEqual(simplicies[index].EdgeStart, output[0]))
                        {
                            closed = true;
                        }
                        else
                        {
                            output.Add(simplicies[index].EdgeStart);
                        }
                        simplicies.RemoveAt(index);
                        --index;
                    }
                    if (!closed)
                    {
                        if (++index == simplicies.Count)
                        {
                            if (count == simplicies.Count)
                            {
                                result = new List<Vertices>();
                                Debug.WriteLine("Undefined error while building result polygon(s).");
                                return PolyClipError.BrokenResult;
                            }
                            index = 0;
                            count = simplicies.Count;
                        }
                    }
                }
                if (output.Count < 3)
                {
                    errVal = PolyClipError.DegeneratedOutput;
                    Debug.WriteLine("Degenerated output polygon produced (vertices < 3).");
                }
                result.Add(output);
            }
            return errVal;
        }

        /// <summary>
        /// Needed to calculate the characteristics function of a simplex.
        /// </summary>
        /// <remarks>Used by method <c>CalculateEdgeCharacter()</c>.</remarks>
        private static float CalculateBeta(Vector2 point, Edge e, float coefficient)
        {
            float result = 0f;
            if (PointInSimplex(point, e))
            {
                result = coefficient;
            }
            if (PointOnLineSegment(Vector2.Zero, e.EdgeStart, point) ||
                PointOnLineSegment(Vector2.Zero, e.EdgeEnd, point))
            {
                result = .5f * coefficient;
            }
            return result;
        }

        /// <summary>
        /// Needed for sorting multiple intersections points on the same edge.
        /// </summary>
        /// <remarks>Used by method <c>CalculateIntersections()</c>.</remarks>
        private static float GetAlpha(Vector2 start, Vector2 end, Vector2 point)
        {
            return (point - start).LengthSquared() / (end - start).LengthSquared();
        }

        /// <summary>
        /// Returns the coefficient of a simplex.
        /// </summary>
        /// <remarks>Used by method <c>CalculateSimplicalChain()</c>.</remarks>
        private static float CalculateSimplexCoefficient(Vector2 a, Vector2 b, Vector2 c)
        {
            float isLeft = MathUtils.Area(ref a, ref b, ref c);
            if (isLeft < 0f)
            {
                return -1f;
            }

            if (isLeft > 0f)
            {
                return 1f;
            }

            return 0f;
        }

        /// <summary>
        /// Winding number test for a point in a simplex.
        /// </summary>
        /// <param name="point">The point to be tested.</param>
        /// <param name="edge">The edge that the point is tested against.</param>
        /// <returns>False if the winding number is even and the point is outside
        /// the simplex and True otherwise.</returns>
        private static bool PointInSimplex(Vector2 point, Edge edge)
        {
            Vertices polygon = new Vertices();
            polygon.Add(Vector2.Zero);
            polygon.Add(edge.EdgeStart);
            polygon.Add(edge.EdgeEnd);
            return (polygon.PointInPolygon(ref point) == 1);
        }

        /// <summary>
        /// Tests if a point lies on a line segment.
        /// </summary>
        /// <remarks>Used by method <c>CalculateBeta()</c>.</remarks>
        private static bool PointOnLineSegment(Vector2 start, Vector2 end, Vector2 point)
        {
            Vector2 segment = end - start;
            return MathUtils.Area(ref start, ref end, ref point) == 0f &&
                   Vector2.Dot(point - start, segment) >= 0f &&
                   Vector2.Dot(point - end, segment) <= 0f;
        }

        private static bool VectorEqual(Vector2 vec1, Vector2 vec2)
        {
            return (vec2 - vec1).LengthSquared() <= ClipperEpsilonSquared;
        }

        #region Nested type: Edge

        /// <summary>Specifies an Edge. Edges are used to represent simplicies in simplical chains</summary>
        private sealed class Edge
        {
            public Edge(Vector2 edgeStart, Vector2 edgeEnd)
            {
                EdgeStart = edgeStart;
                EdgeEnd = edgeEnd;
            }

            public Vector2 EdgeStart { get; private set; }
            public Vector2 EdgeEnd { get; private set; }

            public Vector2 GetCenter()
            {
                return (EdgeStart + EdgeEnd) / 2f;
            }

            public static Edge operator -(Edge e)
            {
                return new Edge(e.EdgeEnd, e.EdgeStart);
            }

            public override bool Equals(Object obj)
            {
                // If parameter is null return false.
                if (obj == null)
                {
                    return false;
                }

                // If parameter cannot be cast to Point return false.
                return Equals(obj as Edge);
            }

            public bool Equals(Edge e)
            {
                // If parameter is null return false:
                if (e == null)
                {
                    return false;
                }

                // Return true if the fields match
                return VectorEqual(EdgeStart, e.EdgeStart) && VectorEqual(EdgeEnd, e.EdgeEnd);
            }

            public override int GetHashCode()
            {
                return EdgeStart.GetHashCode() ^ EdgeEnd.GetHashCode();
            }
        }

        #endregion
    }
}