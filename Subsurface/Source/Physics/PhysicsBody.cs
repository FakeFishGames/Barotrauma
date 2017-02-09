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
    struct PosInfo
    {
        public readonly Vector2 Position;
        public readonly Direction Direction;

        public readonly float Timestamp;
        public readonly UInt16 ID;

        public PosInfo(Vector2 pos, Direction dir, float time)
            : this(pos, dir, 0, time)
        {
        }

        public PosInfo(Vector2 pos, Direction dir, UInt16 ID)
            : this(pos, dir, ID, 0.0f)
        {
        }

        public PosInfo(Vector2 pos, Direction dir, UInt16 ID, float time)
        {
            Position = pos;
            Direction = dir;
            this.ID = ID;

            Timestamp = time;
        }

        public static PosInfo TransformOutToInside(PosInfo posInfo, Submarine submarine)
        {
            //transform outside coordinates to in-sub coordinates
            return new PosInfo(
                posInfo.Position - ConvertUnits.ToSimUnits(submarine.Position),
                posInfo.Direction,
                posInfo.ID,
                posInfo.Timestamp);            
        }

        public static PosInfo TransformInToOutside(PosInfo posInfo)
        {
            var sub = Submarine.FindContaining(ConvertUnits.ToDisplayUnits(posInfo.Position));
            if (sub == null) return posInfo;

            return new PosInfo(
                posInfo.Position + ConvertUnits.ToSimUnits(sub.Position),
                posInfo.Direction,
                posInfo.ID,
                posInfo.Timestamp);            
        }
    }

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

        protected Vector2? targetPosition;
        protected float? targetRotation;

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

        public PhysicsBody(Body farseerBody)
        {
            body = farseerBody;
            if (body.UserData == null) body.UserData = this;

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
            System.Diagnostics.Debug.Assert(MathUtils.IsValid(position));
            System.Diagnostics.Debug.Assert(Math.Abs(position.X) < 1000000.0f);
            System.Diagnostics.Debug.Assert(Math.Abs(position.Y) < 1000000.0f);
            

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

        public void Draw(SpriteBatch spriteBatch, Sprite sprite, Color color, float? depth = null, float scale = 1.0f)
        {
            if (!Enabled) return;

            UpdateDrawPosition();

            if (sprite == null) return;

            SpriteEffects spriteEffect = (dir == 1.0f) ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

            if (GameMain.DebugDraw)
            {
                if (!body.Awake) color = Color.Blue;

                if (targetPosition != null)
                {
                    Vector2 pos = ConvertUnits.ToDisplayUnits((Vector2)targetPosition);
                    if (Submarine != null) pos += Submarine.DrawPosition;

                    GUI.DrawRectangle(spriteBatch,
                        new Vector2(pos.X - 5, -(pos.Y + 5)),
                        Vector2.One*10.0f, Color.Red, false, 0, 3);
                }

                if (offsetFromTargetPos != Vector2.Zero)
                {
                    Vector2 pos = ConvertUnits.ToDisplayUnits(body.Position);
                    if (Submarine != null) pos += Submarine.DrawPosition;

                    GUI.DrawLine(spriteBatch,
                        new Vector2(pos.X, -pos.Y),
                        new Vector2(DrawPosition.X, -DrawPosition.Y),
                        Color.Cyan, 0, 5);
                }
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
            Vector2 newPosition = SimPosition;
            CorrectPosition(positionBuffer, deltaTime, out newVelocity, out newPosition);
            
            SetTransform(newPosition, Rotation);
        }


        public void CorrectPosition(List<PosInfo> positionBuffer, float deltaTime, out Vector2 newVelocity, out Vector2 newPosition)
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
                float speedMultiplier = 0.9f + (float)Math.Pow((positionBuffer.Count - 2) / 5.0f, 2.0f);

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

            if (bodyShapeTexture != null)
            {
                bodyShapeTexture.Dispose();
                bodyShapeTexture = null;
            }
        }

    }
}
