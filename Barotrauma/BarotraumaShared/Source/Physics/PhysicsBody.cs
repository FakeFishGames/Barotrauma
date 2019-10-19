using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
using LimbParams = Barotrauma.RagdollParams.LimbParams;
using ColliderParams = Barotrauma.RagdollParams.ColliderParams;

namespace Barotrauma
{
    class PosInfo
    {
        public Vector2 Position
        {
            get;
            private set;
        }

        public float? Rotation
        {
            get;
            private set;
        }

        public Vector2 LinearVelocity
        {
            get;
            private set;
        }

        public float? AngularVelocity
        {
            get;
            private set;
        }

        public readonly float Timestamp;
        public readonly UInt16 ID;

        public PosInfo(Vector2 pos, float? rotation, Vector2 linearVelocity, float? angularVelocity, float time)
            : this(pos, rotation, linearVelocity, angularVelocity, 0, time)
        {
        }

        public PosInfo(Vector2 pos, float? rotation, Vector2 linearVelocity, float? angularVelocity, UInt16 ID)
            : this(pos, rotation, linearVelocity, angularVelocity, ID, 0.0f)
        {
        }

        protected PosInfo(Vector2 pos, float? rotation, Vector2 linearVelocity, float? angularVelocity, UInt16 ID, float time)
        {
            Position = pos;
            Rotation = rotation;
            LinearVelocity = linearVelocity;
            AngularVelocity = angularVelocity;

            this.ID = ID;            
            Timestamp = time;
        }

        public void TransformOutToInside(Submarine submarine)
        {
            //transform outside coordinates to in-sub coordinates
            Position -= ConvertUnits.ToSimUnits(submarine.Position);         
        }

        public void TransformInToOutside()
        {
            var sub = Submarine.FindContaining(ConvertUnits.ToDisplayUnits(Position));
            if (sub != null)
            {
                Position += ConvertUnits.ToSimUnits(sub.Position);
            }         
        }

        public void Translate(Vector2 posAmount,float rotationAmount)
        {
            Position += posAmount; Rotation += rotationAmount;
        }
    }

    partial class PhysicsBody
    {
        public enum Shape
        {
            Circle, Rectangle, Capsule, HorizontalCapsule
        };

        private static List<PhysicsBody> list = new List<PhysicsBody>();
        public static List<PhysicsBody> List
        {
            get { return list; }
        }

        //the farseer physics body of the item
        private Body body;
        protected Vector2 prevPosition;
        protected float prevRotation;

        protected Vector2? targetPosition;
        protected float? targetRotation;

        private Vector2 drawPosition;
        private float drawRotation;
        
        public Vector2 LastSentPosition
        {
            get;
            private set;
        }

        private Shape bodyShape;
        public float height, width, radius;
        
        private float density;

        //the direction the item is facing (for example, a gun has to be 
        //flipped horizontally if the Character holding it turns around)
        float dir = 1.0f;

        private Vector2 drawOffset;
        private float rotationOffset;

        private float lastProcessedNetworkState;

        public float? PositionSmoothingFactor;

        public Shape BodyShape
        {
            get { return bodyShape; }
        }

        public Vector2? TargetPosition
        {
            get { return targetPosition; }
            set
            {
                if (value == null)
                {
                    targetPosition = null;
                }
                else
                {
                    if (!IsValidValue(value.Value, "target position", -1e5f, 1e5f)) return;
                    targetPosition = new Vector2(
                        MathHelper.Clamp(((Vector2)value).X, -10000.0f, 10000.0f),
                        MathHelper.Clamp(((Vector2)value).Y, -10000.0f, 10000.0f));
                }
            }
        }
        
        public float? TargetRotation
        {
            get { return targetRotation; }
            set 
            {
                if (value == null)
                {
                    targetRotation = null;
                }
                else
                {
                    if (!IsValidValue(value.Value, "target rotation")) return;
                    targetRotation = value;
                }
 
            }
        }

        public Vector2 DrawPosition
        {
            get { return Submarine == null ? drawPosition : drawPosition + Submarine.DrawPosition; }
        }

        public float DrawRotation
        {
            get { return drawRotation; }
        }

