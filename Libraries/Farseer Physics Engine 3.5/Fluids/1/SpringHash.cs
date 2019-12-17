/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using System.Collections.Generic;

namespace FarseerPhysics.Fluids
{
    public class SpringHash : IEqualityComparer<SpringHash>
    {
        public FluidParticle P0;
        public FluidParticle P1;

        public bool Equals(SpringHash lhs, SpringHash rhs)
        {
            return (lhs.P0.Index == rhs.P0.Index && lhs.P1.Index == rhs.P1.Index)
                   || (lhs.P0.Index == rhs.P1.Index && lhs.P1.Index == rhs.P0.Index);
        }

        public int GetHashCode(SpringHash s)
        {
            return (s.P0.Index * 73856093) ^ (s.P1.Index * 19349663) ^ (s.P0.Index * 19349663) ^ (s.P1.Index * 73856093);
        }
    }
}