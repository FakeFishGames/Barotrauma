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
    /// <summary>
    /// Provides a set of tools to simplify polygons in various ways.
    /// </summary>
    public static class SimplifyTools
    {
        /// <summary>
        /// Removes all collinear points on the polygon.
        /// </summary>
        /// <param name="vertices">The polygon that needs simplification.</param>
        /// <param name="collinearityTolerance">The collinearity tolerance.</param>
        /// <returns>A simplified polygon.</returns>
        public static Vertices CollinearSimplify(Vertices vertices, float collinearityTolerance = 0)
        {
            if (vertices.Count <= 3)
                return vertices;

            Vertices simplified = new Vertices(vertices.Count);

            for (int i = 0; i < vertices.Count; i++)
            {
                Vector2 prev = vertices.PreviousVertex(i);
                Vector2 current = vertices[i];
                Vector2 next = vertices.NextVertex(i);

                //If they collinear, continue
                if (MathUtils.IsCollinear(ref prev, ref current, ref next, collinearityTolerance))
                    continue;

                simplified.Add(current);
            }

            return simplified;
        }

        /// <summary>
        /// Ramer-Douglas-Peucker polygon simplification algorithm. This is the general recursive version that does not use the
        /// speed-up technique by using the Melkman convex hull.
        /// 
        /// If you pass in 0, it will remove all collinear points.
        /// </summary>
        /// <returns>The simplified polygon</returns>
        public static Vertices DouglasPeuckerSimplify(Vertices vertices, float distanceTolerance)
        {
            if (vertices.Count <= 3)
                return vertices;

            bool[] usePoint = new bool[vertices.Count];

            for (int i = 0; i < vertices.Count; i++)
                usePoint[i] = true;

            SimplifySection(vertices, 0, vertices.Count - 1, usePoint, distanceTolerance);

            Vertices simplified = new Vertices(vertices.Count);

            for (int i = 0; i < vertices.Count; i++)
            {
                if (usePoint[i])
                    simplified.Add(vertices[i]);
            }

            return simplified;
        }

        private static void SimplifySection(Vertices vertices, int i, int j, bool[] usePoint, float distanceTolerance)
        {
            if ((i + 1) == j)
                return;

            Vector2 a = vertices[i];
            Vector2 b = vertices[j];

            double maxDistance = -1.0;
            int maxIndex = i;
            for (int k = i + 1; k < j; k++)
            {
                Vector2 point = vertices[k];

                double distance = LineTools.DistanceBetweenPointAndLineSegment(ref point, ref a, ref b);

                if (distance > maxDistance)
                {
                    maxDistance = distance;
                    maxIndex = k;
                }
            }

            if (maxDistance <= distanceTolerance)
            {
                for (int k = i + 1; k < j; k++)
                {
                    usePoint[k] = false;
                }
            }
            else
            {
                SimplifySection(vertices, i, maxIndex, usePoint, distanceTolerance);
                SimplifySection(vertices, maxIndex, j, usePoint, distanceTolerance);
            }
        }

        /// <summary>
        /// Merges all parallel edges in the list of vertices
        /// </summary>
        /// <param name="vertices">The vertices.</param>
        /// <param name="tolerance">The tolerance.</param>
        public static Vertices MergeParallelEdges(Vertices vertices, float tolerance)
        {
            //From Eric Jordan's convex decomposition library

            if (vertices.Count <= 3)
                return vertices; //Can't do anything useful here to a triangle

            bool[] mergeMe = new bool[vertices.Count];
            int newNVertices = vertices.Count;

            //Gather points to process
            for (int i = 0; i < vertices.Count; ++i)
            {
                int lower = (i == 0) ? (vertices.Count - 1) : (i - 1);
                int middle = i;
                int upper = (i == vertices.Count - 1) ? (0) : (i + 1);

                float dx0 = vertices[middle].X - vertices[lower].X;
                float dy0 = vertices[middle].Y - vertices[lower].Y;
                float dx1 = vertices[upper].Y - vertices[middle].X;
                float dy1 = vertices[upper].Y - vertices[middle].Y;
                float norm0 = (float)Math.Sqrt(dx0 * dx0 + dy0 * dy0);
                float norm1 = (float)Math.Sqrt(dx1 * dx1 + dy1 * dy1);

                if (!(norm0 > 0.0f && norm1 > 0.0f) && newNVertices > 3)
                {
                    //Merge identical points
                    mergeMe[i] = true;
                    --newNVertices;
                }

                dx0 /= norm0;
                dy0 /= norm0;
                dx1 /= norm1;
                dy1 /= norm1;
                float cross = dx0 * dy1 - dx1 * dy0;
                float dot = dx0 * dx1 + dy0 * dy1;

                if (Math.Abs(cross) < tolerance && dot > 0 && newNVertices > 3)
                {
                    mergeMe[i] = true;
                    --newNVertices;
                }
                else
                    mergeMe[i] = false;
            }

            if (newNVertices == vertices.Count || newNVertices == 0)
                return vertices;

            int currIndex = 0;

            //Copy the vertices to a new list and clear the old
            Vertices newVertices = new Vertices(newNVertices);

            for (int i = 0; i < vertices.Count; ++i)
            {
                if (mergeMe[i] || newNVertices == 0 || currIndex == newNVertices)
                    continue;

                Debug.Assert(currIndex < newNVertices);

                newVertices.Add(vertices[i]);
                ++currIndex;
            }

            return newVertices;
        }

        /// <summary>
        /// Merges the identical points in the polygon.
        /// </summary>
        /// <param name="vertices">The vertices.</param>
        public static Vertices MergeIdenticalPoints(Vertices vertices)
        {
            HashSet<Vector2> unique = new HashSet<Vector2>();

            foreach (Vector2 vertex in vertices)
            {
                unique.Add(vertex);
            }

            return new Vertices(unique);
        }

        /// <summary>
        /// Reduces the polygon by distance.
        /// </summary>
        /// <param name="vertices">The vertices.</param>
        /// <param name="distance">The distance between points. Points closer than this will be removed.</param>
        public static Vertices ReduceByDistance(Vertices vertices, float distance)
        {
            if (vertices.Count <= 3)
                return vertices;

            float distance2 = distance * distance;

            Vertices simplified = new Vertices(vertices.Count);

            for (int i = 0; i < vertices.Count; i++)
            {
                Vector2 current = vertices[i];
                Vector2 next = vertices.NextVertex(i);

                //If they are closer than the distance, continue
                if ((next - current).LengthSquared() <= distance2)
                    continue;

                simplified.Add(current);
            }

            return simplified;
        }

        /// <summary>
        /// Reduces the polygon by removing the Nth vertex in the vertices list.
        /// </summary>
        /// <param name="vertices">The vertices.</param>
        /// <param name="nth">The Nth point to remove. Example: 5.</param>
        /// <returns></returns>
        public static Vertices ReduceByNth(Vertices vertices, int nth)
        {
            if (vertices.Count <= 3)
                return vertices;

            if (nth == 0)
                return vertices;

            Vertices simplified = new Vertices(vertices.Count);

            for (int i = 0; i < vertices.Count; i++)
            {
                if (i % nth == 0)
                    continue;

                simplified.Add(vertices[i]);
            }

            return simplified;
        }

        /// <summary>
        /// Simplify the polygon by removing all points that in pairs of 3 have an area less than the tolerance.
        /// 
        /// Pass in 0 as tolerance, and it will only remove collinear points.
        /// </summary>
        /// <param name="vertices"></param>
        /// <param name="areaTolerance"></param>
        /// <returns></returns>
        public static Vertices ReduceByArea(Vertices vertices, float areaTolerance)
        {
            //From physics2d.net

            if (vertices.Count <= 3)
                return vertices;

            if (areaTolerance < 0)
                throw new ArgumentOutOfRangeException("areaTolerance", "must be equal to or greater than zero.");

            Vertices simplified = new Vertices(vertices.Count);
            Vector2 v3;
            Vector2 v1 = vertices[vertices.Count - 2];
            Vector2 v2 = vertices[vertices.Count - 1];
            areaTolerance *= 2;

            for (int i = 0; i < vertices.Count; ++i, v2 = v3)
            {
                v3 = i == vertices.Count - 1 ? simplified[0] : vertices[i];

                float old1;
                MathUtils.Cross(ref v1, ref v2, out old1);

                float old2;
                MathUtils.Cross(ref v2, ref v3, out old2);

                float new1;
                MathUtils.Cross(ref v1, ref v3, out new1);

                if (Math.Abs(new1 - (old1 + old2)) > areaTolerance)
                {
                    simplified.Add(v2);
                    v1 = v2;
                }
            }

            return simplified;
        }
    }
}