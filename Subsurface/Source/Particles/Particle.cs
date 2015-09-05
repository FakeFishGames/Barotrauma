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

        private float totalLifeTime;
        private float lifeTime;

        private Vector2 velocityChange;

        private Vector2 drawPosition;

        //private float checkCollisionTimer;

        private Hull currentHull;
        
        public ParticlePrefab.DrawTargetType DrawTarget
        {
            get { return prefab.DrawTarget; }        
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

            drawPosition = position;

            velocity = speed;

            this.rotation = rotation + Rand.Range(prefab.StartRotationMin, prefab.StartRotationMax);    
            prevRotation = rotation;

            angularVelocity = prefab.AngularVelocityMin + (prefab.AngularVelocityMax - prefab.AngularVelocityMin) * Rand.Range(0.0f, 1.0f);

            totalLifeTime = prefab.LifeTime;
            lifeTime = prefab.LifeTime;
            
            size = prefab.StartSizeMin + (prefab.StartSizeMax - prefab.StartSizeMin) * Rand.Range(0.0f, 1.0f);

            sizeChange = prefab.SizeChangeMin + (prefab.SizeChangeMax - prefab.SizeChangeMin) * Rand.Range(0.0f, 1.0f);

            color = prefab.StartColor;
            alpha = prefab.StartAlpha;
            
            velocityChange = prefab.VelocityChange;

            if (prefab.DeleteOnCollision)
            {
                currentHull = Hull.FindHull(position);
            }

            if (prefab.RotateToDirection)
            {
                this.rotation = MathUtils.VectorToAngle(new Vector2(velocity.X, -velocity.Y));

                prevRotation = rotation;
            }
        }

        public bool Update(float deltaTime)
        {
            //over 3 times faster than position += velocity * deltatime
            position.X += velocity.X * deltaTime;
            position.Y += velocity.Y * deltaTime;

            if (prefab.RotateToDirection)
            {
                if (velocityChange != Vector2.Zero || angularVelocity != 0.0f)
                {
                    rotation = MathUtils.VectorToAngle(new Vector2(velocity.X, -velocity.Y));
                }
            }
            else
            {
                rotation += angularVelocity * deltaTime;
            }

            velocity.X += velocityChange.X * deltaTime;
            velocity.Y += velocityChange.Y * deltaTime; 

            size.X += sizeChange.X * deltaTime;
            size.Y += sizeChange.Y * deltaTime;  

            alpha += prefab.ColorChange.W * deltaTime;

            color = new Color(
                color.R / 255.0f + prefab.ColorChange.X * deltaTime,
                color.G / 255.0f + prefab.ColorChange.Y * deltaTime,
                color.B / 255.0f + prefab.ColorChange.Z * deltaTime);
            
            if (prefab.DeleteOnCollision && currentHull!=null)
            {
                if (!Submarine.RectContains(currentHull.Rect, position)) return false;
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

            //drawPosition = ConvertUnits.ToDisplayUnits(drawPosition);

            Vector2 drawSize = size;

            if (prefab.GrowTime>0.0f && totalLifeTime-lifeTime < prefab.GrowTime)
            {
                drawSize *= ((totalLifeTime - lifeTime) / prefab.GrowTime);

            }

            prefab.Sprite.Draw(spriteBatch, drawPosition, color*alpha, prefab.Sprite.origin, drawRotation, drawSize, SpriteEffects.None, prefab.Sprite.Depth);

            //spriteBatch.Draw(
            //    prefab.sprite.Texture, 
            //    drawPosition, 
            //    null, 
            //    color*alpha, 
            //    drawRotation, 
            //    prefab.sprite.origin, 
            //    size,
            //    SpriteEffects.None, prefab.sprite.Depth);

            prevPosition = position;
            prevRotation = rotation;
        }
    }
}
