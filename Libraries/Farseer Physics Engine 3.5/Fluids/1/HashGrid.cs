/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Fluids
{
    /// <summary>
    /// Grid used by particle system to keep track of neightbor particles.
    /// </summary>
    public class HashGrid
    {
        private Dictionary<ulong, List<FluidParticle>> _hash = new Dictionary<ulong, List<FluidParticle>>();
        private Stack<List<FluidParticle>> _bucketPool = new Stack<List<FluidParticle>>();

        public HashGrid()
        {
            GridSize = 1.0f;
        }

        public float GridSize { get; set; }

        private static ulong HashKey(int x, int y)
        {
            return ((ulong)x * 2185031351ul) ^ ((ulong)y * 4232417593ul);
        }

        private ulong HashKey(Vector2 position)
        {
            return HashKey(
                (int)Math.Floor(position.X / GridSize),
                (int)Math.Floor(position.Y / GridSize)
            );
        }

        public void Clear()
        {
            foreach (KeyValuePair<ulong, List<FluidParticle>> pair in _hash)
            {
                pair.Value.Clear();
                _bucketPool.Push(pair.Value);
            }
            _hash.Clear();
        }

        public void Add(FluidParticle particle)
        {
            ulong key = HashKey(particle.Position);
            List<FluidParticle> bucket;
            if (!_hash.TryGetValue(key, out bucket))
            {
                if (_bucketPool.Count > 0)
                {
                    bucket = _bucketPool.Pop();
                }
                else
                {
                    bucket = new List<FluidParticle>();
                }
                _hash.Add(key, bucket);
            }
            bucket.Add(particle);
        }

        public void Find(ref Vector2 position, List<FluidParticle> neighbours)
        {
            int ix = (int)Math.Floor(position.X / GridSize);
            int iy = (int)Math.Floor(position.Y / GridSize);

            // Check all 9 neighbouring cells
            for (int x = ix - 1; x <= ix + 1; ++x)
            {
                for (int y = iy - 1; y <= iy + 1; ++y)
                {
                    ulong key = HashKey(x, y);
                    List<FluidParticle> bucket;
                    if (_hash.TryGetValue(key, out bucket))
                    {
                        for (int i = 0; i < bucket.Count; ++i)
                        {
                            if (bucket[i] != null)
                            {
                                neighbours.Add(bucket[i]);
                            }
                        }
                    }
                }
            }
        }
    }
}
