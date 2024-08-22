using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;
using Voronoi2;

namespace Barotrauma
{
    class LatchOntoAI
    {
        const float RaycastInterval = 5.0f;
        private float raycastTimer;
        private Body targetBody;
        private Vector2 attachSurfaceNormal;
        private readonly Character character;

        public bool AttachToSub { get; private set; }
        public bool AttachToWalls { get; private set; }
        public bool AttachToCharacters { get; private set; }

        public Submarine TargetSubmarine { get; private set; }
        public Structure TargetWall { get; private set; }
        public Character TargetCharacter { get; private set; }

        private readonly float minDeattachSpeed, maxDeattachSpeed, maxAttachDuration, coolDown;
        private readonly float damageOnDetach, detachStun;
        private readonly bool weld;
        private float deattachCheckTimer;

        private Vector2 _attachPos;

        /// <summary>
        /// The character won't latch onto anything when the cooldown is active (activates after the character deattaches for whatever reason).
        /// </summary>
        private float attachCooldown;

        private readonly Limb attachLimb;
        private Vector2 localAttachPos;
        private readonly float attachLimbRotation;

        private float jointDir;

        private float latchedDuration;

        private readonly bool freezeWhenLatched;

        public List<Joint> AttachJoints { get; } = new List<Joint>();

        public Vector2? AttachPos
        {
            get;
            private set;
        }

        public bool IsAttached => AttachJoints.Count > 0;

        public bool IsAttachedToSub => IsAttached && TargetSubmarine != null && TargetCharacter == null;

        public LatchOntoAI(XElement element, EnemyAIController enemyAI)
        {
            AttachToWalls = element.GetAttributeBool(nameof(AttachToWalls), false);
            AttachToSub = element.GetAttributeBool(nameof(AttachToSub), false);
            AttachToCharacters = element.GetAttributeBool(nameof(AttachToCharacters), false);
            minDeattachSpeed = element.GetAttributeFloat(nameof(minDeattachSpeed), 5.0f);
            maxDeattachSpeed = Math.Max(minDeattachSpeed, element.GetAttributeFloat(nameof(maxDeattachSpeed), 8.0f));
            maxAttachDuration = element.GetAttributeFloat(nameof(maxAttachDuration), -1.0f);
            coolDown = element.GetAttributeFloat(nameof(coolDown), 2f);
            damageOnDetach = element.GetAttributeFloat(nameof(damageOnDetach), 0.0f);
            detachStun = element.GetAttributeFloat(nameof(detachStun), 0.0f);
            localAttachPos = ConvertUnits.ToSimUnits(element.GetAttributeVector2(nameof(localAttachPos), Vector2.Zero));
            attachLimbRotation = MathHelper.ToRadians(element.GetAttributeFloat(nameof(attachLimbRotation), 0.0f));
            weld = element.GetAttributeBool(nameof(weld), true);
            freezeWhenLatched = element.GetAttributeBool(nameof(freezeWhenLatched), false);

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
            if (!AttachToSub) { return; }
            if (wall == null) { return; }
            var sub = wall.Submarine;
            if (sub == null) { return; }
            Reset();
            TargetWall = wall;
            TargetSubmarine = sub;
            targetBody = TargetSubmarine.PhysicsBody.FarseerBody;
            this.attachSurfaceNormal = attachSurfaceNormal;
            _attachPos = attachPos;
        }

        public void SetAttachTarget(Character target)
        {
            if (!AttachToCharacters) { return; }
            if (target.Submarine != character.Submarine) { return; }
            Reset();
            TargetCharacter = target;
            targetBody = target.AnimController.Collider.FarseerBody;
            attachSurfaceNormal = Vector2.Normalize(character.WorldPosition - target.WorldPosition);
        }

