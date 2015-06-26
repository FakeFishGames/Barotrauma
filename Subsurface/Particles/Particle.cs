using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Subsurface.Particles
{
    class Particle
    {
        private ParticlePrefab prefab;

        private Vector2 position;
        private Vector2 prevPosition;

        private Vector2 velocity;

        private float rotation;
        private float prevRotation;

        private float angularVelocity;

        private Vector2 size;
        private Vector2 sizeChange;

        private Color color;
        private float alpha;

        private float lifeTime;

        private Vector2 velocityChange;

        private Vector2 drawPosition;

        private float checkCollisionTimer;
        
        public bool InWater
        {
            get { return prefab.inWater; }        
        }

        public Vector2 yLimits
        {
            get;
            set;
        }

        public Vector2 Size
        {
            get { return size; }
            set { size = value; }
        }

        public Vector2 VelocityChange
        {
            get { return velocityChange; }
            set { velocityChange = value; }
        }
        
        public void Init(ParticlePrefab prefab, Vector2 position, Vector2 speed, float rotation)
        {
            this.prefab = prefab;

            this.position = position;
            prevPosition = position;

            drawPosition = ConvertUnits.ToDisplayUnits(position);

            velocity = speed;

            this.rotation = rotation + ToolBox.RandomFloatLocal(prefab.startRotationMin, prefab.startRotationMax);    
            prevRotation = rotation;

            float rand = (float)Game1.localRandom.NextDouble();
            angularVelocity = prefab.angularVelocityMin + (prefab.angularVelocityMax - prefab.angularVelocityMin) * rand;

            lifeTime = prefab.lifeTime;

            rand = (float)Game1.localRandom.NextDouble();
            size = prefab.startSizeMin + (prefab.startSizeMax - prefab.startSizeMin)*rand;

            rand = (float)Game1.localRandom.NextDouble();
            sizeChange = prefab.sizeChangeMin + (prefab.sizeChangeMax - prefab.sizeChangeMin) * rand;

            yLimits = Vector2.Zero;

            color = prefab.startColor;
            alpha = prefab.startAlpha;
            
            velocityChange = prefab.velocityChange;            
        }

        public bool Update(float deltaTime)
        {
            //over 3 times faster than position += velocity * deltatime
            position.X += velocity.X * deltaTime;
            position.Y += velocity.Y * deltaTime;

            if (prefab.rotateToDirection)
            {
                rotation = ToolBox.VectorToAngle(velocity);
            }
            else
            {
                rotation += angularVelocity * deltaTime;
            }

            velocity.X += velocityChange.X * deltaTime;
            velocity.Y += velocityChange.Y * deltaTime; 

            size.X += sizeChange.X * deltaTime;
            size.Y += sizeChange.Y * deltaTime;

            alpha += prefab.colorChange.W * deltaTime;

            color = new Color(
                color.R / 255.0f + prefab.colorChange.X * deltaTime,
                color.G / 255.0f + prefab.colorChange.Y * deltaTime,
                color.B / 255.0f + prefab.colorChange.Z * deltaTime);

            if (yLimits!=Vector2.Zero)
            {
                if (position.Y>yLimits.X || position.Y<yLimits.Y)
                {
                    return false;
                }
            }

            if (prefab.deleteOnHit)
            {
                if (checkCollisionTimer > 0.0f)
                {
                    checkCollisionTimer -= deltaTime;
                }
                else
                {
                    if (Submarine.InsideWall(new Vector2(drawPosition.X, -drawPosition.Y)))
                    {
                        return false;
                    }
                    checkCollisionTimer = 0.05f;
                }
            }

            lifeTime -= deltaTime;

            if (lifeTime <= 0.0f || alpha <= 0.0f || size.X <= 0.0f || size.Y <= 0.0f) return false;

            return true;
        }
        
        public void Draw(SpriteBatch spriteBatch)
        {
            drawPosition = Physics.Interpolate(prevPosition, position);
            drawPosition.Y = -drawPosition.Y;
            float drawRotation = Physics.Interpolate(prevRotation, rotation);

            drawPosition = ConvertUnits.ToDisplayUnits(drawPosition);


            spriteBatch.Draw(
                prefab.sprite.Texture, 
                drawPosition, 
                null, 
                color*alpha, 
                drawRotation, 
                prefab.sprite.origin, 
                size,
                SpriteEffects.None, prefab.sprite.Depth);

            prevPosition = position;
            prevRotation = rotation;
        }
    }
}
