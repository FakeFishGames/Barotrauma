using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Dynamics.Joints;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Extensions;
using LimbParams = Barotrauma.RagdollParams.LimbParams;
using JointParams = Barotrauma.RagdollParams.JointParams;

namespace Barotrauma
{
    abstract partial class Ragdoll
    {
        public abstract RagdollParams RagdollParams { get; protected set; }

        const float ImpactDamageMultiplayer = 10.0f;
        /// <summary>
        /// Maximum damage per impact (0.1 = 10% of the character's maximum health)
        /// </summary>
        const float MaxImpactDamage = 0.1f;

        private static List<Ragdoll> list = new List<Ragdoll>();

        protected Hull currentHull;
        
        private Limb[] limbs;
        public Limb[] Limbs
        {
            get
            {
                if (limbs == null)
                {
                    DebugConsole.ThrowError("Attempted to access a potentially removed ragdoll. Character: " + character.Name + ", id: " + character.ID + ", removed: " + character.Removed + ", ragdoll removed: " + !list.Contains(this));
                    GameAnalyticsManager.AddErrorEventOnce(
                        "Ragdoll.Limbs:AccessRemoved",
                        GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Attempted to access a potentially removed ragdoll. Character: " + character.Name + ", id: " + character.ID + ", removed: " + character.Removed + ", ragdoll removed: " + !list.Contains(this) + "\n" + Environment.StackTrace);

                    return new Limb[0];
                }
                return limbs;
            }
        }

        public bool HasMultipleLimbsOfSameType => Limbs.Length > limbDictionary.Count;

        private bool frozen;
        public bool Frozen
        {
            get { return frozen; }
            set 
            { 
                if (frozen == value) return;

                frozen = value;
                
                Collider.PhysEnabled = !frozen;
                if (frozen && MainLimb != null) MainLimb.PullJointWorldAnchorB = MainLimb.SimPosition;                
            }
        }

        private Dictionary<LimbType, Limb> limbDictionary;
        public LimbJoint[] LimbJoints;

        private bool simplePhysicsEnabled;

        protected Character character;

        protected float strongestImpact;

        protected double onFloorTimer;

        private float splashSoundTimer;

        //the movement speed of the ragdoll
        public Vector2 movement;
        //the target speed towards which movement is interpolated
        protected Vector2 targetMovement;

        //a movement vector that overrides targetmovement if trying to steer
        //a Character to the position sent by server in multiplayer mode
        protected Vector2 overrideTargetMovement;
        
        protected float floorY;
        protected float surfaceY;
        
        protected bool inWater, headInWater;
        public bool onGround;
        private bool ignorePlatforms;

        /// <summary>
        /// In sim units. Joint scale applied.
        /// </summary>
        public float ColliderHeightFromFloor => ConvertUnits.ToSimUnits(RagdollParams.ColliderHeightFromFloor) * RagdollParams.JointScale;

        public Structure Stairs;
                
        protected Direction dir;

        public Direction TargetDir;

        protected List<PhysicsBody> collider;
        protected int colliderIndex = 0;

        private Category prevCollisionCategory = Category.None;

        private Body outsideCollisionBlocker;

        public bool IsStuck => Limbs.Any(l => l.IsStuck);

        public PhysicsBody Collider
        {
            get
            {
                return collider?[colliderIndex];
            }
        }

        public int ColliderIndex
        {
            get
            {
                return colliderIndex;
            }
            set
            {
                if (value == colliderIndex || collider == null) return;
                if (value >= collider.Count || value < 0) return;

                if (collider[colliderIndex].height < collider[value].height)
                {
                    Vector2 pos1 = collider[colliderIndex].SimPosition;
                    pos1.Y -= collider[colliderIndex].height * ColliderHeightFromFloor;
                    Vector2 pos2 = pos1;
                    pos2.Y += collider[value].height * 1.1f;
                    if (GameMain.World.RayCast(pos1, pos2).Any(f => f.CollisionCategories.HasFlag(Physics.CollisionWall))) return;
                }


                Vector2 pos = collider[colliderIndex].SimPosition;
                pos.Y -= collider[colliderIndex].height * 0.5f;
                pos.Y += collider[value].height * 0.5f;
                collider[value].SetTransform(pos, collider[colliderIndex].Rotation);

                collider[value].LinearVelocity  = collider[colliderIndex].LinearVelocity;
                collider[value].AngularVelocity = collider[colliderIndex].AngularVelocity;
                collider[value].Submarine       = collider[colliderIndex].Submarine;
                collider[value].PhysEnabled = !frozen;
                collider[value].Enabled = !simplePhysicsEnabled;

                collider[colliderIndex].PhysEnabled = false;
                colliderIndex = value;
            }
        }

        public float FloorY
        {
            get { return floorY; }
        }

        public float Mass
        {
            get;
            private set;
        }

        public void SubtractMass(Limb limb)
        {
            if (limbs.Contains(limb))
            {
                Mass -= limb.Mass;
            }
        }

        public Limb MainLimb
        {
            get
            {
                Limb mainLimb = GetLimb(RagdollParams.MainLimb);
                if (mainLimb == null)
                {
                    Limb torso = GetLimb(LimbType.Torso);
                    Limb head = GetLimb(LimbType.Head);
                    mainLimb = torso ?? head;
                    if (mainLimb == null)
                    {
                        mainLimb = Limbs.FirstOrDefault();
                    }
                }
                return mainLimb;
            }
        }

        public Vector2 WorldPosition
        {
            get
            {
                return character.Submarine == null ?
                    ConvertUnits.ToDisplayUnits(Collider.SimPosition) :
                    ConvertUnits.ToDisplayUnits(Collider.SimPosition) + character.Submarine.Position;
            }
        }

        public bool SimplePhysicsEnabled
        {
            get { return simplePhysicsEnabled; }
            set
            {
                if (value == simplePhysicsEnabled) return;

                simplePhysicsEnabled = value;

                foreach (Limb limb in Limbs)
                {
                    if (limb.IsSevered) continue;
                    if (limb.body == null)
                    {
                        DebugConsole.ThrowError("Limb has no body! (" + (character != null ? character.Name : "Unknown character") + ", " + limb.type.ToString());
                        continue;
                    }
                    limb.body.Enabled = !simplePhysicsEnabled;
                }

                foreach (LimbJoint joint in LimbJoints)
                {
                    joint.Enabled = !joint.IsSevered && !simplePhysicsEnabled;
                }

                if (!simplePhysicsEnabled)
                {
                    foreach (Limb limb in Limbs)
                    {
                        if (limb.IsSevered || !limb.body.PhysEnabled) { continue; }
                        limb.body.SetTransform(Collider.SimPosition, Collider.Rotation);
                        //reset pull joints (they may be somewhere far away if the character has moved from the position where animations were last updated)
                        limb.PullJointEnabled = false;
                        limb.PullJointWorldAnchorB = limb.SimPosition;
                    }
                }
            }
        }

        public const float MAX_SPEED = 15;

        public Vector2 TargetMovement
        {
            get 
            { 
                return (overrideTargetMovement == Vector2.Zero) ? targetMovement : overrideTargetMovement; 
            }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                targetMovement.X = MathHelper.Clamp(value.X, -MAX_SPEED, MAX_SPEED);
                targetMovement.Y = MathHelper.Clamp(value.Y, -MAX_SPEED, MAX_SPEED);
            }
        }

        public abstract float? HeadPosition { get; }
        public abstract float? HeadAngle { get; }
        public abstract float? TorsoPosition { get; }
        public abstract float? TorsoAngle { get; }

