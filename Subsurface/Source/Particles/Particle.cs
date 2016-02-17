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

        //private float checkCollisionTimer;

        private Hull currentHull;

        private List<Gap> hullGaps;
        
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
        
        public void Init(ParticlePrefab prefab, Vector2 position, Vector2 speed, float rotation, Hull hullGuess = null)
        {

            this.prefab = prefab;

            spriteIndex = Rand.Int(prefab.Sprites.Count);

            currentHull = Hull.FindHull(position, hullGuess);
            //if (currentHull != null && currentHull.Submarine != null)
            //{
            //    Vector2 subVel = ConvertUnits.ToDisplayUnits(currentHull.Submarine.Velocity);
            //    //subVel.Y = -subVel.Y;
            //    speed += subVel;
            //}
            //else
            //{
            //    int a = 1;
            //}
            //if (currentHull == null) position = Submarine.Loaded == null ? position : position + Submarine.Loaded.Position;

            this.position = position;
            prevPosition = position;

            drawPosition = position;

            
            velocity = MathUtils.IsValid(speed) ? speed : Vector2.Zero;

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

            OnChangeHull = null;

            if (prefab.DeleteOnCollision || prefab.CollidesWithWalls)
            {
                hullGaps = currentHull == null ? new List<Gap>() : currentHull.ConnectedGaps;
                //hullLimits = new List<Hull>();
                //hullLimits = FindLimits(position);
            }

            if (prefab.RotateToDirection)
            {
                this.rotation = MathUtils.VectorToAngle(new Vector2(velocity.X, -velocity.Y));

                prevRotation = rotation;
            }
        }

        //private List<Hull> FindLimits(Vector2 position)
        //{
        //    List<Hull> hullList = new List<Hull>();

        //    currentHull = Hull.FindHull(position);
        //    if (currentHull == null) return hullList;

        //    hullList.Add(currentHull);

        //    return FindAdjacentHulls(hullList, currentHull, Math.Abs(velocity.X) > Math.Abs(velocity.Y));
        //}

        public bool Update(float deltaTime)
        {

            Vector2 subVel = currentHull ==null || currentHull.Submarine==null ? Vector2.Zero : ConvertUnits.ToDisplayUnits(currentHull.Submarine.Velocity);

            //over 3 times faster than position += velocity * deltatime
            position.X += (velocity.X+subVel.X) * deltaTime;
            position.Y += (velocity.Y+subVel.Y) * deltaTime;

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
                Vector2 edgePos =  position + prefab.CollisionRadius * Vector2.Normalize(velocity) * size.X;
                
                if (!Submarine.RectContains(currentHull.WorldRect, edgePos))
                {
                    if (prefab.DeleteOnCollision) return false;

                    bool gapFound = false;
                    foreach (Gap gap in hullGaps)
                    {
                        if (!gap.isHorizontal)
                        {
                            if (gap.WorldRect.X > position.X || gap.WorldRect.Right < position.X) continue;
                            if (Math.Sign(velocity.Y) != Math.Sign(gap.WorldRect.Y - (currentHull.WorldRect.Y - currentHull.WorldRect.Height))) continue;
                        }
                        else
                        {
                            if (gap.WorldRect.Y < position.Y || gap.WorldRect.Y - gap.WorldRect.Height > position.Y) continue;
                            if (Math.Sign(velocity.X) != Math.Sign(gap.WorldRect.Center.X - currentHull.WorldRect.Center.X)) continue;
                        }

                        //Rectangle enlargedRect = new Rectangle(gap.Rect.X - 10, gap.Rect.Y + 10, gap.Rect.Width + 20, gap.Rect.Height + 20);
                        //if (!Submarine.RectContains(enlargedRect, position)) continue;
                        gapFound = true;
                    }

                    if (!gapFound)
                    {

                        OnWallCollision(currentHull, edgePos);
                    }
                    else
                    {
                        currentHull = Hull.FindHull(position);
                        hullGaps = currentHull == null ? new List<Gap>() : currentHull.ConnectedGaps;

                        if (OnChangeHull != null) OnChangeHull(edgePos, currentHull);

                    }

                    //Hull prevHull = Hull.FindHull(prevPosition, hullLimits, currentHull);

                    //if (prevHull == null) return false;

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

        private void OnWallCollision(Hull prevHull, Vector2 position)
        {
            Rectangle prevHullRect = prevHull.WorldRect;

            if (position.Y < prevHullRect.Y - prevHullRect.Height)
            {
                position.Y = prevHullRect.Y - prevHullRect.Height + 1.0f;
                velocity.Y = -velocity.Y;
            }
            else if (position.Y > prevHullRect.Y)
            {
                position.Y = prevHullRect.Y - 1.0f;
                velocity.X = Math.Abs(velocity.Y) * Math.Sign(velocity.X);
                velocity.Y = -velocity.Y * 0.1f;
            }

            if (position.X < prevHullRect.X)
            {
                position.X = prevHullRect.X + 1.0f;
                velocity.X = -velocity.X;
            }
            else if (position.X > prevHullRect.X + prevHullRect.Width)
            {
                position.X = prevHullRect.X + prevHullRect.Width - 1.0f;
                velocity.X = -velocity.X;
            }

            velocity *= prefab.Restitution;
        }
        
        public void Draw(SpriteBatch spriteBatch)
        {
            drawPosition = Physics.Interpolate(prevPosition, position);
            float drawRotation = Physics.Interpolate(prevRotation, rotation);

            //drawPosition = ConvertUnits.ToDisplayUnits(drawPosition);

            Vector2 drawSize = size;

            if (prefab.GrowTime>0.0f && totalLifeTime-lifeTime < prefab.GrowTime)
            {
                drawSize *= ((totalLifeTime - lifeTime) / prefab.GrowTime);
            }

            prefab.Sprites[spriteIndex].Draw(spriteBatch, 
                new Vector2(drawPosition.X,  -drawPosition.Y), 
                color * alpha, 
                prefab.Sprites[spriteIndex].Origin, drawRotation,
                drawSize, SpriteEffects.None, prefab.Sprites[spriteIndex].Depth);

            prevPosition = position;
            prevRotation = rotation;
        }
    }
}
