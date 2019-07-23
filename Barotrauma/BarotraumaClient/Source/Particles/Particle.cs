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

        private Vector2 dragVec = Vector2.Zero;
        private int dragWait = 0;

        private Vector2 size;
        private Vector2 sizeChange;

        private Color color;
        private bool changeColor;

        private int spriteIndex;

        private float totalLifeTime;
        private float lifeTime;

        private float startDelay;

        private Vector2 velocityChange;
        private Vector2 velocityChangeWater;

        private Vector2 drawPosition;
        private float drawRotation;
        
        private Hull currentHull;

        private List<Gap> hullGaps;

        private bool hasSubEmitters;
        private List<ParticleEmitter> subEmitters = new List<ParticleEmitter>();

        private float animState;
        private int animFrame;

        private float collisionUpdateTimer;

        public bool HighQualityCollisionDetection;
                
        public ParticlePrefab.DrawTargetType DrawTarget
        {
            get { return prefab.DrawTarget; }        
        }

        public ParticleBlendState BlendState
        {
            get { return prefab.BlendState; }
        }

        public float StartDelay
        {
            get { return startDelay; }
            set { startDelay = MathHelper.Clamp(value, Prefab.StartDelayMin, prefab.StartDelayMax); }
        }
        
        public Vector2 Size
        {
            get { return size; }
            set { size = value; }
        }
        
        public Hull CurrentHull
        {
            get { return currentHull; }
        }

        public ParticlePrefab Prefab
        {
            get { return prefab; }
        }
        
        public void Init(ParticlePrefab prefab, Vector2 position, Vector2 speed, float rotation, Hull hullGuess = null)
        {
            this.prefab = prefab;

            spriteIndex = Rand.Int(prefab.Sprites.Count);

            animState = 0;
            animFrame = 0;
            dragWait = 0;
            dragVec = Vector2.Zero;

            currentHull = Hull.FindHull(position, hullGuess);

            this.position = position;
            prevPosition = position;
            
            drawPosition = position;
            
            velocity = MathUtils.IsValid(speed) ? speed : Vector2.Zero;

            if (currentHull?.Submarine != null)
            {
                velocity += ConvertUnits.ToDisplayUnits(currentHull.Submarine.Velocity);
            }

            this.rotation = rotation + Rand.Range(prefab.StartRotationMinRad, prefab.StartRotationMaxRad);    
            prevRotation = rotation;

            angularVelocity = Rand.Range(prefab.AngularVelocityMinRad, prefab.AngularVelocityMaxRad);

            totalLifeTime = prefab.LifeTime;
            lifeTime = prefab.LifeTime;
            startDelay = Rand.Range(prefab.StartDelayMin, prefab.StartDelayMax);
            
            size = prefab.StartSizeMin + (prefab.StartSizeMax - prefab.StartSizeMin) * Rand.Range(0.0f, 1.0f);

            sizeChange = prefab.SizeChangeMin + (prefab.SizeChangeMax - prefab.SizeChangeMin) * Rand.Range(0.0f, 1.0f);

            color = prefab.StartColor;
            changeColor = prefab.StartColor != prefab.EndColor;
            
            velocityChange = prefab.VelocityChangeDisplay;
            velocityChangeWater = prefab.VelocityChangeWaterDisplay;

            HighQualityCollisionDetection = false;

            OnChangeHull = null;

            subEmitters.Clear();
            hasSubEmitters = false;
            foreach (ParticleEmitterPrefab emitterPrefab in prefab.SubEmitters)
            {
                subEmitters.Add(new ParticleEmitter(emitterPrefab));
                hasSubEmitters = true;
            }

            if (prefab.UseCollision)
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
            if (startDelay > 0.0f)
            {
                startDelay -= deltaTime;
                return true;
            }

            prevPosition = position;
            prevRotation = rotation;

            //over 3 times faster than position += velocity * deltatime
            position.X += velocity.X * deltaTime;
            position.Y += velocity.Y * deltaTime;

            if (prefab.RotateToDirection)
            {
                if (velocityChange != Vector2.Zero || angularVelocity != 0.0f)
                {
                    Vector2 relativeVel = velocity;
                    if (currentHull?.Submarine != null)
                    {
                        relativeVel -= ConvertUnits.ToDisplayUnits(currentHull.Submarine.Velocity);
                    }
                    rotation = MathUtils.VectorToAngle(new Vector2(relativeVel.X, -relativeVel.Y));
                }
            }
            else
            {
                rotation += angularVelocity * deltaTime;
            }

            bool inWater = (currentHull == null || (currentHull.Submarine != null && position.Y - currentHull.Submarine.DrawPosition.Y < currentHull.Surface));
            if (inWater)
            {
                velocity.X += velocityChangeWater.X * deltaTime;
                velocity.Y += velocityChangeWater.Y * deltaTime;
                if (prefab.WaterDrag > 0.0f)
                {
                    ApplyDrag(prefab.WaterDrag, deltaTime);
                }
            }
            else
            {
                velocity.X += velocityChange.X * deltaTime;
                velocity.Y += velocityChange.Y * deltaTime; 
                if (prefab.Drag > 0.0f)
                {
                    ApplyDrag(prefab.Drag, deltaTime);
                }
            }

            size.X += sizeChange.X * deltaTime;
            size.Y += sizeChange.Y * deltaTime;  

            if (changeColor)
            {
                color = Color.Lerp(prefab.EndColor, prefab.StartColor, lifeTime / prefab.LifeTime);
            }
            
            if (prefab.Sprites[spriteIndex] is SpriteSheet)
            {
                animState += deltaTime;
                int frameCount = ((SpriteSheet)prefab.Sprites[spriteIndex]).FrameCount;
                animFrame = (int)Math.Min(Math.Floor(animState / prefab.AnimDuration * frameCount), frameCount - 1);
            }
            
            lifeTime -= deltaTime;
            if (lifeTime <= 0.0f || color.A <= 0 || size.X <= 0.0f || size.Y <= 0.0f) { return false; }

            if (hasSubEmitters)
            {
                foreach (ParticleEmitter emitter in subEmitters)
                {
                    emitter.Emit(deltaTime, position, currentHull);
                }
            }

            if (!prefab.UseCollision) { return true; }

            if (HighQualityCollisionDetection)
            {
                return CollisionUpdate();
            }
            else
            {
                collisionUpdateTimer -= deltaTime;
                if (collisionUpdateTimer <= 0.0f)
                {
                    //more frequent collision updates if the particle is moving fast
                    collisionUpdateTimer = 0.5f - Math.Min((Math.Abs(velocity.X) + Math.Abs(velocity.Y)) * 0.01f, 0.45f);
                    return CollisionUpdate();
                }
            }

            return true;
        }

        private bool CollisionUpdate()
        {
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
                Rectangle hullRect = currentHull.WorldRect;
                Vector2 collisionNormal = Vector2.Zero;
                if (velocity.Y < 0.0f && position.Y - prefab.CollisionRadius * size.Y < hullRect.Y - hullRect.Height)
                {
                    if (prefab.DeleteOnCollision) return false;
                    collisionNormal = new Vector2(0.0f, 1.0f);
                }
                else if (velocity.Y > 0.0f && position.Y + prefab.CollisionRadius * size.Y > hullRect.Y)
                {
                    if (prefab.DeleteOnCollision) return false;
                    collisionNormal = new Vector2(0.0f, -1.0f);
                }
                else if (velocity.X < 0.0f && position.X - prefab.CollisionRadius * size.X < hullRect.X)
                {
                    if (prefab.DeleteOnCollision) return false;
                    collisionNormal = new Vector2(1.0f, 0.0f);
                }
                else if (velocity.X > 0.0f && position.X + prefab.CollisionRadius * size.X > hullRect.Right)
                {
                    if (prefab.DeleteOnCollision) return false;
                    collisionNormal = new Vector2(-1.0f, 0.0f);
                }

                if (collisionNormal != Vector2.Zero)
                {
                    bool gapFound = false;
                    foreach (Gap gap in hullGaps)
                    {
                        if (gap.Open <= 0.9f || gap.IsHorizontal != (collisionNormal.X != 0.0f)) continue;

                        if (gap.IsHorizontal)
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
                        Hull newHull = Hull.FindHull(position, currentHull);
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
            if (velocity.LengthSquared() < dragVec.LengthSquared())
            {
                velocity = Vector2.Zero;
                return;
            }
            if (Math.Abs(velocity.X) < 0.0001f && Math.Abs(velocity.Y) < 0.0001f) return;
            
            //TODO: some better way to handle particle drag
            //this doesn't work that well because the drag vector is only updated every 0.5 seconds, allowing the particle to accelerate way more than it should
            //(e.g. a falling particle can freely accelerate for 0.5 seconds before the drag takes effect)
            dragWait--;
            if (dragWait <= 0)
            {
                dragWait = 30;

                float speed = velocity.Length();

                dragVec = (velocity / speed) * Math.Min(speed * speed * dragCoefficient * deltaTime, 1.0f);
            }

            velocity -= dragVec;
        }

        private void OnWallCollisionInside(Hull prevHull, Vector2 collisionNormal)
        {
            Rectangle prevHullRect = prevHull.WorldRect;

            Vector2 subVel = prevHull?.Submarine != null ? ConvertUnits.ToDisplayUnits(prevHull.Submarine.Velocity) : Vector2.Zero;
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

            Vector2 center = new Vector2(hullRect.X + hullRect.Width /2, hullRect.Y - hullRect.Height / 2);

            if (position.Y < center.Y)
            {
                position.Y = hullRect.Y - hullRect.Height - prefab.CollisionRadius;
                velocity.X *= (1.0f - prefab.Friction);
                velocity.Y = -velocity.Y * prefab.Restitution;
            }
            else if (position.Y > center.Y)
            {
                position.Y = hullRect.Y + prefab.CollisionRadius;
                velocity.X *= (1.0f - prefab.Friction);
                velocity.Y = -velocity.Y * prefab.Restitution;
            }

            if (position.X < center.X)
            {
                position.X = hullRect.X - prefab.CollisionRadius;
                velocity.X = -velocity.X * prefab.Restitution;
                velocity.Y *= (1.0f - prefab.Friction);
            }
            else if (position.X > center.X)
            {
                position.X = hullRect.X + hullRect.Width + prefab.CollisionRadius;
                velocity.X = -velocity.X * prefab.Restitution;
                velocity.Y *= (1.0f - prefab.Friction);
            }

            velocity *= prefab.Restitution;
        }

        public void UpdateDrawPos()
        {
            drawPosition = Timing.Interpolate(prevPosition, position);
            drawRotation = Timing.Interpolate(prevRotation, rotation);
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
                    color * (color.A / 255.0f),
                    prefab.Sprites[spriteIndex].Origin, drawRotation,
                    drawSize, SpriteEffects.None, prefab.Sprites[spriteIndex].Depth);
            }
            else
            {
                prefab.Sprites[spriteIndex].Draw(spriteBatch,
                    new Vector2(drawPosition.X, -drawPosition.Y),
                    color * (color.A / 255.0f),
                    prefab.Sprites[spriteIndex].Origin, drawRotation,
                    drawSize, SpriteEffects.None, prefab.Sprites[spriteIndex].Depth);
            }
        }
    }
}