        public float ImpactTolerance => RagdollParams.ImpactTolerance;
        public bool Draggable => RagdollParams.Draggable;
        public bool CanEnterSubmarine => RagdollParams.CanEnterSubmarine;
        public bool CanAttackSubmarine => Limbs.Any(l => l.attack != null && l.attack.IsValidTarget(AttackTarget.Structure));

        public float Dir => dir == Direction.Left ? -1.0f : 1.0f;

        public Direction Direction => dir;

        public bool InWater
        {
            get { return inWater; }
        }

        public bool HeadInWater
        {
            get { return headInWater; }
        }

        public Hull CurrentHull
        {
            get { return currentHull; }
            set
            {
                if (value == currentHull) return;

                currentHull = value;
                Submarine currSubmarine = currentHull?.Submarine;
                foreach (Limb limb in Limbs)
                {
                    limb.body.Submarine = currSubmarine;
                }
                Collider.Submarine = currSubmarine;
            }
        }

        public bool IgnorePlatforms
        {
            get { return ignorePlatforms; }
            set 
            {
                ignorePlatforms = value;
            }
        }
        
        /// <summary>
        /// Call this to create the ragdoll from the RagdollParams.
        /// </summary>
        public virtual void Recreate(RagdollParams ragdollParams = null)
        {
            if (IsFlipped)
            {
                Flip();
            }
            dir = Direction.Right;
            Dictionary<LimbParams, List<WearableSprite>> items = null;
            if (ragdollParams != null)
            {
                RagdollParams = ragdollParams;
            }
            else
            {
                items = limbs?.ToDictionary(l => l.Params, l => l.WearingItems);
            }
            foreach (var limbParams in RagdollParams.Limbs)
            {
                if (!PhysicsBody.IsValidShape(limbParams.Radius, limbParams.Height, limbParams.Width))
                {
                    DebugConsole.ThrowError($"Invalid collider dimensions (r: {limbParams.Radius}, h: {limbParams.Height}, w: {limbParams.Width}) on limb: {limbParams.Name}. Fixing.");
                    limbParams.Radius = 10;
                }
            }
            foreach (var colliderParams in RagdollParams.Colliders)
            {
                if (!PhysicsBody.IsValidShape(colliderParams.Radius, colliderParams.Height, colliderParams.Width))
                {
                    DebugConsole.ThrowError($"Invalid collider dimensions (r: {colliderParams.Radius}, h: {colliderParams.Height}, w: {colliderParams.Width}) on collider: {colliderParams.Name}. Fixing.");
                    colliderParams.Radius = 10;
                }
            }
            CreateColliders();
            CreateLimbs();
            CreateJoints();
            UpdateCollisionCategories();
            character.LoadHeadAttachments();
            if (items != null)
            {
                foreach (var kvp in items)
                {
                    int id = kvp.Key.ID;
                    // This can be the case if we manipulate the ragdoll in runtime (husk appendage, limb severance)
                    if (id > limbs.Length - 1) { continue; }
                    var limb = limbs[id];
                    var itemList = kvp.Value;
                    limb.WearingItems.AddRange(itemList);
                }
            }

            if (character.IsHusk)
            {
                if (Character.TryGetConfigFile(character.ConfigPath, out XDocument configFile))
                {
                    var mainElement = configFile.Root.IsOverride() ? configFile.Root.FirstElement() : configFile.Root;
                    foreach (var huskAppendage in mainElement.GetChildElements("huskappendage"))
                    {
                        AfflictionHusk.AttachHuskAppendage(character, huskAppendage.GetAttributeString("affliction", string.Empty), huskAppendage, ragdoll: this);
                    }
                }
            }
        }

        public Ragdoll(Character character, string seed, RagdollParams ragdollParams = null)
        {
            list.Add(this);
            this.character = character;
            Recreate(ragdollParams ?? RagdollParams);
        }

        protected void CreateColliders()
        {
            if (collider != null)
            {
                collider.ForEach(c => c.Remove());
            }
            DebugConsole.Log($"Creating colliders from {RagdollParams.Name}.");
            collider = new List<PhysicsBody>();
            foreach (var cParams in RagdollParams.Colliders)
            {
                if (!PhysicsBody.IsValidShape(cParams.Radius, cParams.Height, cParams.Width))
                {
                    DebugConsole.ThrowError("Invalid collider dimensions: " + cParams.Name);
                    break; ;
                }
                var body = new PhysicsBody(cParams);
                collider.Add(body);
                body.UserData = character;
                body.FarseerBody.OnCollision += OnLimbCollision;
                if (collider.Count > 1)
                {
                    body.PhysEnabled = false;
                }
            }
        }

        protected void CreateJoints()
        {
            if (LimbJoints != null)
            {
                LimbJoints.ForEach(j => GameMain.World.RemoveJoint(j));
            }
            DebugConsole.Log($"Creating joints from {RagdollParams.Name}.");
            LimbJoints = new LimbJoint[RagdollParams.Joints.Count];
            RagdollParams.Joints.ForEach(j => AddJoint(j));
            // Check the joints
            for (int i = 0; i < LimbJoints.Length; i++)
            {
                if (LimbJoints[i] == null)
                {
                    DebugConsole.ThrowError($"Joint {i} null.");
                }
            }

            // This block was salvaged from the merge of the dev branch to the animation branch. Not sure if every line made it here.
            outsideCollisionBlocker = BodyFactory.CreateEdge(GameMain.World, -Vector2.UnitX * 2.0f, Vector2.UnitX * 2.0f, "blocker");
            outsideCollisionBlocker.BodyType = BodyType.Static;
            outsideCollisionBlocker.CollisionCategories = Physics.CollisionWall;
            outsideCollisionBlocker.CollidesWith = Physics.CollisionCharacter;
            outsideCollisionBlocker.Enabled = false;

            UpdateCollisionCategories();

            SetInitialLimbPositions();
        }

        private void SetInitialLimbPositions()
        {
            foreach (var joint in LimbJoints)
            {
                if (joint == null) { continue; }
                float angle = (joint.LowerLimit + joint.UpperLimit) / 2.0f;
                joint.LimbB?.body?.SetTransform(
                    (joint.WorldAnchorA - MathUtils.RotatePointAroundTarget(joint.LocalAnchorB, Vector2.Zero, MathHelper.ToDegrees(joint.BodyA.Rotation + angle), true)),
                    joint.BodyA.Rotation + angle);
            }
        }

        protected void CreateLimbs()
        {
            if (limbs != null)
            {
                limbs.ForEach(l => l.Remove());
            }
            DebugConsole.Log($"Creating limbs from {RagdollParams.Name}.");
            limbDictionary = new Dictionary<LimbType, Limb>();
            limbs = new Limb[RagdollParams.Limbs.Count];
            RagdollParams.Limbs.ForEach(l => AddLimb(l));
            SetupDrawOrder();
        }

        partial void SetupDrawOrder();
        
        /// <summary>
        /// Saves all serializable data in the currently selected ragdoll params. This method should properly handle character flipping.
        /// </summary>
        public void SaveRagdoll(string fileNameWithoutExtension = null)
        {
            RagdollParams.Save(fileNameWithoutExtension);
        }

        /// <summary>
        /// Resets the serializable data to the currently selected ragdoll params.
        /// Force reloading always loads the xml stored on the disk.
        /// </summary>
        public void ResetRagdoll(bool forceReload = false)
        {
            RagdollParams.Reset(forceReload);
            ResetJoints();
            ResetLimbs();
        }

