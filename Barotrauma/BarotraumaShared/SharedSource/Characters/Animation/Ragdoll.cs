using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Dynamics.Joints;
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

        private static readonly List<Ragdoll> list = new List<Ragdoll>();

        struct Impact
        {
            public Fixture F1, F2;
            public Vector2 LocalNormal;
            public Vector2 Velocity;
            public Vector2 ImpactPos;

            public Impact(Fixture f1, Fixture f2, Contact contact, Vector2 velocity)
            {
                F1 = f1;
                F2 = f2;
                Velocity = velocity;
                LocalNormal = contact.Manifold.LocalNormal;
                contact.GetWorldManifold(out _, out FarseerPhysics.Common.FixedArray2<Vector2> points);
                ImpactPos = points[0];
            }
        }

        private readonly Queue<Impact> impactQueue = new Queue<Impact>();

        protected Hull currentHull;

        private bool accessRemovedCharacterErrorShown;

        private Limb[] limbs;
        public Limb[] Limbs
        {
            get
            {
                if (limbs == null)
                {
                    if (!accessRemovedCharacterErrorShown)
                    {
                        string errorMsg = "Attempted to access a potentially removed ragdoll. Character: " + character.Name + ", id: " + character.ID + ", removed: " + character.Removed + ", ragdoll removed: " + !list.Contains(this);
                        errorMsg += '\n' + Environment.StackTrace.CleanupStackTrace();
                        DebugConsole.ThrowError(errorMsg);
                        GameAnalyticsManager.AddErrorEventOnce(
                            "Ragdoll.Limbs:AccessRemoved",
                            GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                            "Attempted to access a potentially removed ragdoll. Character: " + character.Name + ", id: " + character.ID + ", removed: " + character.Removed + ", ragdoll removed: " + !list.Contains(this) + "\n" + Environment.StackTrace.CleanupStackTrace());
                        accessRemovedCharacterErrorShown = true;
                    }
                    return new Limb[0];
                }
                return limbs;
            }
        }

        public bool HasMultipleLimbsOfSameType => limbs == null ? false : Limbs.Length > limbDictionary.Count;

        private bool frozen;
        public bool Frozen
        {
            get { return frozen; }
            set 
            { 
                if (frozen == value) return;

                frozen = value;

                Collider.FarseerBody.LinearDamping = frozen ? (1.5f / (float)Timing.Step) : 0.0f;
                Collider.FarseerBody.AngularDamping = frozen ? (1.5f / (float)Timing.Step) : PhysicsBody.DefaultAngularDamping;
                Collider.FarseerBody.IgnoreGravity = frozen;

                //Collider.PhysEnabled = !frozen;
                if (frozen && MainLimb != null) MainLimb.PullJointWorldAnchorB = MainLimb.SimPosition;                
            }
        }

        private Dictionary<LimbType, Limb> limbDictionary;
        public LimbJoint[] LimbJoints;

        private bool simplePhysicsEnabled;

        public Character Character => character;
        protected Character character;

        protected float strongestImpact;

        private float splashSoundTimer;

        //the ragdoll builds a "tolerance" to the flow force when being pushed by water. 
        //Allows sudden forces (breach, letting water through a door) to heavily push the character around while ensuring flowing water won't make the characters permanently stuck.
        private float flowForceTolerance, flowStunTolerance;

        //the movement speed of the ragdoll
        public Vector2 movement;
        //the target speed towards which movement is interpolated
        protected Vector2 targetMovement;

        //a movement vector that overrides targetmovement if trying to steer
        //a Character to the position sent by server in multiplayer mode
        protected Vector2 overrideTargetMovement;
        
        protected float floorY, standOnFloorY;
        protected Vector2 floorNormal = Vector2.UnitY;
        protected float surfaceY;
        
        protected bool inWater, headInWater;
        public bool onGround;
        private Vector2 lastFloorCheckPos;
        private bool lastFloorCheckIgnoreStairs, lastFloorCheckIgnorePlatforms;


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
                if (value == colliderIndex || collider == null) { return; }
                if (value >= collider.Count || value < 0) { return; }

                if (collider[colliderIndex].height < collider[value].height)
                {
                    Vector2 pos1 = collider[colliderIndex].SimPosition;
                    pos1.Y -= collider[colliderIndex].height * ColliderHeightFromFloor;
                    Vector2 pos2 = pos1;
                    pos2.Y += collider[value].height * 1.1f;
                    if (GameMain.World.RayCast(pos1, pos2).Any(f => f.CollisionCategories.HasFlag(Physics.CollisionWall) && !(f.Body.UserData is Submarine))) { return; }
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
                if (!IsValid(mainLimb))
                {
                    Limb torso = GetLimb(LimbType.Torso);
                    Limb head = GetLimb(LimbType.Head);
                    mainLimb = torso ?? head;
                    if (!IsValid(mainLimb))
                    {
                        mainLimb = Limbs.FirstOrDefault(l => IsValid(l));
                    }
                }

                bool IsValid(Limb limb) => limb != null && !limb.IsSevered && !limb.IgnoreCollisions && !limb.Hidden;
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
                if (value == simplePhysicsEnabled) { return; }

                simplePhysicsEnabled = value;

                foreach (Limb limb in Limbs)
                {
                    if (limb.IsSevered) { continue; }
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

        public const float MAX_SPEED = 20;

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
                    if (limb.IsSevered) { continue; }
                    limb.body.Submarine = currSubmarine;
                }
                Collider.Submarine = currSubmarine;
            }
        }

        public bool IgnorePlatforms { get; set; }

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
                    // This can be the case if we manipulate the ragdoll at runtime (husk appendage, limb removal in the character editor)
                    if (id > limbs.Length - 1) { continue; }
                    var limb = limbs[id];
                    var itemList = kvp.Value;
                    limb.WearingItems.AddRange(itemList);
                }
            }

            if (character.IsHusk && character.Params.UseHuskAppendage)
            {
                bool inEditor = false;
#if CLIENT
                inEditor = Screen.Selected == GameMain.CharacterEditorScreen;
#endif

                var characterPrefab = CharacterPrefab.FindByFilePath(character.ConfigPath);
                if (characterPrefab?.XDocument != null)
                {
                    var mainElement = characterPrefab.XDocument.Root.IsOverride() ? characterPrefab.XDocument.Root.FirstElement() : characterPrefab.XDocument.Root;
                    foreach (var huskAppendage in mainElement.GetChildElements("huskappendage"))
                    {
                        if (!inEditor && huskAppendage.GetAttributeBool("onlyfromafflictions", false)) { continue; }
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
            collider?.ForEach(c => c.Remove());
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
                foreach (LimbJoint joint in LimbJoints)
                {
                    if (GameMain.World.JointList.Contains(joint.Joint)) { GameMain.World.Remove(joint.Joint); }
                }
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
                    (joint.WorldAnchorA - MathUtils.RotatePointAroundTarget(joint.LocalAnchorB, Vector2.Zero, joint.BodyA.Rotation + angle, true)),
                    joint.BodyA.Rotation + angle);
            }
        }

        protected void CreateLimbs()
        {
            limbs?.ForEach(l => l.Remove());
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
            GameMain.World.Add(joint.Joint);
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

            SubtractMass(limb);
            limb.Remove();
            foreach (LimbJoint limbJoint in attachedJoints)
            {
                GameMain.World.Remove(limbJoint.Joint);
            }
        }

        public bool OnLimbCollision(Fixture f1, Fixture f2, Contact contact)
        {
            if (f2.Body.UserData is Submarine && character.Submarine == (Submarine)f2.Body.UserData) { return false; }
            if (f2.UserData is Hull && character.Submarine != null) { return false; }

            //using the velocity of the limb would make the impact damage more realistic,
            //but would also make it harder to edit the animations because the forces/torques
            //would all have to be balanced in a way that prevents the character from doing
            //impact damage to itself
            Vector2 velocity = Collider.LinearVelocity;
            if (character.Submarine == null && f2.Body.UserData is Submarine)
            {
                velocity -= ((Submarine)f2.Body.UserData).Velocity;
            }

            //always collides with bodies other than structures
            if (!(f2.Body.UserData is Structure structure))
            {
                if (!f2.IsSensor)
                {
                    lock (impactQueue)
                    {
                        impactQueue.Enqueue(new Impact(f1, f2, contact, velocity));
                    }
                }
                return true;
            }

            Vector2 colliderBottom = GetColliderBottom();
            if (structure.IsPlatform)
            {
                if (IgnorePlatforms || currentHull == null) { return false; }

                if (colliderBottom.Y < ConvertUnits.ToSimUnits(structure.Rect.Y - 5)) { return false; }
                if (f1.Body.Position.Y < ConvertUnits.ToSimUnits(structure.Rect.Y - 5)) { return false; }
            }
            else if (structure.StairDirection != Direction.None)
            {
                Stairs = null;

                //don't collider with stairs if

                //1. bottom of the collider is at the bottom of the stairs and the character isn't trying to move upwards
                float stairBottomPos = ConvertUnits.ToSimUnits(structure.Rect.Y - structure.Rect.Height + 10);
                if (colliderBottom.Y < stairBottomPos && targetMovement.Y < 0.5f) { return false; }

                //2. bottom of the collider is at the top of the stairs and the character isn't trying to move downwards
                if (targetMovement.Y >= 0.0f && colliderBottom.Y >= ConvertUnits.ToSimUnits(structure.Rect.Y - Submarine.GridSize.Y * 5)) { return false; }

                //3. collided with the stairs from below
                if (contact.Manifold.LocalNormal.Y < 0.0f) { return false; }

                //4. contact points is above the bottom half of the collider
                contact.GetWorldManifold(out Vector2 normal, out FarseerPhysics.Common.FixedArray2<Vector2> points);
                if (points[0].Y > Collider.SimPosition.Y) { return false; }

                //5. in water
                if (inWater && targetMovement.Y < 0.5f) { return false; }

                //---------------

                //set stairs to that of the one dragging us
                if (character.SelectedBy != null)
                    Stairs = character.SelectedBy.AnimController.Stairs;
                else
                    Stairs = structure;

                if (Stairs == null)
                    return false;
            }

            lock (impactQueue)
            {
                impactQueue.Enqueue(new Impact(f1, f2, contact, velocity));
            }

            return true;
        }

        private void ApplyImpact(Fixture f1, Fixture f2, Vector2 localNormal, Vector2 impactPos, Vector2 velocity)
        {
            if (character.DisableImpactDamageTimer > 0.0f) { return; }

            Vector2 normal = localNormal;
            float impact = Vector2.Dot(velocity, -normal);
            if (f1.Body == Collider.FarseerBody || !Collider.Enabled)
            {
                bool isNotRemote = true;
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { isNotRemote = !character.IsRemotelyControlled; }

                if (isNotRemote)
                {
                    if (impact > ImpactTolerance)
                    {
                        impactPos = ConvertUnits.ToDisplayUnits(impactPos);
                        if (character.Submarine != null) impactPos += character.Submarine.Position;

                        float impactDamage = Math.Min((impact - ImpactTolerance) * ImpactDamageMultiplayer, character.MaxVitality * MaxImpactDamage);

                        character.LastDamageSource = null;
                        character.AddDamage(impactPos, AfflictionPrefab.ImpactDamage.Instantiate(impactDamage).ToEnumerable(), 0.0f, true);
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

        private readonly List<Limb> connectedLimbs = new List<Limb>();
        private readonly List<LimbJoint> checkedJoints = new List<LimbJoint>();
        public bool SeverLimbJoint(LimbJoint limbJoint)
        {
            if (!limbJoint.CanBeSevered || limbJoint.IsSevered)
            {
                return false;
            }

            limbJoint.IsSevered = true;
            limbJoint.Enabled = false;

            Vector2 limbDiff = limbJoint.LimbA.SimPosition - limbJoint.LimbB.SimPosition;
            if (limbDiff.LengthSquared() < 0.0001f) { limbDiff = Rand.Vector(1.0f); }
            limbDiff = Vector2.Normalize(limbDiff);
            float mass = limbJoint.BodyA.Mass + limbJoint.BodyB.Mass;
            limbJoint.LimbA.body.ApplyLinearImpulse(limbDiff * mass, (limbJoint.LimbA.SimPosition + limbJoint.LimbB.SimPosition) / 2.0f);
            limbJoint.LimbB.body.ApplyLinearImpulse(-limbDiff * mass, (limbJoint.LimbA.SimPosition + limbJoint.LimbB.SimPosition) / 2.0f);

            connectedLimbs.Clear();
            checkedJoints.Clear();
            GetConnectedLimbs(connectedLimbs, checkedJoints, MainLimb);
            foreach (Limb limb in Limbs)
            {
                if (connectedLimbs.Contains(limb)) { continue; }
                limb.IsSevered = true;
                if (limb.type == LimbType.RightHand)
                {
                    character.Inventory?.GetItemInLimbSlot(InvSlotType.RightHand)?.Drop(character);
                }
                else if (limb.type == LimbType.LeftHand)
                {
                    character.Inventory?.GetItemInLimbSlot(InvSlotType.LeftHand)?.Drop(character);
                }
            }

            if (!string.IsNullOrEmpty(character.BloodDecalName))
            {
                character.CurrentHull?.AddDecal(character.BloodDecalName, 
                    (limbJoint.LimbA.WorldPosition + limbJoint.LimbB.WorldPosition) / 2, MathHelper.Clamp(Math.Min(limbJoint.LimbA.Mass, limbJoint.LimbB.Mass), 0.5f, 2.0f), isNetworkEvent: false);
            }

            SeverLimbJointProjSpecific(limbJoint, playSound: true);
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
            {
                GameMain.NetworkMember.CreateEntityEvent(character, new object[] { NetEntityEvent.Type.Status });
            }
            return true;
        }

        partial void SeverLimbJointProjSpecific(LimbJoint limbJoint, bool playSound);

        protected List<Limb> GetConnectedLimbs(Limb limb)
        {
            connectedLimbs.Clear();
            checkedJoints.Clear();
            GetConnectedLimbs(connectedLimbs, checkedJoints, limb);
            return connectedLimbs;
        }

        private void GetConnectedLimbs(List<Limb> connectedLimbs, List<LimbJoint> checkedJoints, Limb limb)
        {
            connectedLimbs.Add(limb);

            foreach (LimbJoint joint in LimbJoints)
            {
                if (joint.IsSevered || checkedJoints.Contains(joint)) { continue; }
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
                if (Limbs[i] == null) { continue; }
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
                    "Attempted to find a hull at an invalid position (" + findPos + ")\n" + Environment.StackTrace.CleanupStackTrace());
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
                if (newHull?.Submarine == null && currentHull?.Submarine != null)
                {
                    //don't teleport out yet if the character is going through a gap
                    if (Gap.FindAdjacent(currentHull.ConnectedGaps, findPos, 150.0f) != null) { return; }
                    if (Gap.FindAdjacent(Gap.GapList.Where(g => g.Submarine == currentHull.Submarine), findPos, 150.0f) != null) { return; }
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
            if (currentHull?.Submarine == null) { return; }

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
                    if (Math.Sign((gap.Rect.Y - gap.Rect.Height / 2) - (currentHull.Rect.Y - currentHull.Rect.Height / 2)) !=
                        Math.Sign(character.Position.Y - (currentHull.Rect.Y - currentHull.Rect.Height / 2)))
                    {
                        continue;
                    }
                }

                gap.RefreshOutsideCollider();
            }
        }

        public void Teleport(Vector2 moveAmount, Vector2 velocityChange)
        {
            foreach (Limb limb in Limbs)
            {
                if (limb.IsSevered) { continue; }
                if (limb.body.FarseerBody.ContactList == null) { continue; }

                ContactEdge ce = limb.body.FarseerBody.ContactList;
                while (ce != null && ce.Contact != null)
                {
                    ce.Contact.Enabled = false;
                    ce = ce.Next;
                }
            }

            foreach (Limb limb in Limbs)
            {
                if (limb.IsSevered) { continue; }
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

            Category collisionCategory = (IgnorePlatforms) ?
                wall | Physics.CollisionProjectile | Physics.CollisionStairs
                : wall | Physics.CollisionProjectile | Physics.CollisionPlatform | Physics.CollisionStairs;

            if (collisionCategory == prevCollisionCategory) { return; }
            prevCollisionCategory = collisionCategory;

            Collider.CollidesWith = collisionCategory | Physics.CollisionItemBlocking;

            foreach (Limb limb in Limbs)
            {
                if (limb.IgnoreCollisions || limb.IsSevered) { continue; }

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

            while (impactQueue.Count > 0)
            {
                var impact = impactQueue.Dequeue();
                ApplyImpact(impact.F1, impact.F2, impact.LocalNormal, impact.ImpactPos, impact.Velocity);
            }

            CheckValidity();

            UpdateNetPlayerPosition(deltaTime);
            CheckDistFromCollider();
            UpdateCollisionCategories();

            FindHull();
            PreventOutsideCollision();
            
            CheckBodyInRest(deltaTime);            

            splashSoundTimer -= deltaTime;

            if (character.Submarine == null && Level.Loaded != null)
            {
                if (Collider.SimPosition.Y > Level.Loaded.TopBarrier.Position.Y)
                {
                    Collider.LinearVelocity = new Vector2(Collider.LinearVelocity.X, Math.Min(Collider.LinearVelocity.Y, -1));
                }
                else if (Collider.SimPosition.Y < Level.Loaded.BottomBarrier.Position.Y)
                {
                    Collider.LinearVelocity = new Vector2(Collider.LinearVelocity.X, 
                        MathHelper.Clamp(Collider.LinearVelocity.Y, Level.Loaded.BottomBarrier.Position.Y - Collider.SimPosition.Y, 10.0f));
                }
                foreach (Limb limb in Limbs)
                {
                    if (limb.SimPosition.Y > Level.Loaded.TopBarrier.Position.Y)
                    {
                        limb.body.LinearVelocity = new Vector2(limb.LinearVelocity.X, Math.Min(limb.LinearVelocity.Y, -1));
                    }
                    else if (limb.SimPosition.Y < Level.Loaded.BottomBarrier.Position.Y)
                    {
                        limb.body.LinearVelocity = new Vector2(
                            limb.LinearVelocity.X,
                            MathHelper.Clamp(limb.LinearVelocity.Y, Level.Loaded.BottomBarrier.Position.Y - limb.SimPosition.Y, 10.0f));
                    }
                }
            }

            if (forceStanding)
            {
                inWater = false;
                headInWater = false;
                RefreshFloorY(ignoreStairs: Stairs == null);
            }
            //ragdoll isn't in any room -> it's in the water
            else if (currentHull == null)
            {
                inWater = true;
                headInWater = true;
            }
            else
            {
                headInWater = false;
                inWater = false;
                if (currentHull.WaterVolume > currentHull.Volume * 0.95f)
                {
                    inWater = true;
                }
                else
                {
                    RefreshFloorY(ignoreStairs: Stairs == null);
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

            UpdateHullFlowForces(deltaTime);

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

            if (!inWater && character.AllowInput && levitatingCollider && Collider.LinearVelocity.Y > -ImpactTolerance && onGround)
            {
                float targetY = standOnFloorY + ((float)Math.Abs(Math.Cos(Collider.Rotation)) * Collider.height * 0.5f) + Collider.radius + ColliderHeightFromFloor;
                if (Math.Abs(Collider.SimPosition.Y - targetY) > 0.01f && onGround)
                {
                    if (Stairs != null)
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
            UpdateProjSpecific(deltaTime, cam);
        }

        private void CheckBodyInRest(float deltaTime)
        {
            if (SimplePhysicsEnabled) { return; }

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
            if (limbs == null)
            {
                DebugConsole.ThrowError("Attempted to check the validity of a potentially removed ragdoll. Character: " + character.Name + ", id: " + character.ID + ", removed: " + character.Removed + ", ragdoll removed: " + !list.Contains(this));
                Invalid = true;
                return false;
            }
            bool isColliderValid = CheckValidity(Collider);
            if (!isColliderValid) { Collider.ResetDynamics(); }
            bool limbsValid = true;
            foreach (Limb limb in limbs)
            {
                if (limb.body == null || !limb.body.Enabled) { continue; }
                if (!CheckValidity(limb.body))
                {
                    limbsValid = false;
                    limb.body.ResetDynamics();
                    break;
                }
            }
            bool isValid = isColliderValid && limbsValid;
            if (!isValid)
            {
                validityResets++;
                if (validityResets > 3)
                {
                    Invalid = true;
                    DebugConsole.ThrowError("Invalid ragdoll physics. Ragdoll frozen to prevent crashes.");
                    Collider.SetTransform(Vector2.Zero, 0.0f);
                    Collider.ResetDynamics();
                    foreach (Limb limb in Limbs)
                    {
                        limb.body?.SetTransform(Collider.SimPosition, 0.0f);
                        limb.body?.ResetDynamics();
                    }
                    Frozen = true;
                }
            }
            return isValid;
        }

        private bool CheckValidity(PhysicsBody body)
        {
            string errorMsg = null;
            if (!MathUtils.IsValid(body.SimPosition) || Math.Abs(body.SimPosition.X) > 1e10f || Math.Abs(body.SimPosition.Y) > 1e10f)
            {
                errorMsg = GetBodyName() + " position invalid (" + body.SimPosition + ", character: " + character.Name + "), resetting the ragdoll.";
            }
            else if (!MathUtils.IsValid(body.LinearVelocity) || Math.Abs(body.LinearVelocity.X) > 1000f || Math.Abs(body.LinearVelocity.Y) > 1000f)
            {
                errorMsg = GetBodyName() + " velocity invalid (" + body.LinearVelocity + ", character: " + character.Name + "), resetting the ragdoll.";
            }
            else if (!MathUtils.IsValid(body.Rotation))
            {
                errorMsg = GetBodyName() + " rotation invalid (" + body.Rotation + ", character: " + character.Name + "), resetting the ragdoll.";
            }
            else if (!MathUtils.IsValid(body.AngularVelocity) || Math.Abs(body.AngularVelocity) > 1000f)
            {
                errorMsg = GetBodyName() + " angular velocity invalid (" + body.AngularVelocity + ", character: " + character.Name + "), resetting the ragdoll.";
            }
            if (errorMsg != null)
            {
                if (character.IsRemotelyControlled)
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

            string GetBodyName()
            {
                return body.UserData is Limb limb ? "Limb (" + limb.type + ")" : "Collider";
            }

            return true;
        }

        partial void UpdateProjSpecific(float deltaTime, Camera cam);

        partial void Splash(Limb limb, Hull limbHull);

        private void UpdateHullFlowForces(float deltaTime)
        {
            if (currentHull == null) { return; }

            const float StunForceThreshold = 5.0f;
            const float StunDuration = 0.5f;
            const float ToleranceIncreaseSpeed = 5.0f;
            const float ToleranceDecreaseSpeed = 1.0f;

            //how much distance to a gap affects the force it exerts on the character
            const float DistanceFactor = 0.5f;
            const float ForceMultiplier = 0.035f;

            Vector2 flowForce = Vector2.Zero;
            foreach (Gap gap in Gap.GapList)
            {
                if (gap.Open <= 0.0f || !gap.linkedTo.Contains(currentHull) || gap.LerpedFlowForce.LengthSquared() < 0.01f) { continue; }
                float dist = Vector2.Distance(MainLimb.WorldPosition, gap.WorldPosition) * DistanceFactor;
                flowForce += Vector2.Normalize(gap.LerpedFlowForce) * (Math.Max(gap.LerpedFlowForce.Length() - dist, 0.0f) * ForceMultiplier);
            }

            //throwing conscious/moving characters around takes more force -> double the flow force
            if (character.CanMove) { flowForce *= 2.0f; }
            flowForce *= 1 - Math.Clamp(character.GetStatValue(StatTypes.FlowResistance), 0f, 1f);

            float flowForceMagnitude = flowForce.Length();
            float limbMultipier = limbs.Count(l => l.inWater) / (float)limbs.Length;
            //if the force strong enough, stun the character to let it get thrown around by the water
            if ((flowForceMagnitude * limbMultipier) - flowStunTolerance > StunForceThreshold)
            {
                character.Stun = Math.Max(character.Stun, StunDuration);
                flowStunTolerance = Math.Max(flowStunTolerance, flowForceMagnitude);
            }

            if (character == Character.Controlled && inWater && Screen.Selected?.Cam != null)
            {
                float shakeStrength = Math.Min(flowForceMagnitude / 10.0f, 5.0f) * limbMultipier;
                Screen.Selected.Cam.Shake = Math.Max(Screen.Selected.Cam.Shake, shakeStrength);
            }

            if (flowForceMagnitude > 0.0001f)
            {
                flowForce = Vector2.Normalize(flowForce) * Math.Max(flowForceMagnitude - flowForceTolerance, 0.0f);
            }

            if (flowForceTolerance <= flowForceMagnitude * 1.5f && inWater)
            {
                //build up "tolerance" to the flow force
                //ensures the character won't get permanently stuck by forces, while allowing sudden changes in flow to push the character hard
                flowForceTolerance += deltaTime * ToleranceIncreaseSpeed;
                flowStunTolerance = Math.Max(flowStunTolerance, flowForceTolerance);
            }
            else
            {
                flowForceTolerance = Math.Max(flowForceTolerance - deltaTime * ToleranceDecreaseSpeed, 0.0f);
                flowStunTolerance = Math.Max(flowStunTolerance - deltaTime * ToleranceDecreaseSpeed, 0.0f);
            }

            if (flowForce.LengthSquared() > 0.001f)
            {
                Collider.ApplyForce(flowForce, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                foreach (Limb limb in limbs)
                {
                    if (!limb.inWater) { continue; }
                    limb.body.ApplyForce(flowForce, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                }
            }
        }

        public void ForceRefreshFloorY()
        {
            lastFloorCheckPos = Vector2.Zero;
        }

        private void RefreshFloorY(Limb refLimb = null, bool ignoreStairs = false)
        {
            PhysicsBody refBody = refLimb == null ? Collider : refLimb.body;
            if (Vector2.DistanceSquared(lastFloorCheckPos, refBody.SimPosition) > 0.1f * 0.1f || lastFloorCheckIgnoreStairs != ignoreStairs || lastFloorCheckIgnorePlatforms != IgnorePlatforms)
            {
                floorY = GetFloorY(refBody.SimPosition, ignoreStairs);
                lastFloorCheckPos = refBody.SimPosition;
                lastFloorCheckIgnoreStairs = ignoreStairs;
                lastFloorCheckIgnorePlatforms = IgnorePlatforms;
            }
        }


        private float GetFloorY(Vector2 simPosition, bool ignoreStairs = false)
        {
            onGround = false;
            Stairs = null;
            Vector2 rayStart = simPosition;
            float height = ColliderHeightFromFloor;
            if (HeadPosition.HasValue && MathUtils.IsValid(HeadPosition.Value)) { height = Math.Max(height, HeadPosition.Value); }
            if (TorsoPosition.HasValue && MathUtils.IsValid(TorsoPosition.Value)) { height = Math.Max(height, TorsoPosition.Value); }

            Vector2 rayEnd = rayStart - new Vector2(0.0f, height);
            Vector2 onGroundRayEnd = rayStart - Vector2.UnitY * (Collider.height * 0.5f + Collider.radius + ColliderHeightFromFloor * 1.2f);
            Vector2 colliderBottomDisplay = ConvertUnits.ToDisplayUnits(GetColliderBottom());

            Fixture standOnFloorFixture = null;
            float standOnFloorFraction = 1;
            float closestFraction = 1;
            GameMain.World.RayCast((fixture, point, normal, fraction) =>
            {
                switch (fixture.CollisionCategories)
                {
                    case Physics.CollisionStairs:
                        if (inWater && TargetMovement.Y < 0.5f) { return -1; }

                        if (character.SelectedBy == null && fraction < standOnFloorFraction)
                        {
                            Structure structure = fixture.Body.UserData as Structure;
                            if (colliderBottomDisplay.Y >= structure.Rect.Y - structure.Rect.Height + 30 || TargetMovement.Y > 0.5f || Stairs != null)
                            {
                                standOnFloorFraction = fraction;
                                standOnFloorFixture = fixture;
                            }
                        }

                        if (ignoreStairs) { return -1; }
                        break;
                    case Physics.CollisionPlatform:
                        Structure platform = fixture.Body.UserData as Structure;

                        if (!IgnorePlatforms && fraction < standOnFloorFraction)
                        {
                            if (colliderBottomDisplay.Y >= platform.Rect.Y - 16 || (targetMovement.Y > 0.0f && Stairs == null))
                            {
                                standOnFloorFraction = fraction;
                                standOnFloorFixture = fixture;
                            }
                        }

                        if (colliderBottomDisplay.Y < platform.Rect.Y - 16 && (targetMovement.Y <= 0.0f || Stairs != null)) return -1;
                        if (IgnorePlatforms && TargetMovement.Y < -0.5f || Collider.Position.Y < platform.Rect.Y) return -1;
                        break;
                    case Physics.CollisionWall:
                    case Physics.CollisionLevel:
                        if (!fixture.CollidesWith.HasFlag(Physics.CollisionCharacter)) { return -1; }
                        if (fixture.Body.UserData is Submarine && character.Submarine != null) { return -1; }
                        if (fixture.IsSensor) { return -1; }
                        if (fraction < standOnFloorFraction)
                        {
                            standOnFloorFraction = fraction;
                            standOnFloorFixture = fixture;
                        }
                        break;
                    default:
                        System.Diagnostics.Debug.Assert(false, "Floor raycast should not have hit a fixture with the collision category " + fixture.CollisionCategories);
                        return -1;
                }

                if (fraction < closestFraction)
                {
                    floorNormal = normal;
                    closestFraction = fraction;
                }

                return closestFraction;
            }, rayStart, rayEnd, Physics.CollisionStairs | Physics.CollisionPlatform | Physics.CollisionWall | Physics.CollisionLevel);

            if (standOnFloorFixture != null)
            {
                standOnFloorY = rayStart.Y + (rayEnd.Y - rayStart.Y) * standOnFloorFraction;
                if (rayStart.Y - standOnFloorY < Collider.height * 0.5f + Collider.radius + ColliderHeightFromFloor * 1.2f)
                {
                    onGround = true;
                    if (standOnFloorFixture.CollisionCategories == Physics.CollisionStairs)
                    {
                        Stairs = standOnFloorFixture.Body.UserData as Structure;
                    }
                }
            }

            if (closestFraction == 1) //raycast didn't hit anything
            {
                floorNormal = Vector2.UnitY;
                return (currentHull == null) ? -1000.0f : ConvertUnits.ToSimUnits(currentHull.Rect.Y - currentHull.Rect.Height);
            }
            else
            {
                return rayStart.Y + (rayEnd.Y - rayStart.Y) * closestFraction;
            }
        }

        public void SetPosition(Vector2 simPosition, bool lerp = false, bool ignorePlatforms = true, bool forceMainLimbToCollider = false)
        {
            if (!MathUtils.IsValid(simPosition))
            {
                DebugConsole.ThrowError("Attempted to move a ragdoll (" + character.Name + ") to an invalid position (" + simPosition + "). " + Environment.StackTrace.CleanupStackTrace());
                GameAnalyticsManager.AddErrorEventOnce(
                    "Ragdoll.SetPosition:InvalidPosition",
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    "Attempted to move a ragdoll (" + character.Name + ") to an invalid position (" + simPosition + "). " + Environment.StackTrace.CleanupStackTrace());
                return;
            }
            if (MainLimb == null) { return; }

            Vector2 limbMoveAmount = forceMainLimbToCollider ? simPosition - MainLimb.SimPosition : simPosition - Collider.SimPosition;
            if (lerp)
            {
                Collider.TargetPosition = simPosition;
                Collider.MoveToTargetPosition(true);
            }
            else
            {
                Collider.SetTransform(simPosition, Collider.Rotation);
            }

            if (!MathUtils.NearlyEqual(limbMoveAmount, Vector2.Zero))
            {
                foreach (Limb limb in Limbs)
                {
                    if (limb.IsSevered) { continue; }
                    //check visibility from the new position of the collider to the new position of this limb
                    Vector2 movePos = limb.SimPosition + limbMoveAmount;
                    TrySetLimbPosition(limb, simPosition, movePos, lerp, ignorePlatforms);
                }
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
            allowedDist = Math.Max(allowedDist, 1.0f);
            float resetDist = allowedDist * 5.0f;

            Vector2 diff = Collider.SimPosition - MainLimb.SimPosition;
            float distSqrd = diff.LengthSquared();

            if (distSqrd > resetDist * resetDist)
            {
                //ragdoll way too far, reset position
                SetPosition(Collider.SimPosition, true, forceMainLimbToCollider: true);
            }
            if (distSqrd > allowedDist * allowedDist)
            {
                //ragdoll too far from the collider, disable collisions until it's close enough
                //(in case the ragdoll has gotten stuck somewhere)

                Vector2 forceDir = diff / (float)Math.Sqrt(distSqrd);
                foreach (Limb limb in Limbs)
                {
                    if (limb.IsSevered) { continue; }
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
        
        /// <summary>
        /// Note that if there are multiple limbs of the same type, only the first of them is found in the dictionary.
        /// </summary>
        public Limb GetLimb(LimbType limbType, bool excludeSevered = true)
        {
            Limb limb = null;
            if (HasMultipleLimbsOfSameType)
            {
                for (int i = 0; i < 10; i++)
                {
                    limbDictionary.TryGetValue(limbType, out limb);
                    if (limb == null)
                    {
                        // No limbs found
                        break;
                    }
                    if (!excludeSevered || !limb.IsSevered)
                    {
                        // Found a valid limb
                        break;
                    }
                }
            }
            else
            {
                limbDictionary.TryGetValue(limbType, out limb);
            }
            if (excludeSevered && limb != null && limb.IsSevered)
            {
                limb = null;
            }
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
            return mouthLimb.SimPosition + new Vector2(offset.X * cos - offset.Y * sin, offset.X * sin + offset.Y * cos) * mouthLimb.Scale * RagdollParams.LimbScale;
        }

        public Vector2 GetColliderBottom()
        {
            float offset = 0.0f;

            if (!character.IsDead && character.Stun <= 0.0f && !character.IsIncapacitated)
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
                if (limb.IsSevered) { continue; }
                if (lowestLimb == null)
                {
                    lowestLimb = limb;
                }
                else if (limb.SimPosition.Y < lowestLimb.SimPosition.Y)
                {
                    lowestLimb = limb;
                }
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

            if (LimbJoints != null)
            {
                foreach (var joint in LimbJoints)
                {
                    var j = joint.Joint;
                    if (GameMain.World.JointList.Contains(j))
                    {
                        GameMain.World.Remove(j);
                    }
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
