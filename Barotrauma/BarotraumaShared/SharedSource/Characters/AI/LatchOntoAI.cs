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

        private Body attachTargetBody;
        private Vector2 attachSurfaceNormal;
        private Submarine attachTargetSubmarine;

        public bool AttachToSub { get; private set; }
        public bool AttachToWalls { get; private set; }

        private float minDeattachSpeed = 3.0f, maxDeattachSpeed = 10.0f;
        private float damageOnDetach = 0.0f, detachStun = 0.0f;
        private float deattachTimer;

        private Vector2 wallAttachPos;

        private float attachCooldown;

        private Limb attachLimb;
        private Vector2 localAttachPos;
        private float attachLimbRotation;

        private float jointDir;
        
        private List<WeldJoint> attachJoints = new List<WeldJoint>();

        public List<WeldJoint> AttachJoints
        {
            get { return attachJoints; }
        }

        public Vector2? WallAttachPos
        {
            get;
            private set;
        }

        public bool IsAttached
        {
            get { return attachJoints.Count > 0; }
        }

        public bool IsAttachedToSub => IsAttached && (attachTargetBody?.UserData is Submarine || attachTargetBody?.UserData is Entity entity && entity.Submarine != null);

        public LatchOntoAI(XElement element, EnemyAIController enemyAI)
        {
            AttachToWalls = element.GetAttributeBool("attachtowalls", false);
            AttachToSub = element.GetAttributeBool("attachtosub", false);
            minDeattachSpeed = element.GetAttributeFloat("mindeattachspeed", 3.0f);
            maxDeattachSpeed = Math.Max(minDeattachSpeed, element.GetAttributeFloat("maxdeattachspeed", 10.0f));
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

            enemyAI.Character.OnDeath += OnCharacterDeath;
        }

        public void SetAttachTarget(Body attachTarget, Submarine attachTargetSub, Vector2 attachPos, Vector2 attachSurfaceNormal)
        {
            attachTargetBody = attachTarget;
            attachTargetSubmarine = attachTargetSub;
            this.attachSurfaceNormal = attachSurfaceNormal;
            wallAttachPos = attachPos;
        }
        
        public void Update(EnemyAIController enemyAI, float deltaTime)
        {
            Character character = enemyAI.Character;

            if (character.Submarine != null)
            {
                DeattachFromBody();
                WallAttachPos = null;
                return;
            }
            if (attachJoints.Count > 0)
            {
                if (Math.Sign(attachLimb.Dir) != Math.Sign(jointDir))
                {
                    attachJoints[0].LocalAnchorA =
                        new Vector2(-attachJoints[0].LocalAnchorA.X, attachJoints[0].LocalAnchorA.Y);
                    attachJoints[0].ReferenceAngle = -attachJoints[0].ReferenceAngle;
                    jointDir = attachLimb.Dir;
                }
                for (int i = 0; i < attachJoints.Count; i++)
                {
                    //something went wrong, limb body is very far from the joint anchor -> deattach
                    if (Vector2.DistanceSquared(attachJoints[i].WorldAnchorB, attachJoints[i].BodyA.Position) > 10.0f * 10.0f)
                    {
#if DEBUG
                        DebugConsole.ThrowError("Limb body of the character \"" + character.Name + "\" is very far from the attach joint anchor -> deattach");
#endif
                        DeattachFromBody();
                        return;
                    }
                }
            }

            attachCooldown -= deltaTime;
            deattachTimer -= deltaTime;

            Vector2 transformedAttachPos = wallAttachPos;
            if (character.Submarine == null && attachTargetSubmarine != null)
            {
                transformedAttachPos += ConvertUnits.ToSimUnits(attachTargetSubmarine.Position);
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
                                                attachSurfaceNormal = edge.GetNormal(cell);
                                                attachTargetBody = cell.Body;
                                                Vector2 potentialAttachPos = ConvertUnits.ToSimUnits(intersection);
                                                float distSqr = Vector2.DistanceSquared(character.SimPosition, wallAttachPos);
                                                if (distSqr < closestDist)
                                                {
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

                    if (wallAttachPos == Vector2.Zero)
                    {
                        DeattachFromBody();
                    }
                    else
                    {
                        float squaredDistance = Vector2.DistanceSquared(character.SimPosition, wallAttachPos);
                        float targetDistance = Math.Max(Math.Max(character.AnimController.Collider.radius, character.AnimController.Collider.width), character.AnimController.Collider.height) * 1.2f;
                        if (squaredDistance < targetDistance * targetDistance)
                        {
                            //close enough to a wall -> attach
                            AttachToBody(character.AnimController.Collider, attachLimb, attachTargetBody, wallAttachPos);
                            enemyAI.SteeringManager.Reset();
                        }
                        else
                        {
                            //move closer to the wall
                            DeattachFromBody();
                            enemyAI.SteeringManager.SteeringAvoid(deltaTime, 1.0f, 0.1f);
                            enemyAI.SteeringManager.SteeringSeek(wallAttachPos);
                        }
                    }
                    break;
                case AIState.Attack:
                case AIState.Aggressive:
                    if (enemyAI.AttackingLimb != null)
                    {
                        if (AttachToSub && !enemyAI.IsSteeringThroughGap && wallAttachPos != Vector2.Zero && attachTargetBody != null)
                        {
                            // is not attached or is attached to something else
                            if (!IsAttached || IsAttached && attachJoints[0].BodyB != attachTargetBody)
                            {
                                if (Vector2.DistanceSquared(ConvertUnits.ToDisplayUnits(transformedAttachPos), enemyAI.AttackingLimb.WorldPosition) < enemyAI.AttackingLimb.attack.DamageRange * enemyAI.AttackingLimb.attack.DamageRange)
                                {
                                    AttachToBody(character.AnimController.Collider, attachLimb, attachTargetBody, transformedAttachPos);
                                }
                            }
                        }
                    }
                    break;
                default:
                    WallAttachPos = null;
                    DeattachFromBody();
                    break;
            }

            if (IsAttached && attachTargetBody != null && deattachTimer < 0.0f)
            {
                Entity entity = attachTargetBody.UserData as Entity;
                Submarine attachedSub = entity is Submarine sub ? sub : entity?.Submarine;
                if (attachedSub != null)
                {
                    float velocity = attachedSub.Velocity == Vector2.Zero ? 0.0f : attachedSub.Velocity.Length();
                    float velocityFactor = (maxDeattachSpeed - minDeattachSpeed <= 0.0f) ?
                        Math.Sign(Math.Abs(velocity) - minDeattachSpeed) :
                        (Math.Abs(velocity) - minDeattachSpeed) / (maxDeattachSpeed - minDeattachSpeed);

                    if (Rand.Range(0.0f, 1.0f) < velocityFactor)
                    {
                        DeattachFromBody();
                        character.AddDamage(character.WorldPosition, new List<Affliction>() { AfflictionPrefab.InternalDamage.Instantiate(damageOnDetach) }, detachStun, true);
                        attachCooldown = 5.0f;
                    }
                }

                deattachTimer = 5.0f;
            }
        }

        private void AttachToBody(PhysicsBody collider, Limb attachLimb, Body targetBody, Vector2 attachPos)
        {
            //already attached to something
            if (attachJoints.Count > 0)
            {
                //already attached to the target body, no need to do anything
                if (attachJoints[0].BodyB == targetBody) { return; }
                DeattachFromBody();
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
            attachJoints.Add(limbJoint);

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
            attachJoints.Add(colliderJoint);            
        }

        public void DeattachFromBody()
        {
            foreach (Joint joint in attachJoints)
            {
                GameMain.World.Remove(joint);
            }
            attachJoints.Clear();            
        }

        private void OnCharacterDeath(Character character, CauseOfDeath causeOfDeath)
        {
            DeattachFromBody();
            character.OnDeath -= OnCharacterDeath;
        }
    }
}
