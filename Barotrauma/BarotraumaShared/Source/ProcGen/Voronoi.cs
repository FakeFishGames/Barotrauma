/*
 * Created by SharpDevelop.
 * User: Burhan
 * Date: 17/06/2014
 * Time: 11:30 م
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */

/*
* The author of this software is Steven Fortune.  Copyright (c) 1994 by AT&T
* Bell Laboratories.
* Permission to use, copy, modify, and distribute this software for any
* purpose without fee is hereby granted, provided that this entire notice
* is included in all copies of any software which is or includes a copy
* or modification of this software and in all copies of the supporting
* documentation for such software.
* THIS SOFTWARE IS BEING PROVIDED "AS IS", WITHOUT ANY EXPRESS OR IMPLIED
* WARRANTY.  IN PARTICULAR, NEITHER THE AUTHORS NOR AT&T MAKE ANY
* REPRESENTATION OR WARRANTY OF ANY KIND CONCERNING THE MERCHANTABILITY
* OF THIS SOFTWARE OR ITS FITNESS FOR ANY PARTICULAR PURPOSE.
*/

/* 
 * This code was originally written by Stephan Fortune in C code.  I, Shane O'Sullivan,
 * have since modified it, encapsulating it in a C++ class and, fixing memory leaks and
 * adding accessors to the Voronoi Edges.
 * Permission to use, copy, modify, and distribute this software for any
 * purpose without fee is hereby granted, provided that this entire notice
 * is included in all copies of any software which is or includes a copy
 * or modification of this software and in all copies of the supporting
 * documentation for such software.
 * THIS SOFTWARE IS BEING PROVIDED "AS IS", WITHOUT ANY EXPRESS OR IMPLIED
 * WARRANTY.  IN PARTICULAR, NEITHER THE AUTHORS NOR AT&T MAKE ANY
 * REPRESENTATION OR WARRANTY OF ANY KIND CONCERNING THE MERCHANTABILITY
 * OF THIS SOFTWARE OR ITS FITNESS FOR ANY PARTICULAR PURPOSE.
 */

/* 
 * Java Version by Zhenyu Pan
 * Permission to use, copy, modify, and distribute this software for any
 * purpose without fee is hereby granted, provided that this entire notice
 * is included in all copies of any software which is or includes a copy
 * or modification of this software and in all copies of the supporting
 * documentation for such software.
 * THIS SOFTWARE IS BEING PROVIDED "AS IS", WITHOUT ANY EXPRESS OR IMPLIED
 * WARRANTY.  IN PARTICULAR, NEITHER THE AUTHORS NOR AT&T MAKE ANY
 * REPRESENTATION OR WARRANTY OF ANY KIND CONCERNING THE MERCHANTABILITY
 * OF THIS SOFTWARE OR ITS FITNESS FOR ANY PARTICULAR PURPOSE.
 */

/*
* C# Version by Burhan Joukhadar
* 
* Permission to use, copy, modify, and distribute this software for any
* purpose without fee is hereby granted, provided that this entire notice
* is included in all copies of any software which is or includes a copy
* or modification of this software and in all copies of the supporting
* documentation for such software.
* THIS SOFTWARE IS BEING PROVIDED "AS IS", WITHOUT ANY EXPRESS OR IMPLIED
* WARRANTY.  IN PARTICULAR, NEITHER THE AUTHORS NOR AT&T MAKE ANY
* REPRESENTATION OR WARRANTY OF ANY KIND CONCERNING THE MERCHANTABILITY
* OF THIS SOFTWARE OR ITS FITNESS FOR ANY PARTICULAR PURPOSE.
*/

