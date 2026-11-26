using FarseerPhysics.Common;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    public class SteeringManager
    {
        protected const float CircleDistance = 2.5f;
        protected const float CircleRadius = 0.3f;

        protected const float RayCastInterval = 0.5f;

        /// <summary>
        /// Half-width for "shoulder" raycasts that check if the character's full width can pass (only while swimming).
        /// This will help avoid trying to go through too narrow gaps, and hugging too close to a hull
        /// and ending up getting stuck on turrets and other protrusions that have no static body.
        /// </summary>
        public const float ShoulderRaycastWidth = 0.5f;

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

        /// <summary>
        /// Steer away from any obstacles in the direction the character is heading in. 
        /// Can be used to make the character go around obstacles (although not very reliably, as there's no proper pathfinding involved here).
        /// </summary>
        /// <param name="lookAheadDistance">How far ahead the character we look for obstacles? In sim units.</param>
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
        
        /// <param name="speed">Update speed for the steering. Should normally match the characters current animation speed.</param>
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
                // Can't steer faster than the max speed.
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

            targetVel = Vector2.Normalize(targetVel);

            lastSeekDir = targetVel;
            return targetVel * weight;
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

        /// <summary>
        /// Previous known direction the target was seeking towards, used in the avoidance behavior to see which direction we should look for obstacles in
        /// </summary>
        private Vector2? lastSeekDir;
        private Vector2 desiredAvoidDirection = Vector2.Zero;
        public float avoidDirStrength;
        protected virtual Vector2 DoSteeringAvoid(float deltaTime, float lookAheadDistance, float weight)
        {
            if (steering == Vector2.Zero || host.Steering == Vector2.Zero)
            {
                return Vector2.Zero;
            }

            float maxDistance = lookAheadDistance;
            if (Timing.TotalTime >= lastRayCastTime + RayCastInterval)
            {
                avoidRayCastHit = false;
                if (lastSeekDir != null)
                {
                    //If we're seeking towards a particular direction, do two raycasts:
                    //  1. A check in the direction we're trying to seek towards.
                    Vector2 avoidBasedOnLookAheadDir = GetDesiredAvoidDirection(lastSeekDir.Value, maxDistance);
                    //  2. A check in the direction we're actually currently moving towards (perhaps already avoiding some obstacle)
                    //     to prevent the character from ending up hugging some other obstacle not in the direction of the seek target-
                    //     Shorter distance and a higher weight to just strongly push the character away from the obstacle.
                    Vector2 avoidBasedOnHeading = GetDesiredAvoidDirection(Vector2.Normalize(host.Steering), maxDistance / 2) * 2.0f;
                    desiredAvoidDirection = (avoidBasedOnLookAheadDir + avoidBasedOnHeading);
                }
                else
                {
                    desiredAvoidDirection = GetDesiredAvoidDirection(Vector2.Normalize(host.Steering), maxDistance);
                }
                lastRayCastTime = (float)Timing.TotalTime;
            }

            if (!avoidRayCastHit)
            {
                //nothing hit, lerp avoidance to zero
                if (AvoidDir.LengthSquared() < 0.0001f)
                {
                    AvoidDir = Vector2.Zero;
                    return Vector2.Zero;
                }
                else
                {
                    AvoidDir -= Vector2.Normalize(AvoidDir) * deltaTime * 0.25f;
                }
            }
            else
            {
                if (desiredAvoidDirection.LengthSquared() < 0.0001f) { return Vector2.Zero; }
                AvoidDir = Vector2.Lerp(AvoidDir, desiredAvoidDirection, deltaTime * 2.5f);
            }

            Vector2 diff = AvoidRayCastHitPosition - host.SimPosition;
            float dist = diff.Length();

            float avoidDistance = lookAheadDistance * 1.25f;
            avoidDirStrength = MathHelper.Clamp(1.0f - dist / avoidDistance, 0.0f, 1.0f);

            //the seeking behavior will need to be called again for the seek dir to be set
            //(i.e. make sure we don't keep a stale value in lastSeekDir)
            lastSeekDir = null;

            return AvoidDir * weight * avoidDirStrength;
        }

        private Vector2 GetDesiredAvoidDirection(Vector2 lookAheadDir, float maxDistance)
        {
            AvoidLookAheadPos = host.SimPosition + lookAheadDir * maxDistance;
            Body closestBody = Submarine.CheckVisibility(host.SimPosition, AvoidLookAheadPos);

            // If center ray is clear (and character is swimming), check "shoulders" to ensure the full width can pass
            if (closestBody == null && host is AIController ai && ai.Character.AnimController.InWater)
            {
                Vector2 steeringDir = Vector2.Normalize(host.Steering);
                Vector2 perpendicular = new Vector2(-steeringDir.Y, steeringDir.X);

                // Check one shoulder
                Vector2 leftStart = host.SimPosition + perpendicular * ShoulderRaycastWidth;
                Vector2 leftEnd = AvoidLookAheadPos + perpendicular * ShoulderRaycastWidth;
                closestBody = Submarine.CheckVisibility(leftStart, leftEnd);

                // If one shoulder is clear, check the other one too
                if (closestBody == null)
                {
                    Vector2 rightStart = host.SimPosition - perpendicular * ShoulderRaycastWidth;
                    Vector2 rightEnd = AvoidLookAheadPos - perpendicular * ShoulderRaycastWidth;
                    closestBody = Submarine.CheckVisibility(rightStart, rightEnd);
                }
            }

            if (closestBody != null)
            {
                avoidRayCastHit = true;
                AvoidRayCastHitPosition = Submarine.LastPickedPosition;
                Vector2 desiredAvoidDirection = Submarine.LastPickedNormal;

                Vector2 dirNormalLeft = new Vector2(desiredAvoidDirection.Y, -desiredAvoidDirection.X);
                Vector2 dirNormalRight = new Vector2(-desiredAvoidDirection.Y, desiredAvoidDirection.X);
                //avoid in the direction that's closer to the host's current direction
                //(i.e. if the character is currently moving left to get around some obstacle, keep going in that direction)
                if (Vector2.Dot(lookAheadDir, dirNormalLeft) > Vector2.Dot(AvoidDir, dirNormalRight))
                {
                    desiredAvoidDirection =
                        //steer in the direction of the normal (= along the edge of the obstacle), + slightly away from it
                        dirNormalLeft + desiredAvoidDirection * 0.5f;
                }
                else
                {
                    desiredAvoidDirection = dirNormalRight + desiredAvoidDirection * 0.5f;
                }

                //add a bit of randomness
                desiredAvoidDirection = MathUtils.RotatePoint(desiredAvoidDirection, Rand.Range(-0.2f, 0.2f));

                return desiredAvoidDirection;
            }

            return Vector2.Zero;
        }
    }
}
