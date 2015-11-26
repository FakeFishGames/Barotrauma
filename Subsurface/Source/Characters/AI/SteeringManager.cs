using System;
using Microsoft.Xna.Framework;
using FarseerPhysics.Dynamics;

namespace Barotrauma
{
    class SteeringManager
    {
        protected const float CircleDistance = 2.5f;
        protected const float CircleRadius = 0.3f;

        protected const float RayCastInterval = 0.5f;

        protected ISteerable host;

        private Vector2 steering;

        //the steering amount when avoiding obstacles
        //(needs a separate variable because it's only updated when a raycast is done to detect any nearby obstacles)
        private Vector2 avoidSteering;
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

        public void SteeringSeek(Vector2 targetSimPos, float speed = 1.0f)
        {
            steering += DoSteeringSeek(targetSimPos, speed);
        }

        public void SteeringWander(float speed = 1.0f)
        {
            steering += DoSteeringWander(speed);
        }

        public void SteeringAvoid(float deltaTime, float speed)
        {
            steering += DoSteeringAvoid(deltaTime, speed);
        }

        public virtual void Update(float speed = 1.0f)
        {
            float steeringSpeed = steering.Length();
            if (steeringSpeed>speed)
            {
               steering = Vector2.Normalize(steering) * Math.Abs(speed);
            }

            host.Steering = steering;
        }

        protected virtual Vector2 DoSteeringSeek(Vector2 target, float speed = 1.0f)
        {
            Vector2 targetVel = target - host.SimPosition;

            if (targetVel.LengthSquared() < 0.00001f) return Vector2.Zero;

            targetVel = Vector2.Normalize(targetVel) * speed;
            Vector2 newSteering = targetVel - host.Steering;

            if (newSteering==Vector2.Zero) return Vector2.Zero;

            float steeringSpeed = (newSteering + host.Steering).Length();
            if (steeringSpeed > Math.Abs(speed))
            {
                newSteering = Vector2.Normalize(newSteering)*Math.Abs(speed);
            }

            return newSteering;
        }

        protected virtual Vector2 DoSteeringWander(float speed = 1.0f)
        {
            Vector2 circleCenter = (host.Velocity == Vector2.Zero) ? new Vector2(speed, 0.0f) : host.Velocity;
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

        protected virtual Vector2 DoSteeringAvoid(float deltaTime, float speed = 1.0f)
        {
            if (steering == Vector2.Zero || host.Steering == Vector2.Zero) return Vector2.Zero;

            float maxDistance = 2.0f;

            Vector2 ahead = host.SimPosition + Vector2.Normalize(host.Steering)*maxDistance;

            if (rayCastTimer <= 0.0f)
            {
                rayCastTimer = RayCastInterval;
                Body closestBody = Submarine.CheckVisibility(host.SimPosition, ahead);
                if (closestBody == null)
                {
                    avoidSteering = Vector2.Zero;
                    return Vector2.Zero;                    
                }
                else
                {
                    Structure closestStructure = closestBody.UserData as Structure;
                    if (closestStructure!=null)
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

                        avoidSteering = Vector2.Normalize(Submarine.LastPickedPosition - obstaclePosition);
                    }
                }
                
            }
            else
            {
                rayCastTimer -= deltaTime;
            }

            return avoidSteering * speed;

        }
    }
}
