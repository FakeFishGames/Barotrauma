using System;
using System.Collections.Generic;

namespace FarseerPhysics.Common.Decomposition.CDT.Util
{
    internal class PointGenerator
    {
        private static readonly Random RNG = new Random();

        public static List<TriangulationPoint> UniformDistribution(int n, double scale)
        {
            List<TriangulationPoint> points = new List<TriangulationPoint>();
            for (int i = 0; i < n; i++)
            {
                points.Add(new TriangulationPoint(scale*(0.5 - RNG.NextDouble()), scale*(0.5 - RNG.NextDouble())));
            }
            return points;
        }

        public static List<TriangulationPoint> UniformGrid(int n, double scale)
        {
            double x = 0;
            double size = scale/n;
            double halfScale = 0.5*scale;

            List<TriangulationPoint> points = new List<TriangulationPoint>();
            for (int i = 0; i < n + 1; i++)
            {
                x = halfScale - i*size;
                for (int j = 0; j < n + 1; j++)
                {
                    points.Add(new TriangulationPoint(x, halfScale - j*size));
                }
            }
            return points;
        }
    }
}