        /// <summary>
        /// Resets the current joint values to the serialized joint params.
        /// </summary>
        public void ResetJoints()
        {
            LimbJoints.ForEach(j => j.LoadParams());
        }

        /// <summary>
        /// Resets the current limb values to the serialized limb params.
        /// </summary>
        public void ResetLimbs()
        {
            Limbs.ForEach(l => l.LoadParams());
            SetupDrawOrder();
        }

        public void AddJoint(JointParams jointParams)
        {
            LimbJoint joint = new LimbJoint(Limbs[jointParams.Limb1], Limbs[jointParams.Limb2], jointParams, this);
            GameMain.World.AddJoint(joint);
            for (int i = 0; i < LimbJoints.Length; i++)
            {
                if (LimbJoints[i] != null) continue;
                LimbJoints[i] = joint;
                return;
            }
            Array.Resize(ref LimbJoints, LimbJoints.Length + 1);
            LimbJoints[LimbJoints.Length - 1] = joint;
        }

        protected void AddLimb(LimbParams limbParams)
        {
            byte ID = Convert.ToByte(limbParams.ID);
            Limb limb = new Limb(this, character, limbParams);
            limb.body.FarseerBody.OnCollision += OnLimbCollision;
            Limbs[ID] = limb;
            Mass += limb.Mass;
            if (!limbDictionary.ContainsKey(limb.type)) limbDictionary.Add(limb.type, limb);
        }

        public void AddLimb(Limb limb)
        {
            if (Limbs.Contains(limb)) return;
            limb.body.FarseerBody.OnCollision += OnLimbCollision;
            Array.Resize(ref limbs, Limbs.Length + 1);
            Limbs[Limbs.Length - 1] = limb;
            Mass += limb.Mass;
            if (!limbDictionary.ContainsKey(limb.type)) limbDictionary.Add(limb.type, limb);
            SetupDrawOrder();
        }

        public void RemoveLimb(Limb limb)
        {
            if (!Limbs.Contains(limb)) return;

            Limb[] newLimbs = new Limb[Limbs.Length - 1];

            int i = 0;
            foreach (Limb existingLimb in Limbs)
            {
                if (existingLimb == limb) continue;
                newLimbs[i] = existingLimb;
                i++;
            }

            limbs = newLimbs;
            if (limbDictionary.ContainsKey(limb.type))
            {
                limbDictionary.Remove(limb.type);
                // If there is another limb of the same type, replace the limb in the dictionary.
                if (HasMultipleLimbsOfSameType)
                {
                    var otherLimb = Limbs.FirstOrDefault(l => l != limb && l.type == limb.type);
                    if (otherLimb != null)
                    {
                        limbDictionary.Add(otherLimb.type, otherLimb);
                    }
                }
            }

            // TODO: this could be optimized if needed, but at least we need to remove the limb from the inversedDrawOrder array.
            SetupDrawOrder();

            //remove all joints that were attached to the removed limb
            LimbJoint[] attachedJoints = Array.FindAll(LimbJoints, lj => lj.LimbA == limb || lj.LimbB == limb);
            if (attachedJoints.Length > 0)
            {
                LimbJoint[] newJoints = new LimbJoint[LimbJoints.Length - attachedJoints.Length];
                i = 0;
                foreach (LimbJoint limbJoint in LimbJoints)
                {
                    if (attachedJoints.Contains(limbJoint)) continue;
                    newJoints[i] = limbJoint;
                    i++;
                }
                LimbJoints = newJoints;
            }

            limb.Remove();
            Mass -= limb.Mass;
            foreach (LimbJoint limbJoint in attachedJoints)
            {
                GameMain.World.RemoveJoint(limbJoint);
            }
        }
          
        public bool OnLimbCollision(Fixture f1, Fixture f2, Contact contact)
        {

            if (f2.Body.UserData is Submarine && character.Submarine == (Submarine)f2.Body.UserData) return false;

            //only collide with the ragdoll's own blocker
            if (f2.Body.UserData as string == "blocker" && f2.Body != outsideCollisionBlocker) return false;

            //always collides with bodies other than structures
            if (!(f2.Body.UserData is Structure structure))
            {
                CalculateImpact(f1, f2, contact);
                return true;
            }

            Vector2 colliderBottom = GetColliderBottom();
            
            if (structure.IsPlatform)
            {
                if (ignorePlatforms) return false;

                //the collision is ignored if the lowest limb is under the platform
                //if (lowestLimb==null || lowestLimb.Position.Y < structure.Rect.Y) return false;

                if (colliderBottom.Y < ConvertUnits.ToSimUnits(structure.Rect.Y - 5)) return false; 
                if (f1.Body.Position.Y < ConvertUnits.ToSimUnits(structure.Rect.Y - 5)) return false; 
                
            }
            else if (structure.StairDirection != Direction.None)
            {
                Stairs = null;

                //don't collider with stairs if
                
                //1. bottom of the collider is at the bottom of the stairs and the character isn't trying to move upwards
                float stairBottomPos = ConvertUnits.ToSimUnits(structure.Rect.Y - structure.Rect.Height + 10);
                if (colliderBottom.Y < stairBottomPos && targetMovement.Y < 0.5f) return false;

                //2. bottom of the collider is at the top of the stairs and the character isn't trying to move downwards
                if (targetMovement.Y >= 0.0f && colliderBottom.Y >= ConvertUnits.ToSimUnits(structure.Rect.Y - Submarine.GridSize.Y * 5)) return false;
                               
                //3. collided with the stairs from below
                if (contact.Manifold.LocalNormal.Y < 0.0f) return false;

                //4. contact points is above the bottom half of the collider
                contact.GetWorldManifold(out Vector2 normal, out FarseerPhysics.Common.FixedArray2<Vector2> points);
                if (points[0].Y > Collider.SimPosition.Y) return false;
                
                //5. in water
                if (inWater && targetMovement.Y < 0.5f) return false;

                //---------------

                //set stairs to that of the one dragging us
                if (character.SelectedBy != null)
                    Stairs = character.SelectedBy.AnimController.Stairs;
                else
                    Stairs = structure;

                if (Stairs == null)
                    return false;
            }

            CalculateImpact(f1, f2, contact);

            return true;
        }

        private void CalculateImpact(Fixture f1, Fixture f2, Contact contact)
        {
            if (character.DisableImpactDamageTimer > 0.0f) return;

            //using the velocity of the limb would make the impact damage more realistic,
            //but would also make it harder to edit the animations because the forces/torques
            //would all have to be balanced in a way that prevents the character from doing
            //impact damage to itself
            Vector2 velocity = Collider.LinearVelocity;
            Vector2 normal = contact.Manifold.LocalNormal;

            if (character.Submarine == null && f2.Body.UserData is Submarine)
            {
                velocity -= ((Submarine)f2.Body.UserData).Velocity;
            }

            float impact = Vector2.Dot(velocity, -normal);
            if (f1.Body == Collider.FarseerBody || !Collider.Enabled)
            {
                bool isNotRemote = true;
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) isNotRemote = !character.IsRemotePlayer;

                if (isNotRemote)
                {
                    if (impact > ImpactTolerance)
                    {
                        contact.GetWorldManifold(out _, out FarseerPhysics.Common.FixedArray2<Vector2> points);
                        Vector2 impactPos = ConvertUnits.ToDisplayUnits(points[0]);
                        if (character.Submarine != null) impactPos += character.Submarine.Position;

                        float impactDamage = Math.Min((impact - ImpactTolerance) * ImpactDamageMultiplayer, character.MaxVitality * MaxImpactDamage);

                        character.LastDamageSource = null;
                        character.AddDamage(impactPos, new List<Affliction>() { AfflictionPrefab.InternalDamage.Instantiate(impactDamage) }, 0.0f, true);
                        strongestImpact = Math.Max(strongestImpact, impact - ImpactTolerance);
                        character.ApplyStatusEffects(ActionType.OnImpact, 1.0f);
                        //briefly disable impact damage
                        //otherwise the character will take damage multiple times when for example falling, 
                        //because we use the velocity of the collider to determine the impact
                        //(i.e. the character would take damage until the collider hits the floor and stops)
                        character.DisableImpactDamageTimer = 0.25f;
                    }
                }
            }

