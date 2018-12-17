using FarseerPhysics;
using FarseerPhysics.Common;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Xml.Linq;
using Barotrauma.Extensions;

namespace Barotrauma
{
    class LatchOntoAI
    {
        const float RaycastInterval = 5.0f;

        private float raycastTimer;

        private Body attachTargetBody;
        private Vector2 attachSurfaceNormal;
        private Submarine attachTargetSubmarine;

        private bool attachToSub;
        private bool attachToWalls;

        private float minDeattachSpeed = 3.0f, maxDeattachSpeed = 10.0f;
        private float damageOnDetach = 0.0f, detachStun = 0.0f;
        private float deattachTimer;

        private Vector2 wallAttachPos;

        private float attachCooldown;

        private readonly List<Limb> attachLimbs;
        private Vector2 localAttachPos;
        private float attachLimbRotation;

        private float jointDir;

        private Dictionary<Limb, WeldJoint> attachJoints = new Dictionary<Limb, WeldJoint>();
        private Dictionary<Limb, WeldJoint> colliderJoints = new Dictionary<Limb, WeldJoint>();
        public IEnumerable<WeldJoint> AttachJoints => attachJoints.Values;
        public bool IsAttached => attachJoints.Count > 0;

        public readonly Vector2[] WallAttachPositions;

        public LatchOntoAI(XElement element, EnemyAIController enemyAI)
        {
            attachToWalls = element.GetAttributeBool("attachtowalls", false);
            attachToSub = element.GetAttributeBool("attachtosub", false);
            minDeattachSpeed = element.GetAttributeFloat("mindeattachspeed", 3.0f);
            maxDeattachSpeed = Math.Max(minDeattachSpeed, element.GetAttributeFloat("maxdeattachspeed", 10.0f));
            damageOnDetach = element.GetAttributeFloat("damageondetach", 0.0f);
            detachStun = element.GetAttributeFloat("detachstun", 0.0f);
            localAttachPos = ConvertUnits.ToSimUnits(element.GetAttributeVector2("localattachpos", Vector2.Zero));
            attachLimbRotation = MathHelper.ToRadians(element.GetAttributeFloat("attachlimbrotation", 0.0f));

            if (Enum.TryParse(element.GetAttributeString("attachlimb", "None"), out LimbType attachLimbType))
            {
                if (attachLimbType == LimbType.None)
                {
                    attachLimbs = new List<Limb>();
                    var limbTypes = element.GetAttributeStringArray("attachlimbs", new string[0]);
                    foreach (string limbTypeString in limbTypes)
                    {
                        if (Enum.TryParse(limbTypeString, out LimbType limbType))
                        {
                            Limb attachLimb = enemyAI.Character.AnimController.GetLimb(limbType);
                            if (attachLimb != null)
                            {
                                attachLimbs.Add(attachLimb);
                            }
                        }
                    }
                }
                else
                {
                    attachLimbs = new List<Limb>() { enemyAI.Character.AnimController.GetLimb(attachLimbType) ?? enemyAI.Character.AnimController.MainLimb };
                }
            }
            WallAttachPositions = new Vector2[attachLimbs.Count];
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
                DetachAll();
                wallAttachPos = Vector2.Zero;
                WallAttachPositions.ForEach(p => p = Vector2.Zero);
                return;
            }

            foreach (var attachLimb in attachLimbs)
            {
                if (Math.Sign(attachLimb.Dir) != Math.Sign(jointDir) && IsAttached)
                {
                    if (attachJoints.TryGetValue(attachLimb, out WeldJoint joint))
                    {
                        joint.LocalAnchorA = new Vector2(-joint.LocalAnchorA.X, joint.LocalAnchorA.Y);
                        joint.ReferenceAngle = -joint.ReferenceAngle;
                        jointDir = attachLimb.Dir;
                    }
                }
            }

            attachCooldown -= deltaTime;
            deattachTimer -= deltaTime;

            switch (enemyAI.State)
            {
                case AIController.AIState.None:
                    if (attachToWalls && character.Submarine == null && Level.Loaded != null)
                    {
                        raycastTimer -= deltaTime;
                        //check if there are any walls nearby the character could attach to
                        if (raycastTimer < 0.0f)
                        {
                            wallAttachPos = Vector2.Zero;

                            var cells = Level.Loaded.GetCells(character.WorldPosition, 1);
                            if (cells.Count > 0)
                            {
                                foreach (Voronoi2.VoronoiCell cell in cells)
                                {
                                    foreach (Voronoi2.GraphEdge edge in cell.Edges)
                                    {
                                        Vector2? intersection = MathUtils.GetLineIntersection(edge.Point1, edge.Point2, character.WorldPosition, cell.Center);
                                        if (intersection.HasValue)
                                        {
                                            attachSurfaceNormal = edge.GetNormal(cell);
                                            attachTargetBody = cell.Body;
                                            wallAttachPos = ConvertUnits.ToSimUnits(intersection.Value);
                                            break;
                                        }
                                    }
                                    if (wallAttachPos != Vector2.Zero) break;
                                }
                            }
                            raycastTimer = RaycastInterval;
                        }
                    }
                    else
                    {
                        wallAttachPos = Vector2.Zero;
                    }

                    if (wallAttachPos == Vector2.Zero)
                    {
                        DetachAll();
                    }
                    else
                    {
                        float dist = Vector2.Distance(character.SimPosition, wallAttachPos);
                        if (dist < Math.Max(Math.Max(character.AnimController.Collider.radius, character.AnimController.Collider.width), character.AnimController.Collider.height) * 1.2f)
                        {
                            //close enough to a wall -> attach
                            attachLimbs.ForEach(l => AttachToBody(character.AnimController.Collider, l, attachTargetBody, wallAttachPos));
                            enemyAI.SteeringManager.Reset();
                        }
                        else
                        {
                            //move closer to the wall
                            DetachAll();
                            enemyAI.SteeringManager.SteeringAvoid(deltaTime, 1.0f, 0.1f);
                            enemyAI.SteeringManager.SteeringSeek(wallAttachPos, 2.0f);
                        }
                    }
                    break;
                case AIController.AIState.Attack:
                    if (attachToSub && wallAttachPos != Vector2.Zero && attachTargetBody != null && enemyAI.AttackingLimb != null)
                    {
                        foreach (var limb in attachLimbs)
                        {
                            if (limb != enemyAI.AttackingLimb) { continue; }
                            Vector2 offset = limb.SimPosition - character.AnimController.MainLimb.SimPosition;
                            Vector2 transformedAttachPos = wallAttachPos + offset;
                            if (character.Submarine == null && attachTargetSubmarine != null)
                            {
                                transformedAttachPos += ConvertUnits.ToSimUnits(attachTargetSubmarine.Position);
                            }
                            if (transformedAttachPos != Vector2.Zero)
                            {
                                WallAttachPositions[attachLimbs.IndexOf(limb)] = transformedAttachPos;
                            }
                            if (Vector2.DistanceSquared(transformedAttachPos, limb.SimPosition) < limb.attack.Range * limb.attack.Range)
                            {
                                AttachToBody(character.AnimController.Collider, limb, attachTargetBody, transformedAttachPos);
                            }
                        }
                    }
                    break;
                default:
                    WallAttachPositions.ForEach(p => p = Vector2.Zero);
                    wallAttachPos = Vector2.Zero;
                    DetachAll();
                    break;
            }

