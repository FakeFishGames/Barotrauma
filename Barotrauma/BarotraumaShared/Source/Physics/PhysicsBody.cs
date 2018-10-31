using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Factories;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    class PosInfo
    {
        public Vector2 Position
        {
            get;
            private set;
        }

        public float Rotation
        {
            get;
            private set;
        }

        public readonly float Timestamp;
        public readonly UInt16 ID;

        public PosInfo(Vector2 pos, float rotation, float time)
            : this(pos, rotation, 0, time)
        {
        }

        public PosInfo(Vector2 pos, float rotation, UInt16 ID)
            : this(pos, rotation, ID, 0.0f)
        {
        }
        
        protected PosInfo(Vector2 pos, float rotation, UInt16 ID, float time)
        {
            Position = pos;
            Rotation = rotation;
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

        Vector2 offsetFromTargetPos;
        float offsetLerp;

        private float netInterpolationState;

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
                if (!IsValidValue(value, "angular velocity")) return;
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
            SetTransform(position, 0.0f);
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
            body.UserData = this;
            SetTransform(position, 0.0f);
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
            body.CollidesWith = Physics.CollisionWall | Physics.CollisionLevel;
            body.Friction = element.GetAttributeFloat("friction", 0.3f);
            body.Restitution = element.GetAttributeFloat("restitution", 0.05f);                    
            body.UserData = this;
            SetTransform(position, 0.0f);
            LastSentPosition = position;      
            list.Add(this);
        }

        private void CreateBody(float width, float height, float radius, float density)
        {
            if (width != 0.0f && height != 0.0f)
            {
                body = BodyFactory.CreateRectangle(GameMain.World, width, height, density);
                bodyShape = Shape.Rectangle;
            }
            else if (radius != 0.0f && width != 0.0f)
            {
                body = BodyFactory.CreateCapsuleHorizontal(GameMain.World, width, radius, density);
                bodyShape = Shape.HorizontalCapsule;
            }
            else if (radius != 0.0f && height != 0.0f)
            {
                body = BodyFactory.CreateCapsule(GameMain.World, height, radius, density);
                bodyShape = Shape.Capsule;
            }
            else if (radius != 0.0f)
            {
                body = BodyFactory.CreateCircle(GameMain.World, radius, density);
                bodyShape = Shape.Circle;
            }
            else
            {
                DebugConsole.ThrowError("Invalid physics body dimensions (width: " + width + ", height: " + height + ", radius: " + radius + ")");
            }

            this.width = width;
            this.height = height;
            this.radius = radius;
        }
        
        public Vector2 GetFrontLocal()
        {
            switch (bodyShape)
            {
                case Shape.Capsule:
                    return new Vector2(0.0f, height / 2 + radius);
                case Shape.HorizontalCapsule:
                    return new Vector2(width / 2 + radius, 0.0f);
                case Shape.Circle:
                    return new Vector2(0.0f, radius);
                case Shape.Rectangle:
                    return new Vector2(0.0f, height / 2.0f);
                default:
                    throw new NotImplementedException();
            }
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
        
        public bool IsValidValue(float value, string valueName, float? minValue = null, float? maxValue = null)
        {
            if (!MathUtils.IsValid(value) ||
                (minValue.HasValue && value < minValue.Value) ||
                (maxValue.HasValue && value > maxValue.Value))
            {
                string userData = UserData == null ? "null" : UserData.ToString();
                string errorMsg =
                    "Attempted to apply invalid " + valueName +
                    " to a physics body (userdata: " + userData +
                    "), value: " + value + "\n" + Environment.StackTrace;

                if (GameSettings.VerboseLogging) DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce(
                    "PhysicsBody.SetPosition:InvalidPosition" + userData,
                    GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                    errorMsg);
                return false;
            }
            return true;
        }

        private bool IsValidValue(Vector2 value, string valueName, float? minValue = null, float? maxValue = null)
        {
            if (!MathUtils.IsValid(value) ||
                (minValue.HasValue && (value.X < minValue.Value || value.Y < minValue.Value)) ||
                (maxValue.HasValue && (value.X > maxValue.Value || value.Y > maxValue)))
            {
                string userData = UserData == null ? "null" : UserData.ToString();
                string errorMsg =
                    "Attempted to apply invalid " + valueName +
                    " to a physics body (userdata: " + userData +
                    "), value: " + value + "\n" + Environment.StackTrace;

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

            float currSpeed = body.LinearVelocity.Length();
            Vector2 velocityAddition = impulse / Mass;
            Vector2 newVelocity = body.LinearVelocity + velocityAddition;
            newVelocity = newVelocity.ClampLength(Math.Max(currSpeed, maxVelocity));

            body.ApplyLinearImpulse((newVelocity - body.LinearVelocity) * Mass);
        }

        public void ApplyLinearImpulse(Vector2 impulse, Vector2 point)
        {
            if (!IsValidValue(impulse, "impulse", -1e10f, 1e10f)) return;
            body.ApplyLinearImpulse(impulse, point);
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
            float currSpeed = body.LinearVelocity.Length();
            Vector2 velocityAddition = force / Mass * (float)Timing.Step;
            Vector2 newVelocity = body.LinearVelocity + velocityAddition;
            newVelocity = newVelocity.ClampLength(Math.Max(currSpeed, maxVelocity));

            body.ApplyForce((newVelocity - body.LinearVelocity) * Mass / (float)Timing.Step);
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
            if (targetPosition == null) return;

            if (lerp && Vector2.Distance((Vector2)targetPosition, body.Position) < 10.0f)
            {
                offsetFromTargetPos = (Vector2)targetPosition - (body.Position - Vector2.Lerp(offsetFromTargetPos, Vector2.Zero, offsetLerp));
                offsetLerp = 1.0f;
                prevPosition = (Vector2)targetPosition;
            }

            SetTransform((Vector2)targetPosition, targetRotation == null ? body.Rotation : (float)targetRotation);
            targetPosition = null;
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

            if (LinearVelocity.LengthSquared() > 0.00001f)
            {
                //drag
                Vector2 velDir = Vector2.Normalize(LinearVelocity);

                float vel = LinearVelocity.Length() * 2.0f;
                float drag = vel * vel * Math.Max(height + radius * 2, height);
                dragForce = Math.Min(drag, Mass * 500.0f) * -velDir;                
            }

            ApplyForce(dragForce + buoyancy);
            ApplyTorque(body.AngularVelocity * body.Mass * -0.08f);
        }


        public void UpdateDrawPosition()
        {
            drawPosition = Timing.Interpolate(prevPosition, body.Position);
            drawPosition = ConvertUnits.ToDisplayUnits(drawPosition);

            drawRotation = Timing.InterpolateRotation(prevRotation, body.Rotation);

            if (offsetFromTargetPos == Vector2.Zero)
            {
                return;
            }

            drawPosition -= ConvertUnits.ToDisplayUnits(Vector2.Lerp(Vector2.Zero, offsetFromTargetPos, offsetLerp));

            offsetLerp -= 0.1f;
            if (offsetLerp < 0.0f)
            {
                offsetFromTargetPos = Vector2.Zero;
            }
        }

        public void CorrectPosition<T>(List<T> positionBuffer, float deltaTime, out Vector2 newVelocity) where T : PosInfo
        {
            Vector2 newPosition = SimPosition;
            CorrectPosition(positionBuffer, deltaTime, out newVelocity, out newPosition);
            
            SetTransform(newPosition, Rotation);
        }


        public void CorrectPosition<T>(List<T> positionBuffer, float deltaTime, out Vector2 newVelocity, out Vector2 newPosition) where T : PosInfo
        {
            newVelocity = Vector2.Zero;
            newPosition = SimPosition;

            if (positionBuffer.Count < 2) return;
            
            PosInfo prev = positionBuffer[0];
            PosInfo next = positionBuffer[1];
            
            //interpolate the position of the collider from the first position in the buffer towards the second
            if (prev.Timestamp < next.Timestamp)
            {
                //if there are more than 2 positions in the buffer, 
                //increase the interpolation speed to catch up with the server
                float speedMultiplier = (float)Math.Pow(1.0f + (positionBuffer.Count - 2) / 10.0f, 2.0f);

                netInterpolationState += (deltaTime * speedMultiplier) / (next.Timestamp - prev.Timestamp);

                newPosition = Vector2.Lerp(prev.Position, next.Position, Math.Min(netInterpolationState, 1.0f));

                if (next.Timestamp == prev.Timestamp)
                {
                    newVelocity = Vector2.Zero;
                }
                else
                {
                    //override the targetMovement to make the character play the walking/running animation
                    newVelocity = (next.Position - prev.Position) / (next.Timestamp - prev.Timestamp);
                }
            }
            else
            {
                newPosition = next.Position;
                netInterpolationState = 1.0f;
            }

            if (netInterpolationState >= 1.0f)
            {
                netInterpolationState = 0.0f;
                positionBuffer.RemoveAt(0);
            }
        }
        
        /// <summary>
        /// rotate the body towards the target rotation in the "shortest direction"
        /// </summary>
        public void SmoothRotate(float targetRotation, float force = 10.0f)
        {
            float nextAngle = body.Rotation + body.AngularVelocity * (float)Timing.Step;
            float angle = MathUtils.GetShortestAngle(nextAngle, targetRotation);
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

        partial void DisposeProjSpecific();
    }
}
