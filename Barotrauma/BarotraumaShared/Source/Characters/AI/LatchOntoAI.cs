using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    class LatchOntoAI
    {
        const float RaycastInterval = 5.0f;

        private float raycastTimer;

        private Body attachTargetBody;
        private Submarine attachTargetSubmarine;

        private bool attachToSub;
        private bool attachToWalls;

        private float minDeattachSpeed = 3.0f, maxDeattachSpeed = 10.0f;
        private float deattachTimer;

        private Vector2 wallAttachPos;

        private float attachCooldown;

        private List<Joint> attachJoints = new List<Joint>();

        public List<Joint> AttachJoints
        {
            get { return attachJoints; }
        }

        public LatchOntoAI(XElement element, EnemyAIController enemyAI)
        {
            attachToWalls = element.GetAttributeBool("attachtowalls", false);
            attachToSub = element.GetAttributeBool("attachtosub", false);
            minDeattachSpeed = element.GetAttributeFloat("mindeattachspeed", 3.0f);
            maxDeattachSpeed = Math.Max(minDeattachSpeed, element.GetAttributeFloat("maxdeattachspeed", 10.0f));

            enemyAI.Character.OnDeath += OnCharacterDeath;
        }

        public void SetAttachTarget(Body attachTarget, Submarine attachTargetSub, Vector2 attachPos)
        {
            attachTargetBody = attachTarget;
            attachTargetSubmarine = attachTargetSub;
            wallAttachPos = attachPos;
        }
        
        public void Update(EnemyAIController enemyAI, float deltaTime)
        {
            Character character = enemyAI.Character;

            if (character.Submarine != null)
            {
                DeattachFromBody();
                return;
            }

            attachCooldown -= deltaTime;
            deattachTimer -= deltaTime;

            Vector2 transformedAttachPos = wallAttachPos;
            if (character.Submarine == null && attachTargetSubmarine != null)
            {
                transformedAttachPos += ConvertUnits.ToSimUnits(attachTargetSubmarine.Position);
            }

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
                                Body closestBody = Submarine.CheckVisibility(character.SimPosition, ConvertUnits.ToSimUnits(cells[0].Center));
                                if (closestBody != null && closestBody.UserData is Voronoi2.VoronoiCell)
                                {
                                    attachTargetBody = closestBody;
                                    wallAttachPos = Submarine.LastPickedPosition;
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
                        DeattachFromBody();
                    }
                    else
                    {
                        float dist = Vector2.Distance(character.SimPosition, wallAttachPos);
                        if (dist < Math.Max(Math.Max(character.AnimController.Collider.radius, character.AnimController.Collider.width), character.AnimController.Collider.height) * 1.2f)
                        {
                            //close enough to a wall -> attach
                            character.AnimController.Collider.MoveToPos(wallAttachPos, 1.0f);
                            var attachLimb = character.AnimController.MainLimb;
                            AttachToBody(character.AnimController.Collider, attachLimb.body, attachTargetBody, wallAttachPos);
                            enemyAI.SteeringManager.Reset();
                        }
                        else
                        {
                            //move closer to the wall
                            DeattachFromBody();
                            enemyAI.SteeringManager.SteeringAvoid(deltaTime, 0.1f);
                            enemyAI.SteeringManager.SteeringSeek(wallAttachPos);
                        }
                    }
                    break;
                case AIController.AIState.Attack:
                    if (enemyAI.AttackingLimb != null)
                    {
                        if (attachToSub && wallAttachPos != Vector2.Zero && attachTargetBody != null &&
                            Vector2.DistanceSquared(transformedAttachPos, enemyAI.AttackingLimb.SimPosition) < enemyAI.AttackingLimb.attack.Range * enemyAI.AttackingLimb.attack.Range)
                        {
                            AttachToBody(character.AnimController.Collider, character.AnimController.MainLimb.body, attachTargetBody, transformedAttachPos);
                        }
                    }
                    break;
                default:
                    DeattachFromBody();
                    break;
            }

            if (attachTargetBody != null && deattachTimer < 0.0f)
            {
                Entity entity = attachTargetBody.UserData as Entity;
                Submarine attachedSub = entity is Submarine ? (Submarine)entity : entity?.Submarine;
                if (attachedSub != null)
                {
                    float velocityFactor = (maxDeattachSpeed - minDeattachSpeed <= 0.0f) ?
                        Math.Sign(Math.Abs(attachedSub.Velocity.X) - minDeattachSpeed) :
                        (Math.Abs(attachedSub.Velocity.X) - minDeattachSpeed) / (maxDeattachSpeed - minDeattachSpeed);

                    if (Rand.Range(0.0f, 1.0f) < velocityFactor)
                    {
                        DeattachFromBody();
                        attachCooldown = 5.0f;
                    }
                }

                deattachTimer = 5.0f;
            }
        }

        private void AttachToBody(PhysicsBody collider, PhysicsBody attachLimb, Body targetBody, Vector2 attachPos)
        {
            //already attached to something
            if (attachJoints.Count > 0)
            {
                //already attached to the target body, no need to do anything
                if (attachJoints[0].BodyB == targetBody) return;
                DeattachFromBody();
            }

            var limbJoint = new DistanceJoint(attachLimb.FarseerBody, targetBody, attachLimb.GetFrontLocal(), targetBody.GetLocalPoint(attachPos), false)
            {
                CollideConnected = false,
                Length = 0.1f
            };
            GameMain.World.AddJoint(limbJoint);
            attachJoints.Add(limbJoint);

            var colliderJoint = new DistanceJoint(collider.FarseerBody, targetBody, collider.GetFrontLocal(), targetBody.GetLocalPoint(attachPos), false)
            {
                CollideConnected = true,
                Length = 0.1f
            };
            GameMain.World.AddJoint(colliderJoint);
            attachJoints.Add(colliderJoint);
        }

        public void DeattachFromBody()
        {
            foreach (Joint joint in attachJoints)
            {
                GameMain.World.RemoveJoint(joint);
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
