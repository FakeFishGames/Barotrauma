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

namespace Barotrauma
{
    abstract partial class Ragdoll
    {
        public abstract RagdollParams RagdollParams { get; protected set; }

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

        protected float ColliderHeightFromFloor => ConvertUnits.ToSimUnits(RagdollParams.ColliderHeightFromFloor) * RagdollParams.JointScale;

        public Structure Stairs;
                
        protected Direction dir;

        public Direction TargetDir;

        protected List<PhysicsBody> collider;
        protected int colliderIndex = 0;

        private Category prevCollisionCategory = Category.None;

        private Body outsideCollisionBlocker;

        public PhysicsBody Collider
        {
            get
            {
                return collider[colliderIndex];
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
                if (value == colliderIndex) return;
                if (value >= collider.Count || value < 0) return;

                if (collider[colliderIndex].height<collider[value].height)
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

        public Limb MainLimb
        {
            get;
            private set;
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
                        if (limb.IsSevered) continue;
                        limb.body.SetTransform(Collider.SimPosition, Collider.Rotation);
                    }
                }
            }
        }

        // Currently the camera cannot handle greater speeds. It starts to lag behind.
        public const float MAX_SPEED = 9;

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

        protected abstract float? HeadPosition { get; }
        protected abstract float? HeadAngle { get; }
        protected abstract float? TorsoPosition { get; }
        protected abstract float? TorsoAngle { get; }

        public float ImpactTolerance => RagdollParams.ImpactTolerance;
        public bool Draggable => RagdollParams.Draggable;
        public bool CanEnterSubmarine => RagdollParams.CanEnterSubmarine;

        public float Dir
        {
            get { return ((dir == Direction.Left) ? -1.0f : 1.0f); }
        }

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
                Submarine currSubmarine = currentHull == null ? null : currentHull.Submarine;
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
            if (ragdollParams != null)
            {
                RagdollParams = ragdollParams;
            }
            foreach (var limbParams in RagdollParams.Limbs)
            {
                if (!PhysicsBody.IsValidShape(limbParams.Radius, limbParams.Height, limbParams.Width))
                {
                    {
                        DebugConsole.ThrowError("Cannot create the ragdoll: invalid collider dimensions on limb: " + limbParams.Name);
                        return;
                    }
                }
            }
            foreach (var colliderParams in RagdollParams.ColliderParams)
            {
                if (!PhysicsBody.IsValidShape(colliderParams.Radius, colliderParams.Height, colliderParams.Width))
                {
                    {
                        DebugConsole.ThrowError("Cannot create the ragdoll: invalid collider dimensions on collider: " + colliderParams.Name);
                        return;
                    }
                }
            }
            CreateColliders();
            CreateLimbs();
            CreateJoints();
            UpdateCollisionCategories();
            Limb torso = GetLimb(LimbType.Torso);
            Limb head = GetLimb(LimbType.Head);
            MainLimb = torso ?? head;
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
            foreach (ColliderParams cParams in RagdollParams.ColliderParams)
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

