using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;

namespace Barotrauma
{
    class LatchOntoAI
    {
        const float RaycastInterval = 5.0f;

        private float raycastTimer;

        private Structure targetWall;
        private Body targetBody;
        private Vector2 attachSurfaceNormal;
        private Submarine targetSubmarine;
        private readonly Character character;

        public bool AttachToSub { get; private set; }
        public bool AttachToWalls { get; private set; }

        private readonly float minDeattachSpeed, maxDeattachSpeed;
        private readonly float damageOnDetach, detachStun;
        private float deattachTimer;

        private Vector2 wallAttachPos;

        private float attachCooldown;

        private Limb attachLimb;
        private Vector2 localAttachPos;
        private float attachLimbRotation;

        private float jointDir;

        public List<WeldJoint> AttachJoints { get; } = new List<WeldJoint>();

        public Vector2? WallAttachPos
        {
            get;
            private set;
        }

        public bool IsAttached => AttachJoints.Count > 0;

        public bool IsAttachedToSub => IsAttached && targetSubmarine != null;

        public LatchOntoAI(XElement element, EnemyAIController enemyAI)
        {
            AttachToWalls = element.GetAttributeBool("attachtowalls", false);
            AttachToSub = element.GetAttributeBool("attachtosub", false);
            minDeattachSpeed = element.GetAttributeFloat("mindeattachspeed", 5.0f);
            maxDeattachSpeed = Math.Max(minDeattachSpeed, element.GetAttributeFloat("maxdeattachspeed", 8.0f));
            damageOnDetach = element.GetAttributeFloat("damageondetach", 0.0f);
            detachStun = element.GetAttributeFloat("detachstun", 0.0f);
            localAttachPos = ConvertUnits.ToSimUnits(element.GetAttributeVector2("localattachpos", Vector2.Zero));
            attachLimbRotation = MathHelper.ToRadians(element.GetAttributeFloat("attachlimbrotation", 0.0f));

            string limbString = element.GetAttributeString("attachlimb", null);
            attachLimb = enemyAI.Character.AnimController.Limbs.FirstOrDefault(l => string.Equals(l.Name, limbString, StringComparison.OrdinalIgnoreCase));
            if (attachLimb == null)
            {
                if (Enum.TryParse(limbString, out LimbType attachLimbType))
                {
                    attachLimb = enemyAI.Character.AnimController.GetLimb(attachLimbType);
                }
            }
            if (attachLimb == null)
            {
                attachLimb = enemyAI.Character.AnimController.MainLimb;
            }

            character = enemyAI.Character;
            enemyAI.Character.OnDeath += OnCharacterDeath;
        }

        public void SetAttachTarget(Structure wall, Vector2 attachPos, Vector2 attachSurfaceNormal)
        {
            if (wall == null) { return; }
            var sub = wall.Submarine;
            if (sub == null) { return; }
            targetWall = wall;
            targetSubmarine = sub;
            targetBody = targetSubmarine.PhysicsBody.FarseerBody;
            this.attachSurfaceNormal = attachSurfaceNormal;
            wallAttachPos = attachPos;
        }
        
