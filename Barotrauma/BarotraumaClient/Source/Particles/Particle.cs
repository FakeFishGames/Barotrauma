using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Barotrauma.Particles
{
    class Particle
    {
        private ParticlePrefab prefab;

        public delegate void OnChangeHullHandler(Vector2 position, Hull currentHull);
        public OnChangeHullHandler OnChangeHull;

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

        private int spriteIndex;

        private float totalLifeTime;
        private float lifeTime;

        private Vector2 velocityChange;

        private Vector2 drawPosition;
        private float drawRotation;
        
        private Hull currentHull;

        private List<Gap> hullGaps;

        private float animState;
        private int animFrame;
        
        public ParticlePrefab.DrawTargetType DrawTarget
        {
            get { return prefab.DrawTarget; }        
        }

        public ParticleBlendState BlendState
        {
            get { return prefab.BlendState; }
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

        public Vector2 Velocity
        {
            get { return velocity; }
            set { velocity = value; }
        }

        public Hull CurrentHull
        {
            get { return currentHull; }
        }
        
        public void Init(ParticlePrefab prefab, Vector2 position, Vector2 speed, float rotation, Hull hullGuess = null)
        {
            this.prefab = prefab;

            spriteIndex = Rand.Int(prefab.Sprites.Count);

            animState = 0;
            animFrame = 0;

            currentHull = Hull.FindHull(position, hullGuess);

            this.position = position;
            prevPosition = position;
            
            drawPosition = position;
            
            velocity = MathUtils.IsValid(speed) ? speed : Vector2.Zero;

            if (currentHull != null && currentHull.Submarine != null)
            {
                velocity += ConvertUnits.ToDisplayUnits(currentHull.Submarine.Velocity);
            }

            this.rotation = rotation + Rand.Range(prefab.StartRotationMin, prefab.StartRotationMax);    
            prevRotation = rotation;

            angularVelocity = prefab.AngularVelocityMin + (prefab.AngularVelocityMax - prefab.AngularVelocityMin) * Rand.Range(0.0f, 1.0f);

            totalLifeTime = prefab.LifeTime;
            lifeTime = prefab.LifeTime;
            
            size = prefab.StartSizeMin + (prefab.StartSizeMax - prefab.StartSizeMin) * Rand.Range(0.0f, 1.0f);

            sizeChange = prefab.SizeChangeMin + (prefab.SizeChangeMax - prefab.SizeChangeMin) * Rand.Range(0.0f, 1.0f);

            color = new Color(prefab.StartColor, 1.0f);
            alpha = prefab.StartAlpha;
            
            velocityChange = prefab.VelocityChange;

            OnChangeHull = null;

            if (prefab.DeleteOnCollision || prefab.CollidesWithWalls)
            {
                hullGaps = currentHull == null ? new List<Gap>() : currentHull.ConnectedGaps;
            }

            if (prefab.RotateToDirection)
            {
                this.rotation = MathUtils.VectorToAngle(new Vector2(velocity.X, -velocity.Y));

                prevRotation = rotation;
            }
        }

        public bool Update(float deltaTime)
        {
            prevPosition = position;
            prevRotation = rotation;

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

            if (prefab.WaterDrag > 0.0f && 
                (currentHull == null || (currentHull.Submarine != null && position.Y - currentHull.Submarine.DrawPosition.Y < currentHull.Surface)))
            {
                ApplyDrag(prefab.WaterDrag, deltaTime);
            }
            else if (prefab.Drag > 0.0f)
            {
                ApplyDrag(prefab.Drag, deltaTime);
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

            if (prefab.Sprites[spriteIndex] is SpriteSheet)
            {
                animState += deltaTime;
                int frameCount = ((SpriteSheet)prefab.Sprites[spriteIndex]).FrameCount;
                animFrame = (int)Math.Min(Math.Floor(animState / prefab.AnimDuration * frameCount), frameCount - 1);
            }
            
            lifeTime -= deltaTime;
            if (lifeTime <= 0.0f || alpha <= 0.0f || size.X <= 0.0f || size.Y <= 0.0f) return false;

            if (!prefab.DeleteOnCollision && !prefab.CollidesWithWalls) return true;
            
            if (currentHull == null)
            {
                Hull collidedHull = Hull.FindHull(position);
                if (collidedHull != null)
                {
                    if (prefab.DeleteOnCollision) return false;
                    OnWallCollisionOutside(collidedHull);
                }                   

            }
            else
            {
                Vector2 collisionNormal = Vector2.Zero;
                if (velocity.Y < 0.0f && position.Y - prefab.CollisionRadius * size.Y < currentHull.WorldRect.Y - currentHull.WorldRect.Height)
                {
                    if (prefab.DeleteOnCollision) return false;
                    collisionNormal = new Vector2(0.0f, 1.0f);
                }
                else if (velocity.Y > 0.0f && position.Y + prefab.CollisionRadius * size.Y > currentHull.WorldRect.Y)
                {
                    if (prefab.DeleteOnCollision) return false;
                    collisionNormal = new Vector2(0.0f, -1.0f);
                }
                else if (velocity.X < 0.0f && position.X - prefab.CollisionRadius * size.X < currentHull.WorldRect.X)
                {
                    if (prefab.DeleteOnCollision) return false;
                    collisionNormal = new Vector2(1.0f, 0.0f);
                }
                else if (velocity.X > 0.0f && position.X + prefab.CollisionRadius * size.X > currentHull.WorldRect.Right)
                {
                    if (prefab.DeleteOnCollision) return false;
                    collisionNormal = new Vector2(-1.0f, 0.0f);
                }

                if (collisionNormal != Vector2.Zero)
                {
                    bool gapFound = false;
                    foreach (Gap gap in hullGaps)
                    {
                        if (gap.isHorizontal != (collisionNormal.X != 0.0f)) continue;

                        if (gap.isHorizontal)
                        {
                            if (gap.WorldRect.Y < position.Y || gap.WorldRect.Y - gap.WorldRect.Height > position.Y) continue;
                            int gapDir = Math.Sign(gap.WorldRect.Center.X - currentHull.WorldRect.Center.X);
                            if (Math.Sign(velocity.X) != gapDir || Math.Sign(position.X - currentHull.WorldRect.Center.X) != gapDir) continue;
                        }
                        else
                        {
                            if (gap.WorldRect.X > position.X || gap.WorldRect.Right < position.X) continue;
                            float hullCenterY = currentHull.WorldRect.Y - currentHull.WorldRect.Height / 2;
                            int gapDir = Math.Sign(gap.WorldRect.Y - hullCenterY);
                            if (Math.Sign(velocity.Y) != gapDir || Math.Sign(position.Y - hullCenterY) != gapDir) continue;
                        }

                        gapFound = true;
                        break;
                    }

                    if (!gapFound)
                    {
                        OnWallCollisionInside(currentHull, collisionNormal);
                    }
                    else
                    {
                        Hull newHull = Hull.FindHull(position);
                        if (newHull != currentHull)
                        {
                            currentHull = newHull;
                            hullGaps = currentHull == null ? new List<Gap>() : currentHull.ConnectedGaps;
                            OnChangeHull?.Invoke(position, currentHull);
                        }
                    }
                }
            }

            return true;
        }

        private void ApplyDrag(float dragCoefficient, float deltaTime)
        {
            if (Math.Abs(velocity.X) < 0.0001f && Math.Abs(velocity.Y) < 0.0001f) return;
            float speed = velocity.Length();

            velocity -= (velocity / speed) * Math.Min(speed * speed * dragCoefficient * deltaTime, 1.0f);
        }

        private void OnWallCollisionInside(Hull prevHull, Vector2 collisionNormal)
        {
            Rectangle prevHullRect = prevHull.WorldRect;

            Vector2 subVel = ConvertUnits.ToDisplayUnits(prevHull.Submarine.Velocity);
            velocity -= subVel;

            if (Math.Abs(collisionNormal.X) > Math.Abs(collisionNormal.Y))
            {
                if (collisionNormal.X > 0.0f)
                {
                    position.X = Math.Max(position.X, prevHullRect.X + prefab.CollisionRadius * size.X);
                }
                else
                {
                    position.X = Math.Min(position.X, prevHullRect.Right - prefab.CollisionRadius * size.X);
                }
                velocity.X = Math.Sign(collisionNormal.X) * Math.Abs(velocity.X) * prefab.Restitution;
                velocity.Y *= (1.0f - prefab.Friction);
            }
            else
            {
                if (collisionNormal.Y > 0.0f)
                {
                    position.Y = Math.Max(position.Y, prevHullRect.Y - prevHullRect.Height + prefab.CollisionRadius * size.Y);
                }
                else
                {
                    position.Y = Math.Min(position.Y, prevHullRect.Y - prefab.CollisionRadius * size.Y);

                }
                velocity.X *= (1.0f - prefab.Friction);
                velocity.Y = Math.Sign(collisionNormal.Y) * Math.Abs(velocity.Y) * prefab.Restitution;
            }

            velocity += subVel;
        }


        private void OnWallCollisionOutside(Hull collisionHull)
        {
            Rectangle hullRect = collisionHull.WorldRect;
            
            if (position.Y < hullRect.Y - hullRect.Height)
            {
                position.Y = hullRect.Y - hullRect.Height - prefab.CollisionRadius;
                velocity.Y = -velocity.Y;
            }
            else if (position.Y > hullRect.Y)
            {
                position.Y = hullRect.Y + prefab.CollisionRadius;
                velocity.X = Math.Abs(velocity.Y) * Math.Sign(velocity.X);
                velocity.Y = -velocity.Y;
            }

            if (position.X < hullRect.X)
            {
                position.X = hullRect.X - prefab.CollisionRadius;
                velocity.X = -velocity.X;
            }
            else if (position.X > hullRect.X + hullRect.Width)
            {
                position.X = hullRect.X + hullRect.Width + prefab.CollisionRadius;
                velocity.X = -velocity.X;
            }

            velocity *= prefab.Restitution;
        }

        public void UpdateDrawPos()
        {
            drawPosition = Timing.Interpolate(prevPosition, position);
            drawRotation = Timing.Interpolate(prevRotation, rotation);

            prevPosition = position;
            prevRotation = rotation;
        }
        
        public void Draw(SpriteBatch spriteBatch)
        {
            Vector2 drawSize = size;

            if (prefab.GrowTime > 0.0f && totalLifeTime - lifeTime < prefab.GrowTime)
            {
                drawSize *= ((totalLifeTime - lifeTime) / prefab.GrowTime);
            }

            if (prefab.Sprites[spriteIndex] is SpriteSheet)
            {
                ((SpriteSheet)prefab.Sprites[spriteIndex]).Draw(
                    spriteBatch, animFrame,
                    new Vector2(drawPosition.X, -drawPosition.Y),
                    color * alpha,
                    prefab.Sprites[spriteIndex].Origin, drawRotation,
                    drawSize, SpriteEffects.None, prefab.Sprites[spriteIndex].Depth);
            }
            else
            {
                prefab.Sprites[spriteIndex].Draw(spriteBatch,
                    new Vector2(drawPosition.X, -drawPosition.Y),
                    color * alpha,
                    prefab.Sprites[spriteIndex].Origin, drawRotation,
                    drawSize, SpriteEffects.None, prefab.Sprites[spriteIndex].Depth);
            }
        }
    }
}