            foreach (var joint in LimbJoints)
            {
                if (joint == null) { continue; }
                joint.LimbB?.body?.SetTransform(
                    joint.BodyA.Position + (joint.LocalAnchorA - joint.LocalAnchorB) * 0.1f,
                    (joint.LowerLimit + joint.UpperLimit) / 2.0f);
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
        /// Inversed draw order, which is used for drawing the limbs in 3d (deformable sprites).
        /// </summary>
        protected Limb[] inversedLimbDrawOrder;

        /// <summary>
        /// Saves all serializable data in the currently selected ragdoll params. This method should properly handle character flipping.
        /// </summary>
        public void SaveRagdoll(string fileNameWithoutExtension = null)
        {
            SaveJoints();
            RagdollParams.Save(fileNameWithoutExtension);
        }

        /// <summary>
        /// Resets the serializable data to the currently selected ragdoll params.
        /// Force reloading always loads the xml stored in the disk.
        /// </summary>
        public void ResetRagdoll(bool forceReload = false)
        {
            RagdollParams.Reset(forceReload);
            ResetJoints();
            ResetLimbs();
        }

        /// <summary>
        /// Saves the current joint values to the serializable joint params. This method should properly handle character flipping.
        /// </summary>
        public void SaveJoints()
        {
            LimbJoints.ForEach(j => j.SaveParams());
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
            byte limb1ID = Convert.ToByte(jointParams.Limb1);
            byte limb2ID = Convert.ToByte(jointParams.Limb2);
            LimbJoint joint = new LimbJoint(Limbs[limb1ID], Limbs[limb2ID], jointParams, this);
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

        public void AddJoint(XElement subElement, float scale = 1.0f)
        {
            byte limb1ID = Convert.ToByte(subElement.Attribute("limb1").Value);
            byte limb2ID = Convert.ToByte(subElement.Attribute("limb2").Value);

            Vector2 limb1Pos = subElement.GetAttributeVector2("limb1anchor", Vector2.Zero) * scale;
            limb1Pos = ConvertUnits.ToSimUnits(limb1Pos);

            Vector2 limb2Pos = subElement.GetAttributeVector2("limb2anchor", Vector2.Zero) * scale;
            limb2Pos = ConvertUnits.ToSimUnits(limb2Pos);

            LimbJoint joint = new LimbJoint(Limbs[limb1ID], Limbs[limb2ID], limb1Pos, limb2Pos);
            //joint.CanBeSevered = subElement.GetAttributeBool("canbesevered", true);

            if (subElement.Attribute("lowerlimit") != null)
            {
                joint.LimitEnabled = true;
                joint.LowerLimit = float.Parse(subElement.Attribute("lowerlimit").Value) * ((float)Math.PI / 180.0f);
                joint.UpperLimit = float.Parse(subElement.Attribute("upperlimit").Value) * ((float)Math.PI / 180.0f);
            }

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
            foreach (LimbJoint limbJoint in attachedJoints)
            {
                GameMain.World.RemoveJoint(limbJoint);
            }
        }
          
        public bool OnLimbCollision(Fixture f1, Fixture f2, Contact contact)
        {
            Structure structure = f2.Body.UserData as Structure;

            if (f2.Body.UserData is Submarine && character.Submarine == (Submarine)f2.Body.UserData) return false;

            //only collide with the ragdoll's own blocker
            if (f2.Body.UserData as string == "blocker" && f2.Body != outsideCollisionBlocker) return false;

            //always collides with bodies other than structures
            if (structure == null)
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
                Vector2 normal; FarseerPhysics.Common.FixedArray2<Vector2> points;
                contact.GetWorldManifold(out normal, out points);
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

            Vector2 normal = contact.Manifold.LocalNormal;            
            Vector2 velocity = f1.Body.LinearVelocity;

            if (character.Submarine == null && f2.Body.UserData is Submarine) velocity -= ((Submarine)f2.Body.UserData).Velocity;

            float impact = Vector2.Dot(velocity, -normal);
            if (f1.Body == Collider.FarseerBody)
            {
                if (!character.IsRemotePlayer || GameMain.Server != null)
                {
                    if (impact > ImpactTolerance)
                    {
                        contact.GetWorldManifold(out _, out FarseerPhysics.Common.FixedArray2<Vector2> points);
                        Vector2 impactPos = ConvertUnits.ToDisplayUnits(points[0]);
                        if (character.Submarine != null) impactPos += character.Submarine.Position;

                        character.LastDamageSource = null;
                        character.AddDamage(impactPos, new List<Affliction>() { AfflictionPrefab.InternalDamage.Instantiate((impact - ImpactTolerance) * 10.0f) }, 0.0f, true);
                        strongestImpact = Math.Max(strongestImpact, impact - ImpactTolerance);
                        character.ApplyStatusEffects(ActionType.OnImpact, 1.0f);
                    }
                }
            }

            ImpactProjSpecific(impact, f1.Body);
        }

        public void SeverLimbJoint(LimbJoint limbJoint)
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

            SeverLimbJointProjSpecific(limbJoint);

            if (GameMain.Server != null)
            {
                GameMain.Server.CreateEntityEvent(character, new object[] { NetEntityEvent.Type.Status });
            }
        }

        partial void SeverLimbJointProjSpecific(LimbJoint limbJoint);

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
                if (limb == null || limb.IsSevered) continue;

                limb.Dir = Dir;

                if (limb.MouthPos.HasValue)
                {
                    limb.MouthPos = new Vector2(
                        -limb.MouthPos.Value.X,
                        limb.MouthPos.Value.Y);
                }

                limb.MirrorPullJoint();
            }

