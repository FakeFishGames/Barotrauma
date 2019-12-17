/* Original source Farseer Physics Engine:
 * Copyright (c) 2014 Ian Qvist, http://farseerphysics.codeplex.com
 * Microsoft Permissive License (Ms-PL) v1.1
 */

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace FarseerPhysics.Fluids
{
    public class FluidSystem2
    {
        public const int MaxNeighbors = 25;
        public const int CellSize = 1;

        // Most of these can be tuned at runtime with F1-F9 and keys 1-9 (no numpad)
        public const float InfluenceRadius = 20.0f;
        public const float InfluenceRadiusSquared = InfluenceRadius * InfluenceRadius;
        public const float Stiffness = 0.504f;
        public const float StiffnessFarNearRatio = 10.0f;
        public const float StiffnessNear = Stiffness * StiffnessFarNearRatio;
        public const float ViscositySigma = 0.0f;
        public const float ViscosityBeta = 0.3f;
        public const float DensityRest = 10.0f;
        public const float KSpring = 0.3f;
        public const float RestLength = 5.0f;
        public const float RestLengthSquared = RestLength * RestLength;
        public const float YieldRatioStretch = 0.5f;
        public const float YieldRatioCompress = 0.5f;
        public const float Plasticity = 0.5f;
        public const int VelocityCap = 150;
        public const float DeformationFactor = 0f;
        public const float CollisionForce = 0.3f;

        private bool _isElasticityInitialized;
        private bool _elasticityEnabled;
        private bool _isPlasticityInitialized;
        private bool _plasticityEnabled;

        private float _deltaTime2;
        private Vector2 _dx = new Vector2(0.0f, 0.0f);
        private const int Wpadding = 20;
        private const int Hpadding = 20;

        public SpatialTable Particles;

        // Temp variables
        private Vector2 _rij = new Vector2(0.0f, 0.0f);
        private Vector2 _tempVect = new Vector2(0.0f, 0.0f);

        private Dictionary<int, List<int>> _springPresenceTable;
        private List<Spring2> _springs;
        private List<Particle> _tempParticles;

        private int _worldWidth;
        private int _worldHeight;
        
        public int ParticlesCount { get { return Particles.Count; } }

        public FluidSystem2(Vector2 gravity, int maxParticleLimit, int worldWidth, int worldHeight)
        {
            _worldHeight = worldHeight;
            _worldWidth = worldWidth;
            Particles = new SpatialTable(worldWidth, worldHeight, CellSize);
            MaxParticleLimit = maxParticleLimit;
            Gravity = gravity;
        }

        public Vector2 Gravity { get; set; }
        public int MaxParticleLimit { get; private set; }

        public bool ElasticityEnabled
        {
            get { return _elasticityEnabled; }
            set
            {
                if (!_isElasticityInitialized)
                    InitializeElasticity();

                _elasticityEnabled = value;
            }
        }

        public bool PlasticityEnabled
        {
            get { return _plasticityEnabled; }
            set
            {
                if (!_isPlasticityInitialized)
                    InitializePlasticity();

                _plasticityEnabled = value;
            }
        }

        private void UpdateParticleVelocity(float deltaTime)
        {
            for(int i = 0; i < Particles.Count; i++)
            {
                Particle particle = Particles[i];
                particle.PreviousPosition = particle.Position;
                particle.Position = new Vector2(particle.Position.X + (deltaTime * particle.Velocity.X), particle.Position.Y + (deltaTime * particle.Velocity.Y));
            }
        }

        private void WallCollision(Particle pi)
        {
            float x = 0;
            float y = 0;

            if (pi.Position.X > (_worldWidth / 2 - Wpadding))
                x -= (pi.Position.X - (_worldWidth / 2 - Wpadding)) / CollisionForce;
            else if (pi.Position.X < (-_worldWidth / 2 + Wpadding))
                x += ((-_worldWidth / 2 + Wpadding) - pi.Position.X) / CollisionForce;

            if (pi.Position.Y > (_worldHeight - Hpadding))
                y -= (pi.Position.Y - (_worldHeight - Hpadding)) / CollisionForce;
            else if (pi.Position.Y < Hpadding)
                y += (Hpadding - pi.Position.Y) / CollisionForce;

            pi.Velocity.X += x;
            pi.Velocity.Y += y;
        }

        private void CapVelocity(Vector2 v)
        {
            if (v.X > VelocityCap)
                v.X = VelocityCap;
            else if (v.X < -VelocityCap)
                v.X = -VelocityCap;

            if (v.Y > VelocityCap)
                v.Y = VelocityCap;
            else if (v.Y < -VelocityCap)
                v.Y = -VelocityCap;
        }

        private void InitializePlasticity()
        {
            _isPlasticityInitialized = true;

            _springs.Clear();
            float q;
            foreach (Particle pa in Particles)
            {
                foreach (Particle pb in Particles)
                {
                    if (pa.GetHashCode() == pb.GetHashCode())
                        continue;

                    Vector2.Distance(ref pa.Position, ref pb.Position, out q);
                    Vector2.Subtract(ref pb.Position, ref pa.Position, out _rij);
                    _rij /= q;

                    if (q < RestLength)
                    {
                        _springs.Add(new Spring2(pa, pb, q));
                    }
                }
                pa.Velocity = Vector2.Zero;
            }
        }

        private void CalculatePlasticity(float deltaTime)
        {
            foreach (Spring2 spring in _springs)
            {
                spring.Update();

                if (spring.CurrentDistance == 0)
                    continue;

                Vector2.Subtract(ref  spring.PB.Position, ref spring.PA.Position, out _rij);
                _rij /= spring.CurrentDistance;
                float D = deltaTime * KSpring * (spring.RestLength - spring.CurrentDistance);
                _rij *= (D * 0.5f);
                spring.PA.Position = new Vector2(spring.PA.Position.X - _rij.X, spring.PA.Position.Y - _rij.Y);
                spring.PB.Position = new Vector2(spring.PB.Position.X + _rij.X, spring.PB.Position.Y + _rij.Y);
            }
        }

        private void InitializeElasticity()
        {
            _isElasticityInitialized = true;

            foreach (Particle particle in Particles)
            {
                _springPresenceTable.Add(particle.GetHashCode(), new List<int>(MaxParticleLimit));
                particle.Velocity = Vector2.Zero;
            }
        }

        private void CalculateElasticity(float deltaTime)
        {
            float sqDist;
            for (int i = 0; i < Particles.Count; i++)
            {
                Particle pa = Particles[i];

                if (Particles.CountNearBy(pa) <= 1)
                    continue;

                _tempParticles = Particles.GetNearby(pa);
                int len2 = _tempParticles.Count;

                if (len2 > MaxNeighbors)
                    len2 = MaxNeighbors;

                for (int j = 0; j < len2; j++)
                {
                    Particle pb = Particles[j];
                    Vector2.DistanceSquared(ref pa.Position, ref pb.Position, out sqDist);
                    if (sqDist > RestLengthSquared)
                        continue;
                    if (pa.GetHashCode() == pb.GetHashCode())
                        continue;
                    if (!_springPresenceTable[pa.GetHashCode()].Contains(pb.GetHashCode()))
                    {
                        _springs.Add(new Spring2(pa, pb, RestLength));
                        _springPresenceTable[pa.GetHashCode()].Add(pb.GetHashCode());
                    }
                }
            }

            for (int i = _springs.Count - 1; i >= 0; i--)
            {
                Spring2 spring = _springs[i];
                spring.Update();

                // Stretch
                if (spring.CurrentDistance > (spring.RestLength + DeformationFactor))
                {
                    spring.RestLength += deltaTime * Plasticity * (spring.CurrentDistance - spring.RestLength - (YieldRatioStretch * spring.RestLength));
                }
                // Compress
                else if (spring.CurrentDistance < (spring.RestLength - DeformationFactor))
                {
                    spring.RestLength -= deltaTime * Plasticity * (spring.RestLength - (YieldRatioCompress * spring.RestLength) - spring.CurrentDistance);
                }
                // Remove springs with restLength longer than REST_LENGTH
                if (spring.RestLength > RestLength)
                {
                    _springs.RemoveAt(i);
                    _springPresenceTable[spring.PA.GetHashCode()].Remove(spring.PB.GetHashCode());
                }
                else
                {
                    if (spring.CurrentDistance == 0)
                        continue;

                    Vector2.Subtract(ref spring.PB.Position, ref spring.PA.Position, out _rij);
                    _rij /= spring.CurrentDistance;
                    float D = deltaTime * KSpring * (spring.RestLength - spring.CurrentDistance);
                    _rij *= (D * 0.5f);
                    spring.PA.Position = new Vector2(spring.PA.Position.X - _rij.X, spring.PA.Position.Y - _rij.Y);
                    spring.PB.Position = new Vector2(spring.PB.Position.X + _rij.X, spring.PB.Position.Y + _rij.Y);
                }
            }
        }

        private void ApplyGravity(Particle particle)
        {
            particle.Velocity = new Vector2(particle.Velocity.X + Gravity.X, particle.Velocity.Y + Gravity.Y);
        }

        private void ApplyViscosity(float deltaTime)
        {
            float u, q;
            for (int i = 0; i < Particles.Count; i++)
            {
                Particle particle = Particles[i];

                _tempParticles = Particles.GetNearby(particle);
               
                int len2 = _tempParticles.Count;
                if (len2 > MaxNeighbors)
                    len2 = MaxNeighbors;

                for (int j = 0; j < len2; j++)
                {
                    Particle tempParticle = _tempParticles[j];

                    Vector2.DistanceSquared(ref particle.Position, ref tempParticle.Position, out q);
                    if ((q < InfluenceRadiusSquared) && (q != 0))
                    {
                        q = (float)Math.Sqrt(q);
                        Vector2.Subtract(ref tempParticle.Position, ref particle.Position, out _rij);
                        Vector2.Divide(ref _rij, q, out _rij);

                        Vector2.Subtract(ref particle.Velocity, ref tempParticle.Velocity, out _tempVect);
                        Vector2.Dot(ref _tempVect, ref _rij, out u);
                        if (u <= 0.0f)
                            continue;

                        q /= InfluenceRadius;

                        float I = (deltaTime * (1 - q) * (ViscositySigma * u + ViscosityBeta * u * u));
                        Vector2.Multiply(ref _rij, (I * 0.5f), out _rij);
                        Vector2.Subtract(ref particle.Velocity, ref _rij, out _tempVect);
                        particle.Velocity = _tempVect;
                        _tempVect = tempParticle.Velocity;
                        _tempVect += _rij;
                        tempParticle.Velocity = _tempVect;
                    }
                }
            }
        }

        private void DoubleDensityRelaxation()
        {
            float q;
            for (int i = 0; i < Particles.Count; i++)
            {
                Particle particle = Particles[i];
                particle.Density = 0;
                particle.NearDensity = 0;

                _tempParticles = Particles.GetNearby(particle);
              
                int len2 = _tempParticles.Count;
                if (len2 > MaxNeighbors)
                    len2 = MaxNeighbors;

                for (int j = 0; j < len2; j++)
                {
                    Particle tempParticle = _tempParticles[j];

                    Vector2.DistanceSquared(ref particle.Position, ref tempParticle.Position, out q);
                    if (q < InfluenceRadiusSquared && q != 0)
                    {
                        q = (float)Math.Sqrt(q);
                        q /= InfluenceRadius;
                        float qq = ((1 - q) * (1 - q));
                        particle.Density += qq;
                        particle.NearDensity += qq * (1 - q);
                    }
                }

                particle.Pressure = (Stiffness * (particle.Density - DensityRest));
                particle.NearPressure = (StiffnessNear * particle.NearDensity);
                _dx = Vector2.Zero;

                for (int j = 0; j < len2; j++)
                {
                    Particle tempParticle = _tempParticles[j];

                    Vector2.DistanceSquared(ref particle.Position, ref tempParticle.Position, out q);
                    if ((q < InfluenceRadiusSquared) && (q != 0))
                    {
                        q = (float)Math.Sqrt(q);
                        Vector2.Subtract(ref tempParticle.Position, ref particle.Position, out _rij);
                        Vector2.Divide(ref _rij, q, out _rij);
                        q /= InfluenceRadius;

                        float D = (_deltaTime2 * (particle.Pressure * (1 - q) + particle.NearPressure * (1 - q) * (1 - q)));
                        Vector2.Multiply(ref _rij, (D * 0.5f), out _rij);
                        tempParticle.Position = new Vector2(tempParticle.Position.X + _rij.X, tempParticle.Position.Y + _rij.Y);
                        Vector2.Subtract(ref _dx, ref _rij, out _dx);
                    }
                }
                particle.Position = particle.Position + _dx;
            }
        }

        public void Update(float deltaTime)
        {
            if (deltaTime == 0)
                return;

            _deltaTime2 = deltaTime * deltaTime;

            ApplyViscosity(deltaTime);

            //Update velocity
            UpdateParticleVelocity(deltaTime);

            Particles.Rehash();

            if (_elasticityEnabled)
                CalculateElasticity(deltaTime);

            if (_plasticityEnabled)
                CalculatePlasticity(deltaTime);

            DoubleDensityRelaxation();

            for(int i = 0; i < Particles.Count; i++)
            {
                Particle particle = Particles[i];
                particle.Velocity = new Vector2((particle.Position.X - particle.PreviousPosition.X) / deltaTime, (particle.Position.Y - particle.PreviousPosition.Y) / deltaTime);
                ApplyGravity(particle);
                WallCollision(particle);
                CapVelocity(particle.Velocity);
            }
        }

        public void AddParticle(Vector2 position)
        {
            Particles.Add(new Particle(position.X, position.Y));
        }

    }

    public class Particle
    {
        public float Density;
        public float NearDensity;
        public float NearPressure;
        public Vector2 Position = new Vector2(0, 0);
        public float Pressure;
        public Vector2 PreviousPosition = new Vector2(0, 0);
        public Vector2 Velocity = new Vector2(0, 0);

        public Particle(float posX, float posY)
        {
            Position = new Vector2(posX, posY);
        }
    }

    public class Spring2
    {
        public float CurrentDistance;
        public Particle PA;
        public Particle PB;
        public float RestLength;

        public Spring2(Particle pa, Particle pb, float restLength)
        {
            PA = pa;
            PB = pb;
            RestLength = restLength;
        }

        public void Update()
        {
            Vector2.Distance(ref PA.Position, ref PB.Position, out CurrentDistance);
        }

        public bool Contains(Particle p)
        {
            return (PA.GetHashCode() == p.GetHashCode() || PB.GetHashCode() == p.GetHashCode());
        }
    }
}