            if (attachTargetBody != null && deattachTimer < 0.0f)
            {
                foreach (var attachLimb in attachLimbs)
                {
                    Entity entity = attachTargetBody.UserData as Entity;
                    Submarine attachedSub = entity is Submarine ? (Submarine)entity : entity?.Submarine;
                    if (attachedSub != null)
                    {
                        float velocity = attachedSub.Velocity == Vector2.Zero ? 0.0f : attachedSub.Velocity.Length();
                        float velocityFactor = (maxDeattachSpeed - minDeattachSpeed <= 0.0f) ?
                            Math.Sign(Math.Abs(velocity) - minDeattachSpeed) :
                            (Math.Abs(velocity) - minDeattachSpeed) / (maxDeattachSpeed - minDeattachSpeed);

                        if (Rand.Range(0.0f, 1.0f) < velocityFactor)
                        {
                            Detach(attachLimb);
                            character.AddDamage(character.WorldPosition, new List<Affliction>() { AfflictionPrefab.InternalDamage.Instantiate(damageOnDetach) }, detachStun, true);
                            attachCooldown = 5.0f;
                        }
                    }
                }
                deattachTimer = 5.0f;
            }
        }

        private void AttachToBody(PhysicsBody collider, Limb attachLimb, Body targetBody, Vector2 attachPos)
        {
            if (IsAttached)
            {
                if (attachJoints.TryGetValue(attachLimb, out WeldJoint joint))
                {
                    if (joint.BodyB == targetBody)
                    {
                        //already attached to the target body, no need to do anything
                        return;
                    }
                    else
                    {
                        // attached to something else, detach.
                        Detach(attachLimb);
                    }
                }
            }

            jointDir = attachLimb.Dir;

            Vector2 transformedLocalAttachPos = localAttachPos * attachLimb.character.AnimController.RagdollParams.LimbScale;
            if (jointDir < 0.0f) transformedLocalAttachPos.X = -transformedLocalAttachPos.X;

            //transformedLocalAttachPos = Vector2.Transform(transformedLocalAttachPos, Matrix.CreateRotationZ(attachLimb.Rotation));

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

            Vector2 colliderFront = collider.GetFrontLocal() * attachLimb.character.AnimController.RagdollParams.LimbScale;
            if (jointDir < 0.0f) colliderFront.X = -colliderFront.X;
            collider.SetTransform(attachPos + attachSurfaceNormal * colliderFront.Length(), angle);

            GameMain.World.AddJoint(limbJoint);
            attachJoints.Add(attachLimb, limbJoint);
            var colliderJoint = new WeldJoint(collider.FarseerBody, targetBody, colliderFront, targetBody.GetLocalPoint(attachPos), false)
            {
                FrequencyHz = 10.0f,
                DampingRatio = 0.5f,
                KinematicBodyB = true,
                CollideConnected = false,
                //Length = 0.1f
            };
            GameMain.World.AddJoint(colliderJoint);
            colliderJoints.Add(attachLimb, colliderJoint);            
        }

        private void Detach(Limb limb)
        {
            if (attachJoints.TryGetValue(limb, out WeldJoint attachJoint))
            {
                attachJoints.Remove(limb);
                GameMain.World.RemoveJoint(attachJoint);
            }
            if (colliderJoints.TryGetValue(limb, out WeldJoint colliderJoint))
            {
                colliderJoints.Remove(limb);
                GameMain.World.RemoveJoint(colliderJoint);
            }
        }

        public void DetachAll()
        {
            attachJoints.Values.ForEach(j => GameMain.World.RemoveJoint(j));
            colliderJoints.Values.ForEach(j => GameMain.World.RemoveJoint(j));
            attachJoints.Clear();
            colliderJoints.Clear();
        }

        private void OnCharacterDeath(Character character, CauseOfDeath causeOfDeath)
        {
            DetachAll();
            character.OnDeath -= OnCharacterDeath;
        }
    }
}