using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace Voronoi2
{
    /// <summary>
    /// Description of Voronoi.
    /// </summary>
    public class Voronoi
    {
        // ************* Private members ******************
        double borderMinX, borderMaxX, borderMinY, borderMaxY;
        int siteidx;
        double xmin, xmax, ymin, ymax, deltax, deltay;
        int nvertices;
        int nedges;
        int nsites;
        Site[] sites;
        Site bottomsite;
        int sqrt_nsites;
        double minDistanceBetweenSites;
        int PQcount;
        int PQmin;
        int PQhashsize;
        Halfedge[] PQhash;
        
        const int LE = 0;
        const int RE = 1;

        int ELhashsize;
        Halfedge[] ELhash;
        Halfedge ELleftend, ELrightend;
        List<GraphEdge> allEdges;
        
        
        // ************* Public methods ******************
        // ******************************************
        
        // constructor
        public Voronoi ( double minDistanceBetweenSites )
        {
            siteidx = 0;
            sites = null;
            
            allEdges = null;
            this.minDistanceBetweenSites = minDistanceBetweenSites;
        }
        
        /**
         * 
         * @param xValuesIn Array of X values for each site.
         * @param yValuesIn Array of Y values for each site. Must be identical length to yValuesIn
         * @param minX The minimum X of the bounding box around the voronoi
         * @param maxX The maximum X of the bounding box around the voronoi
         * @param minY The minimum Y of the bounding box around the voronoi
         * @param maxY The maximum Y of the bounding box around the voronoi
         * @return
         */
        // تستدعى هذه العملية لإنشاء مخطط فورونوي
        public List<GraphEdge> generateVoronoi ( double[] xValuesIn, double[] yValuesIn, double minX, double maxX, double minY, double maxY )
        {
            sort(xValuesIn, yValuesIn, xValuesIn.Length);
            
            // Check bounding box inputs - if mins are bigger than maxes, swap them
            double temp = 0;
            if ( minX > maxX )
            {
                temp = minX;
                minX = maxX;
                maxX = temp;
            }
            if ( minY > maxY )
            {
                temp = minY;
                minY = maxY;
                maxY = temp;
            }
            
            borderMinX = minX;
            borderMinY = minY;
            borderMaxX = maxX;
            borderMaxY = maxY;
            
            siteidx = 0;
            voronoi_bd ();
            return allEdges;
        }
        
        
        /*********************************************************
         * Private methods - implementation details
         ********************************************************/
        
        private void sort ( double[] xValuesIn, double[] yValuesIn, int count )
        {
            sites = null;
            allEdges = new List<GraphEdge>();
            
            nsites = count;
            nvertices = 0;
            nedges = 0;
            
            double sn = (double)nsites + 4;
            sqrt_nsites = (int) Math.Sqrt ( sn );
            
            // Copy the inputs so we don't modify the originals
            double[] xValues = new double[count];
            double[] yValues = new double[count];
            for (int i = 0; i < count; i++)
            {
                xValues[i] = xValuesIn[i];
                yValues[i] = yValuesIn[i];
            }
            sortNode ( xValues, yValues, count );
        }
        
        private void qsort ( Site[] sites )
        {
            List<Site> listSites = new List<Site>( sites.Length );
            for ( int i = 0; i < sites.Length; i++ )
            {
                listSites.Add ( sites[i] );
            }
            
            listSites.Sort ( new SiteSorterYX () );
            
            // Copy back into the array
            for (int i=0; i < sites.Length; i++)
            {
                sites[i] = listSites[i];
            }
        }
        
        private void sortNode ( double[] xValues, double[] yValues, int numPoints )
        {
            nsites = numPoints;
            sites = new Site[nsites];
            xmin = xValues[0];
            ymin = yValues[0];
            xmax = xValues[0];
            ymax = yValues[0];
            
            for ( int i = 0; i < nsites; i++ )
            {
                sites[i] = new Site();
                sites[i].Coord.SetPoint ( xValues[i], yValues[i] );
                sites[i].SiteNbr = i;
                
                if ( xValues[i] < xmin )
                    xmin = xValues[i];
                else if ( xValues[i] > xmax )
                    xmax = xValues[i];
                
                if ( yValues[i] < ymin )
                    ymin = yValues[i];
                else if ( yValues[i] > ymax )
                    ymax = yValues[i];
            }
            
            qsort ( sites );
            deltax = xmax - xmin;
            deltay = ymax - ymin;
        }
        
        private Site nextone ()
        {
            Site s;
            if ( siteidx < nsites )
            {
                s = sites[siteidx];
                siteidx++;
                return s;
            }
            return null;
        }
        
        private Edge bisect ( Site s1, Site s2 )
        {
            double dx, dy, adx, ady;
            Edge newedge;
            
            newedge = new Edge();
            
            newedge.reg[0] = s1;
            newedge.reg[1] = s2;
            
            newedge.ep [0] = null;
            newedge.ep[1] = null;
            
            dx = s2.Coord.X - s1.Coord.X;
            dy = s2.Coord.Y - s1.Coord.Y;
            
            adx = dx > 0 ? dx : -dx;
            ady = dy > 0 ? dy : -dy;
            newedge.c = (double)(s1.Coord.X * dx + s1.Coord.Y * dy + (dx * dx + dy* dy) * 0.5);
            
            if ( adx > ady )
            {
                newedge.a = 1.0;
                newedge.b = dy / dx;
                newedge.c /= dx;
            }
            else
            {
                newedge.a = dx / dy;
                newedge.b = 1.0;
                newedge.c /= dy;
            }
            
            newedge.edgenbr = nedges;
            nedges++;
            
            return newedge;
        }
        
        private void makevertex ( Site v )
        {
            v.SiteNbr = nvertices;
            nvertices++;
        }
        
        private bool PQinitialize ()
        {
            PQcount = 0;
            PQmin = 0;
            PQhashsize = 4 * sqrt_nsites;
            PQhash = new Halfedge[ PQhashsize ];
            
            for ( int i = 0; i < PQhashsize; i++ )
            {
                PQhash [i] = new Halfedge();
            }
            return true;
        }
        
        private int PQbucket ( Halfedge he )
        {
            int bucket;
            
            bucket = (int) ((he.ystar - ymin) / deltay * PQhashsize);
            if ( bucket < 0 )
                bucket = 0;
            if ( bucket >= PQhashsize )
                bucket = PQhashsize - 1;
            if ( bucket < PQmin )
                PQmin = bucket;
            
            return bucket;
        }
        
        // push the HalfEdge into the ordered linked list of vertices
        private void PQinsert ( Halfedge he, Site v, double offset )
        {
            Halfedge last, next;
            
            he.vertex = v;
            he.ystar = (double)(v.Coord.Y + offset);
            last = PQhash [ PQbucket (he) ];
            
            while
                (
                    (next = last.PQnext) != null
                    && 
                    (he.ystar > next.ystar || (he.ystar == next.ystar && v.Coord.X > next.vertex.Coord.X))
                )
            {
                last = next;
            }
            
            he.PQnext = last.PQnext;
            last.PQnext = he;
            PQcount++;
        }
        
        // remove the HalfEdge from the list of vertices
        private void PQdelete ( Halfedge he )
        {
            Halfedge last;
            
            if (he.vertex != null)
            {
                last = PQhash [ PQbucket (he) ];
                while ( last.PQnext != he )
                {
                    last = last.PQnext;
                }
                
                last.PQnext = he.PQnext;
                PQcount--;
                he.vertex = null;
            }
        }
        
        private bool PQempty ()
        {
            return ( PQcount == 0 );
        }
        
        private DoubleVector2 PQ_min ()
        {
            DoubleVector2 answer = new DoubleVector2 ();
            
            while ( PQhash[PQmin].PQnext == null  )
            {
                PQmin++;
            }
            
            answer.X = PQhash[PQmin].PQnext.vertex.Coord.X;
            answer.Y = PQhash[PQmin].PQnext.ystar;
            return answer;
        }
        
        private Halfedge PQextractmin ()
        {
            Halfedge curr;
            
            curr = PQhash[PQmin].PQnext;
            PQhash[PQmin].PQnext = curr.PQnext;
            PQcount--;
            
            return curr;
        }
        
        private Halfedge HEcreate(Edge e, int pm)
        {
            Halfedge answer = new Halfedge();
            answer.ELedge = e;
            answer.ELpm = pm;
            answer.PQnext = null;
            answer.vertex = null;
            
            return answer;
        }
        
        private bool ELinitialize()
        {
            ELhashsize = 2 * sqrt_nsites;
            ELhash = new Halfedge[ELhashsize];
            
            for (int i = 0; i < ELhashsize; i++)
            {
                ELhash[i] = null;
            }
            
            ELleftend = HEcreate ( null, 0 );
            ELrightend = HEcreate ( null, 0 );
            ELleftend.ELleft = null;
            ELleftend.ELright = ELrightend;
            ELrightend.ELleft = ELleftend;
            ELrightend.ELright = null;
            ELhash[0] = ELleftend;
            ELhash[ELhashsize - 1] = ELrightend;
            
            return true;
        }
        
        private Halfedge ELright( Halfedge he )
        {
            return he.ELright;
        }
        
        private Halfedge ELleft( Halfedge he )
        {
            return he.ELleft;
        }
        
        private Site leftreg( Halfedge he )
        {
            if (he.ELedge == null)
            {
                return bottomsite;
            }
            return (he.ELpm == LE ? he.ELedge.reg[LE] : he.ELedge.reg[RE]);
        }
        
        private void ELinsert( Halfedge lb, Halfedge newHe )
        {
            newHe.ELleft = lb;
            newHe.ELright = lb.ELright;
            (lb.ELright).ELleft = newHe;
            lb.ELright = newHe;
        }
        
        /*
         * This delete routine can't reclaim node, since pointers from hash table
         * may be present.
         */
        private void ELdelete( Halfedge he )
        {
            (he.ELleft).ELright = he.ELright;
            (he.ELright).ELleft = he.ELleft;
            he.deleted = true;
        }
        
        /* Get entry from hash table, pruning any deleted nodes */
        private Halfedge ELgethash( int b )
        {
            Halfedge he;
            if (b < 0 || b >= ELhashsize)
                return null;
            
            he = ELhash[b];
            if (he == null || !he.deleted )
                return he;
            
            /* Hash table points to deleted half edge. Patch as necessary. */
            ELhash[b] = null;
            return null;
        }
        
        private Halfedge ELleftbnd( DoubleVector2 p )
        {
            int bucket;
            Halfedge he;
            
            /* Use hash table to get close to desired halfedge */
            // use the hash function to find the place in the hash map that this
            // HalfEdge should be
            bucket = (int) ((p.X - xmin) / deltax * ELhashsize);
            
            // make sure that the bucket position is within the range of the hash
            // array
            if ( bucket < 0 ) bucket = 0;
            if ( bucket >= ELhashsize )    bucket = ELhashsize - 1;
            
            he = ELgethash ( bucket );
            
            // if the HE isn't found, search backwards and forwards in the hash map
            // for the first non-null entry
            if ( he == null )
            {
                for ( int i = 1; i < ELhashsize; i++ )
                {
                    if ( (he = ELgethash ( bucket - i ) ) != null )
                        break;
                    if ( (he = ELgethash ( bucket + i ) ) != null )
                        break;
                }
            }
            
            /* Now search linear list of halfedges for the correct one */
            if ( he == ELleftend || ( he != ELrightend && right_of (he, p) ) )
            {
                // keep going right on the list until either the end is reached, or
                // you find the 1st edge which the point isn't to the right of
                do
                {
                    he = he.ELright;
                }
                while ( he != ELrightend && right_of(he, p) );
                he = he.ELleft;
            }
            else
                // if the point is to the left of the HalfEdge, then search left for
                // the HE just to the left of the point
            {
                do
                {
                    he = he.ELleft;
                }
                while ( he != ELleftend && !right_of(he, p) );
            }
            
            /* Update hash table and reference counts */
            if ( bucket > 0 && bucket < ELhashsize - 1)
            {
                ELhash[bucket] = he;
            }
            
            return he;
        }
        
        private void pushGraphEdge( Site leftSite, Site rightSite, Vector2 point1, Vector2 point2 )
        {
            GraphEdge newEdge = new GraphEdge(point1, point2);
            allEdges.Add ( newEdge );
                
            newEdge.Site1 = leftSite;
            newEdge.Site2 = rightSite;
        }
        
        private void clip_line( Edge e )
        {
            double pxmin, pxmax, pymin, pymax;
            Site s1, s2;
            
            double x1 = e.reg[0].Coord.X;
            double y1 = e.reg[0].Coord.Y;
            double x2 = e.reg[1].Coord.X;
            double y2 = e.reg[1].Coord.Y;
            double x = x2- x1;
            double y = y2 - y1;
            
            // if the distance between the two points this line was created from is
            // less than the square root of 2 عن جد؟, then ignore it
            if ( Math.Sqrt ( (x*x) + (y*y) ) < minDistanceBetweenSites )
            {
                return;
            }
            pxmin = borderMinX;
            pymin = borderMinY;
            pxmax = borderMaxX;
            pymax = borderMaxY;
            
            if ( e.a == 1.0 && e.b >= 0.0 )
            {
                s1 = e.ep[1];
                s2 = e.ep[0];
            }
            else
            {
                s1 = e.ep[0];
                s2 = e.ep[1];
            }
            
            if ( e.a == 1.0 )
            {
                y1 = pymin;
                
                if ( s1 != null && s1.Coord.Y > pymin )
                    y1 = s1.Coord.Y;
                if ( y1 > pymax )
                    y1 = pymax;
                x1 = e.c - e.b * y1;
                y2 = pymax;
                
                if ( s2 != null && s2.Coord.Y < pymax )
                    y2 = s2.Coord.Y;
                if ( y2 < pymin )
                    y2 = pymin;
                x2 = e.c - e.b * y2;
                if ( ( (x1 > pxmax) & (x2 > pxmax) ) | ( (x1 < pxmin) & (x2 < pxmin) ) )
                    return;
                
                if ( x1 > pxmax )
                {
                    x1 = pxmax;
                    y1 = ( e.c - x1 ) / e.b;
                }
                if ( x1 < pxmin )
                {
                    x1 = pxmin;
                    y1 = ( e.c - x1 ) / e.b;
                }
                if ( x2 > pxmax )
                {
                    x2 = pxmax;
                    y2 = ( e.c - x2 ) / e.b;
                }
                if ( x2 < pxmin )
                {
                    x2 = pxmin;
                    y2 = ( e.c - x2 ) / e.b;
                }
                
            }
            else
            {
                x1 = pxmin;
                if ( s1 != null && s1.Coord.X > pxmin )
                    x1 = s1.Coord.X;
                if ( x1 > pxmax )
                    x1 = pxmax;
                y1 = e.c - e.a * x1;
                
                x2 = pxmax;
                if ( s2 != null && s2.Coord.X < pxmax )
                    x2 = s2.Coord.X;
                if ( x2 < pxmin )
                    x2 = pxmin;
                y2 = e.c - e.a * x2;
                
                if (((y1 > pymax) & (y2 > pymax)) | ((y1 < pymin) & (y2 < pymin)))
                    return;
                
                if ( y1 > pymax )
                {
                    y1 = pymax;
                    x1 = ( e.c - y1 ) / e.a;
                }
                if ( y1 < pymin )
                {
                    y1 = pymin;
                    x1 = ( e.c - y1 ) / e.a;
                }
                if ( y2 > pymax )
                {
                    y2 = pymax;
                    x2 = ( e.c - y2 ) / e.a;
                }
                if ( y2 < pymin )
                {
                    y2 = pymin;
                    x2 = ( e.c - y2 ) / e.a;
                }
            }

            pushGraphEdge(e.reg[0], e.reg[1], new Vector2((float)x1, (float)y1), new Vector2((float)x2, (float)y2));
        }
        
        private void endpoint( Edge e, int lr, Site s )
        {
            e.ep[lr] = s;
            if ( e.ep[RE - lr] == null )
                return;
            clip_line ( e );
        }
        
        /* returns true if p is to right of halfedge e */
        private bool right_of(Halfedge el, DoubleVector2 p)
        {
            Edge e;
            Site topsite;
            bool right_of_site;
            bool above, fast;
            double dxp, dyp, dxs, t1, t2, t3, yl;
            
            e = el.ELedge;
            topsite = e.reg[1];
            
            if ( p.X > topsite.Coord.X )
                right_of_site = true;
            else
                right_of_site = false;
            
            if ( right_of_site && el.ELpm == LE )
                return true;
            if (!right_of_site && el.ELpm == RE )
                return false;
            
            if ( e.a == 1.0 )
            {
                dxp = p.X - topsite.Coord.X;
                dyp = p.Y - topsite.Coord.Y;
                fast = false;
                
                if ( (!right_of_site & (e.b < 0.0)) | (right_of_site & (e.b >= 0.0)) )
                {
                    above = dyp >= e.b * dxp;
                    fast = above;
                }
                else
                {
                    above = p.X + p.Y * e.b > e.c;
                    if ( e.b < 0.0 )
                        above = !above;
                    if ( !above )
                        fast = true;
                }
                if ( !fast )
                {
                    dxs = topsite.Coord.X - ( e.reg[0] ).Coord.X;
                    above = e.b * (dxp * dxp - dyp * dyp) 
                    < dxs * dyp * (1.0 + 2.0 * dxp / dxs + e.b * e.b);
                    
                    if ( e.b < 0 )
                        above = !above;
                }
            }
            else // e.b == 1.0
            {
                yl = e.c - e.a * p.X;
                t1 = p.Y - yl;
                t2 = p.X - topsite.Coord.X;
                t3 = yl - topsite.Coord.Y;
                above = t1 * t1 > t2 * t2 + t3 * t3;
            }
            return ( el.ELpm == LE ? above : !above );
        }
        
        private Site rightreg(Halfedge he)
        {
            if (he.ELedge == (Edge) null)
                // if this halfedge has no edge, return the bottom site (whatever
                // that is)
            {
                return (bottomsite);
            }

            // if the ELpm field is zero, return the site 0 that this edge bisects,
            // otherwise return site number 1
            return (he.ELpm == LE ? he.ELedge.reg[RE] : he.ELedge.reg[LE]);
        }
        
        private double dist( Site s, Site t )
        {
            double dx, dy;
            dx = s.Coord.X - t.Coord.X;
            dy = s.Coord.Y - t.Coord.Y;
            return Math.Sqrt ( dx * dx + dy * dy );
        }
        
        // create a new site where the HalfEdges el1 and el2 intersect - note that
        // the Point in the argument list is not used, don't know why it's there
        private Site intersect( Halfedge el1, Halfedge el2 )
        {
            Edge e1, e2, e;
            Halfedge el;
            double d, xint, yint;
            bool right_of_site;
            Site v; // vertex
            
            e1 = el1.ELedge;
            e2 = el2.ELedge;
            
            if ( e1 == null || e2 == null )
                return null;
            
            // if the two edges bisect the same parent, return null
            if ( e1.reg[1] == e2.reg[1] )
                return null;
            
            d = e1.a * e2.b - e1.b * e2.a;
            if ( -1.0e-10 < d && d < 1.0e-10 )
                return null;
            
            xint = ( e1.c * e2.b - e2.c * e1.b ) / d;
            yint = ( e2.c * e1.a - e1.c * e2.a ) / d;
            
            if ( (e1.reg[1].Coord.Y < e2.reg[1].Coord.Y)
                || (e1.reg[1].Coord.Y == e2.reg[1].Coord.Y && e1.reg[1].Coord.X < e2.reg[1].Coord.X) )
            {
                el = el1;
                e = e1;
            }
            else
            {
                el = el2;
                e = e2;
            }
            
            right_of_site = xint >= e.reg[1].Coord.X;
            if ((right_of_site && el.ELpm == LE)
                || (!right_of_site && el.ELpm == RE))
                return null;
            
            // create a new site at the point of intersection - this is a new vector
            // event waiting to happen
            v = new Site();
            v.Coord.X = xint;
            v.Coord.Y = yint;
            return v;
        }
        
        /*
         * implicit parameters: nsites, sqrt_nsites, xmin, xmax, ymin, ymax, deltax,
         * deltay (can all be estimates). Performance suffers if they are wrong;
         * better to make nsites, deltax, and deltay too big than too small. (?)
         */
        private bool voronoi_bd()
        {
            Site newsite, bot, top, temp, p;
            Site v;
            DoubleVector2 newintstar = null;
            int pm;
            Halfedge lbnd, rbnd, llbnd, rrbnd, bisector;
            Edge e;

            PQinitialize();
            ELinitialize();

            bottomsite = nextone();
            newsite = nextone();
            while (true)
            {
                if (!PQempty())
                {
                    newintstar = PQ_min();
                }
                // if the lowest site has a smaller y value than the lowest vector
                // intersection,
                // process the site otherwise process the vector intersection

                if (newsite != null && (PQempty()
                                        || newsite.Coord.Y < newintstar.Y
                                        || (newsite.Coord.Y == newintstar.Y
                                            && newsite.Coord.X < newintstar.X)))
                {
                    /* new site is smallest -this is a site event */
                    // get the first HalfEdge to the LEFT of the new site
                    lbnd = ELleftbnd((newsite.Coord));
                    // get the first HalfEdge to the RIGHT of the new site
                    rbnd = ELright(lbnd);
                    // if this halfedge has no edge,bot =bottom site (whatever that
                    // is)
                    bot = rightreg(lbnd);
                    // create a new edge that bisects
                    e = bisect(bot, newsite);

                    // create a new HalfEdge, setting its ELpm field to 0
                    bisector = HEcreate(e, LE);
                    // insert this new bisector edge between the left and right
                    // vectors in a linked list
                    ELinsert(lbnd, bisector);

                    // if the new bisector intersects with the left edge,
                    // remove the left edge's vertex, and put in the new one
                    if ((p = intersect(lbnd, bisector)) != null)
                    {
                        PQdelete(lbnd);
                        PQinsert(lbnd, p, dist(p, newsite));
                    }
                    lbnd = bisector;
                    // create a new HalfEdge, setting its ELpm field to 1
                    bisector = HEcreate(e, RE);
                    // insert the new HE to the right of the original bisector
                    // earlier in the IF stmt
                    ELinsert(lbnd, bisector);

                    // if this new bisector intersects with the new HalfEdge
                    if ((p = intersect(bisector, rbnd)) != null)
                    {
                        // push the HE into the ordered linked list of vertices
                        PQinsert(bisector, p, dist(p, newsite));
                    }
                    newsite = nextone();
                } else if (!PQempty())
                    /* intersection is smallest - this is a vector event */
                {
                    // pop the HalfEdge with the lowest vector off the ordered list
                    // of vectors
                    lbnd = PQextractmin();
                    // get the HalfEdge to the left of the above HE
                    llbnd = ELleft(lbnd);
                    // get the HalfEdge to the right of the above HE
                    rbnd = ELright(lbnd);
                    // get the HalfEdge to the right of the HE to the right of the
                    // lowest HE
                    rrbnd = ELright(rbnd);
                    // get the Site to the left of the left HE which it bisects
                    bot = leftreg(lbnd);
                    // get the Site to the right of the right HE which it bisects
                    top = rightreg(rbnd);

                    v = lbnd.vertex; // get the vertex that caused this event
                    makevertex(v); // set the vertex number - couldn't do this
                    // earlier since we didn't know when it would be processed
                    endpoint(lbnd.ELedge, lbnd.ELpm, v);
                    // set the endpoint of
                    // the left HalfEdge to be this vector
                    endpoint(rbnd.ELedge, rbnd.ELpm, v);
                    // set the endpoint of the right HalfEdge to
                    // be this vector
                    ELdelete(lbnd); // mark the lowest HE for
                    // deletion - can't delete yet because there might be pointers
                    // to it in Hash Map
                    PQdelete(rbnd);
                    // remove all vertex events to do with the right HE
                    ELdelete(rbnd); // mark the right HE for
                    // deletion - can't delete yet because there might be pointers
                    // to it in Hash Map
                    pm = LE; // set the pm variable to zero

                    if (bot.Coord.Y > top.Coord.Y)
                        // if the site to the left of the event is higher than the
                        // Site
                    { // to the right of it, then swap them and set the 'pm'
                        // variable to 1
                        temp = bot;
                        bot = top;
                        top = temp;
                        pm = RE;
                    }
                    e = bisect(bot, top); // create an Edge (or line)
                    // that is between the two Sites. This creates the formula of
                    // the line, and assigns a line number to it
                    bisector = HEcreate(e, pm); // create a HE from the Edge 'e',
                    // and make it point to that edge
                    // with its ELedge field
                    ELinsert(llbnd, bisector); // insert the new bisector to the
                    // right of the left HE
                    endpoint(e, RE - pm, v); // set one endpoint to the new edge
                    // to be the vector point 'v'.
                    // If the site to the left of this bisector is higher than the
                    // right Site, then this endpoint
                    // is put in position 0; otherwise in pos 1

                    // if left HE and the new bisector intersect, then delete
                    // the left HE, and reinsert it
                    if ((p = intersect(llbnd, bisector)) != null)
                    {
                        PQdelete(llbnd);
                        PQinsert(llbnd, p, dist(p, bot));
                    }

                    // if right HE and the new bisector intersect, then
                    // reinsert it
                    if ((p = intersect(bisector, rrbnd)) != null)
                    {
                        PQinsert(bisector, p, dist(p, bot));
                    }
                } else
                {
                    break;
                }
            }

            for (lbnd = ELright(ELleftend); lbnd != ELrightend; lbnd = ELright(lbnd))
            {
                e = lbnd.ELedge;
                clip_line(e);
            }

            return true;
        }
        
        public List<GraphEdge> MakeVoronoiGraph(List<Vector2> sites, int width, int height)
        {
            double[] xVal = new double[sites.Count];
            double[] yVal = new double[sites.Count];
            for (int i = 0; i < sites.Count; i++)
            {
                xVal[i] = sites[i].X;
                yVal[i] = sites[i].Y;
            }
            return generateVoronoi(xVal, yVal, 0, width, 0, height);
        }

        public List<GraphEdge> MakeVoronoiGraph(double[] xVal, double[] yVal, int width, int height)
        {
            return generateVoronoi(xVal, yVal, 0, width, 0, height);
        }

    } // Voronoi Class End
} // namespace Voronoi2 End