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
        protected Vector2 targetVelocity;
        protected float targetRotation;
        protected float targetAngularVelocity;

        private Vector2 drawPosition;
        private float drawRotation;


        public readonly Shape bodyShape;
        public readonly float height, width, radius;
        
        private float density;
        
        //the direction the item is facing (for example, a gun has to be 
        //flipped horizontally if the Character holding it turns around)
        float dir;

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

        public Vector2 TargetVelocity
        {
            get { return targetVelocity; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                targetVelocity.X = MathHelper.Clamp(value.X, -100.0f, 100.0f);
                targetVelocity.Y = MathHelper.Clamp(value.Y, -100.0f, 100.0f); 
            }
        }

        public float TargetRotation
        {
            get { return targetRotation; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                targetRotation = value; 
            }
        }

        public float TargetAngularVelocity
        {
            get { return targetAngularVelocity; }
            set 
            {
                if (!MathUtils.IsValid(value)) return;
                targetAngularVelocity = value; 
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

        public bool Enabled
        {
            get { return body.Enabled; }
            set { body.Enabled = value; }
        }

        public Vector2 SimPosition
        {
            get { return body.Position; }
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

        public PhysicsBody(XElement element)
            : this(element, Vector2.Zero)
        {
        }

        public PhysicsBody(Body body)
        {         
            this.body = body;

            density = 10.0f;

            dir = 1.0f;

            list.Add(this);
        }

        public PhysicsBody(XElement element, Vector2 position)
        {
            float radius = ConvertUnits.ToSimUnits(ToolBox.GetAttributeFloat(element, "radius", 0.0f));
            float height = ConvertUnits.ToSimUnits(ToolBox.GetAttributeFloat(element, "height", 0.0f));
            float width = ConvertUnits.ToSimUnits(ToolBox.GetAttributeFloat(element, "width", 0.0f));

            density = ToolBox.GetAttributeFloat(element, "density", 10.0f);

            if (width != 0.0f && height != 0.0f)
            {
                body = BodyFactory.CreateRectangle(GameMain.World, width, height, density);
                bodyShape = Shape.Rectangle;
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
                DebugConsole.ThrowError("Invalid body dimensions in " + element);
            }

            this.width = width;
            this.height = height;
            this.radius = radius;

            dir = 1.0f;
            
            body.CollisionCategories = Physics.CollisionMisc;
            body.CollidesWith = Physics.CollisionWall | Physics.CollisionLevel;

            body.Friction = ToolBox.GetAttributeFloat(element, "friction", 0.3f);
            body.Restitution = 0.05f;
            
            body.BodyType = BodyType.Dynamic;
            //body.AngularDamping = Limb.LimbAngularDamping;

            body.UserData = this;

            SetTransform(position, 0.0f);

            //prevPosition = ConvertUnits.ToDisplayUnits(position);

            list.Add(this);
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

        public void SetToTargetPosition()
        {
            if (targetPosition != Vector2.Zero)
            {
                body.SetTransform(targetPosition, targetRotation);
                body.LinearVelocity = targetVelocity;
                body.AngularVelocity = targetAngularVelocity;
                targetPosition = Vector2.Zero;
            }
        }

        public void UpdateDrawPosition()
        {
            drawPosition = Physics.Interpolate(prevPosition, body.Position);
            drawPosition = ConvertUnits.ToDisplayUnits(drawPosition);

            drawRotation = Physics.Interpolate(prevRotation, body.Rotation);
        }

        public void Draw(SpriteBatch spriteBatch, Sprite sprite, Color color, float? depth = null)
        {
            if (!body.Enabled) return;

            UpdateDrawPosition();

            SpriteEffects spriteEffect = (dir == 1.0f) ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

            if (GameMain.DebugDraw && !body.Awake)
            {
                color = Color.Blue;
            }
            
            sprite.Draw(spriteBatch, new Vector2(DrawPosition.X, -DrawPosition.Y), color, -drawRotation, 1.0f, spriteEffect, depth);
            
        }

        /// <summary>
        /// rotate the body towards the target rotation in the "shortest direction"
        /// </summary>
        public void SmoothRotate(float targetRotation, float force = 10.0f)
        {
            float nextAngle = body.Rotation + body.AngularVelocity * (float)Physics.step;

            float angle = MathUtils.GetShortestAngle(nextAngle, targetRotation);

            float torque = body.Mass * angle * 60.0f * (force/100.0f);

            body.ApplyTorque(torque);
        }
        

        public void Remove()
        {
            list.Remove(this);
            GameMain.World.RemoveBody(body);
        }

    }
}
