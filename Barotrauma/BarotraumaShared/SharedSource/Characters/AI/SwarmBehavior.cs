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
        private readonly float minDistFromClosest;
        private readonly float maxDistFromCenter;
        private readonly float cohesion;
        public bool ForceActive { get; private set; }

        public List<AICharacter> Members { get; private set; } = new List<AICharacter>();
        public HashSet<AICharacter> ActiveMembers { get; private set; } = new HashSet<AICharacter>();

        private EnemyAIController ai;

        public bool IsActive { get; set; }
        public bool IsEnoughMembers => ActiveMembers.Count > 1;


        public SwarmBehavior(XElement element, EnemyAIController ai)
        {
            this.ai = ai;
            minDistFromClosest = ConvertUnits.ToSimUnits(element.GetAttributeFloat(nameof(minDistFromClosest), 10.0f));
            maxDistFromCenter = ConvertUnits.ToSimUnits(element.GetAttributeFloat(nameof(maxDistFromCenter), 1000.0f));
            cohesion = element.GetAttributeFloat(nameof(cohesion), 1) / 10;
            ForceActive = element.GetAttributeBool(nameof(ForceActive), false);
        }

        public static void CreateSwarm(IEnumerable<AICharacter> swarm)
        {
            var aiControllers = new List<EnemyAIController>();
            foreach (AICharacter character in swarm)
            {
                if (character.AIController is EnemyAIController enemyAI && enemyAI.SwarmBehavior != null)
                {
                    aiControllers.Add(enemyAI);
                }
            }
            var filteredMembers = aiControllers.Select(m => m.Character as AICharacter).Where(m => m != null);
            foreach (EnemyAIController ai in aiControllers)
            {
                ai.SwarmBehavior.Members = filteredMembers.ToList();
            }
        }

        public void Refresh()
        {
            Members.RemoveAll(m => m.IsDead || m.Removed || m.AIController is EnemyAIController ai && ai.State == AIState.Flee);
            foreach (var member in Members)
            {
                if (!member.AIController.Enabled && member.IsRemotePlayer || Character.Controlled == member || !((EnemyAIController)member.AIController).SwarmBehavior.IsActive)
                {
                    ActiveMembers.Remove(member);
                }
                else
                {
                    ActiveMembers.Add(member);
                }
            }
        }

        public void UpdateSteering(float deltaTime)
        {
            if (!IsActive) { return; }
            if (!IsEnoughMembers) { return; }
            //calculate the "center of mass" of the swarm and the distance to the closest character in the swarm
            float closestDistSqr = float.MaxValue;
            Vector2 center = Vector2.Zero;
            AICharacter closest = null;
            foreach (AICharacter member in Members)
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
            center /= Members.Count;

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
                foreach (AICharacter member in Members)
                {
                    avgVel += member.AnimController.TargetMovement;
                }
                avgVel /= Members.Count;
                ai.SteeringManager.SteeringManual(deltaTime, avgVel * cohesion);
            }
        }
    }
}
