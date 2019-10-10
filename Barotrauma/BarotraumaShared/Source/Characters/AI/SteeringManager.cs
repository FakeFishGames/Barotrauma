using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using FarseerPhysics;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class SteeringManager
    {
        protected const float CircleDistance = 2.5f;
        protected const float CircleRadius = 0.3f;

        protected const float RayCastInterval = 0.5f;

        protected ISteerable host;

        protected Vector2 steering;

        private Vector2? avoidObstaclePos;
        private float rayCastTimer;

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

        public void SteeringWander(float weight = 1)
        {
            steering += DoSteeringWander(weight);
        }

        public void SteeringAvoid(float deltaTime, float lookAheadDistance, float weight = 1, Vector2? heading = null)
        {
            steering += DoSteeringAvoid(deltaTime, lookAheadDistance, weight, heading);
        }

        public void SteeringManual(float deltaTime, Vector2 velocity)
        {
            steering += velocity;
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
            host.Steering = steering;
        }

        protected virtual Vector2 DoSteeringSeek(Vector2 target, float weight)
        {
            Vector2 targetVel = target - host.SimPosition;

            if (targetVel.LengthSquared() < 0.00001f) return Vector2.Zero;

            targetVel = Vector2.Normalize(targetVel) * weight;
            Vector2 newSteering = targetVel - host.Steering;

            if (newSteering == Vector2.Zero) return Vector2.Zero;

            float steeringSpeed = (newSteering + host.Steering).Length();
            if (steeringSpeed > Math.Abs(weight))
            {
                newSteering = Vector2.Normalize(newSteering) * Math.Abs(weight);
            }

            return newSteering;
        }

        protected virtual Vector2 DoSteeringWander(float weight)
        {
            Vector2 circleCenter = (host.Steering == Vector2.Zero) ? Vector2.UnitY : host.Steering;
            circleCenter = Vector2.Normalize(circleCenter) * CircleDistance;

            Vector2 displacement = new Vector2(
                (float)Math.Cos(wanderAngle),
                (float)Math.Sin(wanderAngle));
            displacement = displacement * CircleRadius;

            float angleChange = 1.5f;
            
            wanderAngle += Rand.Range(0.0f, 1.0f) * angleChange - angleChange * 0.5f;

            Vector2 newSteering = circleCenter + displacement;
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
            if (rayCastTimer <= 0.0f)
            {
                Vector2 ahead = host.SimPosition + Vector2.Normalize(host.Steering) * maxDistance;
                rayCastTimer = RayCastInterval;
                Body closestBody = Submarine.CheckVisibility(host.SimPosition, ahead);
                if (closestBody == null)
                {
                    avoidObstaclePos = null;
                    return Vector2.Zero;
                }
                else
                {
                    // TODO: Doesn't take items into account (like turrets)
                    if (closestBody.UserData is Structure closestStructure)
                    {
                        Vector2 obstaclePosition = Submarine.LastPickedPosition;
                        if (closestStructure.IsHorizontal)
                        {
                            obstaclePosition.Y = closestStructure.SimPosition.Y;
                        }
                        else
                        {
                            obstaclePosition.X = closestStructure.SimPosition.X;
                        }
                        avoidObstaclePos = obstaclePosition;
                    }
                    else
                    {
                        avoidObstaclePos = Submarine.LastPickedPosition;
                    }
                }
            }
            else
            {
                rayCastTimer -= deltaTime;
            }
            if (!avoidObstaclePos.HasValue)
            {
                return Vector2.Zero;
            }
            Vector2 diff = avoidObstaclePos.Value - host.SimPosition;
            float dist = diff.Length();

            if (dist > maxDistance)
            {
                return Vector2.Zero;
            }
            if (heading.HasValue)
            {
                var f = heading ?? host.Steering;
                // Avoid to left or right depending on the current heading
                Vector2 relativeVector = Vector2.Normalize(diff) - Vector2.Normalize(f);
                var dir = relativeVector.X > 0 ? diff.Right() : diff.Left();
                float factor = 1.0f - Math.Min(dist / maxDistance, 1);
                return dir * factor * weight;
            }
            else
            {
                // Doesn't work right because it effectively just slows down or reverses the movement, where as we'd like to go right or left to avoid the target.
                // There's also another issue, which also affects going right or left: the raycast doesn't hit anything if we turn too much -> avoiding doesn't work well.
                // Could probably "remember" the avoidance a bit longer so that the avoid steering is not immedieately disgarded, but kept for a while and reduced gradually?
                return -diff * (1.0f - dist / maxDistance) * weight;
            }
        }
    }
}
