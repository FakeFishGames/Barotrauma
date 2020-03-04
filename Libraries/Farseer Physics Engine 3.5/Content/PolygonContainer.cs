/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using System.Collections.Generic;
using FarseerPhysics.Common;
using FarseerPhysics.Common.Decomposition;

/*namespace FarseerPhysics.Content
{
    public struct Polygon
    {
        public Vertices Vertices;
        public bool Closed;

        public Polygon(Vertices v, bool closed)
        {
            Vertices = v;
            Closed = closed;
        }
    }

    public class PolygonContainer : Dictionary<string, Polygon>
    {
        public bool IsDecomposed
        {
            get { return _decomposed; }
        }

        private bool _decomposed;

        public void Decompose()
        {
            Dictionary<string, Polygon> containerCopy = new Dictionary<string, Polygon>(this);
            foreach (string key in containerCopy.Keys)
            {
                if (containerCopy[key].Closed)
                {
                    List<Vertices> partition = Triangulate.ConvexPartition(containerCopy[key].Vertices, TriangulationAlgorithm.Bayazit);
                    if (partition.Count > 1)
                    {
                        Remove(key);
                        for (int i = 0; i < partition.Count; i++)
                        {
                            this[key + "_" + i.ToString()] = new Polygon(partition[i], true);
                        }
                    }
                    _decomposed = true;
                }
            }
        }
    }
}*/