        public void SetAttachTarget(VoronoiCell levelWall)
        {
            if (!AttachToWalls) { return; }
            Reset();
            foreach (Voronoi2.GraphEdge edge in levelWall.Edges)
            {
                if (MathUtils.GetLineSegmentIntersection(edge.Point1, edge.Point2, character.WorldPosition, levelWall.Center, out Vector2 intersection))
                {
                    attachSurfaceNormal = edge.GetNormal(levelWall);
                    targetBody = levelWall.Body;
                    _attachPos = ConvertUnits.ToSimUnits(intersection);
                    return;
                }
            }
        }

        public void Update(EnemyAIController enemyAI, float deltaTime)
        {
            if (TargetCharacter != null && character.Submarine != TargetCharacter.Submarine ||
                character.Submarine != null && TargetSubmarine != null && TargetCharacter == null)
            {
                DeattachFromBody(reset: true);
                return;
            }
            if (IsAttached)
            {
                latchedDuration += deltaTime;
                if (freezeWhenLatched && targetBody is { BodyType: BodyType.Static } &&
                    /*brief delay to let the ragdoll "settle"*/
                    latchedDuration > 5.0f)
                {
                    foreach (var limb in character.AnimController.Limbs)
                    {
                        limb.body.LinearVelocity = Vector2.Zero;
                        limb.body.AngularVelocity = 0.0f;
                    }
                }
                if (Math.Sign(attachLimb.Dir) != Math.Sign(jointDir))
                {
                    var attachJoint = AttachJoints[0];
                    if (attachJoint is WeldJoint weldJoint)
                    {
                        weldJoint.LocalAnchorA = new Vector2(-weldJoint.LocalAnchorA.X, weldJoint.LocalAnchorA.Y);
                        weldJoint.ReferenceAngle = -weldJoint.ReferenceAngle;
                    }
                    else if (attachJoint is RevoluteJoint revoluteJoint)
                    {
                        revoluteJoint.LocalAnchorA = new Vector2(-revoluteJoint.LocalAnchorA.X, revoluteJoint.LocalAnchorA.Y);
                        revoluteJoint.ReferenceAngle = -revoluteJoint.ReferenceAngle;
                    }
                    jointDir = attachLimb.Dir;
                }
                for (int i = 0; i < AttachJoints.Count; i++)
                {
                    //something went wrong, limb body is very far from the joint anchor -> deattach
                    if (Vector2.DistanceSquared(AttachJoints[i].WorldAnchorB, AttachJoints[i].BodyA.Position) > 10.0f * 10.0f)
                    {
#if DEBUG
                        DebugConsole.Log("Limb body of the character \"" + character.Name + "\" is very far from the attach joint anchor -> deattach");
#endif
                        DeattachFromBody(reset: true);
                        return;
                    }
                }
                if (TargetCharacter != null)
                {
                    if (enemyAI.AttackLimb?.attack == null)
                    {
                        DeattachFromBody(reset: true, cooldown: 1);
                    }
                    else
                    {
                        float range = enemyAI.AttackLimb.attack.DamageRange * 2f;
                        if (Vector2.DistanceSquared(TargetCharacter.WorldPosition, enemyAI.AttackLimb.WorldPosition) > range * range)
                        {
                            DeattachFromBody(reset: true, cooldown: 1);
                        }
                        else
                        {
                            TargetCharacter.Latchers.Add(this);
                        }
                    }
                }
            }

            if (attachCooldown > 0)
            {
                attachCooldown -= deltaTime;
            }
            if (deattachCheckTimer > 0)
            {
                deattachCheckTimer -= deltaTime;
            }

            if (TargetCharacter != null)
            {
                // Own sim pos -> target where we are
                _attachPos = character.SimPosition;
            }
            Vector2 transformedAttachPos = _attachPos;
            if (character.Submarine == null && TargetSubmarine != null)
            {
                transformedAttachPos += ConvertUnits.ToSimUnits(TargetSubmarine.Position);
            }
            if (transformedAttachPos != Vector2.Zero)
            {
                AttachPos = transformedAttachPos;
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
                                _attachPos = Vector2.Zero;

                                var cells = Level.Loaded.GetCells(character.WorldPosition, 1);
                                if (cells.Count > 0)
                                {
                                    //ignore walls more than 200 meters away
                                    float closestDist = 200.0f * 200.0f;
                                    foreach (Voronoi2.VoronoiCell cell in cells)
                                    {
                                        foreach (Voronoi2.GraphEdge edge in cell.Edges)
                                        {
                                            if (MathUtils.GetLineSegmentIntersection(edge.Point1, edge.Point2, character.WorldPosition, cell.Center, out Vector2 intersection))
                                            {
                                                Vector2 potentialAttachPos = ConvertUnits.ToSimUnits(intersection);
                                                float distSqr = Vector2.DistanceSquared(character.SimPosition, potentialAttachPos);
                                                if (distSqr < closestDist)
                                                {
                                                    attachSurfaceNormal = edge.GetNormal(cell);
                                                    targetBody = cell.Body;
                                                    _attachPos = potentialAttachPos;
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
                        _attachPos = Vector2.Zero;
                    }
                    if (_attachPos == Vector2.Zero || targetBody == null)
                    {
                        DeattachFromBody(reset: false);
                    }
                    else if (attachCooldown <= 0.0f)
                    {
                        float squaredDistance = Vector2.DistanceSquared(character.SimPosition, _attachPos);
                        float targetDistance = Math.Max(Math.Max(character.AnimController.Collider.Radius, character.AnimController.Collider.Width), character.AnimController.Collider.Height) * 1.2f;
                        if (squaredDistance < targetDistance * targetDistance)
                        {
                            //close enough to a wall -> attach
                            AttachToBody(_attachPos);
                            enemyAI.SteeringManager.Reset();
                        }
                        else
                        {
                            //move closer to the wall
                            DeattachFromBody(reset: false);
                            enemyAI.SteeringManager.SteeringAvoid(deltaTime, 1.0f, 0.1f);
                            enemyAI.SteeringManager.SteeringSeek(_attachPos);
                        }
                    }
                    else if (IsAttached)
                    {
                        enemyAI.SteeringManager.Reset();
                    }
                    break;
                case AIState.Attack:
                case AIState.Aggressive:
                    if (enemyAI.IsSteeringThroughGap) { break; }
                    if (_attachPos == Vector2.Zero) { break; }
                    if (!AttachToSub && !AttachToCharacters) { break; }
                    if (enemyAI.AttackLimb == null) { break; }
                    if (targetBody == null) { break; }
                    if (IsAttached && AttachJoints[0].BodyB == targetBody) { break; }
                    Vector2 referencePos = TargetCharacter != null ? TargetCharacter.WorldPosition : ConvertUnits.ToDisplayUnits(transformedAttachPos);
                    if (Vector2.DistanceSquared(referencePos, enemyAI.AttackLimb.WorldPosition) < enemyAI.AttackLimb.attack.DamageRange * enemyAI.AttackLimb.attack.DamageRange)
                    {
                        AttachToBody(transformedAttachPos);
                    }
                    break;
                default:
                    DeattachFromBody(reset: true);
                    break;
            }

            if (IsAttached && targetBody != null && deattachCheckTimer <= 0.0f)
            {
                attachCooldown = coolDown;
                bool deattach = false;
                if (maxAttachDuration > 0)
                {
                    deattach = true;
                }
                if (!deattach && TargetWall != null && TargetSubmarine != null)
                {
                    // Deattach if the wall is broken enough where we are attached to
                    int targetSection = TargetWall.FindSectionIndex(attachLimb.WorldPosition, world: true, clamp: true);
                    if (enemyAI.CanPassThroughHole(TargetWall, targetSection))
                    {
                        deattach = true;
                    }
                    if (!deattach)
                    {
                        // Deattach if the velocity is high
                        float velocity = TargetSubmarine.Velocity == Vector2.Zero ? 0.0f : TargetSubmarine.Velocity.Length();
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
                                    attachCooldown = Math.Max(detachStun * 2, coolDown);
                                }
                            }
                        }
                    }
                    deattachCheckTimer = 5.0f;
                }
                if (deattach)
                {
                    DeattachFromBody(reset: true);
                }
            }
        }

        public void AttachToBody(Vector2 attachPos, Vector2? forceAttachSurfaceNormal = null, Vector2? forceColliderSimPosition = null)
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

            if (forceAttachSurfaceNormal.HasValue) { attachSurfaceNormal = forceAttachSurfaceNormal.Value; }
            if (forceColliderSimPosition.HasValue)
            {
                character.TeleportTo(ConvertUnits.ToDisplayUnits(forceColliderSimPosition.Value));
            }

            // TODO: Shouldn't multiply by LimbScale here, because it's already applied in attachLimb.Scale!
            Vector2 transformedLocalAttachPos = localAttachPos * attachLimb.Scale * attachLimb.Params.Ragdoll.LimbScale;
            if (jointDir < 0.0f)
            {
                transformedLocalAttachPos.X = -transformedLocalAttachPos.X;
            }

            float angle = MathUtils.VectorToAngle(-attachSurfaceNormal) - MathHelper.PiOver2 + attachLimbRotation * attachLimb.Dir;
            //make sure the angle "has the same number of revolutions" as the reference limb
            //(e.g. we don't want to rotate the legs to 0 if the torso is at 360, because that'd blow up the hip joints) 
            angle = attachLimb.body.WrapAngleToSameNumberOfRevolutions(angle);
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

            Joint colliderJoint = weld ?
                new WeldJoint(collider.FarseerBody, targetBody, colliderFront, targetBody.GetLocalPoint(attachPos), false)
                {
                    FrequencyHz = 10.0f,
                    DampingRatio = 0.5f,
                    KinematicBodyB = true,
                    CollideConnected = false,
                } : 
                new RevoluteJoint(collider.FarseerBody, targetBody, colliderFront, targetBody.GetLocalPoint(attachPos), false)
                {
                    MotorEnabled = true,
                    MaxMotorTorque = 0.25f
                } as Joint;

            GameMain.World.Add(colliderJoint);
            AttachJoints.Add(colliderJoint);
            TargetCharacter?.Latchers.Add(this);
            if (maxAttachDuration > 0)
            {
                deattachCheckTimer = maxAttachDuration;
            }

#if SERVER
            if (TargetCharacter != null)
            {
                GameMain.Server.CreateEntityEvent(character, new Character.LatchedOntoTargetEventData(character, TargetCharacter, attachSurfaceNormal, attachPos));
            }
            else if (TargetWall != null)
            {
                GameMain.Server.CreateEntityEvent(character, new Character.LatchedOntoTargetEventData(character, TargetWall, attachSurfaceNormal, attachPos));
            }
            else if (targetBody.UserData is Voronoi2.VoronoiCell cell)
            {
                GameMain.Server.CreateEntityEvent(character, new Character.LatchedOntoTargetEventData(character, cell, attachSurfaceNormal, attachPos));
            }
#endif
        }

        public void DeattachFromBody(bool reset, float cooldown = 0)
        {
            bool wasAttached = IsAttached;
            foreach (Joint joint in AttachJoints)
            {
                GameMain.World.Remove(joint);
            }
            AttachJoints.Clear();
            if (cooldown > 0)
            {
                attachCooldown = cooldown;
            }
            TargetCharacter?.Latchers.Remove(this);
            if (reset)
            {
                Reset();
            }
#if SERVER
            if (wasAttached)
            {
                GameMain.Server.CreateEntityEvent(character, new Character.LatchedOntoTargetEventData());
            }
#endif
        }

        private void Reset()
        {
            TargetCharacter?.Latchers.Remove(this);
            TargetCharacter = null;
            TargetWall = null;
            TargetSubmarine = null;
            targetBody = null;
            AttachPos = null;
        }

        private void OnCharacterDeath(Character character, CauseOfDeath causeOfDeath)
        {
            DeattachFromBody(reset: true);
            character.OnDeath -= OnCharacterDeath;
        }
    }
}