        public Submarine Submarine;

        public float Dir
        {
            get { return dir; }
            set { dir = value; }
        }

        private bool isEnabled = true;
        private bool isPhysEnabled = true;

        public bool Enabled
        {
            get { return isEnabled; }
            set
            {
                isEnabled = value;
                try
                {
                    if (isEnabled) body.Enabled = isPhysEnabled; else body.Enabled = false;
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError("Exception in PhysicsBody.Enabled = " + value + " (" + isPhysEnabled + ")", e);
                    if (UserData != null) DebugConsole.NewMessage("PhysicsBody UserData: " + UserData.GetType().ToString(), Color.Red);
                    if (GameMain.World.ContactManager == null) DebugConsole.NewMessage("ContactManager is null!", Color.Red);
                    else if (GameMain.World.ContactManager.BroadPhase == null) DebugConsole.NewMessage("Broadphase is null!", Color.Red);
                    if (body.FixtureList == null) DebugConsole.NewMessage("FixtureList is null!", Color.Red);

                    if (UserData is Entity entity)
                    {
                        DebugConsole.NewMessage("Entity \"" + entity.ToString() + "\" removed!", Color.Red);
                    }
                }
            }
        }

        public bool PhysEnabled
        {
            get { return body.Enabled; }
            set { isPhysEnabled = value; if (Enabled) body.Enabled = value; }
        }

        public Vector2 SimPosition
        {
            get { return body.Position; }
        }

        public Vector2 Position
        {
            get { return ConvertUnits.ToDisplayUnits(body.Position); }
        }

        public Vector2 PrevPosition
        {
            get { return prevPosition; }
        }

        public float Rotation
        {
            get { return body.Rotation; }
        }

        /// <summary>
        /// Takes flipping (Dir) into account.
        /// </summary>
        public float TransformedRotation => Dir < 0 ? Rotation - MathHelper.Pi : Rotation;

        public Vector2 LinearVelocity
        {
            get { return body.LinearVelocity; }
            set
            {
                if (!IsValidValue(value, "velocity", -1000.0f, 1000.0f)) return;
                body.LinearVelocity = value;
            }
        }

        public float AngularVelocity
        {
            get { return body.AngularVelocity; }
            set
            {
                if (!IsValidValue(value, "angular velocity", -1000f, 1000f)) return;
                body.AngularVelocity = value;
            }
        }

        public float Mass
        {
            get { return body.Mass; }
        }

        public float Density
        {
            get { return density; }
        }

        public Body FarseerBody
        {
            get { return body; }
        }

        public object UserData
        {
            get { return body.UserData; }
            set { body.UserData = value; }
        }

        public float Friction
        {
            set { body.Friction = value; }
        }

        public BodyType BodyType
        {
            get { return body.BodyType; }
            set { body.BodyType = value; }
        }

        public Category CollisionCategories
        {
            set { body.CollisionCategories = value; }
        }

        public Category CollidesWith
        {
            set { body.CollidesWith = value; }
        }

        public PhysicsBody(XElement element, float scale = 1.0f) : this(element, Vector2.Zero, scale) { }
        public PhysicsBody(ColliderParams cParams) : this(cParams, Vector2.Zero) { }
        public PhysicsBody(LimbParams lParams) : this(lParams, Vector2.Zero) { }

        public PhysicsBody(float width, float height, float radius, float density)
        {
            CreateBody(width, height, radius, density);
            LastSentPosition = body.Position;
            list.Add(this);
        }

        public PhysicsBody(Body farseerBody)
        {
            body = farseerBody;
            if (body.UserData == null) body.UserData = this;
            LastSentPosition = body.Position;
            list.Add(this);
        }