        public void Update(EnemyAIController enemyAI, float deltaTime)
        {
            if (character.Submarine != null)
            {
                DeattachFromBody(reset: true);
                return;
            }
            if (AttachJoints.Count > 0)
            {
                if (Math.Sign(attachLimb.Dir) != Math.Sign(jointDir))
                {
                    AttachJoints[0].LocalAnchorA =
                        new Vector2(-AttachJoints[0].LocalAnchorA.X, AttachJoints[0].LocalAnchorA.Y);
                    AttachJoints[0].ReferenceAngle = -AttachJoints[0].ReferenceAngle;
                    jointDir = attachLimb.Dir;
                }
                for (int i = 0; i < AttachJoints.Count; i++)
                {
                    //something went wrong, limb body is very far from the joint anchor -> deattach
                    if (Vector2.DistanceSquared(AttachJoints[i].WorldAnchorB, AttachJoints[i].BodyA.Position) > 10.0f * 10.0f)
                    {
#if DEBUG
                        DebugConsole.ThrowError("Limb body of the character \"" + character.Name + "\" is very far from the attach joint anchor -> deattach");
#endif
                        DeattachFromBody(reset: true);
                        return;
                    }
                }
            }

            if (attachCooldown > 0)
            {
                attachCooldown -= deltaTime;
            }
            if (deattachTimer > 0)
            {
                deattachTimer -= deltaTime;
            }

            Vector2 transformedAttachPos = wallAttachPos;
            if (character.Submarine == null && targetSubmarine != null)
            {
                transformedAttachPos += ConvertUnits.ToSimUnits(targetSubmarine.Position);
            }
            if (transformedAttachPos != Vector2.Zero)
            {
                WallAttachPos = transformedAttachPos;
            }

            switch (enemyAI.State)
            {
                case AIState.Idle:
                    if (AttachToWalls && character.Submarine == null && Level.Loaded != null)
                    {
                        if (!IsAttached)
                        {
                            raycastTimer -= deltaTime;
                            //check if there are any walls nearby the character could attach to
                            if (raycastTimer < 0.0f)
                            {
                                wallAttachPos = Vector2.Zero;

                                var cells = Level.Loaded.GetCells(character.WorldPosition, 1);
                                if (cells.Count > 0)
                                {
                                    float closestDist = float.PositiveInfinity;
                                    foreach (Voronoi2.VoronoiCell cell in cells)
                                    {
                                        foreach (Voronoi2.GraphEdge edge in cell.Edges)
                                        {
                                            if (MathUtils.GetLineIntersection(edge.Point1, edge.Point2, character.WorldPosition, cell.Center, out Vector2 intersection))
                                            {
                                                Vector2 potentialAttachPos = ConvertUnits.ToSimUnits(intersection);
                                                float distSqr = Vector2.DistanceSquared(character.SimPosition, potentialAttachPos);
                                                if (distSqr < closestDist)
                                                {
                                                    attachSurfaceNormal = edge.GetNormal(cell);
                                                    targetBody = cell.Body;
                                                    wallAttachPos = potentialAttachPos;
                                                    closestDist = distSqr;
                                                }
                                                break;
                                            }
                                        }
                                    }
                                }
                                raycastTimer = RaycastInterval;
                            }
                        }                        
                    }
                    else
                    {
                        wallAttachPos = Vector2.Zero;
                    }

                    if (wallAttachPos == Vector2.Zero || targetBody == null)
                    {
                        DeattachFromBody(reset: false);
                    }
                    else
                    {
                        float squaredDistance = Vector2.DistanceSquared(character.SimPosition, wallAttachPos);
                        float targetDistance = Math.Max(Math.Max(character.AnimController.Collider.radius, character.AnimController.Collider.width), character.AnimController.Collider.height) * 1.2f;
                        if (squaredDistance < targetDistance * targetDistance)
                        {
                            //close enough to a wall -> attach
                            AttachToBody(wallAttachPos);
                            enemyAI.SteeringManager.Reset();
                        }
                        else
                        {
                            //move closer to the wall
                            DeattachFromBody(reset: false);
                            enemyAI.SteeringManager.SteeringAvoid(deltaTime, 1.0f, 0.1f);
                            enemyAI.SteeringManager.SteeringSeek(wallAttachPos);
                        }
                    }
                    break;
                case AIState.Attack:
                case AIState.Aggressive:
                    if (enemyAI.AttackingLimb != null)
                    {
                        if (AttachToSub && !enemyAI.IsSteeringThroughGap && wallAttachPos != Vector2.Zero && targetBody != null)
                        {
                            // is not attached or is attached to something else
                            if (!IsAttached || IsAttached && AttachJoints[0].BodyB != targetBody)
                            {
                                if (Vector2.DistanceSquared(ConvertUnits.ToDisplayUnits(transformedAttachPos), enemyAI.AttackingLimb.WorldPosition) < enemyAI.AttackingLimb.attack.DamageRange * enemyAI.AttackingLimb.attack.DamageRange)
                                {
                                    AttachToBody(transformedAttachPos);
                                }
                            }
                        }
                    }
                    break;
                default:
                    DeattachFromBody(reset: true);
                    break;
            }

            if (IsAttached && targetBody != null && targetWall != null && targetSubmarine != null && deattachTimer <= 0.0f)
            {
                bool deattach = false;
                // Deattach if the wall is broken enough where we are attached to
                int targetSection = targetWall.FindSectionIndex(attachLimb.WorldPosition, world: true, clamp: true);
                if (enemyAI.CanPassThroughHole(targetWall, targetSection))
                {
                    deattach = true;
                    attachCooldown = 2;
                }
                if (!deattach)
                {
                    // Deattach if the velocity is high
                    float velocity = targetSubmarine.Velocity == Vector2.Zero ? 0.0f : targetSubmarine.Velocity.Length();
                    deattach = velocity > maxDeattachSpeed;
                    if (!deattach)
                    {
                        if (velocity > minDeattachSpeed)
                        {
                            float velocityFactor = (maxDeattachSpeed - minDeattachSpeed <= 0.0f) ?
                                Math.Sign(Math.Abs(velocity) - minDeattachSpeed) :
                                (Math.Abs(velocity) - minDeattachSpeed) / (maxDeattachSpeed - minDeattachSpeed);

                            if (Rand.Range(0.0f, 1.0f) < velocityFactor)
                            {
                                deattach = true;
                                character.AddDamage(character.WorldPosition, new List<Affliction>() { AfflictionPrefab.InternalDamage.Instantiate(damageOnDetach) }, detachStun, true);
                                attachCooldown = detachStun * 2;
                            }
                        }
                    }
                }
                if (deattach)
                {
                    DeattachFromBody(reset: true);
                }
                deattachTimer = 5.0f;
            }
        }

