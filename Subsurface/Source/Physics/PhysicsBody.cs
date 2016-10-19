using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Factories;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Barotrauma.Networking;
using System.Collections.Generic;
using System;

namespace Barotrauma
{
    class PhysicsBody
    {

        public enum Shape
        {
            Circle, Rectangle, Capsule
        };

        public static List<PhysicsBody> list = new List<PhysicsBody>();

        //the farseer physics body of the item
        private Body body;
        protected Vector2 prevPosition;
        protected float prevRotation;

        protected Vector2 targetPosition;
        //protected Vector2 targetVelocity;
        protected float targetRotation;
        //protected float targetAngularVelocity;

        private Vector2 drawPosition;
        private float drawRotation;


        private float lastNetworkUpdateTime;
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

        private float netInterpolationState;

        public Shape BodyShape
        {
            get { return bodyShape; }
        }

        public Vector2 TargetPosition
        {
            get { return targetPosition; }
            set
            {
                if (!MathUtils.IsValid(value)) return;
                targetPosition.X = MathHelper.Clamp(value.X, -10000.0f, 10000.0f);
                targetPosition.Y = MathHelper.Clamp(value.Y, -10000.0f, 10000.0f);
            }
        }

        //public Vector2 TargetVelocity
        //{
        //    get { return targetVelocity; }
        //    set 
        //    {
        //        if (!MathUtils.IsValid(value)) return;
        //        targetVelocity.X = MathHelper.Clamp(value.X, -100.0f, 100.0f);
        //        targetVelocity.Y = MathHelper.Clamp(value.Y, -100.0f, 100.0f); 
        //    }
        //}

        public float TargetRotation
        {
            get { return targetRotation; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                targetRotation = value; 
            }
        }

        //public float TargetAngularVelocity
        //{
        //    get { return targetAngularVelocity; }
        //    set 
        //    {
        //        if (!MathUtils.IsValid(value)) return;
        //        targetAngularVelocity = value; 
        //    }
        //}

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
            set { isEnabled = value; if (isEnabled) body.Enabled = isPhysEnabled; else body.Enabled = false; }
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