            FlipProjSpecific();
        }

        partial void FlipProjSpecific();

        public Vector2 GetCenterOfMass()
        {
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
                if (newHull != null)
                {
                    Vector2 hullDiff = WorldPosition - newHull.WorldPosition;
                    Vector2 moveDir = hullDiff.LengthSquared() < 0.001f ? Vector2.UnitY : Vector2.Normalize(hullDiff);

                    //find a position 32 units away from the hull
                    Vector2? intersection = MathUtils.GetLineRectangleIntersection(
                        newHull.WorldPosition, 
                        newHull.WorldPosition + moveDir * Math.Max(newHull.Rect.Width, newHull.Rect.Height),
                        new Rectangle(newHull.WorldRect.X - 32, newHull.WorldRect.Y + 32, newHull.WorldRect.Width + 64, newHull.Rect.Height + 64));

                    if (intersection != null)
                    {
                        Collider.SetTransform(ConvertUnits.ToSimUnits((Vector2)intersection), Collider.Rotation);
                    }
                }

                return;
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
                    Teleport(ConvertUnits.ToSimUnits(currentHull.Submarine.Position - newHull.Submarine.Position),
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
        }

        private void UpdateCollisionCategories()
        {
            Category wall = currentHull == null ? 
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

        public bool forceStanding;

        public void Update(float deltaTime, Camera cam)
        {
            if (!character.Enabled || Frozen) return;

            UpdateNetPlayerPosition(deltaTime);
            CheckDistFromCollider();
            UpdateCollisionCategories();

            Vector2 flowForce = Vector2.Zero;

            FindHull();
            PreventOutsideCollision();

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
                    if (HeadPosition.HasValue &&
                        Collider.SimPosition.Y < waterSurface && waterSurface - floorY > HeadPosition * 0.95f)
                    {
                        inWater = true;
                    }
                }
            }

            if (flowForce.LengthSquared() > 0.001f)
            {
                Collider.ApplyForce(flowForce);
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
                            limb.body.ApplyForce(flowForce);
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
                    Vector2 normal;
                    FarseerPhysics.Common.FixedArray2<Vector2> points;

                    contacts.Contact.GetWorldManifold(out normal, out points);

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

            if (forceStanding)
            {
                onGround = true;
            }
            //the ragdoll "stays on ground" for 50 millisecs after separation
            else if (onFloorTimer <= 0.0f)
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
            if (!inWater && !character.IsDead && character.Stun <= 0f && levitatingCollider && Collider.LinearVelocity.Y>-ImpactTolerance)
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

        partial void UpdateProjSpecific(float deltaTime);

        partial void Splash(Limb limb, Hull limbHull);

        protected float GetFloorY(Limb refLimb = null)
        {
            PhysicsBody refBody = refLimb == null ? Collider : refLimb.body;

            return GetFloorY(refBody.SimPosition);            
        }

        protected float GetFloorY(Vector2 simPosition)
        {
            Vector2 rayStart = simPosition;
            float height = ColliderHeightFromFloor;
            if (HeadPosition.HasValue && MathUtils.IsValid(HeadPosition.Value)) height = Math.Max(height, HeadPosition.Value);
            if (TorsoPosition.HasValue && MathUtils.IsValid(TorsoPosition.Value)) height = Math.Max(height, TorsoPosition.Value);

            Vector2 rayEnd = rayStart - new Vector2(0.0f, height);

            var lowestLimb = FindLowestLimb();

            float closestFraction = 1;
            GameMain.World.RayCast((fixture, point, normal, fraction) =>
            {
                switch (fixture.CollisionCategories)
                {
                    case Physics.CollisionStairs:
                        if (inWater && TargetMovement.Y < 0.5f) return -1;
                        break;
                    case Physics.CollisionPlatform:
                        Structure platform = fixture.Body.UserData as Structure;
                        if (IgnorePlatforms || lowestLimb.Position.Y < platform.Rect.Y) return -1;
                        break;
                    case Physics.CollisionWall:
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

        public void SetPosition(Vector2 simPosition, bool lerp = false)
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

            Vector2 limbMoveAmount = simPosition - MainLimb.SimPosition;

            Collider.SetTransform(simPosition, Collider.Rotation);

            foreach (Limb limb in Limbs)
            {
                if (limb.IsSevered) continue;
                //check visibility from the new position of the collider to the new position of this limb
                Vector2 movePos = limb.SimPosition + limbMoveAmount;

                TrySetLimbPosition(limb, simPosition, movePos, lerp);
            }
        }

        protected void TrySetLimbPosition(Limb limb, Vector2 original, Vector2 simPosition, bool lerp = false)
        {
            Vector2 movePos = simPosition;

            if (Vector2.DistanceSquared(original, simPosition) > 0.0001f)
            {
                Category collisionCategory = Physics.CollisionWall | Physics.CollisionLevel;
                //if (!ignorePlatforms) collisionCategory |= Physics.CollisionPlatform;

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

            float distSqrd = Vector2.DistanceSquared(Collider.SimPosition, MainLimb.SimPosition);

            if (distSqrd > resetDist * resetDist)
            {
                //ragdoll way too far, reset position
                SetPosition(Collider.SimPosition, true);
            }
            if (distSqrd > allowedDist * allowedDist)
            {
                //ragdoll too far from the collider, disable collisions until it's close enough
                //(in case the ragdoll has gotten stuck somewhere)
                foreach (Limb limb in Limbs)
                {
                    if (limb.IsSevered) continue;
                    limb.body.CollidesWith = Physics.CollisionNone;
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
        
        private void UpdateNetPlayerPosition(float deltaTime)
        {
            if (GameMain.NetworkMember == null) return;

            float lowestSubPos = ConvertUnits.ToSimUnits(Submarine.Loaded.Min(s => s.HiddenSubPosition.Y - s.Borders.Height - 128.0f));

            for (int i = 0; i < character.MemState.Count; i++ )
            {
                if (character.Submarine == null)
                {
                    //transform in-sub coordinates to outside coordinates
                    if (character.MemState[i].Position.Y > lowestSubPos)
                        character.MemState[i].TransformInToOutside();
                }
                else if (currentHull != null)
                {
                    //transform outside coordinates to in-sub coordinates
                    if (character.MemState[i].Position.Y < lowestSubPos)
                        character.MemState[i].TransformOutToInside(currentHull.Submarine);
                }
            }

            if (GameMain.Server != null) return; //the server should not be trying to correct any positions, it's authoritative
            
            if (character != GameMain.NetworkMember.Character || !character.AllowInput)
            {
                //remove states without a timestamp (there may still be ID-based states 
                //in the list when the controlled character switches to timestamp-based interpolation)
                character.MemState.RemoveAll(m => m.Timestamp == 0.0f);

                //use simple interpolation for other players' characters and characters that can't move
                if (character.MemState.Count > 0)
                {
                    CharacterStateInfo serverPos = character.MemState.Last();
                    if (!character.isSynced)
                    {
                        SetPosition(serverPos.Position, false);
                        Collider.LinearVelocity = Vector2.Zero;
                        character.MemLocalState.Clear();
                        character.LastNetworkUpdateID = serverPos.ID;
                        character.isSynced = true;
                        return;
                    }

                    if (character.MemState[0].Interact == null || character.MemState[0].Interact.Removed)
                    {
                        character.DeselectCharacter();
                        character.SelectedConstruction = null;
                    }
                    else if (character.MemState[0].Interact is Character)
                    {
                        character.SelectCharacter((Character)character.MemState[0].Interact);
                    }
                    else if (character.MemState[0].Interact is Item)
                    {
                        var newSelectedConstruction = (Item)character.MemState[0].Interact;
                        if (newSelectedConstruction != null && character.SelectedConstruction != newSelectedConstruction)
                        {
                            foreach (var ic in newSelectedConstruction.components)
                            {
                                if (ic.CanBeSelected) ic.Select(character);
                            }
                        }
                        character.SelectedConstruction = newSelectedConstruction;
                    }

                    if (character.MemState[0].Animation == AnimController.Animation.CPR)
                    {
                        character.AnimController.Anim = AnimController.Animation.CPR;
                    }
                    else if (character.AnimController.Anim == AnimController.Animation.CPR)
                    {
                        character.AnimController.Anim = AnimController.Animation.None;
                    }

                    Vector2 newVelocity = Vector2.Zero;
                    Vector2 newPosition = Collider.SimPosition;
                    Collider.CorrectPosition(character.MemState, deltaTime, out newVelocity, out newPosition);

                    newVelocity = newVelocity.ClampLength(100.0f);
                    if (!MathUtils.IsValid(newVelocity)) newVelocity = Vector2.Zero;
                    overrideTargetMovement = newVelocity;
                    Collider.LinearVelocity = newVelocity;

                    float distSqrd = Vector2.DistanceSquared(newPosition, Collider.SimPosition);
                    if (distSqrd > 10.0f)
                    {
                        SetPosition(newPosition);
                    }
                    else if (distSqrd > 0.01f)
                    {
                        Collider.SetTransform(newPosition, Collider.Rotation);
                    }

                    //unconscious/dead characters can't correct their position using AnimController movement
                    // -> we need to correct it manually
                    if (!character.AllowInput)
                    {
                        Collider.LinearVelocity = overrideTargetMovement;
                        MainLimb.PullJointWorldAnchorB = Collider.SimPosition;
                        MainLimb.PullJointEnabled = true;
                    }
                }
                character.MemLocalState.Clear();
            }
            else
            {
                //remove states with a timestamp (there may still timestamp-based states 
                //in the list if the controlled character switches from timestamp-based interpolation to ID-based)
                character.MemState.RemoveAll(m => m.Timestamp > 0.0f);
                
                for (int i = 0; i < character.MemLocalState.Count; i++)
                {
                    if (character.Submarine == null)
                    {
                        //transform in-sub coordinates to outside coordinates
                        if (character.MemLocalState[i].Position.Y > lowestSubPos)
                        {                            
                            character.MemLocalState[i].TransformInToOutside();
                        }
                    }
                    else if (currentHull != null)
                    {
                        //transform outside coordinates to in-sub coordinates
                        if (character.MemLocalState[i].Position.Y < lowestSubPos)
                        {
                            character.MemLocalState[i].TransformOutToInside(currentHull.Submarine);
                        }
                    }
                }

                if (character.MemState.Count < 1) return;

                overrideTargetMovement = Vector2.Zero;

                CharacterStateInfo serverPos = character.MemState.Last();

                if (!character.isSynced)
                {
                    SetPosition(serverPos.Position, false);
                    Collider.LinearVelocity = Vector2.Zero;
                    character.MemLocalState.Clear();
                    character.LastNetworkUpdateID = serverPos.ID;
                    character.isSynced = true;
                    return;
                }

                int localPosIndex = character.MemLocalState.FindIndex(m => m.ID == serverPos.ID);
                if (localPosIndex > -1)
                {
                    CharacterStateInfo localPos = character.MemLocalState[localPosIndex];
                    
                    //the entity we're interacting with doesn't match the server's
                    if (localPos.Interact != serverPos.Interact)
                    {
                        if (serverPos.Interact == null || serverPos.Interact.Removed)
                        {
                            character.DeselectCharacter();
                            character.SelectedConstruction = null;
                        }
                        else if (serverPos.Interact is Character)
                        {
                            character.SelectCharacter((Character)serverPos.Interact);
                        }
                        else
                        {
                            var newSelectedConstruction = (Item)serverPos.Interact;
                            if (newSelectedConstruction != null && character.SelectedConstruction != newSelectedConstruction)
                            {
                                newSelectedConstruction.TryInteract(character, true, true);
                            }
                            character.SelectedConstruction = newSelectedConstruction;
                        }
                    }

                    if (localPos.Animation != serverPos.Animation)
                    {
                        if (serverPos.Animation == AnimController.Animation.CPR)
                        {
                            character.AnimController.Anim = AnimController.Animation.CPR;
                        }
                        else if (character.AnimController.Anim == AnimController.Animation.CPR) 
                        {
                            character.AnimController.Anim = AnimController.Animation.None;
                        }
                    }

                    Hull serverHull = Hull.FindHull(ConvertUnits.ToDisplayUnits(serverPos.Position), character.CurrentHull, serverPos.Position.Y < lowestSubPos);
                    Hull clientHull = Hull.FindHull(ConvertUnits.ToDisplayUnits(localPos.Position), serverHull, localPos.Position.Y < lowestSubPos);
                    
                    if (serverHull != null && clientHull != null && serverHull.Submarine != clientHull.Submarine)
                    {
                        //hull subs don't match => teleport the camera to the other sub
                        character.Submarine = serverHull.Submarine;
                        character.CurrentHull = currentHull = serverHull;
                        SetPosition(serverPos.Position);
                        character.MemLocalState.Clear();
                    }
                    else
                    {
                        Vector2 positionError = serverPos.Position - localPos.Position;
                        float rotationError = serverPos.Rotation - localPos.Rotation;

                        for (int i = localPosIndex; i < character.MemLocalState.Count; i++)
                        {
                            Hull pointHull = Hull.FindHull(ConvertUnits.ToDisplayUnits(character.MemLocalState[i].Position), clientHull, character.MemLocalState[i].Position.Y < lowestSubPos);
                            if (pointHull != clientHull && ((pointHull == null) || (clientHull == null) || (pointHull.Submarine == clientHull.Submarine))) break;
                            character.MemLocalState[i].Translate(positionError, rotationError);
                        }

                        float errorMagnitude = positionError.Length();
                        if (errorMagnitude > 0.01f)
                        {
                            Collider.SetTransform(Collider.SimPosition + positionError, Collider.Rotation + rotationError);
                            if (errorMagnitude > 0.5f)
                            {
                                character.MemLocalState.Clear();                 
                                foreach (Limb limb in Limbs)
                                {
                                    limb.body.SetTransform(limb.body.SimPosition + positionError, limb.body.Rotation);
                                }
                            }
                        }
                    }

                }

                if (character.MemLocalState.Count > 120) character.MemLocalState.RemoveRange(0, character.MemLocalState.Count - 120);
                character.MemState.Clear();
            }
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
            Limb mouthLimb = Array.Find(Limbs, l => l != null && l.MouthPos.HasValue);
            if (mouthLimb == null) mouthLimb = GetLimb(LimbType.Head);
            if (mouthLimb == null) return null;

            Vector2 mouthPos = mouthLimb.SimPosition;
            if (mouthLimb.MouthPos.HasValue)
            {
                float cos = (float)Math.Cos(mouthLimb.Rotation);
                float sin = (float)Math.Sin(mouthLimb.Rotation);
                mouthPos += new Vector2(
                     mouthLimb.MouthPos.Value.X * cos - mouthLimb.MouthPos.Value.Y * sin,
                     mouthLimb.MouthPos.Value.X * sin + mouthLimb.MouthPos.Value.Y * cos);
            }
            return mouthPos;
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
                    FarseerPhysics.Collision.AABB aabb;
                    FarseerPhysics.Common.Transform transform;

                    Collider.FarseerBody.GetTransform(out transform);
                    Collider.FarseerBody.FixtureList[i].Shape.ComputeAABB(out aabb, ref transform, i);

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

            foreach (PhysicsBody b in collider)
            {
                b.Remove();
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
