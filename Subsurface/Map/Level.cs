using FarseerPhysics;
using FarseerPhysics.Common;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using Voronoi2;

namespace Subsurface
{
    class Level
    {
        private int seed;

        List<Body> bodies;
        List<VoronoiCell> cells;

        public Level(int seed, int siteCount, int width, int height)
        {
            this.seed = seed;

            Voronoi voronoi = new Voronoi(1.0);

            List<PointF> sites = new List<PointF>();
            Random rand = new Random(seed);

            for (int i = 0; i < siteCount; i++)
            {
                sites.Add(new PointF((float)(rand.NextDouble() * width), (float)(rand.NextDouble() * width)));
            }

            List<GraphEdge> graphEdges = MakeVoronoiGraph(sites, voronoi, width, height);

            cells = new List<VoronoiCell>();
            foreach (GraphEdge ge in graphEdges)
            {
                for (int i = 0; i<2; i++)
                {
                    int site = (i==0) ? ge.site1 : ge.site2;
                    VoronoiCell cell = cells.Find(c => c.site == site);
                    if (cell == null)
                    {
                        cell = new VoronoiCell(site);
                        cells.Add(cell);
                    }
                    if (!cell.edges.Contains(ge)) cell.edges.Add(ge);
                }
            }


            bodies = new List<Body>();
            foreach (VoronoiCell cell in cells)
            {
                //List of vectors defining my custom poly
                List<Vector2> vlist = new List<Vector2>();
                foreach (GraphEdge ge in cell.edges)
                {
                    if (vlist.Contains(ge.point1)) continue;
                    vlist.Add(ge.point1);
                }

                if (vlist.Count < 2) continue;

                for (int i = 0; i < vlist.Count; i++ )
                {
                    vlist[i] = ConvertUnits.ToSimUnits(vlist[i]);
                }


                //get farseer 'vertices' from vectors
                Vertices _shapevertices = new Vertices(vlist);
                //_shapevertices.Sort(less);

                //feed vertices array to BodyFactory.CreatePolygon to get a new farseer polygonal body
                Body _newBody = BodyFactory.CreatePolygon(Game1.world, _shapevertices, 15);
                _newBody.BodyType = BodyType.Static;
                _newBody.CollisionCategories = Physics.CollisionWall;

                bodies.Add(_newBody);
            }
        }


        public int Compare(Vector2 a, Vector2 b, Vector2 center)
        {
            if (a.X - center.X >= 0 && b.X - center.X < 0) return 1;
            if (a.X - center.X < 0 && b.X - center.X >= 0) return -1;
            if (a.X - center.X == 0 && b.X - center.X == 0)
            {
                if (a.Y - center.Y >= 0 || b.Y - center.Y >= 0) return Math.Sign(a.Y - b.Y);
                return Math.Sign(b.Y-a.Y);
            }

            // compute the cross product of vectors (center -> a) x (center -> b)
            float det = (a.X - center.X) * (b.Y - center.Y) - (b.X - center.X) * (a.Y - center.Y);
            if (det < 0) return 1;
            if (det > 0) return -1;

            // points a and b are on the same line from the center
            // check which point is closer to the center
            float d1 = (a.X - center.X) * (a.X - center.X) + (a.Y - center.Y) * (a.Y - center.Y);
            float d2 = (b.X - center.X) * (b.X - center.X) + (b.Y - center.Y) * (b.Y - center.Y);
            return Math.Sign(d1 - d2);
        }

        List<GraphEdge> MakeVoronoiGraph(List<PointF> sites, Voronoi voronoi, int width, int height)
        {
            double[] xVal = new double[sites.Count];
            double[] yVal = new double[sites.Count];
            for (int i = 0; i < sites.Count; i++)
            {
                xVal[i] = sites[i].X;
                yVal[i] = sites[i].Y;
            }
            return voronoi.generateVoronoi(xVal, yVal, 0, width, 0, height);
        }

        public void Render(SpriteBatch spriteBatch)
        {
            foreach (VoronoiCell cell in cells)
            {
                for (int i = 0; i<cell.edges.Count; i++)
                {
                    GUI.DrawLine(spriteBatch, cell.edges[i].point1, cell.edges[i].point2, Microsoft.Xna.Framework.Color.Red);
                }
            }
        }
    }

}