        public PhysicsBody(ColliderParams colliderParams, Vector2 position)
        {
            float radius = ConvertUnits.ToSimUnits(colliderParams.Radius) * colliderParams.Ragdoll.LimbScale;
            float height = ConvertUnits.ToSimUnits(colliderParams.Height) * colliderParams.Ragdoll.LimbScale;
            float width = ConvertUnits.ToSimUnits(colliderParams.Width) * colliderParams.Ragdoll.LimbScale;
            density = 10;
            CreateBody(width, height, radius, density);
            body.BodyType = BodyType.Dynamic;
            body.CollidesWith = Physics.CollisionWall | Physics.CollisionLevel;
            body.CollisionCategories = Physics.CollisionCharacter;
            body.AngularDamping = 5.0f;
            body.FixedRotation = true;
            body.Friction = 0.05f;
            body.Restitution = 0.05f;
            SetTransformIgnoreContacts(position, 0.0f);
            LastSentPosition = position;
            list.Add(this);
        }

        public PhysicsBody(LimbParams limbParams, Vector2 position)
        {
            float radius = ConvertUnits.ToSimUnits(limbParams.Radius) * limbParams.Ragdoll.LimbScale;
            float height = ConvertUnits.ToSimUnits(limbParams.Height) * limbParams.Ragdoll.LimbScale;
            float width = ConvertUnits.ToSimUnits(limbParams.Width) * limbParams.Ragdoll.LimbScale;
            density = limbParams.Density;
            CreateBody(width, height, radius, density);
            body.BodyType = BodyType.Dynamic;
            body.CollidesWith = Physics.CollisionWall | Physics.CollisionLevel;
            body.CollisionCategories = Physics.CollisionItem;
            body.Friction = limbParams.Friction;
            body.Restitution = limbParams.Restitution;
            body.AngularDamping = limbParams.AngularDamping;
            body.UserData = this;
            SetTransformIgnoreContacts(position, 0.0f);
            LastSentPosition = position;
            list.Add(this);
        }
        
        public PhysicsBody(XElement element, Vector2 position, float scale=1.0f)
        {
            float radius = ConvertUnits.ToSimUnits(element.GetAttributeFloat("radius", 0.0f)) * scale;
            float height = ConvertUnits.ToSimUnits(element.GetAttributeFloat("height", 0.0f)) * scale;
            float width = ConvertUnits.ToSimUnits(element.GetAttributeFloat("width", 0.0f)) * scale;
            density = element.GetAttributeFloat("density", 10.0f);
            CreateBody(width, height, radius, density);
            //Enum.TryParse(element.GetAttributeString("bodytype", "Dynamic"), out BodyType bodyType);
            body.BodyType = BodyType.Dynamic;
            body.CollisionCategories = Physics.CollisionItem;
            body.CollidesWith = Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionPlatform;
            body.Friction = element.GetAttributeFloat("friction", 0.3f);
            body.Restitution = element.GetAttributeFloat("restitution", 0.05f);                    
            body.UserData = this;
            SetTransformIgnoreContacts(position, 0.0f);
            LastSentPosition = position;      
            list.Add(this);
        }

        private void CreateBody(float width, float height, float radius, float density)
        {
            if (IsValidShape(radius, height, width))
            {
                bodyShape = DefineBodyShape(radius, width, height);
                switch (bodyShape)
                {
                    case Shape.Capsule:
                        body = BodyFactory.CreateCapsule(GameMain.World, height, radius, density);
                        break;
                    case Shape.HorizontalCapsule:
                        body = BodyFactory.CreateCapsuleHorizontal(GameMain.World, width, radius, density);
                        break;
                    case Shape.Circle:
                        body = BodyFactory.CreateCircle(GameMain.World, radius, density);
                        break;
                    case Shape.Rectangle:
                        body = BodyFactory.CreateRectangle(GameMain.World, width, height, density);
                        break;
                    default:
                        throw new NotImplementedException(bodyShape.ToString());
                }
            }
            else
            {
                DebugConsole.ThrowError("Invalid physics body dimensions (width: " + width + ", height: " + height + ", radius: " + radius + ")");
            }
            this.width = width;
            this.height = height;
            this.radius = radius;
        }