        private Texture2D bodyShapeTexture;
        public Texture2D BodyShapeTexture
        {
            get { return bodyShapeTexture; }
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

        public PhysicsBody(XElement element, Vector2 position, float scale=1.0f)
        {
            float radius = ConvertUnits.ToSimUnits(ToolBox.GetAttributeFloat(element, "radius", 0.0f)) * scale;
            float height = ConvertUnits.ToSimUnits(ToolBox.GetAttributeFloat(element, "height", 0.0f)) * scale;
            float width = ConvertUnits.ToSimUnits(ToolBox.GetAttributeFloat(element, "width", 0.0f)) * scale;

            density = ToolBox.GetAttributeFloat(element, "density", 10.0f);

            CreateBody(width, height, radius, density);

            dir = 1.0f;
            
            body.CollisionCategories = Physics.CollisionItem;
            body.CollidesWith = Physics.CollisionWall | Physics.CollisionLevel;

            body.Friction = ToolBox.GetAttributeFloat(element, "friction", 0.3f);
            body.Restitution = 0.05f;
            
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

        public void SetTransform(Vector2 position, float rotation)
        {
            body.SetTransform(position, rotation);
            SetPrevTransform(position, rotation);
        }

        public void SetPrevTransform(Vector2 position, float rotation)
        {
            prevPosition = position;
            prevRotation = rotation;
        }

        public void MoveToTargetPosition(bool lerp = true)
        {
            if (targetPosition == Vector2.Zero)
            {
                offsetFromTargetPos = Vector2.Zero;
                return;
            }

            if (lerp && Vector2.Distance(targetPosition, body.Position)<10.0f)
            {
                offsetFromTargetPos = targetPosition - (body.Position - offsetFromTargetPos);
                prevPosition = targetPosition;
            }

            body.SetTransform(targetPosition, targetRotation == 0.0f ? body.Rotation : targetRotation);
            targetPosition = Vector2.Zero;
        }
        
        public void MoveToPos(Vector2 pos, float force, Vector2? pullPos = null)
        {
            if (pullPos==null) pullPos = body.Position;

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

            //drag
            Vector2 velDir = Vector2.Normalize(LinearVelocity);

            Vector2 line = new Vector2((float)Math.Cos(body.Rotation), (float)Math.Sin(body.Rotation));
            line *= Math.Max(height + radius*2, height);

            Vector2 normal = new Vector2(-line.Y, line.X);
            normal = Vector2.Normalize(-normal);

            float dragDot = Math.Abs(Vector2.Dot(normal, velDir));
            Vector2 dragForce = Vector2.Zero;
            if (dragDot > 0)
            {
                float vel = LinearVelocity.Length() * 2.0f;
                float drag = dragDot * vel * vel
                    * Math.Max(height + radius * 2, height);
                dragForce = Math.Min(drag, Mass * 1000.0f) * -velDir;
                //if (dragForce.Length() > 100.0f) { }
            }

            body.ApplyForce(dragForce + buoyancy);
            body.ApplyTorque(body.AngularVelocity * body.Mass * -0.08f);
        }


        public void UpdateDrawPosition()
        {
            drawPosition = Timing.Interpolate(prevPosition, body.Position) - offsetFromTargetPos;
            drawPosition = ConvertUnits.ToDisplayUnits(drawPosition);

            drawRotation = Timing.Interpolate(prevRotation, body.Rotation);

            if (offsetFromTargetPos == Vector2.Zero) return;

            float diff = offsetFromTargetPos.Length();
            if (diff < 0.05f)
            {
                offsetFromTargetPos = Vector2.Zero;
            }
            else
            {
                offsetFromTargetPos = Vector2.Lerp(offsetFromTargetPos, Vector2.Zero, 0.1f);
            }
        }

        public void Draw(SpriteBatch spriteBatch, Sprite sprite, Color color, float? depth = null, float scale = 1.0f)
        {
            if (!Enabled) return;

            UpdateDrawPosition();

            SpriteEffects spriteEffect = (dir == 1.0f) ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

            if (GameMain.DebugDraw && !body.Awake)
            {
                color = Color.Blue;
            }
            
            sprite.Draw(spriteBatch, new Vector2(DrawPosition.X, -DrawPosition.Y), color, -drawRotation, scale, spriteEffect, depth);            
        }

        public void DebugDraw(SpriteBatch spriteBatch, Color color)
        {
            if (bodyShapeTexture == null)
            {
                switch (BodyShape)
                {
                    case PhysicsBody.Shape.Rectangle:
                        bodyShapeTexture = GUI.CreateRectangle(
                            (int)ConvertUnits.ToDisplayUnits(width),
                            (int)ConvertUnits.ToDisplayUnits(height));
                        break;

                    case PhysicsBody.Shape.Capsule:
                        bodyShapeTexture = GUI.CreateCapsule(
                            (int)ConvertUnits.ToDisplayUnits(radius),
                            (int)ConvertUnits.ToDisplayUnits(Math.Max(height,width)));
                        break;
                    case PhysicsBody.Shape.Circle:
                        bodyShapeTexture = GUI.CreateCircle((int)ConvertUnits.ToDisplayUnits(radius));
                        break;
                }
            }

            float rot = -DrawRotation;
            if (bodyShape == PhysicsBody.Shape.Capsule && width > height)
            {
                rot -= MathHelper.PiOver2;
            }

            spriteBatch.Draw(
                bodyShapeTexture,
                new Vector2(DrawPosition.X, -DrawPosition.Y),
                null,
                color,
                rot,
                new Vector2(bodyShapeTexture.Width / 2, bodyShapeTexture.Height / 2), 
                1.0f, SpriteEffects.None, 0.0f);
        }

        public void CorrectPosition(List<PosInfo> positionBuffer, float deltaTime, out Vector2 newVelocity)
        {
            newVelocity = Vector2.Zero;
            if (positionBuffer.Count < 2) return;

            PosInfo prev = positionBuffer[0];
            PosInfo next = positionBuffer[1];

            Vector2 currPos = SimPosition;

            //interpolate the position of the collider from the first position in the buffer towards the second
            if (prev.Timestamp < next.Timestamp)
            {
                //if there are more than 2 positions in the buffer, 
                //increase the interpolation speed to catch up with the server
                float speedMultiplier = 1.0f + (float)Math.Pow((positionBuffer.Count - 2) / 2.0f, 2.0f);

                netInterpolationState += (deltaTime * speedMultiplier) / (next.Timestamp - prev.Timestamp);
                currPos = Vector2.Lerp(prev.Position, next.Position, netInterpolationState);

                //override the targetMovement to make the character play the walking/running animation
                newVelocity = (next.Position - prev.Position) / (next.Timestamp - prev.Timestamp);
            }
            else
            {
                currPos = next.Position;
                netInterpolationState = 1.0f;
            }

            SetTransform(currPos, Rotation);

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

            if (bodyShapeTexture != null)
            {
                bodyShapeTexture.Dispose();
                bodyShapeTexture = null;
            }
        }

    }
}
