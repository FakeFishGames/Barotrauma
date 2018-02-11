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

        public readonly float Timestamp;
        public readonly UInt16 ID;

        public PosInfo(Vector2 pos, float time)
            : this(pos, 0, time)
        {
        }

        public PosInfo(Vector2 pos, UInt16 ID)
            : this(pos, ID, 0.0f)
        {
        }
        
        protected PosInfo(Vector2 pos, UInt16 ID, float time)
        {
            Position = pos;
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

        public void Translate(Vector2 amount)
        {
            Position += amount;
        }
    }

    partial class PhysicsBody
    {
        public enum Shape
        {
            Circle, Rectangle, Capsule
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
        float dir;

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
                    if (!MathUtils.IsValid((Vector2)value)) return;

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
                    if (!MathUtils.IsValid((float)value)) return;
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
            set { body.LinearVelocity = value; }
        }

        public float AngularVelocity
        {
            get { return body.AngularVelocity; }
            set { body.AngularVelocity = value; }
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

        public PhysicsBody(XElement element, float scale = 1.0f)
            : this(element, Vector2.Zero, scale)
        {
        }

        public PhysicsBody(float width, float height, float radius, float density)
        {
            CreateBody(width, height, radius, density);
            
            dir = 1.0f;
            
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

        public PhysicsBody(XElement element, Vector2 position, float scale=1.0f)
        {
            float radius = ConvertUnits.ToSimUnits(element.GetAttributeFloat("radius", 0.0f)) * scale;
            float height = ConvertUnits.ToSimUnits(element.GetAttributeFloat("height", 0.0f)) * scale;
            float width = ConvertUnits.ToSimUnits(element.GetAttributeFloat("width", 0.0f)) * scale;

            density = element.GetAttributeFloat("density", 10.0f);

            CreateBody(width, height, radius, density);

            dir = 1.0f;
            
            body.CollisionCategories = Physics.CollisionItem;
            body.CollidesWith = Physics.CollisionWall | Physics.CollisionLevel;

            body.Friction = element.GetAttributeFloat("friction", 0.3f);
            body.Restitution = element.GetAttributeFloat("restitution", 0.05f);
            
            body.BodyType = BodyType.Dynamic;

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
                bodyShape = Shape.Capsule;
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

        public void ResetDynamics()
        {
            body.ResetDynamics();
        }

        public void ApplyLinearImpulse(Vector2 impulse)
        {
            body.ApplyLinearImpulse(impulse);
        }

        public void ApplyLinearImpulse(Vector2 impulse, Vector2 point)
        {
            body.ApplyLinearImpulse(impulse, point);
        }

        public void ApplyForce(Vector2 force)
        {
            body.ApplyForce(force);
        }

        public void ApplyForce(Vector2 force, Vector2 point)
        {
            body.ApplyForce(force, point);
        }

        public void ApplyTorque(float torque)
        {
            body.ApplyTorque(torque);
        }

        public void SetTransform(Vector2 simPosition, float rotation)
        {
            System.Diagnostics.Debug.Assert(MathUtils.IsValid(simPosition));
            System.Diagnostics.Debug.Assert(Math.Abs(simPosition.X) < 1000000.0f);
            System.Diagnostics.Debug.Assert(Math.Abs(simPosition.Y) < 1000000.0f);
            

            body.SetTransform(simPosition, rotation);
            SetPrevTransform(simPosition, rotation);
        }

        public void SetPrevTransform(Vector2 position, float rotation)
        {
            prevPosition = position;
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

            body.SetTransform((Vector2)targetPosition, targetRotation == null ? body.Rotation : (float)targetRotation);
            targetPosition = null;
        }

        public void MoveToPos(Vector2 pos, float force, Vector2? pullPos = null)
        {
            if (pullPos == null) pullPos = body.Position;

            Vector2 vel = body.LinearVelocity;
            Vector2 deltaPos = pos - (Vector2)pullPos;
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

            body.ApplyForce(dragForce + buoyancy);
            body.ApplyTorque(body.AngularVelocity * body.Mass * -0.08f);
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

            float torque = angle * 60.0f * (force/100.0f);

            if (body.IsKinematic)
            {
                body.AngularVelocity = torque;
            }
            else
            {
                body.ApplyTorque(body.Mass * torque);
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