        /// <summary>
        /// Returns the farthest point towards the forward of the body.
        /// For capsules and circles, the front is at the top.
        /// For horizontal capsules, the front is at the right-most point.
        /// For rectangles, the front is either at the top or at the right, depending on which one of the two is greater: width or height.
        /// The rotation is in radians.
        /// </summary>
        public Vector2 GetLocalFront(float spritesheetRotation = 0)
        {
            Vector2 pos;
            switch (bodyShape)
            {
                case Shape.Capsule:
                    pos = new Vector2(0.0f, height / 2 + radius);
                    break;
                case Shape.HorizontalCapsule:
                    pos = new Vector2(width / 2 + radius, 0.0f);
                    break;
                case Shape.Circle:
                    pos = new Vector2(0.0f, radius);
                    break;
                case Shape.Rectangle:
                    pos = new Vector2(0.0f, Math.Max(height, width) / 2.0f);
                    break;
                default:
                    throw new NotImplementedException();
            }
            return spritesheetRotation == 0 ? pos : Vector2.Transform(pos, Matrix.CreateRotationZ(-spritesheetRotation));
        }

        public float GetMaxExtent()
        {
            switch (bodyShape)
            {
                case Shape.Capsule:
                    return height / 2 + radius;
                case Shape.HorizontalCapsule:
                    return width / 2 + radius;
                case Shape.Circle:
                    return radius;
                case Shape.Rectangle:
                    return new Vector2(width * 0.5f, height * 0.5f).Length();
                default:
                    throw new NotImplementedException();
            }
        }

        public Vector2 GetSize()
        {
            switch (bodyShape)
            {
                case Shape.Capsule:
                    return new Vector2(radius * 2, height + radius * 2);
                case Shape.HorizontalCapsule:
                    return new Vector2(width + radius * 2, radius * 2);
                case Shape.Circle:
                    return new Vector2(radius * 2);
                case Shape.Rectangle:
                    return new Vector2(width, height);
                default:
                    throw new NotImplementedException();
            }
        }

        public void SetSize(Vector2 size)
        {
            switch (bodyShape)
            {
                case Shape.Capsule:
                    radius = Math.Max(size.X / 2, 0);
                    height = Math.Max(size.Y - size.X, 0);
                    width = 0;
                    break;
                case Shape.HorizontalCapsule:
                    radius = Math.Max(size.Y / 2, 0);
                    width = Math.Max(size.X - size.Y, 0);
                    height = 0;
                    break;
                case Shape.Circle:
                    radius = Math.Max(Math.Min(size.X, size.Y) / 2, 0);
                    width = 0;
                    height = 0;
                    break;
                case Shape.Rectangle:
                    width = Math.Max(size.X, 0);
                    height = Math.Max(size.Y, 0);
                    radius = 0;
                    break;
                default:
                    throw new NotImplementedException();
            }
#if CLIENT
            bodyShapeTexture = null;
#endif
        }
        
        public bool IsValidValue(float value, string valueName, float minValue = float.MinValue, float maxValue = float.MaxValue)
        {
            if (!MathUtils.IsValid(value) || value < minValue || value > maxValue)
            {
                string userData = UserData == null ? "null" : UserData.ToString();
                string errorMsg =
                    "Attempted to apply invalid " + valueName +
                    " to a physics body (userdata: " + userData +
                    "), value: " + value;
                if (GameMain.NetworkMember != null)
                {
                    errorMsg += GameMain.NetworkMember.IsClient ? " Playing as a client." : " Hosting a server.";
                }
                errorMsg += "\n" + Environment.StackTrace;

                if (GameSettings.VerboseLogging) DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce(
                    "PhysicsBody.SetPosition:InvalidPosition" + userData,
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    errorMsg);
                return false;
            }
            return true;
        }

        private bool IsValidValue(Vector2 value, string valueName, float minValue = float.MinValue, float maxValue = float.MaxValue)
        {
            if (!MathUtils.IsValid(value) ||
                (value.X < minValue || value.Y < minValue) ||
                (value.X > maxValue || value.Y > maxValue))
            {
                string userData = UserData == null ? "null" : UserData.ToString();
                string errorMsg =
                    "Attempted to apply invalid " + valueName +
                    " to a physics body (userdata: " + userData +
                    "), value: " + value;
                if (GameMain.NetworkMember != null)
                {
                    errorMsg += GameMain.NetworkMember.IsClient ? " Playing as a client." : " Hosting a server.";
                }
                errorMsg += "\n" + Environment.StackTrace;

                if (GameSettings.VerboseLogging) DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce(
                    "PhysicsBody.SetPosition:InvalidPosition" + userData,
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    errorMsg);
                return false;
            }
            return true;
        }

