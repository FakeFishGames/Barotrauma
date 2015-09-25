using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

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

        private List<Hull> hullLimits;
        
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

            if (prefab.DeleteOnCollision || prefab.CollidesWithWalls)
            {
                //currentHull = Hull.FindHull(position);
                hullLimits = new List<Hull>();
                hullLimits = FindLimits(position);
            }

            if (prefab.RotateToDirection)
            {
                this.rotation = MathUtils.VectorToAngle(new Vector2(velocity.X, -velocity.Y));

                prevRotation = rotation;
            }
        }

        private List<Hull> FindLimits(Vector2 position)
        {
            List<Hull> hullList = new List<Hull>();

            currentHull = Hull.FindHull(position);
            if (currentHull == null) return hullList;

            hullList.Add(currentHull);
            
            return FindAdjacentHulls(hullList, currentHull, Math.Abs(velocity.X)>Math.Abs(velocity.Y));
        }

        private List<Hull> FindAdjacentHulls(List<Hull> adjacentHulls, Hull currentHull, bool isHorizontal)
        {
                foreach (Gap gap in Gap.GapList)
                {
                    if (gap.isHorizontal != isHorizontal) continue;
                    if (gap.Open < 0.01f) continue;
                    if (gap.linkedTo.Count==1)
                    {
                        if (!adjacentHulls.Contains(gap.linkedTo[0] as Hull))
                        {
                            adjacentHulls.Add(gap.linkedTo[0] as Hull);
                        }
                    }
                    else if (gap.linkedTo[0] == currentHull && gap.linkedTo[1] != null)
                    {
                        if (!adjacentHulls.Contains(gap.linkedTo[1] as Hull))
                        {
                            adjacentHulls.Add(gap.linkedTo[1] as Hull);
                            FindAdjacentHulls(adjacentHulls, gap.linkedTo[1] as Hull, isHorizontal);
                        }
                    }
                    else if (gap.linkedTo[1] == currentHull && gap.linkedTo[0] != null)
                    {
                        if (!adjacentHulls.Contains(gap.linkedTo[0] as Hull))
                        {
                            adjacentHulls.Add(gap.linkedTo[0] as Hull);
                            FindAdjacentHulls(adjacentHulls, gap.linkedTo[0] as Hull, isHorizontal);
                        }
                    }
                }

                return adjacentHulls;
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
            
            if ((prefab.DeleteOnCollision || prefab.CollidesWithWalls) && currentHull!=null)
            {
                bool insideHull = false;
                foreach (Hull hull in hullLimits)
                {
                    if (!Submarine.RectContains(hull.Rect, position)) continue;
                    
                    insideHull = true;
                    break;                    
                }

                if (!insideHull)
                {
                    if (prefab.DeleteOnCollision) return false;

                    Hull prevHull = Hull.FindHull(prevPosition, hullLimits, currentHull);

                    if (prevHull == null) return false;

                    OnWallCollision(prevHull);
                }

                //if (position.Y < currentHull.Rect.Y-currentHull.Rect.Height)
                //{
                //    position.Y = currentHull.Rect.Y - currentHull.Rect.Height;
                //    velocity.Y *= -0.2f;
                //}
                //if (!Submarine.RectContains(currentHull.Rect, position)) return false;
            }

            lifeTime -= deltaTime;

            if (lifeTime <= 0.0f || alpha <= 0.0f || size.X <= 0.0f || size.Y <= 0.0f) return false;

            return true;
        }

        private void OnWallCollision(Hull prevHull)
        {
            float restitution = 0.05f;

            if (position.Y < prevHull.Rect.Y - prevHull.Rect.Height)
            {
                position.Y = prevHull.Rect.Y - prevHull.Rect.Height + 1.0f;
                velocity.Y = -velocity.Y;
            }
            else if (position.Y > prevHull.Rect.Y)
            {
                position.Y = prevHull.Rect.Y - 1.0f;
                velocity.Y = -velocity.Y;
            }

            if (position.X < prevHull.Rect.X)
            {
                position.X = prevHull.Rect.X + 1.0f;
                velocity.X = -velocity.X;
            }
            else if (position.X > prevHull.Rect.X + prevHull.Rect.Width)
            {
                position.X = prevHull.Rect.X + prevHull.Rect.Width - 1.0f;
                velocity.X = -velocity.X;
            }

            velocity *= restitution;
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