            ImpactProjSpecific(impact, f1.Body);
        }
        
        public void SeverLimbJoint(LimbJoint limbJoint, bool playSound = true)
        {
            if (!limbJoint.CanBeSevered || limbJoint.IsSevered)
            {
                return;
            }

            limbJoint.IsSevered = true;
            limbJoint.Enabled = false;

            List<Limb> connectedLimbs = new List<Limb>();
            List<LimbJoint> checkedJoints = new List<LimbJoint>();

            GetConnectedLimbs(connectedLimbs, checkedJoints, MainLimb);
            foreach (Limb limb in Limbs)
            {
                if (connectedLimbs.Contains(limb)) continue;

                limb.IsSevered = true;
            }

            SeverLimbJointProjSpecific(limbJoint, playSound: true);

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
            {
                GameMain.NetworkMember.CreateEntityEvent(character, new object[] { NetEntityEvent.Type.Status });
            }
        }

        partial void SeverLimbJointProjSpecific(LimbJoint limbJoint, bool playSound);

        private void GetConnectedLimbs(List<Limb> connectedLimbs, List<LimbJoint> checkedJoints, Limb limb)
        {
            connectedLimbs.Add(limb);

            foreach (LimbJoint joint in LimbJoints)
            {
                if (joint.IsSevered || checkedJoints.Contains(joint)) continue;
                if (joint.LimbA == limb)
                {
                    if (!connectedLimbs.Contains(joint.LimbB))
                    {
                        checkedJoints.Add(joint);
                        GetConnectedLimbs(connectedLimbs, checkedJoints, joint.LimbB);
                    }
                }
                else if (joint.LimbB == limb)
                {
                    if (!connectedLimbs.Contains(joint.LimbA))
                    {
                        checkedJoints.Add(joint);
                        GetConnectedLimbs(connectedLimbs, checkedJoints, joint.LimbA);
                    }
                }
            }
        }

        partial void ImpactProjSpecific(float impact, Body body);

        public bool IsFlipped { get; private set; }

        public virtual void Flip()
        {
            IsFlipped = !IsFlipped;
            dir = (dir == Direction.Left) ? Direction.Right : Direction.Left;

            for (int i = 0; i < LimbJoints.Length; i++)
            {
                float lowerLimit = -LimbJoints[i].UpperLimit;
                float upperLimit = -LimbJoints[i].LowerLimit;

                LimbJoints[i].LowerLimit = lowerLimit;
                LimbJoints[i].UpperLimit = upperLimit;

                LimbJoints[i].LocalAnchorA = new Vector2(-LimbJoints[i].LocalAnchorA.X, LimbJoints[i].LocalAnchorA.Y);
                LimbJoints[i].LocalAnchorB = new Vector2(-LimbJoints[i].LocalAnchorB.X, LimbJoints[i].LocalAnchorB.Y);
            }

            foreach (Limb limb in Limbs)
            {
                if (limb == null || limb.IsSevered) { continue; }
                limb.Dir = Dir;
                limb.MouthPos = new Vector2(-limb.MouthPos.X, limb.MouthPos.Y);
                limb.MirrorPullJoint();
            }

            FlipProjSpecific();
        }

        partial void FlipProjSpecific();

