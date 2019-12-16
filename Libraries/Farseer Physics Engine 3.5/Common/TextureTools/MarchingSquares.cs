/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using System.Collections.Generic;
using FarseerPhysics.Collision;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Common.TextureTools
{
    // Ported by Matthew Bettcher - Feb 2011

    /*
    Copyright (c) 2010, Luca Deltodesco
    All rights reserved.

    Redistribution and use in source and binary forms, with or without modification, are permitted
    provided that the following conditions are met:

        * Redistributions of source code must retain the above copyright notice, this list of conditions
	      and the following disclaimer.
        * Redistributions in binary form must reproduce the above copyright notice, this list of
	      conditions and the following disclaimer in the documentation and/or other materials provided
	      with the distribution.
        * Neither the name of the nape project nor the names of its contributors may be used to endorse
	     or promote products derived from this software without specific prior written permission.

    THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR
    IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
    FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR
    CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
    DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
    DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER
    IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT
    OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
    */

    public static class MarchingSquares
    {
        /// <summary>
        /// Marching squares over the given domain using the mesh defined via the dimensions
        ///    (wid,hei) to build a set of polygons such that f(x,y) less than 0, using the given number
        ///    'bin' for recursive linear inteprolation along cell boundaries.
        ///
        ///    if 'comb' is true, then the polygons will also be composited into larger possible concave
        ///    polygons.
        /// </summary>
        /// <param name="domain"></param>
        /// <param name="cellWidth"></param>
        /// <param name="cellHeight"></param>
        /// <param name="f"></param>
        /// <param name="lerpCount"></param>
        /// <param name="combine"></param>
        /// <returns></returns>
        public static List<Vertices> DetectSquares(AABB domain, float cellWidth, float cellHeight, sbyte[,] f,
                                                   int lerpCount, bool combine)
        {
            CxFastList<GeomPoly> ret = new CxFastList<GeomPoly>();

            List<Vertices> verticesList = new List<Vertices>();

            //NOTE: removed assignments as they were not used.
            List<GeomPoly> polyList;
            GeomPoly gp;

            int xn = (int)(domain.Extents.X * 2 / cellWidth);
            bool xp = xn == (domain.Extents.X * 2 / cellWidth);
            int yn = (int)(domain.Extents.Y * 2 / cellHeight);
            bool yp = yn == (domain.Extents.Y * 2 / cellHeight);
            if (!xp) xn++;
            if (!yp) yn++;

            sbyte[,] fs = new sbyte[xn + 1, yn + 1];
            GeomPolyVal[,] ps = new GeomPolyVal[xn + 1, yn + 1];

            //populate shared function lookups.
            for (int x = 0; x < xn + 1; x++)
            {
                int x0;
                if (x == xn) x0 = (int)domain.UpperBound.X;
                else x0 = (int)(x * cellWidth + domain.LowerBound.X);
                for (int y = 0; y < yn + 1; y++)
                {
                    int y0;
                    if (y == yn) y0 = (int)domain.UpperBound.Y;
                    else y0 = (int)(y * cellHeight + domain.LowerBound.Y);
                    fs[x, y] = f[x0, y0];
                }
            }

            //generate sub-polys and combine to scan lines
            for (int y = 0; y < yn; y++)
            {
                float y0 = y * cellHeight + domain.LowerBound.Y;
                float y1;
                if (y == yn - 1) y1 = domain.UpperBound.Y;
                else y1 = y0 + cellHeight;
                GeomPoly pre = null;
                for (int x = 0; x < xn; x++)
                {
                    float x0 = x * cellWidth + domain.LowerBound.X;
                    float x1;
                    if (x == xn - 1) x1 = domain.UpperBound.X;
                    else x1 = x0 + cellWidth;

                    gp = new GeomPoly();

                    int key = MarchSquare(f, fs, ref gp, x, y, x0, y0, x1, y1, lerpCount);
                    if (gp.Length != 0)
                    {
                        if (combine && pre != null && (key & 9) != 0)
                        {
                            combLeft(ref pre, ref gp);
                            gp = pre;
                        }
                        else
                            ret.Add(gp);
                        ps[x, y] = new GeomPolyVal(gp, key);
                    }
                    else
                        gp = null;
                    pre = gp;
                }
            }
            if (!combine)
            {
                polyList = ret.GetListOfElements();

                foreach (GeomPoly poly in polyList)
                {
                    verticesList.Add(new Vertices(poly.Points.GetListOfElements()));
                }

                return verticesList;
            }

            //combine scan lines together
            for (int y = 1; y < yn; y++)
            {
                int x = 0;
                while (x < xn)
                {
                    GeomPolyVal p = ps[x, y];

                    //skip along scan line if no polygon exists at this point
                    if (p == null)
                    {
                        x++;
                        continue;
                    }

                    //skip along if current polygon cannot be combined above.
                    if ((p.Key & 12) == 0)
                    {
                        x++;
                        continue;
                    }

                    //skip along if no polygon exists above.
                    GeomPolyVal u = ps[x, y - 1];
                    if (u == null)
                    {
                        x++;
                        continue;
                    }

                    //skip along if polygon above cannot be combined with.
                    if ((u.Key & 3) == 0)
                    {
                        x++;
                        continue;
                    }

                    float ax = x * cellWidth + domain.LowerBound.X;
                    float ay = y * cellHeight + domain.LowerBound.Y;

                    CxFastList<Vector2> bp = p.GeomP.Points;
                    CxFastList<Vector2> ap = u.GeomP.Points;

                    //skip if it's already been combined with above polygon
                    if (u.GeomP == p.GeomP)
                    {
                        x++;
                        continue;
                    }

                    //combine above (but disallow the hole thingies
                    CxFastListNode<Vector2> bi = bp.Begin();
                    while (Square(bi.Elem().Y - ay) > Settings.Epsilon || bi.Elem().X < ax) bi = bi.Next();

                    //NOTE: Unused
                    //Vector2 b0 = bi.elem();
                    Vector2 b1 = bi.Next().Elem();
                    if (Square(b1.Y - ay) > Settings.Epsilon)
                    {
                        x++;
                        continue;
                    }

                    bool brk = true;
                    CxFastListNode<Vector2> ai = ap.Begin();
                    while (ai != ap.End())
                    {
                        if (VecDsq(ai.Elem(), b1) < Settings.Epsilon)
                        {
                            brk = false;
                            break;
                        }
                        ai = ai.Next();
                    }
                    if (brk)
                    {
                        x++;
                        continue;
                    }

                    CxFastListNode<Vector2> bj = bi.Next().Next();
                    if (bj == bp.End()) bj = bp.Begin();
                    while (bj != bi)
                    {
                        ai = ap.Insert(ai, bj.Elem()); // .clone()
                        bj = bj.Next();
                        if (bj == bp.End()) bj = bp.Begin();
                        u.GeomP.Length++;
                    }
                    //u.p.simplify(float.Epsilon,float.Epsilon);
                    //
                    ax = x + 1;
                    while (ax < xn)
                    {
                        GeomPolyVal p2 = ps[(int)ax, y];
                        if (p2 == null || p2.GeomP != p.GeomP)
                        {
                            ax++;
                            continue;
                        }
                        p2.GeomP = u.GeomP;
                        ax++;
                    }
                    ax = x - 1;
                    while (ax >= 0)
                    {
                        GeomPolyVal p2 = ps[(int)ax, y];
                        if (p2 == null || p2.GeomP != p.GeomP)
                        {
                            ax--;
                            continue;
                        }
                        p2.GeomP = u.GeomP;
                        ax--;
                    }
                    ret.Remove(p.GeomP);
                    p.GeomP = u.GeomP;

                    x = (int)((bi.Next().Elem().X - domain.LowerBound.X) / cellWidth) + 1;
                    //x++; this was already commented out!
                }
            }

            polyList = ret.GetListOfElements();

            foreach (GeomPoly poly in polyList)
            {
                verticesList.Add(new Vertices(poly.Points.GetListOfElements()));
            }

            return verticesList;
        }

        #region Private Methods

        //- - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - 

        /** Linearly interpolate between (x0 to x1) given a value at these coordinates (v0 and v1)
            such as to approximate value(return) = 0
        **/

        private static int[] _lookMarch = {
                                              0x00, 0xE0, 0x38, 0xD8, 0x0E, 0xEE, 0x36, 0xD6, 0x83, 0x63, 0xBB, 0x5B, 0x8D,
                                              0x6D, 0xB5, 0x55
                                          };

        private static float Lerp(float x0, float x1, float v0, float v1)
        {
            float dv = v0 - v1;
            float t;
            if (dv * dv < Settings.Epsilon)
                t = 0.5f;
            else t = v0 / dv;
            return x0 + t * (x1 - x0);
        }

        //- - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - 

        /** Recursive linear interpolation for use in marching squares **/

        private static float Xlerp(float x0, float x1, float y, float v0, float v1, sbyte[,] f, int c)
        {
            float xm = Lerp(x0, x1, v0, v1);
            if (c == 0)
                return xm;

            sbyte vm = f[(int)xm, (int)y];

            if (v0 * vm < 0)
                return Xlerp(x0, xm, y, v0, vm, f, c - 1);

            return Xlerp(xm, x1, y, vm, v1, f, c - 1);
        }

        /** Recursive linear interpolation for use in marching squares **/

        private static float Ylerp(float y0, float y1, float x, float v0, float v1, sbyte[,] f, int c)
        {
            float ym = Lerp(y0, y1, v0, v1);
            if (c == 0)
                return ym;

            sbyte vm = f[(int)x, (int)ym];

            if (v0 * vm < 0)
                return Ylerp(y0, ym, x, v0, vm, f, c - 1);

            return Ylerp(ym, y1, x, vm, v1, f, c - 1);
        }

        /** Square value for use in marching squares **/

        private static float Square(float x)
        {
            return x * x;
        }

        private static float VecDsq(Vector2 a, Vector2 b)
        {
            Vector2 d = a - b;
            return d.X * d.X + d.Y * d.Y;
        }

        private static float VecCross(Vector2 a, Vector2 b)
        {
            return a.X * b.Y - a.Y * b.X;
        }

        /** Look-up table to relate polygon key with the vertices that should be used for
            the sub polygon in marching squares
        **/

        /** Perform a single celled marching square for for the given cell defined by (x0,y0) (x1,y1)
            using the function f for recursive interpolation, given the look-up table 'fs' of
            the values of 'f' at cell vertices with the result to be stored in 'poly' given the actual
            coordinates of 'ax' 'ay' in the marching squares mesh.
        **/

        private static int MarchSquare(sbyte[,] f, sbyte[,] fs, ref GeomPoly poly, int ax, int ay, float x0, float y0,
                                       float x1, float y1, int bin)
        {
            //key lookup
            int key = 0;
            sbyte v0 = fs[ax, ay];
            if (v0 < 0) key |= 8;
            sbyte v1 = fs[ax + 1, ay];
            if (v1 < 0) key |= 4;
            sbyte v2 = fs[ax + 1, ay + 1];
            if (v2 < 0) key |= 2;
            sbyte v3 = fs[ax, ay + 1];
            if (v3 < 0) key |= 1;

            int val = _lookMarch[key];
            if (val != 0)
            {
                CxFastListNode<Vector2> pi = null;
                for (int i = 0; i < 8; i++)
                {
                    Vector2 p;
                    if ((val & (1 << i)) != 0)
                    {
                        if (i == 7 && (val & 1) == 0)
                            poly.Points.Add(p = new Vector2(x0, Ylerp(y0, y1, x0, v0, v3, f, bin)));
                        else
                        {
                            if (i == 0) p = new Vector2(x0, y0);
                            else if (i == 2) p = new Vector2(x1, y0);
                            else if (i == 4) p = new Vector2(x1, y1);
                            else if (i == 6) p = new Vector2(x0, y1);

                            else if (i == 1) p = new Vector2(Xlerp(x0, x1, y0, v0, v1, f, bin), y0);
                            else if (i == 5) p = new Vector2(Xlerp(x0, x1, y1, v3, v2, f, bin), y1);

                            else if (i == 3) p = new Vector2(x1, Ylerp(y0, y1, x1, v1, v2, f, bin));
                            else p = new Vector2(x0, Ylerp(y0, y1, x0, v0, v3, f, bin));

                            pi = poly.Points.Insert(pi, p);
                        }
                        poly.Length++;
                    }
                }
                //poly.simplify(float.Epsilon,float.Epsilon);
            }
            return key;
        }

        /** Used in polygon composition to composit polygons into scan lines
            Combining polya and polyb into one super-polygon stored in polya.
        **/

        private static void combLeft(ref GeomPoly polya, ref GeomPoly polyb)
        {
            CxFastList<Vector2> ap = polya.Points;
            CxFastList<Vector2> bp = polyb.Points;
            CxFastListNode<Vector2> ai = ap.Begin();
            CxFastListNode<Vector2> bi = bp.Begin();

            Vector2 b = bi.Elem();
            CxFastListNode<Vector2> prea = null;
            while (ai != ap.End())
            {
                Vector2 a = ai.Elem();
                if (VecDsq(a, b) < Settings.Epsilon)
                {
                    //ignore shared vertex if parallel
                    if (prea != null)
                    {
                        Vector2 a0 = prea.Elem();
                        b = bi.Next().Elem();

                        Vector2 u = a - a0;
                        //vec_new(u); vec_sub(a.p.p, a0.p.p, u);
                        Vector2 v = b - a;
                        //vec_new(v); vec_sub(b.p.p, a.p.p, v);
                        float dot = VecCross(u, v);
                        if (dot * dot < Settings.Epsilon)
                        {
                            ap.Erase(prea, ai);
                            polya.Length--;
                            ai = prea;
                        }
                    }

                    //insert polyb into polya
                    bool fst = true;
                    CxFastListNode<Vector2> preb = null;
                    while (!bp.Empty())
                    {
                        Vector2 bb = bp.Front();
                        bp.Pop();
                        if (!fst && !bp.Empty())
                        {
                            ai = ap.Insert(ai, bb);
                            polya.Length++;
                            preb = ai;
                        }
                        fst = false;
                    }

                    //ignore shared vertex if parallel
                    ai = ai.Next();
                    Vector2 a1 = ai.Elem();
                    ai = ai.Next();
                    if (ai == ap.End()) ai = ap.Begin();
                    Vector2 a2 = ai.Elem();
                    Vector2 a00 = preb.Elem();
                    Vector2 uu = a1 - a00;
                    //vec_new(u); vec_sub(a1.p, a0.p, u);
                    Vector2 vv = a2 - a1;
                    //vec_new(v); vec_sub(a2.p, a1.p, v);
                    float dot1 = VecCross(uu, vv);
                    if (dot1 * dot1 < Settings.Epsilon)
                    {
                        ap.Erase(preb, preb.Next());
                        polya.Length--;
                    }

                    return;
                }
                prea = ai;
                ai = ai.Next();
            }
        }

        #endregion

        #region CxFastList from nape physics

        #region Nested type: CxFastList

        /// <summary>
        /// Designed as a complete port of CxFastList from CxStd.
        /// </summary>
        internal class CxFastList<T>
        {
            // first node in the list
            private CxFastListNode<T> _head;
            private int _count;

            /// <summary>
            /// Iterator to start of list (O(1))
            /// </summary>
            public CxFastListNode<T> Begin()
            {
                return _head;
            }

            /// <summary>
            /// Iterator to end of list (O(1))
            /// </summary>
            public CxFastListNode<T> End()
            {
                return null;
            }

            /// <summary>
            /// Returns first element of list (O(1))
            /// </summary>
            public T Front()
            {
                return _head.Elem();
            }

            /// <summary>
            /// add object to list (O(1))
            /// </summary>
            public CxFastListNode<T> Add(T value)
            {
                CxFastListNode<T> newNode = new CxFastListNode<T>(value);
                if (_head == null)
                {
                    newNode._next = null;
                    _head = newNode;
                    _count++;
                    return newNode;
                }
                newNode._next = _head;
                _head = newNode;

                _count++;

                return newNode;
            }

            /// <summary>
            /// remove object from list, returns true if an element was removed (O(n))
            /// </summary>
            public bool Remove(T value)
            {
                CxFastListNode<T> head = _head;
                CxFastListNode<T> prev = _head;

                EqualityComparer<T> comparer = EqualityComparer<T>.Default;

                if (head != null)
                {
                    if (value != null)
                    {
                        do
                        {
                            // if we are on the value to be removed
                            if (comparer.Equals(head._elt, value))
                            {
                                // then we need to patch the list
                                // check to see if we are removing the _head
                                if (head == _head)
                                {
                                    _head = head._next;
                                    _count--;
                                    return true;
                                }
                                else
                                {
                                    // were not at the head
                                    prev._next = head._next;
                                    _count--;
                                    return true;
                                }
                            }
                            // cache the current as the previous for the next go around
                            prev = head;
                            head = head._next;
                        } while (head != null);
                    }
                }
                return false;
            }

            /// <summary>
            /// pop element from head of list (O(1)) Note: this does not return the object popped! 
            /// There is good reason to this, and it regards the Alloc list variants which guarantee 
            /// objects are released to the object pool. You do not want to retrieve an element 
            /// through pop or else that object may suddenly be used by another piece of code which 
            /// retrieves it from the object pool.
            /// </summary>
            public CxFastListNode<T> Pop()
            {
                return Erase(null, _head);
            }

            /// <summary>
            /// insert object after 'node' returning an iterator to the inserted object.
            /// </summary>
            public CxFastListNode<T> Insert(CxFastListNode<T> node, T value)
            {
                if (node == null)
                {
                    return Add(value);
                }
                CxFastListNode<T> newNode = new CxFastListNode<T>(value);
                CxFastListNode<T> nextNode = node._next;
                newNode._next = nextNode;
                node._next = newNode;

                _count++;

                return newNode;
            }

            /// <summary>
            /// removes the element pointed to by 'node' with 'prev' being the previous iterator, 
            /// returning an iterator to the element following that of 'node' (O(1))
            /// </summary>
            public CxFastListNode<T> Erase(CxFastListNode<T> prev, CxFastListNode<T> node)
            {
                // cache the node after the node to be removed
                CxFastListNode<T> nextNode = node._next;
                if (prev != null)
                    prev._next = nextNode;
                else if (_head != null)
                    _head = _head._next;
                else
                    return null;

                _count--;
                return nextNode;
            }

            /// <summary>
            /// whether the list is empty (O(1))
            /// </summary>
            public bool Empty()
            {
                if (_head == null)
                    return true;
                return false;
            }

            /// <summary>
            /// computes size of list (O(n))
            /// </summary>
            public int Size()
            {
                CxFastListNode<T> i = Begin();
                int count = 0;

                do
                {
                    count++;
                } while (i.Next() != null);

                return count;
            }

            /// <summary>
            /// empty the list (O(1) if CxMixList, O(n) otherwise)
            /// </summary>
            public void Clear()
            {
                CxFastListNode<T> head = _head;
                while (head != null)
                {
                    CxFastListNode<T> node2 = head;
                    head = head._next;
                    node2._next = null;
                }
                _head = null;
                _count = 0;
            }

            /// <summary>
            /// returns true if 'value' is an element of the list (O(n))
            /// </summary>
            public bool Has(T value)
            {
                return (Find(value) != null);
            }

            // Non CxFastList Methods 
            public CxFastListNode<T> Find(T value)
            {
                // start at head
                CxFastListNode<T> head = _head;
                EqualityComparer<T> comparer = EqualityComparer<T>.Default;
                if (head != null)
                {
                    if (value != null)
                    {
                        do
                        {
                            if (comparer.Equals(head._elt, value))
                            {
                                return head;
                            }
                            head = head._next;
                        } while (head != _head);
                    }
                    else
                    {
                        do
                        {
                            if (head._elt == null)
                            {
                                return head;
                            }
                            head = head._next;
                        } while (head != _head);
                    }
                }
                return null;
            }

            public List<T> GetListOfElements()
            {
                List<T> list = new List<T>();

                CxFastListNode<T> iter = Begin();

                if (iter != null)
                {
                    do
                    {
                        list.Add(iter._elt);
                        iter = iter._next;
                    } while (iter != null);
                }
                return list;
            }
        }

        #endregion

        #region Nested type: CxFastListNode

        internal class CxFastListNode<T>
        {
            internal T _elt;
            internal CxFastListNode<T> _next;

            public CxFastListNode(T obj)
            {
                _elt = obj;
            }

            public T Elem()
            {
                return _elt;
            }

            public CxFastListNode<T> Next()
            {
                return _next;
            }
        }

        #endregion

        #endregion

        #region Internal Stuff

        #region Nested type: GeomPoly

        internal class GeomPoly
        {
            public int Length;
            public CxFastList<Vector2> Points;

            public GeomPoly()
            {
                Points = new CxFastList<Vector2>();
                Length = 0;
            }
        }

        #endregion

        #region Nested type: GeomPolyVal

        private class GeomPolyVal
        {
            /** Associated polygon at coordinate **/
            /** Key of original sub-polygon **/
            public int Key;
            public GeomPoly GeomP;

            public GeomPolyVal(GeomPoly geomP, int K)
            {
                GeomP = geomP;
                Key = K;
            }
        }

        #endregion

        #endregion
    }
}