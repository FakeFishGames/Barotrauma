/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Fluids
{
    public class FluidSystem1
    {
        private float _influenceRadiusSquared;
        private HashGrid _hashGrid = new HashGrid();
        private Dictionary<SpringHash, Spring> _springs = new Dictionary<SpringHash, Spring>();
        private List<SpringHash> _springsToRemove = new List<SpringHash>();
        private Vector2 _totalForce;

        public FluidSystem1(Vector2 gravity)
        {
            Gravity = gravity;
            Particles = new List<FluidParticle>();
            DefaultDefinition();
        }

        public FluidDefinition Definition { get; private set; }
        public List<FluidParticle> Particles { get; private set; }
        public int ParticlesCount { get { return Particles.Count; } }
        public Vector2 Gravity { get; set; }

        public void DefaultDefinition()
        {
            SetDefinition(FluidDefinition.Default);
        }

        public void SetDefinition(FluidDefinition def)
        {
            Definition = def;
            Definition.Check();
            _influenceRadiusSquared = Definition.InfluenceRadius * Definition.InfluenceRadius;
        }

        public FluidParticle AddParticle(Vector2 position)
        {
            FluidParticle particle = new FluidParticle(position) { Index = Particles.Count };
            Particles.Add(particle);
            return particle;
        }

        public void Clear()
        {
            //TODO
        }

        public void ApplyForce(Vector2 f)
        {
            _totalForce += f;
        }

        private void ApplyForces()
        {
            Vector2 f = Gravity + _totalForce;

            for (int i = 0; i < Particles.Count; ++i)
            {
                Particles[i].ApplyForce(ref f);
            }

            _totalForce = Vector2.Zero;
        }

        private void ApplyViscosity(FluidParticle p, float timeStep)
        {
            for (int i = 0; i < p.Neighbours.Count; ++i)
            {
                FluidParticle neighbour = p.Neighbours[i];

                if (p.Index >= neighbour.Index)
                {
                    continue;
                }

                float q;
                Vector2.DistanceSquared(ref p.Position, ref neighbour.Position, out q);

                if (q > _influenceRadiusSquared)
                {
                    continue;
                }

                Vector2 direction;
                Vector2.Subtract(ref neighbour.Position, ref p.Position, out direction);

                if (direction.LengthSquared() < float.Epsilon)
                {
                    continue;
                }

                direction.Normalize();

                Vector2 deltaVelocity;
                Vector2.Subtract(ref p.Velocity, ref neighbour.Velocity, out deltaVelocity);

                float u;
                Vector2.Dot(ref deltaVelocity, ref direction, out u);

                if (u > 0.0f)
                {
                    q = 1.0f - (float)Math.Sqrt(q) / Definition.InfluenceRadius;

                    float impulseFactor = 0.5f * timeStep * q * (u * (Definition.ViscositySigma + Definition.ViscosityBeta * u));

                    Vector2 impulse;

                    Vector2.Multiply(ref direction, -impulseFactor, out impulse);
                    p.ApplyImpulse(ref impulse);

                    Vector2.Multiply(ref direction, impulseFactor, out impulse);
                    neighbour.ApplyImpulse(ref impulse);
                }
            }
        }

        private const int MaxNeighbors = 25;
        //private int _len2;
        //private int _j;
        //private float _q;
        //private float _qq;

        //private Vector2 _rij;
        //private float _d;
        //private Vector2 _dx;
        private float _density;
        private float _densityNear;
        private float _pressure;
        private float _pressureNear;
        private float[] _distanceCache = new float[MaxNeighbors];

        //private void DoubleDensityRelaxation1(FluidParticle p, float timeStep)
        //{
        //    _density = 0;
        //    _densityNear = 0;

        //    _len2 = p.Neighbours.Count;
        //    if (_len2 > MaxNeighbors)
        //        _len2 = MaxNeighbors;

        //    for (_j = 0; _j < _len2; _j++)
        //    {
        //        _q = Vector2.DistanceSquared(p.Position, p.Neighbours[_j].Position);
        //        _distanceCache[_j] = _q;
        //        if (_q < _influenceRadiusSquared && _q != 0)
        //        {
        //            _q = (float)Math.Sqrt(_q);
        //            _q /= Definition.InfluenceRadius;
        //            _qq = ((1 - _q) * (1 - _q));
        //            _density += _qq;
        //            _densityNear += _qq * (1 - _q);
        //        }
        //    }

        //    _pressure = Definition.Stiffness * (_density - Definition.DensityRest);
        //    _pressureNear = Definition.StiffnessNear * _densityNear;

        //    _dx = Vector2.Zero;

        //    for (_j = 0; _j < _len2; _j++)
        //    {
        //        _q = _distanceCache[_j];
        //        if (_q < _influenceRadiusSquared && _q != 0)
        //        {
        //            _q = (float)Math.Sqrt(_q);
        //            _rij = p.Neighbours[_j].Position;
        //            _rij -= p.Position;
        //            _rij *= 1 / _q;
        //            _q /= _influenceRadiusSquared;

        //            _d = ((timeStep * timeStep) * (_pressure * (1 - _q) + _pressureNear * (1 - _q) * (1 - _q)));
        //            _rij *= _d * 0.5f;
        //            p.Neighbours[_j].Position += _rij;
        //            _dx -= _rij;
        //        }
        //    }
        //    p.Position += _dx;
        //}

        private void DoubleDensityRelaxation(FluidParticle particle, float deltaTime2)
        {
            _density = 0.0f;
            _densityNear = 0.0f;

            int neightborCount = particle.Neighbours.Count;

            if (neightborCount > MaxNeighbors)
                neightborCount = MaxNeighbors;

            for (int i = 0; i < neightborCount; ++i)
            {
                FluidParticle neighbour = particle.Neighbours[i];

                if (particle.Index == neighbour.Index)
                    continue;

                float q;
                Vector2.DistanceSquared(ref particle.Position, ref neighbour.Position, out q);
                _distanceCache[i] = q;

                if (q > _influenceRadiusSquared)
                    continue;

                q = 1.0f - (float)Math.Sqrt(q) / Definition.InfluenceRadius;

                float densityDelta = q * q;
                _density += densityDelta;
                _densityNear += densityDelta * q;
            }

            _pressure = Definition.Stiffness * (_density - Definition.DensityRest);
            _pressureNear = Definition.StiffnessNear * _densityNear;

            // For gameplay purposes
            particle.Density = _density + _densityNear;
            particle.Pressure = _pressure + _pressureNear;

            Vector2 delta = Vector2.Zero;

            for (int i = 0; i < neightborCount; ++i)
            {
                FluidParticle neighbour = particle.Neighbours[i];

                if (particle.Index == neighbour.Index)
                    continue;

                float q = _distanceCache[i];

                if (q > _influenceRadiusSquared)
                    continue;

                q = 1.0f - (float)Math.Sqrt(q) / Definition.InfluenceRadius;

                float dispFactor = deltaTime2 * (q * (_pressure + _pressureNear * q));

                Vector2 direction;
                Vector2.Subtract(ref neighbour.Position, ref particle.Position, out direction);

                if (direction.LengthSquared() < float.Epsilon)
                    continue;

                direction.Normalize();

                Vector2 disp;

                Vector2.Multiply(ref direction, dispFactor, out disp);
                Vector2.Add(ref neighbour.Position, ref disp, out neighbour.Position);

                Vector2.Multiply(ref direction, -dispFactor, out disp);
                Vector2.Add(ref delta, ref disp, out delta);
            }

            Vector2.Add(ref particle.Position, ref delta, out particle.Position);
        }

        private void CreateSprings(FluidParticle p)
        {
            for (int i = 0; i < p.Neighbours.Count; ++i)
            {
                FluidParticle neighbour = p.Neighbours[i];

                if (p.Index >= neighbour.Index)
                    continue;

                float q;
                Vector2.DistanceSquared(ref p.Position, ref neighbour.Position, out q);

                if (q > _influenceRadiusSquared)
                    continue;

                SpringHash hash = new SpringHash { P0 = p, P1 = neighbour };

                if (!_springs.ContainsKey(hash))
                {
                    //TODO: Use pool?
                    Spring spring = new Spring(p, neighbour) { RestLength = (float)Math.Sqrt(q) };
                    _springs.Add(hash, spring);
                }
            }
        }

        private void AdjustSprings(float timeStep)
        {
            foreach (var pair in _springs)
            {
                Spring spring = pair.Value;

                spring.Update(timeStep, Definition.KSpring, Definition.InfluenceRadius);

                if (spring.Active)
                {
                    float L = spring.RestLength;
                    float distance;
                    Vector2.Distance(ref spring.P0.Position, ref spring.P1.Position, out distance);

                    if (distance > (L + (Definition.YieldRatioStretch * L)))
                    {
                        spring.RestLength += timeStep * Definition.Plasticity * (distance - L - (Definition.YieldRatioStretch * L));
                    }
                    else if (distance < (L - (Definition.YieldRatioCompress * L)))
                    {
                        spring.RestLength -= timeStep * Definition.Plasticity * (L - (Definition.YieldRatioCompress * L) - distance);
                    }
                }
                else
                {
                    _springsToRemove.Add(pair.Key);
                }
            }

            for (int i = 0; i < _springsToRemove.Count; ++i)
            {
                _springs.Remove(_springsToRemove[i]);
            }
        }

        private void ComputeNeighbours()
        {
            _hashGrid.GridSize = Definition.InfluenceRadius;
            _hashGrid.Clear();

            for (int i = 0; i < Particles.Count; ++i)
            {
                FluidParticle p = Particles[i];

                if (p.IsActive)
                {
                    _hashGrid.Add(p);
                }
            }

            for (int i = 0; i < Particles.Count; ++i)
            {
                FluidParticle p = Particles[i];
                p.Neighbours.Clear();
                _hashGrid.Find(ref p.Position, p.Neighbours);
            }
        }

        public void Update(float deltaTime)
        {
            if (deltaTime == 0)
                return;

            float deltaTime2 = 0.5f * deltaTime * deltaTime;

            ComputeNeighbours();
            ApplyForces();

            if (Definition.UseViscosity)
            {
                for (int i = 0; i < Particles.Count; ++i)
                {
                    FluidParticle p = Particles[i];
                    if (p.IsActive)
                    {
                        ApplyViscosity(p, deltaTime);
                    }
                }
            }

            for (int i = 0; i < Particles.Count; ++i)
            {
                FluidParticle p = Particles[i];
                if (p.IsActive)
                {
                    p.Update(deltaTime);
                }
            }

            for (int i = 0; i < Particles.Count; ++i)
            {
                FluidParticle p = Particles[i];
                if (p.IsActive)
                {
                    DoubleDensityRelaxation(p, deltaTime2);
                }
            }

            if (Definition.UsePlasticity)
            {
                for (int i = 0; i < Particles.Count; ++i)
                {
                    FluidParticle p = Particles[i];
                    if (p.IsActive)
                    {
                        CreateSprings(p);
                    }
                }
            }

            AdjustSprings(deltaTime);

            UpdateVelocities(deltaTime);
        }

        internal void UpdateVelocities(float timeStep)
        {
            for (int i = 0; i < Particles.Count; ++i)
            {
                Particles[i].UpdateVelocity(timeStep);
            }
        }
    }
}