        public void ResetDynamics()
        {
            body.ResetDynamics();
        }

        public void ApplyLinearImpulse(Vector2 impulse)
        {
            if (!IsValidValue(impulse / body.Mass, "new velocity", -1000f, 1000f)) return;
            if (!IsValidValue(impulse, "impulse", -1e10f, 1e10f)) return;

            body.ApplyLinearImpulse(impulse);
        }

        /// <summary>
        /// Apply an impulse to the body without increasing it's velocity above a specific limit.
        /// </summary>
        public void ApplyLinearImpulse(Vector2 impulse, float maxVelocity)
        {
            if (!IsValidValue(impulse, "impulse", -1e10f, 1e10f)) return;
            if (!IsValidValue(maxVelocity, "max velocity")) return;
            
            Vector2 velocityAddition = impulse / Mass;
            Vector2 newVelocity = body.LinearVelocity + velocityAddition;
            float newSpeedSqr = newVelocity.LengthSquared();
            if (newSpeedSqr > maxVelocity * maxVelocity)
            {
                newVelocity = newVelocity.ClampLength(maxVelocity);
            }

            if (!IsValidValue((newVelocity - body.LinearVelocity), "new velocity", -1000.0f, 1000.0f)) return;

            body.ApplyLinearImpulse((newVelocity - body.LinearVelocity) * Mass);
        }

        public void ApplyLinearImpulse(Vector2 impulse, Vector2 point)
        {
            if (!IsValidValue(impulse, "impulse", -1e10f, 1e10f)) return;
            if (!IsValidValue(point, "point")) return;
            if (!IsValidValue(impulse / body.Mass, "new velocity", -1000.0f, 1000.0f)) return;
            body.ApplyLinearImpulse(impulse, point);
        }

        /// <summary>
        /// Apply an impulse to the body without increasing it's velocity above a specific limit.
        /// </summary>
        public void ApplyLinearImpulse(Vector2 impulse, Vector2 point, float maxVelocity)
        {
            if (!IsValidValue(impulse, "impulse", -1e10f, 1e10f)) return;
            if (!IsValidValue(point, "point")) return;
            if (!IsValidValue(maxVelocity, "max velocity")) return;

            Vector2 velocityAddition = impulse / Mass;
            Vector2 newVelocity = body.LinearVelocity + velocityAddition;
            float newSpeedSqr = newVelocity.LengthSquared();
            if (newSpeedSqr > maxVelocity * maxVelocity)
            {
                newVelocity = newVelocity.ClampLength(maxVelocity);
            }

            if (!IsValidValue((newVelocity - body.LinearVelocity), "new velocity", -1000.0f, 1000.0f)) return;

            body.ApplyLinearImpulse((newVelocity - body.LinearVelocity) * Mass, point);
            body.AngularVelocity = MathHelper.Clamp(
                body.AngularVelocity, 
                -NetConfig.MaxPhysicsBodyAngularVelocity, 
                NetConfig.MaxPhysicsBodyAngularVelocity);
        }

        public void ApplyForce(Vector2 force)
        {
            if (!IsValidValue(force, "force", -1e10f, 1e10f)) return;
            body.ApplyForce(force);
        }

        /// <summary>
        /// Apply an impulse to the body without increasing it's velocity above a specific limit.
        /// </summary>
        public void ApplyForce(Vector2 force, float maxVelocity)
        {
            if (!IsValidValue(maxVelocity, "max velocity")) return;

            Vector2 velocityAddition = force / Mass * (float)Timing.Step;
            Vector2 newVelocity = body.LinearVelocity + velocityAddition;
            
            float newSpeedSqr = newVelocity.LengthSquared();
            if (newSpeedSqr > maxVelocity * maxVelocity)
            {
                newVelocity = newVelocity.ClampLength(maxVelocity);
            }

            Vector2 clampedForce = (newVelocity - body.LinearVelocity) * Mass / (float)Timing.Step;
            if (!IsValidValue(force, "clamped force", -1e10f, 1e10f)) return;
            body.ApplyForce(force);
        }