        private void AttachToBody(Vector2 attachPos)
        {
            if (attachLimb == null) { return; }
            if (targetBody == null) { return; }
            if (attachCooldown > 0) { return; }
            var collider = character.AnimController.Collider;
            //already attached to something
            if (AttachJoints.Count > 0)
            {
                //already attached to the target body, no need to do anything
                if (AttachJoints[0].BodyB == targetBody) { return; }
                DeattachFromBody(reset: false);
            }

            jointDir = attachLimb.Dir;

            Vector2 transformedLocalAttachPos = localAttachPos * attachLimb.Scale * attachLimb.Params.Ragdoll.LimbScale;
            if (jointDir < 0.0f)
            {
                transformedLocalAttachPos.X = -transformedLocalAttachPos.X;
            }

            float angle = MathUtils.VectorToAngle(-attachSurfaceNormal) - MathHelper.PiOver2 + attachLimbRotation * attachLimb.Dir;
            attachLimb.body.SetTransform(attachPos + attachSurfaceNormal * transformedLocalAttachPos.Length(), angle);

            var limbJoint = new WeldJoint(attachLimb.body.FarseerBody, targetBody,
                transformedLocalAttachPos, targetBody.GetLocalPoint(attachPos), false)
            {
                FrequencyHz = 10.0f,
                DampingRatio = 0.5f,
                KinematicBodyB = true,
                CollideConnected = false,
            };
            GameMain.World.Add(limbJoint);
            AttachJoints.Add(limbJoint);

            // Limb scale is already taken into account when creating the collider.
            Vector2 colliderFront = collider.GetLocalFront();
            if (jointDir < 0.0f)
            {
                colliderFront.X = -colliderFront.X;
            }
            collider.SetTransform(attachPos + attachSurfaceNormal * colliderFront.Length(), MathUtils.VectorToAngle(-attachSurfaceNormal) - MathHelper.PiOver2);

            var colliderJoint = new WeldJoint(collider.FarseerBody, targetBody, colliderFront, targetBody.GetLocalPoint(attachPos), false)
            {
                FrequencyHz = 10.0f,
                DampingRatio = 0.5f,
                KinematicBodyB = true,
                CollideConnected = false,
                //Length = 0.1f
            };
            GameMain.World.Add(colliderJoint);
            AttachJoints.Add(colliderJoint);            
        }

        public void DeattachFromBody(bool reset, float cooldown = 0)
        {
            foreach (Joint joint in AttachJoints)
            {
                GameMain.World.Remove(joint);
            }
            AttachJoints.Clear();
            if (cooldown > 0)
            {
                attachCooldown = cooldown;
            }
            if (reset)
            {
                Reset();
            }
        }

        private void Reset()
        {
            targetWall = null;
            targetSubmarine = null;
            targetBody = null;
            WallAttachPos = null;
        }

        private void OnCharacterDeath(Character character, CauseOfDeath causeOfDeath)
        {
            DeattachFromBody(reset: true);
            character.OnDeath -= OnCharacterDeath;
        }
    }
}
