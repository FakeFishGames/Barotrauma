using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Barotrauma
{
    class SwarmBehavior
    {
        private float minDistFromClosest;
        private float maxDistFromCenter;
        private float cohesion;

        private List<AICharacter> members = new List<AICharacter>();

        private AIController ai;

        public SwarmBehavior(XElement element, AIController ai)
        {
            this.ai = ai;
            minDistFromClosest = ConvertUnits.ToSimUnits(element.GetAttributeFloat("mindistfromclosest", 10.0f));
            maxDistFromCenter = ConvertUnits.ToSimUnits(element.GetAttributeFloat("maxdistfromcenter", 1000.0f));
            cohesion = element.GetAttributeFloat("cohesion", 0.1f);
        }

        public static void CreateSwarm(IEnumerable<AICharacter> swarm)
        {
            foreach (AICharacter character in swarm)
            {
                if (character.AIController is EnemyAIController enemyAI && enemyAI.SwarmBehavior != null)
                {
                    enemyAI.SwarmBehavior.members = swarm.ToList();
                }
            }
        }

        public void Update(float deltaTime)
        {
            members.RemoveAll(m => m.IsDead || m.Removed);
            if (members.Count < 2) { return; }

            //calculate the "center of mass" of the swarm and the distance to the closest character in the swarm
            float closestDistSqr = float.MaxValue;
            Vector2 center = Vector2.Zero;
            AICharacter closest = null;
            foreach (AICharacter member in members)
            {
                center += member.SimPosition;
                if (member == ai.Character) { continue; }
                float distSqr = Vector2.DistanceSquared(member.SimPosition, ai.Character.SimPosition);
                if (distSqr < closestDistSqr)
                {
                    closestDistSqr = distSqr;
                    closest = member;
                }
            }
            center /= members.Count;

            if (closest == null) { return; }

            //steer away from the closest if too close
            float closestDist = (float)Math.Sqrt(closestDistSqr);
            if (closestDist < minDistFromClosest)
            {
                Vector2 diff = closest.SimPosition - ai.SimPosition;
                if (diff.LengthSquared() < 0.0001f)
                {
                    diff = Vector2.UnitX;
                }
                ai.SteeringManager.SteeringManual(deltaTime, -diff);
            }
            //steer closer to the center of mass if too far
            else if (Vector2.DistanceSquared(center, ai.SimPosition) > maxDistFromCenter * maxDistFromCenter)
            {
                float distFromCenter = Vector2.Distance(center, ai.SimPosition);
                ai.SteeringManager.SteeringSeek(center, (distFromCenter - maxDistFromCenter) / 10.0f);
            }

            //keep the characters moving in roughly the same direction
            if (cohesion > 0.0f)
            {
                Vector2 avgVel = Vector2.Zero;
                foreach (AICharacter member in members)
                {
                    avgVel += member.AnimController.TargetMovement;
                }
                avgVel /= members.Count;
                ai.SteeringManager.SteeringManual(deltaTime, avgVel * cohesion);
            }
        }
    }
}
