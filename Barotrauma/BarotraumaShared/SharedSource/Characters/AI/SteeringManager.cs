using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    class SteeringManager
    {
        protected const float CircleDistance = 2.5f;
        protected const float CircleRadius = 0.3f;

        protected const float RayCastInterval = 0.5f;

        protected ISteerable host;

        protected Vector2 steering;
        
        private float lastRayCastTime;

        private bool avoidRayCastHit;

        public Vector2 AvoidDir { get; private set; }
        public Vector2 AvoidRayCastHitPosition { get; private set; }
        public Vector2 AvoidLookAheadPos { get; private set; }

        private float wanderAngle;

        public float WanderAngle
        {
            get { return wanderAngle; }
            set { wanderAngle = value; }
        }

        public SteeringManager(ISteerable host)
        {
            this.host = host;

            wanderAngle = Rand.Range(0.0f, MathHelper.TwoPi);
        }

        public void SteeringSeek(Vector2 targetSimPos, float weight = 1)
        {
            steering += DoSteeringSeek(targetSimPos, weight);
        }

        public void SteeringWander(float weight = 1, bool avoidWanderingOutsideLevel = false)
        {
            steering += DoSteeringWander(weight, avoidWanderingOutsideLevel);
        }

        public void SteeringAvoid(float deltaTime, float lookAheadDistance, float weight = 1)
        {
            steering += DoSteeringAvoid(deltaTime, lookAheadDistance, weight);
        }

        public void SteeringManual(float deltaTime, Vector2 velocity)
        {
            if (MathUtils.IsValid(velocity))
            {
                steering += velocity;
            }
        }

        public void Reset()
        {
            steering = Vector2.Zero;
        }

        public void ResetX()
        {
            steering.X = 0.0f;
        }

        public void ResetY()
        {
            steering.Y = 0.0f;
        }

        public virtual void Update(float speed)
        {
            if (steering == Vector2.Zero || !MathUtils.IsValid(steering))
            {
                steering = Vector2.Zero;
                host.Steering = Vector2.Zero;
                return;
            }
            if (steering.LengthSquared() > speed * speed)
            {
                steering = Vector2.Normalize(steering) * Math.Abs(speed);
            }
            if (host is AIController aiController && aiController?.Character.CharacterHealth.GetAfflictionOfType("invertcontrols".ToIdentifier()) != null)
            {
                steering = -steering;
            }
            host.Steering = steering;
        }

        protected virtual Vector2 DoSteeringSeek(Vector2 target, float weight)
        {
            Vector2 targetVel = target - host.SimPosition;

            if (targetVel.LengthSquared() < 0.00001f) { return Vector2.Zero; }

            targetVel = Vector2.Normalize(targetVel) * weight;
            // TODO: the code below doesn't quite work as it should, and I'm not sure what the purpose of it is/was.
            // So, we'll just return the targetVel for now, as it produces smooth results.
            return targetVel;

            //Vector2 newSteering = targetVel - host.Steering;

            //if (newSteering == Vector2.Zero) return Vector2.Zero;

            //float steeringSpeed = (newSteering + host.Steering).Length();
            //if (steeringSpeed > Math.Abs(weight))
            //{
            //    newSteering = Vector2.Normalize(newSteering) * Math.Abs(weight);
            //}

            //return newSteering;
        }

        protected virtual Vector2 DoSteeringWander(float weight, bool avoidWanderingOutsideLevel)
        {
            Vector2 circleCenter = (host.Steering == Vector2.Zero) ? Vector2.UnitY : host.Steering;
            circleCenter = Vector2.Normalize(circleCenter) * CircleDistance;

            Vector2 displacement = new Vector2(
                (float)Math.Cos(wanderAngle),
                (float)Math.Sin(wanderAngle));
            displacement *= CircleRadius;

            float angleChange = 1.5f;
            
            wanderAngle += Rand.Range(0.0f, 1.0f) * angleChange - angleChange * 0.5f;

            Vector2 newSteering = circleCenter + displacement;
            if (avoidWanderingOutsideLevel && Level.Loaded != null)
            {
                float margin = 5000.0f;
                if (host.WorldPosition.X < -margin)
                {
                    // Too far left
                    newSteering.X += (-margin - host.WorldPosition.X) * weight / margin;
                }
                else if (host.WorldPosition.X > Level.Loaded.Size.X - margin)
                {
                    // Too far right
                    newSteering.X -= (host.WorldPosition.X - (Level.Loaded.Size.X - margin)) * weight / margin;
                }
            }

            float steeringSpeed = (newSteering + host.Steering).Length();
            if (steeringSpeed > weight)
            {
                newSteering = Vector2.Normalize(newSteering) * weight;
            }


            return newSteering;
        }

        protected virtual Vector2 DoSteeringAvoid(float deltaTime, float lookAheadDistance, float weight, Vector2? heading = null)
        {
            if (steering == Vector2.Zero || host.Steering == Vector2.Zero)
            {
                return Vector2.Zero;
            }

            float maxDistance = lookAheadDistance;
            if (Timing.TotalTime >= lastRayCastTime + RayCastInterval)
            {
                avoidRayCastHit = false;
                AvoidLookAheadPos = host.SimPosition + Vector2.Normalize(host.Steering) * maxDistance;
                lastRayCastTime = (float)Timing.TotalTime;
                Body closestBody = Submarine.CheckVisibility(host.SimPosition, AvoidLookAheadPos);
                if (closestBody != null)
                {
                    avoidRayCastHit = true;
                    AvoidRayCastHitPosition = Submarine.LastPickedPosition;
                    AvoidDir = Submarine.LastPickedNormal;
                    //add a bit of randomness
                    AvoidDir = MathUtils.RotatePoint(AvoidDir, Rand.Range(-0.15f, 0.15f));
                    //wait a bit longer for the next raycast
                    lastRayCastTime += RayCastInterval;
                }
            }

            if (AvoidDir.LengthSquared() < 0.0001f) { return Vector2.Zero; }

            //if raycast hit nothing, lerp avoid dir to zero
            if (!avoidRayCastHit)
            {
                AvoidDir -= Vector2.Normalize(AvoidDir) * deltaTime * 0.5f;
            }

            Vector2 diff = AvoidRayCastHitPosition - host.SimPosition;
            float dist = diff.Length();

            //> 0 when heading in the same direction as the obstacle, < 0 when away from it
            float dot = MathHelper.Clamp(Vector2.Dot(diff / dist, host.Steering), 0.0f, 1.0f);
            if (dot < 0) { return Vector2.Zero; }

            return AvoidDir * dot * weight * MathHelper.Clamp(1.0f - dist / lookAheadDistance, 0.0f, 1.0f);            
        }
    }
}