        public void ApplyForce(Vector2 force, Vector2 point)
        {
            if (!IsValidValue(force, "force", -1e10f, 1e10f)) return;
            if (!IsValidValue(point, "point")) return;
            body.ApplyForce(force, point);
        }

        public void ApplyTorque(float torque)
        {
            if (!IsValidValue(torque, "torque")) return;
            body.ApplyTorque(torque);
        }

        public bool SetTransform(Vector2 simPosition, float rotation)
        {
            System.Diagnostics.Debug.Assert(MathUtils.IsValid(simPosition));
            System.Diagnostics.Debug.Assert(Math.Abs(simPosition.X) < 1000000.0f);
            System.Diagnostics.Debug.Assert(Math.Abs(simPosition.Y) < 1000000.0f);

            if (!IsValidValue(simPosition, "position", -1e10f, 1e10f)) return false;
            if (!IsValidValue(rotation, "rotation")) return false;

            body.SetTransform(simPosition, rotation);
            SetPrevTransform(simPosition, rotation);
            return true;
        }

        public bool SetTransformIgnoreContacts(Vector2 simPosition, float rotation)
        {
            System.Diagnostics.Debug.Assert(MathUtils.IsValid(simPosition));
            System.Diagnostics.Debug.Assert(Math.Abs(simPosition.X) < 1000000.0f);
            System.Diagnostics.Debug.Assert(Math.Abs(simPosition.Y) < 1000000.0f);

            if (!IsValidValue(simPosition, "position", -1e10f, 1e10f)) return false;
            if (!IsValidValue(rotation, "rotation")) return false;

            body.SetTransformIgnoreContacts(ref simPosition, rotation);
            SetPrevTransform(simPosition, rotation);
            return true;
        }

        public void SetPrevTransform(Vector2 simPosition, float rotation)
        {
            if (!IsValidValue(simPosition, "position", -1e10f, 1e10f)) return;
            if (!IsValidValue(rotation, "rotation")) return;

            prevPosition = simPosition;
            prevRotation = rotation;
        }

        public void MoveToTargetPosition(bool lerp = true)
        {
            if (targetPosition == null) { return; }

            if (lerp)
            {
                if (Vector2.DistanceSquared((Vector2)targetPosition, body.Position) < 10.0f * 10.0f)
                {
                    drawOffset = -((Vector2)targetPosition - (body.Position + drawOffset));
                    prevPosition = (Vector2)targetPosition;
                }
                else
                {
                    drawOffset = Vector2.Zero;
                }
                if (targetRotation.HasValue)
                {
                    rotationOffset = -MathUtils.GetShortestAngle(body.Rotation + rotationOffset, targetRotation.Value);
                }
            }

            SetTransformIgnoreContacts((Vector2)targetPosition, targetRotation == null ? body.Rotation : (float)targetRotation);
            targetPosition = null;
            targetRotation = null;
        }

        public void MoveToPos(Vector2 simPosition, float force, Vector2? pullPos = null)
        {
            if (pullPos == null) pullPos = body.Position;

            if (!IsValidValue(simPosition, "position", -1e10f, 1e10f)) return;
            if (!IsValidValue(force, "force")) return;

            Vector2 vel = body.LinearVelocity;
            Vector2 deltaPos = simPosition - (Vector2)pullPos;
            deltaPos *= force;
            body.ApplyLinearImpulse((deltaPos - vel * 0.5f) * body.Mass, (Vector2)pullPos);
        }

