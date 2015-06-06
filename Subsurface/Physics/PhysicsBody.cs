using System;
using System.Xml.Linq;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Factories;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Subsurface.Networking;
using System.Collections.Generic;

namespace Subsurface
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
        //flipped horizontally if the character holding it turns around)
        float dir;

        public Vector2 TargetPosition
        {
            get { return targetPosition; }
            set { targetPosition = value; }
        }

        public Vector2 TargetVelocity
        {
            get { return targetVelocity; }
            set { targetVelocity = value; }
        }

        public float TargetRotation
        {
            get { return targetRotation; }
            set { targetRotation = value; }
        }

        public float TargetAngularVelocity
        {
            get { return targetAngularVelocity; }
            set { targetAngularVelocity = value; }
        }

        public Vector2 DrawPosition
        {
            get { return drawPosition; }
        }

        public float DrawRotation
        {
            get { return drawRotation; }
        }

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

        public Vector2 Position
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
                body = BodyFactory.CreateRectangle(Game1.world, width, height, density);
                bodyShape = Shape.Rectangle;
            }
            else if (radius != 0.0f && height != 0.0f)
            {
                body = BodyFactory.CreateCapsule(Game1.world, height, radius, density);
                bodyShape = Shape.Capsule;
            }
            else if (radius != 0.0f)
            {
                body = BodyFactory.CreateCircle(Game1.world, radius, density);
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

            //items only collide with the map
            body.CollisionCategories = Physics.CollisionMisc;
            body.CollidesWith = Physics.CollisionWall;

            body.Friction = ToolBox.GetAttributeFloat(element, "friction", 0.3f);

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

        public void Draw(SpriteBatch spriteBatch, Sprite sprite, Color color)
        {
            if (!body.Enabled) return;

            SpriteEffects spriteEffect = (dir == 1.0f) ? SpriteEffects.None : SpriteEffects.FlipHorizontally;

            drawPosition = Physics.Interpolate(prevPosition, body.Position);
            drawPosition = ConvertUnits.ToDisplayUnits(drawPosition);

            drawRotation = Physics.Interpolate(prevRotation, body.Rotation);

            sprite.Draw(spriteBatch, new Vector2(drawPosition.X, -drawPosition.Y), color, -drawRotation, 1.0f, spriteEffect);

            //prevPosition = body.Position;
            //prevRotation = body.Rotation;
        }

        /// <summary>
        /// rotate the body towards the target rotation in the "shortest direction"
        /// </summary>
        public void SmoothRotate(float targetRotation, float force = 10.0f)
        {
            float nextAngle = body.Rotation + body.AngularVelocity * (float)Physics.step;

            float angle = ToolBox.GetShortestAngle(nextAngle, targetRotation);

            float torque = body.Mass * angle * 60.0f * (force/100.0f);

            body.ApplyTorque(torque);

            //float nextAngle = bodyAngle + body->GetAngularVelocity() / 60.0;
            //float totalRotation = desiredAngle - nextAngle;
            //while (totalRotation < -180 * DEGTORAD) totalRotation += 360 * DEGTORAD;
            //while (totalRotation > 180 * DEGTORAD) totalRotation -= 360 * DEGTORAD;
            //float desiredAngularVelocity = totalRotation * 60;
            //float torque = body->GetInertia() * desiredAngularVelocity / (1 / 60.0);
            //body->ApplyTorque(torque);




            //body.ApplyTorque((Math.Sign(angle) + Math.Max(Math.Min(angle * force, force / 2.0f), -force / 2.0f)) * body.Mass);
            //body.ApplyTorque(-body.AngularVelocity * 0.5f * body.Mass);
        }
        

        public void Remove()
        {
            list.Remove(this);
            Game1.world.RemoveBody(body);

        }

        public void FillNetworkData(NetworkEventType type, NetOutgoingMessage message)
        {
            message.Write(body.Position.X);
            message.Write(body.Position.Y);
            message.Write(body.LinearVelocity.X);
            message.Write(body.LinearVelocity.Y);

            message.Write(body.Rotation);
            message.Write(body.AngularVelocity);
        }

        public void ReadNetworkData(NetworkEventType type, NetIncomingMessage message)
        {
            targetPosition.X = message.ReadFloat();
            targetPosition.Y = message.ReadFloat();
            targetVelocity.X = message.ReadFloat();
            targetVelocity.Y = message.ReadFloat();

            targetRotation = message.ReadFloat();
            targetAngularVelocity = message.ReadFloat();
        }
    }
}