        public Vector2 GetCenterOfMass()
        {
            //all limbs disabled -> use the position of the collider
            if (!Limbs.Any(l => !l.IsSevered && l.body.Enabled))
            {
                return Collider.SimPosition;
            }

            Vector2 centerOfMass = Vector2.Zero;
            float totalMass = 0.0f;
            foreach (Limb limb in Limbs)
            {
                if (limb.IsSevered || !limb.body.Enabled) continue;
                centerOfMass += limb.Mass * limb.SimPosition;
                totalMass += limb.Mass;
            }

            if (totalMass <= 0.0f) return Collider.SimPosition;
            centerOfMass /= totalMass;

            if (!MathUtils.IsValid(centerOfMass))
            {
                string errorMsg = "Ragdoll.GetCenterOfMass returned an invalid value (" + centerOfMass + "). Limb positions: {"
                    + string.Join(", ", limbs.Select(l => l.SimPosition)) + "}, total mass: " + totalMass + ".";
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("Ragdoll.GetCenterOfMass", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return Collider.SimPosition;
            }

            return centerOfMass;
        }

        
        /// <param name="pullFromCenter">if false, force is applied to the position of pullJoint</param>
        protected void MoveLimb(Limb limb, Vector2 pos, float amount, bool pullFromCenter = false)
        {
            limb.MoveToPos(pos, amount, pullFromCenter);
        }
                
        public void ResetPullJoints()
        {
            for (int i = 0; i < Limbs.Length; i++)
            {
                if (Limbs[i] == null) continue;
                Limbs[i].PullJointEnabled = false;
            }
        }

        public static void UpdateAll(float deltaTime, Camera cam)
        {
            foreach (Ragdoll r in list)
            {
                r.Update(deltaTime, cam);
            }
        }

        public void FindHull(Vector2? worldPosition = null, bool setSubmarine = true)
        {
            Vector2 findPos = worldPosition == null ? this.WorldPosition : (Vector2)worldPosition;
            if (!MathUtils.IsValid(findPos))
            {
                GameAnalyticsManager.AddErrorEventOnce(
                    "Ragdoll.FindHull:InvalidPosition",
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    "Attempted to find a hull at an invalid position (" + findPos + ")\n" + Environment.StackTrace);
                return;
            }

            Hull newHull = Hull.FindHull(findPos, currentHull);
            
            if (newHull == currentHull) return;

            if (!CanEnterSubmarine || (character.AIController != null && !character.AIController.CanEnterSubmarine))
            {
                //character is inside the sub even though it shouldn't be able to enter -> teleport it out

                //far from an ideal solution, but monsters getting lodged inside the sub seems to be 
                //pretty rare during normal gameplay (requires abnormally high velocities), so I think
                //this is preferable to the cost of using continuous collision detection for the character collider
                if (newHull?.Submarine != null)
                {
                    Vector2 hullDiff = WorldPosition - newHull.WorldPosition;
                    Vector2 moveDir = hullDiff.LengthSquared() < 0.001f ? Vector2.UnitY : Vector2.Normalize(hullDiff);

                    //find a position 32 units away from the hull
                    if (MathUtils.GetLineRectangleIntersection(
                        newHull.WorldPosition,
                        newHull.WorldPosition + moveDir * Math.Max(newHull.Rect.Width, newHull.Rect.Height),
                        new Rectangle(newHull.WorldRect.X - 32, newHull.WorldRect.Y + 32, newHull.WorldRect.Width + 64, newHull.Rect.Height + 64),
                        out Vector2 intersection))
                    {
                        Collider.SetTransform(ConvertUnits.ToSimUnits(intersection), Collider.Rotation);
                    }
                    return;
                }
            }

            if (setSubmarine)
            {
                //in -> out
                if (newHull == null && currentHull.Submarine != null)
                {
                    if (Gap.FindAdjacent(currentHull.ConnectedGaps, findPos, 150.0f) != null) return;
                    character.MemLocalState?.Clear();
                    Teleport(ConvertUnits.ToSimUnits(currentHull.Submarine.Position), currentHull.Submarine.Velocity);
                }
                //out -> in
                else if (currentHull == null && newHull.Submarine != null)
                {
                    character.MemLocalState?.Clear();
                    Teleport(-ConvertUnits.ToSimUnits(newHull.Submarine.Position), -newHull.Submarine.Velocity);
                }
                //from one sub to another
                else if (newHull != null && currentHull != null && newHull.Submarine != currentHull.Submarine)
                {
                    character.MemLocalState?.Clear();
                    Vector2 newSubPos = newHull.Submarine == null ? Vector2.Zero : newHull.Submarine.Position;
                    Vector2 prevSubPos = currentHull.Submarine == null ? Vector2.Zero : currentHull.Submarine.Position;

                    Teleport(ConvertUnits.ToSimUnits(prevSubPos - newSubPos),
                        Vector2.Zero);
                }
            }
            
            CurrentHull = newHull;
            character.Submarine = currentHull?.Submarine;
        }

        private void PreventOutsideCollision()
        {
            if (currentHull?.Submarine == null)
            {
                outsideCollisionBlocker.Enabled = false;
                return;
            }

            var connectedGaps = currentHull.ConnectedGaps.Where(g => !g.IsRoomToRoom);
            foreach (Gap gap in connectedGaps)
            {
                if (gap.IsHorizontal)
                {
                    if (character.Position.Y > gap.Rect.Y || character.Position.Y < gap.Rect.Y - gap.Rect.Height) continue;
                    if (Math.Sign(gap.Rect.Center.X - currentHull.Rect.Center.X) !=
                        Math.Sign(character.Position.X - currentHull.Rect.Center.X))
                    {
                        continue;
                    }
                }
                else
                {
                    if (character.Position.X < gap.Rect.X || character.Position.X > gap.Rect.Right) continue;
                    if (Math.Sign((gap.Rect.Y - gap.Rect.Height / 2) - (currentHull.Rect.Center.X - currentHull.Rect.Height / 2)) !=
                        Math.Sign(character.Position.X - (currentHull.Rect.Center.X - currentHull.Rect.Height / 2)))
                    {
                        continue;
                    }
                }

                if (!gap.GetOutsideCollider(out Vector2? outsideColliderPos, out Vector2? outsideColliderNormal)) continue;

                outsideCollisionBlocker.SetTransform(
                    outsideColliderPos.Value - currentHull.Submarine.SimPosition, 
                    MathUtils.VectorToAngle(outsideColliderNormal.Value) - MathHelper.PiOver2);
                outsideCollisionBlocker.Enabled = true;
                return;
            }

            outsideCollisionBlocker.Enabled = false;
        }

        public void Teleport(Vector2 moveAmount, Vector2 velocityChange)
        {
            foreach (Limb limb in Limbs)
            {
                if (limb.IsSevered) continue;
                if (limb.body.FarseerBody.ContactList == null) continue;

                ContactEdge ce = limb.body.FarseerBody.ContactList;
                while (ce != null && ce.Contact != null)
                {
                    ce.Contact.Enabled = false;
                    ce = ce.Next;
                }
            }

            foreach (Limb limb in Limbs)
            {
                if (limb.IsSevered) continue;
                limb.body.LinearVelocity += velocityChange;
            }

            character.DisableImpactDamageTimer = 0.25f;

            SetPosition(Collider.SimPosition + moveAmount);
            character.CursorPosition += moveAmount;

            Collider?.UpdateDrawPosition();
            foreach (Limb limb in Limbs)
            {
                limb.body.UpdateDrawPosition();
            }
        }

        private void UpdateCollisionCategories()
        {
            Category wall = currentHull?.Submarine == null ? 
                Physics.CollisionLevel | Physics.CollisionWall 
                : Physics.CollisionWall;

            Category collisionCategory = (ignorePlatforms) ?
                wall | Physics.CollisionProjectile | Physics.CollisionStairs
                : wall | Physics.CollisionProjectile | Physics.CollisionPlatform | Physics.CollisionStairs;
            
            if (collisionCategory == prevCollisionCategory) return;
            prevCollisionCategory = collisionCategory;

            Collider.CollidesWith = collisionCategory | Physics.CollisionItemBlocking;

            foreach (Limb limb in Limbs)
            {
                if (limb.ignoreCollisions || limb.IsSevered) continue;

                try
                {
                    limb.body.CollidesWith = collisionCategory;
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Failed to update ragdoll limb collisioncategories", e);
                }
            }
        }

        protected bool levitatingCollider = true;

        /// <summary>
        /// How long has the ragdoll stayed motionless
        /// </summary>
        private float bodyInRestTimer;

        private float BodyInRestDelay = 1.0f;

        public bool BodyInRest
        {
            get { return bodyInRestTimer > BodyInRestDelay; }
            set
            {
                foreach (Limb limb in Limbs)
                {
                    limb.body.PhysEnabled = !value;
                }
                bodyInRestTimer = value ? BodyInRestDelay : 0.0f;
            }
        }

        public bool forceStanding;

        public void Update(float deltaTime, Camera cam)
        {
            if (!character.Enabled || Frozen || Invalid) { return; }

            CheckValidity();

            UpdateNetPlayerPosition(deltaTime);
            CheckDistFromCollider();
            UpdateCollisionCategories();

            Vector2 flowForce = Vector2.Zero;

            FindHull();
            PreventOutsideCollision();
            
            CheckBodyInRest(deltaTime);            

            splashSoundTimer -= deltaTime;

            if (forceStanding)
            {
                inWater = false;
                headInWater = false;
            }
            //ragdoll isn't in any room -> it's in the water
            else if (currentHull == null)
            {
                inWater = true;
                headInWater = true;
            }
            else
            {
                flowForce = GetFlowForce();

                headInWater = false;

                inWater = false;
                if (currentHull.WaterVolume > currentHull.Volume * 0.95f)
                {
                    inWater = true;
                }
                else
                {
                    floorY = GetFloorY();
                    float waterSurface = ConvertUnits.ToSimUnits(currentHull.Surface);
                    if (targetMovement.Y < 0.0f)
                    {
                        Vector2 colliderBottom = GetColliderBottom();
                        floorY = Math.Min(colliderBottom.Y, floorY);
                        //check if the bottom of the collider is below the current hull
                        if (floorY < ConvertUnits.ToSimUnits(currentHull.Rect.Y - currentHull.Rect.Height))
                        {
                            //set floorY to the position of the floor in the hull below the character
                            var lowerHull = Hull.FindHull(ConvertUnits.ToDisplayUnits(colliderBottom), useWorldCoordinates: false);
                            if (lowerHull != null) floorY = ConvertUnits.ToSimUnits(lowerHull.Rect.Y - lowerHull.Rect.Height);
                        }
                    }
                    float standHeight = 
                        HeadPosition.HasValue ? HeadPosition.Value :
                        TorsoPosition.HasValue ? TorsoPosition.Value :
                        Collider.GetMaxExtent() * 0.5f;
                    if (Collider.SimPosition.Y < waterSurface && waterSurface - floorY > standHeight * 0.95f)
                    {
                        inWater = true;
                    }
                }
            }

            if (flowForce.LengthSquared() > 0.001f)
            {
                Collider.ApplyForce(flowForce, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
            }

            if (currentHull == null ||
                currentHull.WaterVolume > currentHull.Volume * 0.95f ||
                ConvertUnits.ToSimUnits(currentHull.Surface) > Collider.SimPosition.Y)
            {
                Collider.ApplyWaterForces();
            }


            foreach (Limb limb in Limbs)
            {
                //find the room which the limb is in
                //the room where the ragdoll is in is used as the "guess", meaning that it's checked first                
                Hull limbHull = currentHull == null ? null : Hull.FindHull(limb.WorldPosition, currentHull);

                bool prevInWater = limb.inWater;
                limb.inWater = false;

                if (forceStanding)
                {
                    limb.inWater = false;
                }
                else if (limbHull == null)
                {
                    //limb isn't in any room -> it's in the water
                    limb.inWater = true;
                    if (limb.type == LimbType.Head) headInWater = true;
                }
                else if (limbHull.WaterVolume > 0.0f && Submarine.RectContains(limbHull.Rect, limb.Position))
                {
                    if (limb.Position.Y < limbHull.Surface)
                    {
                        limb.inWater = true;

                        if (flowForce.LengthSquared() > 0.001f)
                        {
                            limb.body.ApplyForce(flowForce, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                        }

                        surfaceY = limbHull.Surface;

                        if (limb.type == LimbType.Head)
                        {
                            headInWater = true;
                        }
                    }
                    //the limb has gone through the surface of the water
                    if (Math.Abs(limb.LinearVelocity.Y) > 5.0f && limb.inWater != prevInWater)
                    {
                        Splash(limb, limbHull);

                        //if the Character dropped into water, create a wave
                        if (limb.LinearVelocity.Y < 0.0f)
                        {
                            Vector2 impulse = limb.LinearVelocity * limb.Mass;
                            int n = (int)((limb.Position.X - limbHull.Rect.X) / Hull.WaveWidth);
                            limbHull.WaveVel[n] += MathHelper.Clamp(impulse.Y, -5.0f, 5.0f);
                        }
                    }
                }

                limb.Update(deltaTime);
            }
            
            bool onStairs = Stairs != null;
            Stairs = null;

            var contacts = Collider.FarseerBody.ContactList;
            while (Collider.FarseerBody.Enabled && contacts != null && contacts.Contact != null)
            {
                if (contacts.Contact.Enabled && contacts.Contact.IsTouching)
                {
                    contacts.Contact.GetWorldManifold(out Vector2 normal, out FarseerPhysics.Common.FixedArray2<Vector2> points);

                    switch (contacts.Contact.FixtureA.CollisionCategories)
                    {
                        case Physics.CollisionStairs:
                            Structure structure = contacts.Contact.FixtureA.Body.UserData as Structure;
                            if (structure != null && onStairs)
                            {
                                Stairs = structure;
                            }
                            break;
                    }
                    //    case Physics.CollisionPlatform:
                    //        Structure platform = contacts.Contact.FixtureA.Body.UserData as Structure;
                    //        if (IgnorePlatforms || colliderBottom.Y < ConvertUnits.ToSimUnits(platform.Rect.Y - 15))
                    //        {
                    //            contacts = contacts.Next;
                    //            continue;
                    //        }
                    //        break;
                    //    case Physics.CollisionWall:
                    //        break;
                    //    default:
                    //            contacts = contacts.Next;
                    //            continue;
                    //}


                    if (points[0].Y < Collider.SimPosition.Y)
                    {
                        floorY = Math.Max(floorY, points[0].Y);

                        onGround = true;
                        onFloorTimer = 0.1f;
                    }


                }

                contacts = contacts.Next;
            }

            //the ragdoll "stays on ground" for 50 millisecs after separation
            if (onFloorTimer <= 0.0f)
            {
                onGround = false;
            }
            else
            {
                onFloorTimer -= deltaTime;
            }

            Vector2 rayStart = Collider.SimPosition;
            Vector2 rayEnd = rayStart;
            rayEnd.Y -= Collider.height * 0.5f + Collider.radius + ColliderHeightFromFloor*1.2f;

            Vector2 colliderBottomDisplay = ConvertUnits.ToDisplayUnits(GetColliderBottom());
            if (!inWater && !character.IsDead && character.Stun <= 0f && levitatingCollider && Collider.LinearVelocity.Y > -ImpactTolerance)
            {
                float closestFraction = 1.0f;
                Fixture closestFixture = null;
                GameMain.World.RayCast((fixture, point, normal, fraction) =>
                {
                    switch (fixture.CollisionCategories)
                    {
                        case Physics.CollisionStairs:
                            Structure structure = fixture.Body.UserData as Structure;
                            if (inWater && targetMovement.Y < 0.5f) return -1;
                            if (colliderBottomDisplay.Y < structure.Rect.Y - structure.Rect.Height + 30 && TargetMovement.Y < 0.5f && !onStairs) return -1;
                            if (character.SelectedBy != null) return -1;
                            break;
                        case Physics.CollisionPlatform:
                            Structure platform = fixture.Body.UserData as Structure;
                            //ignore platforms if collider is below it
                            // OR allow the character to "lift" itself above it if heading upwards and not on stairs
                            if (IgnorePlatforms || (colliderBottomDisplay.Y < platform.Rect.Y - 16 && (targetMovement.Y <= 0.0f || onStairs))) return -1;
                            break;
                        case Physics.CollisionWall:
                        case Physics.CollisionLevel:
                            if (!fixture.CollidesWith.HasFlag(Physics.CollisionCharacter)) return -1;
                            break;
                        default:
                            return -1;
                    }

                    if (fraction < closestFraction)
                    {
                        closestFraction = fraction;
                        closestFixture = fixture;
                    }

                    return closestFraction;
                }
                , rayStart, rayEnd);

                if (closestFraction < 1.0f && closestFixture != null)
                {
                    onGround = true;

                    switch (closestFixture.CollisionCategories)
                    {
                        case Physics.CollisionStairs:
                            Stairs = closestFixture.Body.UserData as Structure;
                            onStairs = true;
                            break;
                    }

                    float tfloorY = rayStart.Y + (rayEnd.Y - rayStart.Y) * closestFraction;
                    float targetY = tfloorY + ((float)Math.Abs(Math.Cos(Collider.Rotation)) * Collider.height * 0.5f) + Collider.radius + ColliderHeightFromFloor;

                    if (Math.Abs(Collider.SimPosition.Y - targetY) > 0.01f)
                    {
                        if (onStairs)
                        {
                            Collider.LinearVelocity = new Vector2(Collider.LinearVelocity.X,
                               (targetY < Collider.SimPosition.Y ? Math.Sign(targetY - Collider.SimPosition.Y) : (targetY - Collider.SimPosition.Y)) * 5.0f);
                        }
                        else
                        {
                            Collider.LinearVelocity = new Vector2(Collider.LinearVelocity.X, (targetY - Collider.SimPosition.Y) * 5.0f);
                        }
                    }
                }
            }
            UpdateProjSpecific(deltaTime);
        }

        private void CheckBodyInRest(float deltaTime)
        {
            if (InWater || Collider.LinearVelocity.LengthSquared() > 0.01f || character.SelectedBy != null || !character.IsDead)
            {
                bodyInRestTimer = 0.0f;
                foreach (Limb limb in Limbs)
                {
                    limb.body.PhysEnabled = true;
                }
            }
            else if (Limbs.All(l => l != null && !l.body.Enabled || l.LinearVelocity.LengthSquared() < 0.001f))
            {
                bodyInRestTimer += deltaTime;
                if (bodyInRestTimer > BodyInRestDelay)
                {
                    foreach (Limb limb in Limbs)
                    {
                        limb.body.PhysEnabled = false;
                    }
                }
            }
        }

        public bool Invalid { get; private set; }
        private int validityResets;
        private bool CheckValidity()
        {
            bool isColliderValid = CheckValidity(Collider);
            bool limbsValid = true;
            foreach (Limb limb in limbs)
            {
                if (limb.body == null || !limb.body.Enabled) { continue; }
                if (!CheckValidity(limb.body))
                {
                    limbsValid = false;
                    break;
                }
            }
            bool isValid = isColliderValid && limbsValid;
            if (!isValid)
            {
                validityResets++;
                if (validityResets > 1)
                {
                    Invalid = true;
                    DebugConsole.ThrowError("Invalid ragdoll physics. Ragdoll freezed to prevent crashes.");
                    Collider.SetTransform(Vector2.Zero, 0.0f);
                    foreach (Limb limb in Limbs)
                    {
                        limb.body.SetTransform(Collider.SimPosition, 0.0f);
                        limb.body.ResetDynamics();
                    }
                    Frozen = true;
                }
            }
            return isValid;
        }

        private bool CheckValidity(PhysicsBody body)
        {
            string errorMsg = null;
            string bodyName = body.UserData is Limb limb ?
                "Limb (" + limb.type + ")" :
                "Collider";
            if (!MathUtils.IsValid(body.SimPosition) || Math.Abs(body.SimPosition.X) > 1e10f || Math.Abs(body.SimPosition.Y) > 1e10f)
            {
                errorMsg = bodyName + " position invalid (" + body.SimPosition + ", character: " + character.Name + "), resetting the ragdoll.";
            }
            else if (!MathUtils.IsValid(body.LinearVelocity) || Math.Abs(body.LinearVelocity.X) > 1000f || Math.Abs(body.LinearVelocity.Y) > 1000f)
            {
                errorMsg = bodyName + " velocity invalid (" + body.LinearVelocity + ", character: " + character.Name + "), resetting the ragdoll.";
            }
            else if (!MathUtils.IsValid(body.Rotation))
            {
                errorMsg = bodyName + " rotation invalid (" + body.Rotation + ", character: " + character.Name + "), resetting the ragdoll.";
            }
            else if (!MathUtils.IsValid(body.AngularVelocity) || Math.Abs(body.AngularVelocity) > 1000f)
            {
                errorMsg = bodyName + " angular velocity invalid (" + body.AngularVelocity + ", character: " + character.Name + "), resetting the ragdoll.";
            }
            if (errorMsg != null)
            {
                if (character.IsRemotePlayer)
                {
                    errorMsg += " Ragdoll controlled remotely.";
                }
                if (SimplePhysicsEnabled)
                {
                    errorMsg += " Simple physics enabled.";
                }
                if (GameMain.NetworkMember != null)
                {
                    errorMsg += GameMain.NetworkMember.IsClient ? " Playing as a client." : " Hosting a server.";
                }

#if DEBUG
                DebugConsole.ThrowError(errorMsg);
#else
                DebugConsole.NewMessage(errorMsg, Color.Red);
#endif
                GameAnalyticsManager.AddErrorEventOnce("Ragdoll.CheckValidity:" + character.ID, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);

                if (!MathUtils.IsValid(Collider.SimPosition) || Math.Abs(Collider.SimPosition.X) > 1e10f || Math.Abs(Collider.SimPosition.Y) > 1e10f)
                {
                    Collider.SetTransform(Vector2.Zero, 0.0f);
                }
                foreach (Limb otherLimb in Limbs)
                {
                    otherLimb.body.SetTransform(Collider.SimPosition, 0.0f);
                    otherLimb.body.ResetDynamics();
                }
                SetInitialLimbPositions();
                return false;
            }
            return true;
        }

        partial void UpdateProjSpecific(float deltaTime);

        partial void Splash(Limb limb, Hull limbHull);

        protected float GetFloorY(Limb refLimb = null, bool ignoreStairs = false)
        {
            PhysicsBody refBody = refLimb == null ? Collider : refLimb.body;

            return GetFloorY(refBody.SimPosition, ignoreStairs);            
        }

        protected float GetFloorY(Vector2 simPosition, bool ignoreStairs = false)
        {
            Vector2 rayStart = simPosition;
            float height = ColliderHeightFromFloor;
            if (HeadPosition.HasValue && MathUtils.IsValid(HeadPosition.Value)) height = Math.Max(height, HeadPosition.Value);
            if (TorsoPosition.HasValue && MathUtils.IsValid(TorsoPosition.Value)) height = Math.Max(height, TorsoPosition.Value);

            Vector2 rayEnd = rayStart - new Vector2(0.0f, height);

            Vector2 colliderBottomDisplay = ConvertUnits.ToDisplayUnits(GetColliderBottom());

            float closestFraction = 1;
            GameMain.World.RayCast((fixture, point, normal, fraction) =>
            {
                switch (fixture.CollisionCategories)
                {
                    case Physics.CollisionStairs:
                        if (ignoreStairs) return -1;
                        if (inWater && TargetMovement.Y < 0.5f) return -1;
                        break;
                    case Physics.CollisionPlatform:
                        Structure platform = fixture.Body.UserData as Structure;
                        if (colliderBottomDisplay.Y < platform.Rect.Y - 16 && (targetMovement.Y <= 0.0f || Stairs != null)) return -1;
                        if (IgnorePlatforms && TargetMovement.Y < -0.5f || Collider.Position.Y < platform.Rect.Y) return -1;
                        break;
                    case Physics.CollisionWall:
                    case Physics.CollisionLevel:
                        if (!fixture.CollidesWith.HasFlag(Physics.CollisionCharacter)) return -1;
                        break;
                    default:
                        return -1;
                }

                if (fraction < closestFraction)
                {
                    closestFraction = fraction;
                }

                return closestFraction;
            }
            , rayStart, rayEnd);


            if (closestFraction == 1) //raycast didn't hit anything
            {
                return (currentHull == null) ? -1000.0f : ConvertUnits.ToSimUnits(currentHull.Rect.Y - currentHull.Rect.Height);
            }
            else
            {
                return rayStart.Y + (rayEnd.Y - rayStart.Y) * closestFraction;
            }
        }

        public void SetPosition(Vector2 simPosition, bool lerp = false, bool ignorePlatforms = true)
        {
            if (!MathUtils.IsValid(simPosition))
            {
                DebugConsole.ThrowError("Attempted to move a ragdoll (" + character.Name + ") to an invalid position (" + simPosition + "). " + Environment.StackTrace);
                GameAnalyticsManager.AddErrorEventOnce(
                    "Ragdoll.SetPosition:InvalidPosition",
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    "Attempted to move a ragdoll (" + character.Name + ") to an invalid position (" + simPosition + "). " + Environment.StackTrace);
                return;
            }
            if (MainLimb == null) { return; }

            Vector2 limbMoveAmount = simPosition - MainLimb.SimPosition;

            if (lerp)
            {
                Collider.TargetPosition = simPosition;
                Collider.MoveToTargetPosition(true);
            }
            else
            {
                Collider.SetTransform(simPosition, Collider.Rotation);
            }

            foreach (Limb limb in Limbs)
            {
                if (limb.IsSevered) continue;
                //check visibility from the new position of the collider to the new position of this limb
                Vector2 movePos = limb.SimPosition + limbMoveAmount;

                TrySetLimbPosition(limb, simPosition, movePos, lerp, ignorePlatforms);
            }
        }

        protected void TrySetLimbPosition(Limb limb, Vector2 original, Vector2 simPosition, bool lerp = false, bool ignorePlatforms = true)
        {
            Vector2 movePos = simPosition;

            if (Vector2.DistanceSquared(original, simPosition) > 0.0001f)
            {
                Category collisionCategory = Physics.CollisionWall | Physics.CollisionLevel;
                if (!ignorePlatforms) { collisionCategory |= Physics.CollisionPlatform; }

                Body body = Submarine.PickBody(original, simPosition, null, collisionCategory);
            
                //if there's something in between the limbs
                if (body != null)
                {
                    //move the limb close to the position where the raycast hit something
                    movePos = original + ((simPosition - original) * Submarine.LastPickedFraction * 0.9f);
                }
            }

            if (lerp)
            {
                limb.body.TargetPosition = movePos;
                limb.body.MoveToTargetPosition(true);                
            }
            else
            {
                limb.body.SetTransform(movePos, limb.Rotation);
                limb.PullJointWorldAnchorB = limb.PullJointWorldAnchorA;
                limb.PullJointEnabled = false;
            }
        }


        private bool collisionsDisabled;

        protected void CheckDistFromCollider()
        {
            float allowedDist = Math.Max(Math.Max(Collider.radius, Collider.width), Collider.height) * 2.0f;     
            float resetDist = allowedDist * 5.0f;

            Vector2 diff = Collider.SimPosition - MainLimb.SimPosition;
            float distSqrd = diff.LengthSquared();

            if (distSqrd > resetDist * resetDist)
            {
                //ragdoll way too far, reset position
                SetPosition(Collider.SimPosition, true);
            }
            if (distSqrd > allowedDist * allowedDist)
            {
                //ragdoll too far from the collider, disable collisions until it's close enough
                //(in case the ragdoll has gotten stuck somewhere)

                Vector2 forceDir = diff / (float)Math.Sqrt(distSqrd);
                foreach (Limb limb in Limbs)
                {
                    if (limb.IsSevered) continue;
                    limb.body.CollidesWith = Physics.CollisionNone;
                    limb.body.ApplyForce(forceDir * limb.Mass * 10.0f, maxVelocity: 10.0f);
                }

                collisionsDisabled = true;
            }
            else if (collisionsDisabled)
            {
                //set the position of the ragdoll to make sure limbs don't get stuck inside walls when re-enabling collisions
                SetPosition(Collider.SimPosition, true);
                collisionsDisabled = false;
                //force collision categories to be updated
                prevCollisionCategory = Category.None;
            }
        }

        partial void UpdateNetPlayerPositionProjSpecific(float deltaTime, float lowestSubPos);
        private void UpdateNetPlayerPosition(float deltaTime)
        {
            if (GameMain.NetworkMember == null) return;

            float lowestSubPos = float.MaxValue;
            if (Submarine.Loaded.Any())
            {
                lowestSubPos = ConvertUnits.ToSimUnits(Submarine.Loaded.Min(s => s.HiddenSubPosition.Y - s.Borders.Height - 128.0f));
                for (int i = 0; i < character.MemState.Count; i++)
                {
                    if (character.Submarine == null)
                    {
                        //transform in-sub coordinates to outside coordinates
                        if (character.MemState[i].Position.Y > lowestSubPos)
                            character.MemState[i].TransformInToOutside();
                    }
                    else if (currentHull?.Submarine != null)
                    {
                        //transform outside coordinates to in-sub coordinates
                        if (character.MemState[i].Position.Y < lowestSubPos)
                            character.MemState[i].TransformOutToInside(currentHull.Submarine);
                    }
                }
            }

            UpdateNetPlayerPositionProjSpecific(deltaTime, lowestSubPos);
        }
        
        private Vector2 GetFlowForce()
        {
            Vector2 limbPos = Limbs[0].Position;

            Vector2 force = Vector2.Zero;
            foreach (Gap gap in Gap.GapList)
            {
                if (gap.Open <= 0.0f || gap.FlowTargetHull != currentHull || gap.LerpedFlowForce.LengthSquared() < 0.01f) continue;

                Vector2 gapPos = gap.SimPosition;
                float dist = Vector2.Distance(limbPos, gapPos);
                force += Vector2.Normalize(gap.LerpedFlowForce) * (Math.Max(gap.LerpedFlowForce.Length() - dist, 0.0f) / 500.0f);
            }
            return force;
        }

        /// <summary>
        /// Note that if there are multiple limbs of the same type, only the first of them is found in the dictionary.
        /// </summary>
        public Limb GetLimb(LimbType limbType)
        {
            limbDictionary.TryGetValue(limbType, out Limb limb);
            return limb;
        }

        public Vector2? GetMouthPosition()
        {
            Limb mouthLimb = GetLimb(LimbType.Head);
            if (mouthLimb == null) { return null; }
            float cos = (float)Math.Cos(mouthLimb.Rotation);
            float sin = (float)Math.Sin(mouthLimb.Rotation);
            Vector2 bodySize = mouthLimb.body.GetSize();
            Vector2 offset = new Vector2(mouthLimb.MouthPos.X * bodySize.X / 2, mouthLimb.MouthPos.Y * bodySize.Y / 2);
            return mouthLimb.SimPosition + new Vector2(offset.X * cos - offset.Y * sin, offset.X * sin + offset.Y * cos) * RagdollParams.LimbScale;
        }

        public Vector2 GetColliderBottom()
        {
            float offset = 0.0f;

            if (!character.IsUnconscious && !character.IsDead && character.Stun <= 0.0f)
            {
                offset = -ColliderHeightFromFloor;
            }

            float lowestBound = Collider.SimPosition.Y;
            if (Collider.FarseerBody.FixtureList != null)
            {
                for (int i = 0; i < Collider.FarseerBody.FixtureList.Count; i++)
                {
                    Collider.FarseerBody.GetTransform(out FarseerPhysics.Common.Transform transform);
                    Collider.FarseerBody.FixtureList[i].Shape.ComputeAABB(out FarseerPhysics.Collision.AABB aabb, ref transform, i);

                    lowestBound = Math.Min(aabb.LowerBound.Y, lowestBound);
                }
            }
            return new Vector2(Collider.SimPosition.X, lowestBound + offset);
        }

        public Limb FindLowestLimb()
        {
            Limb lowestLimb = null;
            foreach (Limb limb in Limbs)
            {
                if (lowestLimb == null)
                    lowestLimb = limb;
                else if (limb.SimPosition.Y < lowestLimb.SimPosition.Y)
                    lowestLimb = limb;
            }

            return lowestLimb;
        }

        public void ReleaseStuckLimbs()
        {
            Limbs.ForEach(l => l.Release());
        }

        public void Remove()
        {
            if (Limbs != null)
            {
                foreach (Limb l in Limbs)
                {
                    l.Remove();
                }
                limbs = null;
            }

            if (collider != null)
            {
                foreach (PhysicsBody b in collider)
                {
                    b.Remove();
                }
                collider = null;
            }

            if (outsideCollisionBlocker != null)
            {
                GameMain.World.RemoveBody(outsideCollisionBlocker);
                outsideCollisionBlocker = null;
            }

            if (LimbJoints != null)
            {
                foreach (RevoluteJoint joint in LimbJoints)
                {
                    GameMain.World.RemoveJoint(joint);
                }
                LimbJoints = null;
            }

            list.Remove(this);
        }

        public static void RemoveAll()
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                list[i].Remove();
            }
            System.Diagnostics.Debug.Assert(list.Count == 0, "Some ragdolls were not removed in Ragdoll.RemoveAll");
        }
    }
}
