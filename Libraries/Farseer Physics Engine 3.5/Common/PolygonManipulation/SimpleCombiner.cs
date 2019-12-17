/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

/*
* C# Version Ported by Matt Bettcher and Ian Qvist 2009-2010
* 
* Original C++ Version Copyright (c) 2007 Eric Jordan
*
* This software is provided 'as-is', without any express or implied
* warranty.  In no event will the authors be held liable for any damages
* arising from the use of this software.
* Permission is granted to anyone to use this software for any purpose,
* including commercial applications, and to alter it and redistribute it
* freely, subject to the following restrictions:
* 1. The origin of this software must not be misrepresented; you must not
* claim that you wrote the original software. If you use this software
* in a product, an acknowledgment in the product documentation would be
* appreciated but is not required.
* 2. Altered source versions must be plainly marked as such, and must not be
* misrepresented as being the original software.
* 3. This notice may not be removed or altered from any source distribution.
*/

using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Common.PolygonManipulation
{
    /// <summary>
    /// Combines a list of triangles into a list of convex polygons.
    /// Starts with a seed triangle, keep adding triangles to it until you can't add any more without making the polygon non-convex.
    /// </summary>
    public static class SimpleCombiner
    {
        /// <summary>
        /// Combine a list of triangles into a list of convex polygons.
        /// 
        /// Note: This only works on triangles.
        /// </summary>
        ///<param name="triangles">The triangles.</param>
        ///<param name="maxPolys">The maximun number of polygons to return.</param>
        ///<param name="tolerance">The tolerance</param>
        public static List<Vertices> PolygonizeTriangles(List<Vertices> triangles, int maxPolys = int.MaxValue, float tolerance = 0.001f)
        {
            if (triangles.Count <= 0)
                return triangles;

            List<Vertices> polys = new List<Vertices>();

            bool[] covered = new bool[triangles.Count];
            for (int i = 0; i < triangles.Count; ++i)
            {
                covered[i] = false;

                //Check here for degenerate triangles
                Vertices triangle = triangles[i];
                Vector2 a = triangle[0];
                Vector2 b = triangle[1];
                Vector2 c = triangle[2];

                if ((a.X == b.X && a.Y == b.Y) || (b.X == c.X && b.Y == c.Y) || (a.X == c.X && a.Y == c.Y))
                    covered[i] = true;
            }

            int polyIndex = 0;

            bool notDone = true;
            while (notDone)
            {
                int currTri = -1;
                for (int i = 0; i < triangles.Count; ++i)
                {
                    if (covered[i])
                        continue;

                    currTri = i;
                    break;
                }

                if (currTri == -1)
                {
                    notDone = false;
                }
                else
                {
                    Vertices poly = new Vertices(3);

                    for (int i = 0; i < 3; i++)
                    {
                        poly.Add(triangles[currTri][i]);
                    }

                    covered[currTri] = true;
                    int index = 0;
                    for (int i = 0; i < 2 * triangles.Count; ++i, ++index)
                    {
                        while (index >= triangles.Count) index -= triangles.Count;
                        if (covered[index])
                        {
                            continue;
                        }
                        Vertices newP = AddTriangle(triangles[index], poly);
                        if (newP == null)
                            continue; // is this right

                        if (newP.Count > Settings.MaxPolygonVertices)
                            continue;

                        if (newP.IsConvex())
                        {
                            //Or should it be IsUsable?  Maybe re-write IsConvex to apply the angle threshold from Box2d
                            poly = new Vertices(newP);
                            covered[index] = true;
                        }
                    }

                    //We have a maximum of polygons that we need to keep under.
                    if (polyIndex < maxPolys)
                    {
                        SimplifyTools.MergeParallelEdges(poly, tolerance);

                        //If identical points are present, a triangle gets
                        //borked by the MergeParallelEdges function, hence
                        //the vertex number check
                        if (poly.Count >= 3)
                            polys.Add(new Vertices(poly));
                        else
                            Debug.WriteLine("Skipping corrupt poly.");
                    }

                    if (poly.Count >= 3)
                        polyIndex++; //Must be outside (polyIndex < polysLength) test
                }
            }

            //TODO: Add sanity check
            //Remove empty vertice collections
            for (int i = polys.Count - 1; i >= 0; i--)
            {
                if (polys[i].Count == 0)
                    polys.RemoveAt(i);
            }

            return polys;
        }

        private static Vertices AddTriangle(Vertices t, Vertices vertices)
        {
            // First, find vertices that connect
            int firstP = -1;
            int firstT = -1;
            int secondP = -1;
            int secondT = -1;
            for (int i = 0; i < vertices.Count; i++)
            {
                if (t[0].X == vertices[i].X && t[0].Y == vertices[i].Y)
                {
                    if (firstP == -1)
                    {
                        firstP = i;
                        firstT = 0;
                    }
                    else
                    {
                        secondP = i;
                        secondT = 0;
                    }
                }
                else if (t[1].X == vertices[i].X && t[1].Y == vertices[i].Y)
                {
                    if (firstP == -1)
                    {
                        firstP = i;
                        firstT = 1;
                    }
                    else
                    {
                        secondP = i;
                        secondT = 1;
                    }
                }
                else if (t[2].X == vertices[i].X && t[2].Y == vertices[i].Y)
                {
                    if (firstP == -1)
                    {
                        firstP = i;
                        firstT = 2;
                    }
                    else
                    {
                        secondP = i;
                        secondT = 2;
                    }
                }
            }
            // Fix ordering if first should be last vertex of poly
            if (firstP == 0 && secondP == vertices.Count - 1)
            {
                firstP = vertices.Count - 1;
                secondP = 0;
            }

            // Didn't find it
            if (secondP == -1)
            {
                return null;
            }

            // Find tip index on triangle
            int tipT = 0;
            if (tipT == firstT || tipT == secondT)
                tipT = 1;
            if (tipT == firstT || tipT == secondT)
                tipT = 2;

            Vertices result = new Vertices(vertices.Count + 1);
            for (int i = 0; i < vertices.Count; i++)
            {
                result.Add(vertices[i]);

                if (i == firstP)
                    result.Add(t[tipT]);
            }

            return result;
        }
    }
}