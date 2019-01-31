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

        private Vector2 steering;

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

        public void SteeringSeek(Vector2 targetSimPos, float speed)
        {
            steering += DoSteeringSeek(targetSimPos, speed);
        }

        public void SteeringWander(float speed)
        {
            steering += DoSteeringWander(speed);
        }

        public void SteeringAvoid(float deltaTime, float lookAheadDistance, float speed)
        {
            steering += DoSteeringAvoid(deltaTime, lookAheadDistance, speed);
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

            float steeringSpeed = steering.Length();
            if (steeringSpeed > speed)
            {
                steering = Vector2.Normalize(steering) * Math.Abs(speed);
            }

            host.Steering = steering;
        }

        protected virtual Vector2 DoSteeringSeek(Vector2 target, float speed)
        {
            Vector2 targetVel = target - host.SimPosition;

            if (targetVel.LengthSquared() < 0.00001f) return Vector2.Zero;

            targetVel = Vector2.Normalize(targetVel) * speed;
            Vector2 newSteering = targetVel - host.Steering;

            if (newSteering == Vector2.Zero) return Vector2.Zero;

            float steeringSpeed = (newSteering + host.Steering).Length();
            if (steeringSpeed > Math.Abs(speed))
            {
                newSteering = Vector2.Normalize(newSteering) * Math.Abs(speed);
            }

            return newSteering;
        }

        protected virtual Vector2 DoSteeringWander(float speed)
        {
            Vector2 circleCenter = (host.Steering == Vector2.Zero) ? Rand.Vector(speed) : host.Steering;
            circleCenter = Vector2.Normalize(circleCenter) * CircleDistance;

            Vector2 displacement = new Vector2(
                (float)Math.Cos(wanderAngle),
                (float)Math.Sin(wanderAngle));
            displacement = displacement * CircleRadius;

            float angleChange = 1.5f;
            
            wanderAngle += Rand.Range(0.0f, 1.0f) * angleChange - angleChange * 0.5f;

            Vector2 newSteering = circleCenter + displacement;
            float steeringSpeed = (newSteering + host.Steering).Length();
            if (steeringSpeed > speed)
            {
                newSteering = Vector2.Normalize(newSteering) * speed;
            }

            return newSteering;
        }

        protected virtual Vector2 DoSteeringAvoid(float deltaTime, float lookAheadDistance, float speed)
        {
            if (steering == Vector2.Zero || host.Steering == Vector2.Zero) return Vector2.Zero;

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
                        //avoidSteering = Vector2.Normalize(Submarine.LastPickedPosition - obstaclePosition);
                    }
                    /*else if (closestBody.UserData is Item)
                    {
                        avoidSteering = Vector2.Normalize(Submarine.LastPickedPosition - item.SimPosition);
                    }*/
                    else
                    {

                        avoidObstaclePos = Submarine.LastPickedPosition;
                        //avoidSteering = Vector2.Normalize(host.SimPosition - Submarine.LastPickedPosition);
                    }
                }

            }
            else
            {
                rayCastTimer -= deltaTime;
            }

            if (!avoidObstaclePos.HasValue) return Vector2.Zero;

            Vector2 diff = avoidObstaclePos.Value - host.SimPosition;
            float dist = diff.Length();

            if (dist > maxDistance) return Vector2.Zero;

            return -diff * (1.0f - dist / maxDistance) * speed;
        }

    }
}