        /// <summary>
        /// Applies buoyancy, drag and angular drag caused by water
        /// </summary>
        public void ApplyWaterForces()
        {
            //buoyancy
            Vector2 buoyancy = new Vector2(0, Mass * 9.6f);

            Vector2 dragForce = Vector2.Zero;

            float speedSqr = LinearVelocity.LengthSquared();
            if (speedSqr > 0.00001f)
            {
                //drag
                float speed = (float)Math.Sqrt(speedSqr);
                Vector2 velDir = LinearVelocity / speed;

                float vel = speed * 2.0f;
                float drag = vel * vel * Math.Max(height + radius * 2, height);
                dragForce = Math.Min(drag, Mass * 500.0f) * -velDir;
            }

            ApplyForce(dragForce + buoyancy, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
            ApplyTorque(body.AngularVelocity * body.Mass * -0.08f);
        }

        public void Update(float deltaTime)
        {
            if (drawOffset.LengthSquared() < 0.01f)
            {
                PositionSmoothingFactor = null;
            }
            drawOffset = NetConfig.InterpolateSimPositionError(drawOffset, PositionSmoothingFactor);
            rotationOffset = NetConfig.InterpolateRotationError(rotationOffset);
        }

        public void UpdateDrawPosition()
        {
            drawPosition = Timing.Interpolate(prevPosition, body.Position);
            drawPosition = ConvertUnits.ToDisplayUnits(drawPosition + drawOffset);
            drawRotation = Timing.InterpolateRotation(prevRotation, body.Rotation) + rotationOffset;
        }
        
        public void CorrectPosition<T>(List<T> positionBuffer,
            out Vector2 newPosition, out Vector2 newVelocity, out float newRotation, out float newAngularVelocity) where T : PosInfo
        {
            newVelocity = LinearVelocity;
            newPosition = SimPosition;
            newRotation = Rotation;
            newAngularVelocity = AngularVelocity;

            while (positionBuffer.Count > 0 && positionBuffer[0].Timestamp < lastProcessedNetworkState)
            {
                positionBuffer.RemoveAt(0);
            }

            if (positionBuffer.Count == 0) { return; }

            lastProcessedNetworkState = positionBuffer[0].Timestamp;

            newVelocity = positionBuffer[0].LinearVelocity;
            newPosition = positionBuffer[0].Position;
            newRotation = positionBuffer[0].Rotation ?? Rotation;
            newAngularVelocity = positionBuffer[0].AngularVelocity ?? AngularVelocity;
            
            positionBuffer.RemoveAt(0);            
        }

        /// <summary>
        /// Rotate the body towards the target rotation in the "shortest direction", taking into account the current angular velocity to prevent overshooting.
        /// </summary>
        /// <param name="targetRotation">Desired rotation in radians</param>
        /// <param name="force">How fast the body should be rotated. Does not represent any real unit, you may want to experiment with different values to get the desired effect.</param>
        /// <param name="wrapAngle">Should the angles be wrapped. Set to false if it makes a difference whether the angle of the body is 0.0f or 360.0f.</param>
        public void SmoothRotate(float targetRotation, float force = 10.0f, bool wrapAngle = true)
        {
            float nextAngle = body.Rotation + body.AngularVelocity * (float)Timing.Step;
            float angle = wrapAngle ? 
                MathUtils.GetShortestAngle(nextAngle, targetRotation) : 
                MathHelper.Clamp(targetRotation - nextAngle, -MathHelper.Pi, MathHelper.Pi);
            float torque = angle * 60.0f * (force / 100.0f);

            if (body.IsKinematic)
            {
                if (!IsValidValue(torque, "torque")) return;
                body.AngularVelocity = torque;
            }
            else
            {
                ApplyTorque(body.Mass * torque);
            }
        }
        
        public void Remove()
        {
            list.Remove(this);
            GameMain.World.RemoveBody(body);

            DisposeProjSpecific();
        }

        public static void RemoveAll()
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                list[i].Remove();
            }
            System.Diagnostics.Debug.Assert(list.Count == 0);
        }

        public static bool IsValidShape(float radius, float height, float width) => radius > 0 || (height > 0 && width > 0);

        public static Shape DefineBodyShape(float radius, float width, float height)
        {
            Shape bodyShape;
            if (width <= 0 && height <= 0 && radius > 0)
            {
                bodyShape = Shape.Circle;
            }
            else if (radius > 0)
            {
                if (width > height)
                {
                    bodyShape = Shape.HorizontalCapsule;
                }
                else
                {
                    bodyShape = Shape.Capsule;
                }
            }
            else
            {
                bodyShape = Shape.Rectangle;
            }
            return bodyShape;
        }

        partial void DisposeProjSpecific();

    }
}
