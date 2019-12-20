/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using Microsoft.Xna.Framework;

namespace FarseerPhysics.Fluids
{
    //TODO: Could be struct?

    public class Spring
    {
        public FluidParticle P0;
        public FluidParticle P1;

        public Spring(FluidParticle p0, FluidParticle p1)
        {
            Active = true;
            P0 = p0;
            P1 = p1;
        }

        public bool Active { get; set; }
        public float RestLength { get; set; }

        public void Update(float timeStep, float kSpring, float influenceRadius)
        {
            if (!Active)
                return;

            Vector2 dir = P1.Position - P0.Position;
            float distance = dir.Length();
            dir.Normalize();

            // This is to avoid imploding simulation with really springy fluids
            if (distance < 0.5f * influenceRadius)
            {
                Active = false;
                return;
            }
            if (RestLength > influenceRadius)
            {
                Active = false;
                return;
            }
            
            //Algorithm 3
            float displacement = timeStep * timeStep * kSpring * (1.0f - RestLength / influenceRadius) * (RestLength - distance) * 0.5f;

            dir *= displacement;

            P0.Position -= dir;
            P1.Position += dir;
        }
    }